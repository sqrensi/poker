using UnityEngine;
using UnityEngine.UI;
using Poker.Presentation;

namespace Poker.Menu
{
    /// <summary>
    /// Главное меню: игра с ботами / онлайн (браузер) / выход.
    /// </summary>
    public sealed class MainMenuController : MonoBehaviour
    {
        [SerializeField] string onlineUrl = "http://localhost:8787";

        Text _status;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void BootMenu()
        {
            if (Object.FindObjectOfType<MainMenuController>() != null) return;
            if (Object.FindObjectOfType<PokerGameController>() != null) return;

            var go = new GameObject("MainMenu");
            go.AddComponent<MainMenuController>();
        }

        void Start()
        {
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
            scaler.referenceResolution = new Vector2(1920, 1080);
            canvasGo.AddComponent<GraphicRaycaster>();

            if (Object.FindObjectOfType<UnityEngine.EventSystems.EventSystem>() == null)
            {
                var es = new GameObject("EventSystem");
                es.AddComponent<UnityEngine.EventSystems.EventSystem>();
                es.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
            }

            var bg = CreatePanel(canvasGo.transform, Vector2.zero, Vector2.one, new Color(0.05f, 0.08f, 0.07f, 1f));
            Stretch(bg);

            var title = CreateText(canvasGo.transform, "ПОКЕР", new Vector2(0f, 220f), 64, FontStyle.Bold, new Color(1f, 0.9f, 0.4f));
            title.alignment = TextAnchor.MiddleCenter;

            var sub = CreateText(canvasGo.transform, "Texas Hold'em", new Vector2(0f, 150f), 28, FontStyle.Normal, new Color(0.75f, 0.85f, 0.9f));
            sub.alignment = TextAnchor.MiddleCenter;

            CreateButton(canvasGo.transform, "Играть с ботами (Unity)", new Vector2(0f, 40f), new Color(0.15f, 0.45f, 0.28f), StartBots);
            CreateButton(canvasGo.transform, "Онлайн в браузере", new Vector2(0f, -40f), new Color(0.15f, 0.35f, 0.55f), StartOnline);
            CreateButton(canvasGo.transform, "Выход", new Vector2(0f, -120f), new Color(0.35f, 0.18f, 0.18f), () =>
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
            });

            _status = CreateText(canvasGo.transform, "", new Vector2(0f, -220f), 20, FontStyle.Normal, new Color(0.85f, 0.75f, 0.45f));
            _status.alignment = TextAnchor.MiddleCenter;
            var stRt = _status.GetComponent<RectTransform>();
            stRt.sizeDelta = new Vector2(900f, 80f);
        }

        void StartBots()
        {
            // Убираем меню и запускаем локальный стол
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
            Application.OpenURL(onlineUrl);
            if (_status != null)
                _status.text =
                    "Онлайн = игра в браузере.\n" +
                    "1) cd server && npm start\n" +
                    "2) Создать стол → скопировать ссылку друзьям\n" +
                    "3) Хост жмёт «Начать матч»";
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
            text.font = UiFont.Builtin();
            text.text = content;
            text.fontSize = size;
            text.fontStyle = style;
            text.color = color;
            text.raycastTarget = false;
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
            text.font = UiFont.Builtin();
            text.text = label;
            text.fontSize = 26;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            return btn;
        }
    }
}
