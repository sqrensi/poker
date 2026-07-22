using UnityEngine;
using UnityEngine.UI;
using Poker.Identity;
using Poker.Presentation;

namespace Poker.Menu
{
    /// <summary>Ник в стиле glass pill (низ экрана по центру).</summary>
    public sealed class MainMenuNicknameEditor : MonoBehaviour
    {
        InputField _input;
        Text _status;
        Button _actionBtn;
        Text _actionLabel;
        Image _actionImg;
        bool _editing;
        System.Action _onChanged;

        public static MainMenuNicknameEditor Create(Transform canvas, System.Action onChanged = null,
            float bottomInset = -1f)
        {
            var go = new GameObject("NicknameEditor");
            go.transform.SetParent(canvas, false);
            var editor = go.AddComponent<MainMenuNicknameEditor>();
            editor._onChanged = onChanged;
            editor._bottomInset = bottomInset;
            editor.Build();
            return editor;
        }

        float _bottomInset = -1f;

        void Build()
        {
            bool phone = MobileLayout.IsPhoneLike();
            var root = gameObject.AddComponent<RectTransform>();
            root.anchorMin = new Vector2(0.5f, 0f);
            root.anchorMax = new Vector2(0.5f, 0f);
            root.pivot = new Vector2(0.5f, 0f);
            root.anchoredPosition = new Vector2(0f, _bottomInset >= 0f ? _bottomInset : (phone ? 22f : 28f));
            root.sizeDelta = new Vector2(phone ? 520f : 480f, phone ? 110f : 108f);

            var bg = gameObject.AddComponent<Image>();
            bg.color = UiTheme.Glass;
            bg.raycastTarget = true;
            UiTheme.ApplyRounded(bg);
            var edge = gameObject.AddComponent<Outline>();
            edge.effectColor = UiTheme.GlassBorder;
            edge.effectDistance = new Vector2(1f, -1f);

            var title = CreateLabel(transform, "ПРОФИЛЬ", new Vector2(0f, -10f), new Vector2(200f, 22f), 13,
                FontStyle.Bold, UiTheme.Cyan);
            title.alignment = TextAnchor.MiddleCenter;
            var titleRt = title.rectTransform;
            titleRt.anchorMin = titleRt.anchorMax = new Vector2(0.5f, 1f);
            titleRt.pivot = new Vector2(0.5f, 1f);

            // Pill input
            const float cardPadX = 14f;
            const float textPadX = 22f;
            const float btnW = 118f;
            const float btnGap = 10f;

            var fieldGo = new GameObject("NickInput");
            fieldGo.transform.SetParent(transform, false);
            var fieldRt = fieldGo.AddComponent<RectTransform>();
            fieldRt.anchorMin = new Vector2(0f, 0.5f);
            fieldRt.anchorMax = new Vector2(1f, 0.5f);
            fieldRt.pivot = new Vector2(0.5f, 0.5f);
            fieldRt.anchoredPosition = new Vector2(0f, -4f);
            fieldRt.offsetMin = new Vector2(cardPadX, -27f);
            fieldRt.offsetMax = new Vector2(-(btnW + btnGap + cardPadX), 19f);
            var fieldImg = fieldGo.AddComponent<Image>();
            fieldImg.color = new Color(0f, 0f, 0f, 0.35f);
            UiTheme.ApplyRounded(fieldImg);

            var textGo = new GameObject("Text");
            textGo.transform.SetParent(fieldGo.transform, false);
            var textRt = textGo.AddComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = new Vector2(textPadX, 6f);
            textRt.offsetMax = new Vector2(-textPadX, -6f);
            var text = textGo.AddComponent<Text>();
            text.fontSize = 20;
            text.color = UiTheme.TextMain;
            text.alignment = TextAnchor.MiddleLeft;
            text.supportRichText = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            UiFont.MakeCrisp(text, 0.25f);

            var phGo = new GameObject("Placeholder");
            phGo.transform.SetParent(fieldGo.transform, false);
            var phRt = phGo.AddComponent<RectTransform>();
            phRt.anchorMin = Vector2.zero;
            phRt.anchorMax = Vector2.one;
            phRt.offsetMin = new Vector2(textPadX, 6f);
            phRt.offsetMax = new Vector2(-textPadX, -6f);
            var ph = phGo.AddComponent<Text>();
            ph.fontSize = 18;
            ph.fontStyle = FontStyle.Italic;
            ph.color = UiTheme.TextDim;
            ph.text = "Ваш ник";
            ph.alignment = TextAnchor.MiddleLeft;
            UiFont.MakeCrisp(ph, 0.2f);

            _input = fieldGo.AddComponent<InputField>();
            _input.textComponent = text;
            _input.placeholder = ph;
            _input.characterLimit = 16;
            _input.contentType = InputField.ContentType.Standard;
            _input.lineType = InputField.LineType.SingleLine;

            // Action pill
            var btnGo = new GameObject("Action");
            btnGo.transform.SetParent(transform, false);
            var btnRt = btnGo.AddComponent<RectTransform>();
            btnRt.anchorMin = new Vector2(1f, 0.5f);
            btnRt.anchorMax = new Vector2(1f, 0.5f);
            btnRt.pivot = new Vector2(1f, 0.5f);
            btnRt.anchoredPosition = new Vector2(-cardPadX, -4f);
            btnRt.sizeDelta = new Vector2(btnW, 46f);
            _actionImg = btnGo.AddComponent<Image>();
            _actionImg.color = UiTheme.GlassStrong;
            UiTheme.ApplyRounded(_actionImg);
            _actionBtn = btnGo.AddComponent<Button>();
            _actionBtn.targetGraphic = _actionImg;
            UiPressAnimation.Attach(_actionBtn);
            _actionBtn.onClick.AddListener(PokerSoundFx.WithButton(OnAction));

            var lblGo = new GameObject("Label");
            lblGo.transform.SetParent(btnGo.transform, false);
            var lblRt = lblGo.AddComponent<RectTransform>();
            lblRt.anchorMin = Vector2.zero;
            lblRt.anchorMax = Vector2.one;
            lblRt.offsetMin = lblRt.offsetMax = Vector2.zero;
            _actionLabel = lblGo.AddComponent<Text>();
            _actionLabel.fontSize = 16;
            _actionLabel.fontStyle = FontStyle.Bold;
            _actionLabel.alignment = TextAnchor.MiddleCenter;
            _actionLabel.color = UiTheme.TextMain;
            UiFont.MakeCrisp(_actionLabel, 0.3f);

            _status = CreateLabel(transform, "", Vector2.zero, Vector2.zero, 13,
                FontStyle.Normal, UiTheme.TextDim);
            _status.alignment = TextAnchor.MiddleCenter;
            _status.horizontalOverflow = HorizontalWrapMode.Overflow;
            _status.verticalOverflow = VerticalWrapMode.Truncate;
            var stRt = _status.rectTransform;
            stRt.anchorMin = new Vector2(0f, 0f);
            stRt.anchorMax = new Vector2(1f, 0f);
            stRt.pivot = new Vector2(0.5f, 0f);
            stRt.offsetMin = new Vector2(cardPadX, 10f);
            stRt.offsetMax = new Vector2(-cardPadX, 30f);

            RefreshFromPrefs();
            SetEditing(false);
        }

