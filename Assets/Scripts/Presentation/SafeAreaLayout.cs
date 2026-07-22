using UnityEngine;

namespace Poker.Presentation
{
    /// <summary>Учитывает Screen.safeArea (вырез, камера, системные панели).</summary>
    public sealed class SafeAreaFitter : MonoBehaviour
    {
        Rect _lastSafe;
        Vector2Int _lastScreen;

        RectTransform _rt;

        void Awake()
        {
            _rt = transform as RectTransform;
            if (_rt == null)
                _rt = gameObject.AddComponent<RectTransform>();
        }

        void Start() => ApplyNow();

        void Update()
        {
            if (Screen.safeArea == _lastSafe
                && Screen.width == _lastScreen.x
                && Screen.height == _lastScreen.y)
                return;

            ApplyNow();
        }

        void ApplyNow()
        {
            SafeAreaLayout.Apply(_rt);
            _lastSafe = Screen.safeArea;
            _lastScreen = new Vector2Int(Screen.width, Screen.height);
        }

        internal void MarkApplied() => ApplyNow();
    }

    public static class SafeAreaLayout
    {
        /// <summary>Контейнер SafeArea внутри Canvas — сюда вешать весь интерактивный UI.</summary>
        public static RectTransform Ensure(Transform canvasTransform)
        {
            var existing = canvasTransform.Find("SafeArea");
            if (existing != null)
            {
                var rt = existing as RectTransform;
                Apply(rt);
                return rt;
            }

            var go = new GameObject("SafeArea");
            go.transform.SetParent(canvasTransform, false);
            var rect = go.AddComponent<RectTransform>();
            StretchFull(rect);
            var fitter = go.AddComponent<SafeAreaFitter>();
            Apply(rect);
            fitter.MarkApplied();
            return rect;
        }

        public static void Apply(RectTransform safeArea)
        {
            if (safeArea == null || Screen.width <= 0 || Screen.height <= 0)
                return;

            Rect s = Screen.safeArea;
            safeArea.anchorMin = new Vector2(s.xMin / Screen.width, s.yMin / Screen.height);
            safeArea.anchorMax = new Vector2(s.xMax / Screen.width, s.yMax / Screen.height);
            safeArea.offsetMin = Vector2.zero;
            safeArea.offsetMax = Vector2.zero;
        }

        static void StretchFull(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}
