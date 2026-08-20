using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Newtonsoft.Json.Linq;
using Laubrary.PyrePlus;
using UnityEngine;

namespace Laubrary.PyrePlus.Tests
{
    public class PlusShadeTests
    {
        const string DetonateContract = @"D:\Claude@GDrive\Flame\GEN7\contract\agent3_fork_explosive\detonate";

        static int B(float c) => Mathf.RoundToInt(Mathf.Clamp01(c) * 255f);

        [Test]
        public void Lut_EndpointsAreTheEndStops_AndStopsAreExact()
        {
            var ramp = PlusRampPresets.Ember();
            var lut = PlusShade.BakeLut(ramp, 256);
            Assert.That(lut.Size, Is.EqualTo(256));
            var c0 = lut.Sample(0f); var c1 = lut.Sample(1f);
            Assert.That((B(c0.r), B(c0.g), B(c0.b)), Is.EqualTo((40, 4, 3)));
            Assert.That(c0.a, Is.EqualTo(0.18f).Within(1e-4f));
            Assert.That((B(c1.r), B(c1.g), B(c1.b)), Is.EqualTo((255, 252, 238)));
            Assert.That(c1.a, Is.EqualTo(1f).Within(1e-4f));
            // every stop evaluates to itself exactly (no blending happens ON a stop)
            foreach (var s in ramp.stops)
            {
                var c = ramp.Evaluate(s.pos);
                Assert.That((B(c.r), B(c.g), B(c.b)), Is.EqualTo((B(s.color.r), B(s.color.g), B(s.color.b))), $"stop at {s.pos}");
                Assert.That(c.a, Is.EqualTo(s.color.a).Within(1e-5f));
            }
        }

        [Test]
        public void Blend_LinearLightVsSrgb_KnownCase()
        {
            // Appendix D §A6 #34: EMBER stops 0.20 (166,26,9) → 0.71 (255,168,44), midpoint = (217,124,31) in linear
            // light vs (210,97,26) with an sRGB lerp — the sRGB path is visibly browner.
            var a = new Color(166 / 255f, 26 / 255f, 9 / 255f, 0.62f);
            var b = new Color(255 / 255f, 168 / 255f, 44 / 255f, 0.97f);
            var lin = PlusShade.Blend(a, b, 0.5f, PlusRampSpace.LinearLight);
            var srgb = PlusShade.Blend(a, b, 0.5f, PlusRampSpace.Srgb);
            Assert.That(B(lin.r), Is.EqualTo(217).Within(1)); Assert.That(B(lin.g), Is.EqualTo(124).Within(1)); Assert.That(B(lin.b), Is.EqualTo(31).Within(1));
            Assert.That(B(srgb.r), Is.EqualTo(210).Within(1)); Assert.That(B(srgb.g), Is.EqualTo(97).Within(1)); Assert.That(B(srgb.b), Is.EqualTo(26).Within(1));
            Assert.That(lin.a, Is.EqualTo(0.795f).Within(1e-4f), "alpha is linear in both spaces");
            Assert.That(srgb.a, Is.EqualTo(0.795f).Within(1e-4f));
        }

        [Test]
        public void SrgbRoundTrip()
        {
            for (float c = 0f; c <= 1f; c += 0.05f)
                Assert.That(PlusShade.LinearToSrgb(PlusShade.SrgbToLinear(c)), Is.EqualTo(c).Within(1e-5f));
        }

        [Test]
        public void EmberPreset_MatchesTheContractLut_WhenTheContractIsAvailable()
        {
            string rampPath = Path.Combine(DetonateContract, "ramp.json");
            if (!File.Exists(rampPath)) Assert.Ignore("gen-7 detonate contract not present on this machine");
            var doc = JObject.Parse(File.ReadAllText(rampPath));
            var ember = (JObject)doc["ramps"]["EMBER"];
            int size = ember.Value<int>("lut_size");
            var rgb = (JArray)ember["lut"]["rgb"]; var alpha = (JArray)ember["lut"]["alpha"];
            var lut = PlusShade.BakeLut(PlusRampPresets.Ember(), size);
            int maxDiff = 0; float maxA = 0f;
            for (int i = 0; i < size; i++)
            {
                var c = lut.rgba[i];
                var e = (JArray)rgb[i];
                maxDiff = Mathf.Max(maxDiff, Mathf.Abs(B(c.r) - e[0].Value<int>()), Mathf.Abs(B(c.g) - e[1].Value<int>()), Mathf.Abs(B(c.b) - e[2].Value<int>()));
                maxA = Mathf.Max(maxA, Mathf.Abs(c.a - alpha[i].Value<float>()));
            }
            Assert.That(maxDiff, Is.LessThanOrEqualTo(1), "RGB within one 8-bit step of Kiln's baked LUT over all entries");
            Assert.That(maxA, Is.LessThan(0.005f));
        }

