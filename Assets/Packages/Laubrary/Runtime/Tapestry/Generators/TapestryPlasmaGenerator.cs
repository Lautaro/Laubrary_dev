// TapestryPlasmaGenerator — glowing energy/plasma, ported from Kiln's "Tapestry Surface" project
// (agents/plasma/gen.py, generation 4 — "soft light"). Source idea: an always-lit emissive body whose
// brightness is MODULATED (never replaced) by one of four soft structure fields — a low-frequency wrapped
// fbm wash, raised-cosine rings, a Worley "veil" of lit cells, or a Worley "arc" web of lit walls — plus
// fbm turbulence, a triangle-kernel soften pass so nothing has a hard edge, a short tight-weighted bloom,
// and a fitted-exposure tonemap into a 4-stop palette ramp with optional chromatic dispersion. Every noise
// primitive (wrapped_value_noise / fbm / the wrapped Worley search) is re-derived here from
// tapsurface/canvas.py's technique: hashed grid corners bilinearly interpolated on a torus, so the result
// is unconditionally seamless — same guarantee every other Tapestry generator makes, no toggle.
//
// ARCHITECTURAL ADAPTATION (read before touching this file): the Kiln version was a MATERIAL painted onto
// an externally supplied height field (`sdf`) — the harness's shape. Tapestry's generator contract has no
// such input; TapestryGenCtx hands over only width/height/seed and the read-only composite below this
// layer. So this port is a standalone, FULL-CANVAS generator: every pixel gets the plasma pattern, always,
// at full opacity (alpha = 255 everywhere) — clipping this to a shape is the user's job via the layer
// stack (stack Plasma as a base layer, a Shape-type generator above it on Multiply, for an "engraved glow"
// look), never this generator's own job. Concretely, relative to gen.py:
//   * `body` (0/1 inside/outside the handed shape) is DROPPED — every pixel is "inside". The body-based
//     halo mask (HALO_INSIDE) collapses from a spatial mask to a constant scalar, kept as one for parity.
//   * `edge`/rim glow (rimGain/rimTight) is REMOVED outright. It read brightness off the shape's own
//     zero-crossing tent — a shape boundary that does not exist here. Inventing a fake edge to keep the
//     dial would be worse than admitting the term has no full-canvas equivalent, so it is gone along with
//     its two params. This is the one piece of gen 4's silhouette-reading energy term that could not be
//     meaningfully re-expressed without a shape.
//   * `depth` (signed pseudo-distance to the shape's silhouette, built as a box-blur ladder of `body`) is
//     the one piece of shape-reading machinery `ring` mode structurally needs. Rather than fake it against
//     a nonexistent boundary, Ring mode grows its OWN silhouette: a broad low-frequency fbm field
//     thresholded into soft blobs, fed through the exact same DEPTH_LADDER box-blur-ladder telescoping sum
//     gen.py uses. Rings still emanate from something and fade inward exactly per gen 4's `ringDecay`
//     design — the "something" is now the material's own procedural blob field instead of a shape, no
//     different in kind from how `veil`/`arc` already invent their own Worley lattice.
//   * The tangent frame's `guide` field was `blur(h) + FLOW_CURL*fbm(...)`; with no `h` it is FLOW_CURL
//     scaled fbm alone. `wash`, `veil`, `arc`, turbulence, soften, normalise, halo, and the fitted-exposure
//     percentile solve (now taken over the WHOLE buffer rather than "pixels where body>0.5", since that
//     is now every pixel) all port directly with no shape dependency at all.
//   * The ramp's alpha channel (gen 4 was deliberately "glass": alpha from the ramp so a draw composited
//     over whatever shape sat below it) is IGNORED. Tapestry generators are opaque by construction —
//     alpha-cutout is a layer-compositing concern, never a generator's own — so only the ramp's RGB is
//     used and alpha is forced to 255 unconditionally.
//   * The ramp itself is sampled directly per pixel (palette stops converted to linear once, then
//     lerped/converted back to sRGB per sample) rather than pre-baked into a 256-entry LUT — mathematically
//     the same curve gen.py's bake_lut encodes, just without the quantisation step, since a LUT was purely
//     a vectorisation trick numpy needed and a scalar C# loop does not.
using System;
using UnityEngine;

