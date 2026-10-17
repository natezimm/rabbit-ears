using RabbitEars.Core;

namespace RabbitEars.Tests;

public class TelevisionTests
{
    private static ChannelDefinition Channel(int number, string name, int episodes = 3) =>
        new(number, name, [.. Enumerable.Range(1, episodes).Select(i => new Episode($"{name}-{i}", $"{name} E{i}"))]);

    private static readonly ChannelDefinition[] Lineup =
        [Channel(2, "Arthur"), Channel(3, "Dragon Tales"), Channel(4, "Winnie the Pooh")];

    private static Television Tv(TvSettings? settings = null, SavedState? saved = null, FakeClock? clock = null) =>
        new(settings ?? new TvSettings(), Lineup, saved, new Random(7), clock ?? new FakeClock());

    [Fact]
    public void Powers_on_to_the_start_channel_already_a_few_seconds_in()
    {
        var tv = Tv(new TvSettings { StartChannel = 3 });

        var snapshot = tv.Snapshot();

        Assert.Equal(new ChannelInfo(3, "Dragon Tales"), snapshot.Channel);
        Assert.NotNull(snapshot.Playing);
        Assert.InRange(snapshot.Playing.Start, 6, 10);
        Assert.Equal(PlayReason.Wake, snapshot.Playing.Reason);
    }

    [Fact]
    public void Channel_up_and_down_wrap_around_the_lineup()
    {
        var tv = Tv();

        tv.ChannelDown();
        Assert.Equal(4, tv.Snapshot().Channel!.Number);

        tv.ChannelUp();
        Assert.Equal(2, tv.Snapshot().Channel!.Number);
    }

    [Fact]
    public void Changing_channel_flashes_the_banner_and_asks_for_the_change_effect()
    {
        var tv = Tv();
        var before = tv.Snapshot();

        tv.ChannelUp();
        var after = tv.Snapshot();

        Assert.True(after.BannerSeq > before.BannerSeq);
        Assert.Equal(PlayReason.Tune, after.Playing!.Reason);
        Assert.NotEqual(before.Playing!.Token, after.Playing.Token);
    }

    [Fact]
    public void Resume_mode_comes_back_to_the_same_episode_where_you_left_it()
    {
        var tv = Tv(new TvSettings { TuneIn = TuneInMode.Resume });
        var arthur = tv.Snapshot().Playing!;

        tv.ChannelUp();
        // The screen reports where Arthur had got to just after it is told to switch.
        tv.ReportPosition(arthur.Token, 312.5);
        tv.ChannelDown();

        var back = tv.Snapshot().Playing!;
        Assert.Equal(arthur.EpisodeId, back.EpisodeId);
        Assert.Equal(312.5, back.Start);
    }

    [Fact]
    public void Random_mode_starts_a_fresh_episode_on_every_flip()
    {
        var tv = Tv(new TvSettings { TuneIn = TuneInMode.Random });
        var first = tv.Snapshot().Playing!;

        tv.ChannelUp();
        tv.ChannelDown();

        var back = tv.Snapshot().Playing!;
        Assert.NotEqual(first.EpisodeId, back.EpisodeId);
        Assert.InRange(back.Start, 6, 10);
    }

    [Fact]
    public void Live_mode_keeps_a_channel_running_while_you_watch_another()
    {
        var clock = new FakeClock();
        var tv = Tv(clock: clock);
        var arthur = tv.Snapshot().Playing!;
        tv.ReportPosition(arthur.Token, 300, duration: 1500);

        tv.ChannelUp();
        clock.Advance(TimeSpan.FromMinutes(5));
        tv.ChannelDown();

        var back = tv.Snapshot().Playing!;
        Assert.Equal(arthur.EpisodeId, back.EpisodeId);
        Assert.Equal(600, back.Start);
    }

    [Fact]
    public void Live_mode_rolls_into_later_episodes_that_aired_while_you_were_away()
    {
        var clock = new FakeClock();
        var tv = Tv(clock: clock);
        var first = tv.Snapshot().Playing!;
        tv.ReportPosition(first.Token, 1400, duration: 1500);
        tv.ChannelUp();

        // Watch the next Arthur episode for a while to learn its length too.
        clock.Advance(TimeSpan.FromSeconds(150));
        tv.ChannelDown();
        var second = tv.Snapshot().Playing!;
        Assert.NotEqual(first.EpisodeId, second.EpisodeId);
        Assert.InRange(second.Start, 6 + 50, 10 + 50);
        tv.ReportPosition(second.Token, 100, duration: 1000);

        tv.ChannelUp();
        clock.Advance(TimeSpan.FromSeconds(950));
        tv.ChannelDown();

        var third = tv.Snapshot().Playing!;
        Assert.NotEqual(second.EpisodeId, third.EpisodeId);
        Assert.InRange(third.Start, 6 + 50, 10 + 50);
    }

