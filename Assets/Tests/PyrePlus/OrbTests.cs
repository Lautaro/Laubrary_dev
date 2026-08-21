using NUnit.Framework;
using Laubrary.PyrePlus;
using Laubrary.PyrePlus.Forms.Kiln;
using UnityEngine;

namespace Laubrary.PyrePlus.Tests
{
    /// Port 03 — Energy Projectile / agent2 THE ORB (gen 2). Reference values recorded from numpy 2.x / CPython and the
    /// contract's own noise tiles (D:/Claude@GDrive/Energy Projectile/GEN2/contract/agent2/<draw>/noise_tile.npy).
    public class OrbTests
    {
        static PlusFormCtx Ctx(int W, int frames, int frame, int seed = 2101) =>
            new PlusFormCtx(W, W, frames > 1 ? frame / (float)(frames - 1) : 0f, seed, 0, null, 1f, null, null, null, 0f, frame, frames);

        [Test]
        public void NumpyRng_UniformChoiceIntegers_MatchRecordedStream()
        {
            // default_rng(2108): the emberdrift wake-A turbulence octave draws — uniform, uniform, choice([-1, 1]),
            // integers(1, 4), uniform(0, 2π) — three octaves. choice / integers share one buffered 32-bit half.
            var r = new PlusNumpyRng(2108);
            double[][] exp =
            {
                new[] { 1.0165749180992665, 1.2741854630798906, 1.0, 2, 4.293496888050528 },
                new[] { 0.7834967434667136, 1.0336271458569861, 1.0, 1, 1.020978934569622 },
                new[] { 1.353684843195003, 1.2633122805683572, -1.0, 1, 6.263796579327222 },
            };
            foreach (var row in exp)
            {
                Assert.That(r.Uniform(0.65, 1.45), Is.EqualTo(row[0]).Within(1e-15));
                Assert.That(r.Uniform(0.65, 1.45), Is.EqualTo(row[1]).Within(1e-15));
                Assert.That(r.ChoiceSign(), Is.EqualTo(row[2]));
                Assert.That(r.Integers(1, 4), Is.EqualTo((long)row[3]));
                Assert.That(r.Uniform(0.0, 2.0 * System.Math.PI), Is.EqualTo(row[4]).Within(1e-14));
            }
            // the ember stream default_rng(2101·131 + 3·7919): vy, vx, xjit, yjit, size
            var e = new PlusNumpyRng(2101 * 131 + 3 * 7919);
            Assert.That(e.Uniform(-1.5, 1.5), Is.EqualTo(0.8733992752758981).Within(1e-15));
            Assert.That(e.Uniform(5.5, 9.0), Is.EqualTo(6.643640508952693).Within(1e-14));
            Assert.That(e.Uniform(-3, 3), Is.EqualTo(1.5291826131739015).Within(1e-14));
            Assert.That(e.Uniform(-16 * 0.55, 16 * 0.55), Is.EqualTo(8.574915869326748).Within(1e-14));
            Assert.That(e.NextDouble(), Is.EqualTo(0.3306166773051771).Within(1e-15));
        }

        [Test]
        public void Turb_MatchesContractNoiseTiles()
        {
            // Every draw's tile: turb(X, Y, tph = 0, seed, octaves, scale, aniso) on X = col + 0.5, Y = row + 0.5, 64 × 64.
            // Recorded: tile[0,0], tile[20,10] (row 20, col 10), tile[63,63], mean. numpy evaluates in float32.
            (uint seed, int oct, double scale, double aniso, double v00, double v2010, double v6363, double mean)[] tiles =
            {
                (2102, 4, 0.135, 1.25, -0.8417797684669495, -0.14351357519626617, -0.2555654048919678, -0.005660370923578739),
                (2205, 2, 0.11, 3.2, 0.3666761815547943, -0.24789829552173615, -0.15296882390975952, -0.11422013491392136),
                (2308, 4, 0.28, 1.0, 0.16634953022003174, -0.4501655399799347, 0.11615215241909027, -0.0023711167741566896),
                (2415, 3, 0.22, 1.2, 0.18142029643058777, -0.14539788663387299, 0.2087334841489792, -0.0012982585467398167),
                (2507, 3, 0.20, 1.6, 0.08269376307725906, -0.4222421944141388, 0.7909954190254211, -0.0029065210837870836),
            };
            foreach (var t in tiles)
            {
                var turb = new OrbTurb(t.seed, t.oct, t.scale, t.aniso);
                Assert.That(turb.Eval(0.5, 0.5, 0.0), Is.EqualTo(t.v00).Within(2e-5), t.seed + " [0,0]");
                Assert.That(turb.Eval(10.5, 20.5, 0.0), Is.EqualTo(t.v2010).Within(2e-5), t.seed + " [20,10]");
                Assert.That(turb.Eval(63.5, 63.5, 0.0), Is.EqualTo(t.v6363).Within(2e-5), t.seed + " [63,63]");
                double sum = 0.0;
                for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++) sum += turb.Eval(x + 0.5, y + 0.5, 0.0);
                Assert.That(sum / 4096.0, Is.EqualTo(t.mean).Within(2e-6), t.seed + " mean");
            }
        }

