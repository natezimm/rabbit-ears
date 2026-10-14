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

    private TvService Service()
    {
        var options = new BoxOptions { MediaRoot = _media, StateDir = _state, RescanMinutes = 0 };
        return new TvService(
            options,
            new LibraryScanner(options, NullLogger<LibraryScanner>.Instance),
            new StateFile(options, NullLogger<StateFile>.Instance),
            _hub,
            NullLogger<TvService>.Instance);
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

    /// <summary>Stands in for SignalR: every client gets the same messages, so one list records them.</summary>
    private sealed class RecordingHub : IHubContext<TvHub>, IClientProxy
    {
        public List<(string Method, object?[] Args)> Sent { get; } = [];

        IHubClients IHubContext<TvHub>.Clients => new Everyone(this);
        IGroupManager IHubContext<TvHub>.Groups => throw new NotSupportedException();

        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        {
            Sent.Add((method, args));
            return Task.CompletedTask;
        }

        private sealed class Everyone(IClientProxy proxy) : IHubClients
        {
            public IClientProxy All => proxy;
            public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => proxy;
            public IClientProxy Client(string connectionId) => proxy;
            public IClientProxy Clients(IReadOnlyList<string> connectionIds) => proxy;
            public IClientProxy Group(string groupName) => proxy;
            public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => proxy;
            public IClientProxy Groups(IReadOnlyList<string> groupNames) => proxy;
            public IClientProxy User(string userId) => proxy;
            public IClientProxy Users(IReadOnlyList<string> userIds) => proxy;
        }
    }
}
