using NUnit.Framework;
using System.Collections.Generic;
using Laubrary.Pyre;
using Laubrary.Pyre.Forms.Kiln;
using UnityEngine;

namespace Laubrary.Pyre.Tests
{
    /// Port 05 — Flame / agent3_fork_explosive the explosive jet (gen 7) on the shared engine. Reference values recorded
    /// from numpy 1.26.4 running the fork's own flame3 (slot table + plates of detonate, `frame` / `shade` at frame 10 of
    /// detonate, `_plate_geom` of shatter, frame 5 of every draw — frame 0 is the instant BEFORE the bang on most draws).
    public class ExplosiveJetTests
    {
        static PyreFormCtx Ctx(int W, int frames, int frame, int seed) =>
            new PyreFormCtx(W, W, frames > 1 ? frame / (float)(frames - 1) : 0f, seed, 0, null, 1f, null, null, null, 0f, frame, frames);

        /// detonate's contract frame (184 × 184, seed 101) on the 184 px square: u = 1, no letterbox.
        static JetFrame DetonateFrame(JetSettings c) => JetFrame.Solo(184, 184, c.nozzleX * c.w, c.nozzleY * c.h, 1.0, 101);

        [Test]
        public void SlotTable_Detonate_BlastBirthsLeadAndPlatesMatchNumpy()
        {
            // default_rng(101*7919+13) + plates from default_rng(101*15013+401): da[:4], vs[0] (× pow × front × vel_spread),
            // birth[:3] (at + span·u^skew), lead[:3] (rank), shed.sum(), fx[:3], fat[:3], fnx[0], f2at[0], broke
            var t = (ExplosiveJetProgram.Slots)ExplosiveJetProgram.Default.BuildSlots(ExplosiveJetDraws.Detonate(), 101);
            Assert.That(t.N, Is.EqualTo(560));
            Assert.That(t.da[0], Is.EqualTo(-3.0293190315232574).Within(1e-6));
            Assert.That(t.da[1], Is.EqualTo(0.6564941520582617).Within(1e-6));
            Assert.That(t.da[2], Is.EqualTo(-2.1914624376428655).Within(1e-6));
            Assert.That(t.da[3], Is.EqualTo(0.013061201576209292).Within(1e-6));
            Assert.That(t.vs[0], Is.EqualTo(0.9222434105538105).Within(1e-6));
            Assert.That(t.birth[0], Is.EqualTo(0.0725773540867507).Within(1e-7));
            Assert.That(t.birth[1], Is.EqualTo(0.058457154360163584).Within(1e-7));
            Assert.That(t.birth[2], Is.EqualTo(0.0867332167418377).Within(1e-7));
            Assert.That(t.lead[0], Is.EqualTo(0.47048300536672627).Within(1e-9));
            Assert.That(t.lead[1], Is.EqualTo(0.1413237924865832).Within(1e-9));
            Assert.That(t.lead[2], Is.EqualTo(0.13595706618962433).Within(1e-9));
            int shed = 0; foreach (bool b in t.shed) if (b) shed++;
            Assert.That(shed, Is.EqualTo(59));
            Assert.That(t.fx[0], Is.EqualTo(0.3988527554427385).Within(1e-6));
            Assert.That(t.fx[1], Is.EqualTo(0.5661085284874207).Within(1e-6));
            Assert.That(t.fx[2], Is.EqualTo(1.119159973489129).Within(1e-6));
            Assert.That(t.fat[0], Is.EqualTo(0.3827944193199748).Within(1e-6));
            Assert.That(t.fat[1], Is.EqualTo(0.3520289613580583).Within(1e-6));
            Assert.That(t.fnx[0], Is.EqualTo(-14.701768313928573).Within(1e-4));
            Assert.That(t.f2at[0], Is.EqualTo(0.6513242952759531).Within(1e-6));
        }

