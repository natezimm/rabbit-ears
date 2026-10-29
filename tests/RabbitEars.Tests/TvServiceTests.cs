using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using RabbitEars.Api;
using RabbitEars.Core;

namespace RabbitEars.Tests;

public sealed class TvServiceTests : IDisposable
{
    private readonly string _media = Directory.CreateTempSubdirectory("rabbitears-media-").FullName;
    private readonly string _state = Directory.CreateTempSubdirectory("rabbitears-state-").FullName;
    private readonly RecordingHub _hub = new();
    private readonly FakeClock _clock = new();

    public TvServiceTests()
    {
        Touch("Arthur/S01E01.mp4");
        Touch("Arthur/S01E02.mp4");
        Touch("Dragon Tales/S01E01.mp4");
    }

    public void Dispose()
    {
        Directory.Delete(_media, recursive: true);
        Directory.Delete(_state, recursive: true);
    }

    private void Touch(string relativePath)
    {
        var path = Path.Combine(_media, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "");
    }

    private TvService Service(DemoOptions? demo = null, IEpisodeProbe? probe = null)
    {
        var options = new BoxOptions
        {
            MediaRoot = _media,
            StateDir = _state,
            RescanMinutes = 0,
            Demo = demo ?? new DemoOptions(),
        };
        return new TvService(
            options,
            new LibraryScanner(options, NullLogger<LibraryScanner>.Instance),
            new StateFile(options, NullLogger<StateFile>.Instance),
            _hub,
            NullLogger<TvService>.Instance,
            _clock,
            probe);
    }

    private TvService DemoService(int maxTvs = 100) =>
        Service(new DemoOptions { Enabled = true, MaxTvs = maxTvs, IdleMinutes = 15 });

    [Fact]
    public void Demo_connections_are_bounded_before_rooms_are_allocated()
    {
        var tv = Service(new DemoOptions { Enabled = true, MaxConnections = 3, MaxConnectionsPerRoom = 2 });
        Assert.NotNull(tv.Join("visitor-a"));
        Assert.NotNull(tv.Join("visitor-a"));
        Assert.Null(tv.Join("visitor-a"));
        Assert.NotNull(tv.Join("visitor-b"));
        Assert.Null(tv.Join("visitor-c"));
        Assert.Equal(2, tv.RoomCount);
        tv.Leave("visitor-a");
        Assert.NotNull(tv.Join("visitor-c"));
    }

    [Fact]
    public async Task Rejected_tapes_preserve_playback_and_budget_recovers()
    {
        var tv = DemoService();
        tv.Join("visitor-a");
        TapeShelf[] shelves = [new(null, [new("0000000a", "Tape")])];
        for (var i = 0; i < 6; i++) Assert.True(await tv.LoadTapes(shelves, "visitor-a", "tab"));
        var before = tv.Snapshot("visitor-a")!;
        Assert.False(await tv.LoadTapes([new(null, [new("0000000b", "Other")])], "visitor-a", "tab"));
        Assert.Equal(before.Playing, tv.Snapshot("visitor-a")!.Playing);
        Assert.False(await tv.LoadTapes(null, "visitor-a", "tab"));
        _clock.Advance(TimeSpan.FromMinutes(1));
        Assert.True(await tv.LoadTapes(shelves, "visitor-a", "tab"));
    }

    [Fact]
    public async Task New_demo_rooms_use_known_lengths_on_their_first_snapshot_and_keep_controls_isolated()
    {
        var service = Service(new DemoOptions { Enabled = true }, new FakeProbe(1000));
        await service.ProbeLengths(CancellationToken.None);
        service.Join("visitor-a");
        await service.Press(t => t.ChannelUp(), "visitor-a");
        _clock.Advance(TimeSpan.FromSeconds(234.5));
        service.Join("visitor-b");
        await service.Press(t => t.ChannelDown(), "visitor-a");
        var a = service.Snapshot("visitor-a")!.Playing!;
        var b = service.Snapshot("visitor-b")!.Playing!;
        Assert.Equal((a.EpisodeId, a.Start), (b.EpisodeId, b.Start));
        var expected = (_clock.GetUtcNow() - DateTimeOffset.UnixEpoch).TotalSeconds % 1000;
        Assert.Equal(expected, b.Start);
        await service.Press(t => t.ToggleMute(), "visitor-a");
        Assert.True(service.Snapshot("visitor-a")!.Muted);
        Assert.False(service.Snapshot("visitor-b")!.Muted);
    }

