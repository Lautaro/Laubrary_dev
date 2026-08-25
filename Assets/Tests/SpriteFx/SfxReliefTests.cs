using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Laubrary.SpriteFx;

namespace Laubrary.SpriteFx.Tests
{
    public class SfxReliefTests
    {
        // The host normally resolves an animatable value at the frame's progress; a Static ZUIValue at t=0 is
        // exactly what an un-animated authored effect evaluates to. The ids are RECORDED rather than ignored:
        // the local field id is what keeps each param's Min-Max randomness independent, so a duplicated id is a
        // real bug that nothing else in the suite would notice.
        readonly List<int> _fieldIds = new List<int>();
        float Eval(ZUIValue v, int fieldId) { _fieldIds.Add(fieldId); return v.Evaluate(0f); }

        static float[] Flat(int w, int h, float value)
        {
            var f = new float[w * h];
            for (int i = 0; i < f.Length; i++) f[i] = value;
            return f;
        }

        /// A ridge: height rises from the left edge to the middle, then falls back to the right edge. The left
        /// slope faces -X and the right slope faces +X, so a light from one side lights exactly one of them.
        static float[] Ridge(int w, int h)
        {
            var f = new float[w * h];
            float mid = (w - 1) * 0.5f;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    f[y * w + x] = 1f - Mathf.Abs(x - mid) / mid;
            return f;
        }

        /// Flat everywhere except a step down the LEFT border column. A CONSTANT field cannot tell clamp, wrap
        /// and mirror apart — all three read the same value at every border — so this puts the step where the
        /// three disagree: under clamp it shows at the left edge only, a wrapped read would drag it onto the
        /// RIGHT edge, and a mirrored read would erase it from column 0 altogether.
        static float[] StepAtLeftBorder(int w, int h)
        {
            var f = new float[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    f[y * w + x] = x == 0 ? 1f : 0.5f;
            return f;
        }

        static float MeanOverColumns(float[] light, int w, int h, int x0, int x1)
        {
            double sum = 0; int n = 0;
            for (int y = 1; y < h - 1; y++)
                for (int x = x0; x <= x1; x++) { sum += light[y * w + x]; n++; }
            return (float)(sum / n);
        }

        // ── SfxRelief: the shading math ────────────────────────────────────────────────────────────────────

        [Test]
        public void BorderDifference_ClampsToTheEdge_AndInventsNoSlopeFromTheOppositeSide()
        {
            const int W = 24, H = 18;
            var light = new float[W * H];
            SfxRelief.ShadeDirectional(light, StepAtLeftBorder(W, H), W, H,
                angleDeg: 45f, elevationDeg: 35f, relief: 4f,
                ambient: 0.2f, diffuse: 0.8f, specular: 0.3f, shine: 16f);

            // The flat interior, well clear of the step, is the reference value.
            float flat = light[3 * W + W / 2];
            Assert.That(flat, Is.GreaterThan(0.2f), "a flat surface still faces the light, so it is above ambient");
            for (int y = 0; y < H; y++)
                for (int x = 2; x <= W - 2; x++)
                    Assert.That(light[y * W + x], Is.EqualTo(flat).Within(1e-6f),
                                $"pixel {x},{y} is nowhere near the step but shades differently");

            for (int y = 0; y < H; y++)
            {
                Assert.That(light[y * W + W - 1], Is.EqualTo(flat).Within(1e-6f),
                            $"the right border at row {y} picked up the step from the LEFT border — the " +
                            "difference wrapped around the picture instead of clamping");
                Assert.That(Mathf.Abs(light[y * W] - flat), Is.GreaterThan(0.01f),
                            $"the left border at row {y} sits ON the step and must shade differently — reading " +
                            "the mirrored neighbour instead of clamping would flatten it");
            }
        }

        [Test]
        public void Ridge_LightsTheSlopeFacingTheLight_AndSwapsWhenTheLightSwaps()
        {
            const int W = 64, H = 16;
            var height = Ridge(W, H);
            var fromLeft = new float[W * H];
            var fromRight = new float[W * H];

            // 180 deg = the light comes from -X; 0 deg = from +X. Elevation kept low so the rake is pronounced.
            SfxRelief.ShadeDirectional(fromLeft, height, W, H, 180f, 20f, 6f, 0.1f, 0.9f, 0f, 16f);
            SfxRelief.ShadeDirectional(fromRight, height, W, H, 0f, 20f, 6f, 0.1f, 0.9f, 0f, 16f);

            int lo0 = 4, lo1 = W / 4;              // well inside the left slope
            int hi0 = W - 1 - W / 4, hi1 = W - 5;  // well inside the right slope

            float leftLitL = MeanOverColumns(fromLeft, W, H, lo0, lo1);
            float leftLitR = MeanOverColumns(fromLeft, W, H, hi0, hi1);
            Assert.That(leftLitL, Is.GreaterThan(leftLitR + 0.05f),
                        "with the light from -X the left slope (which faces it) must be the brighter one");

            float rightLitL = MeanOverColumns(fromRight, W, H, lo0, lo1);
            float rightLitR = MeanOverColumns(fromRight, W, H, hi0, hi1);
            Assert.That(rightLitR, Is.GreaterThan(rightLitL + 0.05f),
                        "swapping the light by 180 degrees must swap which slope is lit");
        }

        [Test]
        public void PointLight_IsBrightestAtItsPosition_AndDropsToAmbientBeyondItsRadius()
        {
            const int W = 32, H = 32, R = 8;
            const float ambient = 0.25f, diffuse = 0.4f, specular = 0.2f;
            var light = new float[W * H];

            // Put the light exactly over pixel (16,16) in half-frame units, so its plane distance there is 0.
            float centre = (W - 1) * 0.5f, half = Mathf.Min(W, H) * 0.5f;
            float lx = (16 - centre) / half, ly = (16 - centre) / half;

            SfxRelief.ShadePoint(light, Flat(W, H, 0.5f), W, H,
                lightX: lx, lightY: ly, lightZ: 0.5f, radius: 0.5f, falloff: 2f,
                centreX: centre, centreY: centre, half: half, relief: 4f,
                ambient: ambient, diffuse: diffuse, specular: specular, shine: 16f);

            float at = light[16 * W + 16];
            float far = light[R * W + R];   // (8,8) is ~0.7 half-frame units away — well past radius 0.5

            // Directly under the light on a flat field the closed form is exact: the normal is (0,0,1), the light
            // vector is (0,0,1), so both the diffuse and the specular term are 1 and the attenuation is 1. The
            // terms are deliberately chosen to sum below 1 so the clamp cannot hide a regression.
            Assert.That(at, Is.EqualTo(ambient + diffuse + specular).Within(1e-5f),
                        "under the light, on a flat surface, every term is at full strength and none is clamped");
            Assert.That(far, Is.EqualTo(ambient).Within(1e-6f),
                        "past the radius the attenuation is zero, so only the ambient floor remains");
        }

        // ── RelightModifier: the buffer pass ───────────────────────────────────────────────────────────────

        /// A filled disc on a transparent field, plus one deliberately garbage-coloured fully transparent pixel
        /// so a pass that ignores alpha is caught.
        static Color32[] Disc(int w, int h)
        {
            var buf = new Color32[w * h];
            float cx = (w - 1) * 0.5f, cy = (h - 1) * 0.5f, r = Mathf.Min(w, h) * 0.4f;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float dx = x - cx, dy = y - cy;
                    buf[y * w + x] = (dx * dx + dy * dy <= r * r)
                        ? new Color32(140, 140, 150, 255)
                        : new Color32(200, 30, 90, 0);   // garbage RGB behind zero alpha
                }
            return buf;
        }

