// ZUIEnvelopeData — Serializable multi-point envelope data model.
// Runtime-safe. Holds List<ZUIEnvelopePoint> so the list can be handed
// directly to ZUI.Envelope (editor-side) without conversion.

using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class ZUIEnvelopeData
{
    [SerializeField] bool m_enabled = true;
    [SerializeField] float m_xMin = 0f;
    [SerializeField] float m_xMax = 1f;
    [SerializeField] float m_yMin = 0f;
    [SerializeField] float m_yMax = 1f;
    [SerializeField] List<ZUIEnvelopePoint> m_points = new List<ZUIEnvelopePoint>();

    // Loop
    [SerializeField] bool m_loopEnabled = false;
    [SerializeField] int m_loopMode = 0; // 0=off, 1=forward, 2=ping-pong
    [SerializeField] float m_loopStart = 0f;
    [SerializeField] float m_loopEnd = 1f;

    public bool enabled { get => m_enabled; set => m_enabled = value; }
    public float xMin { get => m_xMin; set => m_xMin = value; }
    public float xMax { get => m_xMax; set { m_xMax = value; if (requiresEndPoint && m_points.Count > 0) m_points[m_points.Count - 1].time = value; } }
    public float yMin { get => m_yMin; set => m_yMin = value; }
    public float yMax { get => m_yMax; set => m_yMax = value; }
    public int Count => m_points.Count;

    /// <summary>Direct access to the point list — hand this to ZUI.Envelope.</summary>
    public List<ZUIEnvelopePoint> points => m_points;

    // An author-chosen top of the value range (T-0640, the envelope's Maximum): when set, yMax is that top and an
    // editor keeps it instead of its own default range. Display only; the engine never reads yMax.
    [SerializeField] bool m_customMaximum = false;
    public bool customMaximum { get => m_customMaximum; set => m_customMaximum = value; }

    // ADSR mode (T-0639): the envelope is drawn from four stages instead of free points. The points are always written
    // from them (start, attack to the peak, decay to the sustain level, the sustain point, release back to start), so
    // the engine plays it as an ordinary envelope; it only adds the ADSR release (leave from the level reached). Times in
    // seconds; sustain is a share (0..1) of the way from start to the peak; start is in the parameter's stored units.
    [SerializeField] bool m_adsr = false;
    [SerializeField] bool m_adsrSet = false;
    [SerializeField] float m_adsrAttack = .01f, m_adsrDecay = .2f, m_adsrSustain = .7f, m_adsrRelease = .5f, m_adsrStart = 0f;
    public bool adsr { get => m_adsr; set => m_adsr = value; }
    /// Whether the ADSR stages and start were ever set (a fresh one takes the editor's per-parameter start).
    public bool adsrSet { get => m_adsrSet; set => m_adsrSet = value; }
    public float adsrAttack { get => m_adsrAttack; set => m_adsrAttack = value; }
    public float adsrDecay { get => m_adsrDecay; set => m_adsrDecay = value; }
    public float adsrSustain { get => m_adsrSustain; set => m_adsrSustain = value; }
    public float adsrRelease { get => m_adsrRelease; set => m_adsrRelease = value; }
    public float adsrStart { get => m_adsrStart; set => m_adsrStart = value; }

    /// Writes the points (and sustain point and length) of the ADSR shape rising from adsrStart to <paramref name="peak"/>.
    /// A stage of 0 still advances by a tenth of a millisecond, because points need strictly increasing times; an
    /// attack of 0 starts at the peak instead (a vertical wall).
    public void ShapeAdsr(float peak)
    {
        const float Gap = .0001f;
        float a = Mathf.Max(0, m_adsrAttack), d = Mathf.Max(0, m_adsrDecay), s = Mathf.Clamp01(m_adsrSustain), r = Mathf.Max(0, m_adsrRelease), start = m_adsrStart;
        m_points.Clear();
        if (a > 0) { m_points.Add(new ZUIEnvelopePoint(0f, start, 1f)); m_points.Add(new ZUIEnvelopePoint(a, peak, 1f)); }
        else m_points.Add(new ZUIEnvelopePoint(0f, peak, 1f));
        float held = a + Mathf.Max(d, Gap), end = held + Mathf.Max(r, Gap);
        m_points.Add(new ZUIEnvelopePoint(held, start + (peak - start) * s, 1f));
        m_points.Add(new ZUIEnvelopePoint(end, start, 1f));
        requiresEndPoint = false;
        m_xMin = 0; m_xMax = end; sustainEnabled = true; sustainPosition = held; m_loopEnabled = false; durationInRows = false; m_enabled = true; m_adsr = true;
    }

    // LFO (T-0641): a wave added to the envelope's (or ADSR's) value on every note, in the parameter's stored units.
    // Speed is cycles per second, or cycles per song row for an envelope measured in rows; Delay is how long the LFO
    // takes to fade in from nothing to its full depth, in the envelope's own time unit (seconds or rows). Shape:
    // 0 sine, 1 triangle, 2 saw, 3 square; every shape starts at the centre (0), so a note starts on the envelope.
    // Off on everything saved before it existed, so those play exactly as before.
    [SerializeField] bool m_lfo = false;
    [SerializeField] float m_lfoDepth = 0f, m_lfoSpeed = 4f, m_lfoDelay = 0f;
    [SerializeField] int m_lfoShape = 0;
    public bool lfo { get => m_lfo; set => m_lfo = value; }
    public float lfoDepth { get => m_lfoDepth; set => m_lfoDepth = value; }
    public float lfoSpeed { get => m_lfoSpeed; set => m_lfoSpeed = value; }
    public float lfoDelay { get => m_lfoDelay; set => m_lfoDelay = value; }
    public int lfoShape { get => m_lfoShape; set => m_lfoShape = value; }

    /// The LFO's offset (in the parameter's units) at <paramref name="time"/> since the note started, in the envelope's
    /// own unit. The engine computes the same thing per note; editors use this to draw the merged wave.
    public float LfoAt(float time)
    {
        if (!m_lfo || m_lfoDepth == 0f) return 0f;
        float phase = time * m_lfoSpeed; phase -= Mathf.Floor(phase);
        float fade = m_lfoDelay > 0 ? Mathf.Clamp01(time / m_lfoDelay) : 1f;
        return LfoWave(m_lfoShape, phase) * m_lfoDepth * fade;
    }

    /// One cycle of an LFO shape (phase 0..1), from -1 to 1, starting at 0 and rising.
    public static float LfoWave(int shape, float phase)
    {
        switch (shape)
        {
            case 1: return phase < .25f ? 4f * phase : phase < .75f ? 2f - 4f * phase : 4f * phase - 4f;
            case 2: return phase < .5f ? 2f * phase : 2f * phase - 2f;
            case 3: return phase < .5f ? 1f : -1f;
            default: return Mathf.Sin(phase * 2f * Mathf.PI);
        }
    }

    public bool loopEnabled { get => m_loopEnabled; set => m_loopEnabled = value; }
    public int loopMode { get => m_loopMode; set => m_loopMode = value; }
    public float loopStart { get => m_loopStart; set => m_loopStart = value; }
    public float loopEnd { get => m_loopEnd; set => m_loopEnd = value; }

    public bool requiresEndPoint = true;
    // A held note stops at this musical time. Note-off resumes the curve and
    // exits any loop; each voice owns its cursor in the prepared renderer.
    public bool sustainEnabled;
    public float sustainPosition;
    // Times (xMax, points, loop, sustain) are song rows instead of seconds, so the envelope follows the song's tempo.
    // False on everything saved before rows existed.
    public bool durationInRows;

    public ZUIEnvelopeData() { }

    public ZUIEnvelopeData(float yMin, float yMax, bool addDefaults = true)
    {
        m_yMin = yMin; m_yMax = yMax;
        if (addDefaults)
        {
            m_points.Add(new ZUIEnvelopePoint(0f, yMax, 1f));
            if (requiresEndPoint)
                m_points.Add(new ZUIEnvelopePoint(1f, yMax, 1f));
        }
    }

    public ZUIEnvelopeData(float xMin, float xMax, float yMin, float yMax, bool addDefaults = true)
    {
        m_xMin = xMin; m_xMax = xMax; m_yMin = yMin; m_yMax = yMax;
        if (addDefaults)
        {
            m_points.Add(new ZUIEnvelopePoint(xMin, yMax, 1f));
            if (requiresEndPoint)
                m_points.Add(new ZUIEnvelopePoint(xMax, yMax, 1f));
        }
    }

    public ZUIEnvelopePoint GetPoint(int i) => m_points[i];

    public float Evaluate(float time)
    {
        if (m_points.Count == 0) return m_yMax;
        if (m_points.Count == 1) return m_points[0].value;

        int idx = 0;
        for (int i = 0; i < m_points.Count; i++)
        {
            if (m_points[i].time > time) { idx = i; break; }
            idx = i + 1;
        }

        if (idx <= 0) return m_points[0].value;
        if (idx >= m_points.Count) return m_points[m_points.Count - 1].value;

        float x1 = m_points[idx - 1].time, x2 = m_points[idx].time;
        float range = x2 - x1;
        if (range <= 0) return m_points[idx].value;
        float t = (time - x1) / range;
        return Mathf.Lerp(m_points[idx - 1].value, m_points[idx].value, Mathf.Pow(t, m_points[idx].exponent));
    }

    public ZUIEnvelopePoint AddPoint(float time, float value)
    {
        int idx = 0;
        for (int i = 0; i < m_points.Count; i++) { if (time < m_points[i].time) break; idx = i + 1; }
        var p = new ZUIEnvelopePoint(time, value, 1f);
        m_points.Insert(idx, p);
        return p;
    }

    public void RemovePoint(int idx)
    {
        if (idx <= 0 || idx >= m_points.Count) return;
        if (requiresEndPoint && idx == m_points.Count - 1) return;
        m_points.RemoveAt(idx);
    }

    public void RemovePoint(ZUIEnvelopePoint p)
    {
        int idx = m_points.IndexOf(p);
        RemovePoint(idx);
    }

    public int IndexOf(ZUIEnvelopePoint p) => m_points.IndexOf(p);

    public void ForEach(Action<int, ZUIEnvelopePoint> handler)
    {
        for (int i = 0; i < m_points.Count; i++) handler(i, m_points[i]);
    }

    public ZUIEnvelopeData DeepCopy()
    {
        var json = JsonUtility.ToJson(this);
        return JsonUtility.FromJson<ZUIEnvelopeData>(json);
    }
}
