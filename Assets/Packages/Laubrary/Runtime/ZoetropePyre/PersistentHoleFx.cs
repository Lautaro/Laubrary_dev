using System.Collections.Generic;
using UnityEngine;
using Laubrary.Zoetrope;
using Laubrary.SpriteFx;
using Laubrary.Chunks;
using ChunksFx = Laubrary.Chunks.Chunks;   // the class is shadowed by the namespace inside a Laubrary.* namespace
using PyreAsset = Laubrary.Pyre.Pyre;       // ditto

namespace Laubrary.ZoetropePyre
{
    /// <summary>
    /// A Zoe-event effect that punches a lasting hole into the character's own sprite at the resolved hit
    /// point, and keeps it there — not a timed flash, a scar for the rest of that character's life (until
    /// death/revive). Add it to a Hit reaction's effect list alongside <see cref="SpawnChunkFx"/> (the flying
    /// bullet-hole debris) rather than replacing it: the two answer different halves of "the bullet hit here" —
    /// SpawnChunkFx throws pixels OFF the character, this one marks where they came FROM. This effect can ALSO
    /// throw its own debris (<see cref="ejectAsParticles"/>), cut from the exact punched region rather than a
    /// random sample of the whole sprite, if a single Hit entry should carry both halves itself.
    ///
    /// Mechanism: gets-or-adds a <see cref="SpriteFxFilter"/> on the hit renderer's own GameObject and drives it
    /// with one <see cref="BulletHoleModifier"/>, played for an effectively unbounded duration (SpriteFxFilter
    /// already re-reads whatever sprite is CURRENTLY showing every tick, so this rides correctly on top of the
    /// character's ongoing animation with no per-pose authoring needed).
    ///
    /// ⚠ KNOWN LIMITATION, disclosed rather than hidden: <see cref="Zoetrope.ReactionFxPlayer.PlayBodyFx"/>
    /// already reuses a SpriteFxFilter on the SAME renderer for a reaction's own body-flash effect (Hurt tint,
    /// etc.), and SpriteFxFilter's own contract is ONE owner of the renderer's sprite per component — a second,
    /// independent Play() on either filter would fight the other over whose output the renderer shows, and a
    /// flash's own Play()/Restore() cycle would blank this effect's hole for the duration of the flash. This
    /// component does not attempt to resolve that composition — it targets a filter of its own, separate from
    /// whatever PlayBodyFx is using at the moment it fires, and DOES NOT wait for or coordinate with the body-fx
    /// system at all. Confirmed live and correct stand-alone (T-0405/T-0406); combining it cleanly with a
    /// character that ALSO gets flashed on every hit still needs real design work this component does not
    /// attempt.
    /// </summary>
    [System.Serializable]
    public class PersistentHoleFx : IEffect
    {
        [Header("Hole")]
        [Tooltip("How far the hole reaches, in HALF-FRAME units (the same space BulletHoleModifier itself " +
                 "uses) — 1 would reach the sprite's shorter-axis edge. Read as \"the actual bullet's size\": " +
                 "Size range below multiplies THIS value, so author it to look like one bullet's own hole. On " +
                 "a small pixel-art sprite this needs to be large enough to cover at least 2-3 real pixels of " +
                 "radius, or it rounds away to nothing visible — confirmed live: 0.16 on a 17x16 sprite was a " +
                 "genuine, measurable alpha hole that was still functionally invisible at ~1px radius.")]
        [Range(0.02f, 1f)] public float radius = 0.35f;

        [Range(0f, 1f)]
        [Tooltip("How soft/torn the hole's edge reads, as a fraction of its own radius.")]
        public float edgeSoftness = 0.35f;

        [Tooltip("How long the hole persists, in seconds. A very large number (the default) reads as " +
                 "'for the rest of this character's life' without needing SpriteFxFilter to grow a genuine " +
                 "infinite-duration mode of its own — Revive() naturally clears it the same way it clears " +
                 "every other body-fx state, since a revived character's ReactionFxPlayer/SpriteFxFilter " +
                 "reset on OnRevived.")]
        public float lifeSeconds = 1_000_000f;

        [Header("Placement")]
        [Tooltip("How far the hole's centre may land from the exact hit point, in world units — a fresh random " +
                 "point is picked uniformly inside this radius around the hit each time. 0 (default) = always " +
                 "exactly on the hit point. The picked centre is clamped to the renderer's own bounds afterward, " +
                 "so the hole always lands ON the sprite even when the jitter radius itself would have carried " +
                 "it past the edge — only the CENTRE is clamped, so the hole's own radius can still bleed past " +
                 "the silhouette exactly as an un-jittered hole near an edge already can.")]
        [Min(0f)] public float placementJitter = 0f;

