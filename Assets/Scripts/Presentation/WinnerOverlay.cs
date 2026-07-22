using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using Poker.Game;

namespace Poker.Presentation
{
    /// <summary>Полноэкранный баннер победителя ~3 сек с плавным появлением/исчезновением.</summary>
    public sealed class WinnerOverlay : MonoBehaviour
    {
        const float Duration = 3f;
        const float FadeIn = 0.35f;
        const float FadeOut = 0.45f;

        CanvasGroup _group;
        RectTransform _panelRt;
        Image _backdrop;
        Image _panel;
        Text _title;
        Text _subtitle;
        Coroutine _anim;
        int _shownHand = -1;

        public static WinnerOverlay Create(Transform canvasParent)
        {
            var root = new GameObject("WinnerOverlay");
            root.transform.SetParent(canvasParent, false);
            var rt = root.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var overlay = root.AddComponent<WinnerOverlay>();
            overlay.Build();
            root.SetActive(false);
            return overlay;
        }

        void Build()
        {
            _group = gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 0f;
            _group.blocksRaycasts = false;
            _group.interactable = false;

            var bdGo = new GameObject("Backdrop");
            bdGo.transform.SetParent(transform, false);
            var bdRt = bdGo.AddComponent<RectTransform>();
            bdRt.anchorMin = Vector2.zero;
            bdRt.anchorMax = Vector2.one;
            bdRt.offsetMin = Vector2.zero;
            bdRt.offsetMax = Vector2.zero;
            _backdrop = bdGo.AddComponent<Image>();
            _backdrop.color = new Color(0.04f, 0.05f, 0.09f, 0.72f);
            _backdrop.raycastTarget = false;
            _backdrop.sprite = UiFont.WhiteSprite();
            _backdrop.type = Image.Type.Simple;

            // Не перехватывать клики — иначе «Новая партия» / «В меню» не нажимаются.

            var panelGo = new GameObject("Panel");
            panelGo.transform.SetParent(transform, false);
            _panelRt = panelGo.AddComponent<RectTransform>();
            _panelRt.anchorMin = new Vector2(0.5f, 0.5f);
            _panelRt.anchorMax = new Vector2(0.5f, 0.5f);
            _panelRt.pivot = new Vector2(0.5f, 0.5f);
            _panelRt.sizeDelta = new Vector2(680f, 210f);
            _panel = panelGo.AddComponent<Image>();
            _panel.color = UiTheme.GlassStrong;
            _panel.raycastTarget = false;
            UiTheme.ApplyRounded(_panel);
            var edge = panelGo.AddComponent<Outline>();
            edge.effectColor = new Color(UiTheme.Cyan.r, UiTheme.Cyan.g, UiTheme.Cyan.b, 0.45f);
            edge.effectDistance = new Vector2(1.5f, -1.5f);

            var accent = new GameObject("Accent");
            accent.transform.SetParent(panelGo.transform, false);
            var aRt = accent.AddComponent<RectTransform>();
            aRt.anchorMin = new Vector2(0.5f, 1f);
            aRt.anchorMax = new Vector2(0.5f, 1f);
            aRt.pivot = new Vector2(0.5f, 1f);
            aRt.sizeDelta = new Vector2(120f, 6f);
            aRt.anchoredPosition = new Vector2(0f, -16f);
            var aImg = accent.AddComponent<Image>();
            aImg.color = UiTheme.Coral;
            aImg.raycastTarget = false;
            UiTheme.ApplyRoundedSmall(aImg);

            _title = CreateCenteredText(panelGo.transform, "Title", new Vector2(0f, 22f), 42, FontStyle.Bold,
                UiTheme.TextMain);
            _subtitle = CreateCenteredText(panelGo.transform, "Subtitle", new Vector2(0f, -42f), 24, FontStyle.Normal,
                UiTheme.Cyan);
        }

        static Text CreateCenteredText(Transform parent, string name, Vector2 pos, int size, FontStyle style, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(680f, 70f);
            var text = go.AddComponent<Text>();
            text.fontSize = size;
            text.fontStyle = style;
            text.color = color;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            text.text = "";
            UiFont.MakeCrisp(text, 0.45f);
            return text;
        }

        public void Show(PokerTable table)
        {
            if (table == null || table.LastResult == null) return;
            if (table.IsMatchOver) return; // матч перекрывает баннер раздачи
            if (table.HandNumber == _shownHand) return;
            _shownHand = table.HandNumber;

            FormatResult(table, out string title, out string subtitle);
            _title.text = title;
            _subtitle.text = subtitle;

            if (_anim != null)
                StopCoroutine(_anim);
            gameObject.SetActive(true);
            _anim = StartCoroutine(Animate(autoHide: true));
        }

