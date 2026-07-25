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
    public enum SfxKernel : byte
    {
        Tint, Contrast, Brightness, Saturation, Posterize, OrderedDither, LayerDissolve, AlphaMask
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
    }

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
    }

    /// The shared kernels + the per-pixel builder + the stack runner. Every method is Burst-legal (pure math, no
    /// managed state): the SfxStackJob's [BurstCompile] compiles this whole call tree, while each modifier's
    /// ApplyPixel calls the identical code managed-side (no separate [BurstCompile] entry needed here).
    public static class SfxKernels
    {
        // Enum int values, mirrored so the kernels never depend on managed enum boxing.
        public const int MaskDiscOut = 0, MaskDiscIn = 1, MaskSwipeH = 2, MaskSwipeV = 3, MaskWedge = 4, MaskNoise = 5;
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

            if (pp.shape == MaskWedge)
            {
                float d = Mathf.Abs(Mathf.Atan2(ny, nx));
                float half = Mathf.Clamp01(pp.prog) * Mathf.PI;
                float edge = Mathf.Max(0.0001f, (1f - pp.sharpness) * 0.4f);
                a *= Mathf.Clamp01((d - half) / edge);
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
                default: field = Mathf.Sqrt(nx * nx + ny * ny) / pp.siz; break;
            }

            float w = Mathf.Max(0.001f, (1f - pp.sharpness) * 0.5f);
            float threshold = pp.shape == MaskDiscIn ? (1f - pp.prog) : pp.prog;
            float ss = Mathf.Clamp01((field - (threshold - w)) / (2f * w));
            ss = ss * ss * (3f - 2f * ss);
            a *= 1f - ss;
            return a > 0.003f;
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
            m is PosterizeModifier || m is OrderedDitherModifier || m is LayerDissolveModifier || m is AlphaMaskModifier;

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
                    case ZUIValue.Mode.Curve:
                        return ZUIEnvelopeEvaluator.Evaluate(v.points, Mathf.Clamp01(life), v.yMax);
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
                    var op = ((PixelModifier)m).ResolveSfxOp();
                    if (op.lutIndex == -2)   // sentinel: this op wants a gradient LUT slot
                    {
                        op.lutIndex = gradients.Count;
                        gradients.Add(((PixelModifier)m).SfxGradient());
                    }
                    list.Add(op);
                }
            }

            ops = new NativeArray<SfxOp>(list.Count, alloc);
            for (int i = 0; i < list.Count; i++) ops[i] = list[i];

            int lutCount = Mathf.Max(1, gradients.Count);   // NativeArray of length 0 is legal but keep >=1 tidy
            luts = new NativeArray<Color32>(lutCount * 256, alloc);
            for (int i = 0; i < gradients.Count; i++) SpriteFxLut.Bake(gradients[i], luts, i);
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
