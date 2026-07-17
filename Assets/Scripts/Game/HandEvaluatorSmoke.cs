using Poker.Core;
using Poker.Game;
using System.Collections.Generic;

namespace Poker.Tests
{
    /// <summary>Lightweight smoke checks — call from console or temporary button.</summary>
    public static class HandEvaluatorSmoke
    {
        public static string RunSmoke()
        {
            var royal = new[]
            {
                new Card(Rank.Ace, Suit.Spades),
                new Card(Rank.King, Suit.Spades),
                new Card(Rank.Queen, Suit.Spades),
                new Card(Rank.Jack, Suit.Spades),
                new Card(Rank.Ten, Suit.Spades)
            };
            var hv = HandEvaluator.EvaluateFive(royal);
            if (hv.Category != HandCategory.RoyalFlush)
                return "FAIL royal";

            var players = new List<Player>
            {
                new Player(0, "A", PlayerType.Human, 500),
                new Player(1, "B", PlayerType.Ai, 500)
            };
            var table = new PokerTable(players, 5, 10, deckSeed: 42);
            table.StartNewHand();
            if (table.Street != Street.Preflop)
                return "FAIL street";
            if (table.Players[0].HoleCards.Count != 2)
                return "FAIL deal";
            return "OK";
        }
    }
}
