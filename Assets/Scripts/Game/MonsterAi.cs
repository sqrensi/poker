using System;
using System.Collections.Generic;
using Poker.Core;

namespace Poker.Game
{
    /// <summary>
    /// Агрессивный солверный бот: Monte Carlo equity, Chen префлоп,
    /// позиция, SPR, дро/семи-блеф, value/protection сайзинг, push/fold.
    /// </summary>
    public static class MonsterAi
    {
        const int McTrialsPreflop = 140;
        const int McTrialsPostflop = 160;

        static readonly Random Rng = new Random();
        static readonly Card[] DeckScratch = new Card[52];
        static readonly Card[] BoardScratch = new Card[5];
        static readonly List<Card> EvalScratch = new List<Card>(7);

        public static PlayerAction Decide(PokerTable table, int seat)
        {
            var legal = table.GetLegalActions(seat);
            var me = table.Players[seat];
            if (me.HoleCards.Count < 2)
                return Fallback(legal);

            int villains = CountVillains(table, seat);
            if (villains < 1) villains = 1;

            float equity = EstimateEquity(me.HoleCards, table.Board, villains,
                table.Board.Count >= 3 ? McTrialsPostflop : McTrialsPreflop);

            float chen = ChenScore(me.HoleCards[0], me.HoleCards[1]);
            float chenNorm = Clamp(chen / 20f, 0f, 1f);
            if (table.Board.Count < 3)
                equity = equity * 0.55f + chenNorm * 0.45f;

            int outs = EstimateOuts(me.HoleCards, table.Board);
            float drawEq = table.Street == Street.Flop ? outs * 0.021f
                : table.Street == Street.Turn ? outs * 0.011f : 0f;
            float effective = Clamp(equity + drawEq, 0.02f, 0.98f);

            // Лёгкий шум — не клоны, но без рандома «обезьяны»
            effective = Clamp(effective + (float)(Rng.NextDouble() * 0.03 - 0.015), 0.02f, 0.98f);

            float potOdds = legal.CallAmount > 0
                ? legal.CallAmount / (float)Math.Max(1, legal.Pot + legal.CallAmount)
                : 0f;

            float pos = PositionScore(table, seat); // 0 early … 1 button
            float stackBb = me.Chips / (float)Math.Max(1, table.BigBlind);
            float spr = legal.Pot > 0 ? me.Chips / (float)legal.Pot : stackBb;

            // Multiway: требуем больше equity
            float multiPenalty = Math.Max(0, villains - 1) * 0.035f;
            float posBonus = (pos - 0.5f) * 0.06f;

            if (table.Street == Street.Preflop && stackBb <= 14f)
                return DecideShortStack(legal, table, chen, chenNorm, potOdds, pos, stackBb, villains);

            if (legal.CanCheck)
                return DecideWhenChecked(legal, table, effective, outs, pos, spr, stackBb, multiPenalty, posBonus);

            return DecideFacingBet(legal, table, effective, potOdds, outs, pos, spr, stackBb, multiPenalty, posBonus, chenNorm);
        }

        static PlayerAction DecideShortStack(
            LegalActions legal, PokerTable table,
            float chen, float chenNorm, float potOdds, float pos, float stackBb, int villains)
        {
            // Open / ISO shove top range
            float shoveBar = villains >= 4 ? 0.62f : villains >= 3 ? 0.52f : 0.42f;
            shoveBar -= pos * 0.12f;
            if (stackBb <= 8f) shoveBar -= 0.08f;

            if (legal.CanCheck || legal.CallAmount == 0)
            {
                if (legal.CanBet && chenNorm >= shoveBar)
                    return RaiseTo(legal, legal.MaxRaiseTo);
                return new PlayerAction(ActionType.Check);
            }

            // Facing raise / open: call shove by pot odds + hand quality
            float callBar = potOdds + 0.04f + Math.Max(0, villains - 1) * 0.03f - pos * 0.04f;
            if (chenNorm >= callBar || chen >= 12f)
            {
                if (legal.CanCall) return new PlayerAction(ActionType.Call);
                if (legal.CanRaise) return RaiseTo(legal, legal.MaxRaiseTo);
            }
            if (chenNorm >= shoveBar + 0.08f && legal.CanRaise)
                return RaiseTo(legal, legal.MaxRaiseTo);
            if (legal.CanFold) return new PlayerAction(ActionType.Fold);
            return legal.CanCall ? new PlayerAction(ActionType.Call) : new PlayerAction(ActionType.Check);
        }

