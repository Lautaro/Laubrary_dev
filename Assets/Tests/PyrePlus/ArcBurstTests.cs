using System.IO;
using NUnit.Framework;
using Newtonsoft.Json.Linq;
using Laubrary.PyrePlus;
using Laubrary.PyrePlus.Forms.Kiln;
using UnityEngine;

namespace Laubrary.PyrePlus.Tests
{
    /// Port 02 — Arc Burst (Energy Explosion / agent4 gen 4): CPython's random.Random replica, the band presets
    /// against the contract's step LUTs, the zero-padded blur, determinism, clone/hash and contract loading.
    public class ArcBurstTests
    {
        const string ContractRoot = @"D:\Claude@GDrive\Energy Explosion\GEN4\contract\agent4";

        [Test]
        public void PyRandom_MatchesRecordedStream()
        {
            // python 3.12: r = random.Random(4308); [r.random() for _ in range(3)]
            var r = new PlusPyRandom(4308);
            Assert.That(r.Random(), Is.EqualTo(0.5640064990043587).Within(1e-15));
            Assert.That(r.Random(), Is.EqualTo(0.939353163429407).Within(1e-15));
            Assert.That(r.Random(), Is.EqualTo(0.31612090588329944).Within(1e-15));
            // r = random.Random(4308); [r.gauss(0, 1) for _ in range(3)] — the second value comes from the cache
            r = new PlusPyRandom(4308);
            Assert.That(r.Gauss(0, 1), Is.EqualTo(-2.1786731246735656).Within(1e-12));
            Assert.That(r.Gauss(0, 1), Is.EqualTo(-0.9266925181904316).Within(1e-12));
            Assert.That(r.Gauss(0, 1), Is.EqualTo(-0.7845782749552905).Within(1e-12));
            // r = random.Random(2637739): randint(2,3)×8, randrange(5,31)×4, choice([-1.0,1.0])×6, uniform(0.28,1.1)
            r = new PlusPyRandom(2637739);
            Assert.That(new[] { r.RandInt(2, 3), r.RandInt(2, 3), r.RandInt(2, 3), r.RandInt(2, 3), r.RandInt(2, 3), r.RandInt(2, 3), r.RandInt(2, 3), r.RandInt(2, 3) },
                        Is.EqualTo(new long[] { 3, 2, 2, 3, 2, 3, 2, 2 }));
            Assert.That(new[] { r.RandRange(5, 31), r.RandRange(5, 31), r.RandRange(5, 31), r.RandRange(5, 31) }, Is.EqualTo(new long[] { 25, 15, 23, 29 }));
            Assert.That(new[] { r.Sign(), r.Sign(), r.Sign(), r.Sign(), r.Sign(), r.Sign() }, Is.EqualTo(new[] { 1.0, -1.0, 1.0, 1.0, -1.0, -1.0 }));
            Assert.That(r.Uniform(0.28, 1.1), Is.EqualTo(0.8474224111212467).Within(1e-15));
            Assert.That(new PlusPyRandom(0).Random(), Is.EqualTo(0.8444218515250481).Within(1e-15));
            r = new PlusPyRandom(4303L * 613 + 17);
            Assert.That(r.Random(), Is.EqualTo(0.5982974422613394).Within(1e-15));
            Assert.That(r.GetRandBits(40), Is.EqualTo(610505355569UL));
            Assert.That(new PlusPyRandom((1L << 40) + 5).Random(), Is.EqualTo(0.5043802970418443).Within(1e-15), "two-word seed");
        }

        [TestCase("bolt", "violet")]
        [TestCase("core", "ion")]
        [TestCase("cage", "cyan")]
        [TestCase("stipple", "steel")]
        public void BandPresets_MatchTheContractStepLut(string draw, string palette)
        {
            string rampPath = Path.Combine(ContractRoot, draw, "ramp.json");
            if (!File.Exists(rampPath)) Assert.Ignore("gen-4 contract not present on this machine");
            var ramp = (JObject)JObject.Parse(File.ReadAllText(rampPath))["ramps"][palette];
            var form = new ArcBurstForm { palette = ArcBands.Get(palette) };
            var probe = (IPlusRampProbe)form;
            int size = ramp.Value<int>("lut_size");
            var rgb = (JArray)ramp["lut"]["rgb"]; var al = (JArray)ramp["lut"]["alpha"];
            for (int i = 0; i < size; i++)
            {
                var c = probe.ProbeRamp(i / (float)(size - 1));
                var e = (JArray)rgb[i];
                Assert.That(Mathf.RoundToInt(c.r * 255f), Is.EqualTo(e[0].Value<int>()), $"{palette} r @ {i}");
                Assert.That(Mathf.RoundToInt(c.g * 255f), Is.EqualTo(e[1].Value<int>()), $"{palette} g @ {i}");
                Assert.That(Mathf.RoundToInt(c.b * 255f), Is.EqualTo(e[2].Value<int>()), $"{palette} b @ {i}");
                Assert.That(c.a, Is.EqualTo(al[i].Value<float>()).Within(1e-6), $"{palette} a @ {i}");
            }
        }

