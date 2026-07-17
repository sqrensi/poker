using System;

namespace Poker.Core
{
    public enum Suit
    {
        Clubs = 0,
        Diamonds = 1,
        Hearts = 2,
        Spades = 3
    }

    public enum Rank
    {
        Two = 2,
        Three = 3,
        Four = 4,
        Five = 5,
        Six = 6,
        Seven = 7,
        Eight = 8,
        Nine = 9,
        Ten = 10,
        Jack = 11,
        Queen = 12,
        King = 13,
        Ace = 14
    }

    [Serializable]
    public readonly struct Card : IEquatable<Card>
    {
        public Rank Rank { get; }
        public Suit Suit { get; }

        public Card(Rank rank, Suit suit)
        {
            Rank = rank;
            Suit = suit;
        }

        public bool Equals(Card other) => Rank == other.Rank && Suit == other.Suit;
        public override bool Equals(object obj) => obj is Card other && Equals(other);
        public override int GetHashCode() => ((int)Rank << 3) | (int)Suit;

        public override string ToString()
        {
            string r = Rank switch
            {
                Rank.Ten => "T",
                Rank.Jack => "J",
                Rank.Queen => "Q",
                Rank.King => "K",
                Rank.Ace => "A",
                _ => ((int)Rank).ToString()
            };
            string s = Suit switch
            {
                Suit.Clubs => "♣",
                Suit.Diamonds => "♦",
                Suit.Hearts => "♥",
                Suit.Spades => "♠",
                _ => "?"
            };
            return r + s;
        }

        public string ShortCode()
        {
            string r = Rank switch
            {
                Rank.Ten => "T",
                Rank.Jack => "J",
                Rank.Queen => "Q",
                Rank.King => "K",
                Rank.Ace => "A",
                _ => ((int)Rank).ToString()
            };
            string s = Suit switch
            {
                Suit.Clubs => "C",
                Suit.Diamonds => "D",
                Suit.Hearts => "H",
                Suit.Spades => "S",
                _ => "?"
            };
            return r + s;
        }
    }
}
