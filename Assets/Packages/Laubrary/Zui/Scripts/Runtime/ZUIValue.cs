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
        return ZUIEnvelopeEvaluator.Evaluate(m_points, norm, fallback);
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
}
