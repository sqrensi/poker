using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Poker.Presentation
{
    /// <summary>Мягкая scale-анимация при нажатии UI-кнопок.</summary>
    public static class UiPressAnimation
    {
        public static void Attach(Button btn)
        {
            if (btn == null) return;
            if (btn.GetComponent<UiPressAnimationBehaviour>() != null) return;
            btn.gameObject.AddComponent<UiPressAnimationBehaviour>();
        }
    }

    sealed class UiPressAnimationBehaviour : MonoBehaviour,
        IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        const float PressScale = 0.93f;
        const float PressInDuration = 0.07f;
        const float ReleaseDuration = 0.2f;
        const float OvershootScale = 1.025f;

        RectTransform _rt;
        Button _button;
        Coroutine _anim;
        bool _pressed;
        Vector3 _baseScale = Vector3.one;

        void Awake()
        {
            _rt = transform as RectTransform;
            _button = GetComponent<Button>();
            if (_rt != null)
                _baseScale = _rt.localScale;
        }

        void OnDisable()
        {
            _pressed = false;
            StopAnim();
            if (_rt != null)
                _rt.localScale = _baseScale;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (!CanAnimate()) return;
            _pressed = true;
            AnimateScale(_baseScale * PressScale, PressInDuration, easeOut: false);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (!_pressed) return;
            _pressed = false;
            PlayRelease();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (!_pressed) return;
            _pressed = false;
            PlayRelease();
        }

        bool CanAnimate() => _rt != null && (_button == null || _button.interactable);

        void PlayRelease()
        {
            StopAnim();
            _anim = StartCoroutine(ReleaseRoutine());
        }

        IEnumerator ReleaseRoutine()
        {
            Vector3 overshoot = _baseScale * OvershootScale;
            yield return ScaleRoutine(_rt.localScale, overshoot, ReleaseDuration * 0.55f, easeOut: true);
            yield return ScaleRoutine(_rt.localScale, _baseScale, ReleaseDuration * 0.45f, easeOut: true);
            _rt.localScale = _baseScale;
            _anim = null;
        }

        void AnimateScale(Vector3 target, float duration, bool easeOut)
        {
            StopAnim();
            _anim = StartCoroutine(ScaleRoutine(_rt.localScale, target, duration, easeOut));
        }

        IEnumerator ScaleRoutine(Vector3 from, Vector3 to, float duration, bool easeOut)
        {
            if (duration <= 0f)
            {
                _rt.localScale = to;
                yield break;
            }

            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / duration);
                if (easeOut)
                    k = 1f - (1f - k) * (1f - k);
                else
                    k = k * k;
                _rt.localScale = Vector3.LerpUnclamped(from, to, k);
                yield return null;
            }

            _rt.localScale = to;
        }

        void StopAnim()
        {
            if (_anim != null)
            {
                StopCoroutine(_anim);
                _anim = null;
            }
        }
    }
}
