using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Channels;
using RabbitEars.Core;

namespace RabbitEars.Api;

/// <summary>
/// Keeps the box's screen connected: finds mpv's control socket, hands it to
/// the <see cref="MpvScreen"/>, and feeds that one thing at a time from a
/// single queue of snapshots, mpv events and clock ticks. When mpv goes away
/// (the kiosk launcher restarts it) this waits for the new one and carries on.
/// </summary>
public sealed class MpvPlayer(BoxOptions options, TvService tv, MpvScreen screen, ILogger<MpvPlayer> log)
    : BackgroundService
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(200);

    private readonly Channel<object> _inbox = Channel.CreateUnbounded<object>(new UnboundedChannelOptions { SingleReader = true });

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.UsesMpv) return;

        var socket = options.Player.MpvSocket;
        tv.HomeChanged += snapshot => _inbox.Writer.TryWrite(snapshot);
        var waiting = false;

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                MpvConnection mpv;
                try
                {
                    mpv = await MpvConnection.Open(socket, message => _inbox.Writer.TryWrite(message), stoppingToken);
                }
                catch (SocketException ex)
                {
                    // A socket that exists but refuses us is a permissions problem, not mpv still starting.
                    if (!waiting)
                    {
                        log.Log(
                            ex.SocketErrorCode == SocketError.AccessDenied ? LogLevel.Warning : LogLevel.Information,
                            "Waiting for mpv to open {Socket}: {Reason}", socket, ex.Message);
                    }
                    waiting = true;
                    await Task.Delay(RetryDelay, stoppingToken);
                    continue;
                }

                waiting = false;
                log.LogInformation("Connected to mpv.");
                await using (mpv)
                {
                    try
                    {
                        await Drive(mpv, stoppingToken);
                    }
                    catch (Exception ex) when (ex is IOException or SocketException or TimeoutException or ObjectDisposedException)
                    {
                        log.LogWarning("Lost mpv: {Reason}", ex.Message);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        // Whatever went wrong, the TV must keep going: start over with a fresh connection.
                        log.LogError(ex, "Driving mpv failed; reconnecting.");
                    }
                    finally
                    {
                        screen.Detach();
                    }
                }
                log.LogInformation("mpv went away; waiting for it to come back.");
                await Task.Delay(RetryDelay, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task Drive(MpvConnection mpv, CancellationToken stoppingToken)
    {
        using var connected = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        _ = mpv.Closed.ContinueWith(_ => _inbox.Writer.TryWrite(new Gone(mpv)), TaskScheduler.Default);
        _ = Ticks(connected.Token);
        try
        {
            await screen.Attach(mpv);
            await foreach (var item in _inbox.Reader.ReadAllAsync(stoppingToken))
            {
                switch (item)
                {
                    case Gone gone when gone.Connection == mpv:
                        return;
                    case TvSnapshot snapshot:
                        await screen.Show(snapshot);
                        break;
                    case JsonElement message:
                        await screen.Hear(message);
                        break;
                    case Tick:
                        await screen.Tick();
                        break;
                }
            }
        }
        finally
        {
            await connected.CancelAsync();
        }
    }

    private async Task Ticks(CancellationToken cancel)
    {
        using var timer = new PeriodicTimer(TickInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancel)) _inbox.Writer.TryWrite(Tick.Instance);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private sealed record Gone(MpvConnection Connection);

    private sealed class Tick
    {
        public static readonly Tick Instance = new();
    }
}
