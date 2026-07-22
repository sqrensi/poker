using UnityEngine;
using UnityEngine.UI;
using Poker.Presentation;
using Poker.Identity;
using Poker.Network;

namespace Poker.Menu
{
    /// <summary>Главное меню — Glass Night (pill-кнопки, орбы, без квадратов).</summary>
    public sealed class MainMenuController : MonoBehaviour
    {
        Text _coinsAmountText;
        GameObject _coinsBadge;
        GameObject _canvasGo;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void BootMenu() { }

        /// <summary>Вернуться в меню после игры (не пересоздаёт, если уже есть).</summary>
        public static void Open()
        {
            foreach (var g in Object.FindObjectsOfType<PokerGameController>())
                Object.Destroy(g.gameObject);
            foreach (var mm in Object.FindObjectsOfType<OnlineMatchmakingController>())
                Object.Destroy(mm.gameObject);
            foreach (var c in Object.FindObjectsOfType<Canvas>())
            {
                if (c.gameObject.name == "MatchmakingCanvas")
                    Object.Destroy(c.gameObject);
            }
            var client = Object.FindObjectOfType<PokerOnlineClient>();
            if (client != null)
                Object.Destroy(client.gameObject);

            var menu = Object.FindObjectOfType<MainMenuController>();
            if (menu != null)
            {
                menu.ShowMenu();
                return;
            }

            var go = new GameObject("MainMenu");
            go.AddComponent<MainMenuController>();
        }

        public void HideMenu()
        {
            if (_canvasGo != null)
                _canvasGo.SetActive(false);
        }

        public void ShowMenu()
        {
            if (_canvasGo != null)
                _canvasGo.SetActive(true);
            RefreshHeader();
        }

        void Start()
        {
            VisualQuality.Apply(Camera.main);
            try { BuildUi(); }
            catch (System.Exception e) { Debug.LogException(e); }
        }

