namespace RabbitEars.Core;

/// <summary>
/// Deals a channel's episodes in a random order, like cards from a deck: each
/// one airs once per pass, and nothing comes round again until the deck has
/// been dealt out. Picking a random episode every time instead would repeat
/// favourites and leave others unaired for weeks.
/// </summary>
public sealed class ShuffleDeck<T>
{
    private readonly T[] _cards;
    private readonly Random _random;

    // Cards before this index have been dealt in the current pass.
    private int _dealt;
    private bool _anyDealt;

    public ShuffleDeck(IEnumerable<T> items, Random? random = null)
    {
        _cards = [.. items];
        _random = random ?? Random.Shared;
        _dealt = _cards.Length;
    }

    public T Next()
    {
        if (_cards.Length == 0)
            throw new InvalidOperationException("There is nothing in the deck to deal.");

        // The deck shuffles as it deals: pick any undealt card and swap it to
        // the front of the undealt run.
        var from = _dealt;
        var to = _cards.Length;
        if (_dealt == _cards.Length)
        {
            _dealt = from = 0;
            // The card that just aired sits last; leaving it out of the first
            // pick of a new pass keeps it from airing twice in a row.
            if (_anyDealt && _cards.Length > 1) to--;
        }

        var pick = _random.Next(from, to);
        (_cards[_dealt], _cards[pick]) = (_cards[pick], _cards[_dealt]);
        _anyDealt = true;
        return _cards[_dealt++];
    }
}
