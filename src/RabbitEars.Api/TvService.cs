using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading.Channels;
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
/// <remarks>
/// In demo mode the same service runs many televisions, one per visitor, each
/// addressed by a room id the browser makes up. Strangers on a public website
/// then cannot change each other's channel, and nothing is saved to disk.
/// </remarks>
public sealed partial class TvService : BackgroundService
{
    /// <summary>The room every client is in when demo mode is off.</summary>
    public const string HomeRoom = "home";

    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan SaveInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan GoodbyeDelay = TimeSpan.FromSeconds(2);

    private readonly Lock _gate = new();
    private readonly BoxOptions _options;
    private readonly LibraryScanner _scanner;
    private readonly StateFile _stateFile;
    private readonly IHubContext<TvHub> _hub;
    private readonly ILogger<TvService> _log;
    private readonly TimeProvider _clock;
    private readonly IEpisodeProbe? _probe;
    private readonly Dictionary<string, Room> _rooms = [];

    // Episode lengths found by probing, handed to every television including ones made later.
    private readonly Dictionary<string, double> _lengths = [];
    private readonly Channel<bool> _rescanned = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

    private Library _library;
    private bool _unsaved;
    private bool _shuttingDown;

    public TvService(
        BoxOptions options,
        LibraryScanner scanner,
        StateFile stateFile,
        IHubContext<TvHub> hub,
        ILogger<TvService> log,
        TimeProvider? clock = null,
        IEpisodeProbe? probe = null)
    {
        _options = options;
        _scanner = scanner;
        _stateFile = stateFile;
        _hub = hub;
        _log = log;
        _clock = clock ?? TimeProvider.System;
        _probe = probe;

        _library = scanner.Scan();
        if (!Demo)
        {
            _rooms[HomeRoom] = new Room(new Television(options.Tv, _library.Channels, stateFile.Load(), clock: _clock), Now);
        }
    }

    private bool Demo => _options.Demo.Enabled;

    /// <summary>
    /// The home television changed. Raised after every broadcast, from whichever
    /// thread pressed the button, for the box's own screen when mpv draws it.
    /// </summary>
    public event Action<TvSnapshot>? HomeChanged;

    private DateTimeOffset Now => _clock.GetUtcNow();

    /// <summary>
    /// Finds the television a new connection belongs to, making one if need be,
    /// and counts the connection so the room is not cleared away under it.
    /// Returns null when the connection should be refused.
    /// </summary>
    public string? Join(string? requested)
    {
        if (!Demo)
        {
            lock (_gate) _rooms[HomeRoom].Connections++;
            return HomeRoom;
        }

        if (requested is null || !RoomId().IsMatch(requested)) return null;
        lock (_gate)
        {
            if (!_rooms.TryGetValue(requested, out var room))
            {
                if (_rooms.Count >= _options.Demo.MaxTvs && !EvictOldestEmptyRoom()) return null;
                room = new Room(new Television(_options.Tv, _library.Channels, clock: _clock), Now);
                foreach (var (id, seconds) in _lengths) room.Tv.SetDuration(id, seconds);
                _rooms[requested] = room;
            }

            room.Connections++;
            room.LastActive = Now;
            return requested;
        }
    }

    public void Leave(string room)
    {
        lock (_gate)
        {
            if (!_rooms.TryGetValue(room, out var found)) return;
            found.Connections = Math.Max(0, found.Connections - 1);
            found.LastActive = Now;
        }
    }

    /// <summary>How many televisions are on; more than one only in demo mode.</summary>
    public int RoomCount
    {
        get { lock (_gate) return _rooms.Count; }
    }

    /// <summary>The state of the home television.</summary>
    public TvSnapshot Snapshot() =>
        Snapshot(HomeRoom) ?? throw new InvalidOperationException("Demo mode has no home television.");

