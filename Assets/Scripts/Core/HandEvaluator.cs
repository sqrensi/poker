using System;
using System.Collections.Generic;

namespace Poker.Core
{
    public enum HandCategory
    {
        HighCard = 0,
        OnePair = 1,
        TwoPair = 2,
        ThreeOfAKind = 3,
        Straight = 4,
        Flush = 5,
        FullHouse = 6,
        FourOfAKind = 7,
        StraightFlush = 8,
        RoyalFlush = 9
    }

    public readonly struct HandValue : IComparable<HandValue>
    {
        public HandCategory Category { get; }
        public int Score { get; }
        public string Description { get; }

        public HandValue(HandCategory category, int score, string description)
        {
            Category = category;
            Score = score;
            Description = description;
        }

        public int CompareTo(HandValue other) => Score.CompareTo(other.Score);
        public override string ToString() => Description;
    }

    public static class HandEvaluator
    {
        public static HandValue EvaluateBest(IReadOnlyList<Card> cards)
        {
            if (cards == null || cards.Count < 5)
                throw new ArgumentException("Need at least 5 cards.");

            HandValue best = default;
            bool hasBest = false;
            var buffer = new Card[5];
            var indices = new int[5];

            void EvaluateCurrent()
            {
                for (int i = 0; i < 5; i++)
                    buffer[i] = cards[indices[i]];
                HandValue value = EvaluateFive(buffer);
                if (!hasBest || value.CompareTo(best) > 0)
                {
                    best = value;
                    hasBest = true;
                }
            }

            void Combine(int start, int depth)
            {
                if (depth == 5)
                {
                    EvaluateCurrent();
                    return;
                }

                for (int i = start; i <= cards.Count - (5 - depth); i++)
                {
                    indices[depth] = i;
                    Combine(i + 1, depth + 1);
                }
            }

            Combine(0, 0);
            return best;
        }

        public static HandValue EvaluateFive(Card[] cards)
        {
            if (cards.Length != 5)
                throw new ArgumentException("Exactly 5 cards required.");

            int[] ranks = new int[5];
            int[] suits = new int[5];
            for (int i = 0; i < 5; i++)
            {
                ranks[i] = (int)cards[i].Rank;
                suits[i] = (int)cards[i].Suit;
            }

            Array.Sort(ranks);
            Array.Reverse(ranks);

            bool flush = suits[0] == suits[1] && suits[1] == suits[2] &&
                         suits[2] == suits[3] && suits[3] == suits[4];

            int straightHigh = StraightHigh(ranks);
            bool straight = straightHigh > 0;

            var counts = new Dictionary<int, int>(5);
            foreach (int r in ranks)
            {
                counts.TryGetValue(r, out int c);
                counts[r] = c + 1;
            }

            var groups = new List<(int rank, int count)>(counts.Count);
            foreach (var kv in counts)
                groups.Add((kv.Key, kv.Value));
            groups.Sort((a, b) =>
            {
                int cmp = b.count.CompareTo(a.count);
                return cmp != 0 ? cmp : b.rank.CompareTo(a.rank);
            });

            if (straight && flush)
            {
                if (straightHigh == 14)
                    return Pack(HandCategory.RoyalFlush, straightHigh, "Рояль-флеш");
                return Pack(HandCategory.StraightFlush, straightHigh, $"Стрит-флеш до {Name(straightHigh)}");
            }

            if (groups[0].count == 4)
            {
                int quad = groups[0].rank;
                int kicker = groups[1].rank;
                return Pack(HandCategory.FourOfAKind, quad, kicker, $"Каре {NameGen(quad)}");
            }

            if (groups[0].count == 3 && groups[1].count == 2)
            {
                int trips = groups[0].rank;
                int pair = groups[1].rank;
                return Pack(HandCategory.FullHouse, trips, pair, $"Фулл-хаус: {NameGen(trips)} и {NameGen(pair)}");
            }

            if (flush)
                return Pack(HandCategory.Flush, ranks[0], ranks[1], ranks[2], ranks[3], ranks[4],
                    $"Флеш до {Name(ranks[0])}");

            if (straight)
                return Pack(HandCategory.Straight, straightHigh, $"Стрит до {Name(straightHigh)}");

            if (groups[0].count == 3)
            {
                int trips = groups[0].rank;
                int k1 = groups[1].rank;
                int k2 = groups[2].rank;
                return Pack(HandCategory.ThreeOfAKind, trips, k1, k2, $"Сет {NameGen(trips)}");
            }

            if (groups[0].count == 2 && groups[1].count == 2)
            {
                int highPair = Math.Max(groups[0].rank, groups[1].rank);
                int lowPair = Math.Min(groups[0].rank, groups[1].rank);
                int kicker = groups[2].rank;
                return Pack(HandCategory.TwoPair, highPair, lowPair, kicker,
                    $"Две пары: {NameGen(highPair)} и {NameGen(lowPair)}");
            }

            if (groups[0].count == 2)
            {
                int pair = groups[0].rank;
                int k1 = groups[1].rank;
                int k2 = groups[2].rank;
                int k3 = groups[3].rank;
                return Pack(HandCategory.OnePair, pair, k1, k2, k3, $"Пара {NameGen(pair)}");
            }

            return Pack(HandCategory.HighCard, ranks[0], ranks[1], ranks[2], ranks[3], ranks[4],
                $"Старшая карта: {NameNom(ranks[0])}");
        }

