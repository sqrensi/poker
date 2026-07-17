using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Poker.Game;

namespace Poker.Presentation
{
    public sealed class PokerGameController : MonoBehaviour
    {
        const string HintsPrefsKey = "poker_hints_enabled";

        [SerializeField] int playerCount = 4;
        [SerializeField] int startingChips = 1000;
        [SerializeField] int smallBlind = 5;
        [SerializeField] int bigBlind = 10;
        [SerializeField] float aiDelaySeconds = 0.7f;
        [SerializeField] int raiseMultiplier = 3;

        PokerTable _table;
        readonly List<SeatView> _seats = new List<SeatView>();
        readonly List<CardView> _boardCards = new List<CardView>();
        readonly Dictionary<int, List<CardView>> _holeCards = new Dictionary<int, List<CardView>>();
        readonly List<Text> _seatUiLines = new List<Text>();
        readonly List<Image> _seatUiRows = new List<Image>();

        Text _hudTitle;
        Text _hudDetail;
        Text _logText;
        Text _potText;
        Text _hintText;
        Text _rosterTitle;
        Image _hintPanel;
        Button _foldBtn;
        Button _checkCallBtn;
        Button _betRaiseBtn;
        Button _nextHandBtn;
        Button _hintsToggleBtn;
        Text _hintsToggleLabel;
        float _aiTimer;
        bool _aiPending;
        bool _hintsEnabled = true;
        Camera _cam;
        TurnArrow _turnArrow;
        Transform _tableRoot;
        WinnerOverlay _winnerOverlay;
        Transform _canvasRoot;

        void Start()
        {
            _hintsEnabled = PlayerPrefs.GetInt(HintsPrefsKey, 1) == 1;
            CardSpriteCatalog.EnsureLoaded();
            Debug.Log("[Poker] Smoke: " + Poker.Tests.HandEvaluatorSmoke.RunSmoke());
            BuildWorld();
            BuildUi();
            StartMatch();
        }

