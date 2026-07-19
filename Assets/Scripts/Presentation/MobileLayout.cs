using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace Poker.Presentation
{
    /// <summary>Адаптация Canvas под телефон в альбомной ориентации.</summary>
    public static class MobileLayout
    {
        public static bool IsPhoneLike()
        {
            if (Application.isMobilePlatform) return true;
            float shortest = Mathf.Min(Screen.width, Screen.height);
            return shortest > 0 && shortest < 900;
        }

        public static void ConfigureCanvas(CanvasScaler scaler)
        {
            if (scaler == null) return;
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            // Меньший reference на телефоне → крупнее весь UI в ландшафте
            if (IsPhoneLike())
            {
                scaler.referenceResolution = new Vector2(1400f, 788f);
                scaler.matchWidthOrHeight = 0.35f;
            }
            else
            {
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.matchWidthOrHeight = 0.5f;
            }
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        }

        public static void EnsureTouchInput()
        {
            Input.multiTouchEnabled = true;
            var es = Object.FindObjectOfType<EventSystem>();
            if (es == null) return;
            var sim = es.GetComponent<StandaloneInputModule>();
            if (sim != null)
                sim.forceModuleActive = true;
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
    }
}
