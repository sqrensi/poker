using UnityEngine;
using Poker.Game;

namespace Poker.Presentation
{
    public sealed class SeatView : MonoBehaviour
    {
        public int SeatIndex { get; private set; }
        public Transform CardAnchor { get; private set; }
        public float SeatAngle => _seatAngle;
        public Vector3 WorldPosition => transform.position;

        GameObject _dealerButton;
        GameObject _betRoot;
        Transform _chipStack;
        float _seatAngle;

        const int MaxChips = 8;
        const float ChipBodyH = 0.055f;
        const float StripeH = 0.018f;
        const float ChipStep = 0.078f;

        public static SeatView Create(Transform parent, int seatIndex, Vector3 position, float seatAngleDegrees)
        {
            var go = new GameObject($"Seat_{seatIndex}");
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            go.transform.rotation = Quaternion.identity;

            var view = go.AddComponent<SeatView>();
            view.SeatIndex = seatIndex;
            view._seatAngle = seatAngleDegrees;
            view.Build();
            return view;
        }

        void Build()
        {
            float rad = _seatAngle * Mathf.Deg2Rad;
            Vector3 toCenter = new Vector3(-Mathf.Cos(rad), 0f, -Mathf.Sin(rad)).normalized;
            Vector3 sideways = new Vector3(-Mathf.Sin(rad), 0f, Mathf.Cos(rad)).normalized;

            CardAnchor = new GameObject("Cards").transform;
            CardAnchor.SetParent(transform, false);
            CardAnchor.localPosition = new Vector3(0f, 0.12f, 0f);

            BuildBetChips(toCenter, sideways);
            BuildDealer(toCenter, sideways);
        }

        void BuildBetChips(Vector3 toCenter, Vector3 sideways)
        {
            Vector3 betPos = toCenter * 1.55f + sideways * 1.4f;
            betPos.y = 0.05f;

            _betRoot = new GameObject("BetChips");
            _betRoot.transform.SetParent(transform, false);
            _betRoot.transform.localPosition = betPos;
            _betRoot.SetActive(false);

            _chipStack = new GameObject("Stack").transform;
            _chipStack.SetParent(_betRoot.transform, false);
            _chipStack.localPosition = Vector3.zero;

            var red = new Color(0.85f, 0.14f, 0.12f);
            var dark = new Color(0.08f, 0.08f, 0.1f);

            for (int i = 0; i < MaxChips; i++)
            {
                var layer = new GameObject($"ChipLayer_{i}");
                layer.transform.SetParent(_chipStack, false);
                layer.transform.localPosition = new Vector3(0f, i * ChipStep, 0f);
                layer.SetActive(false);

                var body = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                body.name = "Body";
                body.transform.SetParent(layer.transform, false);
                body.transform.localPosition = new Vector3(0f, ChipBodyH * 0.5f, 0f);
                body.transform.localScale = new Vector3(0.34f, ChipBodyH * 0.5f, 0.34f);
                SmoothMesh.ReplacePrimitiveMesh(body, SmoothMesh.Cylinder());
                Object.Destroy(body.GetComponent<Collider>());
                PokerMaterials.ApplyColor(body.GetComponent<MeshRenderer>(), red);

                var stripe = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                stripe.name = "Stripe";
                stripe.transform.SetParent(layer.transform, false);
                stripe.transform.localPosition = new Vector3(0f, ChipBodyH + StripeH * 0.5f, 0f);
                stripe.transform.localScale = new Vector3(0.36f, StripeH * 0.5f, 0.36f);
                SmoothMesh.ReplacePrimitiveMesh(stripe, SmoothMesh.Cylinder());
                Object.Destroy(stripe.GetComponent<Collider>());
                PokerMaterials.ApplyColor(stripe.GetComponent<MeshRenderer>(), dark);
            }
        }

        void BuildDealer(Vector3 toCenter, Vector3 sideways)
        {
            Vector3 dealerPos = toCenter * 1.5f - sideways * 1.45f;
            dealerPos.y = 0.08f;
            _dealerButton = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            _dealerButton.name = "DealerButton";
            _dealerButton.transform.SetParent(transform, false);
            _dealerButton.transform.localPosition = dealerPos;
            _dealerButton.transform.localScale = new Vector3(0.28f, 0.04f, 0.28f);
            SmoothMesh.ReplacePrimitiveMesh(_dealerButton, SmoothMesh.Cylinder());
            Object.Destroy(_dealerButton.GetComponent<Collider>());
            PokerMaterials.ApplyColor(_dealerButton.GetComponent<MeshRenderer>(), new Color(0.95f, 0.92f, 0.2f));

            var labelGo = new GameObject("D");
            labelGo.transform.SetParent(_dealerButton.transform, false);
            labelGo.transform.localPosition = new Vector3(0f, 1.2f, 0f);
            labelGo.transform.localRotation = Quaternion.Euler(-90f, 180f, 0f);
            labelGo.transform.localScale = new Vector3(-0.22f, 0.22f, 0.22f);
            var tm = labelGo.AddComponent<TextMesh>();
            tm.font = UiFont.Builtin();
            tm.text = "D";
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.characterSize = 0.22f;
            tm.fontSize = 120;
            tm.fontStyle = FontStyle.Bold;
            tm.color = new Color(0.15f, 0.12f, 0.05f);
            var mr = labelGo.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
            }

            _dealerButton.SetActive(false);
        }

        public void Refresh(Player player, bool isDealer, bool isActing, Street street, int bigBlind, bool isHandWinner = false)
        {
            _dealerButton.SetActive(isDealer);

            bool showBet = !player.HasFolded && player.BetThisStreet > 0;
            _betRoot.SetActive(showBet);
            if (!showBet) return;

            int unit = Mathf.Max(1, bigBlind);
            int chips = Mathf.Clamp(Mathf.CeilToInt(player.BetThisStreet / (float)unit), 1, MaxChips);
            for (int i = 0; i < _chipStack.childCount; i++)
            {
                var layer = _chipStack.GetChild(i);
                bool active = i < chips;
                layer.gameObject.SetActive(active);
                if (!active) continue;

                var stripe = layer.Find("Stripe");
                if (stripe != null)
                    stripe.gameObject.SetActive(i < chips - 1);
            }
        }
    }
}