namespace Laubrary.Tapestry
{
    public enum TapestryPlasmaStructure { Wash, Ring, Veil, Arc }
    public enum TapestryPlasmaPalette { Ion, Arc, Toxic, Solar, Magenta, Aqua }

    [TapestryGeneratorInfo("Plasma", "Surface")]
    [Serializable]
    public class TapestryPlasmaGenerator : TapestryGenerator
    {
        // -- colour -----------------------------------------------------------------------------------
        public TapestryPlasmaPalette palette = TapestryPlasmaPalette.Ion;
        [Range(0.10f, 0.60f)] public float coreWhite = 0.35f;
        [Range(0.75f, 1.35f)] public float exposure = 1.0f;
        [Range(0.80f, 1.40f)] public float rampGamma = 1.0f;
        [Range(0f, 0.7f)] public float dispersePx = 0f;

        // -- structure: which field modulates the always-lit body -------------------------------------
        public TapestryPlasmaStructure structure = TapestryPlasmaStructure.Wash;
        [Range(0.55f, 1.00f)] public float bodyLight = 0.7f;
        [Range(0.40f, 0.90f)] public float structDepth = 0.35f;
        [Range(0.8f, 2.4f)] public float softenPx = 1.6f;
        [Range(1f, 2.6f)] public float anis = 1.6f;

        [Range(4, 10)] public int cellCount = 7;            // veil + arc lattice
        [Range(1.2f, 4.0f)] public float arcWidth = 2.5f;    // veil gutter width / arc wall width, in px
        [Range(0f, 1f)] public float branch = 0.5f;          // arc: strength of a second, coarser web octave
        [Range(9f, 26f)] public float ringPitch = 16f;       // ring
        [Range(0f, 1f)] public float ringDecay = 0.5f;       // ring: how hard the rings fade inward

        // -- energy -------------------------------------------------------------------------------------
        [Range(0.20f, 0.60f)] public float haloGain = 0.4f;
        [Range(2, 5)] public int haloRadius = 3;

        // -- texture ------------------------------------------------------------------------------------
        [Range(3, 9)] public int turbScale = 5;
        [Range(2, 5)] public int turbOctaves = 4;
        [Range(0.15f, 0.70f)] public float turbAmount = 0.45f;
        [Range(0f, 0.12f)] public float warpAmount = 0.05f;

        public int seed = 0;

        public override string DisplayName => "Plasma";
        public override string Description =>
            "Glowing energy: an always-lit emissive body modulated by a soft wash / raised-cosine rings / "
            + "a Worley veil of lit cells / a Worley arc web of lit walls, with fbm turbulence, a "
            + "no-hard-edges soften pass, a short bloom and a fitted-exposure palette ramp. Full-canvas and "
            + "always opaque — stack a Shape generator above on Multiply to clip it to a silhouette.";

        // fixed constants, ported 1:1 from gen.py's module-level constants of the same intent
        const int FlowCells = 4;
        const float FlowCurl = 0.30f;
        const int WarpCells = 3;
        const int BranchMult = 2;
        const int WashCellDiv = 2;
        const float RingRipple = 0.22f;
        const float RingFade = 3.0f;
        const float HaloInside = 0.35f;
        const float HaloSat = 5.0f;
        const float BloomW0 = 0.62f, BloomW1 = 0.28f, BloomW2 = 0.10f;
        const float ExpoTopT = 0.96f;
        const float ExpoPct = 96f;
        const float StructClip = 2.4f;
        static readonly int[] DepthLadder = { 1, 2, 3, 4, 6, 8, 11, 16, 22, 32, 45, 64 };

