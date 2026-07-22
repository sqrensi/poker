using System.Collections;
using UnityEngine;
using Poker.Core;

namespace Poker.Presentation
{
    public sealed class CardView : MonoBehaviour
    {
        SpriteRenderer _renderer;
        bool _faceUp;
        bool _hasCard;
        Card _card;
        float _width = 0.78f;
        float _seatYaw;
        Vector3 _closedPos;
        Vector3 _revealPos;
        Vector3 _baseScale = Vector3.one;
        bool _foldedAway;
        bool _foldAnimating;
        Coroutine _foldRoutine;

        public bool IsFoldedAway => _foldedAway;
        public bool IsFoldAnimating => _foldAnimating;

        public static CardView Create(
            Transform parent,
            Vector3 localPos,
            float yawDegrees = 0f,
            float width = 0.78f,
            int sortingOrder = 10,
            Vector3? revealLocalPos = null)
        {
            CardSpriteCatalog.EnsureLoaded();

            var root = new GameObject("Card");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = localPos;
            root.transform.localScale = Vector3.one;

            var view = root.AddComponent<CardView>();
            view._width = width;
            view._seatYaw = yawDegrees;
            view._closedPos = localPos;
            view._revealPos = revealLocalPos ?? localPos;
            view.ApplyYaw(yawDegrees);
            view.BuildVisuals(sortingOrder);
            view.ShowBack();
            return view;
        }

        void ApplyYaw(float yawDegrees)
        {
            transform.localRotation = Quaternion.Euler(-90f, 180f + yawDegrees, 0f);
        }

        public void SetFacingViewer(bool faceTowardViewer)
        {
            if (_foldAnimating || _foldedAway) return;
            ApplyYaw(faceTowardViewer ? 0f : _seatYaw);
            transform.localPosition = faceTowardViewer ? _revealPos : _closedPos;
        }

        void BuildVisuals(int sortingOrder)
        {
            _renderer = gameObject.AddComponent<SpriteRenderer>();
            _renderer.sortingOrder = sortingOrder;
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;

            var back = CardSpriteCatalog.Back;
            if (back != null)
            {
                _renderer.sprite = back;
                FitWidth(_width);
            }
        }

        public void SetSortingOrder(int order)
        {
            if (_renderer != null)
                _renderer.sortingOrder = order;
        }

        void FitWidth(float worldWidth)
        {
            if (_renderer.sprite == null) return;
            float spriteWidth = _renderer.sprite.bounds.size.x;
            if (spriteWidth < 0.0001f) return;
            float s = worldWidth / spriteWidth;
            _baseScale = new Vector3(-s, s, 1f);
            transform.localScale = _baseScale;
        }

        public void SetCard(Card card, bool faceUp)
        {
            if (_foldAnimating || _foldedAway) return;
            _card = card;
            _hasCard = true;
            _faceUp = faceUp;
            Refresh();
        }

        public void ShowBack()
        {
            if (_foldAnimating || _foldedAway) return;
            _faceUp = false;
            _hasCard = false;
            Refresh();
        }

        void Refresh()
        {
            if (_renderer == null) return;

            Sprite sprite = null;
            if (_faceUp && _hasCard)
                sprite = CardSpriteCatalog.GetFace(_card);
            if (sprite == null)
                sprite = CardSpriteCatalog.Back;

            _renderer.sprite = sprite;
            FitWidth(_width);
            _renderer.color = (_faceUp && _hasCard) ? Color.white : new Color(0.92f, 0.94f, 1f, 1f);
        }

        /// <summary>Анимация сброса — карта уезжает к центру стола и исчезает.</summary>
        public void PlayFoldAway(Vector3 localDrift, float delay = 0f)
        {
            if (_foldedAway || _foldAnimating) return;
            if (_foldRoutine != null)
                StopCoroutine(_foldRoutine);
            _foldRoutine = StartCoroutine(FoldAwayRoutine(localDrift, delay));
        }

        IEnumerator FoldAwayRoutine(Vector3 localDrift, float delay)
        {
            _foldAnimating = true;
            if (delay > 0f)
                yield return new WaitForSeconds(delay);

            gameObject.SetActive(true);
            float dur = 0.42f;
            float t = 0f;
            Vector3 startPos = transform.localPosition;
            Vector3 startScale = transform.localScale;
            Color startColor = _renderer != null ? _renderer.color : Color.white;

            while (t < dur)
            {
                t += Time.deltaTime;
                float u = Mathf.Clamp01(t / dur);
                float ease = u * u * (3f - 2f * u);
                transform.localPosition = startPos + localDrift * ease;
                transform.localScale = Vector3.Lerp(startScale, startScale * 0.55f, ease);
                if (_renderer != null)
                {
                    float a = 1f - ease;
                    _renderer.color = new Color(startColor.r, startColor.g, startColor.b, a);
                }
                yield return null;
            }

            _foldAnimating = false;
            _foldedAway = true;
            _foldRoutine = null;
            gameObject.SetActive(false);
        }

        public void ResetForNewHand()
        {
            if (_foldRoutine != null)
            {
                StopCoroutine(_foldRoutine);
                _foldRoutine = null;
            }
            _foldAnimating = false;
            _foldedAway = false;
            _faceUp = false;
            _hasCard = false;
            gameObject.SetActive(true);
            ApplyYaw(_seatYaw);
            transform.localPosition = _closedPos;
            transform.localScale = _baseScale;
            if (_renderer != null)
                _renderer.color = new Color(0.92f, 0.94f, 1f, 1f);
        }
    }
}
