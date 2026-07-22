using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Poker.Game;
using Poker.Identity;
using Poker.Network;

namespace Poker.Presentation
{
    public sealed class PokerGameController : MonoBehaviour
    {
        [SerializeField] int playerCount = 4;
        [SerializeField] int startingChips = 5000;
        [SerializeField] int smallBlind = 5;
        [SerializeField] int bigBlind = 10;
        [SerializeField] float aiDelaySeconds = 0.55f;
        [SerializeField] int raiseMultiplier = 3;

        PokerTable _table;
        readonly List<SeatView> _seats = new List<SeatView>();
        readonly List<CardView> _boardCards = new List<CardView>();
        readonly Dictionary<int, List<CardView>> _holeCards = new Dictionary<int, List<CardView>>();
        readonly List<RectTransform> _seatLabelRts = new List<RectTransform>();
        readonly List<Text> _seatNameLabels = new List<Text>();
        readonly List<Text> _seatChipLabels = new List<Text>();
        readonly List<Image> _seatLabelBgs = new List<Image>();
        readonly List<Outline> _seatLabelOutlines = new List<Outline>();
        readonly List<bool> _seatActing = new List<bool>();
        readonly HashSet<int> _foldAnimatedSeats = new HashSet<int>();
        int _shownHandNumber = -1;

        static readonly Color SeatNormalBg = new Color(0.04f, 0.06f, 0.12f, 0.88f);
        static readonly Color SeatActingBg = new Color(0.82f, 0.68f, 0.12f, 0.94f);
        static readonly Color SeatNormalOutline = new Color(1f, 1f, 1f, 0.25f);
        static readonly Color SeatActingOutline = new Color(1f, 0.9f, 0.4f, 0.58f);

        Text _hudTitle;
        Text _hudDetail;
        Text _logText;
        Text _potText;
        Button _foldBtn;
        Button _checkCallBtn;
        Button _betRaiseBtn;
        Button _raiseConfirmBtn;
        Button _raiseCancelBtn;
        Button _nextHandBtn;
        Button _rematchBtn;
        Button _menuBtn;
        Button _exitBtn;
        GameObject _raisePanel;
        Slider _raiseSlider;
        Text _raiseAmountLabel;
        bool _raiseMode;
        float _aiTimer;
        bool _aiPending;
        Camera _cam;
        TurnArrow _turnArrow;
        Transform _tableRoot;
        WinnerOverlay _winnerOverlay;
        RectTransform _canvasRoot;

        bool _onlineMode;
        PokerOnlineClient _onlineClient;
        OnlineGameState _onlineState;
        int _myServerSeat = -1;

        public static void StartOnline(PokerOnlineClient client, OnlineGameState initialState)
        {
            foreach (var g in Object.FindObjectsOfType<PokerGameController>())
                Object.Destroy(g.gameObject);
            var go = new GameObject("PokerGame");
            var ctrl = go.AddComponent<PokerGameController>();
            ctrl._onlineMode = true;
            ctrl._onlineClient = client;
            int seats = initialState?.Players?.Count ?? 2;
            ctrl.playerCount = Mathf.Clamp(seats, 2, 4);
            ctrl._onlineState = initialState;
            if (client != null)
                client.StateEvent += ctrl.OnOnlineState;
        }

        void OnOnlineState(OnlineGameState state)
        {
            _onlineState = state;
            RefreshAll();
        }

        void Start()
        {
            StartCoroutine(SoftStart());
        }

        IEnumerator SoftStart()
        {
            // 1) UI сразу — без стола
            try
            {
                BuildUi();
                if (_hudTitle != null) _hudTitle.text = "Загрузка стола…";
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                yield break;
            }

            yield return null;

            string matErr = PokerMaterials.WarmUp();
            if (matErr != null)
            {
                if (_hudTitle != null) _hudTitle.text = "Ошибка материалов: " + matErr;
                yield break;
            }

            yield return null;

            if (_onlineMode && _onlineState != null)
                playerCount = Mathf.Clamp(_onlineState.Players.Count, 2, 4);

            try
            {
                CardSpriteCatalog.EnsureLoaded();
                BuildWorld();
                VisualQuality.Apply(_cam);
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                if (_hudTitle != null) _hudTitle.text = "Ошибка сцены: " + e.Message;
                yield break;
            }

            yield return null;
            if (_onlineMode)
            {
                if (_onlineState != null)
                    ApplySeatLayout(_onlineState.Players.Count);
                ResolveMyServerSeat();
                RefreshAll();
                yield break;
            }
            StartMatch();
        }

        void Update()
        {
            if (_onlineMode)
            {
                UpdateOnline();
                return;
            }

            if (_table == null) return;

            if (_table.Street == Street.MatchComplete)
            {
                ExitRaiseMode();
                SetActionButtons(false);
                if (_nextHandBtn != null) _nextHandBtn.gameObject.SetActive(false);
                if (_rematchBtn != null)
                {
                    _rematchBtn.gameObject.SetActive(true);
                    _rematchBtn.transform.SetAsLastSibling();
                }
                if (_menuBtn != null)
                {
                    _menuBtn.gameObject.SetActive(true);
                    _menuBtn.transform.SetAsLastSibling();
                }
                return;
            }

            if (_rematchBtn != null) _rematchBtn.gameObject.SetActive(false);
            if (_menuBtn != null) _menuBtn.gameObject.SetActive(false);

            if (_table.Street == Street.HandComplete)
            {
                ExitRaiseMode();
                SetActionButtons(false);
                if (_nextHandBtn != null) _nextHandBtn.gameObject.SetActive(true);
                return;
            }

            if (_nextHandBtn != null) _nextHandBtn.gameObject.SetActive(false);

            if (_table.AwaitingHumanAction)
            {
                _aiPending = false;
                RefreshActionButtons();
                return;
            }

            ExitRaiseMode();
            SetActionButtons(false);

            if (_table.ActingSeat >= 0 &&
                _table.Players[_table.ActingSeat].Type == PlayerType.Ai &&
                _table.Players[_table.ActingSeat].CanAct)
            {
                if (!_aiPending)
                {
                    _aiPending = true;
                    _aiTimer = aiDelaySeconds;
                }

                _aiTimer -= Time.deltaTime;
                if (_aiTimer <= 0f)
                {
                    _aiPending = false;
                    int seat = _table.ActingSeat;
                    var action = SimpleAi.Decide(_table, seat);
                    _table.TryApplyAction(seat, action);
                    RefreshAll();
                }
            }
        }

        void UpdateOnline()
        {
            if (_onlineState == null) return;
            bool matchOver = _onlineState.Street == "matchComplete";
            if (matchOver)
            {
                ExitRaiseMode();
                SetActionButtons(false);
                if (_nextHandBtn != null) _nextHandBtn.gameObject.SetActive(false);
                if (_rematchBtn != null) _rematchBtn.gameObject.SetActive(false);
                if (_menuBtn != null)
                {
                    _menuBtn.gameObject.SetActive(true);
                    _menuBtn.transform.SetAsLastSibling();
                }
                return;
            }

            if (_rematchBtn != null) _rematchBtn.gameObject.SetActive(false);
            if (_menuBtn != null) _menuBtn.gameObject.SetActive(false);
            if (_nextHandBtn != null) _nextHandBtn.gameObject.SetActive(false);

            if (_onlineState.IsMyTurn(_onlineState.Legal))
                RefreshOnlineActionButtons();
            else
            {
                ExitRaiseMode();
                SetActionButtons(false);
            }
        }

        void ResolveMyServerSeat()
        {
            if (_onlineState == null) return;
            foreach (var p in _onlineState.Players)
            {
                if (p.Id == _onlineState.YouId)
                {
                    _myServerSeat = p.Seat;
                    return;
                }
            }
        }

        int VisualSeat(int serverSeat)
        {
            if (_myServerSeat < 0) return serverSeat;
            return (serverSeat - _myServerSeat + playerCount) % playerCount;
        }

        void ApplySeatLayout(int count)
        {
            playerCount = Mathf.Clamp(count, 2, 4);
            for (int i = 0; i < _seats.Count; i++)
                SetVisualSeatActive(i, i < playerCount);
        }

