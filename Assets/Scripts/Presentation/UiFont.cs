using UnityEngine;
using UnityEngine.UI;

namespace Poker.Presentation
{
    public static class UiFont
    {
        static Font _cached;
        static Sprite _whiteSprite;

        public static Font Builtin()
        {
            if (_cached != null) return _cached;

            // Предпочитаем геометрические sans с ОС (как на референсах), не LegacyRuntime.
            _cached = Font.CreateDynamicFontFromOSFont(
                new[]
                {
                    "Segoe UI Semibold",
                    "Segoe UI",
                    "Bahnschrift",
                    "Montserrat",
                    "Poppins",
                    "Outfit",
                    "Helvetica Neue",
                    "Arial",
                    "Roboto",
                    "sans-serif"
                },
                42);

            if (_cached == null)
                _cached = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (_cached == null)
                _cached = Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (_cached == null)
                Debug.LogError("[Poker] No UI font available on device");
            return _cached;
        }

        /// <summary>Плоский белый спрайт (fallback). Для UI используйте UiTheme.ApplyRounded/Pill.</summary>
        public static Sprite WhiteSprite()
        {
            if (_whiteSprite != null) return _whiteSprite;
            var tex = Texture2D.whiteTexture;
            _whiteSprite = Sprite.Create(
                tex,
                new Rect(0f, 0f, tex.width, tex.height),
                new Vector2(0.5f, 0.5f),
                100f);
            _whiteSprite.name = "PokerUIWhite";
            return _whiteSprite;
        }

        public static void PrepImage(Image img)
        {
            if (img == null) return;
            // По умолчанию — скруглённая панель, не квадрат 1×1.
            UiTheme.ApplyRounded(img);
        }

        public static void MakeCrisp(Text text, float outline = 0.35f)
        {
            if (text == null) return;
            var font = Builtin();
            if (font != null)
                text.font = font;
            text.alignByGeometry = true;
            text.resizeTextForBestFit = false;
            text.lineSpacing = 1.05f;

            var existing = text.GetComponent<Outline>();
            if (existing == null)
                existing = text.gameObject.AddComponent<Outline>();
            existing.effectColor = new Color(0f, 0f, 0f, 0.4f);
            existing.effectDistance = new Vector2(Mathf.Max(0.6f, outline * 0.7f), -Mathf.Max(0.6f, outline * 0.7f));
            existing.useGraphicAlpha = true;
        }
    }
}
