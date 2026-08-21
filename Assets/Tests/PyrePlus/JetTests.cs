using NUnit.Framework;
using Laubrary.PyrePlus;
using Laubrary.PyrePlus.Forms.Kiln;
using UnityEngine;

namespace Laubrary.PyrePlus.Tests
{
    /// Port 07 — Flame / agent3 the directional jet (gen 2) and its shared engine. Reference values recorded from numpy
    /// 1.26 (slot table, lattices, value noise, the baked LUT, `frame` / `shade` at frame 0 of gout and of every draw).
    public class JetTests
    {
        static PlusFormCtx Ctx(int W, int frames, int frame, int seed) =>
            new PlusFormCtx(W, W, frames > 1 ? frame / (float)(frames - 1) : 0f, seed, 0, null, 1f, null, null, null, 0f, frame, frames);

        /// gout's contract frame (160 × 92, seed 23) letterboxed on the 160 px square: left 0, top 34, u = 1.
        static JetFrame GoutFrame(JetSettings g) => JetFrame.Solo(160, 160, 0 + g.nozzleX * g.w, 34 + g.nozzleY * g.h, 1.0, 23);

        [Test]
        public void SlotTable_MatchesNumpy()
        {
            // default_rng(23*7919+13): da[:4], vs[0], rs[0], as_[0], ls[0], drift[0], shed.sum(), shed[8]
            var t = JetProgram.Default.BuildSlots(JetDraws.Gout(), 23);
            Assert.That(t.N, Is.EqualTo(210));
            Assert.That(t.da[0], Is.EqualTo(0.001621402970148165).Within(1e-15));
            Assert.That(t.da[1], Is.EqualTo(-0.036790025288369534).Within(1e-15));
            Assert.That(t.da[2], Is.EqualTo(0.008275051025073747).Within(1e-15));
            Assert.That(t.da[3], Is.EqualTo(-0.15565141324197218).Within(1e-15));
            // the jittered draws carry the float32 `jitter` dial (0.58f vs 0.58): ~5e-9
            Assert.That(t.vs[0], Is.EqualTo(1.1569981178763276).Within(1e-7));
            Assert.That(t.rs[0], Is.EqualTo(0.9171788221428954).Within(1e-7));
            Assert.That(t.amp[0], Is.EqualTo(0.9322695216101796).Within(1e-7));
            Assert.That(t.ls[0], Is.EqualTo(1.0629362074840372).Within(1e-7));
            Assert.That(t.drift[0], Is.EqualTo(-0.20868507830919047).Within(1e-15));
            int shed = 0; foreach (bool b in t.shed) if (b) shed++;
            Assert.That(shed, Is.EqualTo(33));
            Assert.That(t.shed[8], Is.True); Assert.That(t.shed[0], Is.False);
        }

        [Test]
        public void Lattice_MatchesNumpyUniform()
        {
            // default_rng(23000).uniform(-1, 1, (4, 12, 24)).astype(float32): [0,0,:4], [3,11,23]; default_rng(23001) (8, 24, 48): [0,0,:3]
            var sc = new JetScratch();
            var a = sc.Lattice(23000, 24, 12, 4);
            Assert.That(a.Length, Is.EqualTo(4 * 12 * 24));
            Assert.That(a[0], Is.EqualTo(0.46490222215652466f).Within(1e-7f));
            Assert.That(a[1], Is.EqualTo(0.9517732262611389f).Within(1e-7f));
            Assert.That(a[2], Is.EqualTo(0.47629472613334656f).Within(1e-7f));
            Assert.That(a[3], Is.EqualTo(-0.7995245456695557f).Within(1e-7f));
            Assert.That(a[3 * 12 * 24 + 11 * 24 + 23], Is.EqualTo(-0.36986225843429565f).Within(1e-7f));
            var b = sc.Lattice(23001, 48, 24, 8);
            Assert.That(b[0], Is.EqualTo(-0.34800630807876587f).Within(1e-7f));
            Assert.That(b[1], Is.EqualTo(-0.44471076130867004f).Within(1e-7f));
            Assert.That(b[2], Is.EqualTo(-0.21939364075660706f).Within(1e-7f));
        }

        [Test]
        public void ValueNoise_MatchesNumpy()
        {
            // value_noise_3d(u=[3.7,10.2], v=[1.3,5.9], w=[0,2.5], period=(24,12,4), seed=23, octaves=2); numpy in float32
            var sc = new JetScratch();
            Assert.That(JetNoise.Sample(sc, 23, 3.7, 1.3, 0.0, 24, 12, 4, 2), Is.EqualTo(-0.10028726607561111).Within(2e-6));
            Assert.That(JetNoise.Sample(sc, 23, 10.2, 5.9, 2.5, 24, 12, 4, 2), Is.EqualTo(-0.3453623056411743).Within(2e-6));
            // exactly periodic
            Assert.That(JetNoise.Sample(sc, 23, 3.7 + 24, 1.3 + 12, 0.0 + 4, 24, 12, 4, 2), Is.EqualTo(JetNoise.Sample(sc, 23, 3.7, 1.3, 0.0, 24, 12, 4, 2)).Within(1e-12));
        }

