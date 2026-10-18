namespace RabbitEars.Core;

/// <summary>One video file on a channel. <see cref="Id"/> is stable for a given path.</summary>
public sealed record Episode(string Id, string Title);

/// <summary>A show folder: the folder name is the channel name.</summary>
public sealed record ChannelDefinition(
    int Number, string Name, IReadOnlyList<Episode> Episodes,
    string? Key = null, TuneInMode? TuneIn = null);

public enum TuneInMode
{
    /// <summary>
    /// Channels keep broadcasting while you watch something else, like real TV:
    /// come back after five minutes and the show is five minutes further on,
    /// or onto the next episode.
    /// </summary>
    Live,

    /// <summary>Come back to a channel and it picks up exactly where you left it.</summary>
    Resume,

    /// <summary>Every flip starts a fresh shuffled episode.</summary>
    Random,
}

public sealed class TvSettings
{
    public TuneInMode TuneIn { get; set; } = TuneInMode.Live;

    /// <summary>
    /// A newly drawn episode starts a random number of seconds in, between these
    /// two, so it feels like you tuned in to something already on (and skips the
    /// black leader).
    /// </summary>
    public double StartOffsetMin { get; set; } = 6;
    public double StartOffsetMax { get; set; } = 10;

    /// <summary>Channel the box powers on to the very first time.</summary>
    public int StartChannel { get; set; } = 2;

    public int InitialVolume { get; set; } = 60;
    public int VolumeStep { get; set; } = 5;

    /// <summary>Volume-down once more at 0 says GOODBYE and shuts the box down.</summary>
    public bool PowerOffAtZeroVolume { get; set; }

    /// <summary>How long typed channel digits wait for another digit before tuning.</summary>
    public double DigitEntrySeconds { get; set; } = 2;
}

/// <summary>Why the TV is being told to play something; the screen picks its cut accordingly.</summary>
public static class PlayReason
{
    /// <summary>Channel change: gets the channel-change effect.</summary>
    public const string Tune = "tune";

    /// <summary>The episode ended and the next one rolls on.</summary>
    public const string Advance = "advance";

    /// <summary>Power-on or waking from standby.</summary>
    public const string Wake = "wake";
}

public sealed record ChannelInfo(int Number, string Name);

/// <param name="Token">Unique per play request; the screen echoes it back in reports.</param>
/// <param name="Start">Seconds into the episode to start at.</param>
public sealed record NowPlaying(string Token, string EpisodeId, string Title, double Start, string Reason);

/// <param name="Seconds">How long to show it; 0 means until replaced.</param>
public sealed record OsdMessage(long Seq, string Text, double Seconds);

public sealed record ChannelListing(int Number, string Name, int Episodes, string? Title);

/// <summary>
/// Everything a screen or remote needs to draw itself. The <c>*Seq</c> counters
/// tick whenever that piece of on-screen display should flash, so a client only
/// has to notice the number changed.
/// </summary>
/// <param name="Boot">Changes when the box restarts, so clients know the counters started over.</param>
public sealed record TvSnapshot(
    string Boot,
    long Revision,
    bool Standby,
    bool PoweredOff,
    ChannelInfo? Channel,
    NowPlaying? Playing,
    int Volume,
    bool Muted,
    long BannerSeq,
    long VolumeSeq,
    OsdMessage? Message,
    IReadOnlyList<ChannelListing> Channels);

/// <param name="At">When the channel was at <paramref name="Position"/>, so it can carry on from there.</param>
public sealed record SavedPosition(string EpisodeId, double Position, DateTimeOffset? At = null);

/// <summary>
/// The little bit of state that survives a reboot. Positions are keyed by
/// channel folder key (the name in older saves); durations are episode lengths
/// in seconds, keyed by episode id.
/// </summary>
public sealed record SavedState(
    string? Channel,
    string? LastChannel,
    int Volume,
    bool Muted,
    Dictionary<string, SavedPosition> Positions,
    Dictionary<string, double>? Durations = null);
