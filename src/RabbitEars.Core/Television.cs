namespace RabbitEars.Core;

/// <summary>
/// The television itself: turns remote-control presses into TV behaviour.
/// Channel up/down wrap around the lineup, typed digits tune directly, each
/// channel rolls through its episodes on an endless shuffle, and standby blanks
/// the screen. It never touches the disk, the network or the video player, so
/// the whole thing can be single-stepped in tests.
/// </summary>
/// <remarks>Not thread-safe; the caller serialises access.</remarks>
public sealed class Television
{
    private const int RememberedRequests = 8;
    private const int MaxConsecutiveFailures = 5;
    private const int MaxDigits = 3;

    // Enough to catch a channel up through a week away from it.
    private const int MaxCatchUpEpisodes = 1000;

    private readonly TvSettings _settings;
    private readonly Random _random;
    private readonly TimeProvider _clock;

    // Tokens must not collide with ones a screen still holds from before a restart.
    private readonly string _bootId = Guid.NewGuid().ToString("N")[..8];
    private readonly Queue<PlayRecord> _recentRequests = new();

    // Episode lengths in seconds, as the screen reports them while playing.
    private readonly Dictionary<string, double> _durations = [];

    private List<Tuner> _tuners = [];
    private int _index;
    private string? _lastChannel;
    private NowPlaying? _playing;
    private OsdMessage? _message;
    private long _requestCounter;
    private long _messageSeq;
    private int _failures;
    private string _digits = "";
    private DateTimeOffset _digitDeadline;

    public Television(
        TvSettings settings,
        IReadOnlyList<ChannelDefinition> channels,
        SavedState? saved = null,
        Random? random = null,
        TimeProvider? clock = null)
    {
        _settings = settings;
        _random = random ?? Random.Shared;
        _clock = clock ?? TimeProvider.System;

        Volume = Math.Clamp(saved?.Volume ?? settings.InitialVolume, 0, 100);
        Muted = saved?.Muted ?? false;
        _lastChannel = saved?.LastChannel;
        foreach (var (id, seconds) in saved?.Durations ?? [])
        {
            if (double.IsFinite(seconds) && seconds > 0) _durations[id] = seconds;
        }

        _tuners = [.. channels.OrderBy(c => c.Number).Select(c => new Tuner(c, _random))];
        foreach (var tuner in _tuners)
        {
            if (saved?.Positions.GetValueOrDefault(tuner.Name) is { } position)
                tuner.Restore(position, _clock.GetUtcNow());
        }

        var start = _tuners.FindIndex(t => t.Name == saved?.Channel);
        if (start < 0) start = _tuners.FindIndex(t => t.Number == settings.StartChannel);
        _index = Math.Max(start, 0);

        Tune(PlayReason.Wake);
    }

    public long Revision { get; private set; }
    public int Volume { get; private set; }
    public bool Muted { get; private set; }
    public bool Standby { get; private set; }

    /// <summary>Set once the box has said GOODBYE; the host should shut the machine down.</summary>
    public bool PoweredOff { get; private set; }
    public long BannerSeq { get; private set; }
    public long VolumeSeq { get; private set; }

    private Tuner Current => _tuners[_index];
    private bool Accepting => !Standby && !PoweredOff;

    // -- channels -------------------------------------------------------------

    public void ChannelUp() => Step(+1);

    public void ChannelDown() => Step(-1);

    /// <summary>Jump back to the channel that was on before this one.</summary>
    public void LastChannel()
    {
        if (!Accepting || _lastChannel is null) return;
        var index = _tuners.FindIndex(t => t.Name == _lastChannel);
        if (index >= 0 && index != _index) SwitchTo(index);
    }

    public void GoToChannel(int number)
    {
        if (!Accepting) return;
        ClearDigits();
        var index = _tuners.FindIndex(t => t.Number == number);
        if (index < 0)
        {
            ShowMessage($"CH {number:00}  -  NO CHANNEL", 2);
            Touch();
            return;
        }
        SwitchTo(index);
    }

