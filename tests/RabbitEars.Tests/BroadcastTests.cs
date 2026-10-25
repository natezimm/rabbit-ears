using RabbitEars.Core;

namespace RabbitEars.Tests;

public sealed class BroadcastTests
{
    // Deliberately reversed: the schedule must not depend on disk enumeration.
    private static readonly ChannelDefinition[] Channels =
    [
        new(2, "Animation", [new("b", "Second"), new("a", "First")]),
        new(3, "Sci-Fi", [new("c", "Film")]),
    ];
    private static readonly Dictionary<string, double> Lengths = new()
        { ["a"] = 100, ["b"] = 200, ["c"] = 80 };

    private static Television Broadcast(FakeClock clock, TvSettings? settings = null,
        IReadOnlyDictionary<string, double>? lengths = null) =>
        new(settings ?? new TvSettings(), Channels, clock: clock,
            synchronizedBroadcast: true, durations: lengths ?? Lengths);

    private static void AtCycleStart(FakeClock clock)
    {
        var elapsed = (clock.GetUtcNow() - DateTimeOffset.UnixEpoch).TotalSeconds;
        clock.Advance(TimeSpan.FromSeconds((300 - elapsed % 300) % 300));
    }

    [Fact]
    public void New_rooms_and_returning_viewers_join_the_same_mid_show_instant()
    {
        var clock = new FakeClock();
        AtCycleStart(clock);
        var first = Broadcast(clock);
        first.ChannelUp();
        clock.Advance(TimeSpan.FromSeconds(142.25));
        first.ChannelDown();
        var newcomer = Broadcast(clock);
        var a = first.Snapshot().Playing!;
        var b = newcomer.Snapshot().Playing!;
        Assert.Equal("b", a.EpisodeId);
        Assert.Equal(42.25, a.Start);
        Assert.Equal((a.EpisodeId, a.Start), (b.EpisodeId, b.Start));
        Assert.NotEqual(a.Token, b.Token);
    }

    [Fact]
    public void Episode_end_and_full_cycle_use_the_measured_playlist_durations()
    {
        var clock = new FakeClock();
        AtCycleStart(clock);
        clock.Advance(TimeSpan.FromSeconds(99.5));
        var tv = Broadcast(clock);
        var token = tv.Snapshot().Playing!.Token;
        clock.Advance(TimeSpan.FromSeconds(0.5));
        Assert.True(tv.EpisodeEnded(token));
        Assert.Equal("b", tv.Snapshot().Playing!.EpisodeId);
        Assert.Equal(0, tv.Snapshot().Playing!.Start);
        token = tv.Snapshot().Playing!.Token;
        clock.Advance(TimeSpan.FromSeconds(201));
        tv.EpisodeEnded(token);
        Assert.Equal("a", tv.Snapshot().Playing!.EpisodeId);
        Assert.Equal(1, tv.Snapshot().Playing!.Start);
    }

    [Fact]
    public void Position_reports_cannot_shift_station_time_and_standby_rejoins_live()
    {
        var clock = new FakeClock();
        AtCycleStart(clock);
        var tv = Broadcast(clock);
        tv.ReportPosition(tv.Snapshot().Playing!.Token, 999, duration: 1);
        tv.TogglePower();
        clock.Advance(TimeSpan.FromSeconds(34));
        tv.TogglePower();
        Assert.Equal(34, tv.Snapshot().Playing!.Start);
        Assert.Equal("a", tv.Snapshot().Playing!.EpisodeId);
    }

    [Fact]
    public void Missing_durations_fall_back_then_join_the_clock_on_next_tune()
    {
        var clock = new FakeClock();
        AtCycleStart(clock);
        var tv = Broadcast(clock, lengths: new Dictionary<string, double>());
        Assert.InRange(tv.Snapshot().Playing!.Start, 6, 10);
        foreach (var (id, seconds) in Lengths) tv.SetDuration(id, seconds);
        tv.ChannelUp();
        clock.Advance(TimeSpan.FromSeconds(25));
        tv.ChannelDown();
        Assert.Equal("a", tv.Snapshot().Playing!.EpisodeId);
        Assert.Equal(25, tv.Snapshot().Playing!.Start);
    }

    [Fact]
    public void Resume_keeps_its_room_position_and_unknown_or_invalid_lengths_do_not_break_tuning()
    {
        var clock = new FakeClock();
        var tv = Broadcast(clock, new TvSettings { TuneIn = TuneInMode.Resume });
        tv.ReportPosition(tv.Snapshot().Playing!.Token, 42);
        tv.ChannelUp();
        clock.Advance(TimeSpan.FromDays(3));
        tv.ChannelDown();
        Assert.Equal(42, tv.Snapshot().Playing!.Start);
        var invalid = Broadcast(clock, lengths: new Dictionary<string, double>
            { ["a"] = double.NaN, ["b"] = -1 });
        Assert.InRange(invalid.Snapshot().Playing!.Start, 6, 10);
    }
}
