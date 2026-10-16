using System.Text.RegularExpressions;
using RabbitEars.Api;
using RabbitEars.Core;

namespace RabbitEars.Tests;

public sealed class MpvOsdTests
{
    private static readonly OsdOptions Green = new();
    private static readonly OsdOptions Flat = new() { Glow = false };

    [Fact]
    public void Nothing_to_show_draws_nothing()
    {
        Assert.Equal("", MpvOsd.Draw(OsdView.Nothing, 16.0 / 9, Green).Ass);
    }

    [Fact]
    public void The_banner_sits_top_right_of_a_4_by_3_frame_centred_on_a_wide_screen()
    {
        var view = OsdView.Nothing with { Banner = new ChannelInfo(2, "Arthur") };

        var overlay = MpvOsd.Draw(view, 16.0 / 9, Flat);

        Assert.Equal((1280, 720), (overlay.Width, overlay.Height));
        // 6% in from the frame's right edge, and the frame starts 160 in.
        Assert.Contains(@"\an9\pos(1062.4,", overlay.Ass);
        Assert.Contains("}CH 02", overlay.Ass);
        Assert.Contains("}Arthur", overlay.Ass);
        Assert.Contains(@"\1c&H5AFF4D&", overlay.Ass);
    }

    [Fact]
    public void A_message_takes_the_banners_place()
    {
        var view = OsdView.Nothing with { Banner = new ChannelInfo(2, "Arthur"), Message = "NO CHANNELS" };

        var ass = MpvOsd.Draw(view, 16.0 / 9, Green).Ass;

        Assert.Contains("}NO CHANNELS", ass);
        Assert.DoesNotContain("CH 02", ass);
    }

    [Fact]
    public void The_volume_bar_has_twenty_slots_lit_to_the_level()
    {
        var view = OsdView.Nothing with { ShowVolume = true, Volume = 60 };

        var ass = MpvOsd.Draw(view, 4.0 / 3, Flat).Ass;

        Assert.Contains("}Volume", ass);
        var shapes = ass.Split('\n').Single(line => line.Contains(@"\p1"));
        // Bars are four-cornered paths; dots are drawn with curves.
        Assert.Equal(12, Regex.Count(shapes, @"m [\d.]+ [\d.]+ l"));
        Assert.Equal(8, Regex.Count(shapes, @"m [\d.]+ [\d.]+ b"));
    }

    [Fact]
    public void Muted_shows_an_empty_bar()
    {
        var view = OsdView.Nothing with { ShowVolume = true, Volume = 60, Muted = true };

        var ass = MpvOsd.Draw(view, 16.0 / 9, Green).Ass;

        Assert.Contains("}Mute", ass);
        Assert.DoesNotContain(" l ", ass);
    }

    [Fact]
    public void Braces_in_a_folder_name_cannot_inject_drawing_commands()
    {
        var view = OsdView.Nothing with { Banner = new ChannelInfo(2, @"Shows {\fs200} \N") };

        var ass = MpvOsd.Draw(view, 16.0 / 9, Flat).Ass;

        Assert.Contains("}Shows (/fs200) /N", ass);
    }
}