        [Test]
        public void Banded_ProducesExactlyNColours()
        {
            var bands = new[] { new Color32(10, 0, 0, 255), new Color32(0, 20, 0, 255), new Color32(0, 0, 30, 255), new Color32(40, 40, 40, 255), new Color32(50, 0, 50, 255) };
            var seen = new HashSet<Color32>();
            for (int i = 0; i <= 1000; i++) seen.Add(PlusShade.Banded(i / 1000f, bands));
            Assert.That(seen.Count, Is.EqualTo(5));
            Assert.That(PlusShade.Banded(0f, bands), Is.EqualTo(bands[0]));
            Assert.That(PlusShade.Banded(1f, bands), Is.EqualTo(bands[4]));
            Assert.That(PlusShade.Banded(0.2f, bands), Is.EqualTo(bands[1]));
            var thr = new[] { 0f, 0.5f, 0.9f };
            Assert.That(PlusShade.Banded(0.49f, thr, bands), Is.EqualTo(bands[0]));
            Assert.That(PlusShade.Banded(0.5f, thr, bands), Is.EqualTo(bands[1]));
            Assert.That(PlusShade.Banded(0.95f, thr, bands), Is.EqualTo(bands[2]));
        }

        [Test]
        public void Quantise_MatchesKilnStepsFormula()
        {
            Assert.That(PlusShade.Quantise(0.3f, 0), Is.EqualTo(0.3f));
            Assert.That(PlusShade.Quantise(0.0f, 4), Is.EqualTo(0f));
            Assert.That(PlusShade.Quantise(0.26f, 4), Is.EqualTo(1f / 3f).Within(1e-6f));
            Assert.That(PlusShade.Quantise(1.0f, 4), Is.EqualTo(1f));
        }

        [Test]
        public void DualRamp_CrossfadesThroughTheWindow()
        {
            var hot = PlusShade.BakeLut(PlusRampPresets.Ember());
            var soot = PlusShade.BakeLut(PlusRampPresets.EmberSoot());
            float t = 0.6f;
            Assert.That(PlusShade.DualRamp(hot, soot, t, 0.1f, 0.28f, 0.92f), Is.EqualTo(hot.Sample(t)));
            Assert.That(PlusShade.DualRamp(hot, soot, t, 0.95f, 0.28f, 0.92f), Is.EqualTo(soot.Sample(t)));
            var mid = PlusShade.DualRamp(hot, soot, t, 0.6f, 0.28f, 0.92f);
            Assert.That(mid.r, Is.LessThan(hot.Sample(t).r).And.GreaterThan(soot.Sample(t).r));
        }

        [Test]
        public void Palette2D_SamplesNearestCell()
        {
            var cells = new Color32[6];
            for (int i = 0; i < 6; i++) cells[i] = new Color32((byte)i, 0, 0, 255);
            var pal = new PlusShade.Palette2D(2, 3, cells);
            Assert.That(pal.Sample(0f, 0f).r, Is.EqualTo(0));
            Assert.That(pal.Sample(0f, 0.99f).r, Is.EqualTo(2));
            Assert.That(pal.Sample(1f, 1f).r, Is.EqualTo(5));
            Assert.That(pal.Sample(0.6f, 0.4f).r, Is.EqualTo(4));
        }

        [Test]
        public void AdditiveEmissive_ToneMapsPerChannel_AndBlowsOutToWhite()
        {
            var dim = PlusShade.AdditiveEmissive(0.1f, 0f, 0f, 1f, 0f, 1f, toSrgb: false);
            Assert.That(dim.r, Is.EqualTo(1f - Mathf.Exp(-0.1f)).Within(1e-6f));
            Assert.That(dim.g, Is.EqualTo(0f)); Assert.That(dim.b, Is.EqualTo(0f));
            Assert.That(dim.a, Is.EqualTo(dim.r).Within(1e-6f));
            var hot = PlusShade.AdditiveEmissive(50f, 5f, 0.5f, 1f, 2f, 1f, toSrgb: false);
            Assert.That(hot.r, Is.GreaterThan(0.99f)); Assert.That(hot.g, Is.GreaterThan(0.99f)); Assert.That(hot.b, Is.GreaterThan(0.9f), "blow-out drags the dark channel up");
            var hotNoBlow = PlusShade.AdditiveEmissive(50f, 5f, 0.5f, 1f, 0f, 1f, toSrgb: false);
            Assert.That(hotNoBlow.b, Is.LessThan(0.5f), "without blow-out the dark channel stays dark");
        }

        [Test]
        public void BakeLut_FromGradient_LinearLight_DiffersFromGammaLerp()
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(new Color(166 / 255f, 26 / 255f, 9 / 255f), 0f), new GradientColorKey(new Color(1f, 168 / 255f, 44 / 255f), 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            var lin = PlusShade.BakeLut(g, true, 256);
            var gam = PlusShade.BakeLut(g, false, 256);
            Assert.That(B(lin.Sample(0.5f).g), Is.EqualTo(124).Within(2));
            Assert.That(B(gam.Sample(0.5f).g), Is.EqualTo(97).Within(2));
        }

        [Test]
        public void PlusRamp_Clone_DoesNotAlias_AndFormCloneDeepCopiesIt()
        {
            var r = PlusRampPresets.Ember();
            var c = r.Clone();
            Assert.That(c.stops, Is.Not.SameAs(r.stops));
            Assert.That(c.stops[0], Is.Not.SameAs(r.stops[0]));
            c.stops[0].color = Color.blue;
            Assert.That(r.stops[0].color, Is.Not.EqualTo(Color.blue));
        }
    }
}
