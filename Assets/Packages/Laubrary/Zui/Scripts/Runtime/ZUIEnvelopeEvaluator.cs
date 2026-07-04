// ZUIEnvelopeEvaluator.cs
// Runtime-safe evaluation of a List<ZUIEnvelopePoint> curve. Extracted from
// ZUIEnvelope.EvaluateEnvelope (editor-only) so play-mode code can sample the
// exact same DAW-style bend the editor draws. No editor dependencies.

using System.Collections.Generic;
using UnityEngine;

public static class ZUIEnvelopeEvaluator
{
    /// <summary>
    /// Sample a sorted-by-time point list at <paramref name="time"/>. Mirrors
    /// ZUIEnvelope's editor evaluation exactly: find the bracketing segment, then
    /// Lerp(a, b, Pow(t, b.exponent)). Before the first point holds the first
    /// value; after the last holds the last value. Empty → <paramref name="fallback"/>.
    /// </summary>
    public static float Evaluate(List<ZUIEnvelopePoint> points, float time, float fallback = 0f)
    {
        if (points == null || points.Count == 0) return fallback;
        if (points.Count == 1) return points[0].value;

        int idx = points.Count;
        for (int i = 0; i < points.Count; i++)
            if (points[i].time > time) { idx = i; break; }

        if (idx <= 0) return points[0].value;
        if (idx >= points.Count) return points[points.Count - 1].value;

        var a = points[idx - 1];
        var b = points[idx];
        float range = b.time - a.time;
        if (range <= 0f) return b.value;
        float t = (time - a.time) / range;
        return Bend(a.value, b.value, t, b.exponent);
    }

    /// <summary>Lerp(a, b, Pow(t, exponent)) — exponent 1 = linear, &lt;1 log-style,
    /// &gt;1 exp-style. The canonical per-segment bend used across DAWs.</summary>
    public static float Bend(float aValue, float bValue, float t, float exponent)
    {
        if (exponent <= 0f) exponent = 0.000001f;
        return Mathf.Lerp(aValue, bValue, Mathf.Pow(t, exponent));
    }
}
