// PyrePlusField — the reusable SCALAR-FIELD SUBSTRATE for PyrePlus's stateless "Coalesce" render modes.
//
// Capability 2/3 in PYREPLUS_ADVANCED_DESIGN.md decomposes Pyre1's MetaBlob (Fuse) and HeightBalls (Ramp) into a
// swarm + a stateless FIELD-PASS: after the swarm loop places its particles, the WHOLE set is read as one scalar
// field, thresholded/ramped and shaded into a single merged silhouette (instead of Over-compositing each particle).
// This file owns the small, pure primitives every such pass shares, so slice 1 (Fuse, here), slice 2 (Ramp) and the
// slice-4 matte heightmap all build on ONE substrate rather than each hand-rolling metaball math:
//
//   • FieldParticle          — the collect representation (a centre-relative circle + a field weight).
//   • Sample(parts, sx, sy)  — the compact-polynomial metaball kernel SUM at a sample point (Σ w·(1−d²/r²)²).
//   • Accumulate(field, …)   — gather that kernel into a full float[W*H] buffer (the concrete field map Ramp's
//                              slope-relief lighting and the matte heightmap both need a real buffer for).
//   • ThresholdShade(…)      — the iso-surface resolve: field → (AA-band alpha, surface→core frac), with the band
//                              capped at the threshold exactly as BlastRenderer does (so the AA lower bound can't go
//                              negative and wash the whole frame).
//
// Deliberately knows NOTHING about geometry/pixel modifiers, colour, or the buffer's clear colour — the renderer owns
// all of that and feeds Sample an already-warped point. Pure and deterministic: same particle set ⇒ same field, so a
// Coalesce layer bakes/scrubs/plays back identically (it reads only the seeded ComputeSpawns placements upstream).
// Ported near-verbatim from BlastRenderer.RenderFusedField / RenderMetaBlob (Runtime/Pyre/BlastRenderer.cs:1957).
using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.PyrePlus
{
    // One particle contributed to a scalar field. Position is CANVAS-CENTRE-RELATIVE (origin = the canvas centre),
    // matching the space the field-pass samples in (off = (x+0.5−cx, y+0.5−cy)); radius is the metaball influence
    // radius in px; weight is the particle's own-life alpha (its contribution to the field, = FusionCircle.weight in
    // Pyre1). RenderSwarm's collect-loop fills a List<FieldParticle> when a layer coalesces, then hands it to a pass.
    internal struct FieldParticle
    {
        public float x, y;     // centre-relative position, px
        public float radius;   // metaball influence radius, px
        public float weight;   // field weight (own-life alpha)
        public FieldParticle(float x, float y, float radius, float weight)
        {
            this.x = x; this.y = y; this.radius = radius; this.weight = weight;
        }
    }

    internal static class PyrePlusField
    {
        // Σ over the particle set of the compact polynomial metaball kernel: 1 at a particle's centre → 0 at its
        // radius (k = 1−d²/r², squared for a smooth C¹ falloff). `sx,sy` is the (centre-relative) sample point — the
        // renderer passes it already folded through any geometry warp. This IS the field(px) the design specifies.
        // Zero-weight / zero-radius particles are skipped so they cost nothing and never divide by zero.
        public static float Sample(List<FieldParticle> parts, float sx, float sy)
        {
            float field = 0f;
            int n = parts.Count;
            for (int i = 0; i < n; i++)
            {
                var c = parts[i];
                if (c.weight <= 0.001f) continue;
                float r2 = c.radius * c.radius;
                if (r2 <= 0f) continue;
                float dx = sx - c.x, dy = sy - c.y;
                float d2 = dx * dx + dy * dy;
                if (d2 >= r2) continue;
                float k = 1f - d2 / r2; k *= k;   // compact polynomial kernel: 1 at centre → 0 at radius
                field += c.weight * k;
            }
            return field;
        }

        // Gather the kernel field into a full W*H buffer, NO per-pixel warp — the concrete float[W*H] field map that
        // slice 2 (Ramp: needs the buffer to read local slope for relief lighting) and slice 4 (matte heightmap)
        // reuse. `field` is caller-owned, length W*H, overwritten in full; (cx,cy) is the canvas centre. Slice 1
        // (Fuse) does NOT call this — it samples per warped pixel via Sample so geometry modifiers can bend the field.
        public static void Accumulate(float[] field, int W, int H, List<FieldParticle> parts, float cx, float cy)
        {
            for (int y = 0; y < H; y++)
            {
                float oy = (y + 0.5f) - cy;
                int row = y * W;
                for (int x = 0; x < W; x++)
                    field[row + x] = Sample(parts, (x + 0.5f) - cx, oy);
            }
        }

        // The iso-surface resolve every threshold-style field-pass shares. Given a raw field value and the three
        // authored dials, decide whether the pixel is inside the surface; if so, return the AA-band coverage `alpha`
        // across the iso-surface and the surface→core `frac` (0 at the surface, 1 at the core — the gradient
        // coordinate). Mirrors BlastRenderer.RenderFusedField verbatim:
        //     threshold = max(0.02, thresholdRaw)                     — a sane floor
        //     band      = clamp(softnessRaw, 0.01, threshold)         — CAPPED at threshold so (threshold−band) ≥ 0;
        //                                                               past that the AA lower bound goes negative and
        //                                                               every pixel in the WHOLE frame contributes.
        //     if field ≤ threshold−band → outside
        //     alpha = clamp01((field−(threshold−band))/band)
        //     frac  = clamp01((field−threshold)/shadeRange)
        // shadeRange is floored to a tiny positive so a degenerate 0 can't divide-by-zero (default 1.5 unaffected).
        public static bool ThresholdShade(float field, float thresholdRaw, float softnessRaw, float shadeRangeRaw,
                                          out float alpha, out float frac)
        {
            alpha = 0f; frac = 0f;
            float threshold = Mathf.Max(0.02f, thresholdRaw);
            float band = Mathf.Clamp(softnessRaw, 0.01f, threshold);
            if (field <= threshold - band) return false;
            alpha = Mathf.Clamp01((field - (threshold - band)) / band);   // AA across the iso-surface
            if (alpha <= 0.003f) return false;
            float range = Mathf.Max(0.001f, shadeRangeRaw);
            frac = Mathf.Clamp01((field - threshold) / range);            // 0 = surface, 1 = core
            return true;
        }
    }
}
