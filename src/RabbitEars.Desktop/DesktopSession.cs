using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;

namespace RabbitEars.Desktop;

/// <summary>Owns one API/player pair; closing the player ends the desktop session.</summary>
public static class DesktopSession
{
    public static async Task<int> Run(DesktopOptions options, string appDirectory, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(options.StateDir);
        // Keep the lock file after exit: unlinking a locked inode permits a second owner.
        using var profile = LockProfile(options.StateDir);
        CheckPort(options.Port, options.Lan);
        var apiPath = Path.Combine(appDirectory, "RabbitEars.Api");
        var configPath = Path.Combine(appDirectory, "mpv");
        if (!File.Exists(apiPath) || !File.Exists(Path.Combine(configPath, "input-desktop.conf"))
            || !File.Exists(Path.Combine(appDirectory, "wwwroot", "index.html")))
            throw new FileNotFoundException("Incomplete desktop package. Build it with scripts/publish-mac.sh.");
        var logs = Path.Combine(options.StateDir, "logs");
        Directory.CreateDirectory(logs);
        // macOS temp paths can exceed sockaddr_un's limit; use a short, private directory.
        var socketDirectory = Path.Combine("/tmp", "re-" + Guid.NewGuid().ToString("N"));
        if (OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("This launcher currently supports macOS.");
        Directory.CreateDirectory(socketDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var socket = Path.Combine(socketDirectory, "mpv.sock");
        try
        {
            await using var api = new OwnedProcess(ApiStart(options, apiPath, socket), Path.Combine(logs, "api"));
            await WaitForApi(api.Process, options.Port, socket, cancellationToken);
            await using var mpv = new OwnedProcess(MpvStart(options.Mpv, configPath, socket), Path.Combine(logs, "mpv"));
            Console.WriteLine($"Rabbit Ears: http://127.0.0.1:{options.Port}/remote\nChannels: http://127.0.0.1:{options.Port}/admin");
            Console.WriteLine(options.Lan
                ? $"Phone remote: http://<your Mac's LAN address>:{options.Port}/remote (home network only)."
                : "Local access only. Use --lan to enable your phone remote on your home network.");
            Console.WriteLine($"State and logs: {options.StateDir}\nClose the TV window, press q in mpv, or Ctrl+C here to quit.");
            var apiExit = api.Process.WaitForExitAsync(cancellationToken);
            var mpvExit = mpv.Process.WaitForExitAsync(cancellationToken);
            var completed = await Task.WhenAny(apiExit, mpvExit);
            await completed;
            if (completed == apiExit) throw new IOException($"The API exited unexpectedly. See {logs}.");
            if (mpv.Process.ExitCode != 0) throw new IOException($"mpv exited with code {mpv.Process.ExitCode}. See {logs}.");
            return 0;
        }
        finally { Directory.Delete(socketDirectory, recursive: true); }
    }

    public static FileStream LockProfile(string directory)
    {
        try { return new FileStream(Path.Combine(directory, "desktop.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException ex) { throw new IOException($"Cannot lock profile {directory}. Another Rabbit Ears session may be running.", ex); }
    }

    public static void CheckPort(int port, bool lan)
    {
        using var listener = new TcpListener(lan ? IPAddress.Any : IPAddress.Loopback, port);
        try { listener.Start(); }
        catch (SocketException ex) { throw new IOException($"Port {port} is unavailable. Choose another with --port; no existing service was stopped.", ex); }
    }

    public static ProcessStartInfo ApiStart(DesktopOptions options, string executable, string socket)
    {
        var start = new ProcessStartInfo(executable) { WorkingDirectory = Path.GetDirectoryName(executable)! };
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
        start.Environment["DOTNET_ENVIRONMENT"] = "Production";
        string[] args = ["--urls", $"http://{(options.Lan ? "0.0.0.0" : "127.0.0.1")}:{options.Port}",
            "--Box:MediaRoot", options.MediaRoot, "--Box:StateDir", options.StateDir,
            "--Box:Ffprobe", options.Ffprobe, "--Box:Player:Kind", "mpv", "--Box:Player:MpvSocket", socket,
            "--Box:Demo:Enabled", "false", "--Box:Tv:PowerOffAtZeroVolume", "false"];
        foreach (var arg in args) start.ArgumentList.Add(arg);
        return start;
    }

    public static ProcessStartInfo MpvStart(string executable, string configPath, string socket)
    {
        var start = new ProcessStartInfo(executable);
        // CLI overrides leave the Linux kiosk's full-screen settings and keys unchanged.
        foreach (var arg in new[] { $"--config-dir={configPath}", $"--input-ipc-server={socket}",
            "--fullscreen=no", "--input-terminal=no", "--input-conf=" + Path.Combine(configPath, "input-desktop.conf") })
            start.ArgumentList.Add(arg);
        return start;
    }

    private static async Task WaitForApi(Process api, int port, string socket, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(60));
        using var http = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(2) };
        try
        {
            while (true)
            {
                deadline.Token.ThrowIfCancellationRequested();
                if (api.HasExited) throw new IOException("The API exited during startup. Check the profile's logs folder.");
                try
                {
                    // The random socket identifies our process, not another service racing for the port.
                    var player = await http.GetFromJsonAsync<JsonElement>($"http://127.0.0.1:{port}/api/player", deadline.Token);
                    if (player.GetProperty("socket").GetString() == socket && player.GetProperty("kind").GetString() == "mpv"
                        && !api.HasExited) return;
                }
                catch (Exception ex) when (ex is HttpRequestException or JsonException or KeyNotFoundException
                    || ex is OperationCanceledException && !deadline.IsCancellationRequested) { }
                await Task.Delay(200, deadline.Token);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new IOException("The API did not become ready in 60 seconds. Check the profile's logs folder."); }
    }
}