        public override void Generate(in TapestryGenCtx ctx, Color32[] target)
        {
            int W = ctx.width, H = ctx.height;
            int combinedSeed = seed + ctx.seed;
            float s = W / 128f;
            int n = W * H;

            // 1. tangent frame -- perp(grad(guide)), guide = FLOW_CURL * fbm (the blur(h) term gen.py mixed
            //    in has no equivalent here: there is no handed height field to blur).
            float[] guide = Fbm(W, H, FlowCells, 2, combinedSeed + 30011);
            for (int i = 0; i < n; i++) guide[i] *= FlowCurl;
            ComputeTangent(guide, W, H, out float[] tx, out float[] ty);

            // 2. domain warp (only needed by veil/arc, which read x,y directly)
            float[] wx = null, wy = null;
            if (warpAmount > 1e-4f)
            {
                wx = Fbm(W, H, WarpCells, 2, combinedSeed + 7919);
                wy = Fbm(W, H, WarpCells, 2, combinedSeed + 15013);
                for (int i = 0; i < n; i++) { wx[i] -= 0.5f; wy[i] -= 0.5f; }
            }

            // 3. structure precompute -----------------------------------------------------------------
            int cells = Mathf.Max(2, cellCount);
            float wpx = Mathf.Max(0.3f, arcWidth);
            float wcell = wpx / W * cells;
            float aEffVeil = 1.0f + 0.5f * (anis - 1.0f);
            float aEffArc = anis;

            float[] broad = null, vein = null;
            if (structure == TapestryPlasmaStructure.Wash)
            {
                broad = Fbm(W, H, Mathf.Max(2, turbScale / WashCellDiv), 3, combinedSeed + 9109);
                vein = Fbm(W, H, Mathf.Max(2, turbScale), 3, combinedSeed + 21787);
            }

            float[] depth = null, ripple = null;
            float pitch = Mathf.Max(3f, ringPitch * s);
            if (structure == TapestryPlasmaStructure.Ring)
            {
                // Ring mode needs its own "silhouette" to measure distance to. Grow one: threshold a
                // broad, low-frequency fbm field into soft blobs, then run gen.py's exact box-blur-ladder
                // telescoping sum against that mask instead of an externally-handed shape.
                float[] broadBody = Fbm(W, H, Mathf.Max(2, turbScale), 3, combinedSeed + 5503);
                var bodyMask = new float[n];
                for (int i = 0; i < n; i++) bodyMask[i] = Smoothstep(0.4f, 0.6f, broadBody[i]);

                depth = new float[n];
                float prevR = 0f;
                for (int li = 0; li < DepthLadder.Length; li++)
                {
                    float rp = DepthLadder[li] * s;
                    int br = Mathf.Max(1, Mathf.RoundToInt(rp));
                    float[] blurred = BoxBlurWrap(bodyMask, W, H, br);
                    float weight = rp - prevR;
                    for (int i = 0; i < n; i++) depth[i] += weight * (2f * blurred[i] - 1f);
                    prevR = rp;
                }
                ripple = Fbm(W, H, Mathf.Max(2, turbScale), 3, combinedSeed + 5501);
            }

            // 4. turbulence field (always needed -- every mode is modulated by it in stage 4 below)
            float[] turb = Fbm(W, H, Mathf.Max(2, turbScale), Mathf.Max(1, turbOctaves), combinedSeed);

            // 5. main structure loop --------------------------------------------------------------------
            var structArr = new float[n];
            for (int y = 0; y < H; y++)
            {
                for (int x = 0; x < W; x++)
                {
                    int idx = y * W + x;
                    float val;
                    switch (structure)
                    {
                        case TapestryPlasmaStructure.Wash:
                        {
                            float broadV = broad[idx];
                            float veinV = 1f - Mathf.Abs(2f * vein[idx] - 1f);
                            val = 0.55f * broadV + 0.45f * veinV;
                            break;
                        }
                        case TapestryPlasmaStructure.Ring:
                        {
                            float d = depth[idx];
                            float rip = (ripple[idx] - 0.5f) * pitch * RingRipple;
                            float uu = (d + rip) / pitch;
                            float crest = 0.5f - 0.5f * Mathf.Cos(2f * Mathf.PI * uu);
                            float amp = Mathf.Exp(-ringDecay * Mathf.Max(d, 0f) / (pitch * RingFade));
                            val = 0.5f + (crest - 0.5f) * amp;
                            break;
                        }
                        case TapestryPlasmaStructure.Veil:
                        {
                            float u = (x + 0.5f) / W, v = (y + 0.5f) / H;
                            if (wx != null) { u += wx[idx] * warpAmount * 2f; v += wy[idx] * warpAmount * 2f; }
                            float gx = u * cells, gy = v * cells;
                            Worley(gx, gy, cells, combinedSeed, 41, tx[idx], ty[idx], aEffVeil, out float f1, out float f2);
                            int cxi = Wrap(Mathf.FloorToInt(gx), cells);
                            int cyi = Wrap(Mathf.FloorToInt(gy), cells);
                            float pane = HashCell(cxi, cyi, combinedSeed, 601);
                            val = Smoothstep(0f, wcell * 4f, f2 - f1) * (0.55f + 0.45f * pane);
                            break;
                        }
                        default: // Arc
                        {
                            float u = (x + 0.5f) / W, v = (y + 0.5f) / H;
                            if (wx != null) { u += wx[idx] * warpAmount * 2f; v += wy[idx] * warpAmount * 2f; }
                            float gx = u * cells, gy = v * cells;
                            Worley(gx, gy, cells, combinedSeed, 41, tx[idx], ty[idx], aEffArc, out float f1, out float f2);
                            val = Mathf.Exp(-Mathf.Pow(Mathf.Abs(f2 - f1) / Mathf.Max(wcell, 1e-4f), 1.4f));
                            if (branch > 1e-3f)
                            {
                                int c2 = cells * BranchMult;
                                float w2 = wpx / W * c2;
                                Worley(u * c2, v * c2, c2, combinedSeed, 77, tx[idx], ty[idx], aEffArc, out float b1, out float b2);
                                float branchVal = branch * Mathf.Exp(-Mathf.Pow(Mathf.Abs(b2 - b1) / Mathf.Max(w2, 1e-4f), 1.4f));
                                val = Mathf.Max(val, branchVal);
                            }
                            break;
                        }
                    }
                    structArr[idx] = Mathf.Clamp01(val);
                }
            }

            // 6. turbulence: no two rings/panes/veins equally lit
            for (int i = 0; i < n; i++)
                structArr[i] = Mathf.Clamp(structArr[i] * (1f - turbAmount + turbAmount * (turb[i] * 1.6f)), 0f, 1.6f);

            // 7. soften -- the anti-harshness pass, two wrapped box blurs (a triangle kernel) of everything
            int softenR = Mathf.Max(1, Mathf.RoundToInt(softenPx * s));
            structArr = Soften(structArr, W, H, softenR);

            // 8. normalise to mean 1 (mass = mean(body) = 1 here, since every pixel is "inside")
            float meanStruct = Mean(structArr);
            float normScale = 1f / Mathf.Max(meanStruct, 1e-3f);
            for (int i = 0; i < n; i++) structArr[i] = Mathf.Clamp(structArr[i] * normScale, 0f, StructClip);

            // 9. core -- the body is ALWAYS lit; structure only modulates it (gen 4's central inversion)
            var core = new float[n];
            for (int i = 0; i < n; i++) core[i] = bodyLight * (1f - structDepth + structDepth * structArr[i]);

            // 10. halo -- a short, tight-weighted 3-scale bloom of the (clamped) lit core
            var src = new float[n];
            for (int i = 0; i < n; i++) src[i] = Mathf.Clamp01(core[i]);
            int haloR = Mathf.Max(1, haloRadius);
            float[] bloom1 = Soften(src, W, H, haloR);
            float[] bloom2 = Soften(src, W, H, haloR * 2);
            float[] bloom4 = Soften(src, W, H, haloR * 4);
            var halo = new float[n];
            for (int i = 0; i < n; i++)
            {
                float bloom = BloomW0 * bloom1[i] + BloomW1 * bloom2[i] + BloomW2 * bloom4[i];
                // HALO_INSIDE originally masked bloom OFF where body==1 (the shape's interior); with no
                // shape, every pixel is that "inside" case, so the mask collapses to a flat scalar.
                halo[i] = (1f - Mathf.Exp(-bloom * HaloSat)) * (1f - HaloInside);
            }

            // 11. energy -- rim/edge term dropped entirely (see file header): there is no shape boundary
            var E = new float[n];
            for (int i = 0; i < n; i++) E[i] = core[i] + haloGain * halo[i];

            // 12. fitted exposure -- solve the gain so the EXPO_PCT'th percentile of E lands at ramp
            //     coordinate EXPO_TOP_T. Originally taken over pixels "inside" the shape; that is every
            //     pixel here.
            float eHi = Percentile(E, ExpoPct);
            float expoGain = -Mathf.Log(1f - ExpoTopT) / Mathf.Max(eHi, 1e-4f);
            float expo = expoGain * exposure;

            // 13. shade -- exposure tonemap + soft contrast gamma
            var tArr = new float[n];
            for (int i = 0; i < n; i++)
            {
                float tt = 1f - Mathf.Exp(-Mathf.Max(E[i], 0f) * expo);
                tArr[i] = Mathf.Pow(Mathf.Clamp01(tt), rampGamma);
            }

            // 14. ramp + optional chromatic dispersion -- always fully opaque (Tapestry generators never
            //     cut their own alpha; that is a layer-compositing concern, not a generator's job)
            ComputeRampStops(palette, coreWhite, out float[] stopPos, out Color[] stopLin);

            bool disperse = dispersePx > 1e-3f;
            for (int y = 0; y < H; y++)
            {
                for (int x = 0; x < W; x++)
                {
                    int idx = y * W + x;
                    Color c;
                    if (disperse)
                    {
                        float exd = (E[Wrap(x + 1, W) + y * W] - E[Wrap(x - 1, W) + y * W]) * 0.5f;
                        float eyd = (E[x + Wrap(y + 1, H) * W] - E[x + Wrap(y - 1, H) * W]) * 0.5f;
                        float m = Mathf.Max(Mathf.Sqrt(exd * exd + eyd * eyd), 1e-9f);
                        float nx = exd / m, ny = eyd / m;
                        float tr = SampleWrapped(tArr, W, H, x + nx * dispersePx, y + ny * dispersePx);
                        float tb = SampleWrapped(tArr, W, H, x - nx * dispersePx, y - ny * dispersePx);
                        Color cr = SampleRamp(tr, stopPos, stopLin);
                        Color cg = SampleRamp(tArr[idx], stopPos, stopLin);
                        Color cb = SampleRamp(tb, stopPos, stopLin);
                        c = new Color(cr.r, cg.g, cb.b, 1f);
                    }
                    else
                    {
                        c = SampleRamp(tArr[idx], stopPos, stopLin);
                    }

                    target[idx] = new Color32(
                        (byte)Mathf.Clamp(Mathf.RoundToInt(c.r * 255f), 0, 255),
                        (byte)Mathf.Clamp(Mathf.RoundToInt(c.g * 255f), 0, 255),
                        (byte)Mathf.Clamp(Mathf.RoundToInt(c.b * 255f), 0, 255),
                        255);
                }
            }
        }