        void SetVisualSeatActive(int vi, bool active)
        {
            if (vi >= 0 && vi < _seats.Count && _seats[vi] != null)
                _seats[vi].gameObject.SetActive(active);
            if (vi >= 0 && vi < _seatLabelRts.Count && _seatLabelRts[vi] != null)
                _seatLabelRts[vi].gameObject.SetActive(active);
            if (!active && _holeCards.TryGetValue(vi, out var holes) && holes != null)
            {
                foreach (var h in holes)
                    if (h != null) h.gameObject.SetActive(false);
            }
        }

        void StartMatch()
        {
            var players = new List<Player>();
            players.Add(new Player(0, "Вы", PlayerType.Human, startingChips));
            for (int i = 1; i < playerCount; i++)
                players.Add(new Player(i, $"Бот {i}", PlayerType.Ai, startingChips));

            _table = new PokerTable(players, smallBlind, bigBlind);
            _table.StateChanged += RefreshAll;
            _table.HandEnded += OnHandEnded;
            _table.MatchEnded += OnMatchEnded;
            _table.StartNewHand();
            RefreshAll();
        }

        void OnHandEnded()
        {
            RefreshAll();
            if (_winnerOverlay == null || _table == null) return;
            if (_table.IsMatchOver)
                _winnerOverlay.ShowMatchEnd(_table);
            else
                _winnerOverlay.Show(_table);
        }

        void OnMatchEnded()
        {
            RefreshAll();
            if (_winnerOverlay != null && _table != null)
                _winnerOverlay.ShowMatchEnd(_table);
        }

        void BuildWorld()
        {
            _cam = Camera.main;
            if (_cam == null)
            {
                var camGo = new GameObject("Main Camera");
                _cam = camGo.AddComponent<Camera>();
                camGo.tag = "MainCamera";
                camGo.AddComponent<AudioListener>();
            }
            _cam.transform.position = new Vector3(0f, 10.2f, -8.4f);
            _cam.transform.rotation = Quaternion.Euler(55f, 0f, 0f);
            _cam.backgroundColor = new Color(0.04f, 0.06f, 0.08f);
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.fieldOfView = 46f;
            _cam.allowMSAA = true;
            _cam.allowHDR = true;

            EnsureLight("KeyLight", new Vector3(50f, -25f, 0f), new Color(1f, 0.98f, 0.92f), 1.35f);
            EnsureLight("FillLight", new Vector3(35f, 140f, 0f), new Color(0.7f, 0.8f, 1f), 0.55f);

            var tableRoot = new GameObject("PokerTable").transform;
            tableRoot.SetParent(transform, false);
            _tableRoot = tableRoot;

            // Увеличенное поле — сглаженные цилиндры (64 сегмента)
            var felt = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            felt.name = "Felt";
            felt.transform.SetParent(tableRoot, false);
            felt.transform.localScale = new Vector3(14.5f, 0.14f, 9.0f);
            SmoothMesh.ReplacePrimitiveMesh(felt, SmoothMesh.Cylinder());
            PokerMaterials.ApplyColor(felt.GetComponent<MeshRenderer>(), new Color(0.06f, 0.42f, 0.22f));

            var rim = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            rim.name = "Rim";
            rim.transform.SetParent(tableRoot, false);
            rim.transform.localScale = new Vector3(15.3f, 0.12f, 9.7f);
            rim.transform.position = new Vector3(0f, -0.04f, 0f);
            SmoothMesh.ReplacePrimitiveMesh(rim, SmoothMesh.Cylinder());
            PokerMaterials.ApplyColor(rim.GetComponent<MeshRenderer>(), new Color(0.28f, 0.14f, 0.07f));
            Object.Destroy(rim.GetComponent<Collider>());

            _turnArrow = TurnArrow.Create(tableRoot);

            // Борд ближе к центру, с запасом между картами
            var boardAnchor = new GameObject("Board").transform;
            boardAnchor.SetParent(tableRoot, false);
            boardAnchor.position = new Vector3(0f, 0.28f, 0.45f);

            const float boardSpacing = 1.2f;
            const float boardCardW = 1.05f;
            for (int i = 0; i < 5; i++)
            {
                float x = (i - 2) * boardSpacing;
                _boardCards.Add(CardView.Create(boardAnchor, new Vector3(x, 0.03f * i, 0f), 0f, boardCardW, 20 + i));
            }

            float radiusX = 5.5f;
            float radiusZ = 3.5f;
            for (int i = 0; i < playerCount; i++)
            {
                float angle = -90f + i * (360f / playerCount);
                float rad = angle * Mathf.Deg2Rad;
                var pos = new Vector3(Mathf.Cos(rad) * radiusX, 0.12f, Mathf.Sin(rad) * radiusZ);
                var seat = SeatView.Create(tableRoot, i, pos, angle);
                _seats.Add(seat);

                // Боковые места: карты повёрнуты на 90° в плоскости стола.
                bool side = Mathf.Abs(Mathf.Cos(rad)) > Mathf.Abs(Mathf.Sin(rad));
                float cardYaw = 0f;
                Vector3 o0 = new Vector3(-0.58f, 0.04f, 0f);
                Vector3 o1 = new Vector3(0.58f, 0.05f, 0f);
                Vector3? reveal0 = null;
                Vector3? reveal1 = null;
                if (side)
                {
                    // Справа/слева — вдоль стола; при вскрытии — в ряд лицом к герою.
                    cardYaw = Mathf.Cos(rad) > 0f ? 90f : -90f;
                    o0 = new Vector3(0f, 0.04f, -0.58f);
                    o1 = new Vector3(0f, 0.05f, 0.58f);
                    reveal0 = new Vector3(-0.58f, 0.04f, 0f);
                    reveal1 = new Vector3(0.58f, 0.05f, 0f);
                }
                else if (Mathf.Sin(rad) > 0.5f)
                {
                    // Верхний игрок — карты «к центру», разворот на 180°.
                    cardYaw = 180f;
                }

                var holes = new List<CardView>();
                int baseOrder = 40 + i * 5;
                holes.Add(CardView.Create(seat.CardAnchor, o0, cardYaw, 1.0f, baseOrder, reveal0));
                holes.Add(CardView.Create(seat.CardAnchor, o1, cardYaw, 1.0f, baseOrder + 1, reveal1));
                _holeCards[i] = holes;
            }
        }

        static void EnsureLight(string name, Vector3 euler, Color color, float intensity)
        {
            if (GameObject.Find(name) != null) return;
            var go = new GameObject(name);
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = color;
            light.intensity = intensity;
            light.shadows = LightShadows.None;
            go.transform.rotation = Quaternion.Euler(euler);
        }