        [Header("Size range")]
        [Tooltip("Random multiplier applied to Radius each time this fires, picked uniformly between X and Y. " +
                 "(1,1) — the default — always uses exactly Radius (\"identical to the actual bullet\"); (1,2) " +
                 "ranges from the bullet's own size up to double.")]
        public Vector2 sizeMultiplierRange = new Vector2(1f, 1f);

        [Header("Eject as particles")]
        [Tooltip("Also throw the punched-out pixels off as flying Chunks debris, cut from the exact hole " +
                 "region — not a random sample of the whole sprite — instead of just leaving a hole behind.")]
        public bool ejectAsParticles = false;

        [Tooltip("Chunks recipe the ejected pixels are thrown with. Its debris is tinted from colours sampled " +
                 "out of the punched region itself, and a 'Sampled visual' Debris Scatter piece in the recipe " +
                 "cuts its own shapes from that same region (via the burst's sample-source override) rather " +
                 "than its authored sample source — so the recipe's OWN colours/art are what shows unless it " +
                 "opts into sampling.")]
        public ChunkSpec ejectChunks;

        [Tooltip("How many colours to sample from the punched region for the ejected debris' tint palette.")]
        [Min(1)] public int ejectSampleCount = 4;

        [Header("Pyre")]
        [Tooltip("Optional Pyre blast played once, at the hole, the moment it's punched.")]
        public PyreAsset holePyre;
        [Tooltip("Playback speed for the hole's Pyre blast.")]
        public float holePyreFps = 24f;
        [Tooltip("Uniform scale for the hole's Pyre blast.")]
        public float holePyreScale = 1f;
        [Tooltip("Sorting order for the hole's Pyre blast sprite.")]
        public int holePyreSortingOrder = 10;

        public bool IsEmpty => radius <= 0f;

        public void Apply(EventContext ctx)
        {
            if (ctx.Renderer == null) return;
            var renderer = ctx.Renderer;

            // WHERE: the hit point, optionally jittered within a disc around it, then clamped back onto the
            // renderer's own bounds so a large jitter radius can never place the hole floating off the sprite.
            Vector2 boundsCenter = renderer.bounds.center;
            Vector2 extents = renderer.bounds.extents;
            Vector2 centre = ctx.HitPosition;
            if (placementJitter > 0f)
            {
                Vector2 offset = Random.insideUnitCircle * placementJitter;
                centre += offset;
                centre.x = Mathf.Clamp(centre.x, boundsCenter.x - extents.x, boundsCenter.x + extents.x);
                centre.y = Mathf.Clamp(centre.y, boundsCenter.y - extents.y, boundsCenter.y + extents.y);
            }

            // HOW BIG: Radius scaled by a fresh roll in [min(X,Y), max(X,Y)] — (1,1) is exactly Radius, every
            // time, with no roll cost beyond the one Random.Range call.
            float sizeMul = Mathf.Approximately(sizeMultiplierRange.x, sizeMultiplierRange.y)
                ? sizeMultiplierRange.x
                : Random.Range(Mathf.Min(sizeMultiplierRange.x, sizeMultiplierRange.y),
                                Mathf.Max(sizeMultiplierRange.x, sizeMultiplierRange.y));
            float resolvedRadius = Mathf.Max(0.001f, radius * sizeMul);

            float half = Mathf.Max(0.0001f, Mathf.Min(extents.x, extents.y));
            float centerX = (centre.x - boundsCenter.x) / half;
            float centerY = (centre.y - boundsCenter.y) / half;

            // THE HOLE ITSELF. Deliberately its own BulletHoleModifier instance, not shared/pooled:
            // SpriteFxFilter.modifiers is the whole stack this filter plays, and a second hit re-placing the
            // same instance (rather than adding a new one) is the simplest reading of "the wound moves to the
            // new hit point" — stacking multiple holes from repeated hits is a real, deliberately-deferred
            // design question (does a character accumulate scars, or only ever show the latest?), not resolved
            // here.
            var go = renderer.gameObject;
            var filter = go.GetComponent<SpriteFxFilter>();
            if (filter == null) filter = go.AddComponent<SpriteFxFilter>();

            var hole = new BulletHoleModifier { radius = resolvedRadius, edgeSoftness = edgeSoftness, centerX = centerX, centerY = centerY };
            filter.modifiers = new List<PyreModifier> { hole };
            // BulletHoleModifier is a PostModifier, which SpriteFxStack.RunStack always runs through its own
            // managed Apply() branch regardless of the Burst-jobs dispatch setting (SfxKernels.IsShaped only
            // ever lists the shaped PixelModifier family) — so `dispatch` genuinely doesn't matter here. Left
            // at the component default rather than set to ForceInline, since forcing it would claim a
            // guarantee this modifier didn't actually need.
            filter.Play(Mathf.Max(1f, lifeSeconds));

            if (ejectAsParticles && ejectChunks != null) EjectParticles(renderer, centerX, centerY, resolvedRadius, centre);
            if (holePyre != null) PlayHolePyre(centre, ctx.DirectionDeg, ctx.FlipX);
        }

