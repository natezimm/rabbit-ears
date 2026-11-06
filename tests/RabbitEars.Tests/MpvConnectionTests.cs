using System.Net.Sockets;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using RabbitEars.Api;

namespace RabbitEars.Tests;

/// <summary>The same protocol assertions run over native Windows pipes or Unix sockets.</summary>
public sealed class MpvConnectionTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("ears-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string SocketPath => OperatingSystem.IsWindows()
        ? MpvEndpoint.PipePrefix + Path.GetFileName(_dir)
        : Path.Combine(_dir, "mpv.sock");

    private Listener Listen() => new(SocketPath);

    private sealed class Listener : IDisposable
    {
        private readonly Socket? _socket;
        private readonly NamedPipeServerStream? _pipe;
        public Listener(string endpoint)
        {
            if (OperatingSystem.IsWindows())
                _pipe = new NamedPipeServerStream(MpvEndpoint.PipeName(endpoint), PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            else
            {
                _socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                _socket.Bind(new UnixDomainSocketEndPoint(endpoint));
                _socket.Listen();
            }
        }
        public async Task<Stream> AcceptAsync()
        {
            if (_pipe is not null) { await _pipe.WaitForConnectionAsync(); return _pipe; }
            return new NetworkStream(await _socket!.AcceptAsync(), ownsSocket: true);
        }
        public void Dispose() { _pipe?.Dispose(); _socket?.Dispose(); }
    }

    [Fact]
    public async Task Answers_come_back_to_the_command_that_asked_and_events_go_to_the_callback()
    {
        using var listener = Listen();
        var events = new List<string>();
        var connecting = MpvConnection.Open(SocketPath, e => events.Add(e.GetProperty("event").GetString()!), default);
        using var mpv = await listener.AcceptAsync();
        await using var connection = await connecting;

        var asking = connection.Request(new object[] { "get_property", "volume" });
        var request = JsonDocument.Parse(await ReadLine(mpv)).RootElement;
        Assert.Equal("""["get_property","volume"]""", request.GetProperty("command").GetRawText());
        var id = request.GetProperty("request_id").GetInt64();

        // An event can arrive before the answer; it must not be taken for it.
        await Send(mpv, """{"event":"idle"}""");
        await Send(mpv, $$"""{"data":60.0,"request_id":{{id}},"error":"success"}""");

        Assert.Equal(60.0, (await asking)!.Value.GetDouble());
        Assert.Equal(["idle"], events);
    }

    [Fact]
    public async Task A_refused_command_answers_null()
    {
        using var listener = Listen();
        var connecting = MpvConnection.Open(SocketPath, _ => { }, default);
        using var mpv = await listener.AcceptAsync();
        await using var connection = await connecting;

        var asking = connection.Request(new object[] { "get_property", "time-pos" });
        var id = JsonDocument.Parse(await ReadLine(mpv)).RootElement.GetProperty("request_id").GetInt64();
        await Send(mpv, $$"""{"request_id":{{id}},"error":"property unavailable"}""");

        Assert.Null(await asking);
    }

    [Fact]
    public async Task When_mpv_quits_the_connection_closes_and_waiting_commands_fail()
    {
        using var listener = Listen();
        var connecting = MpvConnection.Open(SocketPath, _ => { }, default);
        var mpv = await listener.AcceptAsync();
        await using var connection = await connecting;

        var asking = connection.Request(new object[] { "get_property", "path" });
        await ReadLine(mpv);
        mpv.Dispose();

        await connection.Closed.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAsync<IOException>(() => asking);
    }

    [Fact]
    public async Task No_mpv_running_is_a_socket_error_to_retry()
    {
        if (OperatingSystem.IsWindows())
            await Assert.ThrowsAsync<TimeoutException>(() => MpvConnection.Open(SocketPath, _ => { }, default));
        else
            await Assert.ThrowsAnyAsync<SocketException>(() => MpvConnection.Open(SocketPath, _ => { }, default));
    }

    [Fact]
    public async Task Concurrent_answers_are_correlated_even_when_reversed()
    {
        using var listener = Listen();
        var opening = MpvConnection.Open(SocketPath, _ => { }, default);
        using var peer = await listener.AcceptAsync();
        await using var connection = await opening;
        var first = connection.Request(new[] { "get_property", "volume" });
        var firstId = JsonDocument.Parse(await ReadLine(peer)).RootElement.GetProperty("request_id").GetInt64();
        var second = connection.Request(new[] { "get_property", "time-pos" });
        var secondId = JsonDocument.Parse(await ReadLine(peer)).RootElement.GetProperty("request_id").GetInt64();
        await Send(peer, $$"""{"data":12,"request_id":{{secondId}},"error":"success"}""");
        await Send(peer, $$"""{"data":65,"request_id":{{firstId}},"error":"success"}""");
        Assert.Equal(65, (await first)!.Value.GetInt32());
        Assert.Equal(12, (await second)!.Value.GetInt32());
    }

    [Fact]
    public async Task Disposal_completes_a_waiting_request_and_allows_a_new_connection()
    {
        using (var listener = Listen())
        {
            var opening = MpvConnection.Open(SocketPath, _ => { }, default);
            using var peer = await listener.AcceptAsync();
            var connection = await opening;
            var pending = connection.Request(new[] { "get_property", "path" });
            await ReadLine(peer);
            await connection.DisposeAsync();
            await Assert.ThrowsAsync<IOException>(() => pending);
            await Assert.ThrowsAsync<IOException>(() => connection.Request(new[] { "stop" }));
        }
        if (!OperatingSystem.IsWindows()) File.Delete(SocketPath);
        using var nextListener = Listen();
        var nextOpening = MpvConnection.Open(SocketPath, _ => { }, default);
        using var nextPeer = await nextListener.AcceptAsync();
        await using var next = await nextOpening;
        Assert.False(next.Closed.IsCompleted);
    }

    [Fact]
    public async Task Connecting_honors_cancellation()
    {
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => MpvConnection.Open(SocketPath, _ => { }, cancel.Token));
    }

    private static async Task<string> ReadLine(Stream socket)
    {
        var line = new StringBuilder();
        var buffer = new byte[1];
        while (await socket.ReadAsync(buffer) == 1 && buffer[0] != '\n') line.Append((char)buffer[0]);
        return line.ToString();
    }

    private static Task Send(Stream socket, string line) => socket.WriteAsync(Encoding.UTF8.GetBytes(line + "\n")).AsTask();
}