        // --------------------------------------------------------------------------------------------
        // palette ramp
        // --------------------------------------------------------------------------------------------

        static void ComputeRampStops(TapestryPlasmaPalette palette, float coreWhite, out float[] pos, out Color[] linCols)
        {
            PaletteColors(palette, out Color dark, out Color mid, out Color hot);
            Color hw = Color.Lerp(hot, Color.white, coreWhite);
            Color deep = dark * 0.55f + mid * 0.18f;

            pos = new float[] { 0.00f, 0.32f, 0.80f, 1.00f };
            var srgb = new Color[]
            {
                dark,
                new Color(Mathf.Clamp01(deep.r), Mathf.Clamp01(deep.g), Mathf.Clamp01(deep.b), 1f),
                mid,
                new Color(Mathf.Clamp01(hw.r), Mathf.Clamp01(hw.g), Mathf.Clamp01(hw.b), 1f),
            };
            linCols = new Color[4];
            for (int i = 0; i < 4; i++) linCols[i] = SrgbToLinear(srgb[i]);
        }

        static void PaletteColors(TapestryPlasmaPalette p, out Color dark, out Color mid, out Color hot)
        {
            switch (p)
            {
                case TapestryPlasmaPalette.Arc:
                    dark = Rgb255(6, 6, 16); mid = Rgb255(120, 60, 240); hot = Rgb255(235, 205, 255);
                    break;
                case TapestryPlasmaPalette.Toxic:
                    dark = Rgb255(3, 14, 8); mid = Rgb255(60, 220, 90); hot = Rgb255(220, 255, 190);
                    break;
                case TapestryPlasmaPalette.Solar:
                    dark = Rgb255(20, 6, 2); mid = Rgb255(255, 120, 20); hot = Rgb255(255, 240, 190);
                    break;
                case TapestryPlasmaPalette.Magenta:
                    dark = Rgb255(16, 3, 14); mid = Rgb255(235, 40, 160); hot = Rgb255(255, 210, 245);
                    break;
                case TapestryPlasmaPalette.Aqua:
                    dark = Rgb255(2, 16, 16); mid = Rgb255(20, 210, 190); hot = Rgb255(210, 255, 250);
                    break;
                default: // Ion
                    dark = Rgb255(4, 8, 22); mid = Rgb255(30, 110, 235); hot = Rgb255(150, 230, 255);
                    break;
            }
        }

