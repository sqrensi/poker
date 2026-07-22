using System.Collections.Generic;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using UnityEngine;

namespace Poker.Identity
{
    /// <summary>Локальные IPv4 для LAN-режима (хост показывает друзьям).</summary>
    public static class LanAddressUtil
    {
        const string PrefsHost = "poker_lan_host";
        public const int DefaultPort = 8787;

        public static string GetSavedHost() => PlayerPrefs.GetString(PrefsHost, "");

        public static void SaveHost(string host)
        {
            PlayerPrefs.SetString(PrefsHost, host ?? "");
            PlayerPrefs.Save();
        }

        public static List<string> LocalIpv4()
        {
            var list = new List<string>();
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    var props = ni.GetIPProperties();
                    foreach (var uni in props.UnicastAddresses)
                    {
                        if (uni.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        string ip = uni.Address.ToString();
                        if (ip.StartsWith("127.")) continue;
                        if (!list.Contains(ip)) list.Add(ip);
                    }
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[Poker] LAN IP: " + e.Message);
            }
            return list;
        }

        public static string BuildUrl(string hostOrUrl, string playerId, bool autoQueue = false)
        {
            string raw = (hostOrUrl ?? "").Trim();
            if (string.IsNullOrEmpty(raw)) raw = "127.0.0.1";
            raw = raw.Replace("\\", "/");
            if (!raw.StartsWith("http://") && !raw.StartsWith("https://"))
            {
                // host or host:port
                if (!raw.Contains(":"))
                    raw = $"http://{raw}:{DefaultPort}";
                else
                    raw = "http://" + raw;
            }
            raw = raw.TrimEnd('/');
            var q = new System.Collections.Generic.List<string>();
            if (!string.IsNullOrEmpty(playerId))
                q.Add("pid=" + System.Uri.EscapeDataString(playerId));
            if (autoQueue)
                q.Add("queue=1");
            if (q.Count == 0) return raw;
            return raw + "?" + string.Join("&", q);
        }
    }
}