        [Test]
        public void DetonateFrame10_FieldAndPixelsMatchNumpy()
        {
            // frame(detonate, tab, 10/30): H.sum 23516.977, H.max 13.760051, T.sum 7727.052, H[92,92] 3.417167, H[60,120] 0.760834;
            // shade: lit 11575, px[92,92] = (252,143,32,247), px[70,110] = (233,87,16,235)
            var c = ExplosiveJetDraws.Detonate();
            var sc = new JetScratch(); sc.Ensure(184 * 184); sc.Clear();
            ExplosiveJetProgram.Default.Frame(c, DetonateFrame(c), 10 / 30.0, sc);
            double sumH = 0, sumT = 0, max = 0;
            for (int i = 0; i < sc.H.Length; i++) { sumH += sc.H[i]; sumT += sc.T[i]; if (sc.H[i] > max) max = sc.H[i]; }
            Assert.That(sumH, Is.EqualTo(23516.977).Within(0.1));
            Assert.That(max, Is.EqualTo(13.760051).Within(1e-4));
            Assert.That(sumT, Is.EqualTo(7727.052).Within(0.1));
            Assert.That(sc.H[92 * 184 + 92], Is.EqualTo(3.417167).Within(1e-4));
            Assert.That(sc.H[60 * 184 + 120], Is.EqualTo(0.760834).Within(1e-4));
            var target = new Color32[184 * 184];
            ExplosiveJetShade.Default.Shade(c, JetShade.Bake(c.ramp), JetShade.Bake(c.sootRamp), sc.H, sc.T, 184, 184, target);
            int lit = 0; foreach (var px in target) if (px.a > 0) lit++;
            Assert.That(lit, Is.EqualTo(11575).Within(2));
            Color32 Px(int row, int col) => target[(184 - 1 - row) * 184 + col];
            AssertPx(Px(92, 92), 252, 143, 32, 247);
            AssertPx(Px(70, 110), 233, 87, 16, 235);
        }

        [Test]
        public void DetonateFrame0_IsTheInstantBeforeTheBang()
        {
            // the loop seam of a detonation is the dark frame before the bang: frame 0 of detonate renders nothing
            var f = new ExplosiveJetForm();
            f.ResolveGeometry();
            var target = new Color32[184 * 184];
            f.Render(Ctx(184, 30, 0, 101), target);
            int n = 0; foreach (var c in target) if (c.a > 0) n++;
            Assert.That(n, Is.EqualTo(0));
        }

        static void AssertPx(Color32 c, int r, int g, int b, int a)
        {
            Assert.That((int)c.r, Is.EqualTo(r).Within(1)); Assert.That((int)c.g, Is.EqualTo(g).Within(1));
            Assert.That((int)c.b, Is.EqualTo(b).Within(1)); Assert.That((int)c.a, Is.EqualTo(a).Within(1));
        }

        [TestCase(ExplosiveJetForm.Variant.Detonate, 101, 184, 184, 30, 8770)]
        [TestCase(ExplosiveJetForm.Variant.Backdraft, 277, 196, 224, 32, 8256)]
        [TestCase(ExplosiveJetForm.Variant.Chain, 241, 296, 168, 32, 5671)]
        [TestCase(ExplosiveJetForm.Variant.Frag, 269, 184, 152, 30, 1767)]
        [TestCase(ExplosiveJetForm.Variant.Fuelair, 257, 176, 200, 32, 4426)]
        [TestCase(ExplosiveJetForm.Variant.Lash, 283, 184, 184, 32, 9045)]
        [TestCase(ExplosiveJetForm.Variant.Muzzle, 223, 232, 156, 28, 250)]
        [TestCase(ExplosiveJetForm.Variant.Shatter, 211, 216, 216, 30, 11694)]
        [TestCase(ExplosiveJetForm.Variant.Shockfront, 233, 232, 208, 30, 7969)]
        [TestCase(ExplosiveJetForm.Variant.Starshell, 293, 208, 208, 30, 4737)]
        public void EveryDraw_Frame5LitCountMatchesNumpy(ExplosiveJetForm.Variant v, int seed, int w, int h, int frames, int lit)
        {
            var f = new ExplosiveJetForm { variant = v };
            f.ResolveGeometry();
            int S = Mathf.Max(w, h);
            var target = new Color32[S * S];
            f.Render(Ctx(S, frames, 5, seed), target);
            int n = 0; foreach (var c in target) if (c.a > 0) n++;
            Assert.That(n, Is.EqualTo(lit).Within(2), "lit pixels at frame 5 (±2 = a despeckle / threshold flip from float32 vs double)");
        }