    [Fact]
    public void Live_mode_keeps_running_through_standby_and_reboots()
    {
        var clock = new FakeClock();
        var tv = Tv(clock: clock);
        var playing = tv.Snapshot().Playing!;
        tv.ReportPosition(playing.Token, 100, duration: 1500);

        tv.TogglePower();
        clock.Advance(TimeSpan.FromSeconds(60));
        tv.TogglePower();
        Assert.Equal(160, tv.Snapshot().Playing!.Start);

        var saved = tv.Save();
        clock.Advance(TimeSpan.FromSeconds(240));
        var rebooted = Tv(saved: saved, clock: clock);
        Assert.Equal(playing.EpisodeId, rebooted.Snapshot().Playing!.EpisodeId);
        Assert.Equal(400, rebooted.Snapshot().Playing!.Start);
    }

    [Fact]
    public void Live_mode_joins_an_episode_of_unknown_length_at_the_time_that_passed()
    {
        var clock = new FakeClock();
        var tv = Tv(clock: clock);
        var playing = tv.Snapshot().Playing!;
        tv.ReportPosition(playing.Token, 100);

        tv.ChannelUp();
        clock.Advance(TimeSpan.FromHours(2));
        tv.ChannelDown();

        // Past its end, so the screen ends it at once and the next one rolls.
        Assert.Equal(playing.EpisodeId, tv.Snapshot().Playing!.EpisodeId);
        Assert.Equal(7300, tv.Snapshot().Playing!.Start);
    }

    [Fact]
    public void When_an_episode_ends_the_next_one_rolls_without_repeating()
    {
        var tv = Tv();
        var seen = new List<string> { tv.Snapshot().Playing!.EpisodeId };

        for (var i = 0; i < 2; i++)
        {
            Assert.True(tv.EpisodeEnded(tv.Snapshot().Playing!.Token));
            seen.Add(tv.Snapshot().Playing!.EpisodeId);
        }

        Assert.Equal(3, seen.Distinct().Count());
        Assert.Equal(PlayReason.Advance, tv.Snapshot().Playing!.Reason);
    }

    [Fact]
    public void A_late_end_report_for_a_channel_already_left_is_ignored()
    {
        var tv = Tv();
        var stale = tv.Snapshot().Playing!.Token;
        tv.ChannelUp();
        var current = tv.Snapshot().Playing!;

        Assert.False(tv.EpisodeEnded(stale));
        Assert.Equal(current, tv.Snapshot().Playing);
    }

    [Fact]
    public void A_channel_of_unplayable_files_gives_up_with_no_signal()
    {
        var tv = Tv();

        for (var i = 0; i < 3; i++)
            tv.EpisodeFailed(tv.Snapshot().Playing?.Token ?? "");

        var snapshot = tv.Snapshot();
        Assert.Null(snapshot.Playing);
        Assert.Equal("CH 02  Arthur  -  NO SIGNAL", snapshot.Message!.Text);
    }

    [Fact]
    public void Typed_digits_tune_after_a_pause()
    {
        var clock = new FakeClock();
        var tv = Tv(clock: clock);

        tv.PushDigit(0);
        tv.PushDigit(4);
        Assert.Equal("CH 04_", tv.Snapshot().Message!.Text);
        Assert.False(tv.Tick());

        clock.Advance(TimeSpan.FromSeconds(2.1));
        Assert.True(tv.Tick());

        Assert.Equal(4, tv.Snapshot().Channel!.Number);
        Assert.Null(tv.Snapshot().Message);
    }

    [Fact]
    public void Confirm_tunes_to_typed_digits_straight_away()
    {
        var tv = Tv();

        tv.PushDigit(3);
        tv.Confirm();

        Assert.Equal(3, tv.Snapshot().Channel!.Number);
    }

    [Fact]
    public void A_number_with_no_channel_says_so_and_stays_put()
    {
        var tv = Tv();

        tv.GoToChannel(9);

        Assert.Equal(2, tv.Snapshot().Channel!.Number);
        Assert.Equal("CH 09  -  NO CHANNEL", tv.Snapshot().Message!.Text);
    }

    [Fact]
    public void Last_channel_jumps_back_and_forth()
    {
        var tv = Tv();
        tv.GoToChannel(4);

        tv.LastChannel();
        Assert.Equal(2, tv.Snapshot().Channel!.Number);

        tv.LastChannel();
        Assert.Equal(4, tv.Snapshot().Channel!.Number);
    }

