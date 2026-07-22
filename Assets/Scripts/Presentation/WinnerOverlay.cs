using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using Poker.Game;
using Poker.Network;

namespace Poker.Presentation
{
    /// <summary>Полноэкранный баннер победителя: 3 сек на экране + плавные анимации.</summary>
    public sealed class WinnerOverlay : MonoBehaviour
    {
        const float HoldDuration = 3f;
        const float FadeIn = 0.45f;
        const float FadeOut = 0.55f;
        const float BackdropMaxAlpha = 0.78f;

        CanvasGroup _group;
        RectTransform _panelRt;
        Image _backdrop;
        Image _panel;
        Text _title;
        Text _subtitle;
        Coroutine _anim;
        int _shownHand = -1;

        public event Action Finished;

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

        public bool ShowOnline(OnlineGameState state)
        {
            if (state == null || state.Street != "handComplete") return false;
            if (state.HandNumber == _shownHand) return false;
            _shownHand = state.HandNumber;

            FormatOnlineResult(state, out string title, out string subtitle);
            _title.text = title;
            _subtitle.text = subtitle;

            if (_anim != null)
                StopCoroutine(_anim);
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            _anim = StartCoroutine(Animate(autoHide: true));
            return true;
        }

        public bool Show(PokerTable table)
        {
            if (table == null || table.LastResult == null) return false;
            if (table.IsMatchOver) return false;
            if (table.HandNumber == _shownHand) return false;
            _shownHand = table.HandNumber;

            FormatResult(table, out string title, out string subtitle);
            _title.text = title;
            _subtitle.text = subtitle;

            if (_anim != null)
                StopCoroutine(_anim);
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            _anim = StartCoroutine(Animate(autoHide: true));
            return true;
        }

        public void ShowMatchEndOnline(OnlineGameState state)
        {
            if (state == null || state.Street != "matchComplete") return;
            _shownHand = -1;

            if (state.MatchWinner >= 0)
            {
                OnlineSeatPlayer winner = null;
                foreach (var p in state.Players)
                {
                    if (p.Seat == state.MatchWinner) { winner = p; break; }
                }
                if (winner != null)
                {
                    bool iWon = winner.Id == state.YouId;
                    _title.text = iWon ? "Вы выиграли матч!" : $"Победитель: {winner.Name}";
                    _subtitle.text = iWon
                        ? $"Стек: {winner.Chips}"
                        : $"Вы проиграли матч  ·  стек {winner.Name}: {winner.Chips}";
                }
                else
                {
                    _title.text = "Матч окончен";
                    _subtitle.text = state.LastLog ?? "";
                }
            }
            else
            {
                _title.text = "Матч окончен";
                _subtitle.text = state.LastLog ?? "";
            }

            if (_anim != null)
                StopCoroutine(_anim);
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            _anim = StartCoroutine(Animate(autoHide: false));
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
                    bool humanInGame = false;
                    for (int i = 0; i < table.Players.Count; i++)
                    {
                        if (table.Players[i].Type == PlayerType.Human)
                        {
                            humanInGame = true;
                            break;
                        }
                    }

                    if (p.Type == PlayerType.Human)
                        title = "Вы выиграли!";
                    else if (humanInGame)
                        title = $"Победитель: {p.Name}";
                    else
                        title = $"{p.Name} побеждает!";

                    var sb = new StringBuilder();
                    if (p.Type != PlayerType.Human && humanInGame)
                        sb.Append("Вы проиграли эту раздачу  ·  ");
                    sb.Append($"+{kv.Value} фишек");
                    if (!string.IsNullOrEmpty(handDesc) && handDesc != "Без вскрытия")
                        sb.Append("  ·  ").Append(handDesc);
                    else if (handDesc == "Без вскрытия")
                        sb.Append("  ·  все соперники сбросили");
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

        static void FormatOnlineResult(OnlineGameState state, out string title, out string subtitle)
        {
            title = "Раздача окончена";
            subtitle = state.LastLog ?? "";

            if (state.Pots.Count == 0)
                return;

            var wonChips = new Dictionary<int, int>();
            string handDesc = null;
            foreach (var pot in state.Pots)
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
                if (handDesc == null && !string.IsNullOrEmpty(pot.Description))
                    handDesc = pot.Description;
            }

            if (wonChips.Count == 0)
                return;

            OnlineSeatPlayer FindSeat(int seat)
            {
                foreach (var p in state.Players)
                    if (p.Seat == seat) return p;
                return null;
            }

            if (wonChips.Count == 1)
            {
                foreach (var kv in wonChips)
                {
                    var p = FindSeat(kv.Key);
                    string name = p?.Name ?? $"Игрок {kv.Key}";
                    bool iWon = p != null && p.Id == state.YouId;

                    title = iWon ? "Вы выиграли!" : $"Победитель: {name}";

                    var sb = new StringBuilder();
                    if (!iWon)
                        sb.Append("Вы проиграли эту раздачу  ·  ");
                    sb.Append($"+{kv.Value} фишек");
                    if (!string.IsNullOrEmpty(handDesc) && handDesc != "Без вскрытия")
                        sb.Append("  ·  ").Append(handDesc);
                    else if (handDesc == "Без вскрытия")
                        sb.Append("  ·  все соперники сбросили");
                    subtitle = sb.ToString();
                    return;
                }
            }

            var names = new List<string>();
            foreach (var kv in wonChips)
            {
                var p = FindSeat(kv.Key);
                names.Add($"{p?.Name ?? kv.Key.ToString()} (+{kv.Value})");
            }
            title = "Ничья — банк разделён";
            subtitle = string.Join(", ", names);
            if (!string.IsNullOrEmpty(handDesc))
                subtitle += "  ·  " + handDesc;
        }

