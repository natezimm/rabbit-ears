using RabbitEars.Core;

namespace RabbitEars.Tests;

public class ShuffleBagTests
{
    [Fact]
    public void Every_item_comes_out_once_before_any_repeats()
    {
        var bag = new ShuffleBag<int>(Enumerable.Range(1, 10), new Random(1));

        for (var cycle = 0; cycle < 5; cycle++)
        {
            var drawn = Enumerable.Range(0, 10).Select(_ => bag.Next()).ToList();
            Assert.Equal(Enumerable.Range(1, 10), drawn.Order());
        }
    }

    [Fact]
    public void Never_repeats_back_to_back_across_a_reshuffle()
    {
        // Three items reshuffle often, so a boundary repeat would show up quickly.
        for (var seed = 0; seed < 200; seed++)
        {
            var bag = new ShuffleBag<string>(["a", "b", "c"], new Random(seed));
            var previous = bag.Next();
            for (var i = 0; i < 30; i++)
            {
                var next = bag.Next();
                Assert.NotEqual(previous, next);
                previous = next;
            }
        }
    }

    [Fact]
    public void A_single_item_just_repeats()
    {
        var bag = new ShuffleBag<string>(["only"]);
        Assert.Equal("only", bag.Next());
        Assert.Equal("only", bag.Next());
    }

    [Fact]
    public void An_empty_bag_cannot_be_drawn_from()
    {
        var bag = new ShuffleBag<string>([]);
        Assert.Throws<InvalidOperationException>(() => bag.Next());
    }
}
