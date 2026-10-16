using System.Globalization;
using System.Text;
using RabbitEars.Core;

namespace RabbitEars.Api;

/// <summary>What the green on-screen display is showing at one moment.</summary>
public sealed record OsdView(
    ChannelInfo? Banner,
    string? Message,
    bool Standby,
    bool ShowVolume,
    int Volume,
    bool Muted)
{
    public static OsdView Nothing { get; } = new(null, null, false, false, 0, false);
}

/// <param name="Ass">ASS events, one per line, for mpv's <c>osd-overlay</c>. Empty when nothing shows.</param>
/// <param name="Width">The overlay's coordinate space, matched to the window's shape.</param>
public sealed record OsdOverlay(string Ass, int Width, int Height);

/// <summary>
/// Draws the TV page's green on-screen display as ASS text for mpv: the channel
/// banner, messages, STANDBY and the volume bar, in VT323, at the same places.
/// </summary>
/// <remarks>
/// Like <c>tv-screen.css</c>, the layout is a 960x720 frame centred in the
/// window, so it sits over a 4:3 picture whatever the screen's shape. Lengths
/// below are that stylesheet's, converted to frame pixels.
/// </remarks>
public static class MpvOsd
{
    private const double FrameWidth = 960;
    private const double FrameHeight = 720;
    private const int VolumeSegments = 20;

    // libass sizes VT323 by its Windows ascent plus descent (1.04 + 0.24 em),
    // where CSS sizes the em. These turn one into the other and line the
    // baselines up with the browser's line-height: 1 boxes.
    private const double AssSizePerEm = 1.28;
    private const double AssTopAboveCss = 0.24;
    private const double AssMiddleAboveCss = 0.1;

    public static OsdOverlay Draw(OsdView view, double screenAspect, OsdOptions osd)
    {
        if (!(screenAspect > 0)) screenAspect = 16.0 / 9;
        var wide = screenAspect >= FrameWidth / FrameHeight;
        var width = wide ? (int)Math.Round(FrameHeight * screenAspect) : (int)FrameWidth;
        var height = wide ? (int)FrameHeight : (int)Math.Round(FrameWidth / screenAspect);
        var page = new Page((width - FrameWidth) / 2, (height - FrameHeight) / 2, Colour(osd.Color), osd.Glow);

        // Messages share the top of the screen with the banner, so they take its place.
        if (view.Message is { } message)
        {
            page.Text(message, FrameWidth / 2, 0.06 * FrameHeight, 0.083 * FrameHeight, Align.TopCentre);
        }
        else if (view.Banner is { } channel)
        {
            var right = FrameWidth * (1 - 0.06);
            var top = 0.06 * FrameHeight;
            var numberSize = 0.122 * FrameHeight;
            page.Text($"CH {channel.Number:00}", right, top, numberSize, Align.TopRight);
            page.Text(channel.Name, right, top + numberSize + 0.022 * FrameHeight, 0.056 * FrameHeight, Align.TopRight);
        }

        if (view.Standby)
        {
            page.Text("STANDBY", FrameWidth / 2, FrameHeight / 2, 0.1 * FrameHeight, Align.Centre);
        }

        if (view.ShowVolume)
        {
            DrawVolume(page, view.Muted ? 0 : view.Volume, view.Muted);
        }

        return new OsdOverlay(page.ToString(), width, height);
    }

    /// <summary>Solid bars for the level, dots for the rest, like a classic TV volume display.</summary>
    private static void DrawVolume(Page page, int volume, bool muted)
    {
        var rowWidth = 0.769 * FrameWidth;
        var rowHeight = 0.067 * FrameHeight;
        var left = (FrameWidth - rowWidth) / 2;
        var rowTop = FrameHeight * (1 - 0.06) - rowHeight;
        var labelSize = 0.067 * FrameHeight;
        var labelTop = rowTop - 0.019 * FrameHeight - labelSize;
        page.Text(muted ? "Mute" : "Volume", left, labelTop, labelSize, Align.TopLeft);

        var slot = 0.0167 * FrameWidth;
        var dot = 0.0167 * FrameHeight;
        var lit = (int)Math.Round(Math.Clamp(volume, 0, 100) / 100.0 * VolumeSegments);
        var shapes = new StringBuilder();
        for (var index = 0; index < VolumeSegments; index++)
        {
            var x = left + index * (rowWidth - slot) / (VolumeSegments - 1);
            if (index < lit) Rectangle(shapes, x, rowTop, slot, rowHeight);
            else Circle(shapes, x + slot / 2, rowTop + rowHeight / 2, dot / 2);
        }
        page.Shapes(shapes.ToString(), glowSize: 0.012 * FrameHeight);
    }

