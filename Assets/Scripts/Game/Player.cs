using System.Collections.Generic;
using Poker.Core;

namespace Poker.Game
{
    public enum PlayerType
    {
        Human,
        Ai
    }

    public sealed class Player
    {
        public int SeatIndex { get; }
        public string Name { get; }
        public PlayerType Type { get; }
        public int Chips { get; set; }
        public List<Card> HoleCards { get; } = new List<Card>(2);
        public int BetThisStreet { get; set; }
        public int TotalBetThisHand { get; set; }
        public bool HasFolded { get; set; }
        public bool IsAllIn { get; set; }
        public bool HasActedThisStreet { get; set; }

        public bool IsInHand => !HasFolded;
        public bool CanAct => !HasFolded && !IsAllIn && Chips > 0;

        public Player(int seatIndex, string name, PlayerType type, int startingChips)
        {
            SeatIndex = seatIndex;
            Name = name;
            Type = type;
            Chips = startingChips;
        }

        public void ResetForNewHand()
        {
            HoleCards.Clear();
            BetThisStreet = 0;
            TotalBetThisHand = 0;
            HasFolded = false;
            IsAllIn = false;
            HasActedThisStreet = false;
        }

        public void ResetStreet()
        {
            BetThisStreet = 0;
            HasActedThisStreet = false;
        }

        public int CommitChips(int amount)
        {
            if (amount <= 0) return 0;
            int paid = amount > Chips ? Chips : amount;
            Chips -= paid;
            BetThisStreet += paid;
            TotalBetThisHand += paid;
            if (Chips == 0)
                IsAllIn = true;
            return paid;
        }
    }
}