        void RefreshFromPrefs()
        {
            if (_input != null)
                _input.text = PlayerIdentityService.GetNickname();
        }

        void SetEditing(bool editing)
        {
            _editing = editing;
            if (_input != null)
            {
                _input.interactable = editing;
                if (editing)
                {
                    _input.Select();
                    _input.ActivateInputField();
                }
            }
            if (_actionLabel != null)
                _actionLabel.text = editing ? "OK" : "Изменить";
            if (_actionImg != null)
                _actionImg.color = editing ? UiTheme.Coral : UiTheme.GlassStrong;
        }

        void OnAction()
        {
            if (!_editing)
            {
                SetEditing(true);
                if (_status != null)
                {
                    _status.text = "3–16 символов";
                    _status.color = UiTheme.TextDim;
                }
                return;
            }

            if (!PlayerIdentityService.TrySetNickname(_input.text, out string err))
            {
                if (_status != null)
                {
                    _status.text = err ?? "Ошибка";
                    _status.color = UiTheme.Danger;
                }
                return;
            }

            RefreshFromPrefs();
            SetEditing(false);
            if (_status != null)
            {
                _status.text = "Сохранено";
                _status.color = UiTheme.Cyan;
            }
            _onChanged?.Invoke();
        }

        static Text CreateLabel(Transform parent, string content, Vector2 pos, Vector2 size,
            int fontSize, FontStyle style, Color color)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            var t = go.AddComponent<Text>();
            t.text = content;
            t.fontSize = fontSize;
            t.fontStyle = style;
            t.color = color;
            t.raycastTarget = false;
            UiFont.MakeCrisp(t, 0.25f);
            return t;
        }
    }
}