        IEnumerator Animate(bool autoHide)
        {
            _group.alpha = 0f;
            _group.blocksRaycasts = false;
            _group.interactable = false;
            _panelRt.localScale = Vector3.one * 0.76f;
            SetBackdropAlpha(0f);
            SetTextAlpha(_title, 0f);
            SetTextAlpha(_subtitle, 0f);

            float t = 0f;
            while (t < FadeIn)
            {
                t += Time.unscaledDeltaTime;
                float u = Mathf.Clamp01(t / FadeIn);
                float e = EaseOutBack(u);
                _group.alpha = u;
                _panelRt.localScale = Vector3.LerpUnclamped(Vector3.one * 0.76f, Vector3.one, e);
                SetBackdropAlpha(Mathf.Lerp(0f, BackdropMaxAlpha, u));
                SetTextAlpha(_title, Mathf.Clamp01(u * 1.35f));
                SetTextAlpha(_subtitle, Mathf.Clamp01((u - 0.12f) * 1.35f));
                yield return null;
            }

            _group.alpha = 1f;
            _panelRt.localScale = Vector3.one;
            SetBackdropAlpha(BackdropMaxAlpha);
            SetTextAlpha(_title, 1f);
            SetTextAlpha(_subtitle, 1f);

            if (!autoHide)
            {
                _anim = null;
                yield break;
            }

            t = 0f;
            while (t < HoldDuration)
            {
                t += Time.unscaledDeltaTime;
                float pulse = 1f + Mathf.Sin(t * 2.4f) * 0.012f;
                _panelRt.localScale = Vector3.one * pulse;
                yield return null;
            }

            t = 0f;
            while (t < FadeOut)
            {
                t += Time.unscaledDeltaTime;
                float u = Mathf.Clamp01(t / FadeOut);
                float e = EaseInQuad(u);
                _group.alpha = 1f - u;
                _panelRt.localScale = Vector3.Lerp(Vector3.one, Vector3.one * 0.92f, e);
                SetBackdropAlpha(Mathf.Lerp(BackdropMaxAlpha, 0f, u));
                SetTextAlpha(_title, 1f - u);
                SetTextAlpha(_subtitle, 1f - u);
                yield return null;
            }

            _group.alpha = 0f;
            SetBackdropAlpha(0f);
            gameObject.SetActive(false);
            _anim = null;
            Finished?.Invoke();
        }

        void SetBackdropAlpha(float a)
        {
            if (_backdrop == null) return;
            var c = _backdrop.color;
            c.a = a;
            _backdrop.color = c;
        }

        static void SetTextAlpha(Text text, float a)
        {
            if (text == null) return;
            var c = text.color;
            c.a = a;
            text.color = c;
        }

        static float EaseInQuad(float x) => x * x;

        static float EaseOutBack(float x)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            return 1f + c3 * Mathf.Pow(x - 1f, 3f) + c1 * Mathf.Pow(x - 1f, 2f);
        }
    }
}
