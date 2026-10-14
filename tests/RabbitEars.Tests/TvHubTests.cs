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

    private async Task<(HubConnection Remote, ChannelReader<TvSnapshot> States)> ConnectRemote()
    {
        var states = Channel.CreateUnbounded<TvSnapshot>();
        var remote = new HubConnectionBuilder()
            .WithUrl(new Uri(_box.Server.BaseAddress, "hub/tv"), options =>
            {
                options.HttpMessageHandlerFactory = _ => _box.Server.CreateHandler();
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
}
