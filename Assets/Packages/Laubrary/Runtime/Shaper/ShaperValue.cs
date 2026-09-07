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
                case ZUIValue.Mode.MinMax: raw = Mathf.Lerp(v.min, v.max, UnitOf(HashMix(seed))); break;
                case ZUIValue.Mode.Curve: raw = v.EvaluateCurveAtNorm(phase01); break;
                case ZUIValue.Mode.Steps: raw = v.EvaluateStepsAtNorm(phase01); break;
                case ZUIValue.Mode.Oscillation: raw = v.EvaluateOscillationAtNorm(phase01); break;
                default: raw = v.staticValue; break;
            }
            return raw * v.Multiplier();
        }

        /// <summary>
        /// An integer avalanche hash (lowbias32), so every field id gets an uncorrelated draw.
        ///
        /// Public because it is the definition of "a deterministic draw" in this engine, and a second caller
        /// now needs exactly that definition: <see cref="ShaperCherry"/>'s min/max hold lengths and
        /// multi-frame picks (T-0143). Re-implementing the same avalanche there would leave two copies free to
        /// drift, which for a hash means two things that are supposed to agree quietly stop agreeing.
        /// </summary>
        public static uint HashMix(uint x)
        {
            x ^= x >> 16; x *= 0x7feb352du;
            x ^= x >> 15; x *= 0x846ca68bu;
            x ^= x >> 16;
            return x;
        }

        /// <summary>A hashed word folded to a unit float in [0,1).</summary>
        public static float UnitOf(uint h) => (h & 0x00FFFFFFu) * (1f / 16777216f);
    }

    /// <summary>
    /// The plain-number view of an animatable dial: its STATIC value, read and written in place. Every authored
    /// block that carries <see cref="ZUIValue"/> dials exposes its fields twice — once as the dial (which the
    /// window edits and the compiler samples over the document's phase) and once as a bare number, which is
    /// what "set this radius to 40" means when it is set from code rather than authored as an envelope.
    ///
    /// Writing through this view deliberately leaves the dial's MODE alone: it edits the number a Static dial
    /// shows and the number a Curve dial is scaled from, never converting one into the other.
    /// </summary>
    public static class ShaperDial
    {
        public static float Get(ZUIValue v, float fallback = 0f) => v != null ? v.staticValue : fallback;

        public static void Set(ref ZUIValue v, float value)
        {
            if (v == null) v = new ZUIValue(value);
            else v.staticValue = value;
        }
    }

    /// <summary>
    /// A settings block whose animatable dials were promoted from plain floats and can still be null on an
    /// object that was constructed in code rather than deserialized. Callers that need every dial to exist
    /// (the layer-key hasher) ask for that and nothing more: the disk-to-memory migration in
    /// <c>OnAfterDeserialize</c> must never run on a live object, because on a freshly constructed block the
    /// "promoted" flag is still false and the migration would overwrite whatever the code just seeded
    /// (the new-document growth curve was lost exactly that way).
    /// </summary>
    public interface IShaperDialOwner
    {
        void EnsureDials();
    }
}