        static RelightModifier LitFromTheLeft()
        {
            return new RelightModifier
            {
                mode = SfxLightMode.Point,
                heightSource = SfxReliefSource.Silhouette,
                depthRadius = 8,
                soften = 2,
                lightX = new ZUIValue(-0.9f),
                lightY = new ZUIValue(0f),
                lightHeight = new ZUIValue(0.4f),
                radius = new ZUIValue(1.6f),
                falloff = new ZUIValue(2f),
                ambient = new ZUIValue(0.25f),
            };
        }

        [Test]
        public void AllThreeOutputTermsAtZero_LeavesTheBufferByteIdentical()
        {
            const int W = 48, H = 48;
            var buf = Disc(W, H);
            var before = (Color32[])buf.Clone();

            var m = LitFromTheLeft();
            m.shading = new ZUIValue(0f);
            m.tint = new ZUIValue(0f);
            m.add = new ZUIValue(0f);
            m.Prepare(Eval);
            m.Apply(buf, W, H);

            for (int i = 0; i < buf.Length; i++)
                Assert.That((buf[i].r, buf[i].g, buf[i].b, buf[i].a),
                            Is.EqualTo((before[i].r, before[i].g, before[i].b, before[i].a)),
                            $"pixel {i % W},{i / W} changed with shading/tint/add all at zero");

            // Guard against a vacuous pass: the same effect with a live shading term MUST change the buffer.
            var buf2 = (Color32[])before.Clone();
            var live = LitFromTheLeft();
            live.shading = new ZUIValue(1f);
            live.tint = new ZUIValue(0f);
            live.add = new ZUIValue(0f);
            live.Prepare(Eval);
            live.Apply(buf2, W, H);

            bool changed = false;
            for (int i = 0; i < buf2.Length && !changed; i++)
                changed = buf2[i].r != before[i].r || buf2[i].g != before[i].g || buf2[i].b != before[i].b;
            Assert.That(changed, Is.True, "the zero-case test would be meaningless if the effect never wrote anything");
        }