        [Test]
        public void Lut_MatchesBuildLut()
        {
            // EMBER.luts(): hot[0], hot[512], alpha[512]; soot[1023] — linear light decoded from the uint8 table
            var hot = JetShade.Bake(PlusRampPresets.Ember());
            Assert.That(hot.r[0], Is.EqualTo(0.021219011710401362).Within(1e-9));
            Assert.That(hot.g[0], Is.EqualTo(0.001214108005996459).Within(1e-9));
            Assert.That(hot.b[0], Is.EqualTo(0.000910580968455449).Within(1e-9));
            Assert.That(hot.r[512], Is.EqualTo(0.8879231243314909).Within(1e-9));
            Assert.That(hot.g[512], Is.EqualTo(0.14412847455130065).Within(1e-9));
            Assert.That(hot.b[512], Is.EqualTo(0.006512091155769694).Within(1e-9));
            Assert.That(hot.a[512], Is.EqualTo(0.8933025002479553).Within(1e-7));
            var soot = JetShade.Bake(PlusRampPresets.EmberSoot());
            Assert.That(soot.r[1023], Is.EqualTo(0.5149176888436047).Within(1e-9));
            Assert.That(soot.g[1023], Is.EqualTo(0.26635563282209973).Within(1e-9));
            Assert.That(soot.b[1023], Is.EqualTo(0.0975873525466093).Within(1e-9));
        }

        [Test]
        public void GoutFrame0_FieldMatchesNumpy()
        {
            // frame(gout, tab, 0): H.sum 1927.967529, H.max 2.423003, T.sum 569.813232, warp_max 4.297884, H[62,20] 1.3448929
            var g = JetDraws.Gout();
            var sc = new JetScratch(); sc.Ensure(160 * 160); sc.Clear();
            JetProgram.Default.Frame(g, GoutFrame(g), 0.0, sc);
            double sumH = 0, sumT = 0, max = 0;
            for (int i = 0; i < sc.H.Length; i++) { sumH += sc.H[i]; sumT += sc.T[i]; if (sc.H[i] > max) max = sc.H[i]; }
            Assert.That(sumH, Is.EqualTo(1927.967529).Within(0.01));
            Assert.That(max, Is.EqualTo(2.423003).Within(1e-5));
            Assert.That(sumT, Is.EqualTo(569.813232).Within(0.01));
            Assert.That(sc.warpMax, Is.EqualTo(4.297884).Within(1e-5));
            Assert.That(sc.H[(34 + 62) * 160 + 20], Is.EqualTo(1.3448929).Within(1e-5));
            // nothing outside the frame rect
            for (int x = 0; x < 160; x++) { Assert.That(sc.H[33 * 160 + x], Is.EqualTo(0f)); Assert.That(sc.H[126 * 160 + x], Is.EqualTo(0f)); }
        }

        [Test]
        public void GoutFrame0_PixelsMatchNumpy()
        {
            // shade(gout, frame 0): lit 2700; px[62,20] = (255,195,81,253), px[60,40] = (255,215,123,255), px[55,100] = (222,88,20,213)
            var g = JetDraws.Gout();
            var sc = new JetScratch(); sc.Ensure(160 * 160); sc.Clear();
            JetProgram.Default.Frame(g, GoutFrame(g), 0.0, sc);
            var target = new Color32[160 * 160];
            JetShade.Default.Shade(g, JetShade.Bake(g.ramp), JetShade.Bake(g.sootRamp), sc.H, sc.T, 160, 160, target);
            int lit = 0; foreach (var c in target) if (c.a > 0) lit++;
            Assert.That(lit, Is.EqualTo(2700));
            Color32 Px(int row, int col) => target[(160 - 1 - (34 + row)) * 160 + col];
            AssertPx(Px(62, 20), 255, 195, 81, 253);
            AssertPx(Px(60, 40), 255, 215, 123, 255);
            AssertPx(Px(55, 100), 222, 88, 20, 213);
        }

        static void AssertPx(Color32 c, int r, int g, int b, int a)
        {
            Assert.That((int)c.r, Is.EqualTo(r).Within(1)); Assert.That((int)c.g, Is.EqualTo(g).Within(1));
            Assert.That((int)c.b, Is.EqualTo(b).Within(1)); Assert.That((int)c.a, Is.EqualTo(a).Within(1));
        }

