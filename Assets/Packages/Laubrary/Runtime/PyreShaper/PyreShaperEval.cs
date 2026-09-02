using Laubrary.Pyre;
using UnityEngine;

namespace Laubrary.PyreShaper
{
    /// <summary>
    /// T-0167 — the one place a hosted Pyre object's <see cref="ZUIValue"/> dials are resolved to a float inside
    /// the Shaper bridge, so a Curve/MinMax/Steps dial authored on a hosted <see cref="PyreForm"/> or a hosted
    /// <c>PixelModifier</c>/<c>GeometryModifier</c> actually reads at the Shaper clock's phase instead of freezing
    /// at its static value.
    ///
    /// <b>Why this duplicates the switch instead of calling <c>PyreRenderer</c>'s own.</b> <c>PyreRenderer.Eval</c>
    /// is <c>private static</c> and additionally keys its MinMax draw off <c>PyreRenderer._layerSalt</c>, a private
    /// static field only the full spec renderer ever sets — neither is reachable from this bridge asmdef, which by
    /// design (<see cref="PyreFormCompositeSource"/>'s class doc) never references anything PyreRenderer-internal.
    /// This method reproduces the exact same four-mode switch against the same public building blocks PyreRenderer
    /// itself uses — <see cref="ZUIEnvelopeEvaluator"/> for Curve and <see cref="PyreRenderer.Hash"/> for the seeded
    /// MinMax draw — so a dial evaluated here draws from the identical RNG stream (same seed/particleIndex/fieldId/
    /// layerSalt four-tuple) it would if hosted directly inside a real Pyre layer.
    ///
    /// <b>W2.1 (effects/modifiers) uses this too.</b> A hosted <c>PixelModifier</c>/<c>GeometryModifier</c>'s
    /// <c>Prepare(Func&lt;ZUIValue,int,float&gt;)</c> closure should call this exact method rather than hand-rolling
    /// a second static-only fallback (the same pattern <see cref="ShaperEffectApplier.ApplyInPlace"/> currently has
    /// at its own <c>effect.Prepare((v, fieldId) =&gt; v != null ? v.staticValue : 0f)</c> line) — one Eval funnel
    /// for every hosted Pyre object Shaper drives, never two.
    /// </summary>
    public static class PyreShaperEval
    {
        /// <summary>
        /// Deterministically resolves <paramref name="v"/> at <paramref name="life"/> (the Shaper phase, already
        /// clamped by the caller). Static reads the value; Curve samples the envelope at life (points are
        /// authored 0..1, no duration/warmup/cooldown — the frame-baked timeline convention, not the runtime-
        /// seconds one); Steps holds one of <c>steps.Count</c> equal sections across life; MinMax draws once from
        /// <see cref="PyreRenderer.Hash"/>(<paramref name="seed"/>, <paramref name="particleIndex"/>,
        /// <paramref name="fieldId"/>, <paramref name="layerSalt"/>) — pass <see cref="PyreRenderer.ModParticleIndex"/>
        /// for a whole-layer/whole-form dial (the same sentinel <c>PyreRenderer.RenderFormLayer</c> passes a real
        /// form's Prepare) and a stable per-node <paramref name="layerSalt"/> (0 is correct for a composite node
        /// hosting exactly one form — there is no sibling layer to decorrelate from).
        /// </summary>
        public static float Eval(ZUIValue v, float life, int seed, int particleIndex, int fieldId, int layerSalt)
        {
            if (v == null) return 0f;
            switch (v.mode)
            {
                case ZUIValue.Mode.Static:
                    return v.staticValue;

                case ZUIValue.Mode.MinMax:
                {
                    var rng = new System.Random(PyreRenderer.Hash(seed, particleIndex, fieldId, layerSalt));
                    return Mathf.Lerp(v.min, v.max, (float)rng.NextDouble());
                }

                case ZUIValue.Mode.Curve:
                    return ZUIEnvelopeEvaluator.Evaluate(v.points, Mathf.Clamp01(life), v.yMax);

                case ZUIValue.Mode.Steps:
                {
                    var steps = v.steps;
                    int n = steps != null ? steps.Count : 0;
                    if (n == 0) return v.staticValue;
                    return steps[Mathf.Clamp(Mathf.FloorToInt(Mathf.Clamp01(life) * n), 0, n - 1)];
                }

                default:
                    return v.staticValue;
            }
        }
    }
}