        [Test]
        public void Bands_UnknownNameIsNull() => Assert.That(ArcBands.Get("ember"), Is.Null);

        [Test]
        public void BlurZero_IsZeroPaddedAndNormalised()
        {
            // a single 1 in the middle of a 9×9: one pass of r=1 spreads 1/9 to each neighbour; three passes keep the
            // total mass inside (no clamping doubles the edge), and at the edge the mass leaks out instead of piling up
            const int S = 9; var f = new float[S * S]; f[4 * S + 4] = 1f; var tmp = new float[S * S];
            ArcField.BlurZero(f, S, 1, tmp);
            double sum = 0; foreach (var v in f) sum += v;
            Assert.That(sum, Is.EqualTo(1.0).Within(1e-5), "mass conserved away from the edges");
            var g = new float[S * S]; g[0] = 1f;
            ArcField.BlurZero(g, S, 1, tmp);
            double sum2 = 0; foreach (var v in g) sum2 += v;
            Assert.That(sum2, Is.LessThan(0.5), "a corner loses mass to the zero padding");
        }

        static PlusFormCtx Ctx(int W, int frames, int frame, int seed = 4303) =>
            new PlusFormCtx(W, W, frames > 1 ? frame / (float)(frames - 1) : 0f, seed, 0, null, 1f, null, null, null, 0f, frame, frames);

        [Test]
        public void Render_IsDeterministic_ColdVsWarm()
        {
            var a = new ArcBurstForm(); var b = new ArcBurstForm();
            var ta = new Color32[64 * 64]; var tb = new Color32[64 * 64];
            a.Render(Ctx(64, 18, 4), ta);
            a.Render(Ctx(64, 18, 9), ta);
            a.Render(Ctx(64, 18, 4), ta);
            b.Render(Ctx(64, 18, 4), tb);
            int diff = 0, lit = 0, partial = 0;
            for (int i = 0; i < ta.Length; i++) { if (!ta[i].Equals(tb[i])) diff++; if (ta[i].a > 0) lit++; if (ta[i].a > 0 && ta[i].a < 255) partial++; }
            Assert.That(diff, Is.EqualTo(0));
            Assert.That(lit, Is.GreaterThan(100), "frame 4 of 18 is lit at 64 px");
            Assert.That(partial, Is.GreaterThan(lit / 2), "most lit pixels carry partial alpha (the generation-4 claim)");
        }

        [Test]
        public void EveryLayout_RendersSomething_AndStaysDeterministic()
        {
            foreach (ArcBurstForm.Layout l in System.Enum.GetValues(typeof(ArcBurstForm.Layout)))
            {
                var f = new ArcBurstForm { layout = l };
                var t1 = new Color32[48 * 48]; var t2 = new Color32[48 * 48];
                f.Render(Ctx(48, 18, 6), t1);
                f.Render(Ctx(48, 18, 12), t2);
                f.Render(Ctx(48, 18, 6), t2);
                int lit = 0, diff = 0;
                for (int i = 0; i < t1.Length; i++) { if (t1[i].a > 0) lit++; if (!t1[i].Equals(t2[i])) diff++; }
                Assert.That(lit, Is.GreaterThan(50), l + " draws at frame 6");
                Assert.That(diff, Is.EqualTo(0), l + " is deterministic");
            }
        }

        [Test]
        public void Clone_DeepCopiesSettingsAndBands_AndHashTracks()
        {
            var a = new ArcBurstForm();
            var b = (ArcBurstForm)a.Clone();
            Assert.That(b.bolt, Is.Not.SameAs(a.bolt));
            Assert.That(b.palette, Is.Not.SameAs(a.palette));
            Assert.That(b.ContentHash(), Is.EqualTo(a.ContentHash()));
            b.bolt.trunks = 7;
            Assert.That(a.bolt.trunks, Is.EqualTo(5));
            Assert.That(b.ContentHash(), Is.Not.EqualTo(a.ContentHash()));
            b.bolt.trunks = 5; b.palette.stops[2].color = Color.red;
            Assert.That(b.ContentHash(), Is.Not.EqualTo(a.ContentHash()), "a band colour edit changes the hash");
        }