    /// <summary>Flash the channel banner again without changing anything.</summary>
    public void ShowInfo()
    {
        if (!Accepting || _tuners.Count == 0) return;
        BannerSeq++;
        Touch();
    }

    // -- direct channel entry ---------------------------------------------------

    public void PushDigit(int digit)
    {
        if (!Accepting || digit is < 0 or > 9) return;
        _digits += digit;
        if (_digits.Length > MaxDigits) _digits = _digits[^MaxDigits..];
        _digitDeadline = _clock.GetUtcNow().AddSeconds(_settings.DigitEntrySeconds);
        ShowMessage($"CH {_digits}_", _settings.DigitEntrySeconds);
        Touch();
    }

    /// <summary>OK / Enter: tune to the typed digits, or show the banner if none were typed.</summary>
    public void Confirm()
    {
        if (_digits.Length == 0)
        {
            ShowInfo();
            return;
        }
        var number = int.Parse(_digits);
        GoToChannel(number);
    }

    /// <summary>
    /// Call a few times a second. Tunes to typed digits once nobody has pressed
    /// another one for a moment. Returns true if anything changed.
    /// </summary>
    public bool Tick()
    {
        if (_digits.Length == 0 || _clock.GetUtcNow() < _digitDeadline) return false;
        Confirm();
        return true;
    }

    // -- volume ---------------------------------------------------------------

    public void VolumeUp()
    {
        if (!Accepting) return;
        SetVolume(Volume + _settings.VolumeStep);
    }

    public void VolumeDown()
    {
        if (!Accepting) return;
        if (_settings.PowerOffAtZeroVolume && !Muted && Volume <= 0)
        {
            PowerOff();
            return;
        }
        SetVolume(Volume - _settings.VolumeStep);
    }

    public void ToggleMute()
    {
        if (!Accepting) return;
        Muted = !Muted;
        VolumeSeq++;
        Touch();
    }

    // -- power ----------------------------------------------------------------

    /// <summary>Standby blanks the screen; pressing power again wakes straight back to a channel.</summary>
    public void TogglePower()
    {
        if (PoweredOff) return;
        Standby = !Standby;
        ClearDigits();
        _message = null;
        if (Standby) _playing = null;
        else Tune(PlayReason.Wake);
        Touch();
    }

    // -- reports from the screen ------------------------------------------------

    /// <summary>The screen finished an episode; roll the next one. Stale tokens are ignored.</summary>
    public bool EpisodeEnded(string token)
    {
        if (!Accepting || _playing?.Token != token) return false;
        _failures = 0;
        Advance();
        Touch();
        return true;
    }

    /// <summary>
    /// The screen could not play an episode. Skip to the next, but give up with
    /// NO SIGNAL rather than spin forever through a channel of bad files.
    /// </summary>
    public bool EpisodeFailed(string token)
    {
        if (!Accepting || _playing?.Token != token) return false;
        var tuner = Current;
        if (++_failures >= Math.Min(MaxConsecutiveFailures, tuner.Episodes.Count))
        {
            _playing = null;
            tuner.Forget();
            ShowMessage($"CH {tuner.Number:00}  {tuner.Name}  -  NO SIGNAL", 0);
        }
        else
        {
            Advance();
        }
        Touch();
        return true;
    }

    /// <summary>
    /// Where the screen has got to, and how long the episode is if it knows.
    /// Reports for a channel that was just left still land on that channel,
    /// which is how tuning back knows where you were.
    /// </summary>
    public void ReportPosition(string token, double seconds, double? duration = null)
    {
        if (!double.IsFinite(seconds) || seconds < 0) return;
        var request = _recentRequests.FirstOrDefault(r => r.Token == token);
        if (request is null) return;

        if (duration is { } length && double.IsFinite(length) && length > 0)
            _durations[request.EpisodeId] = length;

        var tuner = _tuners.Find(t => t.Name == request.Channel);
        if (tuner?.Showing?.Id != request.EpisodeId) return;
        tuner.MoveTo(seconds, _clock.GetUtcNow());
        if (token == _playing?.Token) _failures = 0;
    }

