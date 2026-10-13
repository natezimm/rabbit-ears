using System.Runtime.InteropServices;

namespace RabbitEars.Core;

/// <summary>
/// Hands out every item once in a random order, then reshuffles and goes again.
/// A plain "pick a random episode" repeats itself and starves some episodes;
/// a bag guarantees each one airs before any repeats, and never plays the same
/// episode back-to-back across a reshuffle.
/// </summary>
public sealed class ShuffleBag<T>
{
    private readonly List<T> _items;
    private readonly Random _random;
    private readonly List<T> _queue = [];
    private T? _last;
    private bool _hasLast;

    public ShuffleBag(IEnumerable<T> items, Random? random = null)
    {
        _items = [.. items];
        _random = random ?? Random.Shared;
    }

    public int Count => _items.Count;

    /// <summary>How many draws are left before the next reshuffle.</summary>
    public int Remaining => _queue.Count;

    public T Next()
    {
        if (_items.Count == 0)
            throw new InvalidOperationException("Cannot draw from an empty shuffle bag.");
        if (_queue.Count == 0)
            Refill();

        var item = _queue[^1];
        _queue.RemoveAt(_queue.Count - 1);
        _last = item;
        _hasLast = true;
        return item;
    }

    private void Refill()
    {
        _queue.AddRange(_items);
        _random.Shuffle(CollectionsMarshal.AsSpan(_queue));

        // Draws come off the end. If the end is what just played, send it to
        // the back of the line instead.
        if (_queue.Count > 1 && _hasLast && EqualityComparer<T>.Default.Equals(_queue[^1], _last))
            (_queue[0], _queue[^1]) = (_queue[^1], _queue[0]);
    }
}
