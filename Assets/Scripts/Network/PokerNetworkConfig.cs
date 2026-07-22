using Poker.Identity;
using UnityEngine;

namespace Poker.Network
{
    [CreateAssetMenu(
        fileName = "PokerNetworkConfig",
        menuName = "Poker/Network Config",
        order = 1)]
    public sealed class PokerNetworkConfig : ScriptableObject
    {
        [Header("WebSocket (Editor / dev)")]
        [SerializeField] string serverWsUrl = "ws://127.0.0.1:8787";

        [Header("WebSocket (release build)")]
        [Tooltip("Укажите ws://IP:8787 или wss://домен перед сборкой релиза.")]
        [SerializeField] string serverWsUrlRelease = "";

        [SerializeField] float connectTimeoutSeconds = 8f;

        public float ConnectTimeoutSeconds => connectTimeoutSeconds;

        public string ResolveWsUrl()
        {
#if UNITY_EDITOR
            return Normalize(serverWsUrl);
#else
            if (!string.IsNullOrWhiteSpace(serverWsUrlRelease))
                return Normalize(serverWsUrlRelease);
            return Normalize(serverWsUrl);
#endif
        }

        static string Normalize(string url)
        {
            var raw = string.IsNullOrWhiteSpace(url) ? "ws://127.0.0.1:8787" : url.Trim();
            return LanAddressUtil.BuildWsUrl(raw);
        }
    }

    public static class PokerNetworkConfigProvider
    {
        const string ResourceName = "PokerNetworkConfig";
        static PokerNetworkConfig _cached;

        public static PokerNetworkConfig Load()
        {
            if (_cached == null)
                _cached = Resources.Load<PokerNetworkConfig>(ResourceName);
            return _cached;
        }

        public static string ResolveWsUrl()
        {
            var cfg = Load();
            if (cfg != null)
                return cfg.ResolveWsUrl();
#if UNITY_EDITOR
            return "ws://127.0.0.1:8787";
#else
            Debug.LogWarning("[Poker] PokerNetworkConfig не найден в Resources — localhost.");
            return "ws://127.0.0.1:8787";
#endif
        }

        public static float ConnectTimeoutSeconds =>
            Load()?.ConnectTimeoutSeconds ?? 8f;
    }
}