        [Test]
        public void PlateGeom_Shatter_MatchesNumpy_ThroughTheSlotTable()
        {
            // _plate_geom(shatter, seed 211): edges [0, 1.15088, 3.12514, 5.14361, 2π], broke [1], openk [1.25432, 0.95209, 0.92936, 0.97958],
            // seam [0.71632, 2.26828, 4.07570, 5.53839] — observed through the table: every puff's crack age lies in
            // frac_at + [0, stagger], its second crack never before the first + 0.02, and the cut is across the bisector (|fn| ≤ cut·w·1.7)
            var s = ExplosiveJetDraws.Shatter();
            var t = (ExplosiveJetProgram.Slots)ExplosiveJetProgram.Default.BuildSlots(s, 211);
            double cutMax = s.fracture.cut * s.w * 1.70 + 1e-6;
            for (int i = 0; i < t.N; i++)
            {
                Assert.That(t.fat[i], Is.InRange(s.fracture.at, s.fracture.at + s.fracture.stagger + 1e-6));
                Assert.That(t.f2at[i], Is.GreaterThanOrEqualTo(t.fat[i] + 0.02 - 1e-9));
                Assert.That(System.Math.Sqrt(t.fnx[i] * t.fnx[i] + t.fny[i] * t.fny[i]), Is.LessThanOrEqualTo(cutMax));
            }
            int gripped = 0; foreach (var x in t.fx) if (x > 0) gripped++;
            Assert.That(gripped / (double)t.N, Is.EqualTo(s.fracture.grip).Within(0.06), "frac_grip is a per-puff Bernoulli");
        }

        [Test]
        public void Render_ColdVsWarm_IsIdentical_AndCloneRendersTheSame()
        {
            var f = new ExplosiveJetForm();
            var a = new Color32[64 * 64]; var b = new Color32[64 * 64]; var c = new Color32[64 * 64];
            f.Render(Ctx(64, 12, 5, 7), a);
            f.Render(Ctx(64, 12, 2, 7), c);
            f.Render(Ctx(64, 12, 5, 7), b);
            var clone = (ExplosiveJetForm)f.Clone();
            clone.Render(Ctx(64, 12, 5, 7), c);
            int lit = 0;
            for (int i = 0; i < a.Length; i++)
            {
                Assert.That(b[i].Equals(a[i]), "cold vs warm");
                Assert.That(c[i].Equals(a[i]), "clone");
                if (a[i].a > 0) lit++;
            }
            Assert.That(lit, Is.GreaterThan(100), "a 64 px detonation is visible at frame 5");
            Assert.That(clone.ContentHash(), Is.EqualTo(f.ContentHash()));
            Assert.That(ReferenceEquals(clone.detonate, f.detonate), Is.False, "the box is deep-copied");
            Assert.That(ReferenceEquals(clone.detonate.blasts, f.detonate.blasts), Is.False, "the schedule is deep-copied");
            Assert.That(ReferenceEquals(clone.detonate.fracture, f.detonate.fracture), Is.False, "the concern boxes are deep-copied");
        }

        [Test]
        public void Swarm_HandsEachParticleOneBlastCentredOnIt()
        {
            // chain has three blasts with seat offsets; swarm instance i gets blast i mod 3 alone, at its phase and
            // violence, offset zeroed, every other dial the box's
            var f = new ExplosiveJetForm { variant = ExplosiveJetForm.Variant.Chain };
            var box = f.chain;
            var swarm = new PyreSwarmInstance[4];
            for (int i = 0; i < 4; i++) swarm[i] = new PyreSwarmInstance { index = i, x = 16 + 10 * i, y = 32, own = 0.5f, sizeMul = 1f, brightMul = 1f };
            var target = new Color32[64 * 64];
            var ctx = new PyreFormCtx(64, 64, 5 / 11f, 3, 0, null, 1f, swarm, null, null, 0f, 5, 12);
            f.Render(ctx, target);
            int lit = 0; foreach (var c in target) if (c.a > 0) lit++;
            Assert.That(lit, Is.GreaterThan(0), "a swarm of chains renders");
            Assert.That(box.blasts.Count, Is.EqualTo(3), "the box's own schedule is untouched by instancing");
            Assert.That(box.blasts[0].offX, Is.EqualTo(-0.245f).Within(1e-6), "the box's seat offsets are untouched");
        }

