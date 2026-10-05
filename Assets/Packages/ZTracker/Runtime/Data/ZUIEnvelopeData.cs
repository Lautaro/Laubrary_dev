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

    public bool loopEnabled { get => m_loopEnabled; set => m_loopEnabled = value; }
    public int loopMode { get => m_loopMode; set => m_loopMode = value; }
    public float loopStart { get => m_loopStart; set => m_loopStart = value; }
    public float loopEnd { get => m_loopEnd; set => m_loopEnd = value; }

    public bool requiresEndPoint = true;

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
