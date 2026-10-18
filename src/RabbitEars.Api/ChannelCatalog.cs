using System.Text.Json;
using RabbitEars.Core;

namespace RabbitEars.Api;

public sealed record ChannelEdit(
    string Folder, string Name, int? Number, bool Enabled, string? TuneIn,
    string[] ExcludedEpisodes, string[] ExcludedSeasons);
public sealed record CatalogDocument(string Version, IReadOnlyList<ChannelEdit> Channels);
public sealed record AdminEpisode(string Id, string Title, string Path, string Season);
public sealed record AdminChannel(ChannelEdit Settings, IReadOnlyList<AdminEpisode> Episodes);
public sealed record AdminCatalog(string Version, IReadOnlyList<AdminChannel> Channels, IReadOnlyList<string> NeedsConversion);

/// <summary>
/// Display labels and eligibility live apart from folders. Folder keys keep a
/// channel's saved playback identity when its display label or number changes.
/// TvService serializes writes and rescans through its lineup semaphore.
/// </summary>
public sealed class ChannelCatalog
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly BoxOptions _options;
    private readonly LibraryScanner _scanner;
    private readonly ILogger<ChannelCatalog> _log;
    private CatalogDocument _document = new(Guid.NewGuid().ToString("N"), []);
    private bool _unreadable;
    private string FilePath => Path.Combine(_options.StateDir, "channels.json");

    public ChannelCatalog(BoxOptions options, LibraryScanner scanner, ILogger<ChannelCatalog> log)
    {
        _options = options;
        _scanner = scanner;
        _log = log;
        if (options.Demo.Enabled) return;
        try
        {
            if (File.Exists(FilePath))
            {
                _document = JsonSerializer.Deserialize<CatalogDocument>(File.ReadAllText(FilePath), Json)
                    ?? throw new JsonException("Empty channel metadata.");
                if (_document.Channels is null || string.IsNullOrEmpty(_document.Version)
                    || _document.Channels.Any(c => c is null || c.Folder is null || c.Name is null
                        || c.ExcludedEpisodes is null || c.ExcludedSeasons is null)
                    || _document.Channels.Select(c => c.Folder).Distinct(StringComparer.Ordinal).Count() != _document.Channels.Count)
                    throw new JsonException("Invalid channel metadata.");
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _log.LogWarning(ex, "Cannot read channel metadata. Keeping the folder lineup; admin writes are blocked until repaired.");
            _document = new(Guid.NewGuid().ToString("N"), []);
            _unreadable = true;
        }
    }

    public Library Scan() => Apply(_scanner.Scan(), _document.Channels);

    public AdminCatalog View()
    {
        EnsureReadable();
        var raw = _scanner.Scan();
        return new(_document.Version, [.. raw.Channels.Select(c =>
        {
            var episodes = Episodes(raw, c);
            var settings = Settings(c);
            // A deleted file must not leave an invisible exclusion that prevents
            // saving any edit. Settings for entirely missing channels stay on disk.
            settings = settings with
            {
                ExcludedEpisodes = [.. settings.ExcludedEpisodes.Intersect(episodes.Select(e => e.Id), StringComparer.Ordinal)],
                ExcludedSeasons = [.. settings.ExcludedSeasons.Intersect(episodes.Select(e => e.Season), StringComparer.Ordinal)]
            };
            return new AdminChannel(settings, episodes);
        })], raw.NeedsConversion);
    }

    /// <summary>Validate everything before touching disk or the current lineup.</summary>
    public Library Save(CatalogDocument request)
    {
        EnsureReadable();
        if (request.Version != _document.Version) throw new CatalogConflictException();
        var raw = _scanner.Scan();
        if (request.Channels is null) throw new ArgumentException("Channels are required.");
        var known = raw.Channels.ToDictionary(c => c.Name, StringComparer.Ordinal);
        var folders = new HashSet<string>(StringComparer.Ordinal);
        var numbers = new HashSet<int>();
        foreach (var edit in request.Channels)
        {
            if (edit is null || edit.Folder is null || !known.TryGetValue(edit.Folder, out var channel) || !folders.Add(edit.Folder))
                throw new ArgumentException("Each channel must name a unique, existing folder. Reload after a rescan.");
            if (string.IsNullOrWhiteSpace(edit.Name) || edit.Name.Trim().Length > 80 || edit.Name.Any(char.IsControl))
                throw new ArgumentException("Channel names must have 1–80 visible characters.");
            if (edit.Number is { } number && (number is < 1 or > 999 || !numbers.Add(number)))
                throw new ArgumentException("Custom channel numbers must be unique and between 1 and 999.");
            if (edit.TuneIn is not null and not "Live" and not "Resume" and not "Random")
                throw new ArgumentException("Tune-in must be Default, Live, Resume or Random.");
            var episodes = Episodes(raw, channel);
            if (edit.ExcludedEpisodes is null || edit.ExcludedSeasons is null
                || edit.ExcludedEpisodes.Except(episodes.Select(e => e.Id), StringComparer.Ordinal).Any()
                || edit.ExcludedSeasons.Except(episodes.Where(e => e.Season.Length > 0).Select(e => e.Season), StringComparer.Ordinal).Any())
                throw new ArgumentException("Exclusions must refer to existing episodes or season folders. Reload after a rescan.");
        }
        if (folders.Count != known.Count) throw new ArgumentException("Save the complete current lineup. Reload after a rescan.");

        // Keep settings for temporarily missing drives/folders; they return on the next scan.
        var edits = request.Channels.Select(c => c with { Name = c.Name.Trim() })
            .Concat(_document.Channels.Where(c => !folders.Contains(c.Folder))).ToArray();
        var next = new CatalogDocument(Guid.NewGuid().ToString("N"), edits);
        var library = Apply(raw, edits);
        Directory.CreateDirectory(_options.StateDir);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(next, Json));
        File.Move(temp, FilePath, overwrite: true);
        _document = next;
        return library;
    }

    private void EnsureReadable()
    {
        if (_unreadable) throw new IOException("Repair channels.json and restart before editing the lineup.");
    }

    private ChannelEdit Settings(ChannelDefinition channel) =>
        _document.Channels.FirstOrDefault(c => c.Folder == channel.Name)
        ?? new ChannelEdit(channel.Name, channel.Name, null, true, null, [], []);

    private IReadOnlyList<AdminEpisode> Episodes(Library raw, ChannelDefinition channel) =>
        [.. channel.Episodes.Select(e =>
        {
            // Strip the channel directory; a nested folder is the season identifier.
            var relative = Path.GetRelativePath(Path.Combine(_options.MediaRoot, channel.Name), raw.Files[e.Id]).Replace('\\', '/');
            var season = Path.GetDirectoryName(relative)?.Replace('\\', '/') ?? "";
            return new AdminEpisode(e.Id, e.Title, relative, season);
        })];

    private Library Apply(Library raw, IReadOnlyList<ChannelEdit> edits)
    {
        // The demo's curated library is deployment-managed, never visitor-editable.
        if (_options.Demo.Enabled) return raw;
        var settings = edits.ToDictionary(c => c.Folder, StringComparer.Ordinal);
        var reserved = edits.Where(c => c.Number is >= 1 and <= 999).Select(c => c.Number!.Value).ToHashSet();
        var used = new HashSet<int>();
        var channels = new List<ChannelDefinition>();
        var files = new Dictionary<string, string>();
        var auto = _options.FirstChannelNumber;
        foreach (var channel in raw.Channels)
        {
            settings.TryGetValue(channel.Name, out var edit);
            if (edit is { Enabled: false }) continue;
            var excluded = edit?.ExcludedEpisodes.ToHashSet(StringComparer.Ordinal) ?? [];
            var seasons = edit?.ExcludedSeasons.ToHashSet(StringComparer.Ordinal) ?? [];
            foreach (var episode in Episodes(raw, channel))
                if (seasons.Contains(episode.Season)) excluded.Add(episode.Id);
            var eligible = channel.Episodes.Where(e => !excluded.Contains(e.Id)).ToArray();
            if (eligible.Length == 0) continue;
            var number = edit?.Number;
            if (number is null || !used.Add(number.Value))
            {
                while (reserved.Contains(auto) || used.Contains(auto)) auto++;
                number = auto++;
                if (number > 999) throw new ArgumentException("The lineup has no free three-digit channel numbers.");
                used.Add(number.Value);
            }
            channels.Add(new(number.Value, edit?.Name ?? channel.Name, eligible, channel.Name,
                Enum.TryParse<TuneInMode>(edit?.TuneIn, out var mode) ? mode : null));
            foreach (var episode in eligible) files[episode.Id] = raw.Files[episode.Id];
        }
        return new([.. channels.OrderBy(c => c.Number)], files, raw.NeedsConversion);
    }
}

public sealed class CatalogConflictException : Exception;
