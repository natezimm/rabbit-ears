using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json;
using RabbitEars.Desktop;

if (args.Contains("--help"))
{
    Console.WriteLine("""
        Rabbit Ears Desktop
        RabbitEars.Desktop [--config FILE] [--media FOLDER] [--state FOLDER]
                          [--port 5080] [--mpv FILE] [--ffprobe FILE] [--lan | --local]
        Mac profile: ~/Library/Application Support/RabbitEars/desktop.json
        Windows profile: %LOCALAPPDATA%/RabbitEars/desktop.json
        Default media: ~/Movies/RabbitEars (Mac), ~/Videos/RabbitEars (Windows).
        Settings: command line > RABBITEARS_* environment > profile JSON > defaults.
        Close the TV window or press q to quit. f toggles full screen; p is standby.
        """);
    return 0;
}
if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("This desktop launcher supports macOS and Windows. Use the existing Linux installer on the box.");
    return 1;
}
using var shutdown = new CancellationTokenSource();
using var term = OperatingSystem.IsWindows() ? null : PosixSignalRegistration.Create(PosixSignal.SIGTERM, context => { context.Cancel = true; shutdown.Cancel(); });
using var hangup = OperatingSystem.IsWindows() ? null : PosixSignalRegistration.Create(PosixSignal.SIGHUP, context => { context.Cancel = true; shutdown.Cancel(); });
Console.CancelKeyPress += (_, e) => { e.Cancel = true; shutdown.Cancel(); };
try
{
    var options = DesktopOptions.Load(args, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Environment.GetEnvironmentVariable);
    return await DesktopSession.Run(options, Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "app")), shutdown.Token);
}
catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { return 0; }
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or JsonException or Win32Exception)
{
    Console.Error.WriteLine($"Rabbit Ears: {ex.Message}");
    return 1;
}
