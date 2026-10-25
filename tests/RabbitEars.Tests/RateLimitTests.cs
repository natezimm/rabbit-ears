using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using RabbitEars.Api;

namespace RabbitEars.Tests;

/// <summary>
/// The box with a tiny allowance that does not refill during a test. Clients
/// are told apart by X-Forwarded-For, as on the public demo behind nginx.
/// </summary>
public sealed class RateLimitTests : IAsyncLifetime
{
    private const int Burst = 3;

    private readonly string _media = Directory.CreateTempSubdirectory("rabbitears-media-").FullName;
    private readonly string _state = Directory.CreateTempSubdirectory("rabbitears-state-").FullName;
    private WebApplicationFactory<Program> _box = null!;

    public Task InitializeAsync()
    {
        var episode = Path.Combine(_media, "Arthur", "S01E01.mp4");
        Directory.CreateDirectory(Path.GetDirectoryName(episode)!);
        File.WriteAllText(episode, "not really a video");

        _box = new WebApplicationFactory<Program>().WithWebHostBuilder(host => host
            .UseSetting("Box:MediaRoot", _media)
            .UseSetting("Box:StateDir", _state)
            .UseSetting("Box:RescanMinutes", "0")
            .UseSetting("Box:RateLimit:RequestsPerSecond", "0.01")
            .UseSetting("Box:RateLimit:Burst", Burst.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .UseSetting("Box:RateLimit:TrustForwardedFor", "true"));
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _box.DisposeAsync();
        Directory.Delete(_media, recursive: true);
        Directory.Delete(_state, recursive: true);
    }

    private static HttpClient From(WebApplicationFactory<Program> box, string address)
    {
        var client = box.CreateClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-For", address);
        return client;
    }

    [Fact]
    public async Task A_client_past_its_burst_is_told_to_retry_later()
    {
        using var phone = From(_box, "192.168.1.20");
        for (var i = 0; i < Burst; i++)
            Assert.Equal(HttpStatusCode.OK, (await phone.GetAsync("/api/health")).StatusCode);

        var refused = await phone.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.True(refused.Headers.RetryAfter?.Delta >= TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Static_files_and_media_count_too()
    {
        using var phone = From(_box, "192.168.1.21");
        for (var i = 0; i < Burst; i++) await phone.GetAsync("/remote");

        Assert.Equal(HttpStatusCode.TooManyRequests, (await phone.GetAsync("/media/anything")).StatusCode);
    }

    [Fact]
    public async Task Each_client_has_its_own_allowance()
    {
        using var greedy = From(_box, "192.168.1.22");
        for (var i = 0; i <= Burst; i++) await greedy.GetAsync("/api/health");

        using var other = From(_box, "192.168.1.23");
        Assert.Equal(HttpStatusCode.OK, (await other.GetAsync("/api/health")).StatusCode);
    }

    [Fact]
    public async Task The_box_itself_is_never_limited()
    {
        using var screen = From(_box, "127.0.0.1");
        for (var i = 0; i < Burst * 3; i++)
            Assert.Equal(HttpStatusCode.OK, (await screen.GetAsync("/api/health")).StatusCode);
    }

    [Fact]
    public async Task Zero_requests_per_second_turns_limiting_off()
    {
        using var phone = From(_box.WithWebHostBuilder(host => host
            .UseSetting("Box:RateLimit:RequestsPerSecond", "0")), "192.168.1.24");
        for (var i = 0; i < Burst * 3; i++)
            Assert.Equal(HttpStatusCode.OK, (await phone.GetAsync("/api/health")).StatusCode);
    }

    [Fact]
    public async Task Hub_calls_over_a_websocket_count_against_the_same_allowance()
    {
        var socket = _box.Server.CreateWebSocketClient();
        socket.ConfigureRequest = request => request.Headers["X-Forwarded-For"] = "192.168.1.25";
        await using var remote = new HubConnectionBuilder()
            .WithUrl(new Uri(_box.Server.BaseAddress, "hub/tv"), options =>
            {
                options.Transports = HttpTransportType.WebSockets;
                options.SkipNegotiation = true;
                options.WebSocketFactory = async (context, cancel) => await socket.ConnectAsync(context.Uri, cancel);
            })
            .Build();
        await remote.StartAsync();

        // Opening the socket was one request; the rest of the burst goes on presses.
        for (var i = 1; i < Burst; i++) await remote.InvokeAsync("ChannelUp");

        var refused = await Assert.ThrowsAsync<HubException>(() => remote.InvokeAsync("ChannelUp"));
        Assert.Contains("Too many requests", refused.Message);
    }

    [Theory]
    [InlineData(null, "unknown")]
    [InlineData("192.168.1.20", "192.168.1.20")]
    [InlineData("::ffff:192.168.1.20", "192.168.1.20")]
    [InlineData("2001:db8:1:2:aaaa:bbbb:cccc:dddd", "2001:db8:1:2::/64")]
    [InlineData("2001:db8:1:2::1", "2001:db8:1:2::/64")]
    public void Clients_are_counted_by_address_and_IPv6_by_network(string? address, string key) =>
        Assert.Equal(key, ClientRateLimit.ClientKey(address is null ? null : IPAddress.Parse(address)));
}
