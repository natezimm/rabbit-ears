using RabbitEars.Core;

namespace RabbitEars.Api;

/// <summary>Everything configurable, bound from the "Box" section of appsettings.json.</summary>
public sealed class BoxOptions
{
    /// <summary>One folder per show inside here; the folder name is the channel name.</summary>
    public string MediaRoot { get; set; } = "/srv/rabbitears/media";

    /// <summary>Where the small state file lives so a reboot comes back to the same channel.</summary>
    public string StateDir { get; set; } = "/var/lib/rabbitears";

    public int FirstChannelNumber { get; set; } = 2;

    /// <summary>
    /// Formats Chromium plays natively. With the browser as the TV, anything else
    /// is reported as needing conversion; mpv plays these and the common rip formats.
    /// </summary>
    public string[] VideoExtensions { get; set; } = [".mp4", ".m4v", ".webm", ".mov"];

    /// <summary>How often to look for new episodes and show folders. 0 turns it off.</summary>
    public int RescanMinutes { get; set; } = 10;

    /// <summary>
    /// The ffprobe program, used to learn each episode's length so channels can
    /// keep running while nobody watches them. Empty turns probing off.
    /// </summary>
    public string Ffprobe { get; set; } = "ffprobe";

    /// <summary>
    /// Run after GOODBYE when <see cref="TvSettings.PowerOffAtZeroVolume"/> is on,
    /// e.g. ["systemctl", "poweroff"]. Empty does nothing.
    /// </summary>
    public string[] PowerOffCommand { get; set; } = [];

    public TvSettings Tv { get; set; } = new();

    public PictureOptions Picture { get; set; } = new();

    public PlayerOptions Player { get; set; } = new();

    public DemoOptions Demo { get; set; } = new();

    public RateLimitOptions RateLimit { get; set; } = new();

    /// <summary>
    /// True when mpv draws the picture on the box. The public demo has no box
    /// screen, so it is always the browser there.
    /// </summary>
    public bool UsesMpv => !Demo.Enabled && Player.Kind == PlayerOptions.Mpv;
}

/// <summary>What draws the TV on the box's own screen.</summary>
public sealed class PlayerOptions
{
    public const string Mpv = "mpv";
    public const string Browser = "browser";

    /// <summary>
    /// "mpv" plays the original files, whatever their format. "browser" is the
    /// <c>/tv</c> page in Chromium, which only plays MP4 and WebM.
    /// </summary>
    public string Kind { get; set; } = Mpv;

    /// <summary>The control socket mpv opens and the box connects to.</summary>
    public string MpvSocket { get; set; } = "/run/rabbitears-tv/mpv.sock";
}

/// <summary>What the kiosk launcher asks to learn which player to start.</summary>
public sealed record PlayerInfo(string Kind, string Socket);

/// <summary>
/// A public demo site rather than a TV at home: every visitor gets a television
/// of their own, nothing is saved, and the box never powers off or rescans on request.
/// </summary>
public sealed class DemoOptions
{
    public bool Enabled { get; set; }

    /// <summary>Require approved v2 metadata for mounted public media; generated clips do not need it.</summary>
    public bool RequireApprovedMedia { get; set; }

    /// <summary>Live stations with known durations share a UTC playlist across visitor rooms.</summary>
    public bool SynchronizedBroadcast { get; set; } = true;

    /// <summary>Televisions on at once; past this the one left unwatched longest is switched off.</summary>
    public int MaxTvs { get; set; } = 100;

    /// <summary>How long a television nobody is watching stays on, so a reload comes back to it.</summary>
    public double IdleMinutes { get; set; } = 15;
}

/// <summary>
/// How many requests each client address may make. The defaults leave room for
/// several phones and TV pages at home; the public demo sets tighter ones.
/// </summary>
public sealed class RateLimitOptions
{
    /// <summary>Allowance regained each second. 0 turns limiting off.</summary>
    public double RequestsPerSecond { get; set; } = 50;

    /// <summary>Requests a client may make at once before it has to wait.</summary>
    public int Burst { get; set; } = 500;

    /// <summary>
    /// Count clients by the X-Forwarded-For header rather than the connection.
    /// Only for a box that can be reached solely through a reverse proxy which
    /// sets that header itself, like the public demo behind nginx.
    /// </summary>
    public bool TrustForwardedFor { get; set; }

    public bool Enabled => RequestsPerSecond > 0;
}

/// <summary>What the web app asks to learn whether it is on a public demo site.</summary>
public sealed record DemoInfo(bool Enabled);

/// <summary>How the screen looks and cuts between channels. Sent to the TV page as-is.</summary>
public sealed class PictureOptions
{
    /// <summary>Channel-change effect: "none", "static" (analog snow) or "glitch".</summary>
    public string Transition { get; set; } = "none";

    public double TransitionSeconds { get; set; } = 0.4;

    /// <summary>
    /// With no transition, keep the old show on screen while the next channel
    /// loads so the picture never freezes. After this long, cut to snow instead.
    /// </summary>
    public double BridgeSeconds { get; set; } = 0.8;

    /// <summary>How long the channel banner lingers.</summary>
    public double BannerSeconds { get; set; } = 4;

    /// <summary>How long the volume bar lingers.</summary>
    public double OsdSeconds { get; set; } = 2;

    public CrtOptions Crt { get; set; } = new();

    public OsdOptions Osd { get; set; } = new();
}

/// <summary>The tube-TV picture effect: curved glass, rounded corners, vignette and scanlines.</summary>
public sealed class CrtOptions
{
    public bool Enabled { get; set; } = true;

    /// <summary>0 is flat; about 0.2 is strongly bulged.</summary>
    public double Curvature { get; set; } = 0.15;

    public double CornerRadius { get; set; } = 0.05;

    /// <summary>Edge darkening, 0 to 1.</summary>
    public double Vignette { get; set; } = 0.3;

    public bool Scanlines { get; set; } = true;

    public double ScanlineIntensity { get; set; } = 0.1;
}

/// <summary>The green on-screen readouts.</summary>
public sealed class OsdOptions
{
    public string Color { get; set; } = "#3DFF6E";

    public bool Glow { get; set; } = true;
}
