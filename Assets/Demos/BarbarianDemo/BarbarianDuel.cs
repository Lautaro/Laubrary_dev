using System.Collections.Generic;
using UnityEngine;
using Laubrary.Zoetrope;

namespace Laubrary.Demos.BarbarianDemo
{
    /// <summary>
    /// The rules between the player's Barbarian and the CPU Barbarian: when their drawn sprites touch they stop,
    /// the pixels where the two sprites overlap are left highlighted, hanging in the air, for a moment, and both play the "pushed back"
    /// animation while sliding apart, as if each shoved the other. When the touch comes from the player's own attack
    /// the same push happens, but the CPU Barbarian is knocked down (its "Hit" reaction: it falls and gets up).
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
        [Tooltip("How long the highlighted overlap pixels stay after a touch, in seconds.")]
        public float highlightSeconds = 0.7f;
        [Tooltip("Colour of the highlight laid over the pixels the sprites share.")]
        public Color highlightColor = new Color(1f, 0.92f, 0.3f, 0.85f);
        [Tooltip("Pixels this far above the feet line (art pixels) and lower do not count as touching: the ground shadow drawn into the sprite is not the body.")]
        public float ignoreBelowFeetPixels = 7f;
        [Tooltip("A pixel counts as drawn above this alpha (0..1). Pixel art is fully opaque or clear, so stray faint pixels in the sheet do not count.")]
        [Range(0f, 1f)] public float solidAlpha = 0.5f;
        [Tooltip("How far each body is pushed away from the other by a touch, in world units.")]
        public float pushDistance = 0.9f;
        [Tooltip("How long that push takes, in seconds.")]
        public float pushSeconds = 0.4f;
        [Tooltip("A second touch is ignored for this long after one, so a body held against the other does not retrigger.")]
        public float contactCooldown = 0.8f;

        SpriteRenderer _playerSprite, _enemySprite;
        Rigidbody2D _playerBody, _enemyBody;
        Vector2 _playerClear, _enemyClear;
        float _cooldownUntil, _playerReactingUntil;
        readonly List<GameObject> _highlights = new List<GameObject>();
        /// The pixels the two sprites shared at the latest touch.
        public SpriteContact LastContact { get; private set; }

        /// Raised once per touch with the middle of the shared pixels.
        public event System.Action<Vector2> Touched;

        void Start()
        {
            Resolve();
            Touched += OnTouched;
        }

        // A touch. Both are pushed back and show the pushed-back reaction; but when the touch comes from the player's own
        // attack (the player is in the middle of a move, not in a reaction we caused), the CPU is knocked down instead.
        void OnTouched(Vector2 point)
        {
            var playerReactions = player.GetComponent<ReactionFxPlayer>();
            bool attacking = playerReactions != null && playerReactions.HoldingStill && Time.time >= _playerReactingUntil;
            PushBack(player, enemy, ZoeStates.BarbarianStates.PushedBackwards);
            _playerReactingUntil = Time.time + pushSeconds + 0.4f;
            PushBack(enemy, player, attacking ? ZoeStates.BarbarianStates.Hit : ZoeStates.BarbarianStates.PushedBackwards);
        }

        // Plays the named reaction on a body and slides it away from the other body.
        void PushBack(GameObject body, GameObject from, string reaction)
        {
            body.GetComponent<ReactionFxPlayer>()?.Raise(reaction);
            float away = Mathf.Sign(body.transform.position.x - from.transform.position.x);
            if (away == 0f) away = 1f;
            var slide = body.GetComponent<Slide>();
            if (slide == null) slide = body.AddComponent<Slide>();
            slide.Begin(away, pushDistance, pushSeconds);
        }

        bool Sliding => (player != null && player.TryGetComponent<Slide>(out var a) && a.Active) || (enemy != null && enemy.TryGetComponent<Slide>(out var b) && b.Active);

