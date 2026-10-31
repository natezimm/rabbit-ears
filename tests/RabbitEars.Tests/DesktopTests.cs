using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using RabbitEars.Desktop;

namespace RabbitEars.Tests;

/// <summary>Desktop profiles and ownership without requiring a screen or a media installation.</summary>
public sealed class DesktopTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("ears-desktop-").FullName;
    public void Dispose() => Directory.Delete(_root, recursive: true);

    private DesktopOptions Load(string[]? args = null, Dictionary<string, string>? env = null)
    {
        env ??= [];
        env.TryAdd("RABBITEARS_MPV", "/bin/sh");
        env.TryAdd("RABBITEARS_FFPROBE", "/bin/sh");
        return DesktopOptions.Load(args ?? [], _root, key => env.GetValueOrDefault(key));
    }

    [Fact]
    public void Defaults_use_user_folders_and_only_listen_locally()
    {
        var options = Load();
        Assert.Equal(Path.Combine(_root, "Movies", "RabbitEars"), options.MediaRoot);
        Assert.True(Directory.Exists(options.MediaRoot));
        Assert.Equal(DesktopOptions.DefaultProfile(_root), options.StateDir);
        Assert.Equal(5080, options.Port);
        Assert.False(options.Lan);
    }

    [Fact]
    public void Command_line_overrides_environment_which_overrides_profile()
    {
        var config = Path.Combine(_root, "desktop.json");
        File.WriteAllText(config, """{"port":5090,"lan":true,"state":"saved télévision"}""");
        var env = new Dictionary<string, string> { ["RABBITEARS_PORT"] = "5091", ["RABBITEARS_CONFIG"] = config };
        var options = Load(["--port", "5092", "--local"], env);
        Assert.Equal(5092, options.Port);
        Assert.False(options.Lan);
        Assert.Equal(Path.Combine(_root, "saved télévision"), options.StateDir);
        Assert.Equal(5091, Load(env: env).Port);
        env.Remove("RABBITEARS_PORT");
        Assert.Equal(5090, Load(env: env).Port);
    }

    [Fact]
    public void Existing_box_path_overrides_are_honored()
    {
        var media = Directory.CreateDirectory(Path.Combine(_root, "Shows & films 日本語")).FullName;
        var options = Load(env: new() { ["Box__MediaRoot"] = media, ["Box__StateDir"] = Path.Combine(_root, "state") });
        Assert.Equal(media, options.MediaRoot);
        var start = DesktopSession.ApiStart(options, "/app/RabbitEars.Api", "/tmp/socket");
        Assert.Contains(media, start.ArgumentList);
        Assert.Contains("http://127.0.0.1:5080", start.ArgumentList);
        Assert.Contains("--Box:Tv:PowerOffAtZeroVolume", start.ArgumentList);
        Assert.DoesNotContain("systemctl", start.ArgumentList);
        Assert.Empty(start.Arguments);
    }

    [Theory]
    [InlineData("--port", "0")]
    [InlineData("--port", "65536")]
    [InlineData("--port", "-1")]
    [InlineData("--unknown", "value")]
    public void Invalid_arguments_fail_before_starting_processes(string key, string value) =>
        Assert.Throws<ArgumentException>(() => Load([key, value]));

    [Fact]
    public void Missing_media_and_dependencies_are_actionable_errors()
    {
        Assert.Throws<DirectoryNotFoundException>(() => Load(["--media", Path.Combine(_root, "absent")]));
        var error = Assert.Throws<FileNotFoundException>(() => Load(["--mpv", Path.Combine(_root, "no mpv")]));
        Assert.Contains("--mpv", error.Message);
        Assert.Throws<FileNotFoundException>(() => Load(["--config", Path.Combine(_root, "missing.json")]));
    }

    [Fact]
    public void Invalid_profile_does_not_fall_back_to_defaults()
    {
        var config = Path.Combine(_root, "bad.json");
        File.WriteAllText(config, "{oops");
        Assert.ThrowsAny<JsonException>(() => Load(["--config", config]));
    }

    [Fact]
    public void Player_paths_are_arguments_not_shell_commands()
    {
        var start = DesktopSession.MpvStart("/Applications/My mpv", "/Users/Test user/日本語", "/tmp/re-test/mpv.sock");
        Assert.Equal("/Applications/My mpv", start.FileName);
        Assert.Contains("--input-conf=/Users/Test user/日本語/input-desktop.conf", start.ArgumentList);
        Assert.Contains("--fullscreen=no", start.ArgumentList);
        Assert.Empty(start.Arguments);
    }

    [Fact]
    public void Profile_can_only_have_one_owner_and_can_be_reopened_after_exit()
    {
        using (DesktopSession.LockProfile(_root))
            Assert.Throws<IOException>(() => DesktopSession.LockProfile(_root));
        using var reopened = DesktopSession.LockProfile(_root);
        Assert.True(reopened.CanWrite);
    }

    [Fact]
    public void Occupied_port_is_rejected_without_interrupting_the_listener()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        Assert.Throws<IOException>(() => DesktopSession.CheckPort(port, false));
        Assert.True(listener.Server.IsBound);
    }


    [Fact]
    public async Task Cancelling_during_startup_stops_the_API_and_releases_the_profile()
    {
        var app = FakePackage("trap 'echo saved > saved; exit 0' TERM\necho ready > ready\nwhile :; do sleep 0.1; done\n");
        var options = Load();
        using var cancel = new CancellationTokenSource();
        var running = DesktopSession.Run(options, app, cancel.Token);
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (!File.Exists(Path.Combine(app, "ready"))) await Task.Delay(20, timeout.Token);
        }
        finally { cancel.Cancel(); }
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
        Assert.True(File.Exists(Path.Combine(app, "saved")));
        using var reopened = DesktopSession.LockProfile(options.StateDir);
    }

    [Fact]
    public async Task API_startup_failure_is_reported_and_releases_the_profile()
    {
        var app = FakePackage("exit 17\n");
        var options = Load();
        var error = await Assert.ThrowsAsync<IOException>(() => DesktopSession.Run(options, app, default));
        Assert.Contains("API exited", error.Message);
        using var reopened = DesktopSession.LockProfile(options.StateDir);
    }

    private string FakePackage(string script)
    {
        var app = Directory.CreateDirectory(Path.Combine(_root, "app with spaces")).FullName;
        Directory.CreateDirectory(Path.Combine(app, "mpv"));
        Directory.CreateDirectory(Path.Combine(app, "wwwroot"));
        File.WriteAllText(Path.Combine(app, "mpv", "input-desktop.conf"), "");
        File.WriteAllText(Path.Combine(app, "wwwroot", "index.html"), "");
        var executable = Path.Combine(app, "RabbitEars.Api");
        File.WriteAllText(executable, "#!/bin/sh\n" + script);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return app;
    }

    [Fact]
    public async Task Stopping_an_owned_process_allows_its_shutdown_handler_to_save()
    {
        var ready = Path.Combine(_root, "ready");
        var saved = Path.Combine(_root, "saved");
        var script = Path.Combine(_root, "child.sh");
        File.WriteAllText(script, "trap 'echo saved > \"$2\"; exit 0' TERM\necho ready > \"$1\"\nwhile :; do sleep 0.1; done\n");
        var start = new ProcessStartInfo("/bin/sh");
        foreach (var arg in new[] { script, ready, saved }) start.ArgumentList.Add(arg);
        await using (var child = new OwnedProcess(start, Path.Combine(_root, "child")))
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (!File.Exists(ready)) await Task.Delay(20, timeout.Token);
        }
        Assert.Equal("saved", File.ReadAllText(saved).Trim());
    }
}
