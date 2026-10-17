using System.Net;
using System.Net.Http.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using RabbitEars.Api;
using RabbitEars.Core;

namespace RabbitEars.Tests;

/// <summary>The whole box in memory: the real endpoints and hub, over a temporary media folder.</summary>
public sealed class TvHubTests : IAsyncLifetime
{
    private readonly string _media = Directory.CreateTempSubdirectory("rabbitears-media-").FullName;
    private readonly string _state = Directory.CreateTempSubdirectory("rabbitears-state-").FullName;
    private WebApplicationFactory<Program> _box = null!;

    public Task InitializeAsync()
    {
        foreach (var path in new[] { "Arthur/S01E01.mp4", "Dragon Tales/S01E01.mp4" })
        {
            var full = Path.Combine(_media, path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, "not really a video");
        }

        _box = new WebApplicationFactory<Program>().WithWebHostBuilder(host => host
            .UseSetting("Box:MediaRoot", _media)
            .UseSetting("Box:StateDir", _state)
            .UseSetting("Box:RescanMinutes", "0"));
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _box.DisposeAsync();
        Directory.Delete(_media, recursive: true);
        Directory.Delete(_state, recursive: true);
    }

    /// <summary>The same box set up as the public demo site.</summary>
    private WebApplicationFactory<Program> Demo() =>
        _box.WithWebHostBuilder(host => host.UseSetting("Box:Demo:Enabled", "true"));

    private Task<(HubConnection Remote, ChannelReader<TvSnapshot> States)> ConnectRemote() =>
        ConnectRemote(_box, "hub/tv");

    private static async Task<(HubConnection Remote, ChannelReader<TvSnapshot> States)> ConnectRemote(
        WebApplicationFactory<Program> box, string path)
    {
        var states = Channel.CreateUnbounded<TvSnapshot>();
        var remote = new HubConnectionBuilder()
            .WithUrl(new Uri(box.Server.BaseAddress, path), options =>
            {
                options.HttpMessageHandlerFactory = _ => box.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
            })
            .Build();
        remote.On<TvSnapshot>("State", snapshot => states.Writer.TryWrite(snapshot));
        await remote.StartAsync();
        return (remote, states.Reader);
    }

    private static async Task<TvSnapshot> Next(ChannelReader<TvSnapshot> states, Func<TvSnapshot, bool> match)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (true)
        {
            var snapshot = await states.ReadAsync(timeout.Token);
            if (match(snapshot)) return snapshot;
        }
    }

    [Fact]
    public async Task A_remote_gets_the_state_on_connect_and_after_each_press()
    {
        var (remote, states) = await ConnectRemote();
        await using var _ = remote;

        var first = await Next(states, _ => true);
        Assert.Equal(new ChannelInfo(2, "Arthur"), first.Channel);

        await remote.InvokeAsync("ChannelUp");

        var after = await Next(states, s => s.Revision > first.Revision);
        Assert.Equal(new ChannelInfo(3, "Dragon Tales"), after.Channel);
        Assert.Equal(PlayReason.Tune, after.Playing!.Reason);
    }

    [Fact]
    public async Task Every_connected_client_sees_a_press_from_any_of_them()
    {
        var (tvPage, tvStates) = await ConnectRemote();
        var (phone, phoneStates) = await ConnectRemote();
        await using var _ = tvPage;
        await using var __ = phone;
        var start = await Next(tvStates, _ => true);

        await phone.InvokeAsync("VolumeUp");

        var seen = await Next(tvStates, s => s.Revision > start.Revision);
        Assert.Equal(start.Volume + 5, seen.Volume);
        Assert.Equal(seen.Volume, (await Next(phoneStates, s => s.Revision >= seen.Revision)).Volume);
    }

    [Fact]
    public async Task The_library_lists_the_channels_and_media_serves_only_scanned_ids()
    {
        var http = _box.CreateClient();

        var library = await http.GetFromJsonAsync<LibraryReport>("/api/library");
        var state = await http.GetFromJsonAsync<TvSnapshot>("/api/state");

        Assert.Equal(["Arthur", "Dragon Tales"], library!.Channels.Select(c => c.Name));
        var episode = await http.GetAsync($"/media/{state!.Playing!.EpisodeId}");
        Assert.Equal(HttpStatusCode.OK, episode.StatusCode);
        Assert.Equal("video/mp4", episode.Content.Headers.ContentType!.MediaType);
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync("/media/0000000000000000")).StatusCode);
    }

    [Fact]
    public async Task On_the_demo_site_each_visitor_has_a_tv_of_their_own()
    {
        await using var demo = Demo();
        var (first, firstStates) = await ConnectRemote(demo, "hub/tv?room=visitor-one");
        var (second, secondStates) = await ConnectRemote(demo, "hub/tv?room=visitor-two");
        await using var _ = first;
        await using var __ = second;
        var start = await Next(firstStates, _ => true);
        await Next(secondStates, _ => true);

        await first.InvokeAsync("ChannelUp");
        await Next(firstStates, s => s.Revision > start.Revision);

        var http = demo.CreateClient();
        Assert.Equal(new ChannelInfo(2, "Arthur"),
            (await http.GetFromJsonAsync<TvSnapshot>("/api/state?room=visitor-two"))!.Channel);
        Assert.Equal(new ChannelInfo(3, "Dragon Tales"),
            (await http.GetFromJsonAsync<TvSnapshot>("/api/state?room=visitor-one"))!.Channel);
    }

    [Fact]
    public async Task On_the_demo_site_a_connection_with_no_room_is_turned_away()
    {
        await using var demo = Demo();

        var closed = new TaskCompletionSource();
        var (remote, _) = await ConnectRemote(demo, "hub/tv");
        await using var connection = remote;
        remote.Closed += _ =>
        {
            closed.TrySetResult();
            return Task.CompletedTask;
        };

        if (remote.State != HubConnectionState.Disconnected)
        {
            await closed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        Assert.Equal(HubConnectionState.Disconnected, remote.State);
    }

    [Fact]
    public async Task The_demo_site_opens_on_the_demo_page_and_has_no_rescan_button()
    {
        await using var demo = Demo();
        var http = demo.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var home = await http.GetAsync("/");
        var info = await http.GetFromJsonAsync<DemoInfo>("/api/demo");

        Assert.Equal(HttpStatusCode.Redirect, home.StatusCode);
        Assert.Equal("/demo", home.Headers.Location!.OriginalString);
        Assert.True(info!.Enabled);
        Assert.Equal(HttpStatusCode.NotFound, (await http.PostAsync("/api/rescan", null)).StatusCode);
    }

    [Fact]
    public async Task At_home_the_demo_is_off()
    {
        var info = await _box.CreateClient().GetFromJsonAsync<DemoInfo>("/api/demo");

        Assert.False(info!.Enabled);
    }
}