        [Test]
        public void SetContractParam_ReachesTheScheduleAndTheBoxedKeys()
        {
            var f = new ExplosiveJetForm();
            Assert.That(f.SetContractParam("draw", "frag"), Is.True);
            Assert.That(f.variant, Is.EqualTo(ExplosiveJetForm.Variant.Frag));
            Assert.That(f.SetContractParam("blast_at", new List<object> { 0.0, 0.5 }), Is.True);
            Assert.That(f.frag.blasts.Count, Is.EqualTo(2)); Assert.That(f.frag.blasts[1].at, Is.EqualTo(0.5f));
            Assert.That(f.SetContractParam("blast_pow", new List<object> { 1.2, 0.7 }), Is.True); Assert.That(f.frag.blasts[1].pow, Is.EqualTo(0.7f));
            Assert.That(f.SetContractParam("blast_share", new List<object>()), Is.True, "an empty column is the source's default");
            Assert.That(f.frag.blasts[0].share, Is.EqualTo(1f));
            Assert.That(f.SetContractParam("blast_off", new List<object> { new List<object> { 0.1, -0.2 }, new List<object> { 0.0, 0.0 } }), Is.True);
            Assert.That(f.frag.blasts[0].offY, Is.EqualTo(-0.2f));
            Assert.That(f.SetContractParam("frac_p", 0.5), Is.True); Assert.That(f.frag.fracture.chance, Is.EqualTo(0.5f));
            Assert.That(f.SetContractParam("frac_n", 7L), Is.True); Assert.That(f.frag.fracture.pieces, Is.EqualTo(7));
            Assert.That(f.SetContractParam("frac_open", 0.9), Is.True); Assert.That(f.frag.fracture.open, Is.EqualTo(0.9f));
            Assert.That(f.SetContractParam("frac2_p", 0.3), Is.True); Assert.That(f.frag.fracture2.chance, Is.EqualTo(0.3f));
            Assert.That(f.SetContractParam("frac2_stagger", 0.2), Is.True); Assert.That(f.frag.fracture2.stagger, Is.EqualTo(0.2f));
            Assert.That(f.SetContractParam("flash_r", 5.0), Is.True); Assert.That(f.frag.flash.radius, Is.EqualTo(5f));
            Assert.That(f.SetContractParam("flash_elong", 2.0), Is.True); Assert.That(f.frag.flash.elong, Is.EqualTo(2f));
            Assert.That(f.SetContractParam("chunk_n", 9L), Is.True); Assert.That(f.frag.chunks.count, Is.EqualTo(9));
            Assert.That(f.SetContractParam("gob_r", 3.0), Is.True); Assert.That(f.frag.gobs.radius, Is.EqualTo(3f));
            Assert.That(f.SetContractParam("gob_early", 0.6), Is.True); Assert.That(f.frag.gobs.early, Is.EqualTo(0.6f));
            Assert.That(f.SetContractParam("dust_n", 40L), Is.True); Assert.That(f.frag.dust.count, Is.EqualTo(40));
            Assert.That(f.SetContractParam("dust_where", 0.7), Is.True); Assert.That(f.frag.dust.where, Is.EqualTo(0.7f));
            Assert.That(f.SetContractParam("vel_spread", 0.4), Is.True); Assert.That(f.frag.velSpread, Is.EqualTo(0.4f));
            Assert.That(f.SetContractParam("lead_die", 0.3), Is.True); Assert.That(f.frag.leadDie, Is.EqualTo(0.3f));
            Assert.That(f.SetContractParam("opaq", 0.6), Is.True); Assert.That(f.frag.opaq, Is.EqualTo(0.6f));
            Assert.That(f.SetContractParam("ring_arc", 44.0), Is.True); Assert.That(f.frag.ringArc, Is.EqualTo(44f));
            Assert.That(f.SetContractParam("ramp", "CORDITE"), Is.True);
            Assert.That(f.frag.sootLo.staticValue, Is.EqualTo(0.34f)); Assert.That(f.frag.sootHi.staticValue, Is.EqualTo(0.96f));
            Assert.That(f.SetContractParam("draw", "corona"), Is.False, "a radial name is not an explosive variant");
        }

