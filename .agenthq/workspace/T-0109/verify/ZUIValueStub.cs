using System;
using UnityEngine;

// Minimal stand-in for ZUI's ZUIValue, carrying only the surface the Shaper runtime reads.
// Static mode only, which is what every compiled Shaper dial uses in these probes.
[Serializable]
public class ZUIValue
{
    public enum Mode { Static, MinMax, Curve, Steps, Oscillation }

    public Mode m_mode = Mode.Static;
    public float m_static = 1f;
    public float m_min = 0f;
    public float m_max = 1f;
    public float m_multiplier = 1f;

    public ZUIValue() { }
    public ZUIValue(float v) { m_static = v; }

    public Mode mode { get { return m_mode; } set { m_mode = value; } }
    public float staticValue { get { return m_static; } set { m_static = value; } }
    public float min { get { return m_min; } set { m_min = value; } }
    public float max { get { return m_max; } set { m_max = value; } }

    public float Multiplier() { return m_multiplier; }
    public float EvaluateCurveAtNorm(float p) { return m_static; }
    public float EvaluateStepsAtNorm(float p) { return m_static; }
    public float EvaluateOscillationAtNorm(float p) { return m_static; }
    public float Evaluate() { return m_static * m_multiplier; }
    public float EvaluateNow() { return m_static * m_multiplier; }
}
