using UnityEngine;
using UnityEngine.UI;
using Poker.Presentation;
using Poker.Identity;

namespace Poker.Menu
{
    /// <summary>
    /// Главное меню: боты / онлайн (браузер с id+ником) / выход.
    /// </summary>
    public sealed class MainMenuController : MonoBehaviour
    {
        [SerializeField] string onlineUrl = "http://localhost:8787";

        Text _status;
        Text _idLine;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void BootMenu()
        {
            if (UnityEngine.Object.FindObjectOfType<MainMenuController>() != null) return;
            if (UnityEngine.Object.FindObjectOfType<PokerGameController>() != null) return;

            var go = new GameObject("MainMenu");
            go.AddComponent<MainMenuController>();
        }

        void Start()
        {
            VisualQuality.Apply(Camera.main);
            BuildUi();
        }

        void BuildUi()
        {
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
            float btnW = phone ? 380f : 360f;
            float btnH = phone ? 64f : 64f;
            float y0 = phone ? 40f : 30f;
            float gap = phone ? 78f : 80f;

            if (UnityEngine.Object.FindObjectOfType<UnityEngine.EventSystems.EventSystem>() == null)
            {
                var es = new GameObject("EventSystem");
                es.AddComponent<UnityEngine.EventSystems.EventSystem>();
                es.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
            }

            var bg = CreatePanel(canvasGo.transform, Vector2.zero, Vector2.one, new Color(0.05f, 0.08f, 0.07f, 1f));
            Stretch(bg);

            // Ландшафт: заголовок выше центра, кнопки по центру
            var title = CreateText(canvasGo.transform, "ПОКЕР", new Vector2(0f, phone ? 200f : 240f), phone ? 56 : 72, FontStyle.Bold, new Color(1f, 0.9f, 0.4f));
            title.alignment = TextAnchor.MiddleCenter;

            var sub = CreateText(canvasGo.transform, "Texas Hold'em", new Vector2(0f, phone ? 140f : 170f), phone ? 24 : 32, FontStyle.Normal, new Color(0.75f, 0.85f, 0.9f));
            sub.alignment = TextAnchor.MiddleCenter;

            string nick = PlayerIdentityService.GetNickname();
            string pid = PlayerIdentityService.GetOrCreatePlayerId();
            _idLine = CreateText(canvasGo.transform,
                $"{nick}  ·  id …{pid.Substring(Mathf.Max(0, pid.Length - 8))}",
                new Vector2(0f, phone ? 90f : 110f), 18, FontStyle.Normal, new Color(0.7f, 0.78f, 0.72f));
            _idLine.alignment = TextAnchor.MiddleCenter;

            MainMenuNicknameEditor.Create(canvasGo.transform, () =>
            {
                string n = PlayerIdentityService.GetNickname();
                string id = PlayerIdentityService.GetOrCreatePlayerId();
                if (_idLine != null)
                    _idLine.text = $"{n}  ·  id …{id.Substring(Mathf.Max(0, id.Length - 8))}";
            });

            var b1 = CreateButton(canvasGo.transform, "Играть с ботами (Unity)", new Vector2(0f, y0), new Color(0.15f, 0.45f, 0.28f), StartBots);
            var b2 = CreateButton(canvasGo.transform, "Онлайн в браузере", new Vector2(0f, y0 - gap), new Color(0.15f, 0.35f, 0.55f), StartOnline);
            var b3 = CreateButton(canvasGo.transform, "Выход", new Vector2(0f, y0 - gap * 2f), new Color(0.35f, 0.18f, 0.18f), () =>
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
            });
            b1.GetComponent<RectTransform>().sizeDelta = new Vector2(btnW, btnH);
            b2.GetComponent<RectTransform>().sizeDelta = new Vector2(btnW, btnH);
            b3.GetComponent<RectTransform>().sizeDelta = new Vector2(btnW, btnH);

            _status = CreateText(canvasGo.transform, "", new Vector2(0f, y0 - gap * 3f - 10f), 18, FontStyle.Normal, new Color(0.85f, 0.75f, 0.45f));
            _status.alignment = TextAnchor.MiddleCenter;
            var stRt = _status.GetComponent<RectTransform>();
            stRt.sizeDelta = new Vector2(phone ? 820f : 920f, 80f);
        }

        void StartBots()
        {
            foreach (var c in FindObjectsOfType<Canvas>())
            {
                if (c.gameObject.name == "MenuCanvas")
                    Destroy(c.gameObject);
            }
            Destroy(gameObject);
            var go = new GameObject("PokerGame");
            go.AddComponent<PokerGameController>();
        }

        void StartOnline()
        {
            string id = PlayerIdentityService.GetOrCreatePlayerId();
            string url = $"{onlineUrl.TrimEnd('/')}?pid={System.Uri.EscapeDataString(id)}";
            Application.OpenURL(url);
            if (_status != null)
                _status.text =
                    "Открыт браузер с вашим ID.\n" +
                    "Быстрая игра = очередь 2–10 · или комната по ссылке.\n" +
                    "cd server && npm start";
        }

        static Image CreatePanel(Transform parent, Vector2 min, Vector2 max, Color color)
        {
            var go = new GameObject("Panel");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var img = go.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        static void Stretch(Image img)
        {
            var rt = img.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        static Text CreateText(Transform parent, string content, Vector2 pos, int size, FontStyle style, Color color)
        {
            var go = new GameObject("Text");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(800f, 80f);
            var text = go.AddComponent<Text>();
            text.text = content;
            text.fontSize = size;
            text.fontStyle = style;
            text.color = color;
            text.raycastTarget = false;
            UiFont.MakeCrisp(text, 0.5f);
            return text;
        }

        static Button CreateButton(Transform parent, string label, Vector2 pos, Color bg, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject(label);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(360f, 64f);
            var img = go.AddComponent<Image>();
            img.color = bg;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(onClick);

            var tGo = new GameObject("Label");
            tGo.transform.SetParent(go.transform, false);
            var trt = tGo.AddComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = trt.offsetMax = Vector2.zero;
            var text = tGo.AddComponent<Text>();
            text.text = label;
            text.fontSize = 28;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            UiFont.MakeCrisp(text, 0.4f);
            return btn;
        }
    }
}
