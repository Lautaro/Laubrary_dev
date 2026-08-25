// PyreClipStats — field statistics over the WHOLE CLIP, computed once and reused every frame.
//
// ⚠ NEVER NORMALISE PER FRAME. The ForkBlast post-mortem (Appendix D §A5) is the worked example: Kiln fits the heat
// ceiling `hi` ONCE per draw over eight sampled frames, so the flash frame clips to white and the dying tail goes
// dim ember — the brightness arc white-out → orange → ember IS the field's dynamic range against a FIXED ceiling.
// A per-frame fit ("hi = 0.85·max(density) this frame") flattens that arc: every frame reaches the ramp's hot end,
// the tail renders a white core on six pixels that the source draws near-black. The rule for a port: if the source
// fitted a constant, either port the fitter AT CLIP LEVEL (this class) or freeze its output as an authored dial.
//
// Usage inside a form (the sample pass re-prepares the form at other lives, then restores this frame's dials):
//
//   static readonly PyrePrepassCache<PyreClipStats.Result> _clip = new PyrePrepassCache<PyreClipStats.Result>();
//   var clip = PyreClipStats.Get(_clip, ctx, this, sampleFrames: 8, lo: fieldLow, (life, buf) => AccumulateHeat(life, buf));
//   float hi = clip.pooled.q99 * exposure;     // ONE ceiling for every frame of the clip
//
// The cache key includes the form's content hash and the spec's W/H/frames/seed, so any authoring edit refits.
using System;
using UnityEngine;

namespace Laubrary.Pyre
{
    public static class PyreClipStats
    {
        public sealed class Result
        {
            public int[] frames;                       // the frame indices sampled
            public PyreFieldOps.Stats[] perFrame;      // stats of each sampled frame
            public PyreFieldOps.Stats pooled;          // quantiles over the lit pixels of ALL sampled frames together; peak = max
            public float peakOfPeaks => pooled.peak;
            public int litPxMax;                       // the largest lit count of any sampled frame
        }

        /// Evenly spaced sample frames over [0, frameCount−1], first and last always included.
        public static int[] SampleFrames(int frameCount, int sampleFrames)
        {
            frameCount = Mathf.Max(1, frameCount);
            int n = Mathf.Clamp(sampleFrames, 1, frameCount);
            var f = new int[n];
            for (int i = 0; i < n; i++) f[i] = n > 1 ? Mathf.RoundToInt(i * (frameCount - 1) / (float)(n - 1)) : 0;
            return f;
        }

        /// Compute clip statistics: `fill(frameIndex, buffer)` must overwrite `buffer` (W*H) with the form's field at
        /// that frame. `lo` is the lit threshold. Pure — no caching; see Get for the cached form.
        public static Result Compute(int W, int H, int frameCount, int sampleFrames, float lo, Action<int, float[]> fill)
        {
            var frames = SampleFrames(frameCount, sampleFrames);
            var buf = new float[W * H];
            var sort = new float[W * H];
            var res = new Result { frames = frames, perFrame = new PyreFieldOps.Stats[frames.Length] };
            // pooled: gather every lit value of every sampled frame, then one sort
            var pooled = new float[W * H * frames.Length];
            int pn = 0; float peak = float.NegativeInfinity; double sum = 0;
            for (int i = 0; i < frames.Length; i++)
            {
                fill(frames[i], buf);
                res.perFrame[i] = PyreFieldOps.ComputeStats(buf, lo, sort);
                res.litPxMax = Mathf.Max(res.litPxMax, res.perFrame[i].litPx);
                for (int p = 0; p < buf.Length; p++)
                {
                    float v = buf[p];
                    if (v > peak) peak = v;
                    if (v > lo) { pooled[pn++] = v; sum += v; }
                }
            }
            var ps = new PyreFieldOps.Stats { peak = peak == float.NegativeInfinity ? 0f : peak, litPx = pn };
            if (pn > 0)
            {
                Array.Sort(pooled, 0, pn);
                ps.mean = (float)(sum / pn);
                ps.q50 = PyreFieldOps.Quantile(pooled, pn, 0.50f);
                ps.q90 = PyreFieldOps.Quantile(pooled, pn, 0.90f);
                ps.q99 = PyreFieldOps.Quantile(pooled, pn, 0.99f);
            }
            res.pooled = ps;
            return res;
        }

        /// The cached clip statistics for `form` in `ctx`. `fillAtLife(life, buffer)` accumulates the form's field at
        /// an arbitrary life; this helper prepares the form at each sampled life first (through the renderer's Eval
        /// funnel, so Min-Max / curve dials resolve exactly as they will when that frame renders) and re-prepares it
        /// at ctx.life before returning, so the caller's current-frame dials are intact.
        public static Result Get(PyrePrepassCache<Result> cache, in PyreFormCtx ctx, PyreForm form, int sampleFrames, float lo,
                                 Action<float, float[]> fillAtLife, int extraHash = 0)
        {
            int W = ctx.W, H = ctx.H, frames = ctx.frameCount;
            var c = ctx;   // an `in` parameter cannot be captured by a lambda; copy the readonly struct
            return cache.Get(ctx, form, () =>
            {
                var r = Compute(W, H, frames, sampleFrames, lo, (fi, buf) =>
                {
                    float life = c.LifeOfFrame(fi);
                    form.Prepare(c.PrepareCtxAt(life));
                    fillAtLife(life, buf);
                });
                form.Prepare(c.PrepareCtxAt(c.life));
                return r;
            }, extraHash);
        }
    }
}
