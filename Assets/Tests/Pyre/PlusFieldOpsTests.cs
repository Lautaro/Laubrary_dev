using NUnit.Framework;
using Laubrary.Pyre;
using UnityEngine;

namespace Laubrary.Pyre.Tests
{
    public class PyreFieldOpsTests
    {
        const int W = 33, H = 29;

        static float[] Impulse(int x, int y, float v = 1f) { var f = new float[W * H]; f[y * W + x] = v; return f; }

        [Test]
        public void GaussianBlur_PreservesSum_AndIsSymmetric()
        {
            var f = Impulse(16, 14, 10f);
            PyreFieldOps.GaussianBlur(f, W, H, 2.0f);
            Assert.That(PyreFieldOps.Sum(f), Is.EqualTo(10f).Within(1e-3f), "interior impulse keeps its mass");
            for (int d = 1; d <= 5; d++)
            {
                Assert.That(f[14 * W + 16 + d], Is.EqualTo(f[14 * W + 16 - d]).Within(1e-6f), "x-symmetric");
                Assert.That(f[(14 + d) * W + 16], Is.EqualTo(f[(14 - d) * W + 16]).Within(1e-6f), "y-symmetric");
                Assert.That(f[14 * W + 16 + d], Is.EqualTo(f[(14 + d) * W + 16]).Within(1e-6f), "isotropic");
            }
            Assert.That(f[14 * W + 16], Is.GreaterThan(f[14 * W + 17]), "peak at the centre");
        }

        [Test]
        public void GaussianBlur_EdgeClamp_DoesNotLoseMass_OnConstantPlane()
        {
            var f = new float[W * H];
            for (int i = 0; i < f.Length; i++) f[i] = 3f;
            PyreFieldOps.GaussianBlur(f, W, H, 3f);
            foreach (var v in f) Assert.That(v, Is.EqualTo(3f).Within(1e-4f));
        }

        [Test]
        public void BoxBlur_PreservesSum_AndFlattensImpulse()
        {
            var f = Impulse(16, 14, 9f);
            PyreFieldOps.BoxBlur(f, W, H, 1);
            Assert.That(PyreFieldOps.Sum(f), Is.EqualTo(9f).Within(1e-4f));
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                    Assert.That(f[(14 + dy) * W + 16 + dx], Is.EqualTo(1f).Within(1e-5f));
            Assert.That(f[14 * W + 18], Is.EqualTo(0f).Within(1e-6f));
        }

        [Test]
        public void BoxBlur_ThreePasses_ApproachesGaussian()
        {
            var a = Impulse(16, 14, 1f); var b = Impulse(16, 14, 1f);
            // three box passes of half-width r approximate a Gaussian of sigma = sqrt(3·((2r+1)² − 1)/12) = sqrt(6) for r = 2
            PyreFieldOps.BoxBlur(a, W, H, 2, passes: 3);
            PyreFieldOps.GaussianBlur(b, W, H, Mathf.Sqrt(6f));
            float maxDiff = 0f;
            for (int i = 0; i < a.Length; i++) maxDiff = Mathf.Max(maxDiff, Mathf.Abs(a[i] - b[i]));
            Assert.That(maxDiff, Is.LessThan(0.01f));
        }

        [Test]
        public void SampleBilinear_IsExactAtPixelCentres_AndHalfwayBetween()
        {
            var f = new float[W * H];
            f[5 * W + 5] = 2f; f[5 * W + 6] = 4f;
            Assert.That(PyreFieldOps.SampleBilinear(f, W, H, 5f, 5f), Is.EqualTo(2f));
            Assert.That(PyreFieldOps.SampleBilinear(f, W, H, 5.5f, 5f), Is.EqualTo(3f).Within(1e-6f));
            Assert.That(PyreFieldOps.SampleBilinear(f, W, H, -3f, 5f), Is.EqualTo(0f), "clamps to the edge pixel");
        }

        [Test]
        public void Warp_ShiftsByOffsetPlanes()
        {
            var src = Impulse(10, 10, 1f);
            var dst = new float[W * H];
            var dx = new float[W * H]; var dy = new float[W * H];
            for (int i = 0; i < dx.Length; i++) { dx[i] = 2f; dy[i] = -1f; }   // sample from (x+2, y-1)
            PyreFieldOps.Warp(src, dst, W, H, dx, dy);
            Assert.That(dst[11 * W + 8], Is.EqualTo(1f).Within(1e-6f));
            Assert.That(dst[10 * W + 10], Is.EqualTo(0f));
        }

