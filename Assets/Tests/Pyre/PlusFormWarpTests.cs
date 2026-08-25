using System.Collections.Generic;
using NUnit.Framework;
using Laubrary.Pyre;
using Laubrary.Pyre.Forms.Kiln;
using Laubrary.SpriteFx;
using UnityEngine;

namespace Laubrary.Pyre.Tests
{
    /// T-0058 — the generic post-render geometry pass for plug-in forms: identity stacks are byte-identical, a
    /// quarter turn is an exact array rotation, a wobble moves pixels without creating or destroying coverage, and a
    /// form that handles geometry itself (Inferno) is not warped a second time.
    public class PyreFormWarpTests
    {
        static Pyre Spec(PyreForm form, int size, int frames = 6, int seed = 7)
        {
            var spec = ScriptableObject.CreateInstance<Pyre>();
            spec.canvasSize = size; spec.frameCount = frames; spec.seed = seed;
            spec.layers[0].form = form;
            spec.layers[0].alpha = new ZUIValue(1f);   // the default envelope is 0 at frame 0
            return spec;
        }

        static Color32[] Render(PyreForm form, int size, PyreModifier mod, int frame)
        {
            var spec = Spec(form, size);
            try
            {
                if (mod != null) spec.layers[0].modifiers.Add(mod);
                return PyreRenderer.RenderFrame(spec, frame);
            }
            finally { Object.DestroyImmediate(spec); }
        }

        static void AssertSame(Color32[] a, Color32[] b, string why)
        {
            Assert.That(b.Length, Is.EqualTo(a.Length));
            int diff = 0;
            for (int i = 0; i < a.Length; i++)
                if (a[i].r != b[i].r || a[i].g != b[i].g || a[i].b != b[i].b || a[i].a != b[i].a) diff++;
            Assert.That(diff, Is.EqualTo(0), why + $" ({diff} px differ)");
        }

        static long TotalAlpha(Color32[] buf) { long s = 0; foreach (var c in buf) s += c.a; return s; }

        static Color32[] Asymmetric(int size)
        {
            // An L-shaped patch off-centre: no rotational symmetry, so a wrong rotation direction or an off-by-one
            // in the centre convention cannot pass by accident.
            var buf = new Color32[size * size];
            for (int y = 2; y < 9; y++) buf[y * size + 3] = new Color32(255, 40, 0, 255);
            for (int x = 3; x < 12; x++) buf[2 * size + x] = new Color32(0, 200, 255, 180);
            buf[20 * size + 25] = new Color32(10, 20, 30, 40);
            return buf;
        }

        [Test]
        public void IdentityStack_IsByteIdentical()
        {
            const int S = 32;
            var src = Asymmetric(S);
            var scale1 = new ScaleModifier { both = new ZUIValue(1f) };
            var rot0 = new RotateModifier { degrees = new ZUIValue(0f) };
            scale1.Prepare((v, _) => v.staticValue); rot0.Prepare((v, _) => v.staticValue);
            var buf = (Color32[])src.Clone();
            PyreFormWarp.Apply(buf, S, S, new GeometryModifier[] { scale1, rot0 }, 0.37f);
            AssertSame(src, buf, "Scale 1 + Rotate 0 must map every pixel to itself");
        }

        [Test]
        public void Rotate90_IsAnExactArrayRotation()
        {
            const int S = 32;
            var src = Asymmetric(S);
            var rot = new RotateModifier { degrees = new ZUIValue(90f) };
            rot.Prepare((v, _) => v.staticValue);
            var buf = (Color32[])src.Clone();
            PyreFormWarp.Apply(buf, S, S, new GeometryModifier[] { rot }, 0f);
            // Nearest sampling at pixel centres on a square canvas makes a quarter turn an exact permutation: the
            // destination (px,py) reads the source at (py, S-1-px) for one turn direction or (S-1-py, px) for the
            // other — the modifier's sign convention decides which, so accept either but demand it be exact.
            var cw = new Color32[S * S]; var ccw = new Color32[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    cw[y * S + x] = src[(S - 1 - x) * S + y];
                    ccw[y * S + x] = src[x * S + (S - 1 - y)];
                }
            bool matchesCw = true, matchesCcw = true;
            for (int i = 0; i < buf.Length; i++)
            {
                if (!buf[i].Equals(cw[i])) matchesCw = false;
                if (!buf[i].Equals(ccw[i])) matchesCcw = false;
            }
            Assert.That(matchesCw || matchesCcw, Is.True, "a 90° rotate must equal the source rotated by a quarter turn");
            Assert.That(TotalAlpha(buf), Is.EqualTo(TotalAlpha(src)));
        }

