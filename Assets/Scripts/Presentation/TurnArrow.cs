using UnityEngine;

namespace Poker.Presentation
{
    /// <summary>
    /// Arrow outside the table, pointing inward at the acting seat.
    /// </summary>
    public sealed class TurnArrow : MonoBehaviour
    {
        Transform _shaft;
        Transform _head;

        public static TurnArrow Create(Transform parent)
        {
            var go = new GameObject("TurnArrow");
            go.transform.SetParent(parent, false);
            var arrow = go.AddComponent<TurnArrow>();
            arrow.Build();
            go.SetActive(false);
            return arrow;
        }

        void Build()
        {
            var color = new Color(1f, 0.85f, 0.15f);

            _shaft = GameObject.CreatePrimitive(PrimitiveType.Cube).transform;
            _shaft.name = "Shaft";
            _shaft.SetParent(transform, false);
            _shaft.localPosition = new Vector3(0f, 0.08f, -0.35f);
            _shaft.localScale = new Vector3(0.22f, 0.08f, 0.7f);
            Object.Destroy(_shaft.GetComponent<Collider>());
            _shaft.GetComponent<MeshRenderer>().material = PokerMaterials.ColorMat(color);

            // Наконечник: вытянутый куб ромбом (повёрнут)
            _head = GameObject.CreatePrimitive(PrimitiveType.Cube).transform;
            _head.name = "Head";
            _head.SetParent(transform, false);
            _head.localPosition = new Vector3(0f, 0.08f, 0.15f);
            _head.localRotation = Quaternion.Euler(0f, 45f, 0f);
            _head.localScale = new Vector3(0.45f, 0.08f, 0.45f);
            Object.Destroy(_head.GetComponent<Collider>());
            _head.GetComponent<MeshRenderer>().material = PokerMaterials.ColorMat(color);
        }

        /// <summary>
        /// Place outside the felt, pointing toward seat (radial inward).
        /// </summary>
        public void PointAt(Vector3 seatWorldPos, float outsideScale = 1.38f)
        {
            Vector3 flat = new Vector3(seatWorldPos.x, 0f, seatWorldPos.z);
            if (flat.sqrMagnitude < 0.01f)
            {
                gameObject.SetActive(false);
                return;
            }

            Vector3 outward = flat.normalized;
            Vector3 arrowPos = flat * outsideScale;
            arrowPos.y = 0.05f;
            transform.position = arrowPos;

            // Local +Z is toward head tip → rotate so +Z points to seat (inward = -outward)
            Vector3 inward = -outward;
            transform.rotation = Quaternion.LookRotation(inward, Vector3.up);
            gameObject.SetActive(true);
        }

        public void Hide()
        {
            gameObject.SetActive(false);
        }
    }
}
