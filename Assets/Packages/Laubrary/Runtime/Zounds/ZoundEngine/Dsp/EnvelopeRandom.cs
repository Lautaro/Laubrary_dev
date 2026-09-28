using UnityEngine;

namespace Laubrary.Zounds.Dsp {

    /// <summary>
    /// Random envelope points (T-0483): a point can wander, per play, anywhere inside an ellipse around where it was drawn
    /// -- an X radius (along the curve, as a fraction of it) and a Y radius (in the curve's own values) -- with a bias that
    /// decides where in the ellipse it tends to land: 0.5 is an even spread over the whole ellipse, towards 0 it keeps
    /// near the middle, towards 1 near the edge.
    ///
    /// **The draw.** Each play draws each point's offset from an integer hash of the play's seed (fixed when the play
    /// starts and kept in the voice), the curve and the point -- never from a managed random source, so it is the same on
    /// the audio thread, in a compiled job and on the main thread, and the same every time it is read. That is what lets
    /// the voice work the offset out whenever it reads the point rather than keep a per-play copy of every curve (which it
    /// could not allocate on the audio thread when a live edit swaps its layout), and what lets the play-length calculation
    /// use exactly the curve the play will hear.
    ///
    /// **Even by area.** The distance from the middle is the radius times u^k with u uniform: k = 1/2 at bias 0.5 is the
    /// square root that spreads points evenly over the area (a plain uniform distance would crowd them in the middle); a
    /// lower bias raises k (towards the middle), a higher one lowers it (towards the edge).
    ///
    /// **Order is kept.** A point's drawn time stays between the midpoints to its neighbours, so points never swap order
    /// however large the radii; the first and last points keep their time (a curve always spans its whole length). The
    /// drawn value stays inside the curve's range.
    /// </summary>
    public static class EnvelopeRandom {

        /// <summary>The seed for a play, from its token (never nought for a real play).</summary>
        public static uint SeedFor(long tokenId) => 0x3C6EF372u ^ (uint)tokenId * 0x9E3779B1u ^ (uint)(tokenId >> 32) * 0x85EBCA77u;

        static uint Mix(uint h) { h ^= h >> 16; h *= 0x7FEB352Du; h ^= h >> 15; h *= 0x846CA68Bu; h ^= h >> 16; return h; }

        /// <summary>The drawn offset of point <paramref name="point"/> of modifier <paramref name="mod"/>'s curve.</summary>
        public static void Offset(uint seed, int mod, int point, float rx, float ry, float bias, out float dx, out float dy) {
            uint h = Mix(seed ^ Mix((uint)mod * 0x9E3779B1u + 0x7F4A7C15u) ^ ((uint)point * 0x85EBCA77u + 0xC2B2AE3Du));
            float u1 = (h & 0xFFFFFF) / 16777216f;
            h = Mix(h + 0x68E31DA4u);
            float u2 = ((h & 0xFFFFFF) + 0.5f) / 16777216f;
            float b = bias < 0.02f ? 0.02f : bias > 0.98f ? 0.98f : bias;
            float k = 0.5f * (1f - b) / b;
            float r = Mathf.Pow(u2, k);
            float a = u1 * 6.28318530718f;
            dx = r * Mathf.Cos(a) * rx;
            dy = r * Mathf.Sin(a) * ry;
        }

        /// <summary>A drawn time: moved by <paramref name="dx"/>, kept between the midpoints to its neighbours; the first and
        /// last points never move in time.</summary>
        public static float DrawnTime(float time, float prevTime, float nextTime, bool endpoint, float dx) {
            if (endpoint) return time;
            float lo = (prevTime + time) * 0.5f, hi = (time + nextTime) * 0.5f;
            float t = time + dx;
            return t < lo ? lo : t > hi ? hi : t;
        }

        public static float DrawnValue(float value, float dy, float yMin, float yMax) {
            float v = value + dy;
            return v < yMin ? yMin : v > yMax ? yMax : v;
        }

        public static bool IsRandom(ZUIEnvelopePoint p) => p != null && (p.randomX > 0f || p.randomY > 0f);

        public static bool HasRandom(Envelope e) {
            if (e == null) return false;
            var pts = e.GetPointsList();
            for (int i = 0; i < pts.Count; i++) if (IsRandom(pts[i])) return true;
            return false;
        }

        /// <summary>
        /// The curve's value at <paramref name="time"/> as a play with <paramref name="seed"/> hears it (main thread; the
        /// voice does the same arithmetic). Without random points, or when <paramref name="drawn"/> is false (a display that
        /// has no play to show: the curve as drawn), exactly <see cref="Envelope.Evaluate"/>.
        /// </summary>
        public static float Evaluate(Envelope curve, bool drawn, uint seed, int mod, float time) {
            if (!drawn || !HasRandom(curve)) return curve.Evaluate(time);
            var pts = curve.GetPointsList();
            int n = pts.Count;
            if (n == 0) return 1f;
            float T(int i) {
                var p = pts[i];
                if (p.randomX <= 0f && p.randomY <= 0f) return p.time;
                Offset(seed, mod, i, p.randomX, p.randomY, p.randomBias, out float dx, out _);
                return DrawnTime(p.time, i > 0 ? pts[i - 1].time : p.time, i < n - 1 ? pts[i + 1].time : p.time, i == 0 || i == n - 1, dx);
            }
            float V(int i) {
                var p = pts[i];
                if (p.randomX <= 0f && p.randomY <= 0f) return p.value;
                Offset(seed, mod, i, p.randomX, p.randomY, p.randomBias, out _, out float dy);
                return DrawnValue(p.value, dy, curve.yMin, curve.yMax);
            }
            if (n == 1) return V(0);
            if (time <= T(0)) return V(0);
            if (time >= T(n - 1)) return V(n - 1);
            int s = 0;
            while (s < n - 2 && T(s + 1) <= time) s++;
            float x1 = T(s), x2 = T(s + 1);
            float u = x2 > x1 ? (time - x1) / (x2 - x1) : 1f;
            float exp = pts[s + 1].exponent;
            if (exp <= 0f) exp = 0.000001f;
            float a = V(s), b = V(s + 1);
            return a + (b - a) * Mathf.Pow(u, exp);
        }
    }
}