        /// Throws the punched pixels off as Chunks debris, cut from the exact hole region via the same
        /// half-frame→pixel math <see cref="BulletHoleModifier"/> itself uses (so a mis-sized or mis-placed
        /// crop can never happen from the two disagreeing).
        void EjectParticles(SpriteRenderer renderer, float centerX, float centerY, float radiusInHalfFrameUnits, Vector2 worldPos)
        {
            var sprite = renderer.sprite;
            if (sprite == null || sprite.texture == null) return;

            // textureRect, not rect: SpriteFxFilter/BulletHoleModifier's own "picture" is textureRect-sized
            // (see SpriteFxFilter.ReadSource), and CutRectAround documents its own coordinates the same way —
            // matching this to `rect` instead would silently misplace the crop on a packed/trimmed sprite.
            var rect = sprite.textureRect;
            int sw = Mathf.Max(1, Mathf.RoundToInt(rect.width));
            int sh = Mathf.Max(1, Mathf.RoundToInt(rect.height));
            var holePx = BulletHoleModifier.HalfFrameToPixel(sw, sh, centerX, centerY);
            float radiusPx = BulletHoleModifier.HalfFrameRadiusToPixel(sw, sh, radiusInHalfFrameUnits);
            int cropSize = Mathf.Max(2, Mathf.RoundToInt(radiusPx * 2f));

            var cut = SampledChunkSprites.CutRectAround(sprite, Mathf.RoundToInt(holePx.x), Mathf.RoundToInt(holePx.y), cropSize);
            if (cut.width < 1) return;

            List<Color32> palette = SampleCropPalette(sprite, cut, ejectSampleCount);
            Sprite crop = SampledChunkSprites.SampleAt(sprite, cut, sprite.pixelsPerUnit);

            ChunksFx.Burst(worldPos, ejectChunks, palette, sampleSourceOverride: crop);
        }

        /// A small stride-sampled palette from a cut sprite region — the same "skip near-transparent, spread
        /// evenly across the block" idea <see cref="ZoeLiveSampler"/> uses for the whole sprite, scoped down to
        /// just the punched-out crop so the ejected debris reads as coming from THIS pixel neighbourhood.
        static List<Color32> SampleCropPalette(Sprite source, RectInt cut, int count)
        {
            var result = new List<Color32>(count);
            // `cut` is in the sprite's OWN pixel space (CutRectAround's own contract); GetPixels needs full-
            // TEXTURE coordinates, so the textureRect origin has to be added back in — the exact offset
            // SampledChunkSprites.SampleAt itself applies for the same reason.
            var texRect = source.textureRect;
            int ox = Mathf.FloorToInt(texRect.x) + cut.x;
            int oy = Mathf.FloorToInt(texRect.y) + cut.y;
            Color[] pixels;
            try { pixels = source.texture.GetPixels(ox, oy, cut.width, cut.height, 0); }
            catch (UnityException) { return result; }   // texture not Read/Write enabled
            if (pixels == null || pixels.Length == 0) return result;

            int stride = Mathf.Max(1, pixels.Length / Mathf.Max(1, count * 4));
            for (int i = 0; i < pixels.Length && result.Count < count; i += stride)
            {
                if (pixels[i].a < 0.05f) continue;
                result.Add(pixels[i]);
            }
            return result;
        }

        /// Plays the optional Pyre blast at the (possibly jittered) hole position — same pooled placement
        /// SpawnPyreFx itself uses, reached through composition rather than duplicating PyreBlastPool handling
        /// a second time in this file.
        void PlayHolePyre(Vector2 worldPos, float directionDeg, bool flipX)
        {
            var spawnPyre = new SpawnPyreFx
            {
                blast = holePyre,
                blastFps = holePyreFps,
                sortingOrder = holePyreSortingOrder,
                baseScale = holePyreScale,
            };
            var pyreCtx = new EventContext { Position = worldPos, DirectionDeg = directionDeg, FlipX = flipX };
            spawnPyre.Apply(pyreCtx);
        }
    }
}
