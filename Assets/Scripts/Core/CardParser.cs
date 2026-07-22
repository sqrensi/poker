using System;
using Poker.Core;

namespace Poker.Core
{
    public static class CardParser
    {
        public static bool TryParse(string code, out Card card)
        {
            card = default;
            if (string.IsNullOrEmpty(code) || code == "??") return false;
            if (code.Length < 2) return false;

            char r0 = code[0];
            int rank;
            switch (r0)
            {
                case 'A': case 'a': rank = 14; break;
                case 'K': case 'k': rank = 13; break;
                case 'Q': case 'q': rank = 12; break;
                case 'J': case 'j': rank = 11; break;
                case 'T': case 't': rank = 10; break;
                default:
                    if (r0 >= '2' && r0 <= '9') rank = r0 - '0';
                    else return false;
                    break;
            }

            char s0 = char.ToUpperInvariant(code[^1]);
            Suit suit = s0 switch
            {
                'C' => Suit.Clubs,
                'D' => Suit.Diamonds,
                'H' => Suit.Hearts,
                'S' => Suit.Spades,
                _ => default
            };
            if (s0 != 'C' && s0 != 'D' && s0 != 'H' && s0 != 'S') return false;

            card = new Card((Rank)rank, suit);
            return true;
        }
    }
}
