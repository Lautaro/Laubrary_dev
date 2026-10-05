using Unity.Collections;
using Laubrary.Zounds;
using Laubrary.Zounds.Dsp;
using Mathf = Laubrary.Audio.AudioMath;

namespace Laubrary.Audio {
    public static class EnvelopeRandomMath {
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

    }
}