        [Test]
        public void SetContractParam_ReachesLayoutPaletteAndTypedKeys()
        {
            var f = new ArcBurstForm();
            Assert.That(f.SetContractParam("draw", "cage"), Is.True);
            Assert.That(f.layout, Is.EqualTo(ArcBurstForm.Layout.Cage));
            Assert.That(f.SetContractParam("palette", "cyan"), Is.True);
            Assert.That(f.palette.stops[1].color.g, Is.EqualTo(168f / 255f).Within(1e-6));
            Assert.That(f.SetContractParam("aref", 0.26), Is.True);
            Assert.That(f.aref.staticValue, Is.EqualTo(0.26f).Within(1e-6));   // a contract scalar lands as the Static value
            Assert.That(f.SetContractParam("bloom_radius", 2.6), Is.True);
            Assert.That(f.bloomRadius.staticValue, Is.EqualTo(2.6f).Within(1e-6));
            f.bloomStrength = new ZUIValue(0.4f);
            Assert.That(f.SetContractParam("bloom_alpha_strength", 0.136), Is.True);
            Assert.That(f.bloomAlpha.staticValue, Is.EqualTo(0.34f).Within(1e-5));
            Assert.That(f.SetContractParam("nope", 1), Is.False);
        }

        // ── envelopes (T-0063) ──
        [Test]
        public void Envelope_StaticEqualsFlatCurve_AndARampDrivesTheRender()
        {
            // the form's own dials (ampScale, bloomStrength) and a layout's (bolt.flashAmp): a flat Curve renders the
            // Static bytes; a moving Curve changes the first and the last frame
            var stat = EnvelopeTestUtil.Spec(new ArcBurstForm(), 64, 10, 4303);
            var flat = EnvelopeTestUtil.Spec(new ArcBurstForm { ampScale = EnvelopeTestUtil.Flat(1f), bloomStrength = EnvelopeTestUtil.Flat(0.48f), bolt = { flashAmp = EnvelopeTestUtil.Flat(1.95f) } }, 64, 10, 4303);
            var ramp = EnvelopeTestUtil.Spec(new ArcBurstForm { ampScale = EnvelopeTestUtil.Ramp(0.3f, 2f) }, 64, 10, 4303);
            try
            {
                Assert.That(EnvelopeTestUtil.FnvAll(flat), Is.EqualTo(EnvelopeTestUtil.FnvAll(stat)), "a flat Curve is the Static value");
                Assert.That(EnvelopeTestUtil.DiffPixels(PyrePlusRenderer.RenderFrame(stat, 0), PyrePlusRenderer.RenderFrame(ramp, 0)), Is.GreaterThan(0), "frame 0: ampScale 0.3 vs 1");
                Assert.That(EnvelopeTestUtil.DiffPixels(PyrePlusRenderer.RenderFrame(stat, 5), PyrePlusRenderer.RenderFrame(ramp, 5)), Is.GreaterThan(0), "frame 5: ampScale ≈ 1.24 vs 1 (the last frame has dissolved to nothing on both)");
            }
            finally { Object.DestroyImmediate(stat); Object.DestroyImmediate(flat); Object.DestroyImmediate(ramp); }
        }

        [Test]
        public void Envelope_ALayoutDialIsResolvedForTheActiveLayoutOnly()
        {
            // cage.ballOpa ramped 0→1: the Cage frame changes, while the same edit on a form whose layout is Bolt changes nothing
            var cageS = EnvelopeTestUtil.Spec(new ArcBurstForm { layout = ArcBurstForm.Layout.Cage }, 64, 8, 4303);
            var cageR = EnvelopeTestUtil.Spec(new ArcBurstForm { layout = ArcBurstForm.Layout.Cage, cage = { ballOpa = EnvelopeTestUtil.Ramp(0f, 1f) } }, 64, 8, 4303);
            var boltS = EnvelopeTestUtil.Spec(new ArcBurstForm(), 64, 8, 4303);
            var boltR = EnvelopeTestUtil.Spec(new ArcBurstForm { cage = { ballOpa = EnvelopeTestUtil.Ramp(0f, 1f) } }, 64, 8, 4303);
            try
            {
                Assert.That(EnvelopeTestUtil.DiffPixels(PyrePlusRenderer.RenderFrame(cageS, 1), PyrePlusRenderer.RenderFrame(cageR, 1)), Is.GreaterThan(0), "Cage frame 1: ballOpa ≈ 0.14 vs 0.42");
                Assert.That(EnvelopeTestUtil.FnvAll(boltR), Is.EqualTo(EnvelopeTestUtil.FnvAll(boltS)), "an inactive layout's envelope is not read");
            }
            finally { Object.DestroyImmediate(cageS); Object.DestroyImmediate(cageR); Object.DestroyImmediate(boltS); Object.DestroyImmediate(boltR); }
        }
    }
}
