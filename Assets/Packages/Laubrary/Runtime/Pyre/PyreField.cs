// PyreField — the reusable SCALAR-FIELD SUBSTRATE for Pyre's stateless "Coalesce" render modes.
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
using Laubrary.SpriteFx;
using UnityEngine;

namespace Laubrary.Pyre
{
    // One particle contributed to a scalar field. Position is CANVAS-CENTRE-RELATIVE (origin = the canvas centre),
    // matching the space the field-pass samples in (off = (x+0.5−cx, y+0.5−cy)); radius is the metaball influence
    // radius in px; weight is the particle's own-life alpha (its contribution to the field, = FusionCircle.weight in
    // Pyre1). RenderSwarm's collect-loop fills a List<FieldParticle> when a layer coalesces, then hands it to a pass.
    public struct FieldParticle
    {
        public float x, y;     // centre-relative position, px
        public float radius;   // metaball influence radius, px
        public float weight;   // field weight (own-life alpha)
        public FieldParticle(float x, float y, float radius, float weight)
        {
            this.x = x; this.y = y; this.radius = radius; this.weight = weight;
        }
    }

    // One particle contributed to the Ramp (HeightBalls) field-pass — the sibling of FieldParticle for slice 2's
    // heavier three-field pass. Unlike Fuse, Ramp needs TWO extra per-particle scalars beyond radius: `density`
    // (a ball's MASS/body) and `heat` (its height/ENERGY — how far up the smoke→fire ramp it sits), each an own-
    // life envelope value; `alpha` is its own-life opacity (blended per pixel so a dying ball can't drag a solid
    // one down). Position is CANVAS-CENTRE-RELATIVE, matching FieldParticle. RenderSwarm's collect-loop fills a
    // List<RampParticle> when a layer coalesces by Ramp, then hands it to RenderPlusRampField.
    public struct RampParticle
    {
        public float x, y;              // centre-relative position, px
        public float radius;            // dome influence radius, px
        public float density, heat;     // per-particle field weights (mass, height/energy) at own life
        public float alpha;             // own-life opacity (the coverage-blend weight)
        public RampParticle(float x, float y, float radius, float density, float heat, float alpha)
        {
            this.x = x; this.y = y; this.radius = radius;
            this.density = density; this.heat = heat; this.alpha = alpha;
        }
    }

    public static class PyreField
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

        // ── Ramp (HeightBalls) substrate (slice 2) — SmoothMax dome accumulate + slope-relief lighting ───────────
        // Capability 3 in PYREPLUS_ADVANCED_DESIGN.md decomposes Pyre1's HeightBalls into a swarm + a heavier
        // FIELD-PASS. Where Fuse SUMS a single-weight metaball kernel, Ramp fuses DOMES (s = √(1−q), 1 at a ball's
        // centre → 0 at its rim) by a SMOOTH-MAX so neighbouring balls MELT into one lumpy mass rather than the
        // taller simply winning, and it builds THREE such fields (density / heat / height). These primitives are the
        // shared, geometry-agnostic pieces Ramp's RenderPlusRampField and slice 4's matte HEIGHTMAP both build on
        // (so neither hand-rolls the melt math). Ported near-verbatim from BlastRenderer.RenderHeightBalls
        // (Runtime/Pyre/BlastRenderer.cs:1763) — SmoothMax (:1543), the Pass-1 dome accumulation (:1844-1857) and
        // the Pass-2 relief lighting (:1892-1913).

        // Blends toward max(a,b) with a soft knee of width k — the "fusion" that melts neighbouring domes into one
        // mass. k ≤ 0 ⇒ a hard Max. Verbatim from BlastRenderer.SmoothMax. General (no HeightBalls knowledge).
        public static float SmoothMax(float a, float b, float k)
        {
            if (k <= 0.0001f) return Mathf.Max(a, b);
            float h = Mathf.Clamp01(0.5f + 0.5f * (b - a) / k);
            return Mathf.Lerp(a, b, h) + k * h * (1f - h);
        }