        void Update()
        {
            if (_table == null) return;

            if (_table.Street == Street.HandComplete)
            {
                SetActionButtons(false);
                _nextHandBtn.gameObject.SetActive(true);
                return;
            }

            _nextHandBtn.gameObject.SetActive(false);

            if (_table.AwaitingHumanAction)
            {
                _aiPending = false;
                RefreshActionButtons();
                return;
            }

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

        void StartMatch()
        {
            var players = new List<Player>();
            players.Add(new Player(0, "Вы", PlayerType.Human, startingChips));
            for (int i = 1; i < playerCount; i++)
                players.Add(new Player(i, $"Бот {i}", PlayerType.Ai, startingChips));

            _table = new PokerTable(players, smallBlind, bigBlind);
            _table.StateChanged += RefreshAll;
            _table.HandEnded += OnHandEnded;
            _table.StartNewHand();
            RefreshAll();
        }

        void OnHandEnded()
        {
            RefreshAll();
            if (_winnerOverlay != null)
                _winnerOverlay.Show(_table);
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
            _cam.transform.position = new Vector3(0f, 12.5f, -10.2f);
            _cam.transform.rotation = Quaternion.Euler(54f, 0f, 0f);
            _cam.backgroundColor = new Color(0.04f, 0.06f, 0.08f);
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.fieldOfView = 48f;

            EnsureLight("KeyLight", new Vector3(50f, -25f, 0f), new Color(1f, 0.98f, 0.92f), 1.35f);
            EnsureLight("FillLight", new Vector3(35f, 140f, 0f), new Color(0.7f, 0.8f, 1f), 0.55f);

            var tableRoot = new GameObject("PokerTable").transform;
            tableRoot.SetParent(transform, false);
            _tableRoot = tableRoot;

            // Увеличенное поле
            var felt = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            felt.name = "Felt";
            felt.transform.SetParent(tableRoot, false);
            felt.transform.localScale = new Vector3(11f, 0.12f, 6.8f);
            felt.GetComponent<MeshRenderer>().material = PokerMaterials.ColorMat(new Color(0.06f, 0.42f, 0.22f));

            var rim = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            rim.name = "Rim";
            rim.transform.SetParent(tableRoot, false);
            rim.transform.localScale = new Vector3(11.6f, 0.1f, 7.3f);
            rim.transform.position = new Vector3(0f, -0.04f, 0f);
            rim.GetComponent<MeshRenderer>().material = PokerMaterials.ColorMat(new Color(0.28f, 0.14f, 0.07f));
            Object.Destroy(rim.GetComponent<Collider>());

            _turnArrow = TurnArrow.Create(tableRoot);

            // Борд ближе к центру, с запасом между картами
            var boardAnchor = new GameObject("Board").transform;
            boardAnchor.SetParent(tableRoot, false);
            boardAnchor.position = new Vector3(0f, 0.28f, 0.35f);

            const float boardSpacing = 0.95f;
            const float boardCardW = 0.82f;
            for (int i = 0; i < 5; i++)
            {
                float x = (i - 2) * boardSpacing;
                _boardCards.Add(CardView.Create(boardAnchor, new Vector3(x, 0.03f * i, 0f), 0f, boardCardW, 20 + i));
            }

            float radiusX = 4.35f;
            float radiusZ = 2.85f;
            for (int i = 0; i < playerCount; i++)
            {
                float angle = -90f + i * (360f / playerCount);
                float rad = angle * Mathf.Deg2Rad;
                var pos = new Vector3(Mathf.Cos(rad) * radiusX, 0.12f, Mathf.Sin(rad) * radiusZ);
                var seat = SeatView.Create(tableRoot, i, pos, angle);
                _seats.Add(seat);

                var holes = new List<CardView>();
                int baseOrder = 40 + i * 5;
                holes.Add(CardView.Create(seat.CardAnchor, new Vector3(-0.48f, 0.04f, 0f), 0f, 0.8f, baseOrder));
                holes.Add(CardView.Create(seat.CardAnchor, new Vector3(0.48f, 0.05f, 0f), 0f, 0.8f, baseOrder + 1));
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
            var canvasGo = new GameObject("Canvas");
            _canvasRoot = canvasGo.transform;
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            if (Object.FindObjectOfType<UnityEngine.EventSystems.EventSystem>() == null)
            {
                var es = new GameObject("EventSystem");
                es.AddComponent<UnityEngine.EventSystems.EventSystem>();
                es.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
            }

            var leftPanel = CreatePanel(canvasGo.transform, "HudPanel",
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(18f, -18f), new Vector2(540f, 150f),
                new Color(0.05f, 0.07f, 0.1f, 0.88f));

            _hudTitle = CreateText(leftPanel.transform, "Title",
                new Vector2(16f, -14f), new Vector2(500f, 40f), 28, FontStyle.Bold, Color.white);
            _hudDetail = CreateText(leftPanel.transform, "Detail",
                new Vector2(16f, -58f), new Vector2(500f, 36f), 20, FontStyle.Normal, new Color(0.85f, 0.9f, 0.95f));
            _logText = CreateText(leftPanel.transform, "Log",
                new Vector2(16f, -100f), new Vector2(500f, 36f), 20, FontStyle.Normal, new Color(1f, 0.92f, 0.45f));

            var potPanel = CreatePanel(canvasGo.transform, "PotPanel",
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -18f), new Vector2(300f, 64f),
                new Color(0.08f, 0.1f, 0.05f, 0.9f));
            _potText = CreateText(potPanel.transform, "Pot",
                new Vector2(0f, 0f), new Vector2(280f, 50f), 34, FontStyle.Bold, new Color(1f, 0.92f, 0.35f));
            var potRt = _potText.GetComponent<RectTransform>();
            potRt.anchorMin = new Vector2(0.5f, 0.5f);
            potRt.anchorMax = new Vector2(0.5f, 0.5f);
            potRt.pivot = new Vector2(0.5f, 0.5f);
            potRt.anchoredPosition = Vector2.zero;
            _potText.alignment = TextAnchor.MiddleCenter;

            // Подсказки комбинации — снизу слева
            _hintPanel = CreatePanel(canvasGo.transform, "HintPanel",
                new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(18f, 110f), new Vector2(420f, 100f),
                new Color(0.06f, 0.12f, 0.18f, 0.92f));
            var hintTitle = CreateText(_hintPanel.transform, "HintTitle",
                new Vector2(14f, -10f), new Vector2(390f, 24f), 16, FontStyle.Bold, new Color(0.65f, 0.85f, 1f));
            hintTitle.text = "ВАША КОМБИНАЦИЯ";
            _hintText = CreateText(_hintPanel.transform, "HintBody",
                new Vector2(14f, -36f), new Vector2(390f, 56f), 20, FontStyle.Normal, Color.white);

            _hintsToggleBtn = CreateButton(canvasGo.transform, "Подсказки: вкл", new Vector2(720f, 36f),
                new Color(0.2f, 0.25f, 0.35f), ToggleHints);
            var toggleRt = _hintsToggleBtn.GetComponent<RectTransform>();
            toggleRt.anchorMin = new Vector2(1f, 0f);
            toggleRt.anchorMax = new Vector2(1f, 0f);
            toggleRt.pivot = new Vector2(1f, 0f);
            toggleRt.anchoredPosition = new Vector2(-18f, 36f);
            toggleRt.sizeDelta = new Vector2(220f, 52f);
            _hintsToggleLabel = _hintsToggleBtn.GetComponentInChildren<Text>();
            RefreshHintsToggleLabel();

            var roster = CreatePanel(canvasGo.transform, "RosterPanel",
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-18f, -18f), new Vector2(340f, 52f + playerCount * 58f),
                new Color(0.05f, 0.07f, 0.1f, 0.9f));

            _rosterTitle = CreateText(roster.transform, "RosterTitle",
                new Vector2(14f, -10f), new Vector2(300f, 28f), 20, FontStyle.Bold, new Color(0.75f, 0.85f, 1f));
            _rosterTitle.text = "ИГРОКИ";

            for (int i = 0; i < playerCount; i++)
            {
                float y = -44f - i * 58f;
                var rowGo = new GameObject($"SeatRow_{i}");
                rowGo.transform.SetParent(roster.transform, false);
                var rowRt = rowGo.AddComponent<RectTransform>();
                rowRt.anchorMin = new Vector2(0f, 1f);
                rowRt.anchorMax = new Vector2(1f, 1f);
                rowRt.pivot = new Vector2(0.5f, 1f);
                rowRt.anchoredPosition = new Vector2(0f, y);
                rowRt.sizeDelta = new Vector2(-20f, 52f);
                var row = rowGo.AddComponent<Image>();
                row.color = new Color(0.12f, 0.15f, 0.2f, 0.95f);
                row.raycastTarget = false;

                var line = CreateText(rowGo.transform, "Line",
                    new Vector2(12f, -6f), new Vector2(300f, 42f), 18, FontStyle.Normal, Color.white);
                var lineRt = line.GetComponent<RectTransform>();
                lineRt.anchorMin = Vector2.zero;
                lineRt.anchorMax = Vector2.one;
                lineRt.offsetMin = new Vector2(12f, 4f);
                lineRt.offsetMax = new Vector2(-8f, -4f);
                line.alignment = TextAnchor.MiddleLeft;

                _seatUiRows.Add(row);
                _seatUiLines.Add(line);
            }

            _foldBtn = CreateButton(canvasGo.transform, "Фолд", new Vector2(-250f, 36f),
                new Color(0.55f, 0.15f, 0.15f), () => OnHuman(ActionType.Fold));
            _checkCallBtn = CreateButton(canvasGo.transform, "Чек", new Vector2(0f, 36f),
                new Color(0.15f, 0.4f, 0.25f), () => OnCheckOrCall());
            _betRaiseBtn = CreateButton(canvasGo.transform, "Рейз", new Vector2(250f, 36f),
                new Color(0.15f, 0.3f, 0.55f), () => OnBetOrRaise());
            _nextHandBtn = CreateButton(canvasGo.transform, "Следующая раздача", new Vector2(0f, 36f),
                new Color(0.45f, 0.35f, 0.1f), () =>
                {
                    _table.StartNewHand();
                    RefreshAll();
                });
            var nextRt = _nextHandBtn.GetComponent<RectTransform>();
            nextRt.sizeDelta = new Vector2(280f, 64f);
            _nextHandBtn.gameObject.SetActive(false);
            SetActionButtons(false);
            ApplyHintsVisibility();

            _winnerOverlay = WinnerOverlay.Create(canvasGo.transform);
            // Поверх кнопок
            _winnerOverlay.transform.SetAsLastSibling();
        }

