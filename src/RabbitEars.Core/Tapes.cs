namespace RabbitEars.Core;

/// <summary>
/// One of a demo visitor's own videos. The file stays in their browser, which
/// plays it; the box only learns an id it made up and a name to show.
/// </summary>
public sealed record Tape(string Id, string Title);

/// <summary>
/// Tapes that arrived together in one folder. A shelf with no name holds loose
/// files and becomes the VCR channel.
/// </summary>
public sealed record TapeShelf(string? Name, IReadOnlyList<Tape> Tapes);

/// <summary>
/// Turns a visitor's dropped videos into channels the same way the box turns a
/// media folder into channels: one per folder, named after it, in A-to-Z order
/// after the broadcast lineup. Loose files go on the VCR, channel 0.
/// </summary>
public static class Tapes
{
    /// <summary>Episode ids of tapes start with this, so the screen knows to play them from the browser.</summary>
    public const string EpisodePrefix = "tape-";

    public const string VcrName = "VCR";
    public const int VcrNumber = 0;

    // A small portfolio demo selection; files themselves never reach the server.
    public const int MaxShelves = 3;
    public const int MaxTapes = 5;
    public const int MaxNameLength = 80;
    public const int MaxTitleLength = 120;

    private const string KeyPrefix = "tape:";

    public static bool IsTape(string episodeId) => episodeId.StartsWith(EpisodePrefix, StringComparison.Ordinal);

    /// <summary>
    /// Channels for these shelves, numbered to follow <paramref name="broadcast"/>,
    /// or null if the shelves are malformed or over the limits.
    /// </summary>
    public static IReadOnlyList<ChannelDefinition>? Channels(
        IReadOnlyList<ChannelDefinition> broadcast, IReadOnlyList<TapeShelf>? shelves)
    {
        if (shelves is null || shelves.Count is 0 or > MaxShelves) return null;

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var total = 0;
        foreach (var shelf in shelves)
        {
            if (shelf?.Tapes is not { Count: > 0 } tapes) return null;
            if (!names.Add(shelf.Name ?? "") || (shelf.Name is { } name && !IsPlain(name, MaxNameLength))) return null;
            total += tapes.Count;
            if (total > MaxTapes) return null;
            foreach (var tape in tapes)
            {
                if (tape is null || !IsId(tape.Id) || !ids.Add(tape.Id) || !IsPlain(tape.Title, MaxTitleLength)) return null;
            }
        }

        var next = broadcast.Count == 0 ? 2 : broadcast.Max(c => c.Number) + 1;
        return [.. shelves
            .OrderBy(s => s.Name is null ? 0 : 1)
            .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .Select(s => new ChannelDefinition(
                s.Name is null ? VcrNumber : next++,
                s.Name ?? VcrName,
                [.. s.Tapes.Select(t => new Episode(EpisodePrefix + t.Id, t.Title))],
                KeyPrefix + s.Name, TuneInMode.Resume))];
    }

    // Ids are made up by the browser: plain lowercase hex, so they are safe in any URL or log line.
    private static bool IsId(string? id) =>
        id is { Length: >= 8 and <= 32 } && id.All(c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f'));

    private static bool IsPlain(string? text, int maxLength) =>
        !string.IsNullOrWhiteSpace(text) && text.Length <= maxLength && !text.Any(char.IsControl);
}