        static int StraightHigh(int[] ranksDesc)
        {
            var unique = new List<int>(5);
            foreach (int r in ranksDesc)
            {
                if (unique.Count == 0 || unique[unique.Count - 1] != r)
                    unique.Add(r);
            }

            if (unique.Count != 5)
                return 0;

            if (unique[0] - unique[4] == 4)
                return unique[0];

            if (unique[0] == 14 && unique[1] == 5 && unique[2] == 4 &&
                unique[3] == 3 && unique[4] == 2)
                return 5;

            return 0;
        }

        static HandValue Pack(HandCategory cat, int a, string desc) =>
            new HandValue(cat, ((int)cat << 20) | (a << 16), desc);

        static HandValue Pack(HandCategory cat, int a, int b, string desc) =>
            new HandValue(cat, ((int)cat << 20) | (a << 16) | (b << 12), desc);

        static HandValue Pack(HandCategory cat, int a, int b, int c, string desc) =>
            new HandValue(cat, ((int)cat << 20) | (a << 16) | (b << 12) | (c << 8), desc);

        static HandValue Pack(HandCategory cat, int a, int b, int c, int d, string desc) =>
            new HandValue(cat, ((int)cat << 20) | (a << 16) | (b << 12) | (c << 8) | (d << 4), desc);

        static HandValue Pack(HandCategory cat, int a, int b, int c, int d, int e, string desc) =>
            new HandValue(cat, ((int)cat << 20) | (a << 16) | (b << 12) | (c << 8) | (d << 4) | e, desc);

        static string Name(int rank) => rank switch
        {
            14 => "туза",
            13 => "короля",
            12 => "дамы",
            11 => "валета",
            10 => "десятки",
            9 => "девятки",
            8 => "восьмёрки",
            7 => "семёрки",
            6 => "шестёрки",
            5 => "пятёрки",
            4 => "четвёрки",
            3 => "тройки",
            2 => "двойки",
            _ => rank.ToString()
        };

        static string NameNom(int rank) => rank switch
        {
            14 => "туз",
            13 => "король",
            12 => "дама",
            11 => "валет",
            10 => "десятка",
            9 => "девятка",
            8 => "восьмёрка",
            7 => "семёрка",
            6 => "шестёрка",
            5 => "пятёрка",
            4 => "четвёрка",
            3 => "тройка",
            2 => "двойка",
            _ => rank.ToString()
        };

        static string NameGen(int rank) => rank switch
        {
            14 => "тузов",
            13 => "королей",
            12 => "дам",
            11 => "валетов",
            10 => "десяток",
            9 => "девяток",
            8 => "восьмёрок",
            7 => "семёрок",
            6 => "шестёрок",
            5 => "пятёрок",
            4 => "четвёрок",
            3 => "троек",
            2 => "двоек",
            _ => rank.ToString()
        };
    }
}