        void BuildUi()
        {
            UiTheme.WarmUp();
            PokerSoundFx.WarmUp();

            var canvasGo = new GameObject("MenuCanvas");
            _canvasGo = canvasGo;
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.referencePixelsPerUnit = 100f;
            MobileLayout.ConfigureCanvas(scaler);
            canvasGo.AddComponent<GraphicRaycaster>();
            MobileLayout.EnsureTouchInput();

            bool phone = MobileLayout.IsPhoneLike();
            float btnW = phone ? 420f : 400f;
            float btnH = phone ? 62f : 58f;

            if (Object.FindObjectOfType<UnityEngine.EventSystems.EventSystem>() == null)
            {
                var es = new GameObject("EventSystem");
                es.AddComponent<UnityEngine.EventSystems.EventSystem>();
                es.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
            }

            // Фон
            var bg = MakeImage(canvasGo.transform, "BG", UiTheme.Bg, stretch: true);
            bg.type = Image.Type.Simple;
            bg.sprite = UiFont.WhiteSprite();

            // Мягкие орбы (круглые, не квадраты)
            UiTheme.MakeOrb(canvasGo.transform, "OrbV", new Vector2(-0.15f, 0.35f), new Vector2(0.55f, 1.15f), UiTheme.OrbViolet);
            UiTheme.MakeOrb(canvasGo.transform, "OrbC", new Vector2(0.4f, -0.2f), new Vector2(1.15f, 0.55f), UiTheme.OrbCyan);
            UiTheme.MakeOrb(canvasGo.transform, "OrbO", new Vector2(0.15f, 0.15f), new Vector2(0.75f, 0.75f), UiTheme.OrbCoral);

            var uiRoot = SafeAreaLayout.Ensure(canvasGo.transform);

            // Центральная карточка — якоря сверху вниз, без наложений
            float cardW = phone ? 500f : 480f;
            float padTop = phone ? 24f : 28f;
            float markSize = 56f;
            float rowGap = phone ? 14f : 16f;
            float sectionGap = phone ? 22f : 26f;

            var card = MakeImage(uiRoot, "GlassCard", UiTheme.Glass, stretch: false);
            UiTheme.ApplyRounded(card);
            var cardRt = card.rectTransform;
            cardRt.anchorMin = cardRt.anchorMax = cardRt.pivot = new Vector2(0.5f, 0.5f);
            cardRt.anchoredPosition = new Vector2(0f, phone ? 20f : 12f);
            Transform cardRoot = card.transform;

            CreateCoinsBadge(uiRoot, phone);

            float y = padTop;

            var mark = MakeImage(cardRoot, "BrandMark",
                new Color(UiTheme.Violet.r, UiTheme.Violet.g, UiTheme.Violet.b, 0.55f), false);
            UiTheme.ApplyCircle(mark);
            PinTopCenter(mark.rectTransform, y, new Vector2(markSize, markSize));
            y += markSize + rowGap;

            var markLabel = MakeText(mark.transform, "♠", Vector2.zero, new Vector2(markSize, markSize),
                28, FontStyle.Bold, Color.white);
            markLabel.alignment = TextAnchor.MiddleCenter;
            StretchRect(markLabel.rectTransform);

            float brandH = phone ? 48f : 52f;
            var brand = MakeText(cardRoot, "HOLD'EM CLUB", Vector2.zero,
                new Vector2(cardW - 48f, brandH), phone ? 38 : 44, FontStyle.Bold, UiTheme.TextMain);
            brand.alignment = TextAnchor.MiddleCenter;
            UiTheme.StyleLabel(brand);
            PinTopCenter(brand.rectTransform, y, new Vector2(cardW - 48f, brandH));
            y += brandH + 6f;

            float subH = 22f;
            var sub = MakeText(cardRoot, "TEXAS HOLD'EM", Vector2.zero,
                new Vector2(cardW - 48f, subH), phone ? 13 : 14, FontStyle.Normal, UiTheme.TextDim);
            sub.alignment = TextAnchor.MiddleCenter;
            PinTopCenter(sub.rectTransform, y, new Vector2(cardW - 48f, subH));
            y += subH + sectionGap;

            CreatePillButtonTop(cardRoot, "Онлайн матч", y, btnW, btnH, UiTheme.Coral, Color.white, StartOnline);
            y += btnH + 10f;

            CreateFillBotsToggleTop(cardRoot, y, btnW);
            y += 44f + 10f;

            var bots = CreatePillButtonTop(cardRoot, "Играть с ботами", y, btnW, btnH,
                UiTheme.GlassStrong, UiTheme.TextMain, StartBots);
            var botsOutline = bots.gameObject.AddComponent<Outline>();
            botsOutline.effectColor = new Color(UiTheme.Cyan.r, UiTheme.Cyan.g, UiTheme.Cyan.b, 0.45f);
            botsOutline.effectDistance = new Vector2(1.5f, -1.5f);
            y += btnH + rowGap;

            float exitH = btnH * 0.85f;
            CreatePillButtonTop(cardRoot, "Выход", y, btnW * 0.68f, exitH,
                new Color(1f, 1f, 1f, 0.07f), UiTheme.TextDim, () =>
                {
#if UNITY_EDITOR
                    UnityEditor.EditorApplication.isPlaying = false;
#else
                    Application.Quit();
#endif
                });
            y += exitH + padTop;

            cardRt.sizeDelta = new Vector2(cardW, y);
            var cardEdge = card.gameObject.AddComponent<Outline>();
            cardEdge.effectColor = UiTheme.GlassBorder;
            cardEdge.effectDistance = new Vector2(1.2f, -1.2f);

            MainMenuNicknameEditor.Create(uiRoot, RefreshHeader);

            if (_coinsBadge != null) _coinsBadge.transform.SetAsLastSibling();
        }

        static readonly Color CoinGold = UiTheme.Hex(0xFFD166);
        static readonly Color CoinGoldSoft = UiTheme.Hex(0xFFE9A3);

