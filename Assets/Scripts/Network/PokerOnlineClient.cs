using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Poker.Identity;
using Poker.Identity;
using UnityEngine;

namespace Poker.Network
{
    /// <summary>WebSocket-клиент к poker-серверу (очередь + стол).</summary>
    public sealed class PokerOnlineClient : MonoBehaviour
    {
        ClientWebSocket _ws;
        CancellationTokenSource _cts;
        readonly ConcurrentQueue<Action> _main = new ConcurrentQueue<Action>();
        bool _wantQueueAfterAuth;

        public bool Connected { get; private set; }
        public bool Authed { get; private set; }
        public int Coins { get; private set; }

        public event Action ConnectedEvent;
        public event Action<string> DisconnectedEvent;
        public event Action<OnlineProfile> ProfileEvent;
        public event Action<OnlineQueueStatus> QueueStatusEvent;
        public event Action QueueLeftEvent;
        public event Action MatchedEvent;
        public event Action<OnlineGameState> StateEvent;
        public event Action<string> ErrorEvent;

        public void Connect(string hostOrUrl)
        {
            Disconnect();
            StartCoroutine(ConnectRoutine(hostOrUrl));
        }

        public void AuthAndQueue(string playerId, string nickname)
        {
            _wantQueueAfterAuth = true;
            SendObj($"{{\"type\":\"auth\",\"playerId\":{JsonStr(playerId)},\"nickname\":{JsonStr(nickname)}}}");
        }

        public void Dequeue() => SendObj("{\"type\":\"dequeue\"}");

        public void SendAction(string action, int amount = 0)
        {
            if (amount > 0)
                SendObj($"{{\"type\":\"action\",\"action\":{JsonStr(action)},\"amount\":{amount}}}");
            else
                SendObj($"{{\"type\":\"action\",\"action\":{JsonStr(action)}}}");
        }

        public void Disconnect()
        {
            _cts?.Cancel();
            _cts = null;
            Connected = false;
            Authed = false;
            try { _ws?.Abort(); } catch { /* ignore */ }
            _ws?.Dispose();
            _ws = null;
        }

        void OnDestroy() => Disconnect();

        void Update()
        {
            while (_main.TryDequeue(out var a))
            {
                try { a?.Invoke(); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }

        IEnumerator ConnectRoutine(string hostOrUrl)
        {
            string wsUrl = LanAddressUtil.BuildWsUrl(hostOrUrl);
            _ws = new ClientWebSocket();
            _cts = new CancellationTokenSource();
            var connectTask = _ws.ConnectAsync(new Uri(wsUrl), _cts.Token);
            while (!connectTask.IsCompleted) yield return null;
            if (connectTask.IsFaulted)
            {
                EnqueueError("Не удалось подключиться к серверу");
                yield break;
            }

            Connected = true;
            Enqueue(() => ConnectedEvent?.Invoke());
            StartCoroutine(ReceiveLoop());
        }

        IEnumerator ReceiveLoop()
        {
            var buffer = new byte[16384];
            while (_ws != null && _ws.State == WebSocketState.Open && _cts is { IsCancellationRequested: false })
            {
                var segment = new ArraySegment<byte>(buffer);
                var receiveTask = _ws.ReceiveAsync(segment, _cts.Token);
                while (!receiveTask.IsCompleted) yield return null;

                if (receiveTask.IsFaulted || receiveTask.IsCanceled) break;
                var result = receiveTask.Result;
                if (result.MessageType == WebSocketMessageType.Close) break;
                if (result.MessageType != WebSocketMessageType.Text) continue;

                string json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                while (!result.EndOfMessage)
                {
                    receiveTask = _ws.ReceiveAsync(segment, _cts.Token);
                    while (!receiveTask.IsCompleted) yield return null;
                    result = receiveTask.Result;
                    json += Encoding.UTF8.GetString(buffer, 0, result.Count);
                }
                HandleJson(json);
            }

            Connected = false;
            Authed = false;
            Enqueue(() => DisconnectedEvent?.Invoke("Соединение закрыто"));
        }

        void SyncWalletCoins(int coins)
        {
            if (coins > 0)
            {
                Coins = coins;
                PlayerWalletService.SetCoins(coins);
            }
        }

        void HandleJson(string json)
        {
            Enqueue(() =>
            {
                try
                {
                    if (OnlineGameState.TryParse(json, out var state))
                    {
                        SyncWalletCoins(state.Coins);
                        StateEvent?.Invoke(state);
                        return;
                    }
                    if (OnlineQueueStatus.TryParse(json, out var qs))
                    {
                        SyncWalletCoins(qs.Coins);
                        QueueStatusEvent?.Invoke(qs);
                        return;
                    }
                    if (OnlineProfile.TryParse(json, out var profile))
                    {
                        Authed = true;
                        SyncWalletCoins(profile.Coins);
                        ProfileEvent?.Invoke(profile);
                        if (_wantQueueAfterAuth)
                        {
                            _wantQueueAfterAuth = false;
                            SendObj($"{{\"type\":\"queue\",\"nickname\":{JsonStr(profile.Nickname)}}}");
                        }
                        return;
                    }

                    if (!JsonLite.TryParse(json, out var root))
                        return;

                    string type = root.GetString("type");
                    switch (type)
                    {
                        case "welcome":
                            break;
                        case "matched":
                            MatchedEvent?.Invoke();
                            break;
                        case "queue_left":
                            if (root.TryGetProperty("coins", out var c) && c.TryGetInt32(out var cv))
                                SyncWalletCoins(cv);
                            QueueLeftEvent?.Invoke();
                            break;
                        case "error":
                            ErrorEvent?.Invoke(root.TryGetProperty("error", out var er) ? er.AsString() : "Ошибка");
                            break;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[PokerOnline] parse: " + e.Message);
                }
            });
        }

        void SendObj(string json)
        {
            if (_ws == null || _ws.State != WebSocketState.Open) return;
            var bytes = Encoding.UTF8.GetBytes(json);
            _ = _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _cts?.Token ?? CancellationToken.None);
        }

        void Enqueue(Action a) => _main.Enqueue(a);

        void EnqueueError(string msg) => Enqueue(() => ErrorEvent?.Invoke(msg));

        static string JsonStr(string s)
        {
            if (s == null) return "null";
            return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }
    }
}
