using Microsoft.Extensions.Logging.Abstractions;
using RabbitEars.Api;
using RabbitEars.Core;

namespace RabbitEars.Tests;

public sealed class ChannelCatalogTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("ears-admin-").FullName;
    private readonly BoxOptions _options;

    public ChannelCatalogTests()
    {
        _options = new() { MediaRoot = Path.Combine(_root, "media"), StateDir = Path.Combine(_root, "state") };
        foreach (var file in new[] { "Arthur/Season 1/one.mp4", "Arthur/Season 2/two.mp4", "Movies/film.mp4" })
        {
            var path = Path.Combine(_options.MediaRoot, file);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "video");
        }
    }

    private ChannelCatalog Catalog() => new(_options,
        new LibraryScanner(_options, NullLogger<LibraryScanner>.Instance), NullLogger<ChannelCatalog>.Instance);

    private static CatalogDocument Request(AdminCatalog view, Func<ChannelEdit, ChannelEdit> change) =>
        new(view.Version, view.Channels.Select(c => change(c.Settings)).ToArray());

    [Fact]
    public void Rename_number_mode_and_exclusions_survive_restart_without_changing_identity()
    {
        var catalog = Catalog();
        var view = catalog.View();
        var raw = catalog.Scan();
        var firstId = raw.Channels[0].Episodes[0].Id;
        var library = catalog.Save(Request(view, c => c.Folder == "Arthur"
            ? c with { Name = "PBS Kids", Number = 8, TuneIn = "Resume", ExcludedSeasons = ["Season 2"] }
            : c with { Enabled = false }));
        var channel = Assert.Single(library.Channels);
        Assert.Equal((8, "PBS Kids", "Arthur", TuneInMode.Resume), (channel.Number, channel.Name, channel.Key, channel.TuneIn));
        Assert.Equal(firstId, Assert.Single(channel.Episodes).Id);
        Assert.Single(library.Files);
        Assert.Equal(channel, Assert.Single(Catalog().Scan().Channels), ChannelComparer.Instance);
    }

    [Fact]
    public void Excluding_every_episode_removes_a_channel_and_unchecking_restores_it()
    {
        var catalog = Catalog();
        var view = catalog.View();
        var id = view.Channels[1].Episodes[0].Id;
        Assert.Single(catalog.Save(Request(view, c => c.Folder == "Movies" ? c with { ExcludedEpisodes = [id] } : c)).Channels);
        Assert.Equal(2, catalog.View().Channels.Count);
        Assert.Equal(2, catalog.Save(Request(catalog.View(), c => c with { ExcludedEpisodes = [] })).Channels.Count);
    }

    [Fact]
    public void Automatic_numbers_avoid_custom_numbers_and_all_channels_can_be_disabled()
    {
        var catalog = Catalog();
        var library = catalog.Save(Request(catalog.View(), c => c.Folder == "Movies" ? c with { Number = 2 } : c));
        Assert.Equal([(2, "Movies"), (3, "Arthur")], library.Channels.Select(c => (c.Number, c.Name)));
        Assert.Empty(catalog.Save(Request(catalog.View(), c => c with { Enabled = false })).Channels);
    }

    [Fact]
    public void Invalid_and_stale_saves_do_not_replace_the_last_good_document()
    {
        var catalog = Catalog();
        var view = catalog.View();
        Assert.Throws<ArgumentException>(() => catalog.Save(Request(view, c => c with { Number = 5 })));
        Assert.Throws<ArgumentException>(() => catalog.Save(Request(view, c => c with { TuneIn = "Bogus" })));
        Assert.Throws<ArgumentException>(() => catalog.Save(Request(view, c => c with { ExcludedEpisodes = ["missing"] })));
        Assert.Equal(view.Version, catalog.View().Version);
        catalog.Save(Request(view, c => c));
        Assert.Throws<CatalogConflictException>(() => catalog.Save(Request(view, c => c)));
    }

    [Fact]
    public void Corrupt_metadata_is_not_overwritten_by_an_admin_save()
    {
        Directory.CreateDirectory(_options.StateDir);
        var path = Path.Combine(_options.StateDir, "channels.json");
        File.WriteAllText(path, "broken");
        var catalog = Catalog();
        Assert.Equal(2, catalog.Scan().Channels.Count);
        Assert.Throws<IOException>(() => catalog.View());
        Assert.Throws<IOException>(() => catalog.Save(new("anything", [])));
        Assert.Equal("broken", File.ReadAllText(path));
    }

    [Fact]
    public void Deleted_excluded_episodes_do_not_block_future_edits()
    {
        var catalog = Catalog();
        var view = catalog.View();
        var id = view.Channels[0].Episodes[0].Id;
        catalog.Save(Request(view, c => c.Folder == "Arthur" ? c with { ExcludedEpisodes = [id] } : c));
        File.Delete(Path.Combine(_options.MediaRoot, "Arthur/Season 1/one.mp4"));
        var fresh = catalog.View();
        Assert.Empty(fresh.Channels[0].Settings.ExcludedEpisodes);
        Assert.Equal(2, catalog.Save(Request(fresh, c => c)).Channels.Count);
    }

    public void Dispose() => Directory.Delete(_root, true);

    private sealed class ChannelComparer : IEqualityComparer<ChannelDefinition>
    {
        public static ChannelComparer Instance { get; } = new();
        public bool Equals(ChannelDefinition? x, ChannelDefinition? y) => x is not null && y is not null
            && x.Number == y.Number && x.Name == y.Name && x.Key == y.Key && x.TuneIn == y.TuneIn && x.Episodes.SequenceEqual(y.Episodes);
        public int GetHashCode(ChannelDefinition obj) => obj.Number;
    }
}