        void CreateCoinsBadge(Transform parent, bool phone)
        {
            float pad = phone ? 12f : 18f;
            _coinsBadge = new GameObject("CoinsBadge");
            _coinsBadge.transform.SetParent(parent, false);
            var rt = _coinsBadge.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(pad, -pad);
            rt.sizeDelta = new Vector2(phone ? 148f : 160f, phone ? 44f : 46f);

            var bg = _coinsBadge.AddComponent<Image>();
            bg.color = UiTheme.GlassStrong;
            bg.raycastTarget = false;
            UiTheme.ApplyRoundedSmall(bg);
            var edge = _coinsBadge.AddComponent<Outline>();
            edge.effectColor = new Color(CoinGold.r, CoinGold.g, CoinGold.b, 0.45f);
            edge.effectDistance = new Vector2(1.2f, -1.2f);

            var iconGo = new GameObject("CoinIcon");
            iconGo.transform.SetParent(_coinsBadge.transform, false);
            var iconRt = iconGo.AddComponent<RectTransform>();
            iconRt.anchorMin = iconRt.anchorMax = new Vector2(0f, 0.5f);
            iconRt.pivot = new Vector2(0f, 0.5f);
            iconRt.anchoredPosition = new Vector2(12f, 0f);
            iconRt.sizeDelta = new Vector2(phone ? 34f : 36f, phone ? 34f : 36f);
            var iconBg = iconGo.AddComponent<Image>();
            iconBg.color = new Color(CoinGold.r, CoinGold.g, CoinGold.b, 0.22f);
            iconBg.raycastTarget = false;
            UiTheme.ApplyCircle(iconBg);
            var iconLabel = MakeText(iconGo.transform, "◆", Vector2.zero, iconRt.sizeDelta,
                phone ? 22 : 24, FontStyle.Bold, CoinGold);
            iconLabel.alignment = TextAnchor.MiddleCenter;
            var iconLabelRt = iconLabel.rectTransform;
            iconLabelRt.anchorMin = Vector2.zero;
            iconLabelRt.anchorMax = Vector2.one;
            iconLabelRt.offsetMin = iconLabelRt.offsetMax = Vector2.zero;
            UiTheme.StyleLabel(iconLabel, false);

            var textCol = new GameObject("Texts");
            textCol.transform.SetParent(_coinsBadge.transform, false);
            var colRt = textCol.AddComponent<RectTransform>();
            colRt.anchorMin = Vector2.zero;
            colRt.anchorMax = Vector2.one;
            colRt.offsetMin = new Vector2(phone ? 50f : 54f, 0f);
            colRt.offsetMax = new Vector2(-10f, 0f);

            _coinsAmountText = MakeText(textCol.transform, FormatCoinsAmount(), Vector2.zero,
                new Vector2(120f, 36f), phone ? 24 : 26, FontStyle.Bold, CoinGoldSoft);
            _coinsAmountText.alignment = TextAnchor.MiddleLeft;
            UiTheme.StyleLabel(_coinsAmountText);
            var amountOutline = _coinsAmountText.GetComponent<Outline>();
            if (amountOutline != null)
                amountOutline.effectColor = new Color(CoinGold.r * 0.35f, CoinGold.g * 0.2f, 0f, 0.55f);
            StretchRect(_coinsAmountText.rectTransform);
        }

        void RefreshHeader()
        {
            if (_coinsAmountText != null)
                _coinsAmountText.text = FormatCoinsAmount();
        }

        static string FormatCoinsAmount()
            => PlayerWalletService.FormatCoins(PlayerWalletService.GetCoins());

        void StartBots()
        {
            if (!PlayerWalletService.TryChargeBuyIn(out string err))
            {
                RefreshHeader();
                Debug.LogWarning("[Poker] " + err);
                return;
            }
            // Убрать старые меню/партии, чтобы UI не дублировался.
            foreach (var c in FindObjectsOfType<Canvas>())
            {
                string n = c.gameObject.name;
                if (n == "MenuCanvas" || n == "Canvas" || n == "LoadingCanvas")
                    Destroy(c.gameObject);
            }
            foreach (var g in FindObjectsOfType<PokerGameController>())
            {
                if (g != null && g.gameObject != gameObject)
                    Destroy(g.gameObject);
            }
            Destroy(gameObject);
            var go = new GameObject("PokerGame");
            go.AddComponent<PokerGameController>();
        }

        void StartOnline()
        {
            if (!PlayerWalletService.CanAffordBuyIn())
            {
                RefreshHeader();
                Debug.LogWarning("[Poker] Недостаточно монет для онлайн-матча");
                return;
            }
            OnlineMatchmakingController.StartMatchmaking(this);
        }

        internal void OnMatchmakingCancelled()
        {
            ShowMenu();
        }