        static Color Rgb255(int r, int g, int b) => new Color(r / 255f, g / 255f, b / 255f, 1f);

        static Color SampleRamp(float t, float[] pos, Color[] linCols)
        {
            t = Mathf.Clamp01(t);
            int i = 0;
            while (i < pos.Length - 2 && t > pos[i + 1]) i++;
            float span = pos[i + 1] - pos[i];
            float frac = span > 1e-9f ? Mathf.Clamp01((t - pos[i]) / span) : 0f;
            Color lin = Color.Lerp(linCols[i], linCols[i + 1], frac);
            Color srgb = LinearToSrgb(lin);
            return new Color(Mathf.Clamp01(srgb.r), Mathf.Clamp01(srgb.g), Mathf.Clamp01(srgb.b), 1f);
        }

        static Color SrgbToLinear(Color c) =>
            new Color(SrgbToLinearChan(c.r), SrgbToLinearChan(c.g), SrgbToLinearChan(c.b), 1f);

        static float SrgbToLinearChan(float c) =>
            c > 0.04045f ? Mathf.Pow((c + 0.055f) / 1.055f, 2.4f) : c / 12.92f;

        static Color LinearToSrgb(Color c) =>
            new Color(LinearToSrgbChan(c.r), LinearToSrgbChan(c.g), LinearToSrgbChan(c.b), 1f);