        void BuildUi()
        {
            UiTheme.WarmUp();
            PokerSoundFx.WarmUp();

            // Canvas — дочерний объект контроллера, чтобы Destroy(gameObject) убирал весь UI.
            var canvasGo = new GameObject("Canvas");
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.referencePixelsPerUnit = 100f;
            MobileLayout.ConfigureCanvas(scaler);
            canvasGo.AddComponent<GraphicRaycaster>();
            MobileLayout.EnsureTouchInput();

            var uiRoot = SafeAreaLayout.Ensure(canvasGo.transform);
            _canvasRoot = uiRoot;

            bool phone = MobileLayout.IsPhoneLike();
            float pad = phone ? 16f : 18f;
            float bottomY = phone ? 20f : 36f;
            float btnH = phone ? 72f : 60f;

            if (Object.FindObjectOfType<UnityEngine.EventSystems.EventSystem>() == null)
            {
                var es = new GameObject("EventSystem");
                es.AddComponent<UnityEngine.EventSystems.EventSystem>();
                es.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
            }

            float hudTop = phone ? 14f : 18f;
            float hudLeft = pad;
            float hudRowH = phone ? 32f : 30f;
            float potReserve = phone ? 148f : 168f;

            _hudTitle = CreateText(uiRoot, "HandTitle",
                new Vector2(hudLeft, -hudTop), new Vector2(400f, hudRowH),
                phone ? 24 : 26, FontStyle.Bold, UiTheme.TextMain);
            var titleRt = _hudTitle.rectTransform;
            titleRt.anchorMin = new Vector2(0f, 1f);
            titleRt.anchorMax = new Vector2(1f, 1f);
            titleRt.pivot = new Vector2(0f, 1f);
            titleRt.offsetMin = new Vector2(hudLeft, -(hudTop + hudRowH));
            titleRt.offsetMax = new Vector2(-(potReserve + pad), -hudTop);
            _hudTitle.alignment = TextAnchor.MiddleLeft;
            _hudTitle.horizontalOverflow = HorizontalWrapMode.Overflow;
            _hudTitle.verticalOverflow = VerticalWrapMode.Truncate;
            _hudTitle.lineSpacing = 1f;
            _hudTitle.supportRichText = true;

            _potText = CreateText(uiRoot, "Pot",
                new Vector2(-hudLeft, -hudTop), new Vector2(potReserve, hudRowH),
                phone ? 22 : 24, FontStyle.Bold, UiTheme.CoralHot);
            var potRt = _potText.rectTransform;
            potRt.anchorMin = new Vector2(1f, 1f);
            potRt.anchorMax = new Vector2(1f, 1f);
            potRt.pivot = new Vector2(1f, 1f);
            potRt.anchoredPosition = new Vector2(-hudLeft, -hudTop);
            potRt.sizeDelta = new Vector2(potReserve, hudRowH);
            _potText.alignment = TextAnchor.MiddleRight;
            _potText.horizontalOverflow = HorizontalWrapMode.Overflow;
            _potText.verticalOverflow = VerticalWrapMode.Truncate;

            _hudDetail = null;
            _logText = _hudTitle;

            float dockW = phone ? 200f : 200f;
            float dockH = phone ? 68f : 58f;
            float dockPad = phone ? 16f : 20f;
            float dockGap = phone ? 10f : 10f;

            BuildSeatLabels(uiRoot, phone);

            _foldBtn = CreateButton(uiRoot, "Фолд", Vector2.zero,
                UiTheme.Danger, () => OnHuman(ActionType.Fold), pill: true, uiClick: false);
            _checkCallBtn = CreateButton(uiRoot, "Чек", Vector2.zero,
                UiTheme.Success, PlayCheckOrCallWithSound, pill: true, uiClick: false);
            _betRaiseBtn = CreateButton(uiRoot, "Рейз", Vector2.zero,
                UiTheme.RaiseBlue, EnterRaiseMode, pill: true, uiClick: false);

            PinBottomRight(_betRaiseBtn, dockPad, dockPad, dockW, dockH);
            PinBottomRight(_checkCallBtn, dockPad, dockPad + dockH + dockGap, dockW, dockH);
            PinBottomRight(_foldBtn, dockPad, dockPad + (dockH + dockGap) * 2f, dockW, dockH);

            BuildRaisePanel(uiRoot, phone);

            var foldLabel = _foldBtn.GetComponentInChildren<Text>();
            var checkLabel = _checkCallBtn.GetComponentInChildren<Text>();
            var betLabel0 = _betRaiseBtn.GetComponentInChildren<Text>();
            if (foldLabel != null) foldLabel.fontSize = phone ? 24 : 22;
            if (checkLabel != null) checkLabel.fontSize = phone ? 24 : 22;
            if (betLabel0 != null) betLabel0.fontSize = phone ? 24 : 22;

            _nextHandBtn = CreateButton(uiRoot, "Следующая раздача", new Vector2(0f, bottomY),
                UiTheme.Coral, () =>
                {
                    if (_winnerOverlay != null) _winnerOverlay.Hide();
                    _table.StartNewHand();
                    RefreshAll();
                }, pill: true);
            var nextRt = _nextHandBtn.GetComponent<RectTransform>();
            nextRt.sizeDelta = new Vector2(phone ? 360f : 280f, btnH);
            var nextLabel = _nextHandBtn.GetComponentInChildren<Text>();
            if (nextLabel != null && phone) nextLabel.fontSize = 24;
            _nextHandBtn.gameObject.SetActive(false);

            _rematchBtn = CreateButton(uiRoot, "Новая партия", new Vector2(phone ? -160f : -160f, bottomY),
                UiTheme.Success, OnRematch, pill: true);
            var rematchRt = _rematchBtn.GetComponent<RectTransform>();
            rematchRt.sizeDelta = new Vector2(phone ? 260f : 240f, btnH);
            _rematchBtn.gameObject.SetActive(false);

            _menuBtn = CreateButton(uiRoot, "В меню", new Vector2(phone ? 160f : 160f, bottomY),
                UiTheme.GlassStrong, ReturnToMenu, pill: true);
            var menuRt = _menuBtn.GetComponent<RectTransform>();
            menuRt.sizeDelta = new Vector2(phone ? 200f : 200f, btnH);
            _menuBtn.gameObject.SetActive(false);

            _exitBtn = CreateButton(uiRoot, "Покинуть игру", Vector2.zero,
                UiTheme.Danger, LeaveMatch, pill: true);
            PinTopRight(_exitBtn, phone ? 16f : 20f, phone ? 16f : 20f, phone ? 196f : 184f, phone ? 52f : 48f);
            var exitLabel = _exitBtn.GetComponentInChildren<Text>();
            if (exitLabel != null)
            {
                exitLabel.fontSize = phone ? 19 : 17;
                exitLabel.raycastTarget = false;
            }
            _exitBtn.transform.SetAsLastSibling();

            SetActionButtons(false);

            _winnerOverlay = WinnerOverlay.Create(uiRoot);
            // Кнопки матча поверх баннера
            if (_rematchBtn != null) _rematchBtn.transform.SetAsLastSibling();
            if (_menuBtn != null) _menuBtn.transform.SetAsLastSibling();
        }

        static void PinBottomRight(Button btn, float padRight, float padBottom, float width, float height)
        {
            if (btn == null) return;
            var rt = btn.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(1f, 0f);
            rt.anchoredPosition = new Vector2(-padRight, padBottom);
            rt.sizeDelta = new Vector2(width, height);
        }

        static void PinTopRight(Button btn, float padRight, float padTop, float width, float height)
        {
            if (btn == null) return;
            var rt = btn.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.anchoredPosition = new Vector2(-padRight, -padTop);
            rt.sizeDelta = new Vector2(width, height);
        }

        void LeaveMatch()
        {
            if (_onlineMode && _onlineClient != null && _onlineClient.Connected)
                StartCoroutine(ExitOnlineAndReturn());
            else
                FinishReturnToMenu();
        }

        System.Collections.IEnumerator ExitOnlineAndReturn()
        {
            bool done = false;
            void OnLeft() => done = true;
            _onlineClient.LeftEvent += OnLeft;
            _onlineClient.LeaveMatch();
            float t = 0f;
            while (!done && t < 2.5f)
            {
                t += Time.deltaTime;
                yield return null;
            }
            if (_onlineClient != null)
                _onlineClient.LeftEvent -= OnLeft;
            FinishReturnToMenu(skipOnlineLeave: true);
        }

        void FinishReturnToMenu(bool skipOnlineLeave = false)
        {
            CashOutOfflineStack();
            CleanupMatch(skipOnlineLeave);
            Destroy(gameObject);
            Poker.Menu.MainMenuController.Open();
        }

        void CashOutOfflineStack()
        {
            if (_onlineMode || _table == null) return;
            foreach (var p in _table.Players)
            {
                if (p.Type != PlayerType.Human || p.Chips <= 0) continue;
                PlayerWalletService.AddCoins(p.Chips);
                break;
            }
        }

        void ReturnToMenu()
        {
            LeaveMatch();
        }

        void OnRematch()
        {
            if (_table == null) return;
            ExitRaiseMode();
            if (_winnerOverlay != null) _winnerOverlay.Hide();
            _aiPending = false;
            _table.RestartMatch(startingChips);
            RefreshAll();
        }

        void CleanupMatch(bool skipOnlineLeave = false)
        {
            ExitRaiseMode();
            if (_winnerOverlay != null) _winnerOverlay.Hide();
            if (_onlineMode && _onlineClient != null)
            {
                _onlineClient.StateEvent -= OnOnlineState;
                if (!skipOnlineLeave && _onlineClient.Connected)
                    _onlineClient.LeaveMatch();
                else if (!skipOnlineLeave)
                    _onlineClient.Disconnect();
                _onlineClient = null;
            }
            if (_table != null)
            {
                _table.StateChanged -= RefreshAll;
                _table.HandEnded -= OnHandEnded;
                _table.MatchEnded -= OnMatchEnded;
                _table = null;
            }
            _aiPending = false;
        }