        [Test]
        public void OutputTermsAtIdentity_RunTheArithmetic_AndStillLeaveTheBufferByteIdentical()
        {
            // The all-zero case above never reaches the per-pixel math — the early-out fires first, so it only
            // proves the guard exists. Here `add` is live, so the pass runs in full, and every term is at its
            // IDENTITY: shading 0 is a multiply by 1, tint 0 is a lerp by 0, and the light is parked far outside
            // the frame with a tiny radius so nothing is above the ambient floor and the additive term is 0 too.
            const int W = 48, H = 48;
            var buf = Disc(W, H);
            var before = (Color32[])buf.Clone();

            var m = LitFromTheLeft();
            m.lightX = new ZUIValue(3.9f);
            m.radius = new ZUIValue(0.05f);
            m.shading = new ZUIValue(0f);
            m.tint = new ZUIValue(0f);
            m.add = new ZUIValue(0.5f);
            m.lightColor = new Color(1f, 0.6f, 0.2f, 1f);
            m.Prepare(Eval);
            m.Apply(buf, W, H);

            for (int i = 0; i < buf.Length; i++)
                Assert.That((buf[i].r, buf[i].g, buf[i].b, buf[i].a),
                            Is.EqualTo((before[i].r, before[i].g, before[i].b, before[i].a)),
                            $"pixel {i % W},{i / W} drifted while every output term was at identity — the " +
                            "byte round-trip through the float math is not lossless");

            // Vacuity guard: the same live `add`, with the light actually over the sprite, MUST change it.
            var buf2 = (Color32[])before.Clone();
            var reaching = LitFromTheLeft();
            reaching.shading = new ZUIValue(0f);
            reaching.tint = new ZUIValue(0f);
            reaching.add = new ZUIValue(0.5f);
            reaching.Prepare(Eval);
            reaching.Apply(buf2, W, H);

            bool changed = false;
            for (int i = 0; i < buf2.Length && !changed; i++)
                changed = buf2[i].r != before[i].r || buf2[i].g != before[i].g || buf2[i].b != before[i].b;
            Assert.That(changed, Is.True, "with add live and the light on the sprite the buffer has to change");
        }

        [Test]
        public void FullyTransparentPixels_AreNeverTouched()
        {
            const int W = 48, H = 48;
            var buf = Disc(W, H);
            var before = (Color32[])buf.Clone();

            var m = LitFromTheLeft();
            m.shading = new ZUIValue(1f);
            m.tint = new ZUIValue(1f);
            m.add = new ZUIValue(1.5f);
            m.lightColor = new Color(1f, 0.6f, 0.2f, 1f);
            m.Prepare(Eval);
            m.Apply(buf, W, H);

            for (int i = 0; i < buf.Length; i++)
            {
                if (before[i].a != 0) continue;
                Assert.That((buf[i].r, buf[i].g, buf[i].b, buf[i].a),
                            Is.EqualTo((before[i].r, before[i].g, before[i].b, before[i].a)),
                            $"transparent pixel {i % W},{i / W} was written into — the light leaked past the silhouette");
            }
        }

        [Test]
        public void PointLightOffToOneSide_LightsThatSideOfTheSprite()
        {
            const int W = 64, H = 64;
            var buf = Disc(W, H);

            var m = LitFromTheLeft();
            m.shading = new ZUIValue(1f);
            m.tint = new ZUIValue(0f);
            m.add = new ZUIValue(0f);
            m.Prepare(Eval);
            m.Apply(buf, W, H);

            double left = 0, right = 0; int nl = 0, nr = 0;
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    var c = buf[y * W + x];
                    if (c.a == 0) continue;
                    float lum = (0.299f * c.r + 0.587f * c.g + 0.114f * c.b) / 255f;
                    if (x < W / 2) { left += lum; nl++; } else { right += lum; nr++; }
                }

            Assert.That(nl, Is.GreaterThan(0)); Assert.That(nr, Is.GreaterThan(0));
            Assert.That(left / nl, Is.GreaterThan(right / nr),
                        "a light placed to the left must leave the left half of the sprite measurably brighter");
        }

        [Test]
        public void TheSameInstanceRunTwice_ProducesIdenticalBytes()
        {
            // Deliberately ONE instance across both passes: a fresh instance per pass could not detect state
            // cached from a previous Apply, which is the only way this effect could stop being deterministic.
            const int W = 48, H = 48;
            var a = Disc(W, H);
            var b = Disc(W, H);

            var m = LitFromTheLeft();
            m.shading = new ZUIValue(1f);
            m.tint = new ZUIValue(0.5f);
            m.add = new ZUIValue(0.8f);
            m.lightColor = new Color(1f, 0.7f, 0.3f, 1f);
            m.Prepare(Eval);
            m.Apply(a, W, H);
            m.Apply(b, W, H);

            for (int i = 0; i < a.Length; i++)
                Assert.That((a[i].r, a[i].g, a[i].b, a[i].a), Is.EqualTo((b[i].r, b[i].g, b[i].b, b[i].a)),
                            $"pixel {i % W},{i / W} differs between two runs of the SAME instance — something " +
                            "survived from the first Apply into the second");
        }

