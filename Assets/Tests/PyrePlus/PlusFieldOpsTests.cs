using NUnit.Framework;
using Laubrary.PyrePlus;
using UnityEngine;

namespace Laubrary.PyrePlus.Tests
{
    public class PlusFieldOpsTests
    {
        const int W = 33, H = 29;

        static float[] Impulse(int x, int y, float v = 1f) { var f = new float[W * H]; f[y * W + x] = v; return f; }

        [Test]
        public void GaussianBlur_PreservesSum_AndIsSymmetric()
        {
            var f = Impulse(16, 14, 10f);
            PlusFieldOps.GaussianBlur(f, W, H, 2.0f);
            Assert.That(PlusFieldOps.Sum(f), Is.EqualTo(10f).Within(1e-3f), "interior impulse keeps its mass");
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
            PlusFieldOps.GaussianBlur(f, W, H, 3f);
            foreach (var v in f) Assert.That(v, Is.EqualTo(3f).Within(1e-4f));
        }

        [Test]
        public void BoxBlur_PreservesSum_AndFlattensImpulse()
        {
            var f = Impulse(16, 14, 9f);
            PlusFieldOps.BoxBlur(f, W, H, 1);
            Assert.That(PlusFieldOps.Sum(f), Is.EqualTo(9f).Within(1e-4f));
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
            PlusFieldOps.BoxBlur(a, W, H, 2, passes: 3);
            PlusFieldOps.GaussianBlur(b, W, H, Mathf.Sqrt(6f));
            float maxDiff = 0f;
            for (int i = 0; i < a.Length; i++) maxDiff = Mathf.Max(maxDiff, Mathf.Abs(a[i] - b[i]));
            Assert.That(maxDiff, Is.LessThan(0.01f));
        }

        [Test]
        public void SampleBilinear_IsExactAtPixelCentres_AndHalfwayBetween()
        {
            var f = new float[W * H];
            f[5 * W + 5] = 2f; f[5 * W + 6] = 4f;
            Assert.That(PlusFieldOps.SampleBilinear(f, W, H, 5f, 5f), Is.EqualTo(2f));
            Assert.That(PlusFieldOps.SampleBilinear(f, W, H, 5.5f, 5f), Is.EqualTo(3f).Within(1e-6f));
            Assert.That(PlusFieldOps.SampleBilinear(f, W, H, -3f, 5f), Is.EqualTo(0f), "clamps to the edge pixel");
        }

        [Test]
        public void Warp_ShiftsByOffsetPlanes()
        {
            var src = Impulse(10, 10, 1f);
            var dst = new float[W * H];
            var dx = new float[W * H]; var dy = new float[W * H];
            for (int i = 0; i < dx.Length; i++) { dx[i] = 2f; dy[i] = -1f; }   // sample from (x+2, y-1)
            PlusFieldOps.Warp(src, dst, W, H, dx, dy);
            Assert.That(dst[11 * W + 8], Is.EqualTo(1f).Within(1e-6f));
            Assert.That(dst[10 * W + 10], Is.EqualTo(0f));
        }

        [Test]
        public void SmearIIR_TrailsAlongAngle_WithGeometricDecay()
        {
            var f = Impulse(5, 14, 1f);
            PlusFieldOps.SmearIIR(f, W, H, 0f, 0.5f);   // trail toward +x
            Assert.That(f[14 * W + 5], Is.EqualTo(1f).Within(1e-6f));
            Assert.That(f[14 * W + 6], Is.EqualTo(0.5f).Within(1e-6f));
            Assert.That(f[14 * W + 7], Is.EqualTo(0.25f).Within(1e-6f));
            Assert.That(f[14 * W + 4], Is.EqualTo(0f), "nothing upstream");
            var g = Impulse(16, 5, 1f);
            PlusFieldOps.SmearIIR(g, W, H, 90f, 0.5f);  // trail toward +y
            Assert.That(g[6 * W + 16], Is.EqualTo(0.5f).Within(1e-6f));
        }

        [Test]
        public void Smear_NTap_IsNormalised()
        {
            var src = new float[W * H]; for (int i = 0; i < src.Length; i++) src[i] = 2f;
            var dst = new float[W * H];
            PlusFieldOps.Smear(src, dst, W, H, 37f, 6f, 5);
            foreach (var v in dst) Assert.That(v, Is.EqualTo(2f).Within(1e-4f));
        }

