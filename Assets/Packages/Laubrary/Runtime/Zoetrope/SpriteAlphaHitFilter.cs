using System.Collections.Generic;
using UnityEngine;
using Laubrary.Combat2D;

namespace Laubrary.Zoetrope
{
    /// Pixel-perfect hit confirmation against the sprite's OWN alpha — no painted mask required.
    ///
    /// A collider is a rectangle around a character; a shot that clips its corner reads to the player as a
    /// miss that counted. This confirms the hit landed on a pixel that is actually drawn, which is what
    /// "pixel perfect" means to a player. Colliders stay the BROAD phase (cheap, physics-accelerated) and
    /// this is the narrow one — the same two-phase shape every pixel-perfect 2D game uses.
    ///
    /// Distinct from LauminaryHitFilter, which tests a painted meta-layer and so answers "did it hit the HURT
    /// region" — a different, finer question that needs authoring. This needs none: every sprite already
    /// knows which of its pixels are transparent.
    ///
    /// Filters compose (every IHitFilter on a Combatant must agree), so a Zoe can carry both: alpha for "is
    /// there anything there", a mask for "and is that part vulnerable".
    [RequireComponent(typeof(Combatant))]
    public class SpriteAlphaHitFilter : MonoBehaviour, IHitFilter
    {
        [Tooltip("Alpha above which a pixel counts as solid, 0..1. Above 0 so a soft anti-aliased edge does " +
                 "not register as a hit on empty space.")]
        [Range(0f, 1f)] public float alphaThreshold = 0.1f;

        [Tooltip("What to do when the sprite cannot be read at all (no renderer, no sprite). TRUE accepts the " +
                 "hit — a filter that cannot answer should not silently make a character invulnerable, which " +
                 "is far harder to diagnose than an occasional generous hit.")]
        public bool acceptWhenUnresolved = true;

        SpriteRenderer _sr;

        // Alpha, per sprite, read ONCE. Sprite textures are imported non-readable by default so GetPixels
        // throws on them — the same trap that broke asset thumbnails — so this goes through PreviewKit's
        // blit-based crop, which reads anything the GPU can sample. Static because two Doom Imps share the
        // same lauminary frames and should share the work.
        static readonly Dictionary<int, byte[]> s_alpha = new();
        static readonly Dictionary<int, Vector2Int> s_size = new();

        void Awake() => _sr = GetComponentInChildren<SpriteRenderer>();

        public bool ConfirmHit(Combatant self, GameObject striker, Vector2 worldPoint)
        {
            if (_sr == null) _sr = GetComponentInChildren<SpriteRenderer>();
            var sprite = _sr != null ? _sr.sprite : null;
            if (sprite == null || _sr == null) return acceptWhenUnresolved;

            if (!TryPixel(sprite, _sr, worldPoint, out int x, out int y)) return false;   // off the sprite = a miss

            var alpha = AlphaOf(sprite);
            if (alpha == null) return acceptWhenUnresolved;

            var size = s_size[sprite.GetInstanceID()];
            int i = y * size.x + x;
            if (i < 0 || i >= alpha.Length) return false;
            return alpha[i] >= Mathf.RoundToInt(alphaThreshold * 255f);
        }

        /// World point → pixel within the sprite's own rect. False when it lands outside the sprite entirely.
        static bool TryPixel(Sprite sprite, SpriteRenderer sr, Vector2 world, out int x, out int y)
        {
            x = y = 0;
            Vector2 local = sr.transform.InverseTransformPoint(world);

            // The renderer's flips are a DRAW-time mirror the transform knows nothing about, so undo them
            // here or every hit on a left-facing character is tested against its mirror image.
            if (sr.flipX) local.x = -local.x;
            if (sr.flipY) local.y = -local.y;

            float ppu = sprite.pixelsPerUnit;
            float px = local.x * ppu + sprite.pivot.x;
            float py = local.y * ppu + sprite.pivot.y;

            int w = Mathf.RoundToInt(sprite.rect.width), h = Mathf.RoundToInt(sprite.rect.height);
            if (px < 0f || py < 0f || px >= w || py >= h) return false;

            x = Mathf.Clamp((int)px, 0, w - 1);
            y = Mathf.Clamp((int)py, 0, h - 1);
            return true;
        }

