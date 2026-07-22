using UnityEngine;

namespace Poker.Identity
{
    /// <summary>Локальный кошелёк (офлайн + кэш после онлайн-сессии).</summary>
    public static class PlayerWalletService
    {
        public const int DefaultCoins = 50_000;
        public const int GameBuyIn = 5_000;

        const string CoinsKey = "poker_player_coins_v1";

        public static int GetCoins()
        {
            if (!PlayerPrefs.HasKey(CoinsKey))
                return DefaultCoins;
            return PlayerPrefs.GetInt(CoinsKey, DefaultCoins);
        }

        public static void SetCoins(int coins)
        {
            PlayerPrefs.SetInt(CoinsKey, Mathf.Max(0, coins));
            PlayerPrefs.Save();
        }

        public static void AddCoins(int amount)
        {
            if (amount <= 0) return;
            SetCoins(GetCoins() + amount);
        }

        public static bool CanAffordBuyIn() => GetCoins() >= GameBuyIn;

        public static bool TryChargeBuyIn(out string error)
        {
            error = null;
            int coins = GetCoins();
            if (coins < GameBuyIn)
            {
                error = $"Недостаточно монет (нужно {GameBuyIn}, у вас {coins})";
                return false;
            }
            SetCoins(coins - GameBuyIn);
            return true;
        }

        public static string FormatCoins(int coins) => coins.ToString("N0").Replace(",", " ");
    }
}
