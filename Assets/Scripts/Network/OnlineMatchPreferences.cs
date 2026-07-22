using UnityEngine;

namespace Poker.Network
{
    /// <summary>Настройки онлайн-матча (локально).</summary>
    public static class OnlineMatchPreferences
    {
        const string FillBotsKey = "poker_online_fill_bots_v1";

        /** Добирать ботов до 4 игроков при старте матча из очереди. */
        public static bool FillWithBots
        {
            get => PlayerPrefs.GetInt(FillBotsKey, 0) == 1;
            set
            {
                PlayerPrefs.SetInt(FillBotsKey, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }
    }
}
