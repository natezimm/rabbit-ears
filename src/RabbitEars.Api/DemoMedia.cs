using System.Text.Json;

namespace RabbitEars.Api;

public sealed record DemoCredit(string Channel, string Title, string Creator, string License, string SourceUrl, string? LicenseUrl, string? Modifications, int? Year, string? Attribution);
public sealed record PreparedVideo(string Path, string Title, string Creator, string License, string SourceUrl, double Duration, bool ApprovedForPublicDemo = false, string? LicenseUrl = null, string? Modifications = null, int? Year = null, string? Attribution = null);
public sealed record PreparedMedia(int Version, IReadOnlyList<PreparedVideo> Videos);

/// <summary>Reads only metadata for already-scanned files; a manifest can never expose another path.</summary>
public sealed class DemoMedia(BoxOptions options, ILogger<DemoMedia> log)
{
    public IReadOnlyList<PreparedVideo> Read(Library library)
    {
        if (!options.Demo.Enabled) return [];
        var path = System.IO.Path.Combine(options.MediaRoot, ".demo-media.json");
        if (!File.Exists(path)) return [];
        try
        {
            var metadata = JsonSerializer.Deserialize<PreparedMedia>(File.ReadAllText(path), JsonSerializerOptions.Web);
            if (metadata is not { Version: 1 or 2, Videos: not null }) throw new JsonException("Unsupported media manifest.");
            var files = library.Files.Values.Select(f => System.IO.Path.GetRelativePath(options.MediaRoot, f).Replace('\\', '/')).ToHashSet();
            return [.. metadata.Videos.Where(v => v is not null && files.Contains(v.Path)
                && (metadata.Version == 1 || (v.ApprovedForPublicDemo && !string.IsNullOrWhiteSpace(v.Modifications)))
                && double.IsFinite(v.Duration) && v.Duration > 0
                && !string.IsNullOrWhiteSpace(v.Title) && !string.IsNullOrWhiteSpace(v.Creator)
                && !string.IsNullOrWhiteSpace(v.License)
                && Uri.TryCreate(v.SourceUrl, UriKind.Absolute, out var source) && source.Scheme is "http" or "https"
                && (v.LicenseUrl is null || (Uri.TryCreate(v.LicenseUrl, UriKind.Absolute, out var license) && license.Scheme is "http" or "https")))];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            log.LogWarning(ex, "Could not read prepared demo media metadata; playback can still learn lengths from the screen.");
            return [];
        }
    }
}
