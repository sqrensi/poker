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

        /// <summary>
        /// При вскрытии — лицом к камере; для боковых ещё выстраивает в ряд по X.
        /// </summary>
        public void SetFacingViewer(bool faceTowardViewer)
        {
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
            transform.localScale = new Vector3(-s, s, 1f);
        }

        public void SetCard(Card card, bool faceUp)
        {
            _card = card;
            _hasCard = true;
            _faceUp = faceUp;
            Refresh();
        }

        public void ShowBack()
        {
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
    }
}