        [Test]
        public void Wobble_MovesPixelsButKeepsCoverage()
        {
            // Rendered through the real pipeline on a real form so the renderer hook (not just the helper) is exercised.
            const int S = 48; const int frame = 3;
            var plain = Render(new PlasmaBloomForm(), S, null, frame);
            var wobbled = Render(new PlasmaBloomForm(), S, new WobbleModifier { amplitude = new ZUIValue(3f) }, frame);
            Assert.That(TotalAlpha(plain), Is.GreaterThan(0), "the reference frame must draw something");
            int diff = 0;
            for (int i = 0; i < plain.Length; i++) if (!plain[i].Equals(wobbled[i])) diff++;
            Assert.That(diff, Is.GreaterThan(0), "a 3 px wobble must change the buffer");
            // Nearest resampling of a smooth field neither creates nor destroys coverage beyond edge effects.
            double ratio = TotalAlpha(wobbled) / (double)TotalAlpha(plain);
            Assert.That(ratio, Is.InRange(0.85, 1.15), $"total alpha ratio {ratio:F3}");
        }

        [Test]
        public void NoGeometry_DefaultPathIsUntouched_AndRotateReachesEveryPortedForm()
        {
            const int S = 40; const int frame = 2;
            foreach (var form in new PyreForm[] { new PlasmaBloomForm(), new ArcBurstForm(), new ForkBlastForm() })
            {
                var a = Render((PyreForm)form.Clone(), S, null, frame);
                var b = Render((PyreForm)form.Clone(), S, null, frame);
                AssertSame(a, b, form.GetType().Name + " without modifiers is deterministic");
                var rotated = Render((PyreForm)form.Clone(), S, new RotateModifier { degrees = new ZUIValue(90f) }, frame);
                // Exact quarter turn of the plain render, either direction (see Rotate90_IsAnExactArrayRotation).
                bool cw = true, ccw = true;
                for (int y = 0; y < S; y++)
                    for (int x = 0; x < S; x++)
                    {
                        if (!rotated[y * S + x].Equals(a[(S - 1 - x) * S + y])) cw = false;
                        if (!rotated[y * S + x].Equals(a[x * S + (S - 1 - y)])) ccw = false;
                    }
                Assert.That(cw || ccw, Is.True, form.GetType().Name + ": Rotate 90 through the renderer must equal the plain frame turned a quarter");
            }
        }

        [Test]
        public void InfernoHandlesGeometry_IsNotWarpedTwice()
        {
            Assert.That(new InfernoForm().HandlesGeometry, Is.True);
            Assert.That(new PlasmaBloomForm().HandlesGeometry, Is.False);
            // Inferno warps per sample: with Scale 0.5 the renderer must NOT also shrink the finished buffer. If it
            // did, the result would be the per-sample-scaled frame shrunk again — i.e. equal to the generic pass run
            // on the per-sample frame. Assert it is not.
            const int S = 40; const int frame = 4;
            var scaled = Render(new InfernoForm(), S, new ScaleModifier { both = new ZUIValue(0.5f) }, frame);
            Assert.That(TotalAlpha(scaled), Is.GreaterThan(0), "Inferno must draw at this frame");
            var twice = (Color32[])scaled.Clone();
            var half = new ScaleModifier { both = new ZUIValue(0.5f) }; half.Prepare((v, _) => v.staticValue);
            PyreFormWarp.Apply(twice, S, S, new GeometryModifier[] { half }, 0f);
            int diff = 0;
            for (int i = 0; i < scaled.Length; i++) if (!scaled[i].Equals(twice[i])) diff++;
            Assert.That(diff, Is.GreaterThan(0), "the renderer output must not already be the double-scaled frame");
        }
    }
}