        [Test]
        public void Bloom_AddsBlurredCopy()
        {
            var f = Impulse(16, 14, 1f);
            PlusFieldOps.Bloom(f, W, H, 1.5f, 0.5f);
            Assert.That(PlusFieldOps.Sum(f), Is.EqualTo(1.5f).Within(1e-3f));
            Assert.That(f[14 * W + 17], Is.GreaterThan(0f));
        }

        [Test]
        public void ValueNoise_IsTileable_AtPeriod_AndDeterministic()
        {
            const int period = 8;
            for (int i = 0; i < 50; i++)
            {
                float x = i * 0.37f, y = i * 0.91f;
                float a = PlusFieldOps.ValueNoise2D(x, y, 42, period);
                Assert.That(PlusFieldOps.ValueNoise2D(x + period, y, 42, period), Is.EqualTo(a).Within(1e-5f));
                Assert.That(PlusFieldOps.ValueNoise2D(x, y - 2 * period, 42, period), Is.EqualTo(a).Within(1e-5f));
                Assert.That(PlusFieldOps.ValueNoise2D(x, y, 42, period), Is.EqualTo(a), "same inputs ⇒ same value");
                Assert.That(a, Is.InRange(0f, 1f));
                float f = PlusFieldOps.Fbm2D(x, y, 7, 4, 2f, 0.5f, period);
                Assert.That(PlusFieldOps.Fbm2D(x + period, y + period, 7, 4, 2f, 0.5f, period), Is.EqualTo(f).Within(1e-5f), "fbm tiles");
                float n3 = PlusFieldOps.ValueNoise3D(x, y, i * 0.13f, 9, period);
                Assert.That(PlusFieldOps.ValueNoise3D(x, y + period, i * 0.13f + period, 9, period), Is.EqualTo(n3).Within(1e-5f), "3D tiles");
            }
            Assert.That(PlusFieldOps.ValueNoise2D(3.3f, 4.4f, 1), Is.Not.EqualTo(PlusFieldOps.ValueNoise2D(3.3f, 4.4f, 2)), "seed matters");
        }

        [Test]
        public void ValueNoise_IsContinuous_AcrossLatticeLines()
        {
            float a = PlusFieldOps.ValueNoise2D(2.9999f, 1.5f, 3), b = PlusFieldOps.ValueNoise2D(3.0001f, 1.5f, 3);
            Assert.That(Mathf.Abs(a - b), Is.LessThan(1e-3f));
        }

        [Test]
        public void Curl_IsDivergenceFree_Approximately()
        {
            float eps = 0.01f;
            for (int i = 0; i < 10; i++)
            {
                float x = 1.3f + i * 0.7f, y = 2.1f + i * 0.3f;
                var cx1 = PlusFieldOps.Curl2D(x + eps, y, 5, 3); var cx0 = PlusFieldOps.Curl2D(x - eps, y, 5, 3);
                var cy1 = PlusFieldOps.Curl2D(x, y + eps, 5, 3); var cy0 = PlusFieldOps.Curl2D(x, y - eps, 5, 3);
                float div = (cx1.x - cx0.x) / (2 * eps) + (cy1.y - cy0.y) / (2 * eps);
                Assert.That(Mathf.Abs(div), Is.LessThan(0.5f), $"divergence at ({x},{y}) = {div}");
            }
        }

        [Test]
        public void Stats_MatchKnownQuantiles()
        {
            var f = new float[101];
            for (int i = 0; i <= 100; i++) f[i] = i;   // 0..100
            var s = PlusFieldOps.ComputeStats(f, lo: 0f);  // lit = values > 0 → 1..100
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
            PlusFieldOps.MultiplyNoise(g, W, H, 4f, 0f, 1);
            CollectionAssert.AreEqual(f, g);
            PlusFieldOps.MultiplyNoise(g, W, H, 4f, 0.5f, 1);
            float mean = PlusFieldOps.Sum(g) / g.Length;
            Assert.That(mean, Is.EqualTo(1f).Within(0.15f));
            bool varied = false; foreach (var v in g) if (Mathf.Abs(v - 1f) > 0.05f) varied = true;
            Assert.That(varied);
        }
    }
}
