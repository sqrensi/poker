using UnityEngine;
using UnityEngine.UI;
using Poker.Identity;
using Poker.Menu;
using Poker.Presentation;

namespace Poker.Network
{
    /// <summary>Очередь онлайн-матча в Unity (без браузера).</summary>
    public sealed class OnlineMatchmakingController : MonoBehaviour
    {
        Text _title;
        Text _status;
        Text _error;
        GameObject _canvasGo;
        PokerOnlineClient _client;
        MainMenuController _menu;
        bool _enteringGame;
        bool _cancelling;
        int _coinsBeforeQueue;

        public static void StartMatchmaking(MainMenuController menu)
        {
            foreach (var mm in Object.FindObjectsOfType<OnlineMatchmakingController>())
                Object.Destroy(mm.gameObject);

            menu?.HideMenu();

            var go = new GameObject("OnlineMatchmaking");
            var ctrl = go.AddComponent<OnlineMatchmakingController>();
            ctrl._menu = menu;
        }

        void Start()
        {
            _coinsBeforeQueue = PlayerWalletService.GetCoins();
            UiTheme.WarmUp();
            BuildUi();

            var clientGo = new GameObject("PokerOnlineClient");
            DontDestroyOnLoad(clientGo);
            _client = clientGo.AddComponent<PokerOnlineClient>();
            _client.ErrorEvent += OnError;
            _client.QueueStatusEvent += OnQueueStatus;
            _client.MatchedEvent += OnMatched;
            _client.StateEvent += OnState;
            _client.DisconnectedEvent += OnDisconnected;
            _client.QueueLeftEvent += OnQueueLeft;

            string wsUrl = PokerNetworkConfigProvider.ResolveWsUrl();
            _status.text = "Подключение…";
            _client.Connect(wsUrl);
            StartCoroutine(BeginAfterConnect());
        }

        System.Collections.IEnumerator BeginAfterConnect()
        {
            float timeout = PokerNetworkConfigProvider.ConnectTimeoutSeconds;
            float t = 0f;
            while (!_client.Connected && t < timeout)
            {
                t += Time.deltaTime;
                yield return null;
            }
            if (!_client.Connected)
            {
                _error.text = "Сервер недоступен";
                yield break;
            }
            string id = PlayerIdentityService.GetOrCreatePlayerId();
            string nick = PlayerIdentityService.GetNickname();
            _status.text = "Вход…";
            _client.AuthAndQueue(id, nick);
        }

        void OnQueueStatus(OnlineQueueStatus qs)
        {
            _error.text = "";
            int max = qs.MaxPlayers > 0 ? qs.MaxPlayers : 4;
            _status.text = $"В очереди: {qs.QueueSize} / {max}\nОжидание: {qs.WaitedSec} сек";
            if (qs.QueueSize >= qs.MinPlayers && qs.QueueSize < max)
                _status.text += $"\nСтарт через {qs.FillTimeoutSec} сек без новых игроков";
        }

        void OnMatched()
        {
            _status.text = "Матч найден! Загрузка стола…";
        }

        void OnState(OnlineGameState state)
        {
            if (_enteringGame || !state.Started) return;
            _enteringGame = true;
            _client.StateEvent -= OnState;
            if (_canvasGo != null)
            {
                Destroy(_canvasGo);
                _canvasGo = null;
            }
            PokerGameController.StartOnline(_client, state);
            Destroy(gameObject);
        }

        void OnError(string msg)
        {
            if (_error != null) _error.text = msg ?? "Ошибка";
        }

        void OnDisconnected(string msg)
        {
            if (_enteringGame || _cancelling) return;
            if (_error != null) _error.text = msg;
        }

        void OnQueueLeft()
        {
            if (!_cancelling) return;
            FinishCancel();
        }

        void CancelSearch()
        {
            if (_cancelling || _enteringGame) return;
            _cancelling = true;
            if (_status != null) _status.text = "Отмена…";

            if (_client != null && _client.Connected)
            {
                _client.Dequeue();
                StartCoroutine(FinishCancelAfterTimeout());
                return;
            }

            FinishCancel();
        }

        System.Collections.IEnumerator FinishCancelAfterTimeout()
        {
            yield return new WaitForSeconds(1.5f);
            if (_cancelling)
                FinishCancel();
        }

