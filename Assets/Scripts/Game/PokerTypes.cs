namespace Poker.Game
{
    public enum Street
    {
        Waiting = 0,
        Preflop = 1,
        Flop = 2,
        Turn = 3,
        River = 4,
        Showdown = 5,
        HandComplete = 6,
        /// <summary>Матч окончен: остался один игрок с фишками (или никто).</summary>
        MatchComplete = 7
    }

    public enum ActionType
    {
        Fold,
        Check,
        Call,
        Bet,
        Raise,
        AllIn
    }

    public readonly struct PlayerAction
    {
        public ActionType Type { get; }
        /// <summary>Total chips committed on this street after the action (for bet/raise), or 0.</summary>
        public int Amount { get; }

        public PlayerAction(ActionType type, int amount = 0)
        {
            Type = type;
            Amount = amount;
        }

        public override string ToString() => Amount > 0 ? $"{Type} {Amount}" : Type.ToString();
    }

    public readonly struct LegalActions
    {
        public bool CanFold { get; }
        public bool CanCheck { get; }
        public bool CanCall { get; }
        public int CallAmount { get; }
        public bool CanBet { get; }
        public bool CanRaise { get; }
        public int MinRaiseTo { get; }
        public int MaxRaiseTo { get; }
        public int Pot { get; }
        public int CurrentBet { get; }

        public LegalActions(
            bool canFold, bool canCheck, bool canCall, int callAmount,
            bool canBet, bool canRaise, int minRaiseTo, int maxRaiseTo,
            int pot, int currentBet)
        {
            CanFold = canFold;
            CanCheck = canCheck;
            CanCall = canCall;
            CallAmount = callAmount;
            CanBet = canBet;
            CanRaise = canRaise;
            MinRaiseTo = minRaiseTo;
            MaxRaiseTo = maxRaiseTo;
            Pot = pot;
            CurrentBet = currentBet;
        }
    }
}
