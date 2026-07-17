using System;
using System.Collections.Generic;
using Poker.Core;
using Poker.Game;

namespace Poker.Presentation
{
    public static class PokerRu
    {
        public static string StreetName(Street s)
        {
            switch (s)
            {
                case Street.Waiting: return "Ожидание";
                case Street.Preflop: return "Префлоп";
                case Street.Flop: return "Флоп";
                case Street.Turn: return "Тёрн";
                case Street.River: return "Ривер";
                case Street.Showdown: return "Вскрытие";
                case Street.HandComplete: return "Раздача окончена";
                default: return s.ToString();
            }
        }

        public static string CategoryName(HandCategory c)
        {
            switch (c)
            {
                case HandCategory.HighCard: return "Старшая карта";
                case HandCategory.OnePair: return "Пара";
                case HandCategory.TwoPair: return "Две пары";
                case HandCategory.ThreeOfAKind: return "Сет";
                case HandCategory.Straight: return "Стрит";
                case HandCategory.Flush: return "Флеш";
                case HandCategory.FullHouse: return "Фулл-хаус";
                case HandCategory.FourOfAKind: return "Каре";
                case HandCategory.StraightFlush: return "Стрит-флеш";
                case HandCategory.RoyalFlush: return "Рояль-флеш";
                default: return c.ToString();
            }
        }

        public static string RankName(Rank r)
        {
            switch (r)
            {
                case Rank.Ace: return "туз";
                case Rank.King: return "король";
                case Rank.Queen: return "дама";
                case Rank.Jack: return "валет";
                case Rank.Ten: return "десятка";
                case Rank.Nine: return "девятка";
                case Rank.Eight: return "восьмёрка";
                case Rank.Seven: return "семёрка";
                case Rank.Six: return "шестёрка";
                case Rank.Five: return "пятёрка";
                case Rank.Four: return "четвёрка";
                case Rank.Three: return "тройка";
                case Rank.Two: return "двойка";
                default: return r.ToString();
            }
        }

        public static string RankName(int rank) => RankName((Rank)rank);

        public static string RankNameGenitivePlural(int rank)
        {
            switch ((Rank)rank)
            {
                case Rank.Ace: return "тузов";
                case Rank.King: return "королей";
                case Rank.Queen: return "дам";
                case Rank.Jack: return "валетов";
                case Rank.Ten: return "десяток";
                case Rank.Nine: return "девяток";
                case Rank.Eight: return "восьмёрок";
                case Rank.Seven: return "семёрок";
                case Rank.Six: return "шестёрок";
                case Rank.Five: return "пятёрок";
                case Rank.Four: return "четвёрок";
                case Rank.Three: return "троек";
                case Rank.Two: return "двоек";
                default: return rank.ToString();
            }
        }
    }

    /// <summary>Подсказки текущей комбинации игрока.</summary>
    public static class HandHint
    {
        public static string ForPlayer(Player player, IReadOnlyList<Card> board, Street street)
        {
            if (player == null || player.HasFolded)
                return "Вы вне раздачи";
            if (player.HoleCards.Count < 2)
                return "Ожидание карт…";

            if (board == null || board.Count < 3)
                return DescribePreflop(player.HoleCards[0], player.HoleCards[1]);

            var seven = new List<Card>(7);
            seven.AddRange(player.HoleCards);
            seven.AddRange(board);
            var hv = HandEvaluator.EvaluateBest(seven);
            string stage = board.Count == 3 ? "на флопе" : board.Count == 4 ? "на тёрне" : "на ривере";
            if (street >= Street.Showdown)
                stage = "на вскрытии";

            // Уточнение: старшая карта часто лежит на борде (ваши 10–9 тоже участвуют как кикеры)
            if (hv.Category == HandCategory.HighCard)
            {
                int boardHigh = 0;
                foreach (var c in board)
                {
                    int r = (int)c.Rank;
                    if (r > boardHigh) boardHigh = r;
                }
                int holeHigh = Math.Max((int)player.HoleCards[0].Rank, (int)player.HoleCards[1].Rank);
                if (boardHigh > holeHigh)
                {
                    return $"Старшая карта {stage}\n" +
                           $"{Capitalize(PokerRu.RankName((Rank)boardHigh))} на борде " +
                           $"(ваши карты — кикеры: {PokerRu.RankName(player.HoleCards[0].Rank)}, {PokerRu.RankName(player.HoleCards[1].Rank)})";
                }
            }

            return $"{PokerRu.CategoryName(hv.Category)} {stage}\n{hv.Description}";
        }

        static string DescribePreflop(Card a, Card b)
        {
            bool pair = a.Rank == b.Rank;
            bool suited = a.Suit == b.Suit;
            int hi = (int)a.Rank >= (int)b.Rank ? (int)a.Rank : (int)b.Rank;
            int lo = (int)a.Rank < (int)b.Rank ? (int)a.Rank : (int)b.Rank;
            int gap = hi - lo;

            if (pair)
                return $"Карманная пара {PokerRu.RankNameGenitivePlural(hi)}\nСильная стартовая рука";

            string suitedMark = suited ? "одномастные" : "разномастные";
            if (gap == 1)
                return $"{Capitalize(PokerRu.RankName((Rank)hi))} и {PokerRu.RankName((Rank)lo)}, {suitedMark} коннекторы";
            if (gap == 2)
                return $"{Capitalize(PokerRu.RankName((Rank)hi))} и {PokerRu.RankName((Rank)lo)}, {suitedMark}, гэп 1";

            if (hi >= 12 && lo >= 10)
                return $"Высокие карты: {Capitalize(PokerRu.RankName((Rank)hi))} / {PokerRu.RankName((Rank)lo)} ({suitedMark})";

            if (hi == 14)
                return $"Туз и {PokerRu.RankName((Rank)lo)} ({suitedMark})";

            return $"{Capitalize(PokerRu.RankName((Rank)hi))} / {PokerRu.RankName((Rank)lo)} ({suitedMark})";
        }

        static string Capitalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return char.ToUpperInvariant(s[0]) + s.Substring(1);
        }
    }
}