        void OnDestroy()
        {
            CleanupMatch();
        }

        void RefreshHudHeader(int handNumber, string combination, int pot)
        {
            if (_potText != null)
                _potText.text = $"Банк {pot}";

            if (_hudTitle == null) return;

            string combo = FlattenHint(combination);
            if (string.IsNullOrEmpty(combo))
            {
                _hudTitle.text = $"Раздача №{handNumber}";
                return;
            }

            string cyan = ColorUtility.ToHtmlStringRGB(UiTheme.Cyan);
            _hudTitle.text = $"Раздача №{handNumber} · <color=#{cyan}>{combo}</color>";
        }

        static string GetLocalCombinationName(PokerTable table)
        {
            if (table == null) return "";
            var human = table.Players[0];
            return HandHint.CombinationName(human, table.Board, table.Street);
        }

        static string GetOnlineCombinationName(OnlineGameState state, string youId)
        {
            if (state == null) return "";
            OnlineSeatPlayer me = null;
            foreach (var p in state.Players)
            {
                if (p.Id == youId) { me = p; break; }
            }
            if (me == null || me.Hole.Count < 2) return "";
            var temp = new Player(0, me.Name, PlayerType.Human, me.Chips);
            temp.HoleCards.AddRange(me.Hole);
            return HandHint.CombinationName(temp, state.Board, ParseOnlineStreet(state.Street));
        }

        static string FlattenHint(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace('\r', ' ').Replace('\n', ' ').Trim();
        }

        static Street ParseOnlineStreet(string s) => s switch
        {
            "preflop" => Street.Preflop,
            "flop" => Street.Flop,
            "turn" => Street.Turn,
            "river" => Street.River,
            "showdown" => Street.Showdown,
            "handComplete" => Street.HandComplete,
            "matchComplete" => Street.MatchComplete,
            _ => Street.Waiting
        };

        static Image CreatePanel(Transform parent, string name,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
            Vector2 anchoredPos, Vector2 size, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;
            var img = go.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            UiTheme.ApplyRounded(img);
            var edge = go.AddComponent<Outline>();
            edge.effectColor = UiTheme.GlassBorder;
            edge.effectDistance = new Vector2(1f, -1f);
            return img;
        }

        static Text CreateText(Transform parent, string name, Vector2 anchoredPos, Vector2 size,
            int fontSize, FontStyle style, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;
            var text = go.AddComponent<Text>();
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.color = color;
            text.alignment = TextAnchor.UpperLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            text.text = "";
            UiFont.MakeCrisp(text, 0.3f);
            UiTheme.StyleLabel(text);
            return text;
        }

        static Button CreateButton(Transform parent, string label, Vector2 anchoredPos, Color bg,
            UnityEngine.Events.UnityAction onClick, bool pill = false, bool uiClick = true)
        {
            var go = new GameObject(label);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = new Vector2(220f, 64f);

            var img = go.AddComponent<Image>();
            img.color = bg;
            UiTheme.ApplyRounded(img);

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            var colors = btn.colors;
            colors.highlightedColor = Color.Lerp(bg, Color.white, 0.18f);
            colors.pressedColor = Color.Lerp(bg, Color.black, 0.18f);
            colors.disabledColor = new Color(0.25f, 0.25f, 0.3f, 0.55f);
            btn.colors = colors;
            if (uiClick)
                PokerSoundFx.BindButton(btn, onClick);
            else
            {
                UiPressAnimation.Attach(btn);
                if (onClick != null)
                    btn.onClick.AddListener(onClick);
            }

            var textGo = new GameObject("Label");
            textGo.transform.SetParent(go.transform, false);
            var trt = textGo.AddComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero;
            trt.offsetMax = Vector2.zero;
            var text = textGo.AddComponent<Text>();
            text.fontSize = 24;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.text = label;
            UiFont.MakeCrisp(text, 0.35f);

            return btn;
        }

        void BuildSeatLabels(Transform canvas, bool phone)
        {
            _seatLabelRts.Clear();
            _seatNameLabels.Clear();
            _seatChipLabels.Clear();
            _seatLabelBgs.Clear();
            _seatLabelOutlines.Clear();
            _seatActing.Clear();

            for (int i = 0; i < playerCount; i++)
            {
                var root = new GameObject($"SeatLabel_{i}");
                root.transform.SetParent(canvas, false);
                var rt = root.AddComponent<RectTransform>();
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(phone ? 210f : 190f, phone ? 62f : 56f);
                rt.anchoredPosition = Vector2.zero;

                var bg = root.AddComponent<Image>();
                bg.color = new Color(0.04f, 0.06f, 0.12f, 0.88f);
                bg.raycastTarget = false;
                UiTheme.ApplyRounded(bg);
                var edge = root.AddComponent<Outline>();
                edge.effectColor = SeatNormalOutline;
                edge.effectDistance = new Vector2(1.2f, -1.2f);

                _seatLabelBgs.Add(bg);
                _seatLabelOutlines.Add(edge);
                _seatActing.Add(false);

                var nameGo = new GameObject("Name");
                nameGo.transform.SetParent(root.transform, false);
                var nameRt = nameGo.AddComponent<RectTransform>();
                nameRt.anchorMin = new Vector2(0f, 0.48f);
                nameRt.anchorMax = new Vector2(1f, 1f);
                nameRt.offsetMin = new Vector2(8f, 0f);
                nameRt.offsetMax = new Vector2(-8f, -4f);
                var name = nameGo.AddComponent<Text>();
                name.fontSize = phone ? 20 : 19;
                name.fontStyle = FontStyle.Bold;
                name.alignment = TextAnchor.MiddleCenter;
                name.color = Color.white;
                name.raycastTarget = false;
                name.text = $"Игрок {i}";
                name.horizontalOverflow = HorizontalWrapMode.Overflow;
                name.verticalOverflow = VerticalWrapMode.Overflow;
                UiFont.MakeCrisp(name, 0.35f);

                var chipsGo = new GameObject("Chips");
                chipsGo.transform.SetParent(root.transform, false);
                var chipsRt = chipsGo.AddComponent<RectTransform>();
                chipsRt.anchorMin = new Vector2(0f, 0f);
                chipsRt.anchorMax = new Vector2(1f, 0.52f);
                chipsRt.offsetMin = new Vector2(8f, 4f);
                chipsRt.offsetMax = new Vector2(-8f, 0f);
                var chips = chipsGo.AddComponent<Text>();
                chips.fontSize = phone ? 18 : 17;
                chips.fontStyle = FontStyle.Bold;
                chips.alignment = TextAnchor.MiddleCenter;
                chips.color = UiTheme.Cyan;
                chips.raycastTarget = false;
                chips.text = "1000";
                chips.horizontalOverflow = HorizontalWrapMode.Overflow;
                chips.verticalOverflow = VerticalWrapMode.Overflow;
                UiFont.MakeCrisp(chips, 0.3f);

                _seatLabelRts.Add(rt);
                _seatNameLabels.Add(name);
                _seatChipLabels.Add(chips);
            }
        }

        void LateUpdate()
        {
            PositionSeatLabels();
            UpdateSeatLabelBackgrounds();
        }

        void UpdateSeatLabelBackgrounds()
        {
            float k = Mathf.Clamp01(Time.deltaTime * 9f);
            for (int i = 0; i < _seatLabelBgs.Count && i < playerCount; i++)
            {
                bool acting = i < _seatActing.Count && _seatActing[i];
                var bg = _seatLabelBgs[i];
                if (bg != null)
                    bg.color = Color.Lerp(bg.color, acting ? SeatActingBg : SeatNormalBg, k);
                if (i < _seatLabelOutlines.Count && _seatLabelOutlines[i] != null)
                {
                    var ol = _seatLabelOutlines[i];
                    ol.effectColor = Color.Lerp(ol.effectColor, acting ? SeatActingOutline : SeatNormalOutline, k);
                }
            }
        }