        [Test]
        public void BakeLut_MatchesOrbcanvasLut()
        {
            // orbcanvas.lut(name) rounded (+0.5, truncate) at a handful of indices, and the unrounded sum, per ramp.
            (string name, int[][] px, double sum)[] exp =
            {
                ("ember", new[] { new[] { 26, 4, 2 }, new[] { 28, 4, 2 }, new[] { 88, 15, 4 }, new[] { 227, 91, 17 }, new[] { 254, 186, 80 }, new[] { 255, 251, 228 }, new[] { 255, 252, 232 } }, 87780.62837221907),
                ("frost", new[] { new[] { 6, 18, 40 }, new[] { 6, 19, 41 }, new[] { 11, 43, 87 }, new[] { 43, 141, 204 }, new[] { 140, 217, 248 }, new[] { 248, 253, 255 }, new[] { 250, 254, 255 } }, 101187.55571000121),
                ("gold", new[] { new[] { 32, 12, 2 }, new[] { 34, 13, 2 }, new[] { 102, 39, 4 }, new[] { 228, 137, 23 }, new[] { 254, 215, 97 }, new[] { 255, 254, 242 }, new[] { 255, 255, 246 } }, 98773.77940168208),
                ("toxin", new[] { new[] { 6, 24, 14 }, new[] { 6, 25, 14 }, new[] { 15, 58, 27 }, new[] { 59, 159, 53 }, new[] { 158, 228, 95 }, new[] { 248, 255, 234 }, new[] { 250, 255, 238 } }, 78770.74020304877),
                ("volt", new[] { new[] { 20, 6, 44 }, new[] { 21, 6, 46 }, new[] { 59, 14, 106 }, new[] { 161, 56, 226 }, new[] { 223, 145, 252 }, new[] { 255, 244, 255 }, new[] { 255, 246, 255 } }, 109813.03426910815),
            };
            int[] idx = { 0, 1, 37, 128, 200, 254, 255 };
            foreach (var e in exp)
            {
                var lut = PlusOrb.BakeLut(PlusRampPresets.Orb(e.name));
                for (int k = 0; k < idx.Length; k++)
                    for (int c = 0; c < 3; c++)
                        Assert.That((int)(lut[idx[k] * 3 + c] + 0.5), Is.EqualTo(e.px[k][c]), $"{e.name}[{idx[k]}].{c}");
                double sum = 0.0; foreach (var v in lut) sum += v;
                Assert.That(sum, Is.EqualTo(e.sum).Within(1e-3), e.name + " sum");
            }
            Assert.That(PlusRampPresets.Orb("nope"), Is.Null);
        }

        [Test]
        public void Emit_LoopsWhenPeriodDividesFrameCount()
        {
            var buf = new System.Collections.Generic.List<(int, int)>();
            // period 2, life 8, N 20: at t = 0 the ages present are 0, 2, 4, 6 with ids 0, 9, 8, 7 (wrapping); t = 20 ≡ t = 0
            PlusOrb.Emit(0, 20, 2, 8, buf);
            Assert.That(buf, Is.EqualTo(new[] { (0, 0), (2, 9), (4, 8), (6, 7) }));
            var b2 = new System.Collections.Generic.List<(int, int)>();
            PlusOrb.Emit(20, 20, 2, 8, b2);
            Assert.That(b2, Is.EqualTo(buf));
            PlusOrb.Emit(1, 20, 2, 8, buf);
            Assert.That(buf, Is.EqualTo(new[] { (1, 0), (3, 9), (5, 8), (7, 7) }));
        }