    private static void Rectangle(StringBuilder path, double x, double y, double width, double height) =>
        path.Append(Invariant($"m {x} {y} l {x + width} {y} {x + width} {y + height} {x} {y + height} "));

    private static void Circle(StringBuilder path, double cx, double cy, double r)
    {
        // Four Bézier quarters; 0.5523 puts the control points where a circle needs them.
        var k = r * 0.5523;
        path.Append(Invariant($"m {cx} {cy - r} "));
        path.Append(Invariant($"b {cx + k} {cy - r} {cx + r} {cy - k} {cx + r} {cy} "));
        path.Append(Invariant($"b {cx + r} {cy + k} {cx + k} {cy + r} {cx} {cy + r} "));
        path.Append(Invariant($"b {cx - k} {cy + r} {cx - r} {cy + k} {cx - r} {cy} "));
        path.Append(Invariant($"b {cx - r} {cy - k} {cx - k} {cy - r} {cx} {cy - r} "));
    }

    /// <summary>"#4DFF5A" as ASS writes colours: blue, green, red.</summary>
    private static string Colour(string css)
    {
        var hex = css.TrimStart('#');
        if (hex.Length != 6 || !int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _))
        {
            hex = "4DFF5A";
        }
        return $"&H{hex[4..6]}{hex[2..4]}{hex[0..2]}&".ToUpperInvariant();
    }

    private static string Invariant(FormattableString text) => text.ToString(Numbers.Instance);

    private enum Align
    {
        TopLeft = 7,
        TopCentre = 8,
        TopRight = 9,
        Centre = 5,
    }

    /// <summary>Collects ASS events, placing frame coordinates into the window.</summary>
    private sealed class Page(double offsetX, double offsetY, string colour, bool glow)
    {
        private readonly StringBuilder _events = new();

        /// <param name="y">Where the CSS line box's top (or, centred, its middle) would be.</param>
        /// <param name="size">The CSS font size, in frame pixels.</param>
        public void Text(string text, double x, double y, double size, Align align)
        {
            var assY = align == Align.Centre ? y - AssMiddleAboveCss * size : y - AssTopAboveCss * size;
            var style = Invariant(
                $@"\an{(int)align}\pos({offsetX + x},{offsetY + assY})\fnVT323\fs{size * AssSizePerEm}\shad0\1c{colour}\3c{colour}");
            var safe = Escape(text);

            if (glow)
            {
                // A soft halo under the letters, like a phosphor bloom.
                Add(Invariant($@"{{{style}\bord{size * 0.12}\blur{size * 0.2}\1a&HFF&\3a&H90&}}{safe}"));
            }
            // VT323 has one weight; a thin outline in the same colour is the bold.
            Add(Invariant($@"{{{style}\bord{size * 0.0175}}}{safe}"));
        }

        public void Shapes(string path, double glowSize)
        {
            var style = Invariant($@"\an7\pos({offsetX},{offsetY})\shad0\1c{colour}\3c{colour}");
            if (glow) Add(Invariant($@"{{{style}\bord{glowSize / 2}\blur{glowSize}\3a&H90&\p1}}{path}{{\p0}}"));
            Add(Invariant($@"{{{style}\bord0\p1}}{path}{{\p0}}"));
        }

        private void Add(string assEvent)
        {
            if (_events.Length > 0) _events.Append('\n');
            _events.Append(assEvent);
        }

        public override string ToString() => _events.ToString();

        // Braces start override tags and a backslash starts an escape; neither
        // belongs in a channel name.
        private static string Escape(string text) =>
            text.Replace('\\', '/').Replace('{', '(').Replace('}', ')').Replace('\n', ' ');
    }

    /// <summary>Numbers in ASS are plain decimals with no more precision than a screen can use.</summary>
    private sealed class Numbers : IFormatProvider, ICustomFormatter
    {
        public static readonly Numbers Instance = new();

        public object? GetFormat(Type? formatType) => formatType == typeof(ICustomFormatter) ? this : null;

        public string Format(string? format, object? arg, IFormatProvider? formatProvider) => arg switch
        {
            double number => number.ToString(format ?? "0.##", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(format, CultureInfo.InvariantCulture),
            _ => arg?.ToString() ?? "",
        };
    }
}
