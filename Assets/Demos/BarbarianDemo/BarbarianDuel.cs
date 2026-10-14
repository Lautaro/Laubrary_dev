using System.Collections.Generic;
using UnityEngine;
using Laubrary.Zoetrope;

namespace Laubrary.Demos.BarbarianDemo
{
    /// <summary>
    /// The rules between the player's Barbarian and the CPU Barbarian: when their drawn sprites touch they stop,
    /// and the two sprites are left highlighted, hanging in the air, for a moment.
    ///
    /// The touch is pixel exact (a drawn pixel of one over a drawn pixel of the other), not a box test, so a
    /// raised sword touches before two empty corners would. This is game code: the library supplies the test
    /// (<see cref="SpriteAlphaHitFilter.Overlap"/>); what counts as a contact, and what happens, is decided here.
    /// </summary>
    public class BarbarianDuel : MonoBehaviour
    {
        [Tooltip("The player's character.")]
        public GameObject player;
        [Tooltip("The CPU character.")]
        public GameObject enemy;
        [Tooltip("How long the highlighted sprites and the marker stay after a touch, in seconds.")]
        public float highlightSeconds = 0.7f;
        [Tooltip("Colour of the highlight laid over the touching sprites.")]
        public Color highlightColor = new Color(1f, 0.92f, 0.3f, 0.85f);
        [Tooltip("Pixels this far above the feet line (art pixels) and lower do not count as touching: the ground shadow drawn into the sprite is not the body.")]
        public float ignoreBelowFeetPixels = 7f;
        [Tooltip("A second touch is ignored for this long after one, so a body held against the other does not retrigger.")]
        public float contactCooldown = 0.8f;

        SpriteRenderer _playerSprite, _enemySprite;
        Rigidbody2D _playerBody, _enemyBody;
        Vector2 _playerClear, _enemyClear;
        float _cooldownUntil;
        readonly List<GameObject> _highlights = new List<GameObject>();
        static Sprite s_ring;

        /// Raised once per touch with the middle of the shared pixels.
        public event System.Action<Vector2> Touched;

        void Start()
        {
            Resolve();
        }

        void Resolve()
        {
            if (player != null) { _playerSprite = player.GetComponentInChildren<SpriteRenderer>(); _playerBody = player.GetComponent<Rigidbody2D>(); _playerClear = player.transform.position; }
            if (enemy != null) { _enemySprite = enemy.GetComponentInChildren<SpriteRenderer>(); _enemyBody = enemy.GetComponent<Rigidbody2D>(); _enemyClear = enemy.transform.position; }
        }

        void LateUpdate()
        {
            if (_playerSprite == null || _enemySprite == null) { Resolve(); return; }

            bool touching = SpriteAlphaHitFilter.Overlap(_playerSprite, _enemySprite, out Vector2 centre, out int pixels, 0.1f, FeetY() + ignoreBelowFeetPixels / Mathf.Max(1f, _playerSprite.sprite.pixelsPerUnit));
            if (!touching)
            {
                _playerClear = player.transform.position;
                _enemyClear = enemy.transform.position;
                return;
            }

            // Stop: put both bodies back where they last did not touch.
            Place(player, _playerBody, _playerClear);
            Place(enemy, _enemyBody, _enemyClear);

            if (Time.time < _cooldownUntil) return;
            _cooldownUntil = Time.time + contactCooldown;
            Highlight(centre);
            Touched?.Invoke(centre);
        }

        // The characters stand on one line; the body's own position is its feet.
        float FeetY() => player.transform.position.y;

        static void Place(GameObject body, Rigidbody2D rb, Vector2 at)
        {
            body.transform.position = new Vector3(at.x, at.y, body.transform.position.z);
            if (rb != null) rb.position = at;
        }

        // ── the highlight: a copy of each touching sprite in a bright tint, and a ring on the touch point ──

        void Highlight(Vector2 centre)
        {
            ClearHighlights();
            _highlights.Add(Ghost(_playerSprite));
            _highlights.Add(Ghost(_enemySprite));
            _highlights.Add(Ring(centre));
        }

        GameObject Ghost(SpriteRenderer source)
        {
            var go = new GameObject("Touch highlight");
            go.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
            go.transform.localScale = source.transform.lossyScale;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = source.sprite; sr.flipX = source.flipX; sr.flipY = source.flipY;
            sr.sortingLayerID = source.sortingLayerID; sr.sortingOrder = source.sortingOrder + 1;
            sr.color = highlightColor;
            go.AddComponent<Fade>().Begin(highlightSeconds, highlightColor);
            return go;
        }

        GameObject Ring(Vector2 centre)
        {
            var go = new GameObject("Touch marker");
            go.transform.position = new Vector3(centre.x, centre.y, player.transform.position.z);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = RingSprite(); sr.sortingLayerID = _playerSprite.sortingLayerID; sr.sortingOrder = _playerSprite.sortingOrder + 2;
            go.transform.localScale = Vector3.one * 0.8f;
            go.AddComponent<Fade>().Begin(highlightSeconds, Color.white);
            return go;
        }

        void ClearHighlights()
        {
            foreach (var h in _highlights) if (h != null) Destroy(h);
            _highlights.Clear();
        }

        void OnDestroy() => ClearHighlights();

        static Sprite RingSprite()
        {
            if (s_ring != null) return s_ring;
            const int n = 32;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, hideFlags = HideFlags.HideAndDontSave };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + .5f, y + .5f), new Vector2(n / 2f, n / 2f));
                    px[y * n + x] = d > 12f && d < 15.5f ? new Color32(255, 255, 255, 255) : new Color32(0, 0, 0, 0);
                }
            tex.SetPixels32(px); tex.Apply();
            s_ring = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(.5f, .5f), 16f);
            s_ring.hideFlags = HideFlags.HideAndDontSave;
            return s_ring;
        }

        /// Holds a highlight at full strength for most of its life, fades it out over the last third, then removes it.
        sealed class Fade : MonoBehaviour
        {
            float _life, _t; Color _color; SpriteRenderer _sr;
            public void Begin(float seconds, Color color) { _life = Mathf.Max(0.05f, seconds); _color = color; _sr = GetComponent<SpriteRenderer>(); }
            void Update()
            {
                _t += Time.unscaledDeltaTime;
                float fadeStart = _life * 0.66f;
                if (_sr != null) { var c = _color; c.a *= _t < fadeStart ? 1f : Mathf.Clamp01(1f - (_t - fadeStart) / (_life - fadeStart)); _sr.color = c; }
                if (_t >= _life) Destroy(gameObject);
            }
        }
    }
}