        [Test]
        public void Render_IsDeterministic_ColdVsWarm_AndMostlyPartialAlpha()
        {
            var a = new OrbForm(); var b = new OrbForm();
            var ta = new Color32[64 * 64]; var tb = new Color32[64 * 64];
            a.Render(Ctx(64, 20, 4), ta);
            a.Render(Ctx(64, 20, 9), ta);
            a.Render(Ctx(64, 20, 4), ta);
            b.Render(Ctx(64, 20, 4), tb);
            int diff = 0, lit = 0, partial = 0;
            for (int i = 0; i < ta.Length; i++) { if (!ta[i].Equals(tb[i])) diff++; if (ta[i].a > 0) lit++; if (ta[i].a > 0 && ta[i].a < 255) partial++; }
            Assert.That(diff, Is.EqualTo(0));
            Assert.That(lit, Is.GreaterThan(100), "frame 4 of 20 is lit at 64 px");
            Assert.That(partial, Is.GreaterThan(lit * 3 / 4), "the generation's claim: 86–100 % of lit pixels partial");
        }

        [Test]
        public void EveryVariant_RendersSomething_AndStaysDeterministic()
        {
            foreach (OrbForm.Variant v in System.Enum.GetValues(typeof(OrbForm.Variant)))
            {
                var f = new OrbForm { variant = v };
                var t1 = new Color32[48 * 48]; var t2 = new Color32[48 * 48];
                f.Render(Ctx(48, 24, 6), t1);
                f.Render(Ctx(48, 24, 12), t2);
                f.Render(Ctx(48, 24, 6), t2);
                int lit = 0, diff = 0;
                for (int i = 0; i < t1.Length; i++) { if (t1[i].a > 0) lit++; if (!t1[i].Equals(t2[i])) diff++; }
                Assert.That(lit, Is.GreaterThan(50), v + " draws at frame 6");
                Assert.That(diff, Is.EqualTo(0), v + " is deterministic");
            }
        }

        [Test]
        public void ScaleInvariance_TheSamePictureMagnified()
        {
            // The programs run in source px: the lit AREA scales with the square of the canvas (within the pixel-grain passes).
            var f = new OrbForm();
            var s = new Color32[64 * 64]; var l = new Color32[128 * 128];
            f.Render(Ctx(64, 20, 3), s);
            f.Render(Ctx(128, 20, 3), l);
            int ls = 0, ll = 0;
            foreach (var c in s) if (c.a > 0) ls++;
            foreach (var c in l) if (c.a > 0) ll++;
            Assert.That(ll / (double)ls, Is.EqualTo(4.0).Within(0.6));
        }

        [Test]
        public void Clone_IsDeep_AndContentHashTracksEdits()
        {
            var f = new OrbForm { variant = OrbForm.Variant.Coronal };
            var c = (OrbForm)f.Clone();
            Assert.That(c, Is.Not.SameAs(f));
            Assert.That(c.coronal, Is.Not.SameAs(f.coronal));
            Assert.That(c.coronal.ramp, Is.Not.SameAs(f.coronal.ramp));
            Assert.That(c.ContentHash(), Is.EqualTo(f.ContentHash()));
            c.coronal.prominences = 7;
            Assert.That(f.coronal.prominences, Is.EqualTo(13));
            Assert.That(c.ContentHash(), Is.Not.EqualTo(f.ContentHash()));
            c.coronal.prominences = 13;
            c.coronal.ramp.stops[2].color = Color.red;
            Assert.That(c.ContentHash(), Is.Not.EqualTo(f.ContentHash()), "a ramp stop edit changes the hash");
        }