    // -- library changes --------------------------------------------------------

    /// <summary>
    /// Swap in a freshly scanned lineup. Channels whose episodes did not change
    /// keep their shuffle and position; whatever is on screen keeps playing
    /// unless its file is gone.
    /// </summary>
    public void SetLineup(IReadOnlyList<ChannelDefinition> channels)
    {
        var watching = _tuners.Count > 0 ? Current.Name : null;
        var previous = _tuners.ToDictionary(t => t.Name);

        _tuners = [.. channels.OrderBy(c => c.Number).Select(c =>
            previous.TryGetValue(c.Name, out var old) ? old.Updated(c, _random) : new Tuner(c, _random))];

        var index = _tuners.FindIndex(t => t.Name == watching);
        _index = Math.Max(index, 0);

        var stillOnAir = index >= 0 && _playing is not null && Current.Find(_playing.EpisodeId) is not null;
        if (Accepting && !stillOnAir) Tune(PlayReason.Tune);
        Touch();
    }

    // -- views ------------------------------------------------------------------

    public TvSnapshot Snapshot() => new(
        _bootId,
        Revision,
        Standby,
        PoweredOff,
        _tuners.Count > 0 ? new ChannelInfo(Current.Number, Current.Name) : null,
        _playing,
        Volume,
        Muted,
        BannerSeq,
        VolumeSeq,
        _message,
        [.. _tuners.Select(t => new ChannelListing(t.Number, t.Name, t.Episodes.Count, t.Showing?.Title))]);

    public SavedState Save()
    {
        var onAir = _tuners.SelectMany(t => t.Episodes).Select(e => e.Id).ToHashSet();
        return new(
            _tuners.Count > 0 ? Current.Name : null,
            _lastChannel,
            Volume,
            Muted,
            _tuners.Where(t => t.Showing is not null)
                .ToDictionary(t => t.Name, t => new SavedPosition(t.Showing!.Id, t.Position, t.PositionAt)),
            _durations.Where(d => onAir.Contains(d.Key)).ToDictionary());
    }

    // -- internals --------------------------------------------------------------

    private void Step(int delta)
    {
        if (!Accepting || _tuners.Count == 0) return;
        SwitchTo((_index + delta + _tuners.Count) % _tuners.Count);
    }

    private void SwitchTo(int index)
    {
        ClearDigits();
        if (index == _index)
        {
            BannerSeq++;
        }
        else
        {
            _lastChannel = Current.Name;
            _index = index;
            Tune(PlayReason.Tune);
        }
        Touch();
    }

    private void Tune(string reason)
    {
        _failures = 0;
        if (_tuners.Count == 0)
        {
            _playing = null;
            ShowMessage("NO CHANNELS", 0);
            return;
        }

        var tuner = Current;
        _message = null;
        BannerSeq++;

        if (tuner.Episodes.Count == 0)
        {
            _playing = null;
            ShowMessage($"CH {tuner.Number:00}  {tuner.Name}  -  NO SIGNAL", 6);
            return;
        }

        if (tuner.Showing is null || _settings.TuneIn == TuneInMode.Random) Draw(tuner);
        else if (_settings.TuneIn == TuneInMode.Live) CatchUp(tuner);
        Play(tuner, reason);
    }

    /// <summary>
    /// The channel kept broadcasting while nobody watched it: move it on by the
    /// time that has passed, rolling through as many episodes as fitted in. An
    /// episode whose length is not known yet is joined at the overshoot, and if
    /// that is past its end the screen ends it and the next one rolls.
    /// </summary>
    private void CatchUp(Tuner tuner)
    {
        var now = _clock.GetUtcNow();
        var position = tuner.Position + Math.Max(0, (now - tuner.PositionAt).TotalSeconds);
        for (var rolled = 0; _durations.TryGetValue(tuner.Showing!.Id, out var length) && position >= length; rolled++)
        {
            if (rolled == MaxCatchUpEpisodes)
            {
                Draw(tuner);
                return;
            }
            var overshoot = position - length;
            Draw(tuner);
            position = tuner.Position + overshoot;
        }
        tuner.MoveTo(position, now);
    }