        static PlayerAction DecideWhenChecked(
            LegalActions legal, PokerTable table,
            float eq, int outs, float pos, float spr, float stackBb,
            float multiPenalty, float posBonus)
        {
            float valueLine = 0.58f + multiPenalty - posBonus;
            float thinValue = 0.48f + multiPenalty * 0.5f - posBonus;
            float bluffLine = 0.22f + multiPenalty;

            bool strongDraw = outs >= 8;
            bool mediumDraw = outs >= 4;

            // Монстр / натс-зона — толстый бет
            if (eq >= valueLine + 0.12f && legal.CanBet)
            {
                float potFrac = spr < 2.5f ? 0.95f : (0.70f + eq * 0.35f);
                return BetPotFraction(legal, potFrac);
            }

            // Value
            if (eq >= valueLine && legal.CanBet)
            {
                float potFrac = 0.55f + eq * 0.30f + pos * 0.08f;
                return BetPotFraction(legal, potFrac);
            }

            // Thin value / protection на мокрой доске или мультивее
            if (eq >= thinValue && legal.CanBet && (multiPenalty > 0.02f || table.Street == Street.River || Rng.NextDouble() < 0.55))
            {
                return BetPotFraction(legal, 0.40f + eq * 0.25f);
            }

            // Semi-bluff с дро
            if ((strongDraw || (mediumDraw && pos > 0.45f)) && legal.CanBet && table.Street <= Street.Turn)
            {
                float freq = strongDraw ? 0.72f : 0.45f;
                freq += pos * 0.15f;
                if (Rng.NextDouble() < freq)
                    return BetPotFraction(legal, 0.55f + pos * 0.15f);
            }

            // Чистый блеф (редко, чаще на баттоне / ривере)
            if (eq < bluffLine && legal.CanBet && table.Street >= Street.Turn)
            {
                float freq = 0.06f + pos * 0.10f;
                if (table.Street == Street.River) freq += 0.04f;
                if (Rng.NextDouble() < freq)
                    return BetPotFraction(legal, 0.55f + (float)Rng.NextDouble() * 0.25f);
            }

            // BB check-raise / donk редко — в основном чек
            if (table.Street == Street.Preflop && legal.CanBet && eq >= 0.62f - pos * 0.1f && Rng.NextDouble() < 0.18f + pos * 0.1f)
            {
                int to = Math.Max(legal.MinRaiseTo, (int)(table.BigBlind * (3.0f + pos)));
                return RaiseTo(legal, Clamp(to, legal.MinRaiseTo, legal.MaxRaiseTo));
            }

            return new PlayerAction(ActionType.Check);
        }