        static float LinearToSrgbChan(float c) =>
            c > 0.0031308f ? 1.055f * Mathf.Pow(Mathf.Max(c, 0f), 1f / 2.4f) - 0.055f : 12.92f * c;

        // --------------------------------------------------------------------------------------------
        // wrapped noise (re-derived from tapsurface/canvas.py: hashed grid corners, smoothstep-
        // interpolated on a torus -- NOT Mathf.PerlinNoise, which does not tile)
        // --------------------------------------------------------------------------------------------

        static float[] WrappedValueNoise(int W, int H, int cells, int seed, int salt)
        {
            cells = Mathf.Max(1, cells);
            var outp = new float[W * H];
            for (int y = 0; y < H; y++)
            {
                for (int x = 0; x < W; x++)
                {
                    float u = (x + 0.5f) / W, v = (y + 0.5f) / H;
                    float gx = u * cells, gy = v * cells;
                    float fgx = Mathf.Floor(gx), fgy = Mathf.Floor(gy);
                    int x0 = Wrap((int)fgx, cells), y0 = Wrap((int)fgy, cells);
                    int x1 = (x0 + 1) % cells, y1 = (y0 + 1) % cells;
                    float fx = gx - fgx, fy = gy - fgy;

                    float v00 = HashCell(x0, y0, seed, salt);
                    float v10 = HashCell(x1, y0, seed, salt);
                    float v01 = HashCell(x0, y1, seed, salt);
                    float v11 = HashCell(x1, y1, seed, salt);

                    float sx = fx * fx * (3f - 2f * fx);
                    float sy = fy * fy * (3f - 2f * fy);
                    float top = v00 * (1f - sx) + v10 * sx;
                    float bot = v01 * (1f - sx) + v11 * sx;
                    outp[y * W + x] = top * (1f - sy) + bot * sy;
                }
            }
            return outp;
        }

