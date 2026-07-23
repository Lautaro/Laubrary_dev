using UnityEngine;

namespace Laubrary.Chunks
{
    /// The pseudo-3D trick for a sampled debris chunk: fake a flat sprite tumbling like a lit solid fragment,
    /// with no real 3D geometry. Modelled as rotation about the chunk's own vertical axis as seen face-on:
    /// - squash the sprite's WIDTH by |cos(phase)| — full width face-on (phase 0/180), edge-on-thin at 90/270.
    /// - shade the sprite's colour by a sine of the same phase — darker turning away, brighter catching the
    ///   light — offset from the squash curve so the "lit side" swings independently, the way a real lit
    ///   solid does (squash and shading don't peak at the same moment for an actual rotating object).
    /// A pure function so the runtime chunk update and any future editor preview stay in lockstep by
    /// construction — there's only one place this math lives.
    public static class ChunkTumble
    {
        /// phaseDeg: accumulated simulated rotation in degrees (wraps freely, no need to clamp before calling).
        /// shadeStrength: 0 = no shading (squash only), 1 = full light/dark swing.
        /// Returns (squashX, shade): squashX multiplies the chunk's width scale; shade multiplies its colour.
        public static (float squashX, float shade) Evaluate(float phaseDeg, float shadeStrength)
        {
            float rad = phaseDeg * Mathf.Deg2Rad;
            float squashX = Mathf.Abs(Mathf.Cos(rad));
            float swing = Mathf.Sin(rad) * Mathf.Clamp01(shadeStrength);
            float shade = 1f + swing * 0.4f; // ±40% at full strength, symmetric around neutral (1.0)
            return (squashX, shade);
        }
    }
}