        static PlayerAction DecideFacingBet(
            LegalActions legal, PokerTable table,
            float eq, float potOdds, int outs, float pos, float spr, float stackBb,
            float multiPenalty, float posBonus, float chenNorm)
        {
            float required = potOdds + 0.02f + multiPenalty - posBonus * 0.5f;
            if (spr < 3f) required -= 0.03f;
            if (legal.CallAmount <= table.BigBlind) required -= 0.06f;

            float raiseValue = required + 0.18f + multiPenalty * 0.5f;
            float jamValue = 0.72f + multiPenalty * 0.3f;

            bool strongDraw = outs >= 8;
            bool playableDraw = outs >= 4 && table.Street <= Street.Turn;

            // ——— Префлоп: опен / лимп / фолд / 3-бет ———
            if (table.Street == Street.Preflop)
            {
                bool onlyBlinds = legal.CurrentBet <= table.BigBlind;
                float openBar = 0.40f - pos * 0.14f + multiPenalty * 0.5f;
                float callBar = onlyBlinds ? openBar - 0.06f : required;
                float threeBetBar = 0.58f - pos * 0.12f;

                if (onlyBlinds && chenNorm >= openBar && legal.CanRaise)
                {
                    int openTo = Math.Max(legal.MinRaiseTo, (int)(table.BigBlind * (2.3f + (1f - pos) * 1.0f)));
                    if (stackBb <= 18f && chenNorm >= openBar + 0.08f)
                        return RaiseTo(legal, legal.MaxRaiseTo);
                    return RaiseTo(legal, Clamp(openTo, legal.MinRaiseTo, legal.MaxRaiseTo));
                }

                if (chenNorm >= threeBetBar && legal.CanRaise && !onlyBlinds)
                {
                    int threeBet = Math.Max(legal.MinRaiseTo, (int)(legal.CurrentBet * (2.5f + pos * 0.5f)));
                    if (chenNorm >= 0.78f || stackBb <= 22f)
                        return RaiseTo(legal, legal.MaxRaiseTo);
                    return RaiseTo(legal, Clamp(threeBet, legal.MinRaiseTo, legal.MaxRaiseTo));
                }

                // Спекулятивный 3-бет с баттона
                if (!onlyBlinds && legal.CanRaise && pos >= 0.7f && chenNorm >= 0.42f && Rng.NextDouble() < 0.22f)
                {
                    int threeBet = Math.Max(legal.MinRaiseTo, (int)(legal.CurrentBet * 2.8f));
                    return RaiseTo(legal, Clamp(threeBet, legal.MinRaiseTo, legal.MaxRaiseTo));
                }

                if (legal.CanCall && (chenNorm >= callBar || (onlyBlinds && chenNorm >= 0.32f && pos > 0.55f)))
                    return new PlayerAction(ActionType.Call);

                if (legal.CanFold) return new PlayerAction(ActionType.Fold);
                if (legal.CanCall) return new PlayerAction(ActionType.Call);
                return new PlayerAction(ActionType.Check);
            }

            // Фоллд мусор
            if (eq + (playableDraw ? 0.06f : 0f) < required - 0.07f && !strongDraw)
            {
                if (legal.CanFold) return new PlayerAction(ActionType.Fold);
            }

            // Value raise
            if (eq >= raiseValue && legal.CanRaise)
            {
                float potFrac = eq >= jamValue || spr < 2.2f ? 1.15f : (0.65f + eq * 0.45f);
                if (eq >= jamValue && stackBb <= 25f)
                    return RaiseTo(legal, legal.MaxRaiseTo);
                return RaisePotFraction(legal, potFrac);
            }

            // Semi-bluff raise
            if (strongDraw && legal.CanRaise && eq >= potOdds - 0.02f && Rng.NextDouble() < 0.40f + pos * 0.25f)
                return RaisePotFraction(legal, 0.65f + pos * 0.2f);

            // Колл по цене
            if (legal.CanCall)
            {
                if (eq >= required - 0.02f || strongDraw || (playableDraw && eq >= required - 0.08f))
                    return new PlayerAction(ActionType.Call);
                if (legal.CallAmount <= table.BigBlind && eq >= 0.28f)
                    return new PlayerAction(ActionType.Call);
                if (playableDraw && stackBb >= 40f && eq >= required - 0.12f)
                    return new PlayerAction(ActionType.Call);
            }

            if (legal.CanFold) return new PlayerAction(ActionType.Fold);
            if (legal.CanCall) return new PlayerAction(ActionType.Call);
            return new PlayerAction(ActionType.Check);
        }

        static PlayerAction BetPotFraction(LegalActions legal, float potFrac)
        {
            if (!legal.CanBet) return new PlayerAction(ActionType.Check);
            int amount = Clamp((int)(legal.Pot * potFrac), legal.MinRaiseTo, legal.MaxRaiseTo);
            return new PlayerAction(ActionType.Bet, amount);
        }