        static void CreateFillBotsToggleTop(Transform parent, float top, float width)
        {
            const float toggleSize = 36f;
            const float checkSize = 20f;
            const float toggleLeft = 8f;

            var row = new GameObject("FillBotsToggle");
            row.transform.SetParent(parent, false);
            PinTopCenter(row.AddComponent<RectTransform>(), top, new Vector2(width, 40f));

            var toggleGo = new GameObject("Toggle");
            toggleGo.transform.SetParent(row.transform, false);
            var toggleRt = toggleGo.AddComponent<RectTransform>();
            toggleRt.anchorMin = toggleRt.anchorMax = new Vector2(0f, 0.5f);
            toggleRt.pivot = new Vector2(0.5f, 0.5f);
            toggleRt.anchoredPosition = new Vector2(toggleLeft + toggleSize * 0.5f, 0f);
            toggleRt.sizeDelta = new Vector2(toggleSize, toggleSize);

            var bg = toggleGo.AddComponent<Image>();
            bg.color = UiTheme.GlassStrong;
            bg.raycastTarget = true;
            UiTheme.ApplyCircle(bg);

            var checkGo = new GameObject("Check");
            checkGo.transform.SetParent(toggleGo.transform, false);
            var checkRt = checkGo.AddComponent<RectTransform>();
            checkRt.anchorMin = checkRt.anchorMax = new Vector2(0.5f, 0.5f);
            checkRt.pivot = new Vector2(0.5f, 0.5f);
            checkRt.anchoredPosition = Vector2.zero;
            checkRt.sizeDelta = new Vector2(checkSize, checkSize);
            var checkImg = checkGo.AddComponent<Image>();
            checkImg.color = UiTheme.Cyan;
            checkImg.raycastTarget = false;
            UiTheme.ApplyCircle(checkImg);

            var toggle = toggleGo.AddComponent<Toggle>();
            toggle.targetGraphic = bg;
            toggle.graphic = checkImg;
            toggle.isOn = OnlineMatchPreferences.FillWithBots;
            toggle.transition = Selectable.Transition.None;
            toggle.toggleTransition = Toggle.ToggleTransition.None;
            toggle.onValueChanged.AddListener(v => OnlineMatchPreferences.FillWithBots = v);

            var label = MakeText(row.transform, "Подбор с ботами",
                Vector2.zero, new Vector2(width - toggleLeft - toggleSize - 16f, 40f),
                17, FontStyle.Normal, UiTheme.TextMain);
            label.alignment = TextAnchor.MiddleLeft;
            var labelRt = label.rectTransform;
            labelRt.anchorMin = new Vector2(0f, 0f);
            labelRt.anchorMax = new Vector2(1f, 1f);
            labelRt.offsetMin = new Vector2(toggleLeft + toggleSize + 12f, 0f);
            labelRt.offsetMax = new Vector2(-8f, 0f);
        }

        static Button CreatePillButtonTop(Transform parent, string label, float top,
            float w, float h, Color bg, Color labelColor, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject(label);
            go.transform.SetParent(parent, false);
            PinTopCenter(go.AddComponent<RectTransform>(), top, new Vector2(w, h));

            var img = go.AddComponent<Image>();
            img.color = bg;
            UiTheme.ApplyRounded(img);

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            var colors = btn.colors;
            colors.highlightedColor = Color.Lerp(bg, Color.white, 0.14f);
            colors.pressedColor = Color.Lerp(bg, Color.black, 0.12f);
            btn.colors = colors;
            PokerSoundFx.BindButton(btn, onClick);

            var text = MakeText(go.transform, label, Vector2.zero, new Vector2(w, h), 26, FontStyle.Bold, labelColor);
            text.alignment = TextAnchor.MiddleCenter;
            text.raycastTarget = false;
            StretchRect(text.rectTransform);
            UiTheme.StyleLabel(text);
            return btn;
        }

        static void PinTopCenter(RectTransform rt, float top, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -top);
            rt.sizeDelta = size;
        }

        static void StretchRect(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        static Image MakeImage(Transform parent, string name, Color color, bool stretch)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            if (stretch)
            {
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = rt.offsetMax = Vector2.zero;
            }
            var img = go.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        static Text MakeText(Transform parent, string content, Vector2 pos, Vector2 size,
            int fontSize, FontStyle style, Color color)
        {
            var go = new GameObject("Text");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            var text = go.AddComponent<Text>();
            text.text = content;
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.color = color;
            text.raycastTarget = false;
            UiFont.MakeCrisp(text, 0.35f);
            return text;
        }
    }
}
