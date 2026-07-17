using UnityEngine;
using UnityEngine.UI;
using Poker.Identity;
using Poker.Presentation;

namespace Poker.Menu
{
    /// <summary>
    /// Смена ника в главном меню (упрощённый аналог ShooterPrototype MainMenuNicknameEditor).
    /// Режим просмотра → «Изменить» → поле активно → «Сохранить».
    /// </summary>
    public sealed class MainMenuNicknameEditor : MonoBehaviour
    {
        InputField _input;
        Text _status;
        Text _idHint;
        Button _actionBtn;
        Text _actionLabel;
        bool _editing;
        System.Action _onChanged;

        public static MainMenuNicknameEditor Create(Transform canvas, System.Action onChanged = null)
        {
            var go = new GameObject("NicknameEditor");
            go.transform.SetParent(canvas, false);
            var editor = go.AddComponent<MainMenuNicknameEditor>();
            editor._onChanged = onChanged;
            editor.Build();
            return editor;
        }

        void Build()
        {
            var root = gameObject.AddComponent<RectTransform>();
            root.anchorMin = new Vector2(1f, 0f);
            root.anchorMax = new Vector2(1f, 0f);
            root.pivot = new Vector2(1f, 0f);
            root.anchoredPosition = new Vector2(-20f, 20f);
            root.sizeDelta = new Vector2(MobileLayout.IsPhoneLike() ? 320f : 360f, 150f);

            var bg = gameObject.AddComponent<Image>();
            bg.color = new Color(0.08f, 0.11f, 0.1f, 0.92f);

            var title = CreateLabel(transform, "Ник", new Vector2(16f, -12f), new Vector2(200f, 24f), 16,
                FontStyle.Bold, new Color(0.85f, 0.78f, 0.45f));
            title.alignment = TextAnchor.UpperLeft;

            // Input
            var fieldGo = new GameObject("NickInput");
            fieldGo.transform.SetParent(transform, false);
            var fieldRt = fieldGo.AddComponent<RectTransform>();
            fieldRt.anchorMin = new Vector2(0f, 1f);
            fieldRt.anchorMax = new Vector2(1f, 1f);
            fieldRt.pivot = new Vector2(0.5f, 1f);
            fieldRt.anchoredPosition = new Vector2(-58f, -42f);
            fieldRt.sizeDelta = new Vector2(-132f, 44f);
            var fieldImg = fieldGo.AddComponent<Image>();
            fieldImg.color = new Color(0.05f, 0.07f, 0.07f, 1f);

            var textGo = new GameObject("Text");
            textGo.transform.SetParent(fieldGo.transform, false);
            var textRt = textGo.AddComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = new Vector2(12f, 6f);
            textRt.offsetMax = new Vector2(-12f, -6f);
            var text = textGo.AddComponent<Text>();
            text.font = UiFont.Builtin();
            text.fontSize = 20;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleLeft;
            text.supportRichText = false;
            UiFont.MakeCrisp(text, 0.3f);

            var phGo = new GameObject("Placeholder");
            phGo.transform.SetParent(fieldGo.transform, false);
            var phRt = phGo.AddComponent<RectTransform>();
            phRt.anchorMin = Vector2.zero;
            phRt.anchorMax = Vector2.one;
            phRt.offsetMin = new Vector2(12f, 6f);
            phRt.offsetMax = new Vector2(-12f, -6f);
            var ph = phGo.AddComponent<Text>();
            ph.font = UiFont.Builtin();
            ph.fontSize = 18;
            ph.fontStyle = FontStyle.Italic;
            ph.color = new Color(1f, 1f, 1f, 0.35f);
            ph.text = "Ваш ник";
            ph.alignment = TextAnchor.MiddleLeft;

            _input = fieldGo.AddComponent<InputField>();
            _input.textComponent = text;
            _input.placeholder = ph;
            _input.characterLimit = 16;
            _input.contentType = InputField.ContentType.Standard;
            _input.lineType = InputField.LineType.SingleLine;

            // Action button
            var btnGo = new GameObject("Action");
            btnGo.transform.SetParent(transform, false);
            var btnRt = btnGo.AddComponent<RectTransform>();
            btnRt.anchorMin = new Vector2(1f, 1f);
            btnRt.anchorMax = new Vector2(1f, 1f);
            btnRt.pivot = new Vector2(1f, 1f);
            btnRt.anchoredPosition = new Vector2(-12f, -42f);
            btnRt.sizeDelta = new Vector2(100f, 44f);
            var btnImg = btnGo.AddComponent<Image>();
            btnImg.color = new Color(0.2f, 0.35f, 0.28f, 1f);
            _actionBtn = btnGo.AddComponent<Button>();
            _actionBtn.targetGraphic = btnImg;
            _actionBtn.onClick.AddListener(OnAction);

            var lblGo = new GameObject("Label");
            lblGo.transform.SetParent(btnGo.transform, false);
            var lblRt = lblGo.AddComponent<RectTransform>();
            lblRt.anchorMin = Vector2.zero;
            lblRt.anchorMax = Vector2.one;
            lblRt.offsetMin = Vector2.zero;
            lblRt.offsetMax = Vector2.zero;
            _actionLabel = lblGo.AddComponent<Text>();
            _actionLabel.font = UiFont.Builtin();
            _actionLabel.fontSize = 16;
            _actionLabel.fontStyle = FontStyle.Bold;
            _actionLabel.alignment = TextAnchor.MiddleCenter;
            _actionLabel.color = Color.white;
            UiFont.MakeCrisp(_actionLabel, 0.35f);

            _status = CreateLabel(transform, "", new Vector2(16f, -96f), new Vector2(328f, 22f), 14,
                FontStyle.Normal, new Color(0.75f, 0.85f, 0.7f));
            _status.alignment = TextAnchor.UpperLeft;

            string pid = PlayerIdentityService.GetOrCreatePlayerId();
            _idHint = CreateLabel(transform,
                $"id …{pid.Substring(Mathf.Max(0, pid.Length - 8))}",
                new Vector2(16f, -118f), new Vector2(328f, 20f), 13,
                FontStyle.Normal, new Color(0.55f, 0.62f, 0.58f));
            _idHint.alignment = TextAnchor.UpperLeft;

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
                _actionLabel.text = editing ? "Сохранить" : "Изменить";
            if (_actionBtn != null)
            {
                var img = _actionBtn.targetGraphic as Image;
                if (img != null)
                    img.color = editing
                        ? new Color(0.55f, 0.42f, 0.12f, 1f)
                        : new Color(0.2f, 0.35f, 0.28f, 1f);
            }
        }

        void OnAction()
        {
            if (!_editing)
            {
                SetEditing(true);
                if (_status != null) _status.text = "Введите ник (3–16) и сохраните";
                return;
            }

            if (!PlayerIdentityService.TrySetNickname(_input.text, out string err))
            {
                if (_status != null)
                {
                    _status.text = err ?? "Не удалось сохранить";
                    _status.color = new Color(0.95f, 0.55f, 0.45f);
                }
                return;
            }

            RefreshFromPrefs();
            SetEditing(false);
            if (_status != null)
            {
                _status.text = "Ник сохранён";
                _status.color = new Color(0.75f, 0.85f, 0.7f);
            }
            _onChanged?.Invoke();
        }

        static Text CreateLabel(Transform parent, string content, Vector2 pos, Vector2 size,
            int fontSize, FontStyle style, Color color)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            var t = go.AddComponent<Text>();
            t.font = UiFont.Builtin();
            t.text = content;
            t.fontSize = fontSize;
            t.fontStyle = style;
            t.color = color;
            t.raycastTarget = false;
            UiFont.MakeCrisp(t, 0.3f);
            return t;
        }
    }
}
