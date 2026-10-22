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

    [Fact]
    public void Approved_v2_mount_only_scans_allowlisted_clips_and_public_credits()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Cartoons"));
        File.WriteAllText(Path.Combine(_root, "Cartoons", "approved.mp4"), "approved placeholder");
        File.WriteAllText(Path.Combine(_root, "Cartoons", "unapproved.mp4"), "unapproved placeholder");
        File.WriteAllText(Path.Combine(_root, ".demo-media.json"), JsonSerializer.Serialize(new
        {
            version = 2,
            videos = new[] { new {
                path = "Cartoons/approved.mp4", title = "Approved", creator = "Creator",
                license = "Verified public use", sourceUrl = "https://example.com/source",
                licenseUrl = "https://example.com/license", modifications = "Transcoded", year = 1930,
                attribution = "Courtesy original creator", approvedForPublicDemo = true, duration = 120.0
            } }
        }));
        var options = new BoxOptions { MediaRoot = _root, Demo = new() { Enabled = true, RequireApprovedMedia = true } };
        var library = new LibraryScanner(options, NullLogger<LibraryScanner>.Instance).Scan();
        Assert.Single(library.Files);
        Assert.Equal("Approved", Assert.Single(new DemoMedia(options, NullLogger<DemoMedia>.Instance).Read(library)).Title);
        Assert.DoesNotContain(library.Files.Values, path => path.Contains("unapproved", StringComparison.Ordinal));

        var payload = JsonSerializer.Serialize(new DemoCredit("Cartoons", "Approved", "Creator",
            "Verified public use", "https://example.com/source", "https://example.com/license",
            "Transcoded", 1930, "Courtesy original creator"), JsonSerializerOptions.Web);
        Assert.DoesNotContain("reviewedBy", payload);
        Assert.DoesNotContain("rightsEvidence", payload);
        Assert.Contains("licenseUrl", payload);
    }

    [Theory]
    [InlineData("{ bad json")]
    [InlineData("{\\"version\\":2,\\"videos\\":[{\\"path\\":\\"Cartoons/video.mp4\\",\\"approvedForPublicDemo\\":false}]}")]
    public void Mounted_demo_fails_closed_with_invalid_or_unapproved_metadata(string metadata)
    {
        Directory.CreateDirectory(Path.Combine(_root, "Cartoons"));
        File.WriteAllText(Path.Combine(_root, "Cartoons", "video.mp4"), "fixture");
        File.WriteAllText(Path.Combine(_root, ".demo-media.json"), metadata);
        var options = new BoxOptions { MediaRoot = _root, Demo = new() { Enabled = true, RequireApprovedMedia = true } };
        Assert.Empty(new LibraryScanner(options, NullLogger<LibraryScanner>.Instance).Scan().Files);
    }

    [Fact]
    public void Mounted_demo_fails_closed_without_metadata_but_generated_fallback_still_works()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Cartoons"));
        File.WriteAllText(Path.Combine(_root, "Cartoons", "video.mp4"), "fixture");
        var options = new BoxOptions { MediaRoot = _root, Demo = new() { Enabled = true, RequireApprovedMedia = true } };
        Assert.Empty(new LibraryScanner(options, NullLogger<LibraryScanner>.Instance).Scan().Files);
        options.Demo.RequireApprovedMedia = false;
        Assert.Single(new LibraryScanner(options, NullLogger<LibraryScanner>.Instance).Scan().Files);
    }

    public void Dispose() => Directory.Delete(_root, true);
}
