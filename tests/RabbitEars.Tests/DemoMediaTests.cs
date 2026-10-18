using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using RabbitEars.Api;
using RabbitEars.Core;

namespace RabbitEars.Tests;

public sealed class DemoMediaTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("ears-prepared-").FullName;

    [Fact]
    public async Task Prepared_durations_advance_unwatched_demo_channels_without_ffprobe()
    {
        foreach (var channel in new[] { "First", "Second" })
        {
            Directory.CreateDirectory(Path.Combine(_root, channel));
            File.WriteAllText(Path.Combine(_root, channel, "episode.mp4"), "video");
        }
        File.WriteAllText(Path.Combine(_root, ".demo-media.json"), JsonSerializer.Serialize(new
        {
            version = 1,
            videos = new[] { new { path = "First/episode.mp4", title = "Episode", creator = "Test", license = "Test", sourceUrl = "https://example.com/source", duration = 120 } }
        }));
        var options = new BoxOptions
        {
            MediaRoot = _root, StateDir = Path.Combine(_root, "state"),
            Demo = new() { Enabled = true }, Tv = new() { StartOffsetMin = 0, StartOffsetMax = 0 }
        };
        var clock = new FakeClock();
        using var tv = new TvService(options, new LibraryScanner(options, NullLogger<LibraryScanner>.Instance),
            new StateFile(options, NullLogger<StateFile>.Instance), new RecordingHub(), NullLogger<TvService>.Instance, clock);
        var room = tv.Join("test-room")!;
        await tv.Press(t => t.GoToChannel(3), room);
        clock.Advance(TimeSpan.FromSeconds(305));
        await tv.Press(t => t.GoToChannel(2), room);
        Assert.Equal(65, tv.Snapshot(room)!.Playing!.Start);
        Assert.Single(tv.Credits());
        Assert.False(Directory.Exists(options.StateDir));
    }

    [Fact]
    public void Invalid_metadata_does_not_break_generated_or_plain_file_playback()
    {
        var options = new BoxOptions { MediaRoot = _root, Demo = new() { Enabled = true } };
        File.WriteAllText(Path.Combine(_root, ".demo-media.json"), "not json");
        Assert.Empty(new DemoMedia(options, NullLogger<DemoMedia>.Instance).Read(Library.Empty));
    }

    public void Dispose() => Directory.Delete(_root, true);
}
