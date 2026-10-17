using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using RabbitEars.Core;

namespace RabbitEars.Api;

/// <summary>
/// The box's own screen when mpv draws it: what the TV page does in the
/// browser, done by driving mpv. It follows snapshots (play this episode from
/// here, volume, standby, the green display) and turns what mpv reports, and
/// the remote keys mpv hears, back into presses on the <see cref="TvService"/>.
/// </summary>
/// <remarks>
/// One caller at a time: <see cref="MpvPlayer"/> feeds it from a single loop.
/// mpv numbers every file it is given (its playlist entry id), and reports
/// carry that number, so a report for something already replaced is ignored
/// just as a stale play token is.
/// </remarks>
public sealed partial class MpvScreen(BoxOptions options, TvService tv, ILogger<MpvScreen> log, TimeProvider? clock = null)
{
    private const string Bars = "bars";
    private const string Dark = "dark";
    private const int OsdOverlayId = 1;
    private const int DimensionsObserver = 1;

    private static readonly TimeSpan ReportEvery = TimeSpan.FromSeconds(2);
    /// <summary>Playback that has not moved for this many reports is treated as a dead file.</summary>
    private const int StalledReports = 6;
    /// <summary>After a restart of the box's service, how far apart "what is playing" may be and still count as the same thing.</summary>
    private const double AdoptWindowSeconds = 30;

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly PictureOptions _picture = options.Picture;

    private IMpvLink? _mpv;

    // What mpv is doing: a play token, Bars or Dark, and which of its playlist
    // entries are episodes (by token) so their reports can be routed.
    private string? _showing;
    private readonly Dictionary<long, string> _episodes = [];
    private long _currentEntry = -1;

    // Position reports, and where to pick up if mpv itself has to be restarted.
    private DateTimeOffset _nextReport;
    private double _lastPosition = -1;
    private int _stalled;
    private string? _resumeToken;
    private double _resumeAt;

    // What has already been acted on, so each snapshot only triggers what changed.
    private string? _seenBoot;
    private long _seenRevision = -1;
    private long _seenBanner = -1;
    private long _seenVolume = -1;
    private long _seenMessage = -1;
    private (int Level, bool Muted)? _sound;

    private TvSnapshot? _state;
    private ChannelInfo? _banner;
    private DateTimeOffset _bannerUntil;
    private DateTimeOffset _volumeUntil;
    private string? _message;
    private DateTimeOffset _messageUntil;
    private double _screenAspect = 16.0 / 9;
    private string? _drawnOsd;

    private DateTimeOffset Now => _clock.GetUtcNow();

    /// <summary>The settings-baked copy of the tube shader mpv is pointed at.</summary>
    public string ShaderPath => Path.Combine(options.StateDir, "crt.glsl");

    /// <summary>A newly started (or restarted) mpv: set it up and put the current channel on.</summary>
    public async Task Attach(IMpvLink mpv)
    {
        _mpv = mpv;
        _showing = null;
        _episodes.Clear();
        _currentEntry = -1;
        _seenBoot = null;
        _seenRevision = -1;
        _seenBanner = -1;
        _seenVolume = -1;
        _seenMessage = -1;
        _sound = null;
        _drawnOsd = null;

        var shaders = _picture.Crt.Enabled && WriteShader() ? new[] { ShaderPath } : Array.Empty<string>();
        await Run("set_property", "glsl-shaders", shaders);
        await Run("observe_property", DimensionsObserver, "osd-dimensions");
        if (await mpv.Request(new object[] { "get_property", "osd-dimensions" }) is { } dimensions) SetAspect(dimensions);

        var snapshot = tv.Snapshot();
        await Adopt(snapshot);
        await Show(snapshot);
    }

    public void Detach() => _mpv = null;

