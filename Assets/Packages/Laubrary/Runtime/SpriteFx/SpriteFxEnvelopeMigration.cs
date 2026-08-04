using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.SpriteFx
{
    /// Turns a legacy AnimationCurve life-remap into ZUI envelope points, once, on first access.
    ///
    /// Sampled rather than key-converted: an AnimationCurve key carries tangents that ZUIEnvelopePoint has
    /// no equivalent for, so copying keys would silently change the shape between the old drawing and the
    /// new one. Sampling reproduces what the curve actually DID.
    public static class SpriteFxEnvelopeMigration
    {
        const int Samples = 9;

        public static void Seed(List<ZUIEnvelopePoint> into, AnimationCurve legacy)
        {
            if (into == null) return;
            into.Clear();
            bool identity = legacy == null || legacy.length == 0;
            for (int i = 0; i < Samples; i++)
            {
                float t = (float)i / (Samples - 1);
                float v = identity ? t : Mathf.Clamp01(legacy.Evaluate(t));
                into.Add(new ZUIEnvelopePoint { time = t, value = v });
            }
        }
    }
}
