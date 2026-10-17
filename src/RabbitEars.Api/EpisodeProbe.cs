using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;

namespace RabbitEars.Api;

/// <summary>Finds out how long an episode is without playing it.</summary>
public interface IEpisodeProbe
{
    /// <summary>The length in seconds, or null if it cannot be told.</summary>
    Task<double?> Length(string path, CancellationToken cancellationToken);
}

/// <summary>
/// Asks ffprobe, which reads the length from the file's header without decoding
/// any video, so a whole library takes seconds. Without ffprobe installed it
/// says so once and answers null; episode lengths are then learned as they play.
/// </summary>
public sealed class FfprobeEpisodeProbe(BoxOptions options, ILogger<FfprobeEpisodeProbe> log) : IEpisodeProbe
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private bool _missing = string.IsNullOrWhiteSpace(options.Ffprobe);

    public async Task<double?> Length(string path, CancellationToken cancellationToken)
    {
        if (_missing) return null;

        var start = new ProcessStartInfo(options.Ffprobe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in (string[])
            ["-v", "error", "-show_entries", "format=duration", "-of", "default=noprint_wrappers=1:nokey=1", "-i", path])
        {
            start.ArgumentList.Add(argument);
        }

        Process process;
        try
        {
            process = Process.Start(start) ?? throw new Win32Exception();
        }
        catch (Win32Exception)
        {
            _missing = true;
            log.LogWarning("{Ffprobe} is not installed (sudo apt install ffmpeg); episode lengths will be learned as they play.", options.Ffprobe);
            return null;
        }

        using (process)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(Timeout);
            try
            {
                var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
                _ = process.StandardError.ReadToEndAsync(timeout.Token);
                await process.WaitForExitAsync(timeout.Token);
                var text = (await output).Trim();
                return process.ExitCode == 0
                    && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
                    && double.IsFinite(seconds) && seconds > 0
                    ? seconds
                    : null;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                process.Kill();
                log.LogWarning("ffprobe took too long on {Path}; skipping it.", path);
                return null;
            }
        }
    }
}
