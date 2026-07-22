using UnityEngine;
using UnityEngine.UI;
using Poker.Presentation;
using Poker.Identity;

namespace Poker.Menu
{
    /// <summary>Главное меню — Glass Night (pill-кнопки, орбы, без квадратов).</summary>
    public sealed class MainMenuController : MonoBehaviour
    {
        Text _status;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void BootMenu() { }

        public static void Open()
        {
            foreach (var g in Object.FindObjectsOfType<PokerGameController>())
                Object.Destroy(g.gameObject);
            if (Object.FindObjectOfType<MainMenuController>() != null) return;
            var go = new GameObject("MainMenu");
            go.AddComponent<MainMenuController>();
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

            var canvasGo = new GameObject("MenuCanvas");
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
            float btnW = phone ? 460f : 440f;
            float btnH = phone ? 68f : 64f;
            float y0 = phone ? 36f : 24f;
            float gap = phone ? 82f : 86f;

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

            // Центральная стеклянная карточка
            float cardH = phone ? 620f : 640f;
            float cardW = phone ? 560f : 540f;
            var card = MakeImage(canvasGo.transform, "GlassCard", UiTheme.Glass, stretch: false);
            UiTheme.ApplyRounded(card);
            var cardRt = card.rectTransform;
            cardRt.anchorMin = cardRt.anchorMax = cardRt.pivot = new Vector2(0.5f, 0.5f);
            cardRt.anchoredPosition = new Vector2(0f, phone ? -10f : 0f);
            cardRt.sizeDelta = new Vector2(cardW, cardH);
            var cardEdge = card.gameObject.AddComponent<Outline>();
            cardEdge.effectColor = UiTheme.GlassBorder;
            cardEdge.effectDistance = new Vector2(1.2f, -1.2f);

            // Бренд-марка (круг)
            var mark = MakeImage(canvasGo.transform, "BrandMark",
                new Color(UiTheme.Violet.r, UiTheme.Violet.g, UiTheme.Violet.b, 0.55f), false);
            UiTheme.ApplyCircle(mark);
            var markRt = mark.rectTransform;
            markRt.anchorMin = markRt.anchorMax = markRt.pivot = new Vector2(0.5f, 0.5f);
            markRt.anchoredPosition = new Vector2(0f, phone ? 248f : 265f);
            markRt.sizeDelta = new Vector2(72f, 72f);
            var markLabel = MakeText(mark.transform, "♠", Vector2.zero, new Vector2(72f, 72f), 36, FontStyle.Bold, Color.white);
            markLabel.alignment = TextAnchor.MiddleCenter;
            var mlRt = markLabel.rectTransform;
            mlRt.anchorMin = Vector2.zero;
            mlRt.anchorMax = Vector2.one;
            mlRt.offsetMin = mlRt.offsetMax = Vector2.zero;

            var brand = MakeText(canvasGo.transform, "HOLD'EM CLUB",
                new Vector2(0f, phone ? 175f : 190f), new Vector2(500f, 70f),
                phone ? 46 : 52, FontStyle.Bold, UiTheme.TextMain);
            brand.alignment = TextAnchor.MiddleCenter;
            UiTheme.StyleLabel(brand);

            var sub = MakeText(canvasGo.transform, "TEXAS HOLD'EM",
                new Vector2(0f, phone ? 128f : 140f), new Vector2(400f, 36f),
                phone ? 15 : 16, FontStyle.Normal, UiTheme.TextDim);
            sub.alignment = TextAnchor.MiddleCenter;
            sub.GetComponent<Outline>().effectDistance = new Vector2(0.4f, -0.4f);

            _status = MakeText(canvasGo.transform, PlayerIdentityService.GetNickname(),
                new Vector2(0f, phone ? 90f : 100f), new Vector2(400f, 32f),
                20, FontStyle.Normal, UiTheme.Cyan);
            _status.alignment = TextAnchor.MiddleCenter;

            MainMenuNicknameEditor.Create(canvasGo.transform, () =>
            {
                if (_status != null)
                    _status.text = PlayerIdentityService.GetNickname();
            });

            // CTA: coral pill
            CreatePillButton(canvasGo.transform, "Играть онлайн", new Vector2(0f, y0),
                btnW, btnH, UiTheme.Coral, Color.white, StartOnline);

            // Secondary glass pill + cyan rim
            var bots = CreatePillButton(canvasGo.transform, "Играть с ботами", new Vector2(0f, y0 - gap),
                btnW, btnH, UiTheme.GlassStrong, UiTheme.TextMain, StartBots);
            var botsOutline = bots.gameObject.AddComponent<Outline>();
            botsOutline.effectColor = new Color(UiTheme.Cyan.r, UiTheme.Cyan.g, UiTheme.Cyan.b, 0.45f);
            botsOutline.effectDistance = new Vector2(1.5f, -1.5f);

            CreatePillButton(canvasGo.transform, "Выход", new Vector2(0f, y0 - gap * 2f),
                btnW * 0.72f, btnH * 0.78f, new Color(1f, 1f, 1f, 0.05f), UiTheme.TextDim, () =>
                {
#if UNITY_EDITOR
                    UnityEditor.EditorApplication.isPlaying = false;
#else
                    Application.Quit();
#endif
                });
        }

        void StartBots()
        {
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
            string id = PlayerIdentityService.GetOrCreatePlayerId();
            string host = LanAddressUtil.GetSavedHost();
            if (string.IsNullOrEmpty(host)) host = "127.0.0.1";
            Application.OpenURL(LanAddressUtil.BuildUrl(host, id, autoQueue: true));
        }

        static Button CreatePillButton(Transform parent, string label, Vector2 pos,
            float w, float h, Color bg, Color labelColor, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject(label);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(w, h);

            var img = go.AddComponent<Image>();
            img.color = bg;
            UiTheme.ApplyPill(img);

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            var colors = btn.colors;
            colors.highlightedColor = Color.Lerp(bg, Color.white, 0.14f);
            colors.pressedColor = Color.Lerp(bg, Color.black, 0.12f);
            btn.colors = colors;
            btn.onClick.AddListener(onClick);

            var text = MakeText(go.transform, label, Vector2.zero, new Vector2(w, h), 26, FontStyle.Bold, labelColor);
            text.alignment = TextAnchor.MiddleCenter;
            var trt = text.rectTransform;
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = trt.offsetMax = Vector2.zero;
            UiTheme.StyleLabel(text);
            return btn;
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