        [Test]
        public void SmearIIR_TrailsAlongAngle_WithGeometricDecay()
        {
            var f = Impulse(5, 14, 1f);
            PyreFieldOps.SmearIIR(f, W, H, 0f, 0.5f);   // trail toward +x
            Assert.That(f[14 * W + 5], Is.EqualTo(1f).Within(1e-6f));
            Assert.That(f[14 * W + 6], Is.EqualTo(0.5f).Within(1e-6f));
            Assert.That(f[14 * W + 7], Is.EqualTo(0.25f).Within(1e-6f));
            Assert.That(f[14 * W + 4], Is.EqualTo(0f), "nothing upstream");
            var g = Impulse(16, 5, 1f);
            PyreFieldOps.SmearIIR(g, W, H, 90f, 0.5f);  // trail toward +y
            Assert.That(g[6 * W + 16], Is.EqualTo(0.5f).Within(1e-6f));
        }

        [Test]
        public void Smear_NTap_IsNormalised()
        {
            var src = new float[W * H]; for (int i = 0; i < src.Length; i++) src[i] = 2f;
            var dst = new float[W * H];
            PyreFieldOps.Smear(src, dst, W, H, 37f, 6f, 5);
            foreach (var v in dst) Assert.That(v, Is.EqualTo(2f).Within(1e-4f));
        }

        [Test]
        public void Bloom_AddsBlurredCopy()
        {
            var f = Impulse(16, 14, 1f);
            PyreFieldOps.Bloom(f, W, H, 1.5f, 0.5f);
            Assert.That(PyreFieldOps.Sum(f), Is.EqualTo(1.5f).Within(1e-3f));
            Assert.That(f[14 * W + 17], Is.GreaterThan(0f));
        }

        [Test]
        public void ValueNoise_IsTileable_AtPeriod_AndDeterministic()
        {
            const int period = 8;
            for (int i = 0; i < 50; i++)
            {
                float x = i * 0.37f, y = i * 0.91f;
                float a = PyreFieldOps.ValueNoise2D(x, y, 42, period);
                Assert.That(PyreFieldOps.ValueNoise2D(x + period, y, 42, period), Is.EqualTo(a).Within(1e-5f));
                Assert.That(PyreFieldOps.ValueNoise2D(x, y - 2 * period, 42, period), Is.EqualTo(a).Within(1e-5f));
                Assert.That(PyreFieldOps.ValueNoise2D(x, y, 42, period), Is.EqualTo(a), "same inputs ⇒ same value");
                Assert.That(a, Is.InRange(0f, 1f));
                float f = PyreFieldOps.Fbm2D(x, y, 7, 4, 2f, 0.5f, period);
                Assert.That(PyreFieldOps.Fbm2D(x + period, y + period, 7, 4, 2f, 0.5f, period), Is.EqualTo(f).Within(1e-5f), "fbm tiles");
                float n3 = PyreFieldOps.ValueNoise3D(x, y, i * 0.13f, 9, period);
                Assert.That(PyreFieldOps.ValueNoise3D(x, y + period, i * 0.13f + period, 9, period), Is.EqualTo(n3).Within(1e-5f), "3D tiles");
            }
            Assert.That(PyreFieldOps.ValueNoise2D(3.3f, 4.4f, 1), Is.Not.EqualTo(PyreFieldOps.ValueNoise2D(3.3f, 4.4f, 2)), "seed matters");
        }

        [Test]
        public void ValueNoise_IsContinuous_AcrossLatticeLines()
        {
            float a = PyreFieldOps.ValueNoise2D(2.9999f, 1.5f, 3), b = PyreFieldOps.ValueNoise2D(3.0001f, 1.5f, 3);
            Assert.That(Mathf.Abs(a - b), Is.LessThan(1e-3f));
        }

        [Test]
        public void Curl_IsDivergenceFree_Approximately()
        {
            float eps = 0.01f;
            for (int i = 0; i < 10; i++)
            {
                float x = 1.3f + i * 0.7f, y = 2.1f + i * 0.3f;
                var cx1 = PyreFieldOps.Curl2D(x + eps, y, 5, 3); var cx0 = PyreFieldOps.Curl2D(x - eps, y, 5, 3);
                var cy1 = PyreFieldOps.Curl2D(x, y + eps, 5, 3); var cy0 = PyreFieldOps.Curl2D(x, y - eps, 5, 3);
                float div = (cx1.x - cx0.x) / (2 * eps) + (cy1.y - cy0.y) / (2 * eps);
                Assert.That(Mathf.Abs(div), Is.LessThan(0.5f), $"divergence at ({x},{y}) = {div}");
            }
        }