        static float[] Fbm(int W, int H, int baseCells, int octaves, int seed, float persistence = 0.5f)
        {
            var total = new float[W * H];
            float amp = 1f, ampSum = 0f;
            int cells = Mathf.Max(1, baseCells);
            int oct = Mathf.Max(1, octaves);
            for (int o = 0; o < oct; o++)
            {
                float[] noise = WrappedValueNoise(W, H, cells, seed, o);
                for (int i = 0; i < total.Length; i++) total[i] += noise[i] * amp;
                ampSum += amp;
                amp *= persistence;
                cells *= 2;
            }
            float inv = 1f / Mathf.Max(ampSum, 1e-9f);
            for (int i = 0; i < total.Length; i++) total[i] *= inv;
            return total;
        }

        static void ComputeTangent(float[] guide, int W, int H, out float[] tx, out float[] ty)
        {
            tx = new float[W * H];
            ty = new float[W * H];
            for (int y = 0; y < H; y++)
            {
                for (int x = 0; x < W; x++)
                {
                    int idx = y * W + x;
                    float gxp = guide[Wrap(x + 1, W) + y * W];
                    float gxm = guide[Wrap(x - 1, W) + y * W];
                    float gyp = guide[x + Wrap(y + 1, H) * W];
                    float gym = guide[x + Wrap(y - 1, H) * W];
                    float gx = (gxp - gxm) * 0.5f;
                    float gy = (gyp - gym) * 0.5f;
                    float tX = -gy, tY = gx;
                    float m = Mathf.Max(Mathf.Sqrt(tX * tX + tY * tY), 1e-9f);
                    tx[idx] = tX / m;
                    ty[idx] = tY / m;
                }
            }
        }

        // wrapped, optionally anisotropic Worley distances (F1 = nearest, F2 = second nearest), in cell
        // units. gx,gy are already scaled into cell units by the caller.
        static void Worley(float gx, float gy, int cells, int seed, int salt, float tanX, float tanY, float anisAmt,
            out float f1, out float f2)
        {
            float ix = Mathf.Floor(gx), iy = Mathf.Floor(gy);
            f1 = 1e9f; f2 = 1e9f;
            for (int oy = -1; oy <= 1; oy++)
            {
                for (int ox = -1; ox <= 1; ox++)
                {
                    float bx = ix + ox, by = iy + oy;
                    int cx = Wrap((int)bx, cells), cy = Wrap((int)by, cells);
                    float jx = HashCell(cx, cy, seed, salt);
                    float jy = HashCell(cx, cy, seed, salt + 1);
                    float dx = gx - (bx + jx);
                    float dy = gy - (by + jy);

                    float d;
                    if (anisAmt > 1.0001f)
                    {
                        float dt = dx * tanX + dy * tanY;
                        float dn = -dx * tanY + dy * tanX;
                        float sdt = dt / anisAmt, sdn = dn * anisAmt;
                        d = Mathf.Sqrt(sdt * sdt + sdn * sdn);
                    }
                    else
                    {
                        d = Mathf.Sqrt(dx * dx + dy * dy);
                    }

                    f2 = Mathf.Min(f2, Mathf.Max(f1, d));
                    f1 = Mathf.Min(f1, d);
                }
            }
        }

        // --------------------------------------------------------------------------------------------
        // wrapped box blur (separable, O(1)-per-pixel rolling sum) -- BoxBlurWrap is one true 2D pass;
        // Soften is two of them (a triangle kernel, C1 continuous, gen 4's anti-harshness pass)
        // --------------------------------------------------------------------------------------------

        static float[] BoxBlurWrap(float[] src, int W, int H, int r)
        {
            if (r < 1) return (float[])src.Clone();
            float[] tmp = BlurAxis(src, W, H, r, true);
            return BlurAxis(tmp, W, H, r, false);
        }

