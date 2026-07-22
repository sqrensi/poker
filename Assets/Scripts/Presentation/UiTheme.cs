using UnityEngine;
using UnityEngine.UI;

namespace Poker.Presentation
{
    /// <summary>
    /// Glass Night UI — прямоугольники с мягким скруглением (не «pill»/ромбы).
    /// </summary>
    public static class UiTheme
    {
        public static readonly Color Bg = Hex(0x0B0E17);
        public static readonly Color OrbViolet = new Color(0.48f, 0.42f, 1f, 0.22f);
        public static readonly Color OrbCyan = new Color(0.18f, 0.9f, 0.84f, 0.16f);
        public static readonly Color OrbCoral = new Color(1f, 0.42f, 0.29f, 0.12f);
        public static readonly Color Glass = new Color(1f, 1f, 1f, 0.09f);
        public static readonly Color GlassStrong = new Color(1f, 1f, 1f, 0.13f);
        public static readonly Color GlassBorder = new Color(1f, 1f, 1f, 0.18f);
        public static readonly Color TextMain = Color.white;
        public static readonly Color TextDim = new Color(1f, 1f, 1f, 0.52f);
        public static readonly Color TextSoft = new Color(0.82f, 0.84f, 0.92f, 0.88f);
        public static readonly Color Cyan = Hex(0x2EE6D6);
        public static readonly Color Violet = Hex(0x7B6CFF);
        public static readonly Color Coral = Hex(0xFF6B4A);
        public static readonly Color CoralHot = Hex(0xFF8F5C);
        public static readonly Color Danger = Hex(0xFF5E6C);
        public static readonly Color Success = Hex(0x3DDB9A);
        public static readonly Color RaiseBlue = Hex(0x5B8CFF);
        public static readonly Color Acting = new Color(0.18f, 0.9f, 0.84f, 0.28f);
        public static readonly Color RowIdle = new Color(1f, 1f, 1f, 0.06f);
        public static readonly Color Folded = new Color(1f, 1f, 1f, 0.04f);

        // ~22px скругление — border ≤ половины высоты мелких элементов (44px+)
        const int TexSize = 128;
        const int Corner = 22;
        const int Border = 24;
        const int SmallCorner = 10;
        const int SmallBorder = 12;
        const float SpritePpu = 100f;
        const float EdgeSoftness = 1.25f;

        static Sprite _rounded;
        static Sprite _roundedSmall;
        static Sprite _circle;

        static float SmoothAlpha(float dist, float radius)
        {
            return Mathf.Clamp01(Mathf.SmoothStep(1f, 0f, (dist - radius + EdgeSoftness) / EdgeSoftness));
        }

        public static Sprite RoundedSprite()
        {
            if (_rounded != null) return _rounded;
            _rounded = MakeSliced(Corner, Border, "PokerUIRounded");
            return _rounded;
        }

        public static Sprite RoundedSmallSprite()
        {
            if (_roundedSmall != null) return _roundedSmall;
            _roundedSmall = MakeSliced(SmallCorner, SmallBorder, "PokerUIRoundedSmall");
            return _roundedSmall;
        }

        public static Sprite CircleSprite()
        {
            if (_circle != null) return _circle;
            var tex = new Texture2D(TexSize, TexSize, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            tex.anisoLevel = 2;
            tex.wrapMode = TextureWrapMode.Clamp;
            float c = (TexSize - 1) * 0.5f;
            float r = c - 1.5f;
            for (int y = 0; y < TexSize; y++)
            for (int x = 0; x < TexSize; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), new Vector2(c, c));
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, SmoothAlpha(d, r)));
            }
            tex.Apply(false, true);
            _circle = Sprite.Create(tex, new Rect(0, 0, TexSize, TexSize), new Vector2(0.5f, 0.5f), SpritePpu);
            _circle.name = "PokerUICircle";
            return _circle;
        }

        static Sprite MakeSliced(int radius, int border, string name)
        {
            var tex = new Texture2D(TexSize, TexSize, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            tex.anisoLevel = 2;
            tex.wrapMode = TextureWrapMode.Clamp;
            float r = radius;
            for (int y = 0; y < TexSize; y++)
            for (int x = 0; x < TexSize; x++)
            {
                float ax = Mathf.Min(x, TexSize - 1 - x);
                float ay = Mathf.Min(y, TexSize - 1 - y);
                float a = 1f;
                if (ax < r && ay < r)
                {
                    float dx = r - ax;
                    float dy = r - ay;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    a = SmoothAlpha(d, r);
                }
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
            tex.Apply(false, true);
            var b = border;
            var sp = Sprite.Create(
                tex,
                new Rect(0, 0, TexSize, TexSize),
                new Vector2(0.5f, 0.5f),
                SpritePpu,
                0,
                SpriteMeshType.FullRect,
                new Vector4(b, b, b, b));
            sp.name = name;
            return sp;
        }

        public static void ApplyRounded(Image img)
        {
            if (img == null) return;
            img.sprite = RoundedSprite();
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = 1f;
            img.preserveAspect = false;
            img.material = null;
        }

        /// <summary>Тонкие полоски (прогресс, слайдер) — малый радиус, без ромба.</summary>
        public static void ApplyRoundedSmall(Image img)
        {
            if (img == null) return;
            img.sprite = RoundedSmallSprite();
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = 1f;
            img.preserveAspect = false;
            img.material = null;
        }

        /// <summary>Тот же мягкий прямоугольник (pill больше не используем — даёт ромбы).</summary>
        public static void ApplyPill(Image img) => ApplyRounded(img);

        public static void ApplyCircle(Image img)
        {
            if (img == null) return;
            img.sprite = CircleSprite();
            img.type = Image.Type.Simple;
            img.pixelsPerUnitMultiplier = 1f;
            img.preserveAspect = true;
            img.material = null;
        }

        public static void StyleLabel(Text text, bool softOutline = true)
        {
            if (text == null) return;
            UiFont.MakeCrisp(text, softOutline ? 0.25f : 0.15f);
            var outline = text.GetComponent<Outline>();
            if (outline != null)
            {
                outline.effectColor = new Color(0f, 0f, 0f, 0.35f);
                outline.effectDistance = new Vector2(0.8f, -0.8f);
            }
            var shadow = text.GetComponent<Shadow>();
            if (shadow == null) shadow = text.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.4f);
            shadow.effectDistance = new Vector2(0f, -1.5f);
        }

        public static Image MakeOrb(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var img = go.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            ApplyCircle(img);
            return img;
        }

        public static Color Hex(int hex)
        {
            float r = ((hex >> 16) & 0xFF) / 255f;
            float g = ((hex >> 8) & 0xFF) / 255f;
            float b = (hex & 0xFF) / 255f;
            return new Color(r, g, b, 1f);
        }

        public static void WarmUp()
        {
            _rounded = null;
            _roundedSmall = null;
            _circle = null;
            RoundedSprite();
            RoundedSmallSprite();
            CircleSprite();
            UiFont.Builtin();
        }
    }
}