        [Test]
        public void APaddedBuffer_LightsTheSpriteExactlyAsAnUnpaddedOneDoes()
        {
            // A stack whose other effects reach outward hands every post pass a buffer with a transparent
            // margin. The light is authored against the SPRITE, so the margin must not move it — before the
            // picture rect was threaded through, adding an unrelated glow slid a tuned muzzle flash off the
            // barrel and grew its radius with the margin.
            const int W = 64, H = 64, pad = 16;
            var plain = Disc(W, H);
            int pw = W + pad * 2, ph = H + pad * 2;
            var padded = new Color32[pw * ph];
            for (int y = 0; y < H; y++)
                System.Array.Copy(plain, y * W, padded, (y + pad) * pw + pad, W);

            SpriteFxStack.RunStack(plain, W, H,
                new List<PyreModifier> { Flash() }, 0, 0f, 7, false);
            SpriteFxStack.RunStack(padded, pw, ph, W, H, pad, pad,
                new List<PyreModifier> { Flash() }, 0, 0f, 7, false);

            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    var u = plain[y * W + x];
                    var p = padded[(y + pad) * pw + pad + x];
                    Assert.That((p.r, p.g, p.b, p.a), Is.EqualTo((u.r, u.g, u.b, u.a)),
                                $"sprite pixel {x},{y} differs between a padded and an unpadded buffer — the " +
                                "light is being normalized against the canvas instead of against the sprite");
                }
        }

        static RelightModifier Flash()
        {
            var m = LitFromTheLeft();
            m.shading = new ZUIValue(1f);
            m.tint = new ZUIValue(0.5f);
            m.add = new ZUIValue(0.8f);
            m.lightColor = new Color(1f, 0.7f, 0.3f, 1f);
            return m;
        }

        [Test]
        public void Brightness_IgnoresColourHiddenBehindZeroAlpha()
        {
            // Atlas bleed and lossy compression leave real colour behind a=0. Brightness reads every pixel's
            // RGB and then blurs the result, so without a guard that invisible colour would smear into the
            // visible pixels next to it and change the invented surface.
            const int W = 48, H = 48;
            var garbage = Disc(W, H);                       // (200,30,90,0) behind zero alpha
            var clean = Disc(W, H);
            for (int i = 0; i < clean.Length; i++) if (clean[i].a == 0) clean[i] = default;
            var reference = (Color32[])clean.Clone();

            foreach (var buf in new[] { garbage, clean })
            {
                var m = LitFromTheLeft();
                m.heightSource = SfxReliefSource.Brightness;
                m.shading = new ZUIValue(1f);
                m.tint = new ZUIValue(0.5f);
                m.add = new ZUIValue(0.8f);
                m.lightColor = new Color(1f, 0.7f, 0.3f, 1f);
                m.Prepare(Eval);
                m.Apply(buf, W, H);
            }

            for (int i = 0; i < garbage.Length; i++)
            {
                if (reference[i].a == 0) continue;
                Assert.That((garbage[i].r, garbage[i].g, garbage[i].b),
                            Is.EqualTo((clean[i].r, clean[i].g, clean[i].b)),
                            $"visible pixel {i % W},{i / W} changed with colour hidden behind zero alpha");
            }

            bool wrote = false;
            for (int i = 0; i < clean.Length && !wrote; i++)
                wrote = clean[i].a != 0 && (clean[i].r != reference[i].r || clean[i].g != reference[i].g);
            Assert.That(wrote, Is.True, "the comparison is meaningless unless Brightness actually lit something");
        }

        [Test]
        public void Prepare_HandsOutUniqueConsecutiveFieldIds()
        {
            // The local field id is what keeps each animatable param's Min-Max randomness independent. Two
            // fields sharing an id makes them move together, which no output test would ever notice.
            var m = LitFromTheLeft();
            m.Prepare(Eval);

            Assert.That(_fieldIds.Count, Is.GreaterThan(0), "Prepare resolved nothing at all");
            var seen = new HashSet<int>();
            foreach (int id in _fieldIds)
                Assert.That(seen.Add(id), Is.True, $"field id {id} was handed out twice");
            for (int i = 0; i < _fieldIds.Count; i++)
                Assert.That(seen.Contains(i), Is.True,
                            $"field ids must run 0..{_fieldIds.Count - 1} with no gaps; {i} is missing");
        }
    }
}
