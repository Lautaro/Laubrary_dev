using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Zounds {

    [System.Serializable]
    public class Envelope {

        internal static bool requiresEndPoint = true;

        [SerializeField] private bool m_enabled = false;

        [SerializeField] private float m_xMin = 0f;
        [SerializeField] private float m_xMax = 1f;

        [SerializeField] private float m_yMin = 0f;
        [SerializeField] private float m_yMax = 1f;

        // Points live in a shared ZUI-side type so ZUI.Envelope can render /
        // edit them directly without conversion. Field names (time, value,
        // exponent) match the old nested Point class so existing serialized
        // Envelopes in saved projects deserialize unchanged.
        [SerializeField] private List<ZUIEnvelopePoint> m_points = new List<ZUIEnvelopePoint>();

        public Envelope(float yMin, float yMax) {
            m_xMin = 0f;
            m_xMax = 1f;
            m_yMin = yMin;
            m_yMax = yMax;
            InitRequiredPoints(xMax);
        }

        public Envelope(float xMin, float xMax, float yMin, float yMax) {
            m_xMin = xMin;
            m_xMax = xMax;
            m_yMin = yMin;
            m_yMax = yMax;
            InitRequiredPoints(xMax);
        }

        private void InitRequiredPoints(float xMax) {
            AddPoint(0, 1);
            if (requiresEndPoint) {
                AddPoint(xMax, 1);
            }
        }

        public bool enabled { get => m_enabled; set => m_enabled = value; }

        public float xMin {
            get => m_xMin;
            set {
                m_xMin = value;
            }
        }

        public float xMax {
            get => m_xMax;
            set {
                m_xMax = value;
                if (requiresEndPoint) {
                    m_points[m_points.Count - 1].time = value;
                }
            }
        }

        public float yMin {
            get => m_yMin;
            set {
                m_yMin = value;
            }
        }

        public float yMax {
            get => m_yMax;
            set {
                m_yMax = value;
            }
        }

        public int Count => m_points.Count;

        public float startingValue {
            get {
                return m_points[0].value;
            }
            set {
                m_points[0].value = value;
            }
        }

        public ZUIEnvelopePoint GetPoint(int pointIndex) {
            return m_points[pointIndex];
        }

        /// <summary>Direct access to the backing point list — needed so editor
        /// code can pass it to ZUI.Envelope, which mutates in-place.</summary>
        public List<ZUIEnvelopePoint> GetPointsList() {
            return m_points;
        }

        public float Evaluate(float time) {
            if (m_points.Count == 0) return 1f;

            if (m_points.Count == 1) return m_points[0].value;

            int index = -1;
            foreach (var point in m_points) {
                if (point.time > time) {
                    index++;
                    break;
                }
                index++;
            }

            if (index == 0) {
                return m_points[0].value;
            }

            float x1 = m_points[index - 1].time;
            float x2 = m_points[index].time;
            float t = (time - x1) / (x2 - x1);
            // Canonical DAW-style bend: Lerp(a, b, Pow(t, exp)). Matches
            // ZUI.Envelope.BendSegment exactly so audio tracks the visual.
            float exp = m_points[index].exponent;
            if (exp <= 0f) exp = 0.000001f;
            return Mathf.Lerp(m_points[index - 1].value, m_points[index].value, Mathf.Pow(t, exp));
        }

        public ZUIEnvelopePoint AddPoint(float time, float value) {
            int index = GetClosestIndexCeil(time);
            var newPoint = new ZUIEnvelopePoint {
                time = time,
                value = value,
                exponent = 1f
            };
            m_points.Insert(index, newPoint);
            return newPoint;
        }

        public void RemovePoint(int pointIndex) {
            if (pointIndex == 0 || pointIndex >= m_points.Count) return;
            if (requiresEndPoint) {
                if (pointIndex == m_points.Count - 1) return;
            }
            m_points.RemoveAt(pointIndex);
        }

        public void RemovePoint(ZUIEnvelopePoint point) {
            var index = IndexOf(point);
            if (index == 0) return;
            if (requiresEndPoint) {
                if (index == m_points.Count - 1) return;
            }
            m_points.Remove(point);
        }

        public int GetClosestIndexCeil(float time) {
            int index = 0;
            for (int i = 0; i < m_points.Count; i++) {
                if (time < m_points[i].time) {
                    break;
                }
                index++;
            }
            return index;
        }

        public void Clear() {
            var firstPoint = m_points[0];
            m_points.Clear();
            m_points.Add(firstPoint);
        }

        public void ForEach(System.Action<int, ZUIEnvelopePoint> handler) {
            for (int i = 0; i < m_points.Count; i++) {
                handler(i, m_points[i]);
            }
        }

        public int IndexOf(ZUIEnvelopePoint point) {
            return m_points.IndexOf(point);
        }

        public Envelope DeepCopy() {
            var serialized = JsonUtility.ToJson(this);
            return JsonUtility.FromJson<Envelope>(serialized);
        }

        public bool IsIdentical(Envelope other) {
            if (other == null) return false;
            if (m_enabled != other.m_enabled) return false;
            if (m_points.Count != other.m_points.Count) return false;
            
            for (int i = 0; i < m_points.Count; i++) {
                if (!Mathf.Approximately(m_points[i].time, other.m_points[i].time)) return false;
                if (!Mathf.Approximately(m_points[i].value, other.m_points[i].value)) return false;
                if (!Mathf.Approximately(m_points[i].exponent, other.m_points[i].exponent)) return false;
            }
            return true;
        }

    }

}
