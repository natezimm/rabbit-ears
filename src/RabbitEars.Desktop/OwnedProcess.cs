using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RabbitEars.Desktop;

/// <summary>Drains child output to bounded logs and gives owned processes time to save on exit.</summary>
public sealed class OwnedProcess : IAsyncDisposable
{
    public Process Process { get; }
    private readonly Task[] _output;

    public OwnedProcess(ProcessStartInfo start, string logPath)
    {
        start.UseShellExecute = false;
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        Process = Process.Start(start) ?? throw new IOException($"Could not start {start.FileName}.");
        _output = [Copy(Process.StandardOutput, logPath + ".out.log"), Copy(Process.StandardError, logPath + ".err.log")];
    }

    private static async Task Copy(StreamReader reader, string path)
    {
        // Keep one previous chunk; logs remain bounded even during a long session.
        const int limit = 1024 * 1024;
        var buffer = new char[4096];
        StreamWriter? writer = null;
        try
        {
            while (await reader.ReadAsync(buffer) is var count && count > 0)
            {
                if (writer is null || writer.BaseStream.Length >= limit)
                {
                    if (writer is not null) await writer.DisposeAsync();
                    if (File.Exists(path)) File.Move(path, path + ".previous", overwrite: true);
                    writer = new StreamWriter(path) { AutoFlush = true };
                }
                await writer.WriteAsync(buffer.AsMemory(0, count));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { // Keep draining so a full disk or changed permissions cannot deadlock the child.
            while (await reader.ReadAsync(buffer) > 0) { }
        }
        finally { if (writer is not null) await writer.DisposeAsync(); }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!Process.HasExited)
            {
                // SIGTERM lets ASP.NET run TvService.StopAsync and persist the television.
                if (!OperatingSystem.IsWindows()) Kill(Process.Id, 15);
                else Process.CloseMainWindow();
                try { await Process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
                catch (TimeoutException)
                {
                    if (!Process.HasExited) Process.Kill(entireProcessTree: true);
                    await Process.WaitForExitAsync();
                }
            }
            await Task.WhenAll(_output);
        }
        finally { Process.Dispose(); }
    }

    [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static extern int Kill(int pid, int signal);
}
