using RabbitEars.Core;

namespace RabbitEars.Tests;

public sealed class TapesTests
{
    private static readonly IReadOnlyList<ChannelDefinition> Broadcast =
    [
        new(2, "Arthur", [new Episode("a1", "S01E01")]),
        new(3, "Dragon Tales", [new Episode("d1", "S01E01")]),
    ];

    private static Tape T(string id, string title = "Episode") => new(id, title);

    [Fact]
    public void Folders_follow_the_broadcast_lineup_in_A_to_Z_order_and_loose_files_go_on_the_vcr()
    {
        var channels = Tapes.Channels(Broadcast,
        [
            new TapeShelf("Seinfeld", [T("0000000a", "The Pen")]),
            new TapeShelf(null, [T("0000000b", "Home movie")]),
            new TapeShelf("Cheers", [T("0000000c", "Pilot")]),
        ])!;

        Assert.Equal([(0, "VCR"), (4, "Cheers"), (5, "Seinfeld")], channels.Select(c => (c.Number, c.Name)));
        Assert.Equal(new Episode("tape-0000000b", "Home movie"), Assert.Single(channels[0].Episodes));
        Assert.True(Tapes.IsTape(channels[1].Episodes[0].Id));
    }

    [Fact]
    public void Tape_channels_never_share_a_key_with_a_media_folder_of_the_same_name()
    {
        var channels = Tapes.Channels(Broadcast, [new TapeShelf("Arthur", [T("0000000a")])])!;

        Assert.NotEqual("Arthur", channels[0].Key);
    }

    public static TheoryData<TapeShelf[]?> Malformed => new()
    {
        null,
        Array.Empty<TapeShelf>(),
        new[] { new TapeShelf("Empty", []) },
        new[] { new TapeShelf(null, [T("NOT-HEX!")]) },
        new[] { new TapeShelf(null, [T("0000000a"), T("0000000a")]) },
        new[] { new TapeShelf(null, [T("0000000a", "line\nbreak")]) },
        new[] { new TapeShelf(" ", [T("0000000a")]) },
        new[] { new TapeShelf("Same", [T("0000000a")]), new TapeShelf("same", [T("0000000b")]) },
        new[] { new TapeShelf(null, [.. Enumerable.Range(0, Tapes.MaxTapes + 1).Select(i => T($"{i:x8}"))]) },
    };

    [Theory]
    [MemberData(nameof(Malformed))]
    public void Malformed_or_oversized_shelves_are_refused(TapeShelf[]? shelves) =>
        Assert.Null(Tapes.Channels(Broadcast, shelves));
}
