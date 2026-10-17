using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using RabbitEars.Api;

namespace RabbitEars.Tests;

/// <summary>The control socket, against a stand-in mpv listening on a real Unix socket.</summary>
public sealed class MpvConnectionTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("ears-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string SocketPath => Path.Combine(_dir, "mpv.sock");

    private Socket Listen()
    {
        var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        listener.Bind(new UnixDomainSocketEndPoint(SocketPath));
        listener.Listen();
        return listener;
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
        await Assert.ThrowsAnyAsync<SocketException>(() => MpvConnection.Open(SocketPath, _ => { }, default));
    }

    private static async Task<string> ReadLine(Socket socket)
    {
        var line = new StringBuilder();
        var buffer = new byte[1];
        while (await socket.ReceiveAsync(buffer) == 1 && buffer[0] != '\n') line.Append((char)buffer[0]);
        return line.ToString();
    }

    private static Task Send(Socket socket, string line) => socket.SendAsync(Encoding.UTF8.GetBytes(line + "\n"));
}
