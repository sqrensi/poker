using System;
using System.Collections.Generic;

namespace Poker.Core
{
    public sealed class Deck
    {
        readonly List<Card> _cards = new List<Card>(52);
        readonly Random _rng;

        public int Remaining => _cards.Count;

        public Deck(int? seed = null)
        {
            _rng = seed.HasValue ? new Random(seed.Value) : new Random();
            Reset();
        }

        public void Reset()
        {
            _cards.Clear();
            for (int s = 0; s < 4; s++)
            {
                for (int r = 2; r <= 14; r++)
                    _cards.Add(new Card((Rank)r, (Suit)s));
            }
        }

        public void Shuffle()
        {
            for (int i = _cards.Count - 1; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                (_cards[i], _cards[j]) = (_cards[j], _cards[i]);
            }
        }

        public Card Deal()
        {
            if (_cards.Count == 0)
                throw new InvalidOperationException("Deck is empty.");
            int last = _cards.Count - 1;
            Card card = _cards[last];
            _cards.RemoveAt(last);
            return card;
        }

        public void Burn()
        {
            if (_cards.Count > 0)
                _cards.RemoveAt(_cards.Count - 1);
        }
    }
}
