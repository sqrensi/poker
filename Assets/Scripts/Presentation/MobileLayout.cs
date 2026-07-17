using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace Poker.Presentation
{
    /// <summary>Адаптация Canvas и кнопок под телефон / узкий экран.</summary>
    public static class MobileLayout
    {
        public static bool IsPhoneLike()
        {
            if (Application.isMobilePlatform) return true;
            // Узкий экран в редакторе / WebGL
            float aspect = (float)Screen.width / Mathf.Max(1, Screen.height);
            return aspect < 0.75f || Screen.width < 900;
        }

        public static void ConfigureCanvas(CanvasScaler scaler)
        {
            if (scaler == null) return;
            if (IsPhoneLike())
            {
                scaler.referenceResolution = new Vector2(1080, 1920);
                scaler.matchWidthOrHeight = 0.65f; // ближе к высоте на портрете
            }
            else
            {
                scaler.referenceResolution = new Vector2(1920, 1080);
                scaler.matchWidthOrHeight = 0.5f;
            }
        }

        public static void EnsureTouchInput()
        {
            Input.multiTouchEnabled = true;
            var es = Object.FindObjectOfType<EventSystem>();
            if (es == null) return;
            // StandaloneInputModule уже обрабатывает touch в Unity
            var sim = es.GetComponent<StandaloneInputModule>();
            if (sim != null)
            {
                sim.forceModuleActive = true;
            }
        }

        public static void EnlargeButton(Button btn, float minWidth, float height)
        {
            if (btn == null) return;
            var rt = btn.GetComponent<RectTransform>();
            if (rt == null) return;
            var size = rt.sizeDelta;
            size.x = Mathf.Max(size.x, minWidth);
            size.y = Mathf.Max(size.y, height);
            rt.sizeDelta = size;
        }

        public static void PlaceBottomBar(RectTransform rt, float yFromBottom, float height, float sidePad)
        {
            if (rt == null) return;
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0f, yFromBottom);
            rt.sizeDelta = new Vector2(-sidePad * 2f, height);
        }
    }
}
