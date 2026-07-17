using System;
using UnityEngine;

namespace Poker.Identity
{
    /// <summary>
    /// Стабильный внешний ID игрока (как в ShooterPrototype, упрощённо).
    /// Сохраняется в PlayerPrefs и передаётся в браузерный онлайн как ?pid=
    /// </summary>
    public static class PlayerIdentityService
    {
        const string PrefsKey = "poker_player_external_id_v1";
        const string NickKey = "poker_player_nickname_v1";

        public static string GetOrCreatePlayerId()
        {
            string id = PlayerPrefs.GetString(PrefsKey, "");
            if (!string.IsNullOrEmpty(id) && id.Length >= 8)
                return id;

            id = "player-" + Guid.NewGuid().ToString("N");
            PlayerPrefs.SetString(PrefsKey, id);
            PlayerPrefs.Save();
            return id;
        }

        public static string GetNickname()
        {
            string n = PlayerPrefs.GetString(NickKey, "");
            if (!string.IsNullOrEmpty(n)) return n;
            string shortId = GetOrCreatePlayerId();
            shortId = shortId.Length > 4 ? shortId.Substring(shortId.Length - 4).ToUpperInvariant() : "0000";
            return "Игрок_" + shortId;
        }

        public static void SetNickname(string nickname)
        {
            TrySetNickname(nickname, out _);
        }

        /// <summary>3–16 символов: буквы, цифры, _ - и пробел.</summary>
        public static bool TrySetNickname(string nickname, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(nickname))
            {
                error = "Введите ник";
                return false;
            }
            nickname = nickname.Trim();
            if (nickname.Length < 3)
            {
                error = "Минимум 3 символа";
                return false;
            }
            if (nickname.Length > 16)
            {
                error = "Максимум 16 символов";
                return false;
            }
            for (int i = 0; i < nickname.Length; i++)
            {
                char c = nickname[i];
                if (char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == ' ')
                    continue;
                error = "Только буквы, цифры, _ -";
                return false;
            }
            PlayerPrefs.SetString(NickKey, nickname);
            PlayerPrefs.Save();
            return true;
        }
    }
}
