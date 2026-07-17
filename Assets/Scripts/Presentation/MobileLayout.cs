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
            // Всегда ландшафтный референс — вертикаль на телефоне отключена
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = IsPhoneLike() ? 0.55f : 0.5f;
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
