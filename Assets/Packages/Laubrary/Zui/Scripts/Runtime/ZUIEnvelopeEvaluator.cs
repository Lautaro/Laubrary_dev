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

    /// <summary>Smoothness-aware evaluate. 0 → the exact per-segment Bend path above (BYTE-IDENTICAL);
    /// &gt;0 blends toward a uniform Catmull-Rom spline through the points, rounding the CORNERS at the
    /// interior points. No effect below 3 points (2 points have no interior corner — always a straight
    /// line), so that case returns the linear result too.</summary>
    public static float Evaluate(List<ZUIEnvelopePoint> points, float time, float fallback, float smoothness)
    {
        // Sharp, or too few points to have an interior corner → the exact existing linear/bend path.
        if (smoothness <= 0f || points == null || points.Count < 3)
            return Evaluate(points, time, fallback);

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

        float linear = Bend(a.value, b.value, t, b.exponent);
        // Uniform Catmull-Rom through the 4 surrounding knots (endpoints clamped to themselves).
        float p0 = points[idx >= 2 ? idx - 2 : idx - 1].value;
        float p1 = a.value;
        float p2 = b.value;
        float p3 = points[idx + 1 < points.Count ? idx + 1 : idx].value;
        float smooth = CatmullRom(p0, p1, p2, p3, t);
        return Mathf.Lerp(linear, smooth, Mathf.Clamp01(smoothness));
    }

    /// <summary>Uniform Catmull-Rom on the middle segment P1→P2, tangents derived from P0/P3, t in [0,1].</summary>
    public static float CatmullRom(float p0, float p1, float p2, float p3, float t)
    {
        float t2 = t * t, t3 = t2 * t;
        return 0.5f * ((2f * p1)
                     + (-p0 + p2) * t
                     + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2
                     + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
    }
}