        [Test]
        public void Presets_MatchTheContractRamps()
        {
            // gen.py: CORDITE stop 0 = (10,10,34) a 0.20, stop 7 = (252,254,255) a 1; its soot ramp has 5 stops, window 0.34..0.96;
            // TOXIC stop 2 = (30,112,30) a 0.62, no secondary
            var c = JetShade.Bake(PyreRampPresets.JetCordite());
            Assert.That((int)c.sr[0], Is.EqualTo(10)); Assert.That((int)c.sg[0], Is.EqualTo(10)); Assert.That((int)c.sb[0], Is.EqualTo(34)); Assert.That(c.a[0], Is.EqualTo(0.20).Within(1e-6));
            Assert.That((int)c.sr[1023], Is.EqualTo(252)); Assert.That((int)c.sg[1023], Is.EqualTo(254)); Assert.That((int)c.sb[1023], Is.EqualTo(255));
            Assert.That(PyreRampPresets.JetSecondary("CORDITE").stops.Count, Is.EqualTo(5));
            Assert.That(PyreRampPresets.JetSootWindow("CORDITE"), Is.EqualTo(new Vector2(0.34f, 0.96f)));
            var t = PyreRampPresets.Jet("TOXIC");
            Assert.That(t.stops.Count, Is.EqualTo(9));
            Assert.That(t.stops[2].pos, Is.EqualTo(0.25f).Within(1e-6));
            Assert.That(Mathf.RoundToInt(t.stops[2].color.r * 255f), Is.EqualTo(30)); Assert.That(Mathf.RoundToInt(t.stops[2].color.g * 255f), Is.EqualTo(112)); Assert.That(Mathf.RoundToInt(t.stops[2].color.b * 255f), Is.EqualTo(30));
            Assert.That(t.stops[2].color.a, Is.EqualTo(0.62f).Within(1e-6));
            Assert.That(PyreRampPresets.JetSecondary("TOXIC").IsEmpty, Is.True);
        }

        [Test]
        public void EveryDrawHoldsItsFittedExposure_NeverTheClassDefault()
        {
            // the contract's tune2-fitted hi / curve, frozen per draw (Appendix D row: per-FRAME fit vs once-per-CLIP)
            Assert.That(ExplosiveJetDraws.Detonate().hi.staticValue, Is.EqualTo(7.788f)); Assert.That(ExplosiveJetDraws.Detonate().curve.staticValue, Is.EqualTo(0.55f));
            Assert.That(ExplosiveJetDraws.Backdraft().hi.staticValue, Is.EqualTo(15.116f)); Assert.That(ExplosiveJetDraws.Backdraft().curve.staticValue, Is.EqualTo(0.439f));
            Assert.That(ExplosiveJetDraws.Starshell().hi.staticValue, Is.EqualTo(3.157f)); Assert.That(ExplosiveJetDraws.Starshell().curve.staticValue, Is.EqualTo(0.553f));
            foreach (var s in new[] { ExplosiveJetDraws.Chain(), ExplosiveJetDraws.Frag(), ExplosiveJetDraws.Fuelair(), ExplosiveJetDraws.Lash(), ExplosiveJetDraws.Muzzle(), ExplosiveJetDraws.Shatter(), ExplosiveJetDraws.Shockfront() })
            {
                Assert.That(s.hi.staticValue, Is.Not.EqualTo(1.2f)); Assert.That(s.curve.staticValue, Is.Not.EqualTo(1f)); Assert.That(s.lo.staticValue, Is.EqualTo(0.2f));
            }
        }
    }
}
