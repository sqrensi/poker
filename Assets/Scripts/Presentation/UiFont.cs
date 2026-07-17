using UnityEngine;
using UnityEngine.UI;

namespace Poker.Presentation
{
    public static class UiFont
    {
        static Font _cached;

        public static Font Builtin()
        {
            if (_cached != null) return _cached;
            _cached = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (_cached == null)
                _cached = Resources.GetBuiltinResource<Font>("Arial.ttf");
            return _cached;
        }

        /// <summary>
        /// Sharper legacy UI text: geometry alignment + soft outline for edge clarity.
        /// </summary>
        public static void MakeCrisp(Text text, float outline = 0.35f)
        {
            if (text == null) return;
            text.font = Builtin();
            text.alignByGeometry = true;
            text.resizeTextForBestFit = false;

            var existing = text.GetComponent<Outline>();
            if (existing == null)
                existing = text.gameObject.AddComponent<Outline>();
            existing.effectColor = new Color(0f, 0f, 0f, 0.55f);
            existing.effectDistance = new Vector2(outline, -outline);
            existing.useGraphicAlpha = true;
        }
    }
}