        void Resolve()
        {
            if (player != null) { _playerSprite = player.GetComponentInChildren<SpriteRenderer>(); _playerBody = player.GetComponent<Rigidbody2D>(); _playerClear = player.transform.position; }
            if (enemy != null) { _enemySprite = enemy.GetComponentInChildren<SpriteRenderer>(); _enemyBody = enemy.GetComponent<Rigidbody2D>(); _enemyClear = enemy.transform.position; }
        }

        void LateUpdate()
        {
            if (_playerSprite == null || _enemySprite == null) { Resolve(); return; }

            bool touching = SpriteAlphaHitFilter.Overlap(_playerSprite, _enemySprite, out SpriteContact contact, solidAlpha, FeetY() + ignoreBelowFeetPixels / Mathf.Max(1f, _playerSprite.sprite.pixelsPerUnit));
            if (!touching)
            {
                _playerClear = player.transform.position;
                _enemyClear = enemy.transform.position;
                return;
            }

            // Stop: put both bodies back where they last did not touch (not while a push is carrying them apart).
            if (!Sliding)
            {
                Place(player, _playerBody, _playerClear);
                Place(enemy, _enemyBody, _enemyClear);
            }

            if (Time.time < _cooldownUntil) return;
            _cooldownUntil = Time.time + contactCooldown;
            LastContact = contact;
            Highlight(contact);
            Touched?.Invoke(contact.Centre);
        }

        // The characters stand on one line; the body's own position is its feet.
        float FeetY() => player.transform.position.y;

        static void Place(GameObject body, Rigidbody2D rb, Vector2 at)
        {
            body.transform.position = new Vector3(at.x, at.y, body.transform.position.z);
            if (rb != null) rb.position = at;
        }

        // ── the highlight: only the pixels the two sprites share, drawn where they are ──

        void Highlight(SpriteContact contact)
        {
            ClearHighlights();
            var go = new GameObject("Touch highlight");
            go.transform.position = new Vector3(contact.Origin.x, contact.Origin.y, player.transform.position.z);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = contact.ToSprite(Color.white);
            sr.sortingLayerID = _playerSprite.sortingLayerID; sr.sortingOrder = Mathf.Max(_playerSprite.sortingOrder, _enemySprite.sortingOrder) + 1;
            sr.color = highlightColor;
            go.AddComponent<Fade>().Begin(highlightSeconds, highlightColor);
            _highlights.Add(go);
        }

        void ClearHighlights()
        {
            foreach (var h in _highlights) if (h != null) Destroy(h);
            _highlights.Clear();
        }

        void OnDestroy() => ClearHighlights();

        /// Moves a body along the line, quickly at first and settling, then stops. Used for the push of a touch.
        sealed class Slide : MonoBehaviour
        {
            float _dir, _distance, _seconds, _t; float _done; Rigidbody2D _rb;
            public bool Active { get; private set; }
            public void Begin(float direction, float distance, float seconds)
            {
                _dir = direction; _distance = distance; _seconds = Mathf.Max(0.05f, seconds); _t = 0f; _done = 0f; Active = true;
                if (_rb == null) _rb = GetComponent<Rigidbody2D>();
            }
            void Update()
            {
                if (!Active) return;
                _t += Time.deltaTime;
                float u = Mathf.Clamp01(_t / _seconds);
                float eased = 1f - (1f - u) * (1f - u);
                float now = eased * _distance, step = now - _done; _done = now;
                var p = (Vector2)transform.position + new Vector2(_dir * step, 0f);
                transform.position = new Vector3(p.x, p.y, transform.position.z);
                if (_rb != null) _rb.position = p;
                if (u >= 1f) Active = false;
            }
        }

        /// Holds a highlight at full strength for most of its life, fades it out over the last third, then removes it.
        sealed class Fade : MonoBehaviour
        {
            float _life, _t; Color _color; SpriteRenderer _sr;
            public void Begin(float seconds, Color color) { _life = Mathf.Max(0.05f, seconds); _color = color; _sr = GetComponent<SpriteRenderer>(); }
            // The highlight owns the sprite and texture built for it.
            void OnDestroy() { if (_sr != null && _sr.sprite != null) { Destroy(_sr.sprite.texture); Destroy(_sr.sprite); } }
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
