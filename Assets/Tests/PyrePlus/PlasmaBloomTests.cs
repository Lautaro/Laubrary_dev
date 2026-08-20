using System.IO;
using NUnit.Framework;
using Newtonsoft.Json.Linq;
using Laubrary.PyrePlus;
using Laubrary.PyrePlus.Forms.Kiln;
using UnityEngine;

namespace Laubrary.PyrePlus.Tests
{
    /// Port 01 — Plasma Bloom (Energy Explosion / agent3_fork gen 5): the ramp presets against the contract's own
    /// LUTs, and the form's determinism / fit / clone / contract-loading surface.
    public class PlasmaBloomTests
    {
        const string ContractRoot = @"D:\Claude@GDrive\Energy Explosion\GEN5\contract\agent3_fork";

        static int B(float c) => Mathf.RoundToInt(Mathf.Clamp01(c) * 255f);

        [TestCase("detonate", "ion", "cryo")]
        [TestCase("shockring", "cryo", "volt")]
        [TestCase("lance", "toxin", "cryo")]
        [TestCase("crown", "flare", "ion")]
        public void PlasmaPresets_MatchTheContractLuts(string draw, string hueA, string hueB)
        {
            string rampPath = Path.Combine(ContractRoot, draw, "ramp.json");
            if (!File.Exists(rampPath)) Assert.Ignore("gen-5 contract not present on this machine");
            var doc = JObject.Parse(File.ReadAllText(rampPath));
            var a = (JObject)doc["ramps"][hueA];
            Check(a, PlusRampPresets.Plasma(hueA), hueA);
            Check((JObject)a["secondary"], PlusRampPresets.Plasma(hueB), hueB);
        }

        static void Check(JObject ramp, PlusRamp preset, string name)
        {
            Assert.That(preset, Is.Not.Null, name);
            Assert.That(ramp.Value<string>("space"), Is.EqualTo("srgb"));
            int size = ramp.Value<int>("lut_size");
            var rgb = (JArray)ramp["lut"]["rgb"];
            var lut = PlusShade.BakeLut(preset, size);
            int maxDiff = 0;
            for (int i = 0; i < size; i++)
            {
                var c = lut.rgba[i]; var e = (JArray)rgb[i];
                maxDiff = Mathf.Max(maxDiff, Mathf.Abs(B(c.r) - e[0].Value<int>()), Mathf.Abs(B(c.g) - e[1].Value<int>()), Mathf.Abs(B(c.b) - e[2].Value<int>()));
            }
            Assert.That(maxDiff, Is.LessThanOrEqualTo(1), name + ": RGB within one 8-bit step of the contract LUT");
        }

        [Test]
        public void Plasma_UnknownNameIsNull() => Assert.That(PlusRampPresets.Plasma("ember"), Is.Null);

        static PlusFormCtx Ctx(int W, int frames, int frame, int seed = 1147) =>
            new PlusFormCtx(W, W, frames > 1 ? frame / (float)(frames - 1) : 0f, seed, 0, null, 1f, null, null, null, 0f, frame, frames);

        [Test]
        public void Render_IsDeterministic_ColdVsWarm()
        {
            var a = new PlasmaBloomForm(); var b = new PlasmaBloomForm();
            var ta = new Color32[48 * 48]; var tb = new Color32[48 * 48];
            a.Render(Ctx(48, 12, 3), ta);
            a.Render(Ctx(48, 12, 7), ta);           // warm the caches with another frame
            a.Render(Ctx(48, 12, 3), ta);
            b.Render(Ctx(48, 12, 3), tb);
            int diff = 0, lit = 0;
            for (int i = 0; i < ta.Length; i++) { if (!ta[i].Equals(tb[i])) diff++; if (ta[i].a > 0) lit++; }
            Assert.That(diff, Is.EqualTo(0));
            Assert.That(lit, Is.GreaterThan(100), "frame 3 of 12 is lit");
        }

        [Test]
        public void Fit_PlacesACentredBlastNearTheMiddle_AndInsideTheFrame()
        {
            var f = new PlasmaBloomForm();
            var t = new Color32[64 * 64];
            f.Render(Ctx(64, 16, 4), t);
            Assert.That(f.TryGetFit(out float rMax, out float ox, out float oy));
            Assert.That(rMax, Is.InRange(16f, 64f), "rMax in canvas px for a 64 canvas");
            Assert.That(Mathf.Abs(ox), Is.LessThan(0.3f)); Assert.That(Mathf.Abs(oy), Is.LessThan(0.3f));
            int W = 64, worst = 0;
            for (int i = 0; i < W; i++) worst = Mathf.Max(worst, t[i].a, t[(W - 1) * W + i].a, t[i * W].a, t[i * W + W - 1].a);
            Assert.That(worst, Is.LessThanOrEqualTo(f.borderCap + 6), "fit grid vs shipped pixels may disagree by a few steps");
        }


