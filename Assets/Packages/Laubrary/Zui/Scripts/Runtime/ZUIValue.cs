// ZUIValue.cs
// A configurable numeric value: a single static number, a random min↔max range,
// or an animation curve sampled over time (with a warmup that holds the curve's first value,
// and a cooldown pause before looping). An optional named external multiplier
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
    public enum Mode { Static, MinMax, Curve }

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
    public string multiplierId { get => m_multiplierId; set => m_multiplierId = value; }

    public bool HasMultiplier => !string.IsNullOrEmpty(m_multiplierId);
    public bool IsDynamic => m_mode == Mode.Curve || m_mode == Mode.MinMax || HasMultiplier;

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
            default:          return m_static;
        }
    }

    /// <summary>Full value at <paramref name="time"/> seconds: source × multiplier.</summary>
    public float Evaluate(float time) => EvaluateRaw(time) * Multiplier();

    /// <summary>Convenience: evaluate at the current play-time.</summary>
    public float EvaluateNow() => Evaluate(Application.isPlaying ? Time.time : 0f);

    float EvaluateCurve(float time)
    {
        float fallback = m_yMax;
        // During warmup, hold the curve's first value (norm 0) — that's what the curve reads at its start.
        if (time < m_warmup) return ZUIEnvelopeEvaluator.Evaluate(m_points, 0f, fallback);
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

        float norm = phase / dur; // points are authored in [0..1]
        float v = ZUIEnvelopeEvaluator.Evaluate(m_points, norm, fallback, m_smoothness);
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
        m_multiplierId = other.m_multiplierId;
    }
}
