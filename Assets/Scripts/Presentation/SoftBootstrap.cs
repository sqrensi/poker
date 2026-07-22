using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Poker.Presentation
{
    /// <summary>
    /// Мягкий старт APK: сначала загрузочный экран и прогрев Resources,
    /// потом меню. Стол (овал) НЕ создаётся до успешного материала.
    /// Симулятор Editor ≠ APK: в Editor Shader.Find всегда находит Unlit/Color,
    /// в билде без ссылки на материал шейдер вырезается → magenta-овал.
    /// </summary>
    public sealed class SoftBootstrap : MonoBehaviour
    {
        public static bool AssetsReady { get; private set; }

        Text _status;
        Image _barFill;
        GameObject _root;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (UnityEngine.Object.FindObjectOfType<SoftBootstrap>() != null) return;
            if (UnityEngine.Object.FindObjectOfType<PokerGameController>() != null) return;
            if (UnityEngine.Object.FindObjectOfType<Poker.Menu.MainMenuController>() != null) return;

            var go = new GameObject("SoftBootstrap");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<SoftBootstrap>();
        }

        void Start()
        {
            BuildLoadingUi();
            StartCoroutine(WarmUpAndOpenMenu());
        }

        IEnumerator WarmUpAndOpenMenu()
        {
            SetProgress(0.05f, "Подготовка…");
            yield return null;

            SetProgress(0.2f, "Материалы стола…");
            string matErr = PokerMaterials.WarmUp();
            yield return null;

            if (matErr != null)
            {
                SetProgress(0f, "Ошибка материалов:\n" + matErr);
                yield break;
            }

            SetProgress(0.4f, "Шрифты UI…");
            UiTheme.WarmUp();
            PokerSoundFx.WarmUp();
            yield return null;

            SetProgress(0.65f, "Карты…");
            CardSpriteCatalog.EnsureLoaded();
            yield return null;

            SetProgress(0.85f, "Качество…");
            VisualQuality.Apply(Camera.main);
            yield return null;

            SetProgress(1f, "Готово");
            yield return new WaitForSecondsRealtime(0.35f);

            AssetsReady = true;
            Destroy(_root);
            _root = null;

            Poker.Menu.MainMenuController.Open();
            Destroy(gameObject);
        }

        void SetProgress(float t, string msg)
        {
            if (_status != null) _status.text = msg;
            if (_barFill != null)
            {
                var rt = _barFill.rectTransform;
                rt.anchorMin = new Vector2(0f, 0f);
                rt.anchorMax = new Vector2(Mathf.Clamp01(t), 1f);
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
            }
        }

        void BuildLoadingUi()
        {
            UiTheme.WarmUp();

            _root = new GameObject("LoadingCanvas");
            var canvas = _root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 500;
            var scaler = _root.AddComponent<CanvasScaler>();
            MobileLayout.ConfigureCanvas(scaler);
            _root.AddComponent<GraphicRaycaster>();

            var bg = MakeImage(_root.transform, "BG", UiTheme.Bg, flat: true);
            Stretch(bg.rectTransform);

            UiTheme.MakeOrb(_root.transform, "OrbV", new Vector2(-0.1f, 0.3f), new Vector2(0.6f, 1.1f), UiTheme.OrbViolet);
            UiTheme.MakeOrb(_root.transform, "OrbC", new Vector2(0.4f, -0.15f), new Vector2(1.1f, 0.55f), UiTheme.OrbCyan);

            var uiRoot = SafeAreaLayout.Ensure(_root.transform);

            var titleGo = new GameObject("Title");
            titleGo.transform.SetParent(uiRoot, false);
            var titleRt = titleGo.AddComponent<RectTransform>();
            titleRt.anchorMin = titleRt.anchorMax = titleRt.pivot = new Vector2(0.5f, 0.5f);
            titleRt.anchoredPosition = new Vector2(0f, 80f);
            titleRt.sizeDelta = new Vector2(800f, 80f);
            var title = titleGo.AddComponent<Text>();
            title.text = "HOLD'EM CLUB";
            title.fontSize = 52;
            title.fontStyle = FontStyle.Bold;
            title.alignment = TextAnchor.MiddleCenter;
            title.color = UiTheme.TextMain;
            UiFont.MakeCrisp(title, 0.4f);
            UiTheme.StyleLabel(title);

            _status = MakeText(uiRoot, "Status", new Vector2(0f, -10f), 24, UiTheme.TextSoft);
            _status.text = "Загрузка…";

            var barBg = MakeImage(uiRoot, "BarBg", new Color(1f, 1f, 1f, 0.08f), flat: false);
            UiTheme.ApplyRoundedSmall(barBg);
            var barBgRt = barBg.rectTransform;
            barBgRt.anchorMin = barBgRt.anchorMax = barBgRt.pivot = new Vector2(0.5f, 0.5f);
            barBgRt.anchoredPosition = new Vector2(0f, -70f);
            barBgRt.sizeDelta = new Vector2(480f, 14f);

            _barFill = MakeImage(barBg.transform, "BarFill", UiTheme.Coral, flat: false);
            UiTheme.ApplyRoundedSmall(_barFill);
            var fillRt = _barFill.rectTransform;
            fillRt.anchorMin = new Vector2(0f, 0f);
            fillRt.anchorMax = new Vector2(0.05f, 1f);
            fillRt.offsetMin = Vector2.zero;
            fillRt.offsetMax = Vector2.zero;
        }

        static Image MakeImage(Transform parent, string name, Color color, bool flat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();
            var img = go.AddComponent<Image>();
            img.color = color;
            if (flat)
            {
                img.sprite = UiFont.WhiteSprite();
                img.type = Image.Type.Simple;
            }
            else UiTheme.ApplyRounded(img);
            return img;
        }

        static Text MakeText(Transform parent, string name, Vector2 pos, int size, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(900f, 100f);
            var text = go.AddComponent<Text>();
            text.fontSize = size;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = color;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            UiFont.MakeCrisp(text, 0.3f);
            return text;
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}