        public void ShowMatchEnd(PokerTable table)
        {
            if (table == null || !table.IsMatchOver) return;
            _shownHand = -1;

            if (table.MatchWinnerSeat >= 0 && table.MatchWinnerSeat < table.Players.Count)
            {
                var w = table.Players[table.MatchWinnerSeat];
                _title.text = w.Type == PlayerType.Human ? "Вы выиграли матч!" : $"{w.Name} — чемпион!";
                _subtitle.text = $"Стек победителя: {w.Chips}";
            }
            else
            {
                _title.text = "Матч окончен";
                _subtitle.text = table.LastActionLog ?? "";
            }

            if (_anim != null)
                StopCoroutine(_anim);
            gameObject.SetActive(true);
            _anim = StartCoroutine(Animate(autoHide: false));
        }

        public void Hide()
        {
            if (_anim != null)
                StopCoroutine(_anim);
            _anim = null;
            _group.alpha = 0f;
            gameObject.SetActive(false);
        }

        static void FormatResult(PokerTable table, out string title, out string subtitle)
        {
            var result = table.LastResult;
            if (result.Pots.Count == 0)
            {
                title = "Раздача окончена";
                subtitle = "";
                return;
            }

            // Собираем итог по всем банкам
            var wonChips = new Dictionary<int, int>();
            string handDesc = null;
            foreach (var pot in result.Pots)
            {
                if (pot.WinnerSeats.Count == 0) continue;
                int share = pot.Amount / pot.WinnerSeats.Count;
                int rem = pot.Amount % pot.WinnerSeats.Count;
                for (int i = 0; i < pot.WinnerSeats.Count; i++)
                {
                    int seat = pot.WinnerSeats[i];
                    int gain = share + (i < rem ? 1 : 0);
                    wonChips.TryGetValue(seat, out int prev);
                    wonChips[seat] = prev + gain;
                }
                if (handDesc == null && !string.IsNullOrEmpty(pot.HandDescription))
                    handDesc = pot.HandDescription;
            }

            if (wonChips.Count == 0)
            {
                title = "Раздача окончена";
                subtitle = "";
                return;
            }

            if (wonChips.Count == 1)
            {
                foreach (var kv in wonChips)
                {
                    var p = table.Players[kv.Key];
                    title = p.Type == PlayerType.Human ? "Вы выиграли!" : $"{p.Name} побеждает!";
                    var sb = new StringBuilder();
                    sb.Append($"+{kv.Value} фишек");
                    if (!string.IsNullOrEmpty(handDesc) && handDesc != "Без вскрытия")
                        sb.Append("  ·  ").Append(handDesc);
                    else if (handDesc == "Без вскрытия")
                        sb.Append("  ·  соперники сбросили");
                    subtitle = sb.ToString();
                    return;
                }
            }

            // Сплит
            var names = new List<string>();
            foreach (var kv in wonChips)
                names.Add($"{table.Players[kv.Key].Name} (+{kv.Value})");
            title = "Ничья — банк разделён";
            subtitle = string.Join(", ", names);
            if (!string.IsNullOrEmpty(handDesc))
                subtitle += "  ·  " + handDesc;
        }

        IEnumerator Animate(bool autoHide)
        {
            _group.alpha = 0f;
            // Всегда пропускаем клики к кнопкам под баннером.
            _group.blocksRaycasts = false;
            _group.interactable = false;
            _panelRt.localScale = Vector3.one * 0.82f;

            float t = 0f;
            while (t < FadeIn)
            {
                t += Time.deltaTime;
                float u = Mathf.Clamp01(t / FadeIn);
                float e = EaseOutBack(u);
                _group.alpha = u;
                _panelRt.localScale = Vector3.LerpUnclamped(Vector3.one * 0.82f, Vector3.one, e);
                yield return null;
            }
            _group.alpha = 1f;
            _panelRt.localScale = Vector3.one;

            if (!autoHide)
            {
                _anim = null;
                yield break;
            }

            float hold = Duration - FadeIn - FadeOut;
            if (hold > 0f)
                yield return new WaitForSeconds(hold);

            t = 0f;
            while (t < FadeOut)
            {
                t += Time.deltaTime;
                float u = Mathf.Clamp01(t / FadeOut);
                _group.alpha = 1f - u;
                _panelRt.localScale = Vector3.Lerp(Vector3.one, Vector3.one * 0.94f, u);
                yield return null;
            }

            _group.alpha = 0f;
            gameObject.SetActive(false);
            _anim = null;
        }

        static float EaseOutBack(float x)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            return 1f + c3 * Mathf.Pow(x - 1f, 3f) + c1 * Mathf.Pow(x - 1f, 2f);
        }
    }
}