        void EnsureHandDisplayCycle(int handNumber)
        {
            if (handNumber == _shownHandNumber) return;
            _shownHandNumber = handNumber;
            _foldAnimatedSeats.Clear();
            foreach (var kv in _holeCards)
            {
                if (kv.Value == null) continue;
                if (IsVisualSeatInactive(kv.Key))
                {
                    HideSeatHoleCards(kv.Key);
                    continue;
                }
                foreach (var card in kv.Value)
                    card?.ResetForNewHand();
            }
        }

        bool IsVisualSeatInactive(int visualSeat)
        {
            if (_onlineMode)
            {
                if (_onlineState == null) return false;
                foreach (var p in _onlineState.Players)
                {
                    if (VisualSeat(p.Seat) != visualSeat) continue;
                    return p.Eliminated || p.Chips <= 0;
                }
                return false;
            }

            if (_table == null || visualSeat < 0 || visualSeat >= _table.Players.Count)
                return false;
            var player = _table.Players[visualSeat];
            return player.IsEliminated || player.Chips <= 0;
        }

        void HideSeatHoleCards(int visualSeat)
        {
            if (!_holeCards.TryGetValue(visualSeat, out var holes) || holes == null) return;
            for (int c = 0; c < holes.Count; c++)
            {
                if (holes[c] == null) continue;
                holes[c].gameObject.SetActive(false);
            }
        }

        Vector3 GetFoldDriftLocal(int visualSeat)
        {
            if (visualSeat < 0 || visualSeat >= _seats.Count || _seats[visualSeat] == null)
                return Vector3.down * 0.25f;
            float rad = _seats[visualSeat].SeatAngle * Mathf.Deg2Rad;
            Vector3 toCenter = new Vector3(-Mathf.Cos(rad), 0f, -Mathf.Sin(rad));
            Vector3 world = toCenter * 0.7f + Vector3.down * 0.18f;
            return _seats[visualSeat].CardAnchor.InverseTransformVector(world);
        }

        void AnimateFoldedHoleCards(int visualSeat)
        {
            if (_foldAnimatedSeats.Contains(visualSeat)) return;
            if (!_holeCards.TryGetValue(visualSeat, out var holes) || holes == null) return;
            _foldAnimatedSeats.Add(visualSeat);
            PokerSoundFx.Play(PokerSoundFx.Sfx.Fold);
            Vector3 drift = GetFoldDriftLocal(visualSeat);
            for (int c = 0; c < holes.Count; c++)
            {
                if (holes[c] == null) continue;
                holes[c].PlayFoldAway(drift * (c == 0 ? 0.92f : 1.08f), c * 0.05f);
            }
        }

        void SetSeatActing(int visualSeat, bool acting)
        {
            while (_seatActing.Count <= visualSeat)
                _seatActing.Add(false);
            if (visualSeat >= 0 && visualSeat < _seatActing.Count)
                _seatActing[visualSeat] = acting;
        }

        void PositionSeatLabels()
        {
            if (_canvasRoot == null || _seats.Count == 0 || _seatLabelRts.Count == 0) return;
            if (_cam == null) _cam = Camera.main;
            if (_cam == null) return;

            Rect crect = _canvasRoot.rect;
            float maxX = crect.width * 0.5f - 160f;
            float minX = -crect.width * 0.5f + 20f;
            float maxY = crect.height * 0.5f - 80f;
            float minY = -crect.height * 0.5f + 56f;

            for (int i = 0; i < _seats.Count && i < _seatLabelRts.Count; i++)
            {
                if (i >= playerCount || !_seats[i].gameObject.activeInHierarchy)
                {
                    _seatLabelRts[i].gameObject.SetActive(false);
                    continue;
                }

                float angle = _seats[i].SeatAngle;
                float rad = angle * Mathf.Deg2Rad;
                // Наружу от центра стола — вне сукна
                Vector3 outward = new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad));
                // Вправо относительно игрока (лицом к центру), не «holes[1]»
                Vector3 playerRight = new Vector3(-Mathf.Sin(rad), 0f, Mathf.Cos(rad));

                Vector3 rightCard = _seats[i].CardAnchor.position;
                if (_holeCards.TryGetValue(i, out var holes) && holes != null && holes.Count >= 2
                    && holes[0] != null && holes[1] != null)
                {
                    Vector3 a = holes[0].transform.position;
                    Vector3 b = holes[1].transform.position;
                    // Какая карта правее для этого места
                    rightCard = Vector3.Dot(b - a, playerRight) >= 0f ? b : a;
                }

                // Правее правой карты игрока + вне поля
                Vector3 world = rightCard
                    + playerRight * 1.05f
                    + outward * 1.35f
                    + Vector3.up * 0.35f;

                Vector3 sp = _cam.WorldToScreenPoint(world);
                if (sp.z < 0.05f)
                {
                    _seatLabelRts[i].gameObject.SetActive(false);
                    continue;
                }

                _seatLabelRts[i].gameObject.SetActive(true);

                Vector2 local = new Vector2(
                    (sp.x / Screen.width - 0.5f) * crect.width,
                    (sp.y / Screen.height - 0.5f) * crect.height);