        [Test]
        public void FloatDownsample_AveragesPremultipliedOnce_AndFlips()
        {
            // 2x2 block A (top-left in y-down planes): red a=1, blue a=0.5, two empty → (170,0,85) a=0.375; block B (below): white a=1 once
            int W2 = 4, H2 = 4;
            var pr = new float[16]; var pg = new float[16]; var pb = new float[16]; var pa = new float[16];
            pr[0] = 1f; pa[0] = 1f;                 // (0,0) red
            pb[1] = 0.5f; pa[1] = 0.5f;             // (1,0) blue at half alpha, premultiplied
            pr[8] = pg[8] = pb[8] = 1f; pa[8] = 1f; // (0,2) white
            var t = new Color32[4];
            PlusSupersample.Downsample(pr, pg, pb, pa, W2, H2, 2, t, 2, 2, flipY: true);
            // flipY: plane top row → target row 1 (y-up); plane bottom row → target row 0
            Assert.That(t[2], Is.EqualTo(new Color32(170, 0, 85, 96)), "A un-premultiplied block mean, quantised once");
            Assert.That(t[0], Is.EqualTo(new Color32(255, 255, 255, 64)), "B");
            Assert.That(t[1].a, Is.EqualTo(0)); Assert.That(t[3].a, Is.EqualTo(0));
            // the faint-speck case the overload exists for: four samples at 0.4/255 survive as one 1/255 pixel... or not
            var fa = new float[] { 0.3f / 255f, 0.3f / 255f, 0.3f / 255f, 0.3f / 255f }; var fz = new float[4];
            var one = new Color32[1];
            PlusSupersample.Downsample(fz, fz, fz, fa, 2, 2, 2, one, 1, 1);
            Assert.That(one[0].a, Is.EqualTo(0), "mean 0.3/255 rounds to 0 in both paths");
            fa = new float[] { 0.9f / 255f, 0.9f / 255f, 0.4f / 255f, 0.4f / 255f };
            PlusSupersample.Downsample(fz, fz, fz, fa, 2, 2, 2, one, 1, 1);
            Assert.That(one[0].a, Is.EqualTo(1), "mean 0.65/255 → 1; byte-first would average (1,1,0,0) → 0.5 → banker's 0");
        }

        [Test]
        public void NumpyRng_MatchesRecordedStream()
        {
            // values recorded from numpy (default_rng): see PlusNumpyRng.cs header
            var r = new PlusNumpyRng(9083093u);
            Assert.That(r.NextDouble(), Is.EqualTo(0.9001350855194159).Within(1e-15));
            Assert.That(r.NextDouble(), Is.EqualTo(0.07087491162374149).Within(1e-15));
            Assert.That(r.NextDouble(), Is.EqualTo(0.9353797075681033).Within(1e-15));
            r = new PlusNumpyRng(9083093u);
            r.Random(26); var u = r.Random(26); var q = new float[26];
            for (int i = 0; i < 26; i++) q[i] = (i + u[i]) / 26f;
            r.Shuffle(q);
            Assert.That(q[0], Is.EqualTo(0.1697545200586319f).Within(1e-6f));
            Assert.That(q[5], Is.EqualTo(0.5033358335494995f).Within(1e-6f));
            var g = new PlusNumpyRng(1147u).Random(24 * 7);
            Assert.That(g[0], Is.EqualTo(0.6147122383117676f).Within(1e-6f));
            Assert.That(g[167], Is.EqualTo(0.11120189726352692f).Within(1e-6f));
            Assert.That(new PlusNumpyRng(0u).NextDouble(), Is.EqualTo(0.6369616873214543).Within(1e-15));
        }

        [Test]
        public void Clone_DeepCopiesPopulationsAndRamps_AndHashTracks()
        {
            var f = new PlasmaBloomForm();
            var c = (PlasmaBloomForm)f.Clone();
            Assert.That(c.ContentHash(), Is.EqualTo(f.ContentHash()));
            Assert.That(ReferenceEquals(c.chunks, f.chunks), Is.False);
            Assert.That(ReferenceEquals(c.hueA, f.hueA), Is.False);
            c.chunks.n = 5;
            Assert.That(f.chunks.n, Is.EqualTo(26));
            Assert.That(c.ContentHash(), Is.Not.EqualTo(f.ContentHash()));
        }

        [Test]
        public void SetContractParam_ReachesNestedAndTypedKeys()
        {
            var f = new PlasmaBloomForm();
            Assert.That(f.SetContractParam("chunkN", 17.0)); Assert.That(f.chunks.n, Is.EqualTo(17));
            Assert.That(f.SetContractParam("moteRhoHi", 1.06)); Assert.That(f.motes.rhoHi, Is.EqualTo(1.06f).Within(1e-6f));
            Assert.That(f.SetContractParam("mode", "plume")); Assert.That(f.mode, Is.EqualTo(PlasmaBloomForm.Mode.Plume));
            Assert.That(f.SetContractParam("hueA", "toxin")); Assert.That(f.hueA.stops[2].color.g, Is.EqualTo(190 / 255f).Within(1e-3f));
            Assert.That(f.SetContractParam("expRate", 8.6)); Assert.That(f.expRate, Is.EqualTo(8.6f).Within(1e-6f));
            Assert.That(f.SetContractParam("nope", 1.0), Is.False);
        }
    }
}
