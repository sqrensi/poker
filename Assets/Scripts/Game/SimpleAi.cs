using System;
using Poker.Core;

namespace Poker.Game
{
    /// <summary>Very simple heuristic AI for local bots.</summary>
    public static class SimpleAi
    {
        static readonly Random Rng = new Random();

        public static PlayerAction Decide(PokerTable table, int seat)
        {
            var legal = table.GetLegalActions(seat);
            var player = table.Players[seat];

            float strength = EstimateStrength(player, table);
            float potOdds = legal.CallAmount > 0
                ? legal.CallAmount / (float)(legal.Pot + legal.CallAmount)
                : 0f;

            // Randomize a bit so games aren't identical
            strength += (float)(Rng.NextDouble() * 0.08 - 0.04);
            strength = Clamp(strength, 0f, 1f);

            if (legal.CanCheck)
            {
                if (strength > 0.72f && legal.CanBet)
                {
                    int bet = Clamp((int)(legal.Pot * (0.5f + strength * 0.5f)), legal.MinRaiseTo, legal.MaxRaiseTo);
                    return new PlayerAction(ActionType.Bet, bet);
                }
                return new PlayerAction(ActionType.Check);
            }

            // Facing a bet
            if (strength < 0.28f && potOdds > strength + 0.05f)
                return new PlayerAction(ActionType.Fold);

            if (strength > 0.78f && legal.CanRaise)
            {
                int raiseTo = Clamp(
                    legal.CurrentBet + Math.Max(legal.MinRaiseTo - legal.CurrentBet, (int)(legal.Pot * 0.75f)),
                    legal.MinRaiseTo,
                    legal.MaxRaiseTo);
                return new PlayerAction(ActionType.Raise, raiseTo);
            }

            if (legal.CanCall)
            {
                if (strength >= potOdds - 0.05f || legal.CallAmount <= table.BigBlind)
                    return new PlayerAction(ActionType.Call);

                if (strength < 0.35f)
                    return new PlayerAction(ActionType.Fold);

                return new PlayerAction(ActionType.Call);
            }

            if (legal.CanCheck)
                return new PlayerAction(ActionType.Check);

            return new PlayerAction(ActionType.Fold);
        }

        static float EstimateStrength(Player player, PokerTable table)
        {
            if (player.HoleCards.Count < 2)
                return 0.3f;

            var a = player.HoleCards[0];
            var b = player.HoleCards[1];
            int hi = Math.Max((int)a.Rank, (int)b.Rank);
            int lo = Math.Min((int)a.Rank, (int)b.Rank);
            bool suited = a.Suit == b.Suit;
            bool pair = a.Rank == b.Rank;

            float s;
            if (pair)
                s = 0.45f + (hi - 2) / 24f;
            else
            {
                s = (hi - 2) / 30f + (lo - 2) / 50f;
                if (suited) s += 0.06f;
                if (hi - lo <= 2) s += 0.04f;
            }

            if (table.Board.Count >= 3)
            {
                try
                {
                    var cards = new System.Collections.Generic.List<Card>(7);
                    cards.AddRange(player.HoleCards);
                    cards.AddRange(table.Board);
                    if (cards.Count >= 5)
                    {
                        var hv = HandEvaluator.EvaluateBest(cards);
                        s = 0.2f + (int)hv.Category / 12f + (hv.Score & 0xF) / 80f;
                    }
                }
                catch
                {
                    // keep hole estimate
                }
            }

            return Clamp(s, 0.05f, 0.95f);
        }

        static int Clamp(int v, int min, int max) => v < min ? min : (v > max ? max : v);
        static float Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);
    }
}
