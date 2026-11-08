using System.Collections.Concurrent;
using System.Net.Sockets;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace RabbitEars.Api;

/// <summary>Something that runs mpv commands. The real one uses a socket or pipe; tests use a stand-in.</summary>
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

    private readonly Stream _stream;
    private readonly CancellationTokenSource _closed = new();
    private readonly SemaphoreSlim _writing = new(1, 1);
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement?>> _waiting = new();
    private long _lastId;

    private MpvConnection(Stream stream, Action<JsonElement> onEvent)
    {
        _stream = stream;
        Closed = Read(onEvent);
    }

    /// <summary>Completes when mpv closes the socket or quits.</summary>
    public Task Closed { get; }

    /// <summary>
    /// Connects to the local socket or Windows pipe. A missing pipe times out after one second.
    /// <paramref name="onEvent"/> runs on the reading thread, so it must not wait on a command.
    /// </summary>
    public static async Task<MpvConnection> Open(string socketPath, Action<JsonElement> onEvent, CancellationToken cancel)
    {
        cancel.ThrowIfCancellationRequested();
        if (OperatingSystem.IsWindows())
        {
            // mpv sets the pipe owner to TokenUser. .NET CurrentUserOnly compares TokenOwner,
            // which can be Administrators when elevated; never offer client impersonation.
            var pipe = new NamedPipeClientStream(".", MpvEndpoint.PipeName(socketPath), PipeDirection.InOut,
                PipeOptions.Asynchronous, TokenImpersonationLevel.Anonymous);
            try
            {
                await pipe.ConnectAsync(1000, cancel);
                return new MpvConnection(pipe, onEvent);
            }
            catch { pipe.Dispose(); throw; }
        }
        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            await socket.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), cancel);
            return new MpvConnection(new NetworkStream(socket, ownsSocket: true), onEvent);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    public async Task<JsonElement?> Request(object command)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_closed.Token);
        deadline.CancelAfter(ReplyTimeout);
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

            await _writing.WaitAsync(deadline.Token);
            try
            {
                await _stream.WriteAsync(line, deadline.Token);
                await _stream.WriteAsync("\n"u8.ToArray(), deadline.Token);
            }
            finally
            {
                _writing.Release();
            }

            return await answer.Task.WaitAsync(deadline.Token);
        }
        catch (OperationCanceledException) when (_closed.IsCancellationRequested)
        { throw new IOException("mpv closed its control endpoint."); }
        catch (OperationCanceledException)
        { throw new TimeoutException("mpv did not answer within five seconds."); }
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
            _closed.Cancel();
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
        _closed.Cancel();
        await _stream.DisposeAsync();
        await Closed;
        // Requests may still be unwinding their semaphore lease after cancellation.
    }
}
