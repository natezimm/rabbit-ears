using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace RabbitEars.Api;

/// <summary>Something that runs mpv commands. The real one is a socket; tests use a stand-in.</summary>
public interface IMpvLink
{
    /// <summary>
    /// Runs one mpv command, given as the JSON array (or named-argument object)
    /// mpv expects, and waits for the answer. Returns the answer's data, which
    /// is <see cref="JsonValueKind.Undefined"/> when there is none, or null
    /// when mpv refused the command.
    /// </summary>
    Task<JsonElement?> Request(object command);
}

/// <summary>
/// mpv's JSON control socket: one JSON message per line each way. Answers are
/// matched to commands by request id; everything else mpv says is an event,
/// handed to the callback as it arrives.
/// </summary>
public sealed class MpvConnection : IMpvLink, IAsyncDisposable
{
    /// <summary>mpv answers in milliseconds; this long means it is wedged, and the box reconnects.</summary>
    private static readonly TimeSpan ReplyTimeout = TimeSpan.FromSeconds(5);

    private readonly Socket _socket;
    private readonly NetworkStream _stream;
    private readonly SemaphoreSlim _writing = new(1, 1);
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement?>> _waiting = new();
    private long _lastId;

    private MpvConnection(Socket socket, Action<JsonElement> onEvent)
    {
        _socket = socket;
        _stream = new NetworkStream(socket, ownsSocket: false);
        Closed = Read(onEvent);
    }

    /// <summary>Completes when mpv closes the socket or quits.</summary>
    public Task Closed { get; }

    /// <summary>
    /// Connects to mpv's socket. Throws <see cref="SocketException"/> while mpv is not running.
    /// <paramref name="onEvent"/> runs on the reading thread, so it must not wait on a command.
    /// </summary>
    public static async Task<MpvConnection> Open(string socketPath, Action<JsonElement> onEvent, CancellationToken cancel)
    {
        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            await socket.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), cancel);
            return new MpvConnection(socket, onEvent);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    public async Task<JsonElement?> Request(object command)
    {
        var id = Interlocked.Increment(ref _lastId);
        // Answers arrive on the reading thread; continuing elsewhere keeps that thread reading.
        var answer = new TaskCompletionSource<JsonElement?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _waiting[id] = answer;
        try
        {
            var line = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
            {
                ["command"] = command,
                ["request_id"] = id,
            });

            await _writing.WaitAsync();
            try
            {
                await _stream.WriteAsync(line);
                await _stream.WriteAsync("\n"u8.ToArray());
            }
            finally
            {
                _writing.Release();
            }

            return await answer.Task.WaitAsync(ReplyTimeout);
        }
        finally
        {
            _waiting.TryRemove(id, out _);
        }
    }

    private async Task Read(Action<JsonElement> onEvent)
    {
        try
        {
            using var reader = new StreamReader(_stream, Encoding.UTF8, leaveOpen: true);
            while (await reader.ReadLineAsync() is { } line)
            {
                if (Parse(line) is not { } message) continue;

                if (message.TryGetProperty("event", out _))
                {
                    onEvent(message);
                }
                else if (message.TryGetProperty("request_id", out var requestId)
                    && requestId.TryGetInt64(out var id)
                    && _waiting.TryGetValue(id, out var answer))
                {
                    var ok = message.TryGetProperty("error", out var error) && error.GetString() == "success";
                    answer.TrySetResult(ok ? message.TryGetProperty("data", out var data) ? data : default : null);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
        {
            // mpv went away mid-line; same as a clean close.
        }
        finally
        {
            foreach (var answer in _waiting.Values)
            {
                answer.TrySetException(new IOException("mpv closed its control socket."));
            }
        }
    }

    private static JsonElement? Parse(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        try
        {
            using var document = JsonDocument.Parse(line);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            _socket.Shutdown(SocketShutdown.Both);
        }
        catch (SocketException)
        {
            // Already gone from mpv's side.
        }
        _socket.Dispose();
        await Closed;
        await _stream.DisposeAsync();
        _writing.Dispose();
    }
}
