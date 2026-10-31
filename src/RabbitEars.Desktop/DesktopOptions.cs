using System.Globalization;
using System.Text.Json;

namespace RabbitEars.Desktop;

/// <summary>A user-owned desktop profile, independent of the installed release.</summary>
public sealed record DesktopOptions(string MediaRoot, string StateDir, string Mpv, string Ffprobe, int Port, bool Lan)
{
    public static string DefaultProfile(string home) => Path.Combine(home, "Library", "Application Support", "RabbitEars");

    /// <summary>Command line, environment, profile JSON, then desktop defaults.</summary>
    public static DesktopOptions Load(string[] args, string home, Func<string, string?> env)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i++)
        {
            var key = args[i];
            if (key is "--lan" or "--local") { values["lan"] = (key == "--lan").ToString(); continue; }
            if (key is not ("--config" or "--media" or "--state" or "--mpv" or "--ffprobe" or "--port")
                || ++i >= args.Length || args[i].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException($"Unknown option or missing value: {key}. Use --help.");
            values[key[2..]] = args[i];
        }

        var config = Path.GetFullPath(values.GetValueOrDefault("config") ?? env("RABBITEARS_CONFIG")
            ?? Path.Combine(DefaultProfile(home), "desktop.json"));
        if (!File.Exists(config) && (values.ContainsKey("config") || env("RABBITEARS_CONFIG") is not null))
            throw new FileNotFoundException("Desktop configuration does not exist.", config);
        using var document = JsonDocument.Parse(File.Exists(config) ? File.ReadAllText(config) : "{}");
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("desktop.json must contain a JSON object.");
        string? FromJson(string key) => document.RootElement.TryGetProperty(key, out var value) ? value.ToString() : null;
        string Read(string key, string fallback, string? alias = null) => values.GetValueOrDefault(key)
            ?? env("RABBITEARS_" + key.ToUpperInvariant()) ?? (alias is null ? null : env(alias)) ?? FromJson(key) ?? fallback;
        // Profile paths must not depend on Finder's working directory.
        string FullPath(string value) => Path.GetFullPath(value.StartsWith("~/", StringComparison.Ordinal)
            ? Path.Combine(home, value[2..]) : value, Path.GetDirectoryName(config)!);

        if (!int.TryParse(Read("port", "5080"), NumberStyles.None, CultureInfo.InvariantCulture, out var port)
            || port is < 1024 or > 65535) throw new ArgumentException("Port must be between 1024 and 65535.");
        if (!bool.TryParse(Read("lan", "false"), out var lan)) throw new ArgumentException("lan must be true or false.");
        var defaultMedia = Path.Combine(home, "Movies", "RabbitEars");
        var media = FullPath(Read("media", defaultMedia, "Box__MediaRoot"));
        if (media == defaultMedia) Directory.CreateDirectory(media);
        if (!Directory.Exists(media)) throw new DirectoryNotFoundException($"Media folder does not exist: {media}");
        // Force an early permission check instead of silently starting with no channels.
        using (var entries = Directory.EnumerateFileSystemEntries(media).GetEnumerator()) entries.MoveNext();
        return new(media, FullPath(Read("state", DefaultProfile(home), "Box__StateDir")),
            Executable(Read("mpv", "mpv"), env("PATH"), FullPath),
            Executable(Read("ffprobe", "ffprobe", "Box__Ffprobe"), env("PATH"), FullPath), port, lan);
    }

    private static string Executable(string name, string? path, Func<string, string> fullPath)
    {
        var candidates = name.Contains('/')
            ? new[] { fullPath(name) }
            : (path ?? "").Split(Path.PathSeparator).Where(p => p.Length > 0)
                .Concat(["/opt/homebrew/bin", "/usr/local/bin"]).Select(p => Path.Combine(p, name));
        foreach (var candidate in candidates)
        {
            if (!File.Exists(candidate)) continue;
            if (!OperatingSystem.IsWindows() && (File.GetUnixFileMode(candidate)
                & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) == 0) continue;
            return Path.GetFullPath(candidate);
        }
        throw new FileNotFoundException($"Executable '{name}' was not found. Install mpv and ffmpeg, or set --mpv and --ffprobe to executable paths.");
    }
}
