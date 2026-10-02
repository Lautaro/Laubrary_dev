using UnityEngine;

namespace Laubrary.MetaMapper
{
    /// <summary>
    /// The SpritePixels binding: map space → world, and back. The model is pure data and world-blind; this
    /// static helper owns ALL of the world math for sprite subjects, so there is exactly one place where pivot,
    /// pixels-per-unit, transform and flipX are interpreted.
    ///
    /// <see cref="ToWorld"/> is <c>ZonedAnimationPlayer.PixelToWorld</c> lifted VERBATIM (its twin lives in
    /// <c>Runtime/Launimator/ZonedAnimationPlayer.cs</c>) — not paraphrased, so that when the player adopts the
    /// model its shim produces bit-identical world positions and the acceptance test ("Muzzle" resolves to the
    /// same Vector3 before and after) can actually pass. Keep them in step; delete the twin on adoption.
    /// </summary>
    public static class MetaMapSprite
    {
        /// <summary>
        /// Map position (subject pixels, bottom-left origin) → world, honouring sprite pixel size, pivot, PPU,
        /// transform scale/rotation and flipX.
        ///
        /// <paramref name="gridW"/>/<paramref name="gridH"/> are PixelToWorld's generalisation, preserved: pass
        /// the mask's resolution when <paramref name="mapPos"/> is in MASK-CELL units and the mask is coarser
        /// than the sprite rect (the GetMaskCells feeder path). Leave them 0 — the normal case — when mapPos is
        /// already in map units (sprite pixels), which is what every other query returns; 0 is exactly
        /// equivalent to passing gridW == rect.width, i.e. a scale factor of 1.
        /// </summary>
        public static Vector3 ToWorld(Sprite spr, Transform t, bool flipX, Vector2 mapPos, int gridW = 0, int gridH = 0)
        {
            if (spr == null) return t != null ? t.position : (Vector3)mapPos;

            float spx = gridW > 0 ? mapPos.x * (spr.rect.width / gridW) : mapPos.x;
            float spy = gridH > 0 ? mapPos.y * (spr.rect.height / gridH) : mapPos.y;
            Vector2 pivotPx = spr.pivot;                       // px from the sprite rect's bottom-left
            float ppu = spr.pixelsPerUnit <= 0f ? 16f : spr.pixelsPerUnit;
            Vector3 local = new Vector3((spx - pivotPx.x) / ppu, (spy - pivotPx.y) / ppu, 0f);
            if (flipX) local.x = -local.x;                     // SpriteRenderer.flipX mirrors about the pivot
            return t != null ? t.TransformPoint(local) : local;
        }

        /// <summary>The inverse: world → map position. The transform half of Launimator's IsMetaPainted, split
        /// out so the model can answer <see cref="MetaMapData.MaskContains"/> in map space without knowing about
        /// world space. Same gridW/gridH meaning as <see cref="ToWorld"/>.</summary>
        public static Vector2 ToMap(Sprite spr, Transform t, bool flipX, Vector3 world, int gridW = 0, int gridH = 0)
        {
            if (spr == null) return default;

            Vector3 local = t != null ? t.InverseTransformPoint(world) : world;
            if (flipX) local.x = -local.x;
            float ppu = spr.pixelsPerUnit <= 0f ? 16f : spr.pixelsPerUnit;
            Vector2 pivotPx = spr.pivot;
            float spx = local.x * ppu + pivotPx.x;
            float spy = local.y * ppu + pivotPx.y;
            return new Vector2(gridW > 0 ? spx / (spr.rect.width / gridW) : spx,
                               gridH > 0 ? spy / (spr.rect.height / gridH) : spy);
        }

        /// <summary>A facing → world, rotated and mirrored but NOT translated. Under flipX the direction
        /// mirrors with the geometry — the physically-correct default; a consumer that wants mirror-invariant
        /// facing normalises that away itself (consumer policy, not map data).</summary>
        public static Vector3 ToWorldDirection(Transform t, bool flipX, Vector2 dir)
        {
            Vector3 local = new Vector3(flipX ? -dir.x : dir.x, dir.y, 0f);
            Vector3 world = t != null ? t.TransformDirection(local) : local;   // direction: ignores scale, as it should
            return world.sqrMagnitude > 0f ? world.normalized : Vector3.zero;
        }
    }
}