        void FinishCancel()
        {
            PlayerWalletService.SetCoins(_coinsBeforeQueue);

            _cancelling = false;
            if (_client != null)
            {
                _client.ErrorEvent -= OnError;
                _client.QueueStatusEvent -= OnQueueStatus;
                _client.MatchedEvent -= OnMatched;
                _client.StateEvent -= OnState;
                _client.DisconnectedEvent -= OnDisconnected;
                _client.QueueLeftEvent -= OnQueueLeft;
                _client.Disconnect();
                Destroy(_client.gameObject);
                _client = null;
            }
            if (_canvasGo != null)
            {
                Destroy(_canvasGo);
                _canvasGo = null;
            }

            var menu = _menu;
            Destroy(gameObject);

            if (menu != null)
                menu.OnMatchmakingCancelled();
            else
                MainMenuController.Open();
        }

        void BuildUi()
        {
            _canvasGo = new GameObject("MatchmakingCanvas");
            var canvasGo = _canvasGo;
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 60;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            canvasGo.AddComponent<GraphicRaycaster>();
            MobileLayout.EnsureTouchInput();

            if (Object.FindObjectOfType<UnityEngine.EventSystems.EventSystem>() == null)
            {
                var es = new GameObject("EventSystem");
                es.AddComponent<UnityEngine.EventSystems.EventSystem>();
                es.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
            }

            var bg = new GameObject("BG");
            bg.transform.SetParent(canvasGo.transform, false);
            var bgRt = bg.AddComponent<RectTransform>();
            bgRt.anchorMin = Vector2.zero;
            bgRt.anchorMax = Vector2.one;
            bgRt.offsetMin = bgRt.offsetMax = Vector2.zero;
            var bgImg = bg.AddComponent<Image>();
            bgImg.color = UiTheme.Bg;
            bgImg.sprite = UiFont.WhiteSprite();

            var card = new GameObject("Card");
            card.transform.SetParent(canvasGo.transform, false);
            var cardRt = card.AddComponent<RectTransform>();
            cardRt.anchorMin = cardRt.anchorMax = cardRt.pivot = new Vector2(0.5f, 0.5f);
            cardRt.sizeDelta = new Vector2(520f, 360f);
            var cardImg = card.AddComponent<Image>();
            cardImg.color = UiTheme.Glass;
            UiTheme.ApplyRounded(cardImg);

            _title = MakeText(card.transform, "Поиск матча", new Vector2(0f, 100f), 32, FontStyle.Bold, UiTheme.TextMain);
            _status = MakeText(card.transform, "…", new Vector2(0f, 10f), 22, FontStyle.Normal, UiTheme.Cyan);
            _error = MakeText(card.transform, "", new Vector2(0f, -70f), 20, FontStyle.Normal, UiTheme.CoralHot);

            var cancel = new GameObject("Cancel");
            cancel.transform.SetParent(card.transform, false);
            var crt = cancel.AddComponent<RectTransform>();
            crt.anchorMin = crt.anchorMax = crt.pivot = new Vector2(0.5f, 0.5f);
            crt.anchoredPosition = new Vector2(0f, -130f);
            crt.sizeDelta = new Vector2(280f, 56f);
            var cimg = cancel.AddComponent<Image>();
            cimg.color = UiTheme.GlassStrong;
            UiTheme.ApplyRounded(cimg);
            var btn = cancel.AddComponent<Button>();
            btn.targetGraphic = cimg;
            btn.onClick.AddListener(CancelSearch);
            var cancelLabel = MakeText(cancel.transform, "Отмена", Vector2.zero, 22, FontStyle.Bold, UiTheme.TextMain);
            var cancelLabelRt = cancelLabel.rectTransform;
            cancelLabelRt.anchorMin = Vector2.zero;
            cancelLabelRt.anchorMax = Vector2.one;
            cancelLabelRt.offsetMin = cancelLabelRt.offsetMax = Vector2.zero;
        }

        static Text MakeText(Transform parent, string text, Vector2 pos, int size, FontStyle style, Color color)
        {
            var go = new GameObject("Text");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(480f, 120f);
            var t = go.AddComponent<Text>();
            t.font = UiFont.Builtin();
            t.text = text;
            t.fontSize = size;
            t.fontStyle = style;
            t.color = color;
            t.alignment = TextAnchor.MiddleCenter;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            UiFont.MakeCrisp(t, 0.3f);
            return t;
        }
    }
}