        static PlayerAction RaisePotFraction(LegalActions legal, float potFrac)
        {
            if (!legal.CanRaise) return legal.CanCall ? new PlayerAction(ActionType.Call) : new PlayerAction(ActionType.Fold);
            int raiseBy = Math.Max(legal.MinRaiseTo - legal.CurrentBet, (int)(legal.Pot * potFrac));
            int to = Clamp(legal.CurrentBet + raiseBy, legal.MinRaiseTo, legal.MaxRaiseTo);
            return new PlayerAction(ActionType.Raise, to);
        }

        static PlayerAction RaiseTo(LegalActions legal, int to)
        {
            to = Clamp(to, legal.MinRaiseTo, legal.MaxRaiseTo);
            if (legal.CanRaise || legal.CanBet)
                return new PlayerAction(legal.CanBet && legal.CurrentBet == 0 ? ActionType.Bet : ActionType.Raise, to);
            if (legal.CanCall) return new PlayerAction(ActionType.Call);
            return new PlayerAction(ActionType.Check);
        }

        static PlayerAction Fallback(LegalActions legal)
        {
            if (legal.CanCheck) return new PlayerAction(ActionType.Check);
            if (legal.CanCall && legal.CallAmount <= 0) return new PlayerAction(ActionType.Call);
            if (legal.CanFold) return new PlayerAction(ActionType.Fold);
            return new PlayerAction(ActionType.Check);
        }

        // ——— Equity ———

        public static float EstimateEquity(IReadOnlyList<Card> hole, IReadOnlyList<Card> board, int opponents, int trials)
        {
            opponents = Math.Max(1, Math.Min(opponents, 8));
            trials = Math.Max(40, trials);

            int deckLen = BuildRemainingDeck(hole, board);
            if (deckLen < 2 * opponents + Math.Max(0, 5 - board.Count))
                return 0.35f;

            double scoreSum = 0;
            int boardNeed = Math.Max(0, 5 - board.Count);

            for (int t = 0; t < trials; t++)
            {
                ShufflePrefix(deckLen);

                int idx = 0;
                // Complete board
                for (int i = 0; i < board.Count; i++)
                    BoardScratch[i] = board[i];
                for (int i = 0; i < boardNeed; i++)
                    BoardScratch[board.Count + i] = DeckScratch[idx++];

                int heroScore = EvalSeven(hole[0], hole[1], BoardScratch);

                int bestOpp = int.MinValue;
                int tiesAtBest = 0;
                for (int o = 0; o < opponents; o++)
                {
                    Card o0 = DeckScratch[idx++];
                    Card o1 = DeckScratch[idx++];
                    int s = EvalSeven(o0, o1, BoardScratch);
                    if (s > bestOpp)
                    {
                        bestOpp = s;
                        tiesAtBest = 1;
                    }
                    else if (s == bestOpp)
                        tiesAtBest++;
                }

                if (heroScore > bestOpp)
                    scoreSum += 1.0;
                else if (heroScore == bestOpp)
                    scoreSum += 1.0 / (1 + tiesAtBest);
            }

            return (float)(scoreSum / trials);
        }

        static int EvalSeven(Card h0, Card h1, Card[] board5)
        {
            EvalScratch.Clear();
            EvalScratch.Add(h0);
            EvalScratch.Add(h1);
            for (int i = 0; i < 5; i++)
                EvalScratch.Add(board5[i]);
            return HandEvaluator.EvaluateBest(EvalScratch).Score;
        }

        static int BuildRemainingDeck(IReadOnlyList<Card> hole, IReadOnlyList<Card> board)
        {
            int n = 0;
            for (int s = 0; s < 4; s++)
            {
                for (int r = 2; r <= 14; r++)
                {
                    var c = new Card((Rank)r, (Suit)s);
                    if (Contains(hole, c) || Contains(board, c)) continue;
                    DeckScratch[n++] = c;
                }
            }
            return n;
        }

        static bool Contains(IReadOnlyList<Card> list, Card c)
        {
            for (int i = 0; i < list.Count; i++)
                if (list[i].Equals(c)) return true;
            return false;
        }

