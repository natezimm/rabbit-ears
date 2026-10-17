using Microsoft.Extensions.Logging.Abstractions;
using RabbitEars.Api;
using RabbitEars.Core;

namespace RabbitEars.Tests;

public sealed class StateFileTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("rabbitears-state-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private StateFile File() =>
        new(new BoxOptions { StateDir = _dir }, NullLogger<StateFile>.Instance);

    [Fact]
    public void Saved_state_comes_back_after_a_restart()
    {
        var state = new SavedState("Arthur", "Dragon Tales", 35, true,
            new() { ["Arthur"] = new SavedPosition("abc123", 312.5) });

        File().Save(state);
        var loaded = File().Load();

        Assert.NotNull(loaded);
        Assert.Equal(("Arthur", "Dragon Tales", 35, true), (loaded.Channel, loaded.LastChannel, loaded.Volume, loaded.Muted));
        Assert.Equal(new SavedPosition("abc123", 312.5), loaded.Positions["Arthur"]);
        Assert.False(System.IO.File.Exists(Path.Combine(_dir, "state.json.tmp")));
    }

    [Fact]
    public void No_file_yet_means_a_fresh_start()
    {
        Assert.Null(File().Load());
    }

    [Fact]
    public void A_corrupt_file_means_a_fresh_start_not_a_crash()
    {
        System.IO.File.WriteAllText(Path.Combine(_dir, "state.json"), "{ half a file");

        Assert.Null(File().Load());
    }

    [Fact]
    public void A_file_without_positions_loads_with_none()
    {
        System.IO.File.WriteAllText(Path.Combine(_dir, "state.json"),
            """{ "Channel": "Arthur", "Volume": 40, "Muted": false }""");

        var loaded = File().Load();

        Assert.NotNull(loaded);
        Assert.Empty(loaded.Positions);
    }
}
