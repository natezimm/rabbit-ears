using Microsoft.Extensions.Logging.Abstractions;
using RabbitEars.Api;

namespace RabbitEars.Tests;

public sealed class LibraryScannerTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("rabbitears-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private void Touch(string relativePath)
    {
        var path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "");
    }

    private Library Scan() =>
        new LibraryScanner(new BoxOptions { MediaRoot = _root }, NullLogger<LibraryScanner>.Instance).Scan();

    [Fact]
    public void Folders_become_channels_numbered_from_two_in_alphabetical_order()
    {
        Touch("Dragon Tales/S01E01.mp4");
        Touch("arthur/Season 1/S01E01.mp4");
        Touch("arthur/Season 1/S01E02.mp4");

        var library = Scan();

        Assert.Equal([(2, "arthur", 2), (3, "Dragon Tales", 1)],
            library.Channels.Select(c => (c.Number, c.Name, c.Episodes.Count)));
        Assert.Equal("S01E02", library.Channels[0].Episodes[1].Title);
    }

    [Fact]
    public void Only_browser_playable_files_are_episodes_and_the_rest_are_reported()
    {
        Touch("Arthur/S01E01.mp4");
        Touch("Arthur/S01E02.mkv");
        Touch("Arthur/cover.jpg");
        Touch("Arthur/.hidden.mp4");

        var library = Scan();

        Assert.Single(library.Channels[0].Episodes);
        Assert.Equal(["Arthur/S01E02.mkv"], library.NeedsConversion);
    }

    [Fact]
    public void A_folder_with_nothing_playable_is_not_a_channel()
    {
        Touch("Arthur/S01E01.mp4");
        Touch("Old Rips/S01E01.avi");
        Directory.CreateDirectory(Path.Combine(_root, "Empty"));

        var library = Scan();

        Assert.Equal(["Arthur"], library.Channels.Select(c => c.Name));
    }

    [Fact]
    public void Episode_ids_are_stable_and_resolve_to_the_file()
    {
        Touch("Arthur/S01E01.mp4");

        var first = Scan();
        var second = Scan();

        var id = first.Channels[0].Episodes[0].Id;
        Assert.Equal(id, second.Channels[0].Episodes[0].Id);
        Assert.Equal(Path.Combine(_root, "Arthur", "S01E01.mp4"), first.Files[id]);
    }

    [Fact]
    public void A_missing_media_folder_is_an_empty_lineup_not_a_crash()
    {
        var scanner = new LibraryScanner(
            new BoxOptions { MediaRoot = Path.Combine(_root, "nope") },
            NullLogger<LibraryScanner>.Instance);

        Assert.Empty(scanner.Scan().Channels);
    }
}