        static float[] Soften(float[] src, int W, int H, int r) => BoxBlurWrap(BoxBlurWrap(src, W, H, r), W, H, r);

        static float[] BlurAxis(float[] src, int W, int H, int r, bool horizontal)
        {
            var dst = new float[W * H];
            int n = horizontal ? W : H;
            int rr = Mathf.Max(1, Mathf.Min(r, Mathf.Max(1, n / 2)));
            int window = 2 * rr + 1;

            if (horizontal)
            {
                for (int y = 0; y < H; y++)
                {
                    int rowBase = y * W;
                    float sum = 0f;
                    for (int k = -rr; k <= rr; k++) sum += src[rowBase + Wrap(k, W)];
                    for (int x = 0; x < W; x++)
                    {
                        dst[rowBase + x] = sum / window;
                        sum -= src[rowBase + Wrap(x - rr, W)];
                        sum += src[rowBase + Wrap(x + rr + 1, W)];
                    }
                }
            }
            else
            {
                for (int x = 0; x < W; x++)
                {
                    float sum = 0f;
                    for (int k = -rr; k <= rr; k++) sum += src[x + Wrap(k, H) * W];
                    for (int y = 0; y < H; y++)
                    {
                        dst[x + y * W] = sum / window;
                        sum -= src[x + Wrap(y - rr, H) * W];
                        sum += src[x + Wrap(y + rr + 1, H) * W];
                    }
                }
            }
            return dst;
        }

        static float SampleWrapped(float[] f, int W, int H, float px, float py)
        {
            int x0 = Mathf.FloorToInt(px);
            int y0 = Mathf.FloorToInt(py);
            float fx = px - x0;
            float fy = py - y0;
            int x0w = Wrap(x0, W), y0w = Wrap(y0, H);
            int x1w = Wrap(x0 + 1, W), y1w = Wrap(y0 + 1, H);
            float top = f[y0w * W + x0w] * (1f - fx) + f[y0w * W + x1w] * fx;
            float bot = f[y1w * W + x0w] * (1f - fx) + f[y1w * W + x1w] * fx;
            return top * (1f - fy) + bot * fy;
        }

        // --------------------------------------------------------------------------------------------
        // small helpers
        // --------------------------------------------------------------------------------------------

        static float Mean(float[] a)
        {
            double sum = 0;
            for (int i = 0; i < a.Length; i++) sum += a[i];
            return a.Length > 0 ? (float)(sum / a.Length) : 0f;
        }

        static float Percentile(float[] data, float pct)
        {
            var sorted = (float[])data.Clone();
            Array.Sort(sorted);
            int cnt = sorted.Length;
            if (cnt == 0) return 0f;
            float rank = (pct / 100f) * (cnt - 1);
            int lo = Mathf.Clamp(Mathf.FloorToInt(rank), 0, cnt - 1);
            int hi = Mathf.Clamp(Mathf.CeilToInt(rank), 0, cnt - 1);
            if (lo == hi) return sorted[lo];
            float frac = rank - lo;
            return Mathf.Lerp(sorted[lo], sorted[hi], frac);
        }

        static float Smoothstep(float a, float b, float x)
        {
            float denom = (b - a) != 0f ? (b - a) : 1e-9f;
            float t = Mathf.Clamp01((x - a) / denom);
            return t * t * (3f - 2f * t);
        }

        static int Wrap(int v, int n) => ((v % n) + n) % n;

        // Deterministic per-cell pseudo-random value keyed by (cx, cy, seed, salt) -- same technique (and
        // the same literal constants) as TapestryPanelsGenerator.HashCell / canvas.py's hash_cell: a
        // wrapped cell index reproduces the exact same value as its "home" cell, which is what keeps
        // every noise primitive in this file seamless.
        static float HashCell(int cx, int cy, int seed, int salt)
        {
            unchecked
            {
                int h = cx * 374761393 + cy * 668265263 + seed * 1103515245 + salt * 2032854233;
                h = (h ^ (h >> 13)) * 1274126177;
                h ^= h >> 16;
                return (h & 0x7fffffff) / (float)int.MaxValue;
            }
        }
    }
}
