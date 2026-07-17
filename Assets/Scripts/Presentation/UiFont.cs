using UnityEngine;

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
    }
}