    private void Advance()
    {
        var tuner = Current;
        Draw(tuner);
        Play(tuner, PlayReason.Advance);
    }

    private void Draw(Tuner tuner) => tuner.Draw(RandomStartOffset(), _clock.GetUtcNow());

    private void Play(Tuner tuner, string reason)
    {
        var episode = tuner.Showing!;
        var token = $"{_bootId}-{++_requestCounter}";
        _playing = new NowPlaying(token, episode.Id, episode.Title, tuner.Position, reason);

        _recentRequests.Enqueue(new PlayRecord(token, tuner.Name, episode.Id));
        while (_recentRequests.Count > RememberedRequests) _recentRequests.Dequeue();
    }

    private void SetVolume(int value)
    {
        Volume = Math.Clamp(value, 0, 100);
        Muted = false;
        VolumeSeq++;
        Touch();
    }

    private void PowerOff()
    {
        PoweredOff = true;
        _playing = null;
        ClearDigits();
        ShowMessage("GOODBYE", 0);
        Touch();
    }

    private double RandomStartOffset()
    {
        var min = Math.Max(0, _settings.StartOffsetMin);
        var max = Math.Max(min, _settings.StartOffsetMax);
        return min + _random.NextDouble() * (max - min);
    }

    private void ShowMessage(string text, double seconds) =>
        _message = new OsdMessage(++_messageSeq, text, seconds);

    private void ClearDigits()
    {
        if (_digits.Length == 0) return;
        _digits = "";
        _message = null;
    }

    private void Touch() => Revision++;

    private sealed record PlayRecord(string Token, string Channel, string EpisodeId);

    /// <summary>
    /// One channel's runtime state: its shuffle, what it was last showing, and
    /// how far in it was at <see cref="PositionAt"/>.
    /// </summary>
    private sealed class Tuner(ChannelDefinition definition, Random random)
    {
        private ShuffleDeck<Episode> _deck = new(definition.Episodes, random);

        public int Number { get; private set; } = definition.Number;
        public string Name { get; } = definition.Name;
        public IReadOnlyList<Episode> Episodes { get; private set; } = definition.Episodes;
        public Episode? Showing { get; private set; }
        public double Position { get; private set; }
        public DateTimeOffset PositionAt { get; private set; }

        public Episode? Find(string episodeId) => Episodes.FirstOrDefault(e => e.Id == episodeId);

        public void Draw(double startAt, DateTimeOffset now)
        {
            Showing = _deck.Next();
            MoveTo(startAt, now);
        }

        public void MoveTo(double position, DateTimeOffset at)
        {
            Position = position;
            PositionAt = at;
        }

        public void Forget()
        {
            Showing = null;
            Position = 0;
        }

        /// <summary>A saved position from before positions carried a time counts as just now.</summary>
        public void Restore(SavedPosition saved, DateTimeOffset now)
        {
            Showing = Find(saved.EpisodeId);
            MoveTo(Showing is null ? 0 : Math.Max(0, saved.Position), saved.At ?? now);
        }

        /// <summary>Take on a rescanned definition, keeping the shuffle if the episodes are the same.</summary>
        public Tuner Updated(ChannelDefinition next, Random rng)
        {
            Number = next.Number;
            if (Episodes.Select(e => e.Id).SequenceEqual(next.Episodes.Select(e => e.Id)))
                return this;

            Episodes = next.Episodes;
            _deck = new ShuffleDeck<Episode>(next.Episodes, rng);
            if (Showing is not null && Find(Showing.Id) is null) Forget();
            return this;
        }
    }
}
