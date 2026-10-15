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

    /// <summary>Formats Chromium plays natively. Anything else is reported as needing conversion.</summary>
    public string[] VideoExtensions { get; set; } = [".mp4", ".m4v", ".webm", ".mov"];

    /// <summary>How often to look for new episodes and show folders. 0 turns it off.</summary>
    public int RescanMinutes { get; set; } = 10;

    /// <summary>
    /// Run after GOODBYE when <see cref="TvSettings.PowerOffAtZeroVolume"/> is on,
    /// e.g. ["systemctl", "poweroff"]. Empty does nothing.
    /// </summary>
    public string[] PowerOffCommand { get; set; } = [];

    public TvSettings Tv { get; set; } = new();

    public PictureOptions Picture { get; set; } = new();

    public DemoOptions Demo { get; set; } = new();
}

/// <summary>
/// A public demo site rather than a TV at home: every visitor gets a television
/// of their own, nothing is saved, and the box never powers off or rescans on request.
/// </summary>
public sealed class DemoOptions
{
    public bool Enabled { get; set; }

    /// <summary>Televisions on at once; past this the one left unwatched longest is switched off.</summary>
    public int MaxTvs { get; set; } = 100;

    /// <summary>How long a television nobody is watching stays on, so a reload comes back to it.</summary>
    public double IdleMinutes { get; set; } = 15;
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
    public double Curvature { get; set; } = 0.12;

    public double CornerRadius { get; set; } = 0.065;

    /// <summary>Edge darkening, 0 to 1.</summary>
    public double Vignette { get; set; } = 0.25;

    public bool Scanlines { get; set; } = true;

    public double ScanlineIntensity { get; set; } = 0.12;
}

/// <summary>The green on-screen readouts.</summary>
public sealed class OsdOptions
{
    public string Color { get; set; } = "#4DFF5A";

    public bool Glow { get; set; } = true;
}
