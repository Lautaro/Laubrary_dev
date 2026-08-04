using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace Laubrary.SpriteFx
{
    // ── Burst-shaped stateless per-pixel colour/mask kernels (slice #46) ────────────────────────────────────────
    //
    // The SAME per-pixel math is written ONCE here, as static blittable kernels, and driven two ways:
    //   • MANAGED, one pixel at a time — each PixelModifier's ApplyPixel builds its param struct and calls the
    //     matching kernel, so the baked/preview output stays byte-identical to before (a pure math extraction).
    //   • BURST, a whole Color32 buffer at once — SfxStackJob (an IJobParallelFor) selects the kernel by an
    //     enum tag (no managed virtual dispatch) and runs a small resolved stack over the buffer. RunInline runs
    //     that identical stack managed-side, so INLINE == JOB byte-for-byte.
    //
    // The two conversions the design (PYREPLUS_ADVANCED_DESIGN.md → runtime & Burst) calls for:
    //   1. managed virtual dispatch → blittable kernels: a PyreModifier subclass' ApplyPixel becomes a static
    //      kernel over a blittable param struct resolved once per frame from the modifier's already-Prepared floats.
    //   2. per-pixel Gradient.Evaluate → a main-thread NativeArray<Color32>[256] LUT (SpriteFxLut) indexed in the
    //      job. Only shaped modifiers whose gradient index is an INPUT field (Tint's crossFrac) need this; the
    //      managed bake path keeps the live Gradient.Evaluate for byte-identity, the LUT is the runtime path.
    //
    // In scope (pure per-pixel, no buffer-neighbour gather): Tint, Contrast, Brightness, Saturation, Posterize,
    // OrderedDither, LayerDissolve, AlphaMask. Deferred: VoronoiCrack (its gradient index is COMPUTED inside the
    // kernel, not a pre-sampleable input field), all GeometryModifiers (interwoven with rasterisation), all
    // PostModifiers (Bloom/Outline/Dissolve/… read neighbours).

    /// Which shaped kernel an <see cref="SfxOp"/> runs. Byte-tagged for a compact blittable stack.
    /// APPEND-ONLY (a byte tag packed into a resolved op) — never reorder.
    public enum SfxKernel : byte
    {
        Tint, Contrast, Brightness, Saturation, Posterize, OrderedDither, LayerDissolve, AlphaMask,
        ColorTint, ColorReplace
    }

    // ── blittable param structs (resolved once per frame from a modifier's Prepared float fields) ────────────────
    public struct TintP { public float tr, tg, tb, amt; public int hasCross; }
    public struct ScalarP { public float v; }                                   // Contrast / Brightness / Saturation
    public struct PosterizeP { public int levels, affectAlpha; }
    public struct DitherP { public float amt; }
    public struct DissolveP { public float amt, smooth; public int mode; }      // mode: 0=Erase 1=Scatter
    public struct MaskP
    {
        public int shape;                                                       // MaskShape as int
        public float prog, siz, rotRad, driftX, driftY, sharpness, offsetX, offsetY, noiseWarp;
        public float strength;                                                  // how much alpha the mask removes
        public int fadeMode;                                                    // 0=Edge 1=Solid 2=Directional
        public float fadeAngleRad;                                              // Directional only: the fade's axis
        public float biteX, biteR;                                              // Crescent only: the bite disc
    }
    /// The colour a pixel is washed TOWARD (as opposed to Tint's multiply), and how far it travels.
    public struct ColorTintP { public float r, g, b, amt; }
    /// One hue-band replacement: match a hue band, then rewrite its hue / saturation / brightness.
    // hlMix/hlTarget drive the authoring HIGHLIGHT: matched pixels pulse toward white and then black, so the
    // area a hue band actually catches is unmistakable. Both are resolved once per frame (the cycle is uniform
    // across the picture), so the kernel only has to blend.
    public struct ReplaceP { public float hue, range, smooth, amt, bri, sat, outHue, spread, hlMix, hlTarget; }

    /// One resolved modifier in a stack — a tag plus every kernel's params inline (only the tagged one is read).
    /// Fully unmanaged so it lives in a NativeArray and crosses into Burst. `lutIndex` selects this op's 256-entry
    /// slice of the shared LUT array (−1 = none).
    public struct SfxOp
    {
        public SfxKernel kind;
        public int lutIndex;
        public TintP tint;
        public ScalarP scalar;
        public PosterizeP poster;
        public DitherP dither;
        public DissolveP dissolve;
        public MaskP mask;
        public ColorTintP ctint;
        public ReplaceP replace;
    }

    /// The shared kernels + the per-pixel builder + the stack runner. Every method is Burst-legal (pure math, no
    /// managed state): the SfxStackJob's [BurstCompile] compiles this whole call tree, while each modifier's
    /// ApplyPixel calls the identical code managed-side (no separate [BurstCompile] entry needed here).
    public static class SfxKernels
    {
        // Enum int values, mirrored so the kernels never depend on managed enum boxing.
        public const int MaskDiscOut = 0, MaskDiscIn = 1, MaskSwipeH = 2, MaskSwipeV = 3, MaskWedge = 4, MaskNoise = 5;
        public const int MaskTriangle = 6, MaskSquare = 7, MaskCrescent = 8;
        public const int FadeEdge = 0, FadeSolid = 1, FadeDirectional = 2;
        public const int DissolveScatter = 1;

        public static byte ToByte(float v) => (byte)(Mathf.Clamp01(v) * 255f + 0.5f);

        /// Deterministic per-pixel context for the RUNTIME job/inline path over a bare Color32 buffer: a radial
        /// crossFrac (Sqrt is IEEE-exact so it is bit-identical Burst==Mono), a hashed per-pixel seed, wx/wy at
        /// pixel centres. Both the job and RunInline build it here so their PixelInfo is identical by construction.
        public static PixelInfo MakePixel(int x, int y, int W, int H, int frame, float life, int seed)
        {
            float wx = x + 0.5f, wy = y + 0.5f;
            float halfW = W * 0.5f, halfH = H * 0.5f;
            float dx = wx - halfW, dy = wy - halfH;
            float unit = Mathf.Max(1f, Mathf.Min(halfW, halfH));
            float crossFrac = Mathf.Clamp01(Mathf.Sqrt(dx * dx + dy * dy) / unit);
            int hash = unchecked((int)(Sfx.Hash01(seed, x, y) * 2147483647f));
            return new PixelInfo(x, y, wx, wy, frame, crossFrac, life, hash, W, H);
        }

        // ── the eight kernels (verbatim math of each modifier's ApplyPixel body) ─────────────────────────────────

        public static bool KTint(in TintP pp, Color grad, ref Color c, ref float a)
        {
            c = new Color(c.r * pp.tr, c.g * pp.tg, c.b * pp.tb, c.a);
            if (pp.amt > 0.001f && pp.hasCross != 0)
                c = new Color(c.r * Mathf.Lerp(1f, grad.r, pp.amt), c.g * Mathf.Lerp(1f, grad.g, pp.amt),
                              c.b * Mathf.Lerp(1f, grad.b, pp.amt), c.a);
            return true;
        }

        public static bool KContrast(in ScalarP pp, ref Color c, ref float a)
        {
            float v = pp.v;
            c = new Color(Mathf.Clamp01((c.r - 0.5f) * v + 0.5f), Mathf.Clamp01((c.g - 0.5f) * v + 0.5f),
                          Mathf.Clamp01((c.b - 0.5f) * v + 0.5f), c.a);
            return true;
        }

        public static bool KBrightness(in ScalarP pp, ref Color c, ref float a)
        {
            float v = pp.v;
            c = new Color(Mathf.Clamp01(c.r * v), Mathf.Clamp01(c.g * v), Mathf.Clamp01(c.b * v), c.a);
            return true;
        }

        public static bool KSaturation(in ScalarP pp, ref Color c, ref float a)
        {
            float v = pp.v;
            float lum = c.r * 0.299f + c.g * 0.587f + c.b * 0.114f;
            c = new Color(Mathf.Clamp01(Mathf.Lerp(lum, c.r, v)), Mathf.Clamp01(Mathf.Lerp(lum, c.g, v)),
                          Mathf.Clamp01(Mathf.Lerp(lum, c.b, v)), c.a);
            return true;
        }

        public static bool KPosterize(in PosterizeP pp, ref Color c, ref float a)
        {
            int n = Mathf.Max(2, pp.levels);
            float step = 1f / (n - 1);
            c = new Color(Quant(c.r, step), Quant(c.g, step), Quant(c.b, step), c.a);
            if (pp.affectAlpha != 0) a = Quant(a, step);
            return true;
        }
        static float Quant(float v, float step) => Mathf.Clamp01(Mathf.Round(Mathf.Clamp01(v) / step) * step);

        public static bool KOrderedDither(in DitherP pp, ref Color c, ref float a, in PixelInfo p)
        {
            if (pp.amt <= 0.001f) return true;
            float threshold = (Bayer4x4((p.y & 3) * 4 + (p.x & 3)) + 0.5f) / 16f;
            float hard = a >= threshold ? 1f : 0f;
            a = Mathf.Lerp(a, hard, pp.amt);
            return a > 0.003f;
        }
        // The exact 4x4 Bayer ordered matrix OrderedDitherModifier ships, as a Burst-legal switch (a managed
        // static array can't be read from Burst). Same values, same order.
        static float Bayer4x4(int i)
        {
            switch (i)
            {
                case 0: return 0f; case 1: return 8f; case 2: return 2f; case 3: return 10f;
                case 4: return 12f; case 5: return 4f; case 6: return 14f; case 7: return 6f;
                case 8: return 3f; case 9: return 11f; case 10: return 1f; case 11: return 9f;
                case 12: return 15f; case 13: return 7f; case 14: return 13f; default: return 5f;
            }
        }

        public static bool KLayerDissolve(in DissolveP pp, ref Color c, ref float a, in PixelInfo p)
        {
            if (pp.amt <= 0.001f) return true;
            int gx = Mathf.FloorToInt(p.wx), gy = Mathf.FloorToInt(p.wy);
            float h = pp.mode == DissolveScatter
                ? Sfx.Hash01(unchecked(p.hash ^ (p.frame * 92821)), gx, gy)
                : Sfx.Hash01(p.hash, gx, gy);
            if (h >= pp.amt) return true;
            if (pp.smooth <= 0.0001f) return false;
            float fadeSpan = Mathf.Lerp(0.02f, 0.6f, pp.smooth);
            float pastCut = pp.amt - h;
            float keep = Mathf.Clamp01(1f - pastCut / fadeSpan);
            a *= keep;
            return a > 0.003f;
        }

        public static bool KAlphaMask(in MaskP pp, ref Color col, ref float a, in PixelInfo p)
        {
            float halfW = p.W * 0.5f, halfH = p.H * 0.5f;
            float unit = Mathf.Max(1f, Mathf.Min(halfW, halfH));
            float nx = ((p.x + 0.5f) - halfW) / unit - pp.offsetX;
            float ny = ((p.y + 0.5f) - halfH) / unit - pp.offsetY;
            if (pp.rotRad != 0f)
            {
                float c = Mathf.Cos(-pp.rotRad), s = Mathf.Sin(-pp.rotRad);
                float rx = nx * c - ny * s; ny = nx * s + ny * c; nx = rx;
            }

            // How much of the mask's verdict actually lands. 1 = the mask fully removes what it covers (the
            // Alpha-mask effect's only behaviour); below 1 the mask only thins those pixels, so it can be
            // animated in and out without the shape's edge popping.
            float strength = Mathf.Clamp01(pp.strength);

            if (pp.shape == MaskWedge)
            {
                float d = Mathf.Abs(Mathf.Atan2(ny, nx));
                float half = Mathf.Clamp01(pp.prog) * Mathf.PI;
                float edge = Mathf.Max(0.0001f, (1f - pp.sharpness) * 0.4f);
                a *= Mathf.Lerp(1f, Mathf.Clamp01((d - half) / edge), strength);
                return a > 0.003f;
            }

            float field;
            switch (pp.shape)
            {
                case MaskSwipeH: field = (nx / pp.siz) * 0.5f + 0.5f; break;
                case MaskSwipeV: field = (ny / pp.siz) * 0.5f + 0.5f; break;
                case MaskNoise:
                    field = PyreNoise.Sample((nx + pp.driftX) / pp.siz, (ny + pp.driftY) / pp.siz, p.hash, pp.noiseWarp);
                    break;
                // A triangle sits apex-up and a square edge-on, the orientations a wipe is authored against;
                // `rotation` turns them from there.
                case MaskTriangle: field = PolyField(nx / pp.siz, ny / pp.siz, 3, Mathf.PI / 6f); break;
                case MaskSquare: field = PolyField(nx / pp.siz, ny / pp.siz, 4, 0f); break;
                case MaskCrescent: field = CrescentField(nx / pp.siz, ny / pp.siz, pp.biteX, pp.biteR); break;
                default: field = Mathf.Sqrt(nx * nx + ny * ny) / pp.siz; break;
            }

            float threshold = pp.shape == MaskDiscIn ? (1f - pp.prog) : pp.prog;
            float ss;
            if (pp.fadeMode == FadeSolid)
            {
                ss = field >= threshold ? 1f : 0f;
            }
            else if (pp.fadeMode == FadeDirectional)
            {
                // A hard shape cut, then a linear ramp ACROSS the shape along the chosen axis — so the reveal
                // is solid at one side and gone at the other, in whatever direction is authored.
                float u = nx * Mathf.Cos(pp.fadeAngleRad) + ny * Mathf.Sin(pp.fadeAngleRad);
                float g = Mathf.Clamp01(u / (2f * pp.siz) + 0.5f);
                ss = Mathf.Max(field >= threshold ? 1f : 0f, g);
            }
            else
            {
                float w = Mathf.Max(0.001f, (1f - pp.sharpness) * 0.5f);
                ss = Mathf.Clamp01((field - (threshold - w)) / (2f * w));
                ss = ss * ss * (3f - 2f * ss);
            }
            a *= 1f - ss * strength;
            return a > 0.003f;
        }

        /// Distance field for a regular N-gon of circumradius 1 (exactly 1 on the boundary), by folding the
        /// sample into one wedge. `baseRot` orients it. N=3 → triangle, N=4 → square; the disc is the limit and
        /// stays its own case.
        static float PolyField(float x, float y, int sides, float baseRot)
        {
            float r = Mathf.Sqrt(x * x + y * y);
            if (r <= 0.00001f) return 0f;
            float seg = (Mathf.PI * 2f) / sides;
            float ang = Mathf.Atan2(y, x) - baseRot;
            float a2 = ang - seg * Mathf.Floor(ang / seg + 0.5f);
            return r * Mathf.Cos(a2) / Mathf.Cos(seg * 0.5f);
        }

        /// A unit disc with a second disc bitten out of it, offset along +x. `biteX` is how far the bite sits
        /// from the centre (0 swallows the whole disc), `biteR` its radius (thicker bite = thinner crescent).
        static float CrescentField(float x, float y, float biteX, float biteR)
        {
            float d1 = Mathf.Sqrt(x * x + y * y);
            float dx = x - biteX;
            float d2 = Mathf.Sqrt(dx * dx + y * y) / Mathf.Max(0.01f, biteR);
            return Mathf.Max(d1, 2f - d2);   // inside the disc AND outside the bite
        }

        /// Blend the pixel TOWARD a colour. Tint can only multiply, so it can never brighten and never push a
        /// pixel to a hue it does not already contain; this is the "wash it toward red" verb.
        public static bool KColorTint(in ColorTintP pp, ref Color c, ref float a)
        {
            float t = Mathf.Clamp01(pp.amt);
            if (t <= 0.001f) return true;
            c = new Color(Mathf.Lerp(c.r, pp.r, t), Mathf.Lerp(c.g, pp.g, t), Mathf.Lerp(c.b, pp.b, t), c.a);
            return true;
        }

        /// Rewrite one band of the hue wheel: match hues within `range` of `hue` (softened by `smooth`), then
        /// move them toward `outHue` and scale their saturation / brightness, all weighted by `amt`.
        public static bool KColorReplace(in ReplaceP pp, ref Color c, ref float a)
        {
            float amt = Mathf.Clamp01(pp.amt);
            if (amt <= 0.001f) return true;
            RgbToHsv(c.r, c.g, c.b, out float h, out float s, out float v);

            float range = Mathf.Max(0.01f, pp.range);
            float d = Wrap(h * 360f - pp.hue + 180f, 360f) - 180f;   // signed shortest hue distance
            float ad = Mathf.Abs(d);
            if (ad >= range) return true;

            // `smooth` eats inward from the band's rim: 0 = a hard band edge, 1 = the match fades across the
            // whole band, so a recolour blends into neighbouring hues instead of showing a posterised seam.
            float band = Mathf.Clamp01(pp.smooth) * range;
            float w = band <= 0.0001f ? 1f : Mathf.Clamp01((range - ad) / band);
            w = w * w * (3f - 2f * w) * amt;
            if (w <= 0.001f) return true;

            // `spread` keeps each pixel's offset within the band (1) or collapses the whole band onto one flat
            // hue (0) — the replacement hue's own smoothness.
            float nh = Wrap(pp.outHue + pp.spread * d, 360f) / 360f;
            float hd = Wrap(nh - h + 0.5f, 1f) - 0.5f;   // travel the short way round the wheel
            h = Wrap(h + hd * w, 1f);
            s = Mathf.Clamp01(s * Mathf.Lerp(1f, pp.sat, w));
            v = Mathf.Clamp01(v * Mathf.Lerp(1f, pp.bri, w));
            HsvToRgb(h, s, v, out float r2, out float g2, out float b2);
            c = new Color(r2, g2, b2, c.a);

            // Authoring highlight: pulse ONLY the pixels this band actually matched, weighted by how strongly
            // it matched them — so a soft band rim reads as a soft edge on the highlight too, which is the
            // part that is otherwise impossible to judge.
            if (pp.hlMix > 0.001f)
            {
                float k = Mathf.Clamp01(w * pp.hlMix);
                c = new Color(Mathf.Lerp(c.r, pp.hlTarget, k),
                              Mathf.Lerp(c.g, pp.hlTarget, k),
                              Mathf.Lerp(c.b, pp.hlTarget, k), c.a);
            }
            return true;
        }

        /// Positive modulo, written out rather than via Mathf.Repeat so the whole kernel tree stays plainly
        /// Burst-shaped arithmetic.
        static float Wrap(float v, float m) => v - Mathf.Floor(v / m) * m;

        // Burst-legal HSV conversions (UnityEngine.Color's own helpers are managed statics). Hue is 0..1.
        public static void RgbToHsv(float r, float g, float b, out float h, out float s, out float v)
        {
            float max = Mathf.Max(r, Mathf.Max(g, b));
            float min = Mathf.Min(r, Mathf.Min(g, b));
            v = max;
            float d = max - min;
            s = max <= 0.00001f ? 0f : d / max;
            if (d <= 0.00001f) { h = 0f; return; }
            if (max == r) h = (g - b) / d;
            else if (max == g) h = 2f + (b - r) / d;
            else h = 4f + (r - g) / d;
            h /= 6f;
            if (h < 0f) h += 1f;
        }

        public static void HsvToRgb(float h, float s, float v, out float r, out float g, out float b)
        {
            float hh = Wrap(h, 1f) * 6f;
            int i = (int)hh;
            if (i > 5) i = 5;
            float f = hh - i;
            float p = v * (1f - s);
            float q = v * (1f - s * f);
            float t = v * (1f - s * (1f - f));
            switch (i)
            {
                case 0: r = v; g = t; b = p; break;
                case 1: r = q; g = v; b = p; break;
                case 2: r = p; g = v; b = t; break;
                case 3: r = p; g = q; b = v; break;
                case 4: r = t; g = p; b = v; break;
                default: r = v; g = p; b = q; break;
            }
        }

        // ── stack dispatch (enum tag, no virtual dispatch) — shared by the job and RunInline ─────────────────────

        /// Run one resolved op. `luts` supplies the baked gradient for LUT-backed ops (Tint's crossFrac gradient).
        public static bool RunOp(in SfxOp op, in NativeArray<Color32> luts, ref Color c, ref float a, in PixelInfo p)
        {
            switch (op.kind)
            {
                case SfxKernel.Tint:
                {
                    Color grad = default;
                    if (op.tint.hasCross != 0 && op.lutIndex >= 0)
                        grad = SampleLut(luts, op.lutIndex, Mathf.Clamp01(p.crossFrac));
                    return KTint(op.tint, grad, ref c, ref a);
                }
                case SfxKernel.Contrast: return KContrast(op.scalar, ref c, ref a);
                case SfxKernel.Brightness: return KBrightness(op.scalar, ref c, ref a);
                case SfxKernel.Saturation: return KSaturation(op.scalar, ref c, ref a);
                case SfxKernel.Posterize: return KPosterize(op.poster, ref c, ref a);
                case SfxKernel.OrderedDither: return KOrderedDither(op.dither, ref c, ref a, p);
                case SfxKernel.LayerDissolve: return KLayerDissolve(op.dissolve, ref c, ref a, p);
                case SfxKernel.AlphaMask: return KAlphaMask(op.mask, ref c, ref a, p);
                case SfxKernel.ColorTint: return KColorTint(op.ctint, ref c, ref a);
                case SfxKernel.ColorReplace: return KColorReplace(op.replace, ref c, ref a);
                default: return true;
            }
        }

        static Color SampleLut(in NativeArray<Color32> luts, int lutIndex, float t)
        {
            int gi = lutIndex * 256 + (int)(Mathf.Clamp01(t) * 255f + 0.5f);
            Color32 lc = luts[gi];
            return new Color(lc.r / 255f, lc.g / 255f, lc.b / 255f, lc.a / 255f);
        }

        /// Run the whole op stack for one pixel index `i`. Loads the Color32, runs each op in order (dropping the
        /// pixel outright if any op returns false), writes the Color32 back. THE single per-pixel routine both the
        /// Burst job and RunInline call — so the two paths are byte-identical by construction (only the codegen
        /// differs).
        public static void RunPixel(int i, in NativeArray<SfxOp> ops, in NativeArray<Color32> luts,
                                    ref NativeArray<Color32> pixels, int W, int H, int frame, float life, int seed)
        {
            int x = i % W, y = i / W;
            PixelInfo p = MakePixel(x, y, W, H, frame, life, seed);
            Color32 src = pixels[i];
            Color c = new Color(src.r / 255f, src.g / 255f, src.b / 255f, src.a / 255f);
            float a = src.a / 255f;
            for (int k = 0; k < ops.Length; k++)
            {
                if (!RunOp(ops[k], luts, ref c, ref a, p)) { pixels[i] = default; return; }
            }
            pixels[i] = new Color32(ToByte(c.r), ToByte(c.g), ToByte(c.b), ToByte(a));
        }
    }

    /// The Burst runtime path: applies a resolved op stack to a Color32 buffer, one pixel per work item.
    /// FloatMode.Strict (no FMA/reassociation) + CompileSynchronously keep the native codegen bit-consistent with
    /// the managed kernels AND across runs — so a byte-identity-critical filter never renders one frame on the Mono
    /// fallback and the next on native, which could differ by an ULP on a transcendental.
    [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.High, CompileSynchronously = true)]
    public struct SfxStackJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<SfxOp> ops;
        [ReadOnly] public NativeArray<Color32> luts;
        public NativeArray<Color32> pixels;
        public int W, H, frame, seed;
        public float life;

        public void Execute(int i) => SfxKernels.RunPixel(i, ops, luts, ref pixels, W, H, frame, life, seed);
    }

    /// Main-thread helper: bake a Gradient into 256 Color32 entries of a shared LUT array (or a fresh one).
    public static class SpriteFxLut
    {
        /// Fill dst[baseIndex .. baseIndex+256) with `g` sampled at i/255. Straight (non-premultiplied) Color32.
        public static void Bake(Gradient g, NativeArray<Color32> dst, int lutIndex)
        {
            int b = lutIndex * 256;
            for (int i = 0; i < 256; i++)
            {
                Color c = g != null ? g.Evaluate(i / 255f) : Color.white;
                dst[b + i] = new Color32(SfxKernels.ToByte(c.r), SfxKernels.ToByte(c.g), SfxKernels.ToByte(c.b), SfxKernels.ToByte(c.a));
            }
        }
    }

    /// Resolve a managed modifier list into a blittable op stack (+ baked LUTs) and run it either inline (managed)
    /// or as the Burst job. This is the static entry the design asks for: one resolved stack, two interchangeable
    /// runners, no math drift between them.
    public static class SpriteFxStack
    {
        /// True for the pixel modifiers this slice Burst-shaped.
        public static bool IsShaped(PyreModifier m) =>
            m is TintModifier || m is ContrastModifier || m is BrightnessModifier || m is SaturationModifier ||
            m is PosterizeModifier || m is OrderedDitherModifier || m is LayerDissolveModifier || m is AlphaMaskModifier ||
            m is ColorTintModifier || m is ColorReplaceModifier || m is WipeModifier;

        /// A life/seed eval mirroring BlastRenderer.Eval (Static / MinMax / Curve), so a resolved op reflects the
        /// same animated value the managed path would at this frame. `fieldSalt` keeps a modifier's per-field
        /// MinMax randomness independent, matching the localFieldId contract of PyreModifier.Prepare.
        public static Func<ZUIValue, int, float> LifeEval(float life, int seed, int fieldSalt = 0)
        {
            return (v, fid) =>
            {
                if (v == null) return 0f;
                switch (v.mode)
                {
                    case ZUIValue.Mode.Static: return v.staticValue;
                    case ZUIValue.Mode.MinMax:
                    {
                        var rng = new System.Random(unchecked(seed * 73856093 ^ (fieldSalt + fid) * 19349663));
                        return Mathf.Lerp(v.min, v.max, (float)rng.NextDouble());
                    }
                    // Every envelope-shaped mode samples through the value's OWN normalized evaluators, so a
                    // stack sees exactly what the control draws — including the curve's corner smoothness, which
                    // a local ZUIEnvelopeEvaluator call silently drops.
                    case ZUIValue.Mode.Curve: return v.EvaluateCurveAtNorm(life);
                    case ZUIValue.Mode.Steps: return v.EvaluateStepsAtNorm(life);
                    case ZUIValue.Mode.Oscillation: return v.EvaluateOscillationAtNorm(life);
                    default: return v.staticValue;
                }
            };
        }

        /// Prepare + pack every shaped modifier in `mods` into ops (in list order), baking any per-pixel gradient
        /// into `luts`. Non-shaped / disabled modifiers are skipped (the caller runs those on the managed path).
        public static void Resolve(IReadOnlyList<PyreModifier> mods, Func<ZUIValue, int, float> eval, Allocator alloc,
                                   out NativeArray<SfxOp> ops, out NativeArray<Color32> luts)
        {
            var list = new List<SfxOp>();
            var gradients = new List<Gradient>();

            if (mods != null)
            {
                for (int i = 0; i < mods.Count; i++)
                {
                    var m = mods[i];
                    if (m == null || !m.enabled || !IsShaped(m)) continue;
                    m.Prepare(eval);
                    // Most effects are ONE op; a few (Colour replace, which holds a whole list of hue swaps)
                    // expand into several, applied in their authored order right where the effect sits.
                    var pm = (PixelModifier)m;
                    int opCount = pm.SfxOpCount;
                    for (int k = 0; k < opCount; k++)
                    {
                        var op = pm.ResolveSfxOp(k);
                        if (op.lutIndex == -2)   // sentinel: this op wants a gradient LUT slot
                        {
                            op.lutIndex = gradients.Count;
                            gradients.Add(pm.SfxGradient());
                        }
                        list.Add(op);
                    }
                }
            }

            ops = new NativeArray<SfxOp>(list.Count, alloc);
            for (int i = 0; i < list.Count; i++) ops[i] = list[i];

            int lutCount = Mathf.Max(1, gradients.Count);   // NativeArray of length 0 is legal but keep >=1 tidy
            luts = new NativeArray<Color32>(lutCount * 256, alloc);
            for (int i = 0; i < gradients.Count; i++) SpriteFxLut.Bake(gradients[i], luts, i);
        }

        /// <summary>
        /// Run a WHOLE authored stack over a managed buffer — every effect family, in the order the author
        /// listed them.
        ///
        /// SpriteFx used to run only the eleven gather-free pixel effects, because those are the ones the
        /// Burst job can express: one work item owns one pixel and may not touch another. That left the
        /// rest of the family — every geometry warp, and every whole-frame pass like outline, bloom and drop
        /// shadow — unreachable from a sprite, which is most of what people actually want an effect to do.
        ///
        /// The dispatch keeps the fast path where it applies. Consecutive SHAPED effects are batched into
        /// one resolved op list and go through Burst exactly as before; anything else flushes the batch and
        /// runs its own pass. So a plain colour stack costs precisely what it did, and the authored order is
        /// preserved either way — which matters, because these effects do not commute.
        /// </summary>
        public static void RunStack(Color32[] px, int W, int H, IReadOnlyList<PyreModifier> mods,
                                    int frame, float life, int seed, bool useBurst)
        {
            if (px == null || px.Length == 0 || mods == null) return;
            var eval = LifeEval(life, seed);

            var batch = new List<PyreModifier>();
            void Flush()
            {
                if (batch.Count == 0) return;
                Resolve(batch, eval, Allocator.TempJob, out var ops, out var luts);
                try
                {
                    if (ops.Length > 0)
                    {
                        if (useBurst)
                        {
                            var native = new NativeArray<Color32>(px.Length, Allocator.TempJob);
                            native.CopyFrom(px);
                            Schedule(native, ops, luts, W, H, frame, life, seed).Complete();
                            native.CopyTo(px);
                            native.Dispose();
                        }
                        else RunInline(px, ops, luts, W, H, frame, life, seed);
                    }
                }
                finally
                {
                    if (ops.IsCreated) ops.Dispose();
                    if (luts.IsCreated) luts.Dispose();
                }
                batch.Clear();
            }

            for (int i = 0; i < mods.Count; i++)
            {
                var m = mods[i];
                if (m == null || !m.enabled) continue;

                if (IsShaped(m)) { batch.Add(m); continue; }

                Flush();   // everything below must see the pixels the batch produced

                switch (m)
                {
                    case GeometryModifier g:
                        m.Prepare(eval);
                        RunWarp(px, W, H, g, life);
                        break;

                    case PostModifier post:
                        // The three hooks Pyre's renderer sets before every post pass — without them a post
                        // that reads life or hashes per pixel (Dissolve) sees zero and produces nothing.
                        post.SetLife(life);
                        post.SetSeed(seed);
                        post.SetFrameIndex(frame);
                        m.Prepare(eval);
                        post.Apply(px, W, H);
                        break;

                    case PixelModifier pm:
                        // A pixel effect the Burst family does not cover (Voronoi crack, Colour remap): same
                        // per-pixel contract, run on the managed path.
                        m.Prepare(eval);
                        RunManagedPixel(px, W, H, pm, frame, life, seed);
                        break;
                }
            }
            Flush();
        }

        /// A geometry effect as a RESAMPLE. Its contract is an INVERSE map — "where did this pixel come
        /// from" — which is exactly what a destination-driven resample needs: for every output pixel, ask
        /// where it came from and fetch that. Sampling is point, not bilinear, because this is pixel art and
        /// a smoothed warp would turn crisp pixels into mush.
        ///
        /// Anything mapping from outside the buffer reads as transparent, so a warp that pulls the picture
        /// inward leaves clean empty space rather than smearing its edge pixels outward.
        static void RunWarp(Color32[] px, int W, int H, GeometryModifier g, float life)
        {
            var src = (Color32[])px.Clone();
            float hHalf = W * 0.5f, vHalf = H * 0.5f;
            var ctx = new GeoCtx(hHalf, vHalf, new Vector2(hHalf, vHalf), Mathf.Min(hHalf, vHalf));
            // The oscillation phase these warps ride, one full turn across the play-through — the same
            // convention Pyre's renderer uses (frameIndex / frameCount × 2π), so an effect authored against
            // one behaves the same on the other.
            //
            // It is derived from LIFE, not from a render-frame counter. A counter would tie the motion to
            // whatever rate the editor happened to be repainting at, and — worse — scrubbing back to the
            // same life would show a different pose every time, which makes an effect impossible to judge.
            float phase = life * Mathf.PI * 2f;

            for (int y = 0, i = 0; y < H; y++)
            {
                for (int x = 0; x < W; x++, i++)
                {
                    var off = new Vector2(x + 0.5f - hHalf, y + 0.5f - vHalf);
                    var s = g.InverseWarp(off, phase, ctx);
                    int sx = Mathf.FloorToInt(s.x + hHalf);
                    int sy = Mathf.FloorToInt(s.y + vHalf);
                    px[i] = (sx < 0 || sx >= W || sy < 0 || sy >= H) ? default : src[sy * W + sx];
                }
            }
        }

        /// One non-shaped pixel effect over the buffer, through its own ApplyPixel. Mirrors the PixelInfo the
        /// Burst path builds (MakePixel) so a modifier behaves the same whichever path it lands on.
        static void RunManagedPixel(Color32[] px, int W, int H, PixelModifier m, int frame, float life, int seed)
        {
            for (int y = 0, i = 0; y < H; y++)
            {
                for (int x = 0; x < W; x++, i++)
                {
                    Color32 s = px[i];
                    Color c = new Color(s.r / 255f, s.g / 255f, s.b / 255f, s.a / 255f);
                    float a = c.a;
                    var info = SfxKernels.MakePixel(x, y, W, H, frame, life, seed);
                    bool keep = m.ApplyPixel(ref c, ref a, info);
                    c.a = keep ? a : 0f;
                    px[i] = (Color32)c;
                }
            }
        }

        /// Run the resolved stack over a managed Color32[] using the identical per-pixel routine the Burst job runs.
        public static void RunInline(Color32[] pixels, NativeArray<SfxOp> ops, NativeArray<Color32> luts,
                                     int W, int H, int frame, float life, int seed)
        {
            var native = new NativeArray<Color32>(pixels.Length, Allocator.Temp);
            native.CopyFrom(pixels);
            for (int i = 0; i < native.Length; i++)
                SfxKernels.RunPixel(i, ops, luts, ref native, W, H, frame, life, seed);
            native.CopyTo(pixels);
            native.Dispose();
        }

        /// Schedule the Burst job over a NativeArray<Color32>. Caller owns/disposes ops, luts and pixels.
        public static JobHandle Schedule(NativeArray<Color32> pixels, NativeArray<SfxOp> ops, NativeArray<Color32> luts,
                                         int W, int H, int frame, float life, int seed, int batch = 64, JobHandle deps = default)
        {
            var job = new SfxStackJob
            {
                ops = ops, luts = luts, pixels = pixels,
                W = W, H = H, frame = frame, seed = seed, life = life
            };
            return job.Schedule(pixels.Length, Mathf.Max(1, batch), deps);
        }
    }
}
