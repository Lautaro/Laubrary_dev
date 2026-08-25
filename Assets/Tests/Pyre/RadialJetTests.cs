using NUnit.Framework;
using Laubrary.Pyre;
using Laubrary.Pyre.Forms.Kiln;
using UnityEngine;

namespace Laubrary.Pyre.Tests
{
    /// Port 06 — Flame / agent3_fork_radial the radial jet (gen 3) on the shared engine. Reference values recorded from
    /// numpy 1.26.4 running the fork's own flame3 (slot table, `frame` / `shade` at frame 0 of corona and of every draw).
    public class RadialJetTests
    {
        static PyreFormCtx Ctx(int W, int frames, int frame, int seed) =>
            new PyreFormCtx(W, W, frames > 1 ? frame / (float)(frames - 1) : 0f, seed, 0, null, 1f, null, null, null, 0f, frame, frames);

        /// corona's contract frame (176 × 176, seed 101) on the 176 px square: u = 1, no letterbox.
        static JetFrame CoronaFrame(JetSettings c) => JetFrame.Solo(176, 176, c.nozzleX * c.w, c.nozzleY * c.h, 1.0, 101);

        [Test]
        public void SlotTable_Corona_StratifiedPermutedAnglesMatchNumpy()
        {
            // default_rng(101*7919+13): spread 180 → stratified + permuted base, bias 1.05; da[:4], vs[0], shed.sum()
            var t = RadialJetProgram.Default.BuildSlots(RadialJetDraws.Corona(), 101);
            Assert.That(t.N, Is.EqualTo(700));
            // bias is a float32 dial (1.05f): |b|^bias carries ~5e-8 relative
            Assert.That(t.da[0], Is.EqualTo(3.070039382761524).Within(1e-6));
            Assert.That(t.da[1], Is.EqualTo(2.168186429306753).Within(1e-6));
            Assert.That(t.da[2], Is.EqualTo(-2.0791591118163).Within(1e-6));
            Assert.That(t.da[3], Is.EqualTo(-1.2864513277449507).Within(1e-6));
            Assert.That(t.vs[0], Is.EqualTo(1.0031414437398842).Within(1e-7));
            int shed = 0; foreach (bool b in t.shed) if (b) shed++;
            Assert.That(shed, Is.EqualTo(92));
        }

        [Test]
        public void SlotTable_Whirl_LobesRemapAndKickMatchNumpy()
        {
            // whirl (seed 139): lobes 3 depth 0.78 kick 0.46 → da[:3], vs[:2]
            var t = RadialJetProgram.Default.BuildSlots(RadialJetDraws.Whirl(), 139);
            Assert.That(t.da[0], Is.EqualTo(2.2672748681270645).Within(1e-7));
            Assert.That(t.da[1], Is.EqualTo(2.487656103689002).Within(1e-7));
            Assert.That(t.da[2], Is.EqualTo(-0.7124594156348184).Within(1e-7));
            Assert.That(t.vs[0], Is.EqualTo(1.0850483368356039).Within(1e-7));
            Assert.That(t.vs[1], Is.EqualTo(0.8708700812552529).Within(1e-7));
        }

        [Test]
        public void CoronaFrame0_FieldMatchesNumpy()
        {
            // frame(corona, tab, 0): H.sum 9820.675781, H.max 10.786695, T.sum 3148.604004, warp_max 6.694079, H[88,100] 1.9580852
            var c = RadialJetDraws.Corona();
            var sc = new JetScratch(); sc.Ensure(176 * 176); sc.Clear();
            RadialJetProgram.Default.Frame(c, CoronaFrame(c), 0.0, sc);
            double sumH = 0, sumT = 0, max = 0;
            for (int i = 0; i < sc.H.Length; i++) { sumH += sc.H[i]; sumT += sc.T[i]; if (sc.H[i] > max) max = sc.H[i]; }
            Assert.That(sumH, Is.EqualTo(9820.675781).Within(0.05));
            Assert.That(max, Is.EqualTo(10.786695).Within(1e-4));
            Assert.That(sumT, Is.EqualTo(3148.604004).Within(0.05));
            Assert.That(sc.warpMax, Is.EqualTo(6.694079).Within(1e-4));
            Assert.That(sc.H[88 * 176 + 100], Is.EqualTo(1.9580852).Within(1e-4));
        }