    [Fact]
    public void Volume_steps_clamp_and_unmute()
    {
        var tv = Tv(new TvSettings { InitialVolume = 95, VolumeStep = 5 });
        tv.ToggleMute();
        Assert.True(tv.Snapshot().Muted);

        tv.VolumeUp();
        tv.VolumeUp();

        var snapshot = tv.Snapshot();
        Assert.Equal(100, snapshot.Volume);
        Assert.False(snapshot.Muted);
    }

    [Fact]
    public void Volume_down_past_zero_says_goodbye_when_enabled()
    {
        var tv = Tv(new TvSettings { InitialVolume = 5, PowerOffAtZeroVolume = true });

        tv.VolumeDown();
        Assert.False(tv.PoweredOff);

        tv.VolumeDown();

        Assert.True(tv.PoweredOff);
        Assert.Equal("GOODBYE", tv.Snapshot().Message!.Text);
        Assert.Null(tv.Snapshot().Playing);
    }

    [Fact]
    public void Volume_down_at_zero_does_nothing_when_power_off_is_disabled()
    {
        var tv = Tv(new TvSettings { InitialVolume = 0 });

        tv.VolumeDown();

        Assert.False(tv.PoweredOff);
        Assert.Equal(0, tv.Snapshot().Volume);
    }

    [Fact]
    public void Standby_blanks_the_screen_and_ignores_everything_but_power()
    {
        var tv = Tv();
        var playing = tv.Snapshot().Playing!;
        tv.ReportPosition(playing.Token, 120);

        tv.TogglePower();
        tv.ChannelUp();
        tv.VolumeUp();

        var asleep = tv.Snapshot();
        Assert.True(asleep.Standby);
        Assert.Null(asleep.Playing);
        Assert.Equal(2, asleep.Channel!.Number);
        Assert.Equal(60, asleep.Volume);

        tv.TogglePower();

        var awake = tv.Snapshot();
        Assert.False(awake.Standby);
        Assert.Equal(playing.EpisodeId, awake.Playing!.EpisodeId);
        Assert.Equal(120, awake.Playing.Start);
        Assert.Equal(PlayReason.Wake, awake.Playing.Reason);
    }

    [Fact]
    public void A_reboot_comes_back_to_the_same_channel_volume_and_position()
    {
        var tv = Tv();
        tv.GoToChannel(3);
        tv.VolumeDown();
        var playing = tv.Snapshot().Playing!;
        tv.ReportPosition(playing.Token, 77);

        var rebooted = Tv(saved: tv.Save());

        var snapshot = rebooted.Snapshot();
        Assert.Equal(3, snapshot.Channel!.Number);
        Assert.Equal(55, snapshot.Volume);
        Assert.Equal(playing.EpisodeId, snapshot.Playing!.EpisodeId);
        Assert.Equal(77, snapshot.Playing.Start);
    }

    [Fact]
    public void A_rescan_that_adds_a_show_does_not_interrupt_what_is_on()
    {
        var tv = Tv();
        tv.GoToChannel(3);
        var playing = tv.Snapshot().Playing!;

        // "Blue's Clues" sorts in before Dragon Tales and takes its number.
        tv.SetLineup([Channel(2, "Arthur"), Channel(3, "Blue's Clues"), Channel(4, "Dragon Tales"), Channel(5, "Winnie the Pooh")]);

        var snapshot = tv.Snapshot();
        Assert.Equal(new ChannelInfo(4, "Dragon Tales"), snapshot.Channel);
        Assert.Equal(playing, snapshot.Playing);
        Assert.Equal(4, snapshot.Channels.Count);
    }

    [Fact]
    public void A_rescan_that_removes_the_current_show_tunes_somewhere_else()
    {
        var tv = Tv();
        tv.GoToChannel(3);

        tv.SetLineup([Channel(2, "Arthur"), Channel(3, "Winnie the Pooh")]);

        var snapshot = tv.Snapshot();
        Assert.Equal("Arthur", snapshot.Channel!.Name);
        Assert.StartsWith("Arthur-", snapshot.Playing!.EpisodeId);
    }

    [Fact]
    public void No_shows_at_all_says_no_channels()
    {
        var tv = new Television(new TvSettings(), []);

        tv.ChannelUp();

        var snapshot = tv.Snapshot();
        Assert.Null(snapshot.Channel);
        Assert.Null(snapshot.Playing);
        Assert.Equal("NO CHANNELS", snapshot.Message!.Text);
    }

    private sealed class FakeClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
