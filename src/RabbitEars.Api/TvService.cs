using System.Diagnostics;
using Microsoft.AspNetCore.SignalR;
using RabbitEars.Core;

namespace RabbitEars.Api;

public sealed record LibraryReport(
    string MediaRoot,
    IReadOnlyList<ChannelListing> Channels,
    int Episodes,
    IReadOnlyList<string> NeedsConversion);

/// <summary>
/// The one television in the house. Owns the <see cref="Television"/>, lets one
/// remote press at a time through, tells every connected screen and remote what
/// changed, and keeps the state file and channel lineup fresh in the background.
/// </summary>
public sealed class TvService : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan SaveInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan GoodbyeDelay = TimeSpan.FromSeconds(2);

    private readonly Lock _gate = new();
    private readonly BoxOptions _options;
    private readonly LibraryScanner _scanner;
    private readonly StateFile _stateFile;
    private readonly IHubContext<TvHub> _hub;
    private readonly ILogger<TvService> _log;
    private readonly Television _tv;

    private Library _library;
    private bool _unsaved;
    private bool _shuttingDown;

    public TvService(
        BoxOptions options,
        LibraryScanner scanner,
        StateFile stateFile,
        IHubContext<TvHub> hub,
        ILogger<TvService> log)
    {
        _options = options;
        _scanner = scanner;
        _stateFile = stateFile;
        _hub = hub;
        _log = log;

        _library = scanner.Scan();
        _tv = new Television(options.Tv, _library.Channels, stateFile.Load());
    }

    public TvSnapshot Snapshot()
    {
        lock (_gate) return _tv.Snapshot();
    }

    /// <summary>The file behind an episode id, or null. Only scanned episodes resolve.</summary>
    public string? FindMedia(string episodeId)
    {
        lock (_gate) return _library.Files.GetValueOrDefault(episodeId);
    }

    public LibraryReport Report()
    {
        lock (_gate)
        {
            return new LibraryReport(
                _options.MediaRoot,
                _tv.Snapshot().Channels,
                _library.Files.Count,
                _library.NeedsConversion);
        }
    }

    /// <summary>Apply one remote press and, if it changed anything, tell everyone.</summary>
    public Task Press(Action<Television> press)
    {
        TvSnapshot snapshot;
        lock (_gate)
        {
            var before = _tv.Revision;
            press(_tv);
            if (_tv.Revision == before) return Task.CompletedTask;
            _unsaved = true;
            snapshot = _tv.Snapshot();
        }
        return Broadcast(snapshot);
    }

    /// <summary>Frequent and uninteresting to other clients, so nothing is broadcast.</summary>
    public void ReportPosition(string token, double seconds)
    {
        lock (_gate)
        {
            _tv.ReportPosition(token, seconds);
            _unsaved = true;
        }
    }

    public Task Rescan()
    {
        var library = _scanner.Scan();
        return Press(tv =>
        {
            _library = library;
            tv.SetLineup(library.Channels);
        });
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var rescanEvery = _options.RescanMinutes > 0
            ? TimeSpan.FromMinutes(_options.RescanMinutes)
            : Timeout.InfiniteTimeSpan;
        var lastSave = Stopwatch.StartNew();
        var lastScan = Stopwatch.StartNew();

        using var timer = new PeriodicTimer(TickInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await Press(tv => tv.Tick());

                if (lastSave.Elapsed >= SaveInterval)
                {
                    Save();
                    lastSave.Restart();
                }

                if (rescanEvery != Timeout.InfiniteTimeSpan && lastScan.Elapsed >= rescanEvery)
                {
                    await Rescan();
                    lastScan.Restart();
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        Save();
    }

    private async Task Broadcast(TvSnapshot snapshot)
    {
        await _hub.Clients.All.SendAsync("State", snapshot);
        if (snapshot.PoweredOff) BeginShutdown();
    }

    private void Save()
    {
        SavedState state;
        lock (_gate)
        {
            if (!_unsaved) return;
            _unsaved = false;
            state = _tv.Save();
        }
        _stateFile.Save(state);
    }

    private void BeginShutdown()
    {
        lock (_gate)
        {
            if (_shuttingDown) return;
            _shuttingDown = true;
        }

        Save();
        if (_options.PowerOffCommand.Length == 0)
        {
            _log.LogInformation("GOODBYE shown; no PowerOffCommand configured, so the box stays up.");
            return;
        }

        _ = Task.Run(async () =>
        {
            // Leave GOODBYE on screen for a moment before the lights go out.
            await Task.Delay(GoodbyeDelay);
            try
            {
                _log.LogInformation("Powering off: {Command}", string.Join(' ', _options.PowerOffCommand));
                Process.Start(_options.PowerOffCommand[0], _options.PowerOffCommand[1..]);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Power-off command failed.");
            }
        });
    }
}