        [Test]
        public void CoronaFrame0_PixelsMatchNumpy()
        {
            // shade(corona, frame 0): lit 8685; px[88,100] = (254,163,42,246), px[60,60] = (198,57,16,189)
            var c = RadialJetDraws.Corona();
            var sc = new JetScratch(); sc.Ensure(176 * 176); sc.Clear();
            RadialJetProgram.Default.Frame(c, CoronaFrame(c), 0.0, sc);
            var target = new Color32[176 * 176];
            JetShade.Default.Shade(c, JetShade.Bake(c.ramp), JetShade.Bake(c.sootRamp), sc.H, sc.T, 176, 176, target);
            int lit = 0; foreach (var px in target) if (px.a > 0) lit++;
            Assert.That(lit, Is.EqualTo(8685).Within(2));
            Color32 Px(int row, int col) => target[(176 - 1 - row) * 176 + col];
            AssertPx(Px(88, 100), 254, 163, 42, 246);
            AssertPx(Px(60, 60), 198, 57, 16, 189);
        }

        static void AssertPx(Color32 c, int r, int g, int b, int a)
        {
            Assert.That((int)c.r, Is.EqualTo(r).Within(1)); Assert.That((int)c.g, Is.EqualTo(g).Within(1));
            Assert.That((int)c.b, Is.EqualTo(b).Within(1)); Assert.That((int)c.a, Is.EqualTo(a).Within(1));
        }

        [TestCase(RadialJetForm.Variant.Corona, 101, 176, 176, 30, 8685)]
        [TestCase(RadialJetForm.Variant.Fan, 113, 144, 216, 30, 9606)]
        [TestCase(RadialJetForm.Variant.Crown, 127, 184, 180, 32, 7044)]
        [TestCase(RadialJetForm.Variant.Whirl, 139, 176, 176, 32, 5429)]
        [TestCase(RadialJetForm.Variant.Shockring, 151, 176, 176, 30, 7014)]
        [TestCase(RadialJetForm.Variant.Maw, 163, 200, 152, 30, 8315)]
        [TestCase(RadialJetForm.Variant.Starburst, 173, 176, 176, 28, 4684)]
        [TestCase(RadialJetForm.Variant.Halo, 181, 192, 192, 32, 6577)]
        public void EveryDraw_Frame0LitCountMatchesNumpy(RadialJetForm.Variant v, int seed, int w, int h, int frames, int lit)
        {
            var f = new RadialJetForm { variant = v };
            f.ResolveGeometry();
            int S = Mathf.Max(w, h);
            var target = new Color32[S * S];
            f.Render(Ctx(S, frames, 0, seed), target);
            int n = 0; foreach (var c in target) if (c.a > 0) n++;
            Assert.That(n, Is.EqualTo(lit).Within(2), "lit pixels at frame 0 (±2 = a despeckle / threshold flip from float32 vs double)");
        }

        [Test]
        public void Render_ColdVsWarm_IsIdentical_AndCloneRendersTheSame()
        {
            var f = new RadialJetForm();
            var a = new Color32[64 * 64]; var b = new Color32[64 * 64]; var c = new Color32[64 * 64];
            f.Render(Ctx(64, 12, 5, 7), a);
            f.Render(Ctx(64, 12, 2, 7), c);
            f.Render(Ctx(64, 12, 5, 7), b);
            var clone = (RadialJetForm)f.Clone();
            clone.Render(Ctx(64, 12, 5, 7), c);
            int lit = 0;
            for (int i = 0; i < a.Length; i++)
            {
                Assert.That(b[i].Equals(a[i]), "cold vs warm");
                Assert.That(c[i].Equals(a[i]), "clone");
                if (a[i].a > 0) lit++;
            }
            Assert.That(lit, Is.GreaterThan(100), "a 64 px corona is visible");
            Assert.That(clone.ContentHash(), Is.EqualTo(f.ContentHash()));
            Assert.That(ReferenceEquals(clone.corona, f.corona), Is.False, "the box is deep-copied");
        }