        [Test]
        public void SetContractParam_LoadsADrawAndConvertsItsFrame()
        {
            var f = new OrbForm();
            Assert.That(f.SetContractParam("tag", "wisp"));
            Assert.That(f.variant, Is.EqualTo(OrbForm.Variant.Wisp));
            Assert.That(f.SetContractParam("w", 200.0)); Assert.That(f.SetContractParam("h", 80.0));
            Assert.That(f.SetContractParam("cy", 40.0)); Assert.That(f.SetContractParam("nx", 155.0));
            Assert.That(f.SetContractParam("R", 15.0)); Assert.That(f.SetContractParam("L", 132.0));
            Assert.That(f.noseX.staticValue, Is.EqualTo(0.775f).Within(1e-6f));   // the contract's placement lands as the Static value
            Assert.That(f.axisY.staticValue, Is.EqualTo((60 + 40) / 200f).Within(1e-6f));   // letterboxed at the centre of the 200 px square canvas
            Assert.That(f.radius.staticValue, Is.EqualTo(15f / 200f).Within(1e-6f));
            Assert.That(f.wake.staticValue, Is.EqualTo(132f / 15f).Within(1e-5f));
            Assert.That(f.SetContractParam("smear_taps", 20.0)); Assert.That(f.wisp.smearTaps, Is.EqualTo(20));
            Assert.That(f.SetContractParam("smear_norm", false)); Assert.That(f.wisp.smearNorm, Is.False);
            Assert.That(f.SetContractParam("ramp", "frost")); Assert.That(f.wisp.ramp.stops.Count, Is.EqualTo(8));
            Assert.That(f.SetContractParam("floor", 3.0)); Assert.That(f.floor, Is.EqualTo(3));
            Assert.That(f.SetContractParam("tube_path", "x = nx - 0.95R - s*L"), Is.False, "formula strings are frozen literals");
            Assert.That(f.SetContractParam("no_such_key", 1.0), Is.False);
        }

        // ── envelopes (T-0063) ──
        [Test]
        public void Envelope_StaticEqualsFlatCurve_AndARampDrivesTheRender()
        {
            // a shared StyleSettings dial (gain), the form's (radius) and a variant's own (emberdrift.coreAmp): a flat
            // Curve renders the Static bytes; a moving Curve changes the first and the last frame of the loop
            var stat = EnvelopeTestUtil.Spec(new OrbForm(), 64, 10, 2101);
            var flat = EnvelopeTestUtil.Spec(new OrbForm { radius = EnvelopeTestUtil.Flat(16f / 192f), emberdrift = { gain = EnvelopeTestUtil.Flat(0.58f), coreAmp = EnvelopeTestUtil.Flat(3.15f) } }, 64, 10, 2101);
            var ramp = EnvelopeTestUtil.Spec(new OrbForm { emberdrift = { gain = EnvelopeTestUtil.Ramp(0.2f, 3f) } }, 64, 10, 2101);
            try
            {
                Assert.That(EnvelopeTestUtil.FnvAll(flat), Is.EqualTo(EnvelopeTestUtil.FnvAll(stat)), "a flat Curve is the Static value");
                Assert.That(EnvelopeTestUtil.DiffPixels(PyrePlusRenderer.RenderFrame(stat, 0), PyrePlusRenderer.RenderFrame(ramp, 0)), Is.GreaterThan(0), "frame 0: gain 0.2 vs 0.58");
                Assert.That(EnvelopeTestUtil.DiffPixels(PyrePlusRenderer.RenderFrame(stat, 9), PyrePlusRenderer.RenderFrame(ramp, 9)), Is.GreaterThan(0), "frame 9: gain 3 vs 0.58");
            }
            finally { Object.DestroyImmediate(stat); Object.DestroyImmediate(flat); Object.DestroyImmediate(ramp); }
        }

        [Test]
        public void Envelope_AVariantDialIsResolvedForTheActiveVariantOnly()
        {
            var voltS = EnvelopeTestUtil.Spec(new OrbForm { variant = OrbForm.Variant.Voltcore }, 64, 8, 2505);
            var voltR = EnvelopeTestUtil.Spec(new OrbForm { variant = OrbForm.Variant.Voltcore, voltcore = { ballAmp = EnvelopeTestUtil.Ramp(0f, 3f) } }, 64, 8, 2505);
            var emberS = EnvelopeTestUtil.Spec(new OrbForm(), 64, 8, 2101);
            var emberR = EnvelopeTestUtil.Spec(new OrbForm { voltcore = { ballAmp = EnvelopeTestUtil.Ramp(0f, 3f) } }, 64, 8, 2101);
            try
            {
                Assert.That(EnvelopeTestUtil.DiffPixels(PyrePlusRenderer.RenderFrame(voltS, 7), PyrePlusRenderer.RenderFrame(voltR, 7)), Is.GreaterThan(0), "Voltcore frame 7: ballAmp 3 vs its default");
                Assert.That(EnvelopeTestUtil.FnvAll(emberR), Is.EqualTo(EnvelopeTestUtil.FnvAll(emberS)), "an inactive variant's envelope is not read");
            }
            finally { Object.DestroyImmediate(voltS); Object.DestroyImmediate(voltR); Object.DestroyImmediate(emberS); Object.DestroyImmediate(emberR); }
        }
    }
}