    /// <summary>React to a new snapshot from the television.</summary>
    public async Task Show(TvSnapshot state)
    {
        if (_mpv is null) return;
        // A snapshot queued while mpv was away can arrive after a newer one.
        if (state.Boot == _seenBoot && state.Revision < _seenRevision) return;
        var firstFromThisBoot = state.Boot != _seenBoot;
        _seenBoot = state.Boot;
        _seenRevision = state.Revision;
        _state = state;

        var on = !state.Standby && !state.PoweredOff;
        var wanted = state.Playing?.Token ?? (on ? Bars : Dark);
        if (wanted != _showing) await Change(state, wanted);

        var sound = (state.Volume, state.Muted);
        if (sound != _sound)
        {
            _sound = sound;
            await Run("set_property", "volume", MpvVolume(state.Volume));
            await Run("set_property", "mute", state.Muted);
        }

        var now = Now;
        if (state.BannerSeq != _seenBanner)
        {
            _seenBanner = state.BannerSeq;
            _banner = state.Channel;
            _bannerUntil = now + TimeSpan.FromSeconds(_picture.BannerSeconds);
        }

        if (state.VolumeSeq != _seenVolume)
        {
            // A fresh connection is not somebody pressing volume.
            if (!firstFromThisBoot && _seenVolume != -1) _volumeUntil = now + TimeSpan.FromSeconds(_picture.OsdSeconds);
            _seenVolume = state.VolumeSeq;
        }

        var messageSeq = state.Message?.Seq ?? 0;
        if (messageSeq != _seenMessage)
        {
            _seenMessage = messageSeq;
            _message = state.Message?.Text;
            var seconds = state.Message?.Seconds ?? 0;
            _messageUntil = seconds > 0 ? now + TimeSpan.FromSeconds(seconds) : DateTimeOffset.MaxValue;
        }

        if (!on)
        {
            _banner = null;
            _volumeUntil = default;
        }

        await DrawOsd();
    }

    /// <summary>Something mpv said: a file started or ended, a remote key, the window resized.</summary>
    public async Task Hear(JsonElement message)
    {
        if (_mpv is null) return;
        switch (Text(message, "event"))
        {
            case "start-file" when Number(message, "playlist_entry_id") is { } entry:
                _currentEntry = entry;
                _lastPosition = -1;
                _stalled = 0;
                break;

            case "end-file" when Number(message, "playlist_entry_id") is { } entry:
                if (!_episodes.Remove(entry, out var token)) break;
                if (entry == _currentEntry) _currentEntry = -1;
                // "stop" and "redirect" are mpv being told to play something else.
                switch (Text(message, "reason"))
                {
                    case "eof":
                        await tv.Press(t => t.EpisodeEnded(token));
                        break;
                    case "error":
                        log.LogWarning("mpv could not play {Token}: {Error}", token, Text(message, "file_error"));
                        await tv.Press(t => t.EpisodeFailed(token));
                        break;
                }
                break;

            case "client-message":
                if (message.TryGetProperty("args", out var args) && args.ValueKind == JsonValueKind.Array)
                {
                    await Key([.. args.EnumerateArray().Select(a => a.GetString() ?? "")]);
                }
                break;

            case "property-change" when Text(message, "name") == "osd-dimensions":
                if (message.TryGetProperty("data", out var dimensions)) SetAspect(dimensions);
                await DrawOsd();
                break;
        }
    }

    /// <summary>The clock moved: time out parts of the display, and say how far into the episode mpv is.</summary>
    public async Task Tick()
    {
        if (_mpv is null) return;
        await DrawOsd();

        var now = Now;
        if (now < _nextReport) return;
        _nextReport = now + ReportEvery;
        if (await Position() is not ({ } token, var seconds)) return;

        if (seconds == _lastPosition)
        {
            if (++_stalled >= StalledReports)
            {
                _stalled = 0;
                log.LogWarning("Playback of {Token} has not moved for a while; skipping it.", token);
                await tv.Press(t => t.EpisodeFailed(token));
            }
            return;
        }
        _lastPosition = seconds;
        _stalled = 0;
        Report(token, seconds);
    }

    /// <summary>
    /// The names <c>input.conf</c> sends as <c>script-message ears NAME</c>, and
    /// the press each one makes. The same keys the TV page listens for.
    /// </summary>
    public static IReadOnlyDictionary<string, Action<Television>> Keys { get; } =
        new Dictionary<string, Action<Television>>
        {
            ["channel-up"] = t => t.ChannelUp(),
            ["channel-down"] = t => t.ChannelDown(),
            ["volume-up"] = t => t.VolumeUp(),
            ["volume-down"] = t => t.VolumeDown(),
            ["mute"] = t => t.ToggleMute(),
            ["power"] = t => t.TogglePower(),
            ["info"] = t => t.ShowInfo(),
            ["last-channel"] = t => t.LastChannel(),
            ["confirm"] = t => t.Confirm(),
        };

