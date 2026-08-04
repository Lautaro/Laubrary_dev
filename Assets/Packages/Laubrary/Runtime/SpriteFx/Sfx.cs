using UnityEngine;

namespace Laubrary.SpriteFx
{
    /// Neutral, dependency-free primitives shared by the stateless SpriteFx modifiers (and forwarded to by Pyre so
    /// there is a single canonical copy). Extracted verbatim from Pyre's BlastRenderer/Layer when the stateless
    /// modifier family was elevated out of the Pyre assembly, so Lauminaries/Chunks can reuse them without depending on
    /// Pyre. Hash01 is byte-frozen — every baked/loaded asset depends on its exact output; do not alter it.
    public static class Sfx
    {
        /// Deterministic 0..1 hash of three ints (FNV-1a folded through a MurmurHash finaliser). The determinism
        /// backbone of every noise/scatter/jitter pass — must stay bit-identical across editor preview, baker and
        /// runtime. Pyre's BlastRenderer.Hash01 forwards here so there is one canonical implementation.
        public static float Hash01(int a, int b, int c)
        {
            unchecked
            {
                uint h = 2166136261u;
                h = (h ^ (uint)a) * 16777619u;
                h = (h ^ (uint)b) * 16777619u;
                h = (h ^ (uint)c) * 16777619u;
                h ^= h >> 13; h *= 0x5bd1e995u; h ^= h >> 15;
                return (h & 0xFFFFFF) / (float)0x1000000;
            }
        }

        /// A ZUIValue in Curve mode built from flat (time, value) pairs. yMax bounds the curve editor's Y axis.
        public static ZUIValue CurveVal(float yMax, params float[] tv)
        {
            var v = new ZUIValue { mode = ZUIValue.Mode.Curve, yMin = 0f, yMax = yMax };
            v.points.Clear();
            for (int i = 0; i + 1 < tv.Length; i += 2) v.points.Add(new ZUIEnvelopePoint(tv[i], tv[i + 1]));
            return v;
        }

        /// A flat white gradient — the identity for the multiplying cross gradient / colour grade.
        public static Gradient WhiteGradient()
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return g;
        }

        /// A deep copy of an animatable value. Delegates to ZUIValue's own CopyFrom so a clone carries EVERY
        /// mode's data — a hand-listed field set silently drops whatever a later mode adds (this one used to
        /// lose curve smoothness and the whole step sequence).
        public static ZUIValue CloneVal(ZUIValue s)
        {
            if (s == null) return new ZUIValue();
            var v = new ZUIValue();
            v.CopyFrom(s);   // deep-copies mode + every mode's data (curve points, smoothness, steps included)
            return v;
        }

        public static Gradient CloneGradient(Gradient g)
        {
            if (g == null) return null;
            var n = new Gradient();
            n.SetKeys(g.colorKeys, g.alphaKeys);
            n.mode = g.mode;
            return n;
        }
    }

    /// How a shape's colour gradient is applied (solid shapes — Disc / Crescent / MetaBlob). Shared by Pyre's shape
    /// fill and by SpriteFx modifiers (Voronoi crack / Outline). Serialized as an int, so the namespace move is
    /// byte-transparent to existing assets. Append-only.
    public enum ColorMode
    {
        OverLife,     // one colour for the whole shape, sampled from the gradient at the shape's life 0→1
        Fill,         // the gradient fills the shape spatially (centre → edge), constant over life
        FlowingFill,  // a spatial fill whose gradient scrolls through its spectrum over the shape's life
        NoiseFill     // the gradient is painted through a domain-warped noise field sampled inside the shape
    }
}
