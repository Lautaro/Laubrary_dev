using System;
using UnityEngine;

namespace Laubrary.SpriteFx
{
    /// Punches a soft-edged hole of transparency into a sprite, at a fixed position/radius in HALF-FRAME units
    /// (the same "sprite-centre-relative, ±1 reaches the shorter axis' edge" space RelightModifier's Point
    /// light already uses — copied deliberately, not reinvented, so an author who already knows that space
    /// reads this one for free).
    ///
    /// Written as its own effect rather than reusing WipeModifier/the old "AlphaMask" name: that kernel
    /// (SfxKernels.KAlphaMask) is a REVEAL/HIDE transition measured from a single threshold on a radial field —
    /// at any one fixed progress it can only ever split the sprite into "a disc kept, everything past it
    /// removed" or the reverse. There is no progress value that keeps everything OUTSIDE a small disc and
    /// removes only the disc itself — which is exactly what a bullet hole needs. Confirmed by reading
    /// SfxKernels.KAlphaMask directly (2026-09-22) rather than assumed from its doc comments.
    ///
    /// A PostModifier (not a per-pixel PixelModifier) for the same reason RelightModifier is one: this effect's
    /// polarity is intentionally the plain, correct one — INSIDE the radius is removed, OUTSIDE is untouched —
    /// which needs nothing more than a per-pixel distance test, no threshold-vs-shape-field indirection.
    [Serializable]
    public class BulletHoleModifier : PostModifier
    {
        [ZUIPair2D("centerY", "Hole centre")]
        [Range(-4f, 4f)]
        [Tooltip("Where the hole sits over the picture, in HALF-FRAME units: (0,0) is the centre of the frame, " +
                 "1 unit is half the frame's SHORTER side (so ±1 reaches that axis' edge; the longer axis of a " +
                 "wide/tall frame runs further out than that — the same space RelightModifier's Point light " +
                 "position uses). Set once from the hit position when this effect is triggered; not intended " +
                 "to be hand-animated.")]
        public float centerX = 0f;

        [Range(-4f, 4f)]
        [Tooltip("Vertical half-frame position of the hole's centre. See Hole centre X for the coordinate space.")]
        public float centerY = 0f;

        [Range(0.02f, 2f)]
        [Tooltip("How far the hole reaches, in the same half-frame units as its centre. Small values are a " +
                 "single-pixel-scale puncture; large ones eat a real chunk of the sprite.")]
        public float radius = 0.18f;

        [Range(0f, 1f)]
        [Tooltip("How soft the hole's edge is, as a fraction of its own radius. 0 is a hard pixel-art cut; " +
                 "higher feathers it into a ragged/torn-looking fade instead of a clean circle.")]
        public float edgeSoftness = 0.35f;

        public override string DisplayName => "Bullet hole";

        // The hole only ever REMOVES pixels that already exist — it never paints past the silhouette, so it
        // never needs the overflow margin other effects (Bloom, Relight's own glow) can ask for.
        public override int OutwardReachPx() => 0;

        float radiusV, softV;

        public override void Prepare(Func<ZUIValue, int, float> e)
        {
            // No animatable ZUIValue fields here on purpose: unlike a flash effect, this one is meant to be set
            // ONCE (centre + radius) at the moment it's triggered and then hold steady for as long as it plays —
            // an author who wants it to grow in can still animate `radius` by hand later; this ships the simple,
            // correct static case first.
            radiusV = Mathf.Max(0.001f, radius);
            softV = Mathf.Clamp01(edgeSoftness);
        }

        /// The half-frame → picture-pixel conversion this modifier's own <see cref="Apply"/> uses, exposed so a
        /// caller that needs to know WHERE the hole lands in real sprite pixels — <see cref="PersistentHoleFx"/>,
        /// to cut the ejected-particles crop from the same spot — never re-derives the formula by hand. The two
        /// must never drift apart: a hole and its "eject what came out of it" crop disagreeing on where the hole
        /// actually is would be a worse bug than either feature missing.
        public static Vector2 HalfFrameToPixel(int pictureW, int pictureH, float centerX, float centerY)
        {
            float half = Mathf.Max(1f, Mathf.Min(pictureW, pictureH) * 0.5f);
            float cx = (pictureW - 1) * 0.5f, cy = (pictureH - 1) * 0.5f;
            return new Vector2(cx + centerX * half, cy + centerY * half);
        }

        /// The half-frame → picture-pixel radius conversion, same reasoning as <see cref="HalfFrameToPixel"/>.
        public static float HalfFrameRadiusToPixel(int pictureW, int pictureH, float radius)
            => radius * Mathf.Max(1f, Mathf.Min(pictureW, pictureH) * 0.5f);

        public override void Apply(Color32[] buf, int W, int H)
        {
            if (W <= 0 || H <= 0 || buf == null || buf.Length < W * H) return;

            // Same picture-vs-padded-buffer handling as RelightModifier.Apply — normalize against the SPRITE
            // rect, never against a margin some earlier effect in the stack may have added, or the hole would
            // silently drift off the hit point the moment this effect shares a stack with an outward-reaching one.
            bool known = pictureW > 0 && pictureH > 0;
            int sw = known ? pictureW : W, sh = known ? pictureH : H;
            int ox = known ? padX : 0, oy = known ? padY : 0;

            var holePx = HalfFrameToPixel(sw, sh, centerX, centerY);
            float holePxX = ox + holePx.x, holePxY = oy + holePx.y;
            float radiusPx = HalfFrameRadiusToPixel(sw, sh, radiusV);
            float softPx = Mathf.Max(0.01f, radiusPx * softV);

            for (int y = 0, i = 0; y < H; y++)
                for (int x = 0; x < W; x++, i++)
                {
                    Color32 c = buf[i];
                    if (c.a == 0) continue;   // nothing to remove from an already-empty pixel

                    float dx = (x + 0.5f) - holePxX, dy = (y + 0.5f) - holePxY;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);

                    // d <= radiusPx - softPx  → fully inside, alpha 0 (a true hole, not just thinned).
                    // d >= radiusPx + softPx  → fully outside, untouched — this is the whole point: everywhere
                    //                           that isn't the hole is left EXACTLY as the sprite already was.
                    // in between               → linear feather, for a torn rather than laser-cut edge.
                    float keep = Mathf.Clamp01((d - (radiusPx - softPx)) / (2f * softPx));
                    if (keep >= 0.999f) continue;

                    buf[i] = new Color32(c.r, c.g, c.b, (byte)(c.a * keep));
                }
        }
    }
}