    private Task Key(string[] args)
    {
        if (args is not ["ears", var name, ..]) return Task.CompletedTask;
        if (name == "digit" && args.Length > 2 && int.TryParse(args[2], CultureInfo.InvariantCulture, out var digit))
        {
            return tv.Press(t => t.PushDigit(digit));
        }
        return Keys.TryGetValue(name, out var press) ? tv.Press(press) : Task.CompletedTask;
    }

    /// <summary>Put something else on: an episode, the colour bars, or nothing.</summary>
    private async Task Change(TvSnapshot state, string wanted)
    {
        var leavingEpisode = _showing is not null and not Bars and not Dark;
        // Say exactly where the old channel was left, so coming back resumes there.
        if (leavingEpisode && await Position() is ({ } token, var seconds)) Report(token, seconds);

        _showing = wanted;
        _episodes.Clear();
        _currentEntry = -1;
        _lastPosition = -1;
        _stalled = 0;
        _nextReport = Now + ReportEvery;

        if (state.Playing is { } playing)
        {
            await Play(playing, changingChannel: leavingEpisode && playing.Reason == PlayReason.Tune);
        }
        else if (wanted == Bars)
        {
            await Load(BarsSource, "replace");
        }
        else
        {
            await Run("stop");
        }
    }

    private async Task Play(NowPlaying playing, bool changingChannel)
    {
        if (tv.FindMedia(playing.EpisodeId) is not { } path)
        {
            await tv.Press(t => t.EpisodeFailed(playing.Token));
            return;
        }

        var start = playing.Start;
        if (_resumeToken == playing.Token) start = Math.Max(start, _resumeAt);

        // mpv swaps files in one window, so a channel change goes through a
        // burst of snow (or glitch) while the next file opens behind it.
        var flags = "replace";
        if (changingChannel && TransitionSource() is { } effect)
        {
            await Load(effect, flags);
            flags = "append";
        }

        var options = new Dictionary<string, string>
        {
            ["start"] = start.ToString("0.###", CultureInfo.InvariantCulture),
        };
        if (await Load(path, flags, options) is { } entry) _episodes[entry] = playing.Token;
    }

    /// <summary>
    /// After the box's service restarts it asks again for what was already
    /// playing. If mpv is still showing that, carry on rather than reload it.
    /// </summary>
    private async Task Adopt(TvSnapshot state)
    {
        if (state.Playing is not { Reason: PlayReason.Wake } playing) return;
        if (tv.FindMedia(playing.EpisodeId) is not { } path) return;
        if (await Get("path") is not { ValueKind: JsonValueKind.String } playingPath || playingPath.GetString() != path) return;
        if (await Get("time-pos") is not { ValueKind: JsonValueKind.Number } position) return;
        if (Math.Abs(position.GetDouble() - playing.Start) > AdoptWindowSeconds) return;
        if (await Get("playlist-playing-pos") is not { ValueKind: JsonValueKind.Number } index) return;
        if (await Get($"playlist/{index.GetInt32()}/id") is not { ValueKind: JsonValueKind.Number } entry) return;

        _showing = playing.Token;
        _currentEntry = entry.GetInt64();
        _episodes[_currentEntry] = playing.Token;
    }

    /// <summary>Which episode mpv is on and how far into it, if it is playing one.</summary>
    private async Task<(string? Token, double Seconds)> Position()
    {
        if (!_episodes.TryGetValue(_currentEntry, out var token)) return (null, 0);
        var entry = _currentEntry;
        if (await Get("time-pos") is not { ValueKind: JsonValueKind.Number } position) return (null, 0);
        // The file may have changed while the answer was on its way.
        return entry == _currentEntry ? (token, position.GetDouble()) : (null, 0);
    }

    private void Report(string token, double seconds)
    {
        _resumeToken = token;
        _resumeAt = seconds;
        tv.ReportPosition(token, seconds);
    }

    private async Task DrawOsd()
    {
        if (_state is not { } state) return;
        var now = Now;
        if (_message is not null && now >= _messageUntil) _message = null;
        if (_banner is not null && now >= _bannerUntil) _banner = null;

        var view = new OsdView(
            _banner,
            _message,
            state.Standby,
            now < _volumeUntil,
            state.Volume,
            state.Muted);
        var overlay = MpvOsd.Draw(view, _screenAspect, _picture.Osd);
        var key = $"{overlay.Width}x{overlay.Height}\n{overlay.Ass}";
        if (key == _drawnOsd) return;
        _drawnOsd = key;

        if (overlay.Ass.Length == 0)
        {
            await Run("osd-overlay", OsdOverlayId, "none", "");
        }
        else
        {
            await Run("osd-overlay", OsdOverlayId, "ass-events", overlay.Ass, overlay.Width, overlay.Height);
        }
    }