                local.x = Mathf.Clamp(local.x, minX, maxX);
                local.y = Mathf.Clamp(local.y, minY, maxY);
                _seatLabelRts[i].anchoredPosition = local;
            }
        }

        void BuildRaisePanel(Transform canvas, bool phone)
        {
            float stripW = phone ? 108f : 120f;
            float pad = phone ? 10f : 14f;

            _raisePanel = new GameObject("RaisePanel");
            _raisePanel.transform.SetParent(canvas, false);
            var panelRt = _raisePanel.AddComponent<RectTransform>();
            // Полная высота справа
            panelRt.anchorMin = new Vector2(1f, 0f);
            panelRt.anchorMax = new Vector2(1f, 1f);
            panelRt.pivot = new Vector2(1f, 0.5f);
            panelRt.offsetMin = new Vector2(-stripW - pad, pad);
            panelRt.offsetMax = new Vector2(-pad, -pad);

            var panelImg = _raisePanel.AddComponent<Image>();
            panelImg.color = new Color(0.06f, 0.08f, 0.14f, 0.88f);
            panelImg.raycastTarget = true;
            UiTheme.ApplyRounded(panelImg);
            var edge = _raisePanel.AddComponent<Outline>();
            edge.effectColor = new Color(UiTheme.Coral.r, UiTheme.Coral.g, UiTheme.Coral.b, 0.35f);
            edge.effectDistance = new Vector2(1.2f, -1.2f);

            // Сумма сверху
            _raiseAmountLabel = CreateText(_raisePanel.transform, "RaiseAmount",
                new Vector2(0f, -14f), new Vector2(stripW - 16f, 56f), phone ? 20 : 22, FontStyle.Bold, UiTheme.CoralHot);
            _raiseAmountLabel.alignment = TextAnchor.MiddleCenter;
            var amtRt = _raiseAmountLabel.rectTransform;
            amtRt.anchorMin = new Vector2(0f, 1f);
            amtRt.anchorMax = new Vector2(1f, 1f);
            amtRt.pivot = new Vector2(0.5f, 1f);
            amtRt.anchoredPosition = new Vector2(0f, -12f);
            amtRt.sizeDelta = new Vector2(-12f, 56f);
            _raiseAmountLabel.horizontalOverflow = HorizontalWrapMode.Wrap;

            // Кнопки снизу
            float btnH = phone ? 52f : 56f;
            _raiseCancelBtn = CreateButton(_raisePanel.transform, "Отмена", Vector2.zero,
                UiTheme.GlassStrong, ExitRaiseMode, pill: true);
            var canRt = _raiseCancelBtn.GetComponent<RectTransform>();
            canRt.anchorMin = new Vector2(0f, 0f);
            canRt.anchorMax = new Vector2(1f, 0f);
            canRt.pivot = new Vector2(0.5f, 0f);
            canRt.anchoredPosition = new Vector2(0f, 10f);
            canRt.sizeDelta = new Vector2(-16f, btnH);
            var canLabel = _raiseCancelBtn.GetComponentInChildren<Text>();
            if (canLabel != null) canLabel.fontSize = phone ? 16 : 17;

            _raiseConfirmBtn = CreateButton(_raisePanel.transform, "OK", Vector2.zero,
                UiTheme.Coral, ConfirmRaiseWithSound, pill: true, uiClick: false);
            var confRt = _raiseConfirmBtn.GetComponent<RectTransform>();
            confRt.anchorMin = new Vector2(0f, 0f);
            confRt.anchorMax = new Vector2(1f, 0f);
            confRt.pivot = new Vector2(0.5f, 0f);
            confRt.anchoredPosition = new Vector2(0f, 10f + btnH + 8f);
            confRt.sizeDelta = new Vector2(-16f, btnH);
            var confLabel = _raiseConfirmBtn.GetComponentInChildren<Text>();
            if (confLabel != null) confLabel.fontSize = phone ? 18 : 20;

            // Вертикальный слайдер между суммой и кнопками
            var sliderGo = new GameObject("RaiseSlider");
            sliderGo.transform.SetParent(_raisePanel.transform, false);
            var sliderRt = sliderGo.AddComponent<RectTransform>();
            sliderRt.anchorMin = new Vector2(0.5f, 0f);
            sliderRt.anchorMax = new Vector2(0.5f, 1f);
            sliderRt.pivot = new Vector2(0.5f, 0.5f);
            sliderRt.anchoredPosition = Vector2.zero;
            sliderRt.sizeDelta = new Vector2(44f, 0f);
            sliderRt.offsetMin = new Vector2(-22f, 10f + btnH * 2f + 28f);
            sliderRt.offsetMax = new Vector2(22f, -78f);

            // Track (вертикальный)
            var bgGo = new GameObject("Background");
            bgGo.transform.SetParent(sliderGo.transform, false);
            var bgRt = bgGo.AddComponent<RectTransform>();
            bgRt.anchorMin = new Vector2(0.35f, 0f);
            bgRt.anchorMax = new Vector2(0.65f, 1f);
            bgRt.offsetMin = Vector2.zero;
            bgRt.offsetMax = Vector2.zero;
            var bgImg = bgGo.AddComponent<Image>();
            bgImg.color = new Color(1f, 1f, 1f, 0.12f);
            UiTheme.ApplyRoundedSmall(bgImg);

            var fillArea = new GameObject("Fill Area");
            fillArea.transform.SetParent(sliderGo.transform, false);
            var fillAreaRt = fillArea.AddComponent<RectTransform>();
            fillAreaRt.anchorMin = new Vector2(0.35f, 0f);
            fillAreaRt.anchorMax = new Vector2(0.65f, 1f);
            fillAreaRt.offsetMin = Vector2.zero;
            fillAreaRt.offsetMax = Vector2.zero;

            var fillGo = new GameObject("Fill");
            fillGo.transform.SetParent(fillArea.transform, false);
            var fillRt = fillGo.AddComponent<RectTransform>();
            fillRt.anchorMin = Vector2.zero;
            fillRt.anchorMax = Vector2.one;
            fillRt.offsetMin = Vector2.zero;
            fillRt.offsetMax = Vector2.zero;
            var fillImg = fillGo.AddComponent<Image>();
            fillImg.color = UiTheme.Coral;
            UiTheme.ApplyRoundedSmall(fillImg);

            var handleArea = new GameObject("Handle Slide Area");
            handleArea.transform.SetParent(sliderGo.transform, false);
            var handleAreaRt = handleArea.AddComponent<RectTransform>();
            handleAreaRt.anchorMin = Vector2.zero;
            handleAreaRt.anchorMax = Vector2.one;
            handleAreaRt.offsetMin = new Vector2(0f, 14f);
            handleAreaRt.offsetMax = new Vector2(0f, -14f);

            var handleGo = new GameObject("Handle");
            handleGo.transform.SetParent(handleArea.transform, false);
            var handleRt = handleGo.AddComponent<RectTransform>();
            handleRt.sizeDelta = new Vector2(36f, 36f);
            var handleImg = handleGo.AddComponent<Image>();
            handleImg.color = Color.white;
            UiTheme.ApplyCircle(handleImg);
            var handleGlow = handleGo.AddComponent<Outline>();
            handleGlow.effectColor = new Color(UiTheme.Coral.r, UiTheme.Coral.g, UiTheme.Coral.b, 0.55f);
            handleGlow.effectDistance = new Vector2(2f, -2f);

            _raiseSlider = sliderGo.AddComponent<Slider>();
            _raiseSlider.fillRect = fillRt;
            _raiseSlider.handleRect = handleRt;
            _raiseSlider.targetGraphic = handleImg;
            _raiseSlider.direction = Slider.Direction.BottomToTop;
            _raiseSlider.wholeNumbers = true;
            _raiseSlider.minValue = 1;
            _raiseSlider.maxValue = 100;
            _raiseSlider.value = 1;
            _raiseSlider.onValueChanged.AddListener(_ => UpdateRaiseLabel());

            _raisePanel.SetActive(false);
        }

        void UpdateRaiseLabel()
        {
            if (_raiseAmountLabel == null || _raiseSlider == null) return;
            if (_onlineMode)
            {
                if (_onlineState?.Legal == null || !_raiseMode)
                {
                    _raiseAmountLabel.text = "";
                    return;
                }
                var onlineLegal = _onlineState.Legal;
                int onlineAmount = Mathf.RoundToInt(_raiseSlider.value);
                bool onlineIsBet = onlineLegal.CanBet;
                bool onlineAtMax = onlineAmount >= onlineLegal.MaxRaiseTo && onlineLegal.MaxRaiseTo > 0;
                if (onlineAtMax) _raiseAmountLabel.text = $"ALL IN\n{onlineAmount}";
                else if (onlineIsBet) _raiseAmountLabel.text = $"BET\n{onlineAmount}";
                else _raiseAmountLabel.text = $"RAISE\n{onlineAmount}";
                return;
            }
            if (_table == null) return;
            if (!_table.AwaitingHumanAction)
            {
                _raiseAmountLabel.text = "";
                return;
            }
            var legal = _table.GetLegalActions(_table.ActingSeat);
            int amount = Mathf.RoundToInt(_raiseSlider.value);
            bool isBet = legal.CanBet;
            bool atMax = amount >= legal.MaxRaiseTo && legal.MaxRaiseTo > 0;
            if (atMax)
                _raiseAmountLabel.text = $"ALL IN\n{amount}";
            else if (isBet)
                _raiseAmountLabel.text = $"BET\n{amount}";
            else
                _raiseAmountLabel.text = $"RAISE\n{amount}";
        }

        void EnterRaiseMode()
        {
            PokerSoundFx.Play(PokerSoundFx.Sfx.Button);
            if (_onlineMode)
            {
                if (_onlineState?.Legal == null) return;
                var onlineLegal = _onlineState.Legal;
                if (!onlineLegal.CanBet && !onlineLegal.CanRaise) return;
                _raiseMode = true;
                if (_foldBtn != null) _foldBtn.gameObject.SetActive(false);
                if (_checkCallBtn != null) _checkCallBtn.gameObject.SetActive(false);
                if (_betRaiseBtn != null) _betRaiseBtn.gameObject.SetActive(false);
                int onlineMin = Mathf.Max(1, onlineLegal.MinRaiseTo);
                int onlineMax = Mathf.Max(onlineMin, onlineLegal.MaxRaiseTo);
                _raiseSlider.minValue = onlineMin;
                _raiseSlider.maxValue = onlineMax;
                _raiseSlider.value = onlineMin;
                if (_raisePanel != null)
                {
                    _raisePanel.SetActive(true);
                    _raisePanel.transform.SetAsLastSibling();
                }
                UpdateRaiseLabel();
                UpdateExitButtonVisibility();
                return;
            }

            if (!_table.AwaitingHumanAction) return;
            var legal = _table.GetLegalActions(_table.ActingSeat);
            if (!legal.CanBet && !legal.CanRaise) return;

            _raiseMode = true;
            if (_foldBtn != null) _foldBtn.gameObject.SetActive(false);
            if (_checkCallBtn != null) _checkCallBtn.gameObject.SetActive(false);
            if (_betRaiseBtn != null) _betRaiseBtn.gameObject.SetActive(false);

            int min = Mathf.Max(1, legal.MinRaiseTo);
            int max = Mathf.Max(min, legal.MaxRaiseTo);
            _raiseSlider.minValue = min;
            _raiseSlider.maxValue = max;
            _raiseSlider.value = min;
            if (_raisePanel != null)
            {
                _raisePanel.SetActive(true);
                _raisePanel.transform.SetAsLastSibling();
            }
            UpdateRaiseLabel();
            UpdateExitButtonVisibility();
        }

        void ExitRaiseMode()
        {
            _raiseMode = false;
            if (_raisePanel != null) _raisePanel.SetActive(false);
            UpdateExitButtonVisibility();
        }

        void UpdateExitButtonVisibility()
        {
            if (_exitBtn != null)
                _exitBtn.gameObject.SetActive(!_raiseMode);
        }

        void ConfirmRaiseWithSound()
        {
            PokerSoundFx.Play(PokerSoundFx.Sfx.Raise);
            ConfirmRaise();
        }

        void PlayCheckOrCallWithSound()
        {
            if (_onlineMode)
            {
                var legal = _onlineState?.Legal;
                if (legal != null)
                    PokerSoundFx.Play(legal.CanCheck ? PokerSoundFx.Sfx.Check : PokerSoundFx.Sfx.Call);
            }
            else if (_table != null && _table.AwaitingHumanAction)
            {
                var legal = _table.GetLegalActions(_table.ActingSeat);
                PokerSoundFx.Play(legal.CanCheck ? PokerSoundFx.Sfx.Check : PokerSoundFx.Sfx.Call);
            }
            OnCheckOrCall();
        }

        void ConfirmRaise()
        {
            if (_onlineMode)
            {
                if (!_raiseMode || _onlineState?.Legal == null) return;
                var onlineLegal = _onlineState.Legal;
                int onlineAmount = Mathf.RoundToInt(_raiseSlider.value);
                onlineAmount = Mathf.Clamp(onlineAmount, onlineLegal.MinRaiseTo, onlineLegal.MaxRaiseTo);
                ExitRaiseMode();
                if (onlineLegal.CanBet) _onlineClient.SendAction("bet", onlineAmount);
                else if (onlineLegal.CanRaise) _onlineClient.SendAction("raise", onlineAmount);
                else _onlineClient.SendAction("allin");
                SetActionButtons(false);
                return;
            }

            if (!_table.AwaitingHumanAction || !_raiseMode) return;
            int seat = _table.ActingSeat;
            var legal = _table.GetLegalActions(seat);
            int amount = Mathf.RoundToInt(_raiseSlider.value);
            amount = Mathf.Clamp(amount, legal.MinRaiseTo, legal.MaxRaiseTo);

            ExitRaiseMode();
            if (legal.CanBet)
                _table.TryApplyAction(seat, new PlayerAction(ActionType.Bet, amount));
            else if (legal.CanRaise)
                _table.TryApplyAction(seat, new PlayerAction(ActionType.Raise, amount));
            else
                _table.TryApplyAction(seat, new PlayerAction(ActionType.AllIn));
            RefreshAll();
        }

        void OnHuman(ActionType type)
        {
            if (_onlineMode)
            {
                if (_onlineState?.Legal == null) return;
                ExitRaiseMode();
                string action = type switch
                {
                    ActionType.Fold => "fold",
                    ActionType.Check => "check",
                    ActionType.Call => "call",
                    ActionType.AllIn => "allin",
                    _ => "fold"
                };
                _onlineClient.SendAction(action);
                SetActionButtons(false);
                return;
            }

            if (!_table.AwaitingHumanAction) return;
            ExitRaiseMode();
            _table.TryApplyAction(_table.ActingSeat, new PlayerAction(type));
            RefreshAll();
        }

        void OnCheckOrCall()
        {
            if (_onlineMode)
            {
                if (_onlineState?.Legal == null) return;
                ExitRaiseMode();
                var onlineLegal = _onlineState.Legal;
                if (onlineLegal.CanCheck) _onlineClient.SendAction("check");
                else if (onlineLegal.CanCall) _onlineClient.SendAction("call");
                SetActionButtons(false);
                return;
            }

            if (!_table.AwaitingHumanAction) return;
            ExitRaiseMode();
            int seat = _table.ActingSeat;
            var legal = _table.GetLegalActions(seat);
            if (legal.CanCheck)
                _table.TryApplyAction(seat, new PlayerAction(ActionType.Check));
            else if (legal.CanCall)
                _table.TryApplyAction(seat, new PlayerAction(ActionType.Call));
            RefreshAll();
        }

        void SetActionButtons(bool visible)
        {
            if (_raiseMode && visible) return;
            if (_foldBtn != null) _foldBtn.gameObject.SetActive(visible);
            if (_checkCallBtn != null) _checkCallBtn.gameObject.SetActive(visible);
            if (_betRaiseBtn != null) _betRaiseBtn.gameObject.SetActive(visible);
            if (!visible) ExitRaiseMode();
        }

        void RefreshActionButtons()
        {
            if (_foldBtn == null || _checkCallBtn == null || _betRaiseBtn == null) return;
            if (_raiseMode)
            {
                // В режиме рейза только панель слайдера.
                if (_raisePanel != null) _raisePanel.SetActive(true);
                if (_foldBtn != null) _foldBtn.gameObject.SetActive(false);
                if (_checkCallBtn != null) _checkCallBtn.gameObject.SetActive(false);
                if (_betRaiseBtn != null) _betRaiseBtn.gameObject.SetActive(false);
                UpdateRaiseLabel();
                UpdateExitButtonVisibility();
                return;
            }

            SetActionButtons(true);
            var legal = _table.GetLegalActions(_table.ActingSeat);

            _foldBtn.interactable = legal.CanFold;
            _checkCallBtn.interactable = legal.CanCheck || legal.CanCall;
            var checkCallLabel = _checkCallBtn.GetComponentInChildren<Text>();
            if (checkCallLabel != null)
                checkCallLabel.text = legal.CanCheck ? "Чек" : $"Колл {legal.CallAmount}";

            bool canBetRaise = legal.CanBet || legal.CanRaise;
            _betRaiseBtn.interactable = canBetRaise;
            var betLabel = _betRaiseBtn.GetComponentInChildren<Text>();
            if (betLabel != null)
            {
                if (legal.CanBet) betLabel.text = "Бет";
                else if (legal.CanRaise) betLabel.text = "Рейз";
                else betLabel.text = "Олл-ин";
            }
        }

        void RefreshAll()
        {
            if (_onlineMode)
            {
                RefreshAllOnline();
                return;
            }

            if (_table == null) return;

            EnsureHandDisplayCycle(_table.HandNumber);

            RefreshHudHeader(_table.HandNumber, GetLocalCombinationName(_table), _table.Pot);

            bool showTurn = _table.ActingSeat >= 0 &&
                            _table.Street >= Street.Preflop &&
                            _table.Street <= Street.River;
            if (showTurn)
                _turnArrow.PointAt(_seats[_table.ActingSeat].WorldPosition);
            else
                _turnArrow.Hide();

            for (int i = 0; i < 5; i++)
            {
                if (i < _table.Board.Count)
                {
                    _boardCards[i].gameObject.SetActive(true);
                    _boardCards[i].SetCard(_table.Board[i], true);
                }
                else
                    _boardCards[i].gameObject.SetActive(false);
            }

            for (int i = 0; i < _seats.Count; i++)
            {
                var player = _table.Players[i];
                bool acting = _table.ActingSeat == i;
                bool dealer = _table.DealerSeat == i;
                bool handWinner = IsSeatHandWinner(i);
                _seats[i].Refresh(player, dealer, acting, _table.Street, _table.BigBlind, handWinner);
                SetSeatActing(i, acting);

                if (i < _seatNameLabels.Count)
                {
                    string tag = acting ? " ●" : "";
                    if (player.IsEliminated || player.Chips <= 0) tag = " · out";
                    else if (player.HasFolded) tag = " · фолд";
                    _seatNameLabels[i].text = player.Name + tag;
                    _seatNameLabels[i].color = acting
                        ? UiTheme.CoralHot
                        : player.IsEliminated || player.Chips <= 0 || player.HasFolded
                            ? UiTheme.TextDim
                            : UiTheme.TextMain;
                }
                if (i < _seatChipLabels.Count)
                {
                    _seatChipLabels[i].text = player.IsEliminated ? "—" : $"{player.Chips}";
                    _seatChipLabels[i].color = player.IsEliminated || player.Chips <= 0 || player.HasFolded
                        ? UiTheme.TextDim
                        : UiTheme.Cyan;
                }

                bool reveal = player.Type == PlayerType.Human ||
                              (_table.Street >= Street.Showdown && !player.HasFolded && !player.IsEliminated) ||
                              ((_table.Street == Street.HandComplete || _table.Street == Street.MatchComplete) &&
                               !player.HasFolded && _table.LastResult != null);

                // Вскрытие — лицом к герою (камере), рубашки — по ориентации места.
                bool faceTowardMe = reveal && player.Type != PlayerType.Human;

                var holes = _holeCards[i];
                if (player.IsEliminated || player.Chips <= 0)
                {
                    HideSeatHoleCards(i);
                    continue;
                }

                if (player.HasFolded)
                {
                    AnimateFoldedHoleCards(i);
                    continue;
                }

                for (int c = 0; c < 2; c++)
                {
                    if (player.IsEliminated || player.HoleCards.Count == 0)
                    {
                        holes[c].gameObject.SetActive(false);
                        continue;
                    }
                    if (c < player.HoleCards.Count)
                    {
                        holes[c].gameObject.SetActive(true);
                        holes[c].SetFacingViewer(faceTowardMe);
                        holes[c].SetCard(player.HoleCards[c], reveal);
                    }
                    else
                        holes[c].gameObject.SetActive(false);
                }
            }

            PositionSeatLabels();

            if (_table.AwaitingHumanAction)
                RefreshActionButtons();
            else if (_table.Street != Street.HandComplete)
                SetActionButtons(false);
        }

        void RefreshAllOnline()
        {
            if (_onlineState == null) return;
            ResolveMyServerSeat();
            ApplySeatLayout(_onlineState.Players.Count);

            EnsureHandDisplayCycle(_onlineState.HandNumber);

            RefreshHudHeader(_onlineState.HandNumber,
                GetOnlineCombinationName(_onlineState, _onlineState.YouId),
                _onlineState.Pot);

            bool showTurn = _onlineState.Acting >= 0 &&
                            (_onlineState.Street == "preflop" || _onlineState.Street == "flop" ||
                             _onlineState.Street == "turn" || _onlineState.Street == "river");
            if (showTurn)
            {
                int visual = VisualSeat(_onlineState.Acting);
                if (visual >= 0 && visual < _seats.Count)
                    _turnArrow.PointAt(_seats[visual].WorldPosition);
                else
                    _turnArrow.Hide();
            }
            else
                _turnArrow.Hide();

            for (int i = 0; i < 5; i++)
            {
                if (i < _onlineState.Board.Count)
                {
                    _boardCards[i].gameObject.SetActive(true);
                    _boardCards[i].SetCard(_onlineState.Board[i], true);
                }
                else
                    _boardCards[i].gameObject.SetActive(false);
            }

            for (int vi = 0; vi < playerCount && vi < _seats.Count; vi++)
            {
                SetSeatActing(vi, false);
                OnlineSeatPlayer op = null;
                foreach (var p in _onlineState.Players)
                {
                    if (VisualSeat(p.Seat) == vi) { op = p; break; }
                }

                if (op == null) continue;

                bool acting = _onlineState.Acting == op.Seat;
                bool dealer = _onlineState.Dealer == op.Seat;
                bool isMe = op.Id == _onlineState.YouId;
                var temp = new Player(vi, op.Name, isMe ? PlayerType.Human : PlayerType.Ai, op.Chips)
                {
                    BetThisStreet = op.BetStreet,
                    HasFolded = op.Folded,
                    IsAllIn = op.AllIn,
                    IsEliminated = op.Eliminated
                };
                _seats[vi].Refresh(temp, dealer, acting, ParseOnlineStreet(_onlineState.Street), _onlineState.BigBlind, false);
                SetSeatActing(vi, acting);

                if (vi < _seatNameLabels.Count)
                {
                    string tag = acting ? " ●" : "";
                    if (op.Eliminated || op.Chips <= 0) tag = " · out";
                    else if (op.Folded) tag = " · фолд";
                    _seatNameLabels[vi].text = op.Name + tag;
                    _seatNameLabels[vi].color = acting
                        ? UiTheme.CoralHot
                        : op.Eliminated || op.Chips <= 0 || op.Folded
                            ? UiTheme.TextDim
                            : UiTheme.TextMain;
                }
                if (vi < _seatChipLabels.Count)
                {
                    _seatChipLabels[vi].text = op.Eliminated ? "—" : $"{op.Chips}";
                    _seatChipLabels[vi].color = op.Eliminated || op.Chips <= 0 || op.Folded
                        ? UiTheme.TextDim
                        : UiTheme.Cyan;
                }

                bool reveal = isMe ||
                              _onlineState.Street == "showdown" ||
                              _onlineState.Street == "handComplete" ||
                              _onlineState.Street == "matchComplete";

                bool faceTowardMe = reveal && !isMe;
                var holes = _holeCards[vi];

                if (op.Eliminated || op.Chips <= 0)
                {
                    HideSeatHoleCards(vi);
                    continue;
                }

                if (op.Folded)
                {
                    AnimateFoldedHoleCards(vi);
                    continue;
                }

                for (int c = 0; c < 2; c++)
                {
                    if (op.Eliminated)
                    {
                        holes[c].gameObject.SetActive(false);
                        continue;
                    }

                    // Соперник: сервер шлёт «??» — показываем рубашки, не прячем карты.
                    if (!isMe && op.HoleHidden && c >= op.Hole.Count)
                    {
                        holes[c].gameObject.SetActive(true);
                        holes[c].SetFacingViewer(false);
                        holes[c].ShowBack();
                        continue;
                    }

                    if (c >= op.Hole.Count)
                    {
                        holes[c].gameObject.SetActive(false);
                        continue;
                    }

                    holes[c].gameObject.SetActive(true);
                    holes[c].SetFacingViewer(faceTowardMe);
                    holes[c].SetCard(op.Hole[c], reveal || isMe);
                }
            }

            PositionSeatLabels();

            if (_onlineState.IsMyTurn(_onlineState.Legal))
                RefreshOnlineActionButtons();
            else if (_onlineState.Street != "handComplete")
                SetActionButtons(false);
        }

        void RefreshOnlineActionButtons()
        {
            if (_foldBtn == null || _checkCallBtn == null || _betRaiseBtn == null) return;
            var legal = _onlineState?.Legal;
            if (legal == null || !legal.HasAny)
            {
                SetActionButtons(false);
                return;
            }

            if (_raiseMode)
            {
                if (_raisePanel != null) _raisePanel.SetActive(true);
                _foldBtn.gameObject.SetActive(false);
                _checkCallBtn.gameObject.SetActive(false);
                _betRaiseBtn.gameObject.SetActive(false);
                UpdateRaiseLabel();
                UpdateExitButtonVisibility();
                return;
            }

            SetActionButtons(true);
            _foldBtn.interactable = legal.CanFold;
            _checkCallBtn.interactable = legal.CanCheck || legal.CanCall;
            var checkCallLabel = _checkCallBtn.GetComponentInChildren<Text>();
            if (checkCallLabel != null)
                checkCallLabel.text = legal.CanCheck ? "Чек" : $"Колл {legal.CallAmount}";

            bool canBetRaise = legal.CanBet || legal.CanRaise;
            _betRaiseBtn.interactable = canBetRaise;
            var betLabel = _betRaiseBtn.GetComponentInChildren<Text>();
            if (betLabel != null)
            {
                if (legal.CanBet) betLabel.text = "Бет";
                else if (legal.CanRaise) betLabel.text = "Рейз";
                else betLabel.text = "Олл-ин";
            }
        }

        bool IsSeatHandWinner(int seat)
        {
            if (_table.MatchWinnerSeat == seat) return true;
            var result = _table.LastResult;
            if (result == null) return false;
            for (int p = 0; p < result.Pots.Count; p++)
            {
                var winners = result.Pots[p].WinnerSeats;
                for (int w = 0; w < winners.Count; w++)
                    if (winners[w] == seat) return true;
            }
            return false;
        }
    }
}
