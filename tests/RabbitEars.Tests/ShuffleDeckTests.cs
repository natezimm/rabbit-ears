using RabbitEars.Core;

namespace RabbitEars.Tests;

public class ShuffleDeckTests
{
    [Fact]
    public void Each_pass_deals_every_episode_exactly_once()
    {
        var deck = new ShuffleDeck<char>("abcdefgh", new Random(7));

        for (var pass = 0; pass < 4; pass++)
        {
            var dealt = string.Concat(Enumerable.Range(0, 8).Select(_ => deck.Next()).Order());
            Assert.Equal("abcdefgh", dealt);
        }
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    public void The_last_episode_of_a_pass_never_opens_the_next(int size)
    {
        for (var seed = 0; seed < 100; seed++)
        {
            var deck = new ShuffleDeck<int>(Enumerable.Range(0, size), new Random(seed));
            var dealt = Enumerable.Range(0, size * 12).Select(_ => deck.Next()).ToList();
            Assert.DoesNotContain(dealt.Zip(dealt.Skip(1)), pair => pair.First == pair.Second);
        }
    }

    [Fact]
    public void Every_episode_can_open_a_pass()
    {
        var openers = new HashSet<int>();
        for (var seed = 0; seed < 200; seed++)
            openers.Add(new ShuffleDeck<int>([1, 2, 3, 4], new Random(seed)).Next());

        Assert.Equal(new[] { 1, 2, 3, 4 }, openers.Order());
    }

    [Fact]
    public void A_one_episode_channel_airs_it_again_and_again()
    {
        var deck = new ShuffleDeck<string>(["pilot"]);
        Assert.All(Enumerable.Range(0, 3), _ => Assert.Equal("pilot", deck.Next()));
    }

    [Fact]
    public void An_empty_deck_has_nothing_to_deal()
    {
        Assert.Throws<InvalidOperationException>(() => new ShuffleDeck<string>([]).Next());
    }
}