        /// True when a point of the world lies on a pixel that is actually drawn by this renderer's current sprite.
        public static bool IsSolidAt(SpriteRenderer sr, Vector2 worldPoint, float alphaThreshold = 0.1f)
        {
            var sprite = sr != null ? sr.sprite : null;
            if (sprite == null || !TryPixel(sprite, sr, worldPoint, out int x, out int y)) return false;
            var alpha = AlphaOf(sprite);
            if (alpha == null) return false;
            var size = s_size[sprite.GetInstanceID()];
            int i = y * size.x + x;
            return i >= 0 && i < alpha.Length && alpha[i] >= Mathf.RoundToInt(alphaThreshold * 255f);
        }

        /// Do two drawn sprites touch: is there a point where both have a drawn pixel? Walks the rectangle the two
        /// bounds share at the finer of the two pixel sizes, so cost follows the overlap, not the sprites. On a touch,
        /// <paramref name="contact"/> carries exactly the shared pixels (see <see cref="SpriteContact"/>: where they are, how many, the middle, and a sprite of just them). Pixels below <paramref name="ignoreBelowWorldY"/> are not looked at (a ground shadow drawn into the sprite is not a body).
        public static bool Overlap(SpriteRenderer a, SpriteRenderer b, out SpriteContact contact, float alphaThreshold = 0.1f, float ignoreBelowWorldY = float.NegativeInfinity)
        {
            contact = null;
            if (a == null || b == null || a.sprite == null || b.sprite == null) return false;
            Bounds ba = a.bounds, bb = b.bounds;
            float step = Mathf.Min(PixelWorld(a), PixelWorld(b));
            // The grid sits on whole art pixels of the world, so the shared pixels read as pixels when drawn.
            float minX = Mathf.Floor(Mathf.Max(ba.min.x, bb.min.x) / step) * step, maxX = Mathf.Min(ba.max.x, bb.max.x);
            float minY = Mathf.Floor(Mathf.Max(Mathf.Max(ba.min.y, bb.min.y), ignoreBelowWorldY) / step) * step, maxY = Mathf.Min(ba.max.y, bb.max.y);
            if (minX >= maxX || minY >= maxY) return false;
            int w = Mathf.Max(1, Mathf.CeilToInt((maxX - minX) / step)), h = Mathf.Max(1, Mathf.CeilToInt((maxY - minY) / step));
            var shared = new bool[w * h];
            Vector2 sum = Vector2.zero; int pixels = 0;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    var p = new Vector2(minX + (x + .5f) * step, minY + (y + .5f) * step);
                    if (!IsSolidAt(a, p, alphaThreshold) || !IsSolidAt(b, p, alphaThreshold)) continue;
                    shared[y * w + x] = true; sum += p; pixels++;
                }
            if (pixels == 0) return false;
            contact = new SpriteContact(new Vector2(minX, minY), step, w, h, shared, sum / pixels, pixels);
            return true;
        }

        static float PixelWorld(SpriteRenderer sr) => Mathf.Max(1e-3f, Mathf.Abs(sr.transform.lossyScale.x) / Mathf.Max(1f, sr.sprite.pixelsPerUnit));

        static byte[] AlphaOf(Sprite sprite)
        {
            int id = sprite.GetInstanceID();
            if (s_alpha.TryGetValue(id, out var cached)) return cached;

            var tex = Laubrary.PreviewKit.PreviewTex.CropSprite(sprite);
            if (tex == null) { s_alpha[id] = null; return null; }

            var px = tex.GetPixels32();
            var a = new byte[px.Length];
            for (int i = 0; i < px.Length; i++) a[i] = px[i].a;

            s_alpha[id] = a;
            s_size[id] = new Vector2Int(tex.width, tex.height);
            Object.DestroyImmediate(tex);
            return a;
        }
    }
}