        [TestCase(JetForm.Variant.Lance, 11, 168, 52, 28, 1104)]
        [TestCase(JetForm.Variant.Gout, 23, 160, 92, 30, 2700)]
        [TestCase(JetForm.Variant.Sputter, 37, 152, 116, 30, 3430)]
        [TestCase(JetForm.Variant.Whip, 53, 168, 104, 32, 2360)]
        [TestCase(JetForm.Variant.Wyrm, 71, 180, 100, 30, 2055)]
        public void EveryDraw_Frame0LitCountMatchesNumpy(JetForm.Variant v, int seed, int w, int h, int frames, int lit)
        {
            var f = new JetForm { variant = v };
            f.ResolveGeometry();
            int S = Mathf.Max(w, h);
            var target = new Color32[S * S];
            f.Render(Ctx(S, frames, 0, seed), target);
            int n = 0; foreach (var c in target) if (c.a > 0) n++;
            Assert.That(n, Is.EqualTo(lit).Within(2), "lit pixels at frame 0 (±2 = a despeckle / threshold flip from float32 vs double)");
        }

        [Test]
        public void LeadRank_IsRankBased()
        {
            Assert.That(JetProgram.LeadRank(new[] { 0.3, 0.1, 0.2, 0.1, 0.9 }), Is.EqualTo(new[] { 0.75, 0.0, 0.5, 0.25, 1.0 }));
        }

        [Test]
        public void StratifiedPermuted_MatchesNumpyPermutation()
        {
            // default_rng(5): base = (i+0.5)/6*2-1 + uniform(-0.5,0.5,6)*2/6; rng.permutation(base); then rng.random()
            var rng = new PlusNumpyRng(5);
            var b = JetProgram.StratifiedPermuted(rng, 6);
            double[] exp = { 0.09526712669604728, -0.7316656920848733, -0.39735307008783544, 0.7944562935951727, -0.16155814631928597, 0.35131023412721885 };
            for (int i = 0; i < 6; i++) Assert.That(b[i], Is.EqualTo(exp[i]).Within(1e-15));
            Assert.That(rng.NextDouble(), Is.EqualTo(0.9991761150650714).Within(1e-15));
        }

        [Test]
        public void Render_ColdVsWarm_IsIdentical_AndCloneRendersTheSame()
        {
            var f = new JetForm();
            var a = new Color32[64 * 64]; var b = new Color32[64 * 64]; var c = new Color32[64 * 64];
            f.Render(Ctx(64, 12, 5, 7), a);
            f.Render(Ctx(64, 12, 2, 7), c);
            f.Render(Ctx(64, 12, 5, 7), b);
            var clone = (JetForm)f.Clone();
            clone.Render(Ctx(64, 12, 5, 7), c);
            int lit = 0;
            for (int i = 0; i < a.Length; i++)
            {
                Assert.That(b[i].Equals(a[i]), "cold vs warm");
                Assert.That(c[i].Equals(a[i]), "clone");
                if (a[i].a > 0) lit++;
            }
            Assert.That(lit, Is.GreaterThan(100), "a 64 px gout is visible");
            Assert.That(clone.ContentHash(), Is.EqualTo(f.ContentHash()));
            Assert.That(ReferenceEquals(clone.gout, f.gout), Is.False, "the box is deep-copied");
        }

        [Test]
        public void Presets_MatchTheContractRamps()
        {
            // ramp.json lut (256) spot checks: DIRTY entry 0 = (28,6,6) a 0.20; GOLD entry 255 = (255,250,216); TORCH entry 255 alpha 1
            var d = JetShade.Bake(PlusRampPresets.JetDirty());
            Assert.That((int)d.sr[0], Is.EqualTo(28)); Assert.That((int)d.sg[0], Is.EqualTo(6)); Assert.That((int)d.sb[0], Is.EqualTo(6)); Assert.That(d.a[0], Is.EqualTo(0.20).Within(1e-6));
            var g = JetShade.Bake(PlusRampPresets.JetGold());
            Assert.That((int)g.sr[1023], Is.EqualTo(255)); Assert.That((int)g.sg[1023], Is.EqualTo(250)); Assert.That((int)g.sb[1023], Is.EqualTo(216));
            Assert.That(PlusRampPresets.Jet("torch").stops.Count, Is.EqualTo(8));
            Assert.That(PlusRampPresets.JetSecondary("TORCH").IsEmpty, Is.True);
            Assert.That(PlusRampPresets.JetSootWindow("GOLD"), Is.EqualTo(new Vector2(0.20f, 0.85f)));
            Assert.That(PlusRampPresets.Jet("nope"), Is.Null);
        }
    }
}