    [Fact]
    public async Task A_press_that_changes_the_tv_sends_the_new_snapshot_to_everyone()
    {
        var tv = Service();
        var before = tv.Snapshot();

        await tv.Press(t => t.ChannelUp());

        var sent = Assert.Single(_hub.Sent);
        Assert.Equal("State", sent.Method);
        var snapshot = Assert.IsType<TvSnapshot>(Assert.Single(sent.Args));
        Assert.True(snapshot.Revision > before.Revision);
        Assert.Equal(new ChannelInfo(3, "Dragon Tales"), snapshot.Channel);
    }

    [Fact]
    public async Task A_late_episode_ended_for_a_channel_just_left_changes_nothing_and_sends_nothing()
    {
        var tv = Service();
        var arthur = tv.Snapshot().Playing!;
        await tv.Press(t => t.ChannelUp());
        var dragonTales = tv.Snapshot();
        _hub.Sent.Clear();

        await tv.Press(t => t.EpisodeEnded(arthur.Token));

        Assert.Empty(_hub.Sent);
        Assert.Equal((dragonTales.Revision, dragonTales.Playing), (tv.Snapshot().Revision, tv.Snapshot().Playing));
    }

    [Fact]
    public async Task Probed_lengths_let_a_channel_roll_on_to_its_next_episode_while_away()
    {
        var probe = new FakeProbe(1000);
        var tv = Service(probe: probe);
        await tv.ProbeLengths(CancellationToken.None);
        var first = tv.Snapshot().Playing!;

        await tv.Press(t => t.ChannelUp());
        _clock.Advance(TimeSpan.FromSeconds(1000));
        await tv.Press(t => t.ChannelDown());

        Assert.Equal(3, probe.Asked);
        Assert.NotEqual(first.EpisodeId, tv.Snapshot().Playing!.EpisodeId);

        // Lengths the television already knows are not probed again.
        await tv.StopAsync(CancellationToken.None);
        var again = new FakeProbe(1000);
        await Service(probe: again).ProbeLengths(CancellationToken.None);
        Assert.Equal(0, again.Asked);
    }

    private sealed class FakeProbe(double seconds) : IEpisodeProbe
    {
        public int Asked { get; private set; }

        public Task<double?> Length(string path, CancellationToken cancellationToken)
        {
            Asked++;
            return Task.FromResult<double?>(seconds);
        }
    }

    [Fact]
    public void Position_reports_are_not_broadcast()
    {
        var tv = Service();

        tv.ReportPosition(tv.Snapshot().Playing!.Token, 42);

        Assert.Empty(_hub.Sent);
    }

    [Fact]
    public async Task Channel_and_position_survive_a_restart()
    {
        var first = Service();
        await first.Press(t => t.ChannelUp());
        var playing = first.Snapshot().Playing!;
        first.ReportPosition(playing.Token, 42);
        await first.StopAsync(CancellationToken.None);

        var second = Service().Snapshot();

        Assert.Equal(new ChannelInfo(3, "Dragon Tales"), second.Channel);
        Assert.Equal(playing.EpisodeId, second.Playing!.EpisodeId);
        Assert.Equal(42, second.Playing.Start);
    }

    [Fact]
    public void Only_scanned_episodes_resolve_to_files()
    {
        var tv = Service();
        var id = tv.Snapshot().Playing!.EpisodeId;

        Assert.StartsWith(Path.Combine(_media, "Arthur"), tv.FindMedia(id));
        Assert.Null(tv.FindMedia("../../etc/passwd"));
        Assert.Null(tv.FindMedia("Arthur/S01E01.mp4"));
    }

    [Fact]
    public async Task A_rescan_adds_a_new_show_folder_and_tells_everyone()
    {
        var tv = Service();
        Touch("Winnie the Pooh/S01E01.mp4");

        await tv.Rescan();

        Assert.Equal(["Arthur", "Dragon Tales", "Winnie the Pooh"], tv.Report().Channels.Select(c => c.Name));
        var snapshot = Assert.IsType<TvSnapshot>(Assert.Single(Assert.Single(_hub.Sent).Args));
        Assert.Equal(3, snapshot.Channels.Count);
    }

    [Fact]
    public void At_home_every_connection_joins_the_one_tv_whatever_room_it_asks_for()
    {
        var tv = Service();

        Assert.Equal(TvService.HomeRoom, tv.Join(null));
        Assert.Equal(TvService.HomeRoom, tv.Join("somebody-else"));
        Assert.Equal(1, tv.RoomCount);
    }

    [Fact]
    public async Task In_demo_mode_each_room_is_its_own_tv_and_hears_only_its_own_presses()
    {
        var tv = DemoService();
        Assert.Equal("visitor-a", tv.Join("visitor-a"));
        Assert.Equal("visitor-b", tv.Join("visitor-b"));

        await tv.Press(t => t.ChannelUp(), "visitor-a");

        Assert.Equal(new ChannelInfo(3, "Dragon Tales"), tv.Snapshot("visitor-a")!.Channel);
        Assert.Equal(new ChannelInfo(2, "Arthur"), tv.Snapshot("visitor-b")!.Channel);
        Assert.Equal("visitor-a", Assert.Single(_hub.Sent).Group);
    }