        static void ShufflePrefix(int n)
        {
            for (int i = n - 1; i > 0; i--)
            {
                int j = Rng.Next(i + 1);
                (DeckScratch[i], DeckScratch[j]) = (DeckScratch[j], DeckScratch[i]);
            }
        }

        // ——— Chen ———

        public static float ChenScore(Card a, Card b)
        {
            int hi = Math.Max((int)a.Rank, (int)b.Rank);
            int lo = Math.Min((int)a.Rank, (int)b.Rank);
            bool suited = a.Suit == b.Suit;
            bool pair = a.Rank == b.Rank;

            float ScoreRank(int r) => r switch
            {
                14 => 10f,
                13 => 8f,
                12 => 7f,
                11 => 6f,
                10 => 5f,
                _ => r / 2f
            };

            float score = ScoreRank(hi);
            if (pair)
            {
                score = Math.Max(5f, score * 2f);
            }
            else
            {
                if (suited) score += 2f;
                int gap = hi - lo - 1;
                if (gap == 0) score += 1f;
                else if (gap == 1) score += 0f;
                else if (gap == 2) score -= 1f;
                else if (gap == 3) score -= 2f;
                else score -= 4f;
                if (gap <= 1 && hi < 12) score += 1f;
            }

            return Math.Max(0f, score);
        }

        // ——— Outs / texture ———

        static int EstimateOuts(IReadOnlyList<Card> hole, IReadOnlyList<Card> board)
        {
            if (board == null || board.Count < 3 || board.Count >= 5) return 0;

            int outs = 0;
            int[] suits = new int[4];
            int[] ranks = new int[15];

            void Add(Card c)
            {
                suits[(int)c.Suit]++;
                ranks[(int)c.Rank]++;
            }

            Add(hole[0]);
            Add(hole[1]);
            for (int i = 0; i < board.Count; i++) Add(board[i]);

            // Flush draw
            for (int s = 0; s < 4; s++)
            {
                if (suits[s] == 4)
                {
                    bool holeInSuit = hole[0].Suit == (Suit)s || hole[1].Suit == (Suit)s;
                    if (holeInSuit) outs += 9;
                }
            }

            // Straight-ish: count unique ranks in window
            var present = new bool[15];
            for (int r = 2; r <= 14; r++)
                if (ranks[r] > 0) present[r] = true;
            if (present[14]) present[1] = true; // wheel

            int bestNeed = 5;
            for (int high = 5; high <= 14; high++)
            {
                int have = 0;
                for (int r = high - 4; r <= high; r++)
                {
                    int rr = r == 1 ? 1 : r;
                    if (rr >= 1 && rr <= 14 && present[rr]) have++;
                }
                int need = 5 - have;
                if (need < bestNeed) bestNeed = need;
            }

            if (bestNeed == 1) outs += 8;      // OESD-ish
            else if (bestNeed == 2) outs += 4; // gutshot-ish

            // Overcards to board (weak outs)
            int boardHigh = 0;
            for (int i = 0; i < board.Count; i++)
                boardHigh = Math.Max(boardHigh, (int)board[i].Rank);
            if ((int)hole[0].Rank > boardHigh && hole[0].Rank != hole[1].Rank) outs += 3;
            if ((int)hole[1].Rank > boardHigh && hole[0].Rank != hole[1].Rank) outs += 3;

            return Math.Min(outs, 15);
        }

        static int CountVillains(PokerTable table, int seat)
        {
            int n = 0;
            for (int i = 0; i < table.Players.Count; i++)
            {
                if (i == seat) continue;
                var p = table.Players[i];
                if (p.IsInHand) n++;
            }
            return n;
        }

        /// <summary>0 = earliest, 1 = button.</summary>
        static float PositionScore(PokerTable table, int seat)
        {
            int n = table.Players.Count;
            if (n <= 1) return 0.5f;
            int dist = (seat - table.DealerSeat + n) % n; // 0 = BTN
            // Среди оставшихся в раздаче — нормализуем
            return 1f - dist / (float)(n - 1);
        }

        static int Clamp(int v, int min, int max) => v < min ? min : (v > max ? max : v);
        static float Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);
    }
}
