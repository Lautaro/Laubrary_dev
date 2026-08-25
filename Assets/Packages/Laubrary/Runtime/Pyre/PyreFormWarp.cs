// PyreFormWarp — the generic geometry-modifier pass for plug-in forms (runbook step 0, T-0058).
//
// GeometryModifier is defined as an INVERSE warp of the sample position (InverseWarp: "where in the unwarped shape
// does this destination pixel read from?"). That is natural for per-pixel-sampled shapes — the Disc path folds it
// into every sample — but a field-accumulating form (Plasma Bloom, Arc Burst, ForkBlast) has no sample position to
// warp: it stamps/blurs into a buffer. So the renderer gives those forms the modifiers for free AFTER they render:
// every destination pixel of the finished layer buffer is inverse-mapped through the stack and the source buffer is
// resampled there. A form that already applies ctx.geo per sample (Inferno) overrides PyreForm.HandlesGeometry to
// opt out, otherwise it would be warped twice.
//
// Conventions deliberately match PyreInferno's per-sample loop (the reference) so a given modifier stack looks the
// same on every form:
//   • offsets are canvas pixels from the canvas centre, (px + 0.5 − W/2, py + 0.5 − H/2); row 0 = bottom so y is up
//   • GeoCtx = (W/2, H/2, centre = zero, radius = min(W,H)/2) — LAYER-centred. The Disc path warps each swarm
//     particle around its own centre; here the whole layer buffer warps once ("wobble the layer"). A per-instance
//     warp for swarms is a possible later refinement, not built.
//   • the ModSet.geo array is ascending by WarpPass and is applied in REVERSE (highest pass first), like ApplyGeo.
//   • sampling is NEAREST (floor of the warped coordinate): an identity-equivalent stack maps every pixel to itself
//     exactly, hard pixel-art edges survive, and parity planes can be warped by the very same integer map. Straight
//     alpha, RGBA moved together; a source position outside the canvas reads transparent.
using Laubrary.SpriteFx;
using UnityEngine;

namespace Laubrary.Pyre
{
    public static class PyreFormWarp
    {
        /// Build the nearest-sample source index for every destination pixel (−1 = outside the canvas). One map
        /// serves the colour buffer and every published plane of the same frame, so they stay aligned by construction.
        public static int[] BuildMap(int W, int H, GeometryModifier[] geo, float phase)
        {
            var map = new int[W * H];
            float ccx = W * 0.5f, ccy = H * 0.5f;
            var gctx = new GeoCtx(ccx, ccy, Vector2.zero, Mathf.Min(W, H) * 0.5f);
            for (int py = 0; py < H; py++)
            {
                float dy = (py + 0.5f) - ccy;
                for (int px = 0; px < W; px++)
                {
                    var off = new Vector2((px + 0.5f) - ccx, dy);
                    for (int gi = geo.Length - 1; gi >= 0; gi--) off = geo[gi].InverseWarp(off, phase, gctx);
                    int sx = Mathf.FloorToInt(off.x + ccx), sy = Mathf.FloorToInt(off.y + ccy);
                    map[py * W + px] = (uint)sx < (uint)W && (uint)sy < (uint)H ? sy * W + sx : -1;
                }
            }
            return map;
        }

        /// Warp `buf` in place through a map from BuildMap (the source is copied first, so overlapping reads are safe).
        public static void Apply(Color32[] buf, int[] map)
        {
            var src = (Color32[])buf.Clone();
            for (int i = 0; i < map.Length; i++) buf[i] = map[i] >= 0 ? src[map[i]] : default;
        }

        /// The same warp for a float plane; returns a NEW array and leaves the form's own plane untouched.
        public static float[] Apply(float[] plane, int[] map)
        {
            var outp = new float[plane.Length];
            for (int i = 0; i < map.Length; i++) outp[i] = map[i] >= 0 ? plane[map[i]] : 0f;
            return outp;
        }

        /// Convenience for tests and callers without a shared map: warp one buffer through a modifier stack.
        public static void Apply(Color32[] buf, int W, int H, GeometryModifier[] geo, float phase)
            => Apply(buf, BuildMap(W, H, geo, phase));
    }
}
