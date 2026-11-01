using Microsoft.Extensions.Logging.Abstractions;
using RabbitEars.Api;

namespace RabbitEars.Tests;

public sealed class EpisodeProbeTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("rabbitears-probe-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    /// <summary>A stand-in ffprobe: a shell script that writes its process id, then runs <paramref name="body"/>.</summary>
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    private FfprobeEpisodeProbe Probe(string body)
    {
        var script = Path.Combine(_dir, "ffprobe");
        File.WriteAllText(script, $"#!/bin/sh\necho $$ > \"{Path.Combine(_dir, "pid")}\"\n{body}\n");
        File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return new FfprobeEpisodeProbe(new BoxOptions { Ffprobe = script }, NullLogger<FfprobeEpisodeProbe>.Instance);
    }

    private async Task<int> StartedPid()
    {
        var file = Path.Combine(_dir, "pid");
        for (var i = 0; i < 200; i++)
        {
            if (File.Exists(file) && int.TryParse(File.ReadAllText(file).Trim(), out var pid)) return pid;
            await Task.Delay(25);
        }
        throw new TimeoutException("The fake ffprobe never started.");
    }

    private static bool Running(int pid)
    {
        // A killed child that nobody has reaped yet still has an entry; only count live ones.
        var stat = $"/proc/{pid}/stat";
        if (!File.Exists(stat)) return false;
        try { return File.ReadAllText(stat).Split(") ")[1][0] != 'Z'; }
        catch (IOException) { return false; }
    }

    [Fact]
    public async Task A_cancelled_probe_does_not_leave_ffprobe_running()
    {
        if (!OperatingSystem.IsLinux()) return;
        var probe = Probe("exec sleep 60");
        using var cancel = new CancellationTokenSource();

        var length = probe.Length("episode.mp4", cancel.Token);
        var pid = await StartedPid();
        cancel.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => length);
        Assert.False(Running(pid));
    }

    [Fact]
    public async Task A_finished_probe_reads_the_length()
    {
        if (!OperatingSystem.IsLinux()) return;
        var probe = Probe("echo 1312.5");

        Assert.Equal(1312.5, await probe.Length("episode.mp4", CancellationToken.None));
    }
}