        void ToggleHints()
        {
            _hintsEnabled = !_hintsEnabled;
            PlayerPrefs.SetInt(HintsPrefsKey, _hintsEnabled ? 1 : 0);
            PlayerPrefs.Save();
            RefreshHintsToggleLabel();
            ApplyHintsVisibility();
            RefreshHint();
        }

        void RefreshHintsToggleLabel()
        {
            if (_hintsToggleLabel != null)
                _hintsToggleLabel.text = _hintsEnabled ? "Подсказки: вкл" : "Подсказки: выкл";
        }

        void ApplyHintsVisibility()
        {
            if (_hintPanel != null)
                _hintPanel.gameObject.SetActive(_hintsEnabled);
        }

        void RefreshHint()
        {
            if (!_hintsEnabled || _hintText == null || _table == null) return;
            var human = _table.Players[0];
            _hintText.text = HandHint.ForPlayer(human, _table.Board, _table.Street);
        }

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
            text.font = UiFont.Builtin();
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.color = color;
            text.alignment = TextAnchor.UpperLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            text.text = "";
            return text;
        }

        static Button CreateButton(Transform parent, string label, Vector2 anchoredPos, Color bg,
            UnityEngine.Events.UnityAction onClick)
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

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            var colors = btn.colors;
            colors.highlightedColor = Color.Lerp(bg, Color.white, 0.2f);
            colors.pressedColor = Color.Lerp(bg, Color.black, 0.2f);
            colors.disabledColor = new Color(0.25f, 0.25f, 0.28f, 0.85f);
            btn.colors = colors;
            btn.onClick.AddListener(onClick);

