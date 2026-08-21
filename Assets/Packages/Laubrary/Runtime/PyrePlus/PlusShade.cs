// PlusShade — field → RGBA. The four colour families the Kiln catalogue found that a Fill cannot express:
//   (a) a LUT interpolated in LINEAR light with per-stop alpha (Unity's Gradient lerps gamma values and caps at 8
//       keys; Kiln ramps have up to 10 stops and carry an opacity ceiling per stop),
//   (b) hard cel bands (a floor into N colours, no interpolation — any blending destroys the contour),
//   (c) two ramps crossfaded by a second channel (soot / smoke / cooling) and a 2-D palette grid,
//   (d) emergent additive RGB with a per-channel 1−exp(−k·L) tone map and white blow-out.
// Plus `PlusRamp`, the serialisable ramp a form can carry as a field (ZuiReflect draws it: a list of stop cards; a
// HARD band table is a `ZuiGradient` LOCKED to banded sampling instead — see ZuiGradient.LockToBands), and
// `PlusRampPresets`, where a port ships its source ramps verbatim so they are never left on a default gradient.
//
// Conventions: ramp position 0 = the COLD outer edge, 1 = the HOTTEST core (Kiln's contract convention); a LUT is
// sampled by t in 0..1. Colours are UnityEngine.Color in sRGB (what the rest of PyrePlus composites).
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.PyrePlus
{
    public enum PlusRampSpace { LinearLight, Srgb }

    /// One ramp stop: position along the ramp and its colour — the alpha channel IS the per-stop opacity ceiling.
    [Serializable]
    public sealed class PlusRampStop
    {
        [Tooltip("Where on the ramp this stop sits. 0 = the cold outer edge, 1 = the hottest core.")]
        [Range(0f, 1f)] public float pos;
        [Tooltip("The colour at this stop. Its alpha is the opacity ceiling there — a translucent rim is a stop with alpha below 1.")]
        public Color color = Color.white;

        public PlusRampStop() { }
        public PlusRampStop(float pos, Color color) { this.pos = pos; this.color = color; }
        public PlusRampStop(float pos, byte r, byte g, byte b, float a) { this.pos = pos; color = new Color(r / 255f, g / 255f, b / 255f, a); }
    }

    /// A colour ramp a form can hold as a public field (drawn by ZuiReflect as stop cards) and bake into a LUT with
    /// `PlusShade.BakeLut`. Stops need not be sorted; baking sorts a copy.
    [Serializable]
    public sealed class PlusRamp
    {
        [Tooltip("The colour stops, cold edge (0) to hottest core (1). Each stop's alpha is its opacity ceiling.")]
        public List<PlusRampStop> stops = new List<PlusRampStop>();
        [Tooltip("Linear light decodes sRGB before blending and re-encodes after (Kiln's default — mid-tones stay bright instead of going brown); sRGB blends the stored values directly, the way Unity's Gradient does.")]
        public PlusRampSpace space = PlusRampSpace.LinearLight;

        public bool IsEmpty => stops == null || stops.Count == 0;

        public PlusRamp Clone()
        {
            var c = new PlusRamp { space = space };
            if (stops != null) foreach (var s in stops) c.stops.Add(new PlusRampStop(s.pos, s.color));
            return c;
        }

        public Color Evaluate(float t) => PlusShade.EvalStops(stops, space, t);
    }

    /// A baked ramp: `size` RGBA entries, entry i at t = i/(size−1). Sample by t (nearest entry, Kiln's
    /// `idx = int(t·(size−1) + 0.5)`). Bake once (Prepare / the prepass cache), sample per pixel.
    public sealed class PlusLut
    {
        public readonly Color[] rgba;
        public readonly Color32[] rgba32;
        public int Size => rgba.Length;

        public PlusLut(Color[] entries)
        {
            rgba = entries;
            rgba32 = new Color32[entries.Length];
            for (int i = 0; i < entries.Length; i++) rgba32[i] = entries[i];
        }

        public int IndexOf(float t) { int i = (int)(Mathf.Clamp01(t) * (rgba.Length - 1) + 0.5f); return i >= rgba.Length ? rgba.Length - 1 : i; }
        public Color Sample(float t) => rgba[IndexOf(t)];
        public Color32 Sample32(float t) => rgba32[IndexOf(t)];
        public float Alpha(float t) => rgba[IndexOf(t)].a;
    }

    public static class PlusShade
    {
        // ── sRGB ↔ linear ───────────────────────────────────────────────────────────────────────────────────

        public static float SrgbToLinear(float c) => c <= 0.04045f ? c / 12.92f : Mathf.Pow((c + 0.055f) / 1.055f, 2.4f);
        public static float LinearToSrgb(float c) => c <= 0.0031308f ? c * 12.92f : 1.055f * Mathf.Pow(c, 1f / 2.4f) - 0.055f;
        public static Color ToLinear(Color c) => new Color(SrgbToLinear(c.r), SrgbToLinear(c.g), SrgbToLinear(c.b), c.a);
        public static Color ToSrgb(Color c) => new Color(LinearToSrgb(c.r), LinearToSrgb(c.g), LinearToSrgb(c.b), c.a);

        // ── (a) linear-light LUT ────────────────────────────────────────────────────────────────────────────

        /// Evaluate a stop list exactly at t: the two bracketing stops are blended in `space` (RGB), alpha always
        /// linearly. Outside the first/last stop the end stop holds. Stops may be unsorted (sorted on a copy).
        public static Color EvalStops(IList<PlusRampStop> stops, PlusRampSpace space, float t)
        {
            if (stops == null || stops.Count == 0) return Color.white;
            if (stops.Count == 1) return stops[0].color;
            var sorted = Sorted(stops);
            t = Mathf.Clamp01(t);
            if (t <= sorted[0].pos) return sorted[0].color;
            int last = sorted.Length - 1;
            if (t >= sorted[last].pos) return sorted[last].color;
            int i = 0;
            while (i < last - 1 && t > sorted[i + 1].pos) i++;
            var a = sorted[i]; var b = sorted[i + 1];
            float span = b.pos - a.pos;
            float u = span > 1e-6f ? (t - a.pos) / span : 1f;
            return Blend(a.color, b.color, u, space);
        }

        /// Blend two sRGB colours by u in `space` (RGB); alpha linear in both spaces.
        public static Color Blend(Color a, Color b, float u, PlusRampSpace space)
        {
            if (space == PlusRampSpace.Srgb) return Color.LerpUnclamped(a, b, u);
            var la = ToLinear(a); var lb = ToLinear(b);
            var l = new Color(la.r + (lb.r - la.r) * u, la.g + (lb.g - la.g) * u, la.b + (lb.b - la.b) * u, a.a + (b.a - a.a) * u);
            return ToSrgb(l);
        }

        public static PlusLut BakeLut(PlusRamp ramp, int size = 256) => BakeLut(ramp?.stops, ramp?.space ?? PlusRampSpace.LinearLight, size);

        public static PlusLut BakeLut(IList<PlusRampStop> stops, PlusRampSpace space, int size = 256)
        {
            size = Mathf.Max(2, size);
            var e = new Color[size];
            for (int i = 0; i < size; i++) e[i] = EvalStops(stops, space, i / (float)(size - 1));
            return new PlusLut(e);
        }

        /// Bake from a Unity Gradient. `linearLight` re-interpolates its keys in linear light (so a Gradient authored
        /// in the editor can still get Kiln's blending); false samples `Gradient.Evaluate` as-is.
        public static PlusLut BakeLut(Gradient g, bool linearLight, int size = 256)
        {
            if (g == null) return BakeLut((IList<PlusRampStop>)null, PlusRampSpace.Srgb, size);
            if (!linearLight)
            {
                var e = new Color[Mathf.Max(2, size)];
                for (int i = 0; i < e.Length; i++) e[i] = g.Evaluate(i / (float)(e.Length - 1));
                return new PlusLut(e);
            }
            // Colour keys carry the RGB, alpha keys the alpha — merge both key sets into stops at the union of their
            // times, each stop taking the gradient's own alpha at that time (alpha is linear in both spaces anyway).
            var times = new SortedSet<float>();
            foreach (var k in g.colorKeys) times.Add(k.time);
            foreach (var k in g.alphaKeys) times.Add(k.time);
            var stops = new List<PlusRampStop>();
            foreach (var t in times)
            {
                var c = g.Evaluate(t);   // exact at a key time (no blending happens on a key)
                stops.Add(new PlusRampStop(t, c));
            }
            return BakeLut(stops, PlusRampSpace.LinearLight, size);
        }

        /// Bake from a ZuiFill by sampling `fill.Evaluate(t, 0.5, 0.5)` — no re-interpolation (the fill decides its
        /// own blending); `reverse` samples at 1−t for fills authored hot-end-left (Inferno / Fork Blast read theirs
        /// that way).
        public static PlusLut BakeLut(ZuiFill fill, int size = 256, bool reverse = false)
        {
            size = Mathf.Max(2, size);
            var e = new Color[size];
            for (int i = 0; i < size; i++)
            {
                float t = i / (float)(size - 1);
                e[i] = fill != null ? fill.Evaluate(reverse ? 1f - t : t, 0.5f, 0.5f) : Color.white;
            }
            return new PlusLut(e);
        }

        /// Bake a ZuiGradient into a PlusLut WITHOUT a Texture2D — safe to call from a form's Prepare/Render even on
        /// a worker thread (PlusFrameFill renders frames concurrently; touching a UnityEngine.Object off the main
        /// thread is forbidden by the PlusForm contract). `ZuiGradient.ToLut` bakes to a Texture2D and must NOT be
        /// used here for exactly that reason — this samples the same `Evaluate` math directly into a plain array.
        public static PlusLut BakeLut(ZuiGradient g, int size = 256, float phase = 0f, float life = 0f)
        {
            size = Mathf.Max(2, size);
            var e = new Color[size];
            for (int i = 0; i < size; i++) e[i] = g != null ? g.Evaluate(i / (float)(size - 1), phase, life) : Color.white;
            return new PlusLut(e);
        }

        // ── (b) banded / cel ────────────────────────────────────────────────────────────────────────────────

        /// N equal bands over t in 0..1: colour index = min(floor(t·N), N−1). No interpolation. Alpha is a separate
        /// continuous term the caller multiplies in (this returns the band colour's own alpha).
        public static Color32 Banded(float t, Color32[] bands)
        {
            int n = bands.Length;
            int i = (int)(Mathf.Clamp01(t) * n);
            return bands[i >= n ? n - 1 : i];
        }

        /// Explicit thresholds (ascending): colour i is used when t ≥ thresholds[i]; below thresholds[0] → colours[0].
        public static Color32 Banded(float t, float[] thresholds, Color32[] colors)
        {
            int i = 0;
            while (i + 1 < thresholds.Length && t >= thresholds[i + 1]) i++;
            return colors[Mathf.Min(i, colors.Length - 1)];
        }

        /// Quantise t itself into `steps` levels (Kiln's `steps` dial: t = min(floor(t·steps), steps−1)/(steps−1)), so a
        /// continuous LUT becomes a cel ramp. steps ≤ 1 ⇒ t unchanged.
        public static float Quantise(float t, int steps)
        {
            if (steps <= 1) return t;
            int i = (int)(Mathf.Clamp01(t) * steps); if (i >= steps) i = steps - 1;
            return i / (float)(steps - 1);
        }

        // ── (c) dual ramp + 2-D palette ─────────────────────────────────────────────────────────────────────

        /// Hermite smoothstep of x over [lo, hi].
        public static float SmoothStep(float lo, float hi, float x)
        {
            float u = Mathf.Clamp01((x - lo) / Mathf.Max(1e-6f, hi - lo));
            return u * u * (3f - 2f * u);
        }

        /// Two LUTs sampled at t, crossfaded by a second channel `w` through a smoothstep window [lo, hi]: below lo
        /// pure A, above hi pure B. RGB blends in linear light (the contract's `rgb_lin = hot·(1−w) + soot·w`), alpha
        /// linearly — the soot / smoke / cooling family.
        public static Color DualRamp(PlusLut a, PlusLut b, float t, float w, float lo = 0f, float hi = 1f)
        {
            float k = SmoothStep(lo, hi, w);
            var ca = a.Sample(t);
            if (k <= 0f) return ca;
            var cb = b.Sample(t);
            if (k >= 1f) return cb;
            return Blend(ca, cb, k, PlusRampSpace.LinearLight);
        }

        /// A 2-D colour grid pal[a][b] sampled by two channels in 0..1 (nearest cell, no interpolation — the
        /// `palette[soot][temp]` family). `cells` is row-major [a * nb + b].
        public sealed class Palette2D
        {
            public readonly Color32[] cells;
            public readonly int na, nb;
            public Palette2D(int na, int nb, Color32[] cells)
            {
                if (cells.Length != na * nb) throw new ArgumentException("Palette2D: cells.Length must be na*nb");
                this.na = na; this.nb = nb; this.cells = cells;
            }
            public Color32 Sample(float a, float b)
            {
                int ia = (int)(Mathf.Clamp01(a) * na); if (ia >= na) ia = na - 1;
                int ib = (int)(Mathf.Clamp01(b) * nb); if (ib >= nb) ib = nb - 1;
                return cells[ia * nb + ib];
            }
        }

        // ── (d) additive emissive ───────────────────────────────────────────────────────────────────────────

        /// Per-channel tone map of an additive RGB light sample: c = 1 − exp(−k·L). `blowout` > 0 pushes a bright
        /// pixel toward white (the "hot core goes white" look): w = (1 − exp(−k·blowout·mean(L)))^3, c = lerp(c, 1, w).
        /// Returned in linear light; pass `toSrgb` = true to encode for compositing. Alpha = 1 − exp(−k·alphaGain·max(L)).
        public static Color AdditiveEmissive(float lr, float lg, float lb, float k, float blowout = 0f, float alphaGain = 1f, bool toSrgb = true)
        {
            lr = Mathf.Max(0f, lr); lg = Mathf.Max(0f, lg); lb = Mathf.Max(0f, lb);
            float r = 1f - Mathf.Exp(-k * lr), g = 1f - Mathf.Exp(-k * lg), b = 1f - Mathf.Exp(-k * lb);
            if (blowout > 0f)
            {
                float w = 1f - Mathf.Exp(-k * blowout * (lr + lg + lb) / 3f);
                w = w * w * w;
                r += (1f - r) * w; g += (1f - g) * w; b += (1f - b) * w;
            }
            float a = 1f - Mathf.Exp(-k * alphaGain * Mathf.Max(lr, Mathf.Max(lg, lb)));
            var c = new Color(r, g, b, a);
            return toSrgb ? ToSrgb(c) : c;
        }

        /// Whole-plane form of the above: three light planes → straight-alpha pixels in `target` (W*H). Pixels with
        /// no light stay untouched (so the caller's cleared scratch stays transparent there).
        public static void AdditiveEmissive(float[] lr, float[] lg, float[] lb, Color32[] target, float k, float blowout = 0f, float alphaGain = 1f)
        {
            for (int i = 0; i < target.Length; i++)
            {
                if (lr[i] <= 0f && lg[i] <= 0f && lb[i] <= 0f) continue;
                target[i] = AdditiveEmissive(lr[i], lg[i], lb[i], k, blowout, alphaGain);
            }
        }

        // ── helpers ─────────────────────────────────────────────────────────────────────────────────────────

        static PlusRampStop[] Sorted(IList<PlusRampStop> stops)
        {
            var arr = new PlusRampStop[stops.Count];
            for (int i = 0; i < arr.Length; i++) arr[i] = stops[i];
            Array.Sort(arr, (x, y) => x.pos.CompareTo(y.pos));   // stable enough: equal positions keep list order only by luck, so avoid them
            return arr;
        }
    }

    /// Ramps shipped verbatim from their Kiln source so a port never starts on a default gradient. Each returns a
    /// fresh PlusRamp (a form stores its own copy; edits never touch the preset).
    public static class PlusRampPresets
    {
        /// Kiln Flame `EMBER` (agent3_fork_explosive, gen 7 contract `ramp.json`): 10 stops, linear light, opacity
        /// ceiling 0.18 at the cold edge → 1.0 from the upper-mid tones — the brick-red edge and white-hot core of
        /// the Fork Blast detonation.
        public static PlusRamp Ember() => new PlusRamp
        {
            space = PlusRampSpace.LinearLight,
            stops =
            {
                new PlusRampStop(0.00f, 40, 4, 3, 0.18f),
                new PlusRampStop(0.09f, 104, 12, 6, 0.40f),
                new PlusRampStop(0.20f, 166, 26, 9, 0.62f),
                new PlusRampStop(0.32f, 208, 52, 12, 0.77f),
                new PlusRampStop(0.45f, 236, 88, 16, 0.87f),
                new PlusRampStop(0.58f, 250, 128, 24, 0.93f),
                new PlusRampStop(0.71f, 255, 168, 44, 0.97f),
                new PlusRampStop(0.83f, 255, 205, 92, 1.00f),
                new PlusRampStop(0.93f, 255, 234, 166, 1.00f),
                new PlusRampStop(1.00f, 255, 252, 238, 1.00f),
            }
        };

        /// EMBER's secondary `soot` ramp (same contract): what the hot ramp crossfades into as tint = T/H rises,
        /// through a smoothstep window of 0.28..0.92 (`PlusShade.DualRamp(hot, soot, t, tint, 0.28f, 0.92f)`).
        public static PlusRamp EmberSoot() => new PlusRamp
        {
            space = PlusRampSpace.LinearLight,
            stops =
            {
                new PlusRampStop(0.0f, 20, 13, 11, 0.18f),
                new PlusRampStop(0.2f, 44, 27, 19, 0.38f),
                new PlusRampStop(0.4f, 76, 47, 29, 0.58f),
                new PlusRampStop(0.6f, 110, 71, 41, 0.74f),
                new PlusRampStop(0.8f, 148, 101, 57, 0.87f),
                new PlusRampStop(1.0f, 190, 141, 88, 0.94f),
            }
        };
        public const float EmberSootLo = 0.28f, EmberSootHi = 0.92f;

        // ── Kiln Flame / agent3 gen 2 (the directional jet; gen.py TORCH / EMBER / DIRTY / GOLD / WYRM, contract ramp.json:
        // (pos, sRGB, opacity ceiling), linear light, pos 0 = the cold edge). EMBER is `Ember()` / `EmberSoot()` above —
        // the explosive fork kept it verbatim. `Jet(name)` / `JetSecondary(name)` / `JetSootWindow(name)` by Kiln name.

        /// `lance`: a gas cutting torch — indigo → blue → cyan → white, the only cool ramp that reaches white.
        public static PlusRamp JetTorch() => Stops(PlusRampSpace.LinearLight, new float[] { 0f, 0.12f, 0.26f, 0.40f, 0.55f, 0.70f, 0.84f, 1f },
            new byte[,] { { 6, 10, 62 }, { 14, 28, 132 }, { 22, 58, 190 }, { 30, 104, 232 }, { 54, 152, 250 }, { 104, 200, 255 }, { 176, 234, 255 }, { 248, 253, 255 } },
            new float[] { 0.24f, 0.48f, 0.68f, 0.82f, 0.90f, 0.95f, 0.99f, 1f });

        /// `sputter`: the low-contrast one — nine ember stops that never reach white, topping out at a pale apricot.
        public static PlusRamp JetDirty() => Stops(PlusRampSpace.LinearLight, new float[] { 0f, 0.12f, 0.26f, 0.40f, 0.55f, 0.70f, 0.84f, 0.94f, 1f },
            new byte[,] { { 28, 6, 6 }, { 66, 14, 8 }, { 112, 26, 10 }, { 154, 44, 13 }, { 188, 70, 18 }, { 214, 104, 30 }, { 234, 142, 52 }, { 245, 172, 84 }, { 252, 196, 120 } },
            new float[] { 0.20f, 0.40f, 0.58f, 0.71f, 0.81f, 0.89f, 0.94f, 0.98f, 1f });
        /// DIRTY's soot ramp (crossfade window 0.30..0.95).
        public static PlusRamp JetDirtySoot() => Stops(PlusRampSpace.LinearLight, new float[] { 0f, 0.25f, 0.50f, 0.75f, 1f },
            new byte[,] { { 16, 11, 9 }, { 38, 25, 17 }, { 66, 43, 27 }, { 98, 65, 39 }, { 132, 90, 53 } },
            new float[] { 0.20f, 0.40f, 0.58f, 0.74f, 0.86f });

        /// `whip`: reference 0135's two-tone — olive through ochre and gold to cream, no white.
        public static PlusRamp JetGold() => Stops(PlusRampSpace.LinearLight, new float[] { 0f, 0.13f, 0.28f, 0.44f, 0.60f, 0.76f, 0.90f, 1f },
            new byte[,] { { 44, 28, 8 }, { 92, 60, 13 }, { 144, 102, 22 }, { 188, 144, 36 }, { 218, 180, 60 }, { 238, 212, 108 }, { 250, 236, 166 }, { 255, 250, 216 } },
            new float[] { 0.24f, 0.48f, 0.68f, 0.82f, 0.90f, 0.96f, 1f, 1f });
        /// GOLD's second ramp — not soot but HEAT: the head of the swung stream crosses into a hot orange (window 0.20..0.85).
        public static PlusRamp JetGoldHeat() => Stops(PlusRampSpace.LinearLight, new float[] { 0f, 0.22f, 0.45f, 0.68f, 0.86f, 1f },
            new byte[,] { { 84, 20, 8 }, { 162, 44, 12 }, { 222, 88, 20 }, { 248, 146, 38 }, { 255, 196, 92 }, { 255, 236, 172 } },
            new float[] { 0.30f, 0.56f, 0.76f, 0.90f, 0.98f, 1f });

        /// `wyrm`: the coolest and most transparent — violet through periwinkle to a cyan-white.
        public static PlusRamp JetWyrm() => Stops(PlusRampSpace.LinearLight, new float[] { 0f, 0.12f, 0.26f, 0.40f, 0.55f, 0.70f, 0.84f, 0.94f, 1f },
            new byte[,] { { 20, 6, 52 }, { 44, 14, 104 }, { 76, 28, 162 }, { 104, 56, 212 }, { 120, 106, 240 }, { 128, 166, 248 }, { 152, 216, 252 }, { 200, 241, 254 }, { 246, 253, 255 } },
            new float[] { 0.18f, 0.36f, 0.54f, 0.68f, 0.80f, 0.89f, 0.95f, 0.99f, 1f });

        // ── Kiln Flame / agent3_fork_radial gen 3 (the radial jet; gen.py BURNER / WHIRL / SOLAR / GHOST — its EMBER / GOLD /
        // DIRTY are the presets above stop for stop, and its VIOLET is `JetWyrm()` stop for stop, so `Jet("VIOLET")` maps there).

        /// `crown`: A GAS BURNER — blue at the ports through sky to a blue-white; the hot ramp of the one draw whose second
        /// ramp is doing something the first cannot.
        public static PlusRamp JetBurner() => Stops(PlusRampSpace.LinearLight, new float[] { 0f, 0.12f, 0.27f, 0.43f, 0.60f, 0.78f, 0.92f, 1f },
            new byte[,] { { 4, 12, 46 }, { 10, 34, 108 }, { 16, 74, 172 }, { 26, 126, 216 }, { 58, 176, 238 }, { 128, 216, 250 }, { 196, 240, 254 }, { 245, 253, 255 } },
            new float[] { 0.22f, 0.46f, 0.66f, 0.80f, 0.90f, 0.96f, 1f, 1f });
        /// BURNER's second ramp — the ORANGE TIPS the blue gas crosses into by AGE (tint = age, not heat), window 0.18..0.88.
        public static PlusRamp JetBurnerTips() => Stops(PlusRampSpace.LinearLight, new float[] { 0f, 0.18f, 0.38f, 0.58f, 0.78f, 0.92f, 1f },
            new byte[,] { { 52, 14, 6 }, { 128, 34, 8 }, { 196, 68, 12 }, { 234, 116, 24 }, { 252, 168, 52 }, { 255, 212, 112 }, { 255, 240, 182 } },
            new float[] { 0.22f, 0.46f, 0.66f, 0.82f, 0.93f, 0.99f, 1f });

        /// `whirl`: a magenta-black bottom end (the cool envelope of a vortex reads violet, not sooty) through orange to white.
        public static PlusRamp JetWhirl() => Stops(PlusRampSpace.LinearLight, new float[] { 0f, 0.10f, 0.22f, 0.35f, 0.48f, 0.62f, 0.76f, 0.88f, 1f },
            new byte[,] { { 34, 4, 22 }, { 92, 10, 40 }, { 150, 22, 42 }, { 196, 44, 34 }, { 228, 82, 24 }, { 246, 124, 30 }, { 255, 168, 60 }, { 255, 208, 118 }, { 255, 246, 226 } },
            new float[] { 0.18f, 0.40f, 0.60f, 0.75f, 0.86f, 0.93f, 0.97f, 1f, 1f });

        /// `starburst`: high key — the dark end is short and already orange, so the tongues read as thrown light; the hottest.
        public static PlusRamp JetSolar() => Stops(PlusRampSpace.LinearLight, new float[] { 0f, 0.14f, 0.30f, 0.46f, 0.62f, 0.78f, 0.91f, 1f },
            new byte[,] { { 96, 16, 4 }, { 170, 44, 6 }, { 224, 88, 10 }, { 248, 138, 22 }, { 255, 184, 48 }, { 255, 220, 96 }, { 255, 242, 168 }, { 255, 254, 240 } },
            new float[] { 0.22f, 0.48f, 0.70f, 0.84f, 0.92f, 0.97f, 1f, 1f });

        /// `halo`: barely a fire — a desaturated teal that never saturates, with the lowest opacity ceilings in the family.
        public static PlusRamp JetGhost() => Stops(PlusRampSpace.LinearLight, new float[] { 0f, 0.14f, 0.30f, 0.46f, 0.62f, 0.78f, 0.91f, 1f },
            new byte[,] { { 8, 26, 30 }, { 14, 52, 62 }, { 24, 88, 100 }, { 44, 132, 140 }, { 86, 176, 176 }, { 146, 212, 206 }, { 200, 236, 232 }, { 240, 252, 250 } },
            new float[] { 0.12f, 0.26f, 0.40f, 0.54f, 0.66f, 0.78f, 0.88f, 0.96f });

        // ── Kiln Flame / agent3_fork_explosive gen 7 (the explosive jet; gen.py CORDITE / TOXIC — its EMBER / GOLD / DIRTY /
        // WHIRL / SOLAR / GHOST / VIOLET are the presets above stop for stop).

        /// `muzzle`: CORDITE — a near-black navy through a hard steel blue into a white with blue left in it; the top three
        /// stops packed into the last eighth because a muzzle flash is a small very hot core inside a much cooler cloud.
        public static PlusRamp JetCordite() => Stops(PlusRampSpace.LinearLight, new float[] { 0f, 0.10f, 0.24f, 0.40f, 0.56f, 0.72f, 0.86f, 1f },
            new byte[,] { { 10, 10, 34 }, { 24, 28, 82 }, { 40, 60, 140 }, { 62, 106, 190 }, { 104, 156, 226 }, { 162, 200, 244 }, { 214, 232, 252 }, { 252, 254, 255 } },
            new float[] { 0.20f, 0.44f, 0.64f, 0.78f, 0.88f, 0.95f, 0.99f, 1f });
        /// CORDITE's second ramp — the burnt-propellant ORANGE the cloud crosses into as it soots (window 0.34..0.96).
        public static PlusRamp JetCorditeSoot() => Stops(PlusRampSpace.LinearLight, new float[] { 0f, 0.25f, 0.50f, 0.75f, 1f },
            new byte[,] { { 46, 16, 8 }, { 122, 48, 14 }, { 196, 96, 26 }, { 240, 158, 60 }, { 255, 214, 140 } },
            new float[] { 0.24f, 0.52f, 0.74f, 0.90f, 1f });

        /// `starshell`: TOXIC — the widest hue travel in the family: a black-green through a poison lime to a white that still
        /// has green in it. Chemical fire, not wood fire.
        public static PlusRamp JetToxic() => Stops(PlusRampSpace.LinearLight, new float[] { 0f, 0.11f, 0.25f, 0.40f, 0.55f, 0.70f, 0.84f, 0.94f, 1f },
            new byte[,] { { 6, 26, 10 }, { 14, 62, 20 }, { 30, 112, 30 }, { 68, 162, 40 }, { 122, 202, 52 }, { 176, 230, 76 }, { 218, 246, 128 }, { 240, 252, 190 }, { 252, 255, 238 } },
            new float[] { 0.20f, 0.42f, 0.62f, 0.76f, 0.86f, 0.93f, 0.98f, 1f, 1f });

        /// The jet family's primary ramp by its Kiln name (TORCH / EMBER / DIRTY / GOLD / WYRM / VIOLET / BURNER / WHIRL /
        /// SOLAR / GHOST / CORDITE / TOXIC); null when unknown.
        public static PlusRamp Jet(string name) => name?.ToUpperInvariant() switch
        {
            "TORCH" => JetTorch(), "EMBER" => Ember(), "DIRTY" => JetDirty(), "GOLD" => JetGold(), "WYRM" => JetWyrm(), "VIOLET" => JetWyrm(),
            "BURNER" => JetBurner(), "WHIRL" => JetWhirl(), "SOLAR" => JetSolar(), "GHOST" => JetGhost(),
            "CORDITE" => JetCordite(), "TOXIC" => JetToxic(), _ => null,
        };

        /// The second ramp a jet ramp crosses into (EMBER / DIRTY / CORDITE: soot; GOLD: heat; BURNER: the orange tips); an
        /// empty ramp when it has none.
        public static PlusRamp JetSecondary(string name) => name?.ToUpperInvariant() switch
        {
            "EMBER" => EmberSoot(), "DIRTY" => JetDirtySoot(), "GOLD" => JetGoldHeat(), "BURNER" => JetBurnerTips(), "CORDITE" => JetCorditeSoot(), _ => new PlusRamp(),
        };

        /// The crossfade window (soot_lo, soot_hi) of a jet ramp's secondary; the JetSpec default (0.35, 0.95) when it has none.
        public static Vector2 JetSootWindow(string name) => name?.ToUpperInvariant() switch
        {
            "EMBER" => new Vector2(EmberSootLo, EmberSootHi), "DIRTY" => new Vector2(0.30f, 0.95f), "GOLD" => new Vector2(0.20f, 0.85f),
            "BURNER" => new Vector2(0.18f, 0.88f), "CORDITE" => new Vector2(0.34f, 0.96f), _ => new Vector2(0.35f, 0.95f),
        };

        static PlusRamp Stops(PlusRampSpace space, float[] pos, byte[,] rgb, float[] alpha)
        {
            var r = new PlusRamp { space = space };
            for (int i = 0; i < pos.Length; i++) r.stops.Add(new PlusRampStop(pos[i], rgb[i, 0], rgb[i, 1], rgb[i, 2], alpha[i]));
            return r;
        }

        // ── Kiln Energy Explosion / agent3_fork gen 5 (plasma.RAMPS, contract ramp.json: 6 stops each, sRGB lerp,
        // no per-stop alpha — that agent's alpha is a separate smoothstep on the field). Every one runs DEEP →
        // SATURATED → WHITE: the hottest part of a plasma is white whatever is burning, the hue shows in the skirts.
        static PlusRamp Plasma6(byte[,] c, float[] pos)
        {
            var r = new PlusRamp { space = PlusRampSpace.Srgb };
            for (int i = 0; i < 6; i++) r.stops.Add(new PlusRampStop(pos[i], c[i, 0], c[i, 1], c[i, 2], 1f));
            return r;
        }
        static readonly float[] PlasmaPosA = { 0f, 0.22f, 0.45f, 0.68f, 0.86f, 1f };
        public static PlusRamp PlasmaIon() => Plasma6(new byte[,] { { 26, 6, 54 }, { 86, 20, 140 }, { 186, 52, 214 }, { 255, 122, 226 }, { 255, 202, 246 }, { 255, 255, 255 } }, PlasmaPosA);
        public static PlusRamp PlasmaCryo() => Plasma6(new byte[,] { { 4, 18, 52 }, { 12, 72, 152 }, { 28, 160, 232 }, { 120, 232, 255 }, { 208, 250, 255 }, { 255, 255, 255 } }, PlasmaPosA);
        public static PlusRamp PlasmaVolt() => Plasma6(new byte[,] { { 14, 10, 60 }, { 48, 44, 190 }, { 96, 130, 255 }, { 170, 200, 255 }, { 228, 234, 255 }, { 255, 255, 255 } }, new[] { 0f, 0.24f, 0.50f, 0.72f, 0.90f, 1f });
        public static PlusRamp PlasmaToxin() => Plasma6(new byte[,] { { 4, 32, 24 }, { 16, 96, 54 }, { 64, 190, 86 }, { 158, 244, 120 }, { 228, 255, 202 }, { 255, 255, 255 } }, new[] { 0f, 0.22f, 0.46f, 0.70f, 0.88f, 1f });
        /// Crimson → magenta → white: the warm end of the set, deliberately with no orange step (orange reads as FIRE).
        public static PlusRamp PlasmaFlare() => Plasma6(new byte[,] { { 40, 2, 40 }, { 140, 14, 110 }, { 236, 52, 150 }, { 255, 130, 190 }, { 255, 214, 238 }, { 255, 255, 255 } }, new[] { 0f, 0.22f, 0.46f, 0.68f, 0.88f, 1f });
        /// The plasma palette by its Kiln name ("ion", "cryo", "volt", "toxin", "flare"); null for an unknown name.
        public static PlusRamp Plasma(string name)
        {
            switch ((name ?? "").Trim().ToLowerInvariant())
            {
                case "ion": return PlasmaIon();
                case "cryo": return PlasmaCryo();
                case "volt": return PlasmaVolt();
                case "toxin": return PlasmaToxin();
                case "flare": return PlasmaFlare();
                default: return null;
            }
        }

        // ── the Energy Projectile agent2 (THE ORB, gen 2) signatures: orbcanvas.RAMPS verbatim ──
        // Eight uneven control points per ramp, interpolated DIRECTLY IN sRGB (np.interp on 0..255 channels): the dark
        // end is long (the halo and the wake live there), the white end short (only the nucleus is white-hot). No
        // per-stop alpha — that agent's alpha is a window on the tone value, a dial of the form.
        static PlusRamp Orb8(float[] pos, byte[,] c)
        {
            var r = new PlusRamp { space = PlusRampSpace.Srgb };
            for (int i = 0; i < 8; i++) r.stops.Add(new PlusRampStop(pos[i], c[i, 0], c[i, 1], c[i, 2], 1f));
            return r;
        }
        public static PlusRamp OrbEmber() => Orb8(new[] { 0f, 0.14f, 0.32f, 0.52f, 0.70f, 0.85f, 0.94f, 1f },
            new byte[,] { { 26, 4, 2 }, { 86, 14, 4 }, { 172, 42, 8 }, { 232, 96, 18 }, { 252, 158, 44 }, { 255, 208, 108 }, { 255, 238, 178 }, { 255, 252, 232 } });
        public static PlusRamp OrbFrost() => Orb8(new[] { 0f, 0.16f, 0.36f, 0.56f, 0.74f, 0.88f, 0.96f, 1f },
            new byte[,] { { 6, 18, 40 }, { 12, 46, 92 }, { 22, 98, 164 }, { 52, 158, 220 }, { 118, 208, 246 }, { 186, 236, 252 }, { 226, 248, 255 }, { 250, 254, 255 } });
        public static PlusRamp OrbGold() => Orb8(new[] { 0f, 0.15f, 0.34f, 0.54f, 0.72f, 0.86f, 0.95f, 1f },
            new byte[,] { { 32, 12, 2 }, { 104, 40, 4 }, { 186, 92, 10 }, { 238, 148, 26 }, { 254, 200, 66 }, { 255, 232, 134 }, { 255, 248, 200 }, { 255, 255, 246 } });
        public static PlusRamp OrbToxin() => Orb8(new[] { 0f, 0.16f, 0.36f, 0.56f, 0.74f, 0.88f, 0.96f, 1f },
            new byte[,] { { 6, 24, 14 }, { 16, 62, 28 }, { 28, 118, 44 }, { 72, 176, 56 }, { 140, 220, 78 }, { 198, 244, 132 }, { 232, 252, 198 }, { 250, 255, 238 } });
        public static PlusRamp OrbVolt() => Orb8(new[] { 0f, 0.15f, 0.34f, 0.54f, 0.72f, 0.86f, 0.95f, 1f },
            new byte[,] { { 20, 6, 44 }, { 60, 14, 108 }, { 114, 30, 186 }, { 172, 62, 236 }, { 212, 118, 250 }, { 236, 176, 254 }, { 250, 222, 255 }, { 255, 246, 255 } });
        /// The orb signature by its Kiln name ("ember", "frost", "gold", "toxin", "volt"); null for an unknown name.
        public static PlusRamp Orb(string name)
        {
            switch ((name ?? "").Trim().ToLowerInvariant())
            {
                case "ember": return OrbEmber();
                case "frost": return OrbFrost();
                case "gold": return OrbGold();
                case "toxin": return OrbToxin();
                case "volt": return OrbVolt();
                default: return null;
            }
        }

        // ── the Flame agent2 (the GROUNDED flame, gen 2) banded palettes: gen.py RAMP_* as the contract writes them ──
        // NOT gradients: seven hard cel bands. A band starts at its threshold (in heat-field units) and runs to the next;
        // the contract stores positions as threshold / top threshold with each step as a REPEATED position (the band
        // before it, then the band starting there), sRGB, alpha 1 — reproduced here verbatim. Below the first
        // threshold the first colour applies. The form keeps the top threshold (`rampTop`) as its own dial.
        static PlusRamp Torch7(float[] th, byte[,] c)
        {
            var r = new PlusRamp { space = PlusRampSpace.Srgb };
            float top = th[th.Length - 1];
            r.stops.Add(new PlusRampStop(0f, c[0, 0], c[0, 1], c[0, 2], 1f));
            for (int k = 1; k < th.Length; k++)
            {
                float p = th[k] / top;
                r.stops.Add(new PlusRampStop(p, c[k - 1, 0], c[k - 1, 1], c[k - 1, 2], 1f));
                r.stops.Add(new PlusRampStop(p, c[k, 0], c[k, 1], c[k, 2], 1f));
            }
            r.stops.Add(new PlusRampStop(1f, c[th.Length - 1, 0], c[th.Length - 1, 1], c[th.Length - 1, 2], 1f));
            return r;
        }
        /// RAMP_HOT (draw `lash`, top 0.88): hot orange-red.
        public static PlusRamp TorchHot() => Torch7(new[] { 0.055f, 0.13f, 0.24f, 0.38f, 0.54f, 0.72f, 0.88f },
            new byte[,] { { 118, 22, 10 }, { 188, 48, 12 }, { 232, 96, 18 }, { 250, 150, 32 }, { 255, 200, 72 }, { 255, 236, 152 }, { 255, 252, 234 } });
        /// RAMP_EMBER (draw `emberbed`, top 0.82): the dark end carries most of the range, so the bed reads as coals.
        public static PlusRamp TorchEmber() => Torch7(new[] { 0.04f, 0.10f, 0.19f, 0.31f, 0.46f, 0.63f, 0.82f },
            new byte[,] { { 92, 12, 6 }, { 150, 28, 8 }, { 198, 60, 12 }, { 232, 104, 22 }, { 248, 152, 46 }, { 254, 202, 104 }, { 255, 238, 186 } });
        /// RAMP_WHITE (draw `surge`, top 1.12): pale, white-hot at the core — the top stop sits ABOVE the field's peak.
        public static PlusRamp TorchWhite() => Torch7(new[] { 0.05f, 0.14f, 0.28f, 0.45f, 0.65f, 0.88f, 1.12f },
            new byte[,] { { 128, 34, 14 }, { 196, 66, 16 }, { 238, 118, 24 }, { 252, 172, 48 }, { 255, 216, 104 }, { 255, 242, 182 }, { 255, 255, 250 } });
        /// RAMP_RIM (draw `barbs`, top 1.15): 0115's scheme — a wide red rim, a yellow body, a white spine.
        public static PlusRamp TorchRim() => Torch7(new[] { 0.05f, 0.16f, 0.30f, 0.46f, 0.66f, 0.90f, 1.15f },
            new byte[,] { { 150, 16, 16 }, { 214, 40, 18 }, { 240, 92, 20 }, { 250, 148, 26 }, { 254, 202, 44 }, { 255, 238, 128 }, { 255, 255, 236 } });
        /// RAMP_GOLD (draw `curl`, top 1.18): gold / amber for the rolling lobes.
        public static PlusRamp TorchGold() => Torch7(new[] { 0.05f, 0.11f, 0.21f, 0.36f, 0.55f, 0.80f, 1.18f },
            new byte[,] { { 112, 30, 8 }, { 192, 78, 16 }, { 228, 124, 28 }, { 246, 172, 56 }, { 252, 210, 104 }, { 255, 236, 168 }, { 255, 250, 230 } });
        /// The top threshold (heat-field units) of each torch palette — what the form's `rampTop` dial holds.
        public static float TorchTop(string name)
        {
            switch (Short(name))
            {
                case "hot": return 0.88f; case "ember": return 0.82f; case "white": return 1.12f;
                case "rim": return 1.15f; case "gold": return 1.18f; default: return 0f;
            }
        }
        /// The torch palette by its Kiln name as a locked ZuiGradient (the band palette the form holds): the step
        /// stops' REPEATED positions collapsed to one key per band (see CollapseSteps), banded via LockToBands so
        /// it can never sample smooth. Positions/colours are identical anchors to the old PlusBands table — only
        /// the storage changed. Null for an unknown name.
        public static ZuiGradient TorchGradient(string name)
        {
            var r = Torch(name);
            if (r == null) return null;
            var stops = CollapseSteps(r);
            var g = new ZuiGradient { gradient = StopsToGradient(stops) };
            g.LockToBands(stops.Count);
            return g;
        }
        /// Sort by position and collapse repeated positions to the LATER stop (the band that STARTS there) — the
        /// same rule the retired PlusBands.Normalise used, kept here purely to read a step-encoded contract ramp
        /// (Torch7's repeated-position boundaries) back into one key per band.
        static List<PlusRampStop> CollapseSteps(PlusRamp ramp)
        {
            var src = new List<PlusRampStop>(ramp.stops);
            src.Sort((a, b) => a.pos.CompareTo(b.pos));
            var outList = new List<PlusRampStop>(src.Count);
            foreach (var st in src)
            {
                if (outList.Count > 0 && outList[outList.Count - 1].pos == st.pos) outList[outList.Count - 1] = st;
                else outList.Add(st);
            }
            // A closing stop at 1 repeating the top band's colour is not a distinct band — drop it (Torch7 always
            // appends one).
            int n = outList.Count;
            if (n >= 2 && outList[n - 1].pos >= 1f && outList[n - 1].color == outList[n - 2].color) outList.RemoveAt(n - 1);
            return outList;
        }
        /// A plain smooth UnityEngine.Gradient from (position, colour) stops — capped at Unity's own 8 colour/alpha
        /// keys (fine for every band table in this file; none exceeds 8 bands).
        static Gradient StopsToGradient(List<PlusRampStop> stops)
        {
            var g = new Gradient();
            var ck = new GradientColorKey[stops.Count];
            var ak = new GradientAlphaKey[stops.Count];
            for (int i = 0; i < stops.Count; i++)
            {
                ck[i] = new GradientColorKey(stops[i].color, stops[i].pos);
                ak[i] = new GradientAlphaKey(stops[i].color.a, stops[i].pos);
            }
            g.SetKeys(ck, ak);
            return g;
        }
        /// The torch palette by its Kiln name ("RAMP_HOT" / "hot", "ember", "white", "rim", "gold") as the contract writes
        /// it (a step-stop PlusRamp); null for an unknown name. The form holds TorchBands(name).
        public static PlusRamp Torch(string name)
        {
            switch (Short(name))
            {
                case "hot": return TorchHot();
                case "ember": return TorchEmber();
                case "white": return TorchWhite();
                case "rim": return TorchRim();
                case "gold": return TorchGold();
                default: return null;
            }
        }
        static string Short(string name)
        {
            string s = (name ?? "").Trim().ToLowerInvariant();
            return s.StartsWith("ramp_") ? s.Substring(5) : s;
        }
    }
}
