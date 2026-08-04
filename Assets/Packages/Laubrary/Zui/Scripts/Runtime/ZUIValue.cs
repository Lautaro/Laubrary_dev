// ZUIValue.cs
// A configurable numeric value: a single static number, a random min↔max range,
// an animation curve sampled over time (with a warmup that holds the curve's first value,
// and a cooldown pause before looping), a held step sequence, or an oscillation — a sine
// carrier swinging between two envelopes at an animatable rate. An optional named external multiplier
// (resolved by the host game, e.g. a "global value") scales the result — ZUI
// itself never needs to know what the id means.
//
// Runtime-safe (no editor deps). Authored by ZUIValueControl (editor); evaluated
// here at play time.

using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class ZUIValue
{
    // APPEND-ONLY: serialized as an int, so an existing asset's mode must never shift index.
    public enum Mode { Static, MinMax, Curve, Steps, Oscillation }

    [SerializeField] Mode m_mode = Mode.Static;

    // Static
    [SerializeField] float m_static = 1f;

    // MinMax (random sample each evaluation)
    [SerializeField] float m_min = 0f;
    [SerializeField] float m_max = 1f;

    // Curve. Points live in normalized time [0..1]; `duration` scales that to
    // seconds so the time axis can be retimed without moving points. Values live
    // in [yMin..yMax].
    [SerializeField] List<ZUIEnvelopePoint> m_points = new List<ZUIEnvelopePoint>();
    [SerializeField] float m_yMin = 0f;
    [SerializeField] float m_yMax = 1f;
    [SerializeField] float m_duration = 4f;   // seconds for one playthrough
    [SerializeField] float m_warmup = 0f;     // seconds before the curve starts (holds the curve's first value)
    [SerializeField] float m_cooldown = -1f;  // pause (s) after a playthrough before looping; -1 = never loop

    // Corner smoothness for Curve mode: 0 = the authored per-segment bend (sharp corners at the points), up to
    // 1 = a Catmull-Rom spline through the points (rounded corners). A CHEAP smooth-path control, not precision
    // curve editing. No effect below 3 points (2 points are always a straight line). Default 0 keeps every
    // existing curve byte-identical.
    [SerializeField, Range(0f, 1f)] float m_smoothness = 0f;

    // Steps (a step sequencer): one value per equal-width section over [0..1], HELD across each section (no
    // interpolation between them). Uses the same duration/warmup/cooldown timing + yMin/yMax range as Curve.
    [SerializeField] List<float> m_steps = new List<float>();

    // Oscillation: a FIXED sine carrier travelling between two ENVELOPES, at a rate that is itself an envelope.
    // The carrier is deliberately not editable — the authored part is the band it swings inside (which can open,
    // close, tilt or collapse over the playthrough) and how fast it swings. Three real envelopes, sampled by the
    // same evaluator and authored in the same editor as Curve mode's, sharing its yMin/yMax range and smoothness.
    //
    // Why the bounds are POINT LISTS and not nested ZUIValues: a ZUIValue field inside ZUIValue would be inlined
    // by value by Unity's serializer and recurse forever (the "serialization depth limit exceeded" wall), and the
    // [SerializeReference] escape from that is not open here either — ToClipboardString/TryFromClipboardString
    // round-trip through JsonUtility, which does not serialize managed references at all, so copy/paste would
    // silently drop every nested bound.
    [SerializeField] List<ZUIEnvelopePoint> m_oscMin = new List<ZUIEnvelopePoint>();
    [SerializeField] List<ZUIEnvelopePoint> m_oscMax = new List<ZUIEnvelopePoint>();
    [SerializeField] List<ZUIEnvelopePoint> m_oscRate = new List<ZUIEnvelopePoint>();
    [SerializeField] float m_oscRateMax = 8f;   // ceiling of the rate envelope's own Y axis (cycles/playthrough)

    // External multiplier. When set and a resolver is registered, the evaluated
    // source value is multiplied by resolver(multiplierId). Lets a host's "global
    // values" scale any ZUIValue without ZUI referencing the game.
    [SerializeField] string m_multiplierId = "";

    /// <summary>Host hook: id → multiplier. Null (or unset id) means ×1.</summary>
    public static Func<string, float> MultiplierResolver;

    // ── Properties (so the editor drawer + game can read/write) ───────────────
    public Mode mode { get => m_mode; set => m_mode = value; }
    public float staticValue { get => m_static; set => m_static = value; }
    public float min { get => m_min; set => m_min = value; }
    public float max { get => m_max; set => m_max = value; }
    public List<ZUIEnvelopePoint> points => m_points;
    public float yMin { get => m_yMin; set => m_yMin = value; }
    public float yMax { get => m_yMax; set => m_yMax = value; }
    public float duration { get => m_duration; set => m_duration = Mathf.Max(0.0001f, value); }
    public float warmup { get => m_warmup; set => m_warmup = Mathf.Max(0f, value); }
    public float cooldown { get => m_cooldown; set => m_cooldown = value; }
    public float smoothness { get => m_smoothness; set => m_smoothness = Mathf.Clamp01(value); }
    public List<float> steps => m_steps;
    public List<ZUIEnvelopePoint> oscMin => m_oscMin;
    public List<ZUIEnvelopePoint> oscMax => m_oscMax;
    public List<ZUIEnvelopePoint> oscRate => m_oscRate;
    public float oscRateMax { get => m_oscRateMax; set => m_oscRateMax = Mathf.Max(0.1f, value); }
    public string multiplierId { get => m_multiplierId; set => m_multiplierId = value; }

    public bool HasMultiplier => !string.IsNullOrEmpty(m_multiplierId);
    public bool IsDynamic => m_mode == Mode.Curve || m_mode == Mode.MinMax || m_mode == Mode.Steps
                          || m_mode == Mode.Oscillation || HasMultiplier;

    public ZUIValue() { }
    public ZUIValue(float staticValue) { m_static = staticValue; }

    /// <summary>The current external multiplier (1 when none / no resolver).</summary>
    public float Multiplier()
    {
        if (string.IsNullOrEmpty(m_multiplierId) || MultiplierResolver == null) return 1f;
        return MultiplierResolver(m_multiplierId);
    }

    /// <summary>Source value at <paramref name="time"/> seconds, before the multiplier.</summary>
    public float EvaluateRaw(float time)
    {
        switch (m_mode)
        {
            case Mode.Static: return m_static;
            case Mode.MinMax: return UnityEngine.Random.Range(m_min, m_max);
            case Mode.Curve:  return EvaluateCurve(time);
            case Mode.Steps:  return EvaluateSteps(time);
            case Mode.Oscillation: return EvaluateOscillationAtNorm(PhaseNorm01(time));
            default:          return m_static;
        }
    }

    /// <summary>Full value at <paramref name="time"/> seconds: source × multiplier.</summary>
    public float Evaluate(float time) => EvaluateRaw(time) * Multiplier();

    /// <summary>Convenience: evaluate at the current play-time.</summary>
    public float EvaluateNow() => Evaluate(Application.isPlaying ? Time.time : 0f);

    /// <summary>Where <paramref name="time"/> lands inside ONE playthrough, as [0..1]: warmup holds 0, a
    /// non-looping value holds 1 after its duration, a looping one wraps and holds 1 through the cooldown gap.
    /// The single place the timed modes agree on their clock.</summary>
    float PhaseNorm01(float time)
    {
        if (time < m_warmup) return 0f;
        float local = time - m_warmup;
        float dur = Mathf.Max(0.0001f, m_duration);
        float phase;
        if (m_cooldown < 0f)
        {
            // No loop: play once, then hold the final value.
            phase = Mathf.Min(local, dur);
        }
        else
        {
            float period = dur + m_cooldown;
            float t = local % period;
            // During the cooldown gap, hold the end value.
            phase = t <= dur ? t : dur;
        }
        return Mathf.Clamp01(phase / dur);   // points are authored in [0..1]
    }

    float EvaluateCurve(float time) => EvaluateCurveAtNorm(PhaseNorm01(time));

    /// <summary>The curve's value at a NORMALIZED position through one playthrough. Exposed for hosts that own
    /// their own clock and hand this value a progress rather than a wall time (a SpriteFx stack's `life`), so
    /// they get the smoothness-aware sampling instead of re-deriving a partial one.</summary>
    public float EvaluateCurveAtNorm(float norm01)
    {
        float v = ZUIEnvelopeEvaluator.Evaluate(m_points, Mathf.Clamp01(norm01), m_yMax, m_smoothness);
        // A smoothed (Catmull-Rom) curve can overshoot past the authored points; keep the value inside its
        // declared range so a smoothed path can't leave the plot / a bounded value its range. Only when
        // smoothed — the linear path (smoothness 0) already stays in range, so the byte-identical path is kept.
        if (m_smoothness > 0f) v = Mathf.Clamp(v, Mathf.Min(m_yMin, m_yMax), Mathf.Max(m_yMin, m_yMax));
        return v;
    }

    /// <summary>Seed a default 0→1 ramp so a freshly-switched Curve has something to show.</summary>
    public void EnsureCurveDefaults()
    {
        if (m_points == null) m_points = new List<ZUIEnvelopePoint>();
        if (m_points.Count == 0)
        {
            m_points.Add(new ZUIEnvelopePoint(0f, m_yMin));
            m_points.Add(new ZUIEnvelopePoint(1f, m_yMax));
        }
    }

    /// <summary>The HELD value at <paramref name="time"/>: the envelope is divided into <c>steps.Count</c> equal
    /// sections and each section returns its own value, with no interpolation. Same warmup/duration/loop timing
    /// as Curve.</summary>
    float EvaluateSteps(float time) => EvaluateStepsAtNorm(PhaseNorm01(time));

    /// <summary>The held step value at a NORMALIZED position through one playthrough — the Steps twin of
    /// <see cref="EvaluateCurveAtNorm"/>, for a host driving this value off its own progress.</summary>
    public float EvaluateStepsAtNorm(float norm01)
    {
        int n = m_steps != null ? m_steps.Count : 0;
        if (n == 0) return m_yMin;
        int idx = Mathf.Clamp(Mathf.FloorToInt(Mathf.Clamp01(norm01) * n), 0, n - 1);
        return m_steps[idx];
    }

    /// <summary>The oscillation's value at a NORMALIZED position through one playthrough: a sine carrier
    /// travelling between the Floor and Ceiling envelopes, starting AT the floor, at the rate the Rate envelope
    /// asks for. The carrier is fixed by design — what an author shapes is the band and the rate.</summary>
    /// The highest value this can produce over a playthrough — what a host needs to SIZE something for:
    /// a buffer, a bounding box, an allocation. Getting it too low clips the result.
    ///
    /// Existed as three hand-copied switches across Pyre, and every copy fell through to the static value
    /// for Steps, so an authored step sequence was sized as though it were flat. One implementation here
    /// means the next mode added cannot silently under-report in three places.
    ///
    /// ⚠️ Curve deliberately keeps its long-standing convention — the max of the raw authored point
    /// VALUES, not the evaluated output, and not accounting for smoothing overshoot. It is arguably wrong,
    /// but every existing Pyre asset was authored and baked against it, so changing it here would resize
    /// blasts that people already tuned. Steps and Oscillation follow the same convention for consistency.
    public float PeakValue()
    {
        switch (m_mode)
        {
            case Mode.Static: return m_static;
            case Mode.MinMax: return Mathf.Max(m_min, m_max);
            case Mode.Curve:  return PeakOfPoints(m_points, m_static);
            case Mode.Steps:
            {
                if (m_steps == null || m_steps.Count == 0) return m_static;
                float m = float.MinValue;
                foreach (var v in m_steps) if (v > m) m = v;
                return m;
            }
            // The carrier never exceeds its CEILING envelope, so the ceiling is the peak.
            case Mode.Oscillation: return PeakOfPoints(m_oscMax, m_static);
            default: return m_static;
        }
    }

    static float PeakOfPoints(List<ZUIEnvelopePoint> pts, float fallback)
    {
        if (pts == null || pts.Count == 0) return fallback;
        float m = 0f;
        foreach (var p in pts) if (p.value > m) m = p.value;
        return m;
    }
    public float EvaluateOscillationAtNorm(float norm01)
    {
        float t = Mathf.Clamp01(norm01);
        float lo = ZUIEnvelopeEvaluator.Evaluate(m_oscMin, t, m_yMin, m_smoothness);
        float hi = ZUIEnvelopeEvaluator.Evaluate(m_oscMax, t, m_yMax, m_smoothness);
        float s = 0.5f - 0.5f * Mathf.Cos(OscCycles(t) * (Mathf.PI * 2f));
        return Mathf.Lerp(lo, hi, s);
    }

    /// Cycles completed by <paramref name="norm01"/> — the trapezoid INTEGRAL of the rate envelope, not
    /// rate × time. Integrating is what lets the rate itself be animated: speeding up adds cycles faster
    /// instead of yanking the carrier's phase to a new position every time the rate changes.
    float OscCycles(float norm01)
    {
        if (m_oscRate == null || m_oscRate.Count == 0) return norm01;   // nothing authored = one cycle
        const int Steps = 32;
        float total = 0f;
        float prev = ZUIEnvelopeEvaluator.Evaluate(m_oscRate, 0f, 1f, m_smoothness);
        for (int i = 1; i <= Steps; i++)
        {
            float cur = ZUIEnvelopeEvaluator.Evaluate(m_oscRate, norm01 * i / Steps, 1f, m_smoothness);
            total += (prev + cur) * 0.5f;
            prev = cur;
        }
        return total * (norm01 / Steps);
    }

    /// <summary>Seed a flat band and a steady rate so a freshly-switched Oscillation shows a real wave.</summary>
    public void EnsureOscillationDefaults()
    {
        if (m_oscMin == null) m_oscMin = new List<ZUIEnvelopePoint>();
        if (m_oscMax == null) m_oscMax = new List<ZUIEnvelopePoint>();
        if (m_oscRate == null) m_oscRate = new List<ZUIEnvelopePoint>();
        if (m_oscMin.Count == 0)
        {
            m_oscMin.Add(new ZUIEnvelopePoint(0f, m_yMin));
            m_oscMin.Add(new ZUIEnvelopePoint(1f, m_yMin));
        }
        if (m_oscMax.Count == 0)
        {
            m_oscMax.Add(new ZUIEnvelopePoint(0f, m_yMax));
            m_oscMax.Add(new ZUIEnvelopePoint(1f, m_yMax));
        }
        if (m_oscRate.Count == 0)
        {
            float r = Mathf.Clamp(3f, 0f, Mathf.Max(0.1f, m_oscRateMax));
            m_oscRate.Add(new ZUIEnvelopePoint(0f, r));
            m_oscRate.Add(new ZUIEnvelopePoint(1f, r));
        }
    }

    /// <summary>Seed a default flat run of sections so a freshly-switched Steps has something to edit.</summary>
    public void EnsureStepsDefaults(int count = 8)
    {
        if (m_steps == null) m_steps = new List<float>();
        if (m_steps.Count == 0)
            for (int i = 0; i < Mathf.Max(2, count); i++) m_steps.Add(m_yMin);
    }

    /// <summary>Resize the step sections to <paramref name="count"/>, preserving existing values and seeding new
    /// ones at the range's floor.</summary>
    public void SetStepCount(int count)
    {
        if (m_steps == null) m_steps = new List<float>();
        count = Mathf.Clamp(count, 2, 64);
        while (m_steps.Count < count) m_steps.Add(m_yMin);
        while (m_steps.Count > count) m_steps.RemoveAt(m_steps.Count - 1);
    }

    // ── Copy/paste support (editor-invoked, kept here since it's plain data — no UnityEditor dependency) ──

    const string ClipboardPrefix = "ZUIVALUE1:";

    /// <summary>Serializes this value (mode + every mode's data) for the system clipboard.</summary>
    public string ToClipboardString() => ClipboardPrefix + JsonUtility.ToJson(this);

    /// <summary>Parses a string previously produced by <see cref="ToClipboardString"/>. False (and a null
    /// <paramref name="value"/>) for anything else — including another Laubrary control's clipboard payload
    /// (e.g. a ZUIValue2DControl pair) or unrelated clipboard text — so callers can gate a Paste menu item
    /// without risking a garbage paste.</summary>
    public static bool TryFromClipboardString(string s, out ZUIValue value)
    {
        if (string.IsNullOrEmpty(s) || !s.StartsWith(ClipboardPrefix)) { value = null; return false; }
        try { value = JsonUtility.FromJson<ZUIValue>(s.Substring(ClipboardPrefix.Length)); return value != null; }
        catch { value = null; return false; }
    }

    /// <summary>Overwrites every field from <paramref name="other"/> — used by Paste, and by anything that
    /// needs to clone another ZUIValue's data onto an already-wired instance (replacing the reference itself
    /// would break the caller's serialized field). Curve points are deep-copied, not aliased.</summary>
    public void CopyFrom(ZUIValue other)
    {
        if (other == null) return;
        m_mode = other.m_mode;
        m_static = other.m_static;
        m_min = other.m_min;
        m_max = other.m_max;
        m_points.Clear();
        foreach (var p in other.m_points)
            m_points.Add(new ZUIEnvelopePoint(p.time, p.value, p.exponent, p.editState));
        m_yMin = other.m_yMin;
        m_yMax = other.m_yMax;
        m_duration = other.m_duration;
        m_warmup = other.m_warmup;
        m_cooldown = other.m_cooldown;
        m_smoothness = other.m_smoothness;
        m_steps.Clear();
        if (other.m_steps != null) m_steps.AddRange(other.m_steps);
        if (m_oscMin == null) m_oscMin = new List<ZUIEnvelopePoint>();
        if (m_oscMax == null) m_oscMax = new List<ZUIEnvelopePoint>();
        if (m_oscRate == null) m_oscRate = new List<ZUIEnvelopePoint>();
        CopyPoints(other.m_oscMin, m_oscMin);
        CopyPoints(other.m_oscMax, m_oscMax);
        CopyPoints(other.m_oscRate, m_oscRate);
        m_oscRateMax = other.m_oscRateMax;
        m_multiplierId = other.m_multiplierId;
    }

    static void CopyPoints(List<ZUIEnvelopePoint> src, List<ZUIEnvelopePoint> dst)
    {
        dst.Clear();
        if (src == null) return;
        foreach (var p in src) dst.Add(new ZUIEnvelopePoint(p.time, p.value, p.exponent, p.editState));
    }
}
