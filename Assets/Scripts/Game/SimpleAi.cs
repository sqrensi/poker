using Poker.Core;

namespace Poker.Game
{
    /// <summary>Локальные боты — делегирует в MonsterAi.</summary>
    public static class SimpleAi
    {
        public static PlayerAction Decide(PokerTable table, int seat)
            => MonsterAi.Decide(table, seat);
    }
}