            var textGo = new GameObject("Label");
            textGo.transform.SetParent(go.transform, false);
            var trt = textGo.AddComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero;
            trt.offsetMax = Vector2.zero;
            var text = textGo.AddComponent<Text>();
            text.font = UiFont.Builtin();
            text.fontSize = 24;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.text = label;

            return btn;
        }

        void OnHuman(ActionType type)
        {
            if (!_table.AwaitingHumanAction) return;
            _table.TryApplyAction(_table.ActingSeat, new PlayerAction(type));
            RefreshAll();
        }

        void OnCheckOrCall()
        {
            if (!_table.AwaitingHumanAction) return;
            int seat = _table.ActingSeat;
            var legal = _table.GetLegalActions(seat);
            if (legal.CanCheck)
                _table.TryApplyAction(seat, new PlayerAction(ActionType.Check));
            else if (legal.CanCall)
                _table.TryApplyAction(seat, new PlayerAction(ActionType.Call));
            RefreshAll();
        }

        void OnBetOrRaise()
        {
            if (!_table.AwaitingHumanAction) return;
            int seat = _table.ActingSeat;
            var legal = _table.GetLegalActions(seat);
            if (legal.CanBet)
            {
                int amount = Mathf.Clamp(legal.Pot * raiseMultiplier / 4, legal.MinRaiseTo, legal.MaxRaiseTo);
                _table.TryApplyAction(seat, new PlayerAction(ActionType.Bet, amount));
            }
            else if (legal.CanRaise)
            {
                int amount = Mathf.Clamp(legal.MinRaiseTo + legal.Pot / 2, legal.MinRaiseTo, legal.MaxRaiseTo);
                _table.TryApplyAction(seat, new PlayerAction(ActionType.Raise, amount));
            }
            else
                _table.TryApplyAction(seat, new PlayerAction(ActionType.AllIn));
            RefreshAll();
        }

        void SetActionButtons(bool visible)
        {
            _foldBtn.gameObject.SetActive(visible);
            _checkCallBtn.gameObject.SetActive(visible);
            _betRaiseBtn.gameObject.SetActive(visible);
        }

        void RefreshActionButtons()
        {
            SetActionButtons(true);
            var legal = _table.GetLegalActions(_table.ActingSeat);
            _foldBtn.interactable = legal.CanFold;
            _checkCallBtn.interactable = legal.CanCheck || legal.CanCall;
            var checkCallLabel = _checkCallBtn.GetComponentInChildren<Text>();
            if (checkCallLabel != null)
                checkCallLabel.text = legal.CanCheck ? "Чек" : $"Колл {legal.CallAmount}";

            _betRaiseBtn.interactable = legal.CanBet || legal.CanRaise;
            var betLabel = _betRaiseBtn.GetComponentInChildren<Text>();
            if (betLabel != null)
            {
                if (legal.CanBet) betLabel.text = $"Бет {legal.MinRaiseTo}+";
                else if (legal.CanRaise) betLabel.text = $"Рейз до {legal.MinRaiseTo}+";
                else betLabel.text = "Олл-ин";
            }
        }

        void RefreshAll()
        {
            if (_table == null) return;

            _hudTitle.text = $"Раздача №{_table.HandNumber}   ·   {PokerRu.StreetName(_table.Street)}";
            _hudDetail.text = $"Нужно уравнять: {_table.CurrentBet}    ·    Блайнды {_table.SmallBlind}/{_table.BigBlind}";
            _logText.text = _table.LastActionLog;
            _potText.text = $"БАНК  {_table.Pot}";

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
                _seats[i].Refresh(player, dealer, acting, _table.Street, _table.BigBlind);

                string status = "";
                if (player.HasFolded) status = "ФОЛД";
                else if (player.IsAllIn) status = "ОЛЛ-ИН";
                else if (player.BetThisStreet > 0) status = $"ставка {player.BetThisStreet}";
                else if (acting) status = "ходит";

                string dealerMark = dealer ? " [D]" : "";
                string blindMark = "";
                if (i == _table.SmallBlindSeat) blindMark = " МБ";
                if (i == _table.BigBlindSeat) blindMark = " ББ";

                _seatUiLines[i].text = $"{player.Name}{dealerMark}{blindMark}\n{player.Chips} фишек" +
                                       (string.IsNullOrEmpty(status) ? "" : $"  ·  {status}");

                if (player.HasFolded)
                    _seatUiRows[i].color = new Color(0.2f, 0.2f, 0.22f, 0.85f);
                else if (acting)
                    _seatUiRows[i].color = new Color(0.45f, 0.35f, 0.08f, 0.95f);
                else if (player.Type == PlayerType.Human)
                    _seatUiRows[i].color = new Color(0.12f, 0.28f, 0.22f, 0.95f);
                else
                    _seatUiRows[i].color = new Color(0.12f, 0.15f, 0.2f, 0.95f);

                bool reveal = player.Type == PlayerType.Human ||
                              (_table.Street >= Street.Showdown && !player.HasFolded) ||
                              (_table.Street == Street.HandComplete && !player.HasFolded && _table.LastResult != null);

                var holes = _holeCards[i];
                for (int c = 0; c < 2; c++)
                {
                    if (c < player.HoleCards.Count)
                    {
                        holes[c].gameObject.SetActive(true);
                        holes[c].SetCard(player.HoleCards[c], reveal);
                    }
                    else
                        holes[c].gameObject.SetActive(false);
                }
            }

            RefreshHint();

            if (_table.AwaitingHumanAction)
                RefreshActionButtons();
            else if (_table.Street != Street.HandComplete)
                SetActionButtons(false);
        }
    }
}