        [Test]
        public void Stats_MatchKnownQuantiles()
        {
            var f = new float[101];
            for (int i = 0; i <= 100; i++) f[i] = i;   // 0..100
            var s = PyreFieldOps.ComputeStats(f, lo: 0f);  // lit = values > 0 → 1..100
            Assert.That(s.litPx, Is.EqualTo(100));
            Assert.That(s.peak, Is.EqualTo(100f));
            Assert.That(s.q50, Is.EqualTo(50.5f).Within(1e-4f));
            Assert.That(s.q90, Is.EqualTo(90.1f).Within(1e-4f));
            Assert.That(s.q99, Is.EqualTo(99.01f).Within(1e-3f));
            Assert.That(s.mean, Is.EqualTo(50.5f).Within(1e-4f));
        }

        [Test]
        public void MultiplyNoise_KeepsMeanRoughly_AndAmountZeroIsNoop()
        {
            var f = new float[W * H]; for (int i = 0; i < f.Length; i++) f[i] = 1f;
            var g = (float[])f.Clone();
            PyreFieldOps.MultiplyNoise(g, W, H, 4f, 0f, 1);
            CollectionAssert.AreEqual(f, g);
            PyreFieldOps.MultiplyNoise(g, W, H, 4f, 0.5f, 1);
            float mean = PyreFieldOps.Sum(g) / g.Length;
            Assert.That(mean, Is.EqualTo(1f).Within(0.15f));
            bool varied = false; foreach (var v in g) if (Mathf.Abs(v - 1f) > 0.05f) varied = true;
            Assert.That(varied);
        }

        [Test]
        public void SmearShiftX_TrailsTowardMinusXWithGeometricWeights()
        {
            // One lit pixel at x = 5: normalised, the trail appears at x = 5, 4, 3 with weights 1, d, d² / (1 + d + d²);
            // nothing to the RIGHT of the source (the leading edge stays crisp); unnormalised, the raw weights.
            int W = 8, H = 1; float d = 0.5f;
            var f = new float[W]; f[5] = 1f;
            PyreFieldOps.SmearShiftX(f, W, H, 2, d, normalise: false);
            Assert.That(f[5], Is.EqualTo(1f).Within(1e-6f)); Assert.That(f[4], Is.EqualTo(d).Within(1e-6f));
            Assert.That(f[3], Is.EqualTo(d * d).Within(1e-6f)); Assert.That(f[2], Is.EqualTo(0f)); Assert.That(f[6], Is.EqualTo(0f));
            var g = new float[W]; g[5] = 1f;
            PyreFieldOps.SmearShiftX(g, W, H, 2, d, normalise: true);
            float tot = 1f + d + d * d;
            Assert.That(g[5] + g[4] + g[3], Is.EqualTo(1f).Within(1e-6f));
            Assert.That(g[4], Is.EqualTo(d / tot).Within(1e-6f));
            // a source at the right edge has nothing beyond it to pull from: zero-filled, so its value is just its own weight
            var e = new float[W]; e[7] = 1f;
            PyreFieldOps.SmearShiftX(e, W, H, 2, d, normalise: true);
            Assert.That(e[7], Is.EqualTo(1f / tot).Within(1e-6f)); Assert.That(e[6], Is.EqualTo(d / tot).Within(1e-6f));
        }

        [Test]
        public void BinomialBlur_IsZeroPaddedAndKeepsInteriorMass()
        {
            // A lone pixel in the middle of a 5×5 spreads into the [1 2 1]⊗[1 2 1]/16 kernel; one at the corner loses the
            // mass that falls off the edge (zero padding, never a clamp).
            int W = 5, H = 5;
            var f = new float[W * H]; f[2 * W + 2] = 16f;
            PyreFieldOps.BinomialBlur(f, W, H, 1);
            Assert.That(f[2 * W + 2], Is.EqualTo(4f).Within(1e-5f)); Assert.That(f[2 * W + 1], Is.EqualTo(2f).Within(1e-5f));
            Assert.That(f[1 * W + 1], Is.EqualTo(1f).Within(1e-5f)); Assert.That(f[0], Is.EqualTo(0f));
            float sum = 0f; foreach (var v in f) sum += v;
            Assert.That(sum, Is.EqualTo(16f).Within(1e-4f));
            var c = new float[W * H]; c[0] = 16f;
            PyreFieldOps.BinomialBlur(c, W, H, 1);
            Assert.That(c[0], Is.EqualTo(4f).Within(1e-5f));
            sum = 0f; foreach (var v in c) sum += v;
            Assert.That(sum, Is.EqualTo(9f).Within(1e-4f));   // 4 + 2 + 2 + 1: the three quarters beyond the corner are gone
        }
    }
}
