using System.Collections.Generic;
using Laubrary.PyrePlus;
using UnityEngine;

namespace Laubrary.PyrePlus.Tests
{
    /// Shared by the per-form envelope tests (T-0063): one spec, one layer carrying the form, rendered through the
    /// real renderer (so the form's Prepare sees the renderer's Eval funnel — a hand-built PlusFormCtx has no
    /// evaluator and would flatten every Curve to its static value).
    public static class EnvelopeTestUtil
    {
        public static PyrePlusSpec Spec(PlusForm form, int size, int frames, int seed)
        {
            var spec = ScriptableObject.CreateInstance<PyrePlusSpec>();
            spec.canvasSize = size; spec.frameCount = frames; spec.seed = seed;
            spec.background = new Color(0, 0, 0, 0); spec.backgroundUseFill = false;
            spec.layers.Clear();
            spec.layers.Add(new PyrePlusLayer { form = form, swarmEnabled = false, matteEnabled = false, alpha = new ZUIValue(1f) });
            return spec;
        }

        /// A Curve envelope rising linearly from `y0` at life 0 to `y1` at life 1.
        public static ZUIValue Ramp(float y0, float y1)
        {
            var v = new ZUIValue { mode = ZUIValue.Mode.Curve, yMin = Mathf.Min(y0, y1), yMax = Mathf.Max(y0, y1) };
            v.points.Clear();
            v.points.Add(new ZUIEnvelopePoint(0f, y0));
            v.points.Add(new ZUIEnvelopePoint(1f, y1));
            return v;
        }

        /// A Curve envelope that holds one value over the whole life — the envelope path carrying a constant.
        public static ZUIValue Flat(float y) => Ramp(y, y);

        public static uint Fnv(Color32[] px)
        {
            uint h = 2166136261u;
            foreach (var c in px) { h ^= c.r; h *= 16777619u; h ^= c.g; h *= 16777619u; h ^= c.b; h *= 16777619u; h ^= c.a; h *= 16777619u; }
            return h;
        }

        public static int Lit(Color32[] px) { int n = 0; foreach (var c in px) if (c.a != 0) n++; return n; }

        public static int DiffPixels(Color32[] a, Color32[] b)
        {
            int n = 0;
            for (int i = 0; i < a.Length; i++) if (a[i].r != b[i].r || a[i].g != b[i].g || a[i].b != b[i].b || a[i].a != b[i].a) n++;
            return n;
        }

        public static uint FnvAll(PyrePlusSpec spec)
        {
            uint h = 2166136261u;
            for (int f = 0; f < spec.frameCount; f++)
                foreach (var c in PyrePlusRenderer.RenderFrame(spec, f)) { h ^= c.r; h *= 16777619u; h ^= c.g; h *= 16777619u; h ^= c.b; h *= 16777619u; h ^= c.a; h *= 16777619u; }
            return h;
        }
    }
}
