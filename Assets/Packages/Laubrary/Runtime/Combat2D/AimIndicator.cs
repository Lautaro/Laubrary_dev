using UnityEngine;

namespace Laubrary.Combat2D
{
    /// A direction marker riding a circle around a character: the twin-stick "where am I aiming" pointer.
    /// It marks direction only and never travels from its owner. Generic on purpose — the game feeds it a
    /// Direction and owns nothing else; radius, size, colour and the circle's centre are dials.
    ///
    /// The circle centres on the VISUAL body, not the transform origin: a character root usually sits at the
    /// feet, and a pointer orbiting the feet reads as broken. By default the centre is measured once from
    /// the first sprite the owner shows; setting `centerOffset` by hand switches the measurement off.
    public class AimIndicator : MonoBehaviour
    {
        [Tooltip("How far from the circle's centre the marker sits, in world units.")]
        [Range(0.1f, 4f)] public float radius = 0.9f;

        [Tooltip("Size of the marker triangle, in world units.")]
        [Range(0.05f, 2f)] public float size = 0.4f;

        [Tooltip("Marker colour.")]
        public Color color = new Color(1f, 1f, 1f, 0.9f);

        [Tooltip("Where the circle's centre sits, relative to this transform. Left at zero, it is measured " +
                 "from the owner's visible sprite once one exists; set it by hand to override.")]
        public Vector2 centerOffset;

        [Tooltip("Sprite sorting order for the marker — keep it above the character.")]
        public int sortingOrder = 60;

        /// The aim, set by the game every frame. Normalised on use.
        public Vector2 Direction { get; set; } = Vector2.up;

        Transform marker;
        SpriteRenderer markerRenderer;
        bool centered;

        void OnEnable()
        {
            if (marker != null) return;
            var go = new GameObject("AimMarker");
            go.transform.SetParent(transform, false);
            markerRenderer = go.AddComponent<SpriteRenderer>();
            markerRenderer.sprite = TriangleSprite();
            marker = go.transform;
        }

        void OnDisable()
        {
            if (marker != null) Destroy(marker.gameObject);
            marker = null;
            centered = false;
        }

        void LateUpdate()
        {
            if (marker == null) return;

            // Measure the visual centre lazily — a character's view is often built a frame after spawn.
            if (!centered && centerOffset == Vector2.zero)
            {
                foreach (var sr in GetComponentsInChildren<SpriteRenderer>())
                {
                    if (sr == markerRenderer || sr.sprite == null) continue;
                    centerOffset = sr.bounds.center - transform.position;
                    centered = true;
                    break;
                }
            }

            var dir = Direction.sqrMagnitude > 0.0001f ? Direction.normalized : Vector2.up;
            marker.localPosition = (Vector3)(centerOffset + dir * radius);
            marker.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f);
            marker.localScale = Vector3.one * size;
            markerRenderer.color = color;
            markerRenderer.sortingOrder = sortingOrder;
        }

        static Sprite _triangle;
        static Sprite TriangleSprite()
        {
            if (_triangle != null) return _triangle;
            const int S = 16;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Point, hideFlags = HideFlags.HideAndDontSave };
            var px = new Color32[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                    px[y * S + x] = Mathf.Abs(x - (S - 1) * 0.5f) <= (S - 1 - y) * 0.5f
                        ? new Color32(255, 255, 255, 255) : default;
            tex.SetPixels32(px);
            tex.Apply();
            _triangle = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), S);
            _triangle.hideFlags = HideFlags.HideAndDontSave;
            return _triangle;
        }
    }
}