        // Accumulate ONE scalar field from a set of circular DOMES fused by SmoothMax. Each dome that covers a
        // pixel contributes s·weight where s = √(1−q) is a soft dome (1 at centre → 0 at rim) and q = d²/r²,
        // OPTIONALLY scaled per pixel by `rimInv2` (a shared surface-noise field that makes neighbouring domes
        // bulge/pinch TOGETHER — the boiling-mass rim; null = plain circles). `field` is caller-owned length W*H,
        // CLEARED to 0 and rebuilt in full; (cx,cy) is the canvas centre; positions are centre-relative; `knee` is
        // the SmoothMax width. A pixel no dome reaches stays exactly 0 (SmoothMax is not the identity on two zeroes,
        // so absent domes must never fold in — matched by only touching a pixel a dome actually covers). This is the
        // general float[]-level "SmoothMax accumulate into a float[] field" Ramp's three fields and slice 4's matte
        // heightmap share — no per-pixel geometry warp (matches Accumulate above), no colour, no modifier knowledge.
        public static void AccumulateDomes(float[] field, int W, int H, List<FieldParticle> domes, float knee,
                                           float cx, float cy, float[] rimInv2 = null)
        {
            int n = domes.Count;
            for (int y = 0; y < H; y++)
            {
                float oy = (y + 0.5f) - cy;
                int row = y * W;
                for (int x = 0; x < W; x++)
                {
                    int idx = row + x;
                    float ox = (x + 0.5f) - cx;
                    float inv = rimInv2 != null ? rimInv2[idx] : 1f;
                    float acc = 0f;
                    for (int i = 0; i < n; i++)
                    {
                        var c = domes[i];
                        if (c.weight <= 0f) continue;
                        float r2 = c.radius * c.radius;
                        if (r2 <= 0f) continue;
                        float dx = ox - c.x, dy = oy - c.y;
                        float q = (dx * dx + dy * dy) / r2 * inv;
                        if (q >= 1f) continue;
                        float s = Mathf.Sqrt(1f - q);          // soft dome: 1 at the centre → 0 at the rim
                        acc = SmoothMax(acc, s * c.weight, knee);
                    }
                    field[idx] = acc;                          // 0 where no dome covers (acc never left 0)
                }
            }
        }

        // Relief lighting from a HEIGHT field's local slope: a normal built from finite differences, dotted (Lambert)
        // with a light at `lightAngleDeg` in the screen plane (elevation baked as `lz`). Fills `light` (length W*H):
        // pixels with height ≤ 0 get 0; elsewhere light = ambient + max(0, n·L)·gain. `relief` scales the slope so
        // taller features catch more light. Verbatim from BlastRenderer.RenderHeightBalls Pass 2 (defaults are its
        // exact 0.18 / 0.82 / 0.72 constants). General — any float[] height field can be lit by it.
        public static void ReliefLight(float[] light, float[] height, int W, int H, float lightAngleDeg, float relief,
                                       float ambient = 0.18f, float gain = 0.82f, float lz = 0.72f)
        {
            float ang = lightAngleDeg * Mathf.Deg2Rad;
            float lx = Mathf.Cos(ang), ly = Mathf.Sin(ang);
            float ll = Mathf.Sqrt(lx * lx + ly * ly + lz * lz);
            lx /= ll; ly /= ll; float lzn = lz / ll;
            float rel = Mathf.Max(0.01f, relief);
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int i = y * W + x;
                    if (height[i] <= 0f) { light[i] = 0f; continue; }
                    float hl = height[y * W + Mathf.Max(0, x - 1)], hr = height[y * W + Mathf.Min(W - 1, x + 1)];
                    float hd = height[Mathf.Max(0, y - 1) * W + x], hu = height[Mathf.Min(H - 1, y + 1) * W + x];
                    float nx = -(hr - hl) * rel, ny = -(hu - hd) * rel, nz = 1f;
                    float nl = Mathf.Sqrt(nx * nx + ny * ny + nz * nz);
                    light[i] = ambient + Mathf.Max(0f, (nx * lx + ny * ly + nz * lzn) / nl) * gain;
                }
        }

        // A small deterministic value-noise, 0..1 — a bilinear (smoothstepped) hash lattice plus a second octave.
        // Self-contained because Pyre's PyreNoise is `internal` and unreachable across the asmdef boundary; the
        // shape mirrors PyreNoise.ValueNoise closely (a coherent roughness, not a bit-match). Ramp's surface-noise
        // rim samples it; general enough for any coherent spatial roughness a field-pass wants. Seeded ⇒ a Coalesce
        // layer bakes/scrubs identically.
        public static float Noise01(float x, float y, int seed)
        {
            float a = ValueNoise(x, y, seed);
            float b = ValueNoise(x * 2.13f, y * 2.13f, seed ^ 0x7F4A7C15);
            return Mathf.Clamp01(a * 0.65f + b * 0.35f);
        }

        static float ValueNoise(float x, float y, int seed)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float tx = x - x0, ty = y - y0;
            tx = tx * tx * (3f - 2f * tx); ty = ty * ty * (3f - 2f * ty);
            float h00 = Hash01(seed, x0, y0), h10 = Hash01(seed, x0 + 1, y0);
            float h01 = Hash01(seed, x0, y0 + 1), h11 = Hash01(seed, x0 + 1, y0 + 1);
            return Mathf.Lerp(Mathf.Lerp(h00, h10, tx), Mathf.Lerp(h01, h11, tx), ty);
        }

        // FNV-1a over three ints → a stable 0..1 draw (the same funnel PyreRenderer.Hash uses, mapped to a float).
        static float Hash01(int a, int b, int c)
        {
            unchecked
            {
                uint h = 2166136261u;
                h = (h ^ (uint)a) * 16777619u;
                h = (h ^ (uint)b) * 16777619u;
                h = (h ^ (uint)c) * 16777619u;
                return (h & 0xFFFFFFu) / (float)0x1000000;
            }
        }
    }
}
