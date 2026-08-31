using UnityEngine;

namespace Laubrary.Shaper
{
    /// <summary>
    /// The one place a <see cref="ZUIValue"/> dial is sampled. Called once per compile, never per sample — a
    /// value funnel must be evaluated <i>into</i> the compiled program up front and never called from inside
    /// the per-tile loop (BC-1.2).
    ///
    /// It takes a normalised phase in [0,1] rather than a wall time, and reaches for ZUI's own
    /// <c>Evaluate*AtNorm</c> entry points. Calling <c>ZUIValue.Evaluate(t)</c> instead would divide the time
    /// by the value's <c>duration</c> (default 4 s) and sweep only the curve's first quarter — the silent
    /// wrongness Pyre's <c>Eval</c> exists to avoid (<c>PyreRenderer.cs:5037-5039</c>).
    /// </summary>
    public static class ShaperValue
    {
        /// <summary>
        /// The dial's value at <paramref name="phase01"/>.
        ///
        /// <c>MinMax</c> draws once, deterministically, from an integer hash of the seed rather than from
        /// <c>System.Random</c> or <c>UnityEngine.Random</c>: the first is banned from a generator (BC-1.3),
        /// the second re-rolls on every call and would make a dial flicker every frame.
        /// <c>Oscillation</c> is sampled properly rather than silently falling through to the static value,
        /// which is the one mode Pyre's own funnel still drops.
        /// </summary>
        public static float Sample(ZUIValue v, float phase01, uint seed, float fallback = 0f)
        {
            if (v == null) return fallback;
            float raw;
            switch (v.mode)
            {
                case ZUIValue.Mode.Static: raw = v.staticValue; break;
                case ZUIValue.Mode.MinMax: raw = Mathf.Lerp(v.min, v.max, Unit(Hash(seed))); break;
                case ZUIValue.Mode.Curve: raw = v.EvaluateCurveAtNorm(phase01); break;
                case ZUIValue.Mode.Steps: raw = v.EvaluateStepsAtNorm(phase01); break;
                case ZUIValue.Mode.Oscillation: raw = v.EvaluateOscillationAtNorm(phase01); break;
                default: raw = v.staticValue; break;
            }
            return raw * v.Multiplier();
        }

        /// <summary>An integer avalanche hash (lowbias32), so every field id gets an uncorrelated draw.</summary>
        static uint Hash(uint x)
        {
            x ^= x >> 16; x *= 0x7feb352du;
            x ^= x >> 15; x *= 0x846ca68bu;
            x ^= x >> 16;
            return x;
        }

        static float Unit(uint h) => (h & 0x00FFFFFFu) * (1f / 16777216f);
    }
}