    /// <summary>The state of one television, or null if no such room is on.</summary>
    public TvSnapshot? Snapshot(string room)
    {
        lock (_gate) return _rooms.GetValueOrDefault(room)?.Tv.Snapshot();
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
            var channels = _rooms.GetValueOrDefault(HomeRoom)?.Tv.Snapshot().Channels
                ?? [.. _library.Channels.Select(c => new ChannelListing(c.Number, c.Name, c.Episodes.Count, null))];
            return new LibraryReport(_options.MediaRoot, channels, _library.Files.Count, _library.NeedsConversion);
        }
    }

    /// <summary>Apply one remote press and, if it changed anything, tell everyone watching that TV.</summary>
    public Task Press(Action<Television> press, string room = HomeRoom)
    {
        TvSnapshot snapshot;
        lock (_gate)
        {
            if (!_rooms.TryGetValue(room, out var found)) return Task.CompletedTask;
            found.LastActive = Now;
            var before = found.Tv.Revision;
            press(found.Tv);
            if (found.Tv.Revision == before) return Task.CompletedTask;
            _unsaved = true;
            snapshot = found.Tv.Snapshot();
        }
        return Broadcast(room, snapshot);
    }

    /// <summary>Frequent and uninteresting to other clients, so nothing is broadcast.</summary>
    public void ReportPosition(string token, double seconds, double? duration = null, string room = HomeRoom)
    {
        lock (_gate)
        {
            if (!_rooms.TryGetValue(room, out var found)) return;
            found.Tv.ReportPosition(token, seconds, duration);
            _unsaved = true;
        }
    }

    public async Task Rescan()
    {
        var library = _scanner.Scan();
        string[] rooms;
        lock (_gate)
        {
            _library = library;
            rooms = [.. _rooms.Keys];
        }

        foreach (var room in rooms)
        {
            await Press(tv => tv.SetLineup(library.Channels), room);
        }
        _rescanned.Writer.TryWrite(true);
    }

    /// <summary>
    /// Find out how long each episode is that no television knows the length of
    /// yet, so channels can keep running while nobody watches them.
    /// </summary>
    public async Task ProbeLengths(CancellationToken cancellationToken)
    {
        if (_probe is null) return;
        KeyValuePair<string, string>[] unknown;
        lock (_gate)
        {
            var home = _rooms.GetValueOrDefault(HomeRoom)?.Tv;
            unknown = [.. _library.Files.Where(f => !_lengths.ContainsKey(f.Key) && home?.HasDuration(f.Key) != true)];
        }

        foreach (var (id, path) in unknown)
        {
            if (await _probe.Length(path, cancellationToken) is not { } seconds) continue;
            lock (_gate)
            {
                _lengths[id] = seconds;
                foreach (var room in _rooms.Values) room.Tv.SetDuration(id, seconds);
                _unsaved = true;
            }
        }
    }

    /// <summary>Probe once at start, then again whenever a rescan may have found new episodes.</summary>
    private async Task ProbeAfterScans(CancellationToken stoppingToken)
    {
        try
        {
            do
            {
                await ProbeLengths(stoppingToken);
            }
            while (await _rescanned.Reader.WaitToReadAsync(stoppingToken) && _rescanned.Reader.TryRead(out _));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var rescanEvery = _options.RescanMinutes > 0
            ? TimeSpan.FromMinutes(_options.RescanMinutes)
            : Timeout.InfiniteTimeSpan;
        var lastSave = Stopwatch.StartNew();
        var lastScan = Stopwatch.StartNew();
        var probing = Task.Run(() => ProbeAfterScans(stoppingToken), CancellationToken.None);

        using var timer = new PeriodicTimer(TickInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await TickAll();

                if (lastSave.Elapsed >= SaveInterval)
                {
                    if (Demo) ClearIdleRooms();
                    else Save();
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
        await probing;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        Save();
    }

    private async Task TickAll()
    {
        var changed = new List<(string Room, TvSnapshot Snapshot)>();
        lock (_gate)
        {
            foreach (var (name, room) in _rooms)
            {
                // Ticking is the clock moving, not a viewer, so it does not keep a room alive.
                var before = room.Tv.Revision;
                room.Tv.Tick();
                if (room.Tv.Revision == before) continue;
                _unsaved = true;
                changed.Add((name, room.Tv.Snapshot()));
            }
        }

        foreach (var (room, snapshot) in changed)
        {
            await Broadcast(room, snapshot);
        }
    }

    /// <summary>A visitor who closed the tab leaves a TV nobody can see; switch it off after a while.</summary>
    public void ClearIdleRooms()
    {
        var cutoff = Now - TimeSpan.FromMinutes(_options.Demo.IdleMinutes);
        lock (_gate)
        {
            foreach (var (name, room) in _rooms.ToList())
            {
                if (room.Connections == 0 && room.LastActive < cutoff) _rooms.Remove(name);
            }
        }
    }

    /// <summary>Makes space for a new visitor by switching off the TV nobody has watched for longest.</summary>
    private bool EvictOldestEmptyRoom()
    {
        var oldest = _rooms
            .Where(pair => pair.Value.Connections == 0)
            .OrderBy(pair => pair.Value.LastActive)
            .Select(pair => pair.Key)
            .FirstOrDefault();
        return oldest is not null && _rooms.Remove(oldest);
    }

    private async Task Broadcast(string room, TvSnapshot snapshot)
    {
        if (Demo)
        {
            await _hub.Clients.Group(room).SendAsync("State", snapshot);
            return;
        }

        await _hub.Clients.All.SendAsync("State", snapshot);
        HomeChanged?.Invoke(snapshot);
        if (snapshot.PoweredOff) BeginShutdown();
    }

    private void Save()
    {
        // Demo televisions belong to passing visitors; there is nothing worth keeping.
        if (Demo) return;

        SavedState state;
        lock (_gate)
        {
            if (!_unsaved) return;
            _unsaved = false;
            state = _rooms[HomeRoom].Tv.Save();
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

    /// <summary>Room ids are made up by the browser; keep them short and plain.</summary>
    [GeneratedRegex("^[A-Za-z0-9_-]{6,40}$")]
    private static partial Regex RoomId();

    private sealed class Room(Television tv, DateTimeOffset now)
    {
        public Television Tv { get; } = tv;
        public int Connections { get; set; }
        public DateTimeOffset LastActive { get; set; } = now;
    }
}
