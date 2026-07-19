using UnityEngine;

namespace Poker.Presentation
{
    /// <summary>
    /// Материалы для стола/фишек.
    /// Важно: в Editor/Simulator Shader.Find("Unlit/Color") работает всегда.
    /// В APK неиспользуемые шейдеры вырезаются — без Resources-материала стол = magenta.
    /// </summary>
    public static class PokerMaterials
    {
        static Material _template;
        static Shader _shader;
        static bool _ready;
        static string _lastError;

        public static bool IsReady => _ready && _shader != null;
        public static string LastError => _lastError;

        /// <summary>Прогрев до CreatePrimitive. null = ок, иначе текст ошибки.</summary>
        public static string WarmUp()
        {
            EnsureReady();
            if (_shader == null)
            {
                _lastError = "Нет шейдера (Resources/Poker/UnlitColor). Пересобери APK.";
                return _lastError;
            }
            if (_shader.name.Contains("InternalError") || _shader.name.Contains("Error"))
            {
                _lastError = "Шейдер Error/Internal — в билде вырезан Unlit.";
                return _lastError;
            }
            _lastError = null;
            return null;
        }

        public static Material ColorMat(Color color)
        {
            EnsureReady();

            Material mat;
            if (_template != null)
                mat = new Material(_template);
            else if (_shader != null)
                mat = new Material(_shader);
            else
            {
                var fb = Shader.Find("Sprites/Default") ?? Shader.Find("UI/Default");
                if (fb == null)
                {
                    Debug.LogError("[Poker] No usable color shader on device");
                    _lastError = "null shader";
                    return null;
                }
                mat = new Material(fb);
            }

            if (mat.HasProperty("_Color"))
                mat.SetColor("_Color", color);
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", color);
            mat.color = color;
            return mat;
        }

        /// <summary>Сразу заменить дефолтный материал примитива (он розовый в APK).</summary>
        public static void ApplyColor(Renderer renderer, Color color)
        {
            if (renderer == null) return;
            var mat = ColorMat(color);
            if (mat != null)
                renderer.sharedMaterial = mat;
        }

        static void EnsureReady()
        {
            if (_ready) return;
            _ready = true;

            _template = Resources.Load<Material>("Poker/UnlitColor");
            if (_template != null && _template.shader != null)
                _shader = _template.shader;

            if (_shader == null)
                _shader = Shader.Find("Poker/UnlitColor");
            if (_shader == null)
                _shader = Shader.Find("Sprites/Default");
            if (_shader == null)
                _shader = Shader.Find("UI/Default");
            if (_shader == null)
                _shader = Shader.Find("Mobile/Particles/Alpha Blended");
            if (_shader == null)
                _shader = Shader.Find("Unlit/Color");

            string name = _shader != null ? _shader.name : "NULL";
            Debug.Log($"[Poker] Color material warm-up: shader={name}, template={_template != null}");
        }
    }
}