    [Fact]
    public async Task Tapes_become_channels_on_that_visitors_tv_only_and_eject_takes_them_out()
    {
        var tv = DemoService();
        tv.Join("visitor-a");
        tv.Join("visitor-b");

        await tv.LoadTapes([new TapeShelf(null, [new Tape("0000000a", "Home movie")])], "visitor-a", "tab-1");

        var a = tv.Snapshot("visitor-a")!;
        Assert.Equal(new ChannelInfo(0, "VCR"), a.Channel);
        Assert.Equal("tape-0000000a", a.Playing!.EpisodeId);
        Assert.Equal(3, a.Channels.Count);
        Assert.Equal(2, tv.Snapshot("visitor-b")!.Channels.Count);

        await tv.EjectTapes("visitor-a");

        Assert.Equal(2, tv.Snapshot("visitor-a")!.Channels.Count);
        Assert.NotEqual(0, tv.Snapshot("visitor-a")!.Channel!.Number);
    }

    [Fact]
    public async Task Tapes_come_out_when_the_tab_holding_them_leaves_but_not_when_another_client_does()
    {
        var tv = DemoService();
        tv.Join("visitor-a");
        await tv.LoadTapes([new TapeShelf("Cheers", [new Tape("0000000a", "Pilot")])], "visitor-a", "tab-1");

        await tv.EjectTapes("visitor-a", "phone");
        Assert.Equal(3, tv.Snapshot("visitor-a")!.Channels.Count);

        await tv.EjectTapes("visitor-a", "tab-1");
        Assert.Equal(2, tv.Snapshot("visitor-a")!.Channels.Count);
    }

    [Fact]
    public async Task Reloading_tapes_without_tuning_keeps_the_channel_being_watched()
    {
        var tv = DemoService();
        tv.Join("visitor-a");
        var shelves = new[] { new TapeShelf("Cheers", [new Tape("0000000a", "Pilot")]) };

        await tv.LoadTapes(shelves, "visitor-a", "tab-1", tune: false);

        Assert.Equal(new ChannelInfo(2, "Arthur"), tv.Snapshot("visitor-a")!.Channel);
        Assert.Equal(3, tv.Snapshot("visitor-a")!.Channels.Count);
    }

    [Fact]
    public async Task At_home_tapes_are_ignored()
    {
        var tv = Service();
        tv.Join(null);

        await tv.LoadTapes([new TapeShelf(null, [new Tape("0000000a", "Home movie")])], TvService.HomeRoom, "tab-1");

        Assert.Equal(2, tv.Snapshot().Channels.Count);
        Assert.Empty(_hub.Sent);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("../../etc")]
    public void In_demo_mode_a_missing_or_odd_room_id_is_refused(string? room)
    {
        var tv = DemoService();

        Assert.Null(tv.Join(room));
        Assert.Equal(0, tv.RoomCount);
    }

    [Fact]
    public async Task In_demo_mode_nothing_is_written_to_disk()
    {
        var tv = DemoService();
        tv.Join("visitor-a");
        tv.ReportPosition(tv.Snapshot("visitor-a")!.Playing!.Token, 42, room: "visitor-a");

        await tv.StopAsync(CancellationToken.None);

        Assert.Empty(Directory.GetFiles(_state));
    }

    [Fact]
    public void An_unwatched_demo_tv_is_switched_off_after_the_idle_time_but_a_watched_one_stays()
    {
        var tv = DemoService();
        tv.Join("visitor-a");
        tv.Join("visitor-b");
        tv.Leave("visitor-a");

        _clock.Advance(TimeSpan.FromMinutes(14));
        tv.ClearIdleRooms();
        Assert.Equal(2, tv.RoomCount);

        _clock.Advance(TimeSpan.FromMinutes(2));
        tv.ClearIdleRooms();
        Assert.Null(tv.Snapshot("visitor-a"));
        Assert.NotNull(tv.Snapshot("visitor-b"));
    }

    [Fact]
    public void When_the_demo_is_full_a_new_visitor_takes_the_place_of_the_longest_unwatched_tv()
    {
        var tv = DemoService(maxTvs: 2);
        tv.Join("visitor-a");
        tv.Leave("visitor-a");
        _clock.Advance(TimeSpan.FromMinutes(1));
        tv.Join("visitor-b");

        Assert.Equal("visitor-c", tv.Join("visitor-c"));
        Assert.Null(tv.Snapshot("visitor-a"));

        // Both remaining TVs are being watched, so there is no room for a fourth.
        Assert.Null(tv.Join("visitor-d"));
    }
}