    private void SetAspect(JsonElement dimensions)
    {
        if (dimensions.ValueKind != JsonValueKind.Object) return;
        if (Number(dimensions, "w") is { } width and > 0 && Number(dimensions, "h") is { } height and > 0)
        {
            _screenAspect = (double)width / height;
        }
    }

    /// <summary>
    /// The TV page squares the level to get loudness; mpv cubes its volume.
    /// This puts each step of the remote at the same loudness on both.
    /// </summary>
    public static double MpvVolume(int level) =>
        Math.Round(100 * Math.Pow(Math.Clamp(level, 0, 100) / 100.0, 2.0 / 3), 2);

    // mpv's built-in generators, so snow and bars need no files. The snow is the
    // shader's: 360x270 grain, a new field every frame, with a faint rolling band.
    private const string BarsSource = "av://lavfi:smptebars=s=640x480:r=30";

    private string? TransitionSource()
    {
        var seconds = Math.Max(0.05, _picture.TransitionSeconds).ToString("0.###", CultureInfo.InvariantCulture);
        return _picture.Transition switch
        {
            "static" => $"av://lavfi:nullsrc=s=360x270:r=30:d={seconds},"
                + "geq=lum='clip(random(1)*230+18*sin((Y/H+T*0.6)*14),0,255)':cb=128:cr=128,"
                + "scale=1440:1080:flags=neighbor",
            "glitch" => $"av://lavfi:nullsrc=s=96x54:r=25:d={seconds},format=gbrp,"
                + "geq=r='random(1)*255':g='random(2)*255':b='random(3)*255',"
                + "scale=1440:810:flags=neighbor",
            _ => null,
        };
    }

    /// <summary>
    /// Writes the tube shader with this box's settings baked in. mpv runs as
    /// the desktop user and reads it from the state folder.
    /// </summary>
    private bool WriteShader()
    {
        var template = Path.Combine(AppContext.BaseDirectory, "mpv", "crt.glsl");
        try
        {
            Directory.CreateDirectory(options.StateDir);
            File.WriteAllText(ShaderPath, BakeShader(File.ReadAllText(template), _picture.Crt));
            return true;
        }
        catch (IOException ex)
        {
            log.LogWarning(ex, "Could not write the tube shader; the picture will be flat.");
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            log.LogWarning(ex, "Could not write the tube shader; the picture will be flat.");
            return false;
        }
    }

    /// <summary>Sets the shader's <c>#define</c>s from the box's picture settings.</summary>
    public static string BakeShader(string template, CrtOptions crt)
    {
        var values = new Dictionary<string, double>
        {
            ["CURVATURE"] = crt.Curvature,
            ["CORNER_RADIUS"] = crt.CornerRadius,
            ["VIGNETTE"] = crt.Vignette,
            ["SCANLINES"] = crt.Scanlines ? crt.ScanlineIntensity : 0,
        };
        return Define().Replace(template, match =>
            values.TryGetValue(match.Groups[1].Value, out var value)
                ? $"#define {match.Groups[1].Value} {value.ToString("0.0#####", CultureInfo.InvariantCulture)}"
                : match.Value);
    }

    [GeneratedRegex(@"^#define (\w+) .*$", RegexOptions.Multiline)]
    private static partial Regex Define();

    private async Task<long?> Load(string url, string flags, Dictionary<string, string>? fileOptions = null)
    {
        // Named arguments, because mpv 0.38 added a positional one before the options.
        var command = new Dictionary<string, object> { ["name"] = "loadfile", ["url"] = url, ["flags"] = flags };
        if (fileOptions is not null) command["options"] = fileOptions;
        var answer = await _mpv!.Request(command);
        return answer is { ValueKind: JsonValueKind.Object } data && Number(data, "playlist_entry_id") is { } entry
            ? entry
            : null;
    }

    private Task<JsonElement?> Get(string property) => _mpv!.Request(new object[] { "get_property", property });

    private async Task Run(params object[] command)
    {
        if (await _mpv!.Request(command) is null) log.LogDebug("mpv refused {Command}", command[0]);
    }

    private static string? Text(JsonElement message, string name) =>
        message.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static long? Number(JsonElement message, string name) =>
        message.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)
            ? number
            : null;
}
