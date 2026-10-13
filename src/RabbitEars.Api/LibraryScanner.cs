using System.Security.Cryptography;
using System.Text;
using RabbitEars.Core;

namespace RabbitEars.Api;

/// <param name="Channels">Show folders that have at least one playable episode.</param>
/// <param name="Files">Episode id to absolute path. The only files the box will ever serve.</param>
/// <param name="NeedsConversion">Videos in a format the browser cannot play, relative to the media root.</param>
public sealed record Library(
    IReadOnlyList<ChannelDefinition> Channels,
    IReadOnlyDictionary<string, string> Files,
    IReadOnlyList<string> NeedsConversion)
{
    public static Library Empty { get; } = new([], new Dictionary<string, string>(), []);
}

/// <summary>
/// Turns the media folder into a channel lineup: every folder with videos in it
/// is a channel, named after the folder and numbered in A-to-Z order. Season
/// subfolders are fine.
/// </summary>
public sealed class LibraryScanner(BoxOptions options, ILogger<LibraryScanner> log)
{
    // Common rips that Chromium will not play; worth telling the owner about.
    private static readonly HashSet<string> UnplayableVideo = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mkv", ".avi", ".wmv", ".mpg", ".mpeg", ".ts", ".flv", ".divx", ".vob", ".ogv", ".3gp",
    };

    public Library Scan()
    {
        var root = options.MediaRoot;
        if (!Directory.Exists(root))
        {
            log.LogWarning("Media folder {Root} does not exist; no channels.", root);
            return Library.Empty;
        }

        var playable = new HashSet<string>(options.VideoExtensions, StringComparer.OrdinalIgnoreCase);
        var channels = new List<ChannelDefinition>();
        var files = new Dictionary<string, string>();
        var needsConversion = new List<string>();
        var number = options.FirstChannelNumber;

        var folders = Directory.EnumerateDirectories(root)
            .Where(folder => !IsHidden(folder))
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase);

        foreach (var folder in folders)
        {
            var name = Path.GetFileName(folder);
            var episodes = new List<Episode>();

            var paths = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                .Where(path => !IsHidden(path))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);

            foreach (var path in paths)
            {
                var extension = Path.GetExtension(path);
                var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
                if (playable.Contains(extension))
                {
                    var id = IdFor(relative);
                    files[id] = path;
                    episodes.Add(new Episode(id, Path.GetFileNameWithoutExtension(path)));
                }
                else if (UnplayableVideo.Contains(extension))
                {
                    needsConversion.Add(relative);
                }
            }

            if (episodes.Count == 0)
            {
                log.LogWarning("Skipping {Folder}: no playable episodes in it.", name);
                continue;
            }
            channels.Add(new ChannelDefinition(number++, name, episodes));
        }

        log.LogInformation(
            "Scanned {Root}: {Channels} channels, {Episodes} episodes, {Unplayable} files need converting.",
            root, channels.Count, files.Count, needsConversion.Count);
        return new Library(channels, files, needsConversion);
    }

    private static bool IsHidden(string path) => Path.GetFileName(path).StartsWith('.');

    // Stable across rescans and restarts, and keeps real paths out of URLs.
    private static string IdFor(string relativePath) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(relativePath)))[..16];
}