        [Test]
        public void SetContractParam_ReachesTheRadialKeys()
        {
            var f = new RadialJetForm();
            Assert.That(f.SetContractParam("draw", "crown"), Is.True);
            Assert.That(f.variant, Is.EqualTo(RadialJetForm.Variant.Crown));
            Assert.That(f.SetContractParam("src_r", 0.2), Is.True); Assert.That(f.crown.srcR.staticValue, Is.EqualTo(0.2f));
            Assert.That(f.SetContractParam("lobe_depth", 0.5), Is.True); Assert.That(f.crown.lobeDepth.staticValue, Is.EqualTo(0.5f));
            Assert.That(f.SetContractParam("ring_flat", true), Is.True); Assert.That(f.crown.ringFlat, Is.True);
            Assert.That(f.SetContractParam("warp_spin", 2L), Is.True); Assert.That(f.crown.warpSpin, Is.EqualTo(2));
            Assert.That(f.SetContractParam("root_k", 8L), Is.True); Assert.That(f.crown.rootK, Is.EqualTo(8));
            Assert.That(f.SetContractParam("ramp", "BURNER"), Is.True);
            Assert.That(f.crown.sootLo.staticValue, Is.EqualTo(0.18f)); Assert.That(f.crown.sootHi.staticValue, Is.EqualTo(0.88f));
            Assert.That(f.SetContractParam("draw", "gout"), Is.False, "a base-jet name is not a radial variant");
        }

        [Test]
        public void Presets_MatchTheContractRamps()
        {
            // ramp.json: BURNER stop 0 = (4,12,46) a 0.22; GHOST's top ceiling is 0.96; VIOLET is wyrm's ramp stop for stop
            var b = JetShade.Bake(PyreRampPresets.JetBurner());
            Assert.That((int)b.sr[0], Is.EqualTo(4)); Assert.That((int)b.sg[0], Is.EqualTo(12)); Assert.That((int)b.sb[0], Is.EqualTo(46)); Assert.That(b.a[0], Is.EqualTo(0.22).Within(1e-6));
            var g = JetShade.Bake(PyreRampPresets.JetGhost());
            Assert.That(g.a[1023], Is.EqualTo(0.96).Within(1e-6));
            var v = PyreRampPresets.Jet("VIOLET"); var w = PyreRampPresets.JetWyrm();
            Assert.That(v.stops.Count, Is.EqualTo(w.stops.Count));
            for (int i = 0; i < v.stops.Count; i++) { Assert.That(v.stops[i].pos, Is.EqualTo(w.stops[i].pos)); Assert.That(v.stops[i].color, Is.EqualTo(w.stops[i].color)); }
            Assert.That(PyreRampPresets.JetSecondary("BURNER").stops.Count, Is.EqualTo(7));
            Assert.That(PyreRampPresets.JetSecondary("SOLAR").IsEmpty, Is.True);
            Assert.That(PyreRampPresets.JetSootWindow("BURNER"), Is.EqualTo(new Vector2(0.18f, 0.88f)));
        }

        // ── envelopes (T-0063) ──
        [Test]
        public void Envelope_StaticEqualsFlatCurve_AndARadialDialRampDrivesTheRender()
        {
            // a base dial (strength) and the radial box's own (swirl, through RadialJetSettings.Own): a flat Curve renders
            // the Static bytes; a moving swirl Curve changes the first and the last frame of the loop
            var stat = EnvelopeTestUtil.Spec(new RadialJetForm { variant = RadialJetForm.Variant.Whirl }, 64, 10, 139);
            var flat = EnvelopeTestUtil.Spec(new RadialJetForm { variant = RadialJetForm.Variant.Whirl, whirl = { strength = EnvelopeTestUtil.Flat(RadialJetDraws.Whirl().strength.staticValue), swirl = EnvelopeTestUtil.Flat(RadialJetDraws.Whirl().swirl.staticValue) } }, 64, 10, 139);
            var ramp = EnvelopeTestUtil.Spec(new RadialJetForm { variant = RadialJetForm.Variant.Whirl, whirl = { swirl = EnvelopeTestUtil.Ramp(0f, 300f) } }, 64, 10, 139);
            try
            {
                Assert.That(EnvelopeTestUtil.FnvAll(flat), Is.EqualTo(EnvelopeTestUtil.FnvAll(stat)), "a flat Curve is the Static value");
                Assert.That(EnvelopeTestUtil.DiffPixels(PyreRenderer.RenderFrame(stat, 0), PyreRenderer.RenderFrame(ramp, 0)), Is.GreaterThan(0), "frame 0: swirl 0 vs 142");
                Assert.That(EnvelopeTestUtil.DiffPixels(PyreRenderer.RenderFrame(stat, 9), PyreRenderer.RenderFrame(ramp, 9)), Is.GreaterThan(0), "frame 9: swirl 300 vs 142");
            }
            finally { Object.DestroyImmediate(stat); Object.DestroyImmediate(flat); Object.DestroyImmediate(ramp); }
        }
    }
}
