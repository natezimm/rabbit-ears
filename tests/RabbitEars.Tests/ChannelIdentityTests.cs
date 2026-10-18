using RabbitEars.Core;

namespace RabbitEars.Tests;

public sealed class ChannelIdentityTests
{
    private static readonly Episode One = new("one", "One");
    private static readonly Episode Two = new("two", "Two");
    private static readonly TvSettings Settings = new() { StartOffsetMin = 0, StartOffsetMax = 0 };

    [Fact]
    public void Rename_and_reorder_preserve_play_token_position_and_last_channel_across_restart()
    {
        var clock = new FakeClock();
        var tv = new Television(Settings, [new(2, "Arthur", [One], "Arthur"), new(3, "Movies", [Two], "Movies")], clock: clock);
        var token = tv.Snapshot().Playing!.Token;
        tv.ReportPosition(token, 45);
        ChannelDefinition[] renamed = [new(8, "PBS", [One], "Arthur"), new(3, "Movies", [Two], "Movies")];
        tv.SetLineup(renamed);
        Assert.Equal(token, tv.Snapshot().Playing!.Token);
        Assert.Equal(new ChannelInfo(8, "PBS"), tv.Snapshot().Channel);
        tv.GoToChannel(3);
        var saved = tv.Save();
        var restarted = new Television(Settings, renamed, saved, clock: clock);
        restarted.LastChannel();
        Assert.Equal(new ChannelInfo(8, "PBS"), restarted.Snapshot().Channel);
        Assert.Equal(45, restarted.Snapshot().Playing!.Start);
    }

    [Fact]
    public void Each_channel_can_override_the_global_tune_in_mode()
    {
        var clock = new FakeClock();
        var tv = new Television(Settings, [new(2, "Resume", [One], TuneIn: TuneInMode.Resume), new(3, "Live", [Two])], clock: clock);
        tv.ReportPosition(tv.Snapshot().Playing!.Token, 25);
        tv.GoToChannel(3);
        tv.ReportPosition(tv.Snapshot().Playing!.Token, 30);
        clock.Advance(TimeSpan.FromSeconds(60));
        tv.GoToChannel(2);
        Assert.Equal(25, tv.Snapshot().Playing!.Start);
        tv.GoToChannel(3);
        Assert.Equal(90, tv.Snapshot().Playing!.Start);
    }

    [Fact]
    public void Removing_current_episode_retunes_and_an_empty_lineup_clears_playback()
    {
        var tv = new Television(Settings, [new(2, "Show", [One, Two])], random: new Random(1));
        var old = tv.Snapshot().Playing!;
        var other = old.EpisodeId == "one" ? Two : One;
        tv.SetLineup([new(2, "Show", [other])]);
        Assert.Equal(other.Id, tv.Snapshot().Playing!.EpisodeId);
        Assert.False(tv.EpisodeEnded(old.Token));
        tv.SetLineup([]);
        Assert.Null(tv.Snapshot().Channel);
        Assert.Null(tv.Snapshot().Playing);
        Assert.Equal("NO CHANNELS", tv.Snapshot().Message!.Text);
    }
}
