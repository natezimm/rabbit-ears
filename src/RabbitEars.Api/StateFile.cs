using System.Text.Json;
using RabbitEars.Core;

namespace RabbitEars.Api;

/// <summary>
/// Remembers the channel, volume and per-channel positions across reboots in one
/// small JSON file. Kids pull the plug, so writes go to a temp file and are
/// renamed into place: a cut mid-write leaves the previous file intact.
/// </summary>
public sealed class StateFile(BoxOptions options, ILogger<StateFile> log)
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private string FilePath => Path.Combine(options.StateDir, "state.json");

    public SavedState? Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            var saved = JsonSerializer.Deserialize<SavedState>(File.ReadAllText(FilePath), Json);
            return saved is null ? null : saved with { Positions = saved.Positions ?? [] };
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            log.LogWarning(ex, "Could not read {Path}; starting fresh.", FilePath);
            return null;
        }
    }

    /// <summary>False when the write failed and the previous file is still in place.</summary>
    public bool Save(SavedState state)
    {
        try
        {
            Directory.CreateDirectory(options.StateDir);
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(state, Json));
            File.Move(temp, FilePath, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.LogWarning(ex, "Could not save {Path}; will try again.", FilePath);
            return false;
        }
    }
}
