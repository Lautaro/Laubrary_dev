using UnityEngine;

namespace Laubrary.Shaper
{
    /// <summary>
    /// T-0111 — one imported Kiln Tapestry Shape draw: a signed, unnormalised, seamlessly-tileable
    /// height field, ready to hand to a <see cref="ShaperFillKind.HeightField"/> fill's
    /// <see cref="ShaperFillDef.heightField"/> slot.
    ///
    /// <b>Deliberately a thin wrapper, not a re-encoding.</b> <see cref="field"/> is the exact texture
    /// the import pipeline wrote — an <c>RFloat</c>, non-sRGB, Read/Write-enabled
    /// <see cref="Texture2D"/> whose texel values are the SAME IEEE-754 float32 bits as the source
    /// <c>.npy</c> array, byte for byte. <see cref="measuredMin"/>/<see cref="measuredMax"/> are
    /// INFORMATIONAL ONLY (an author-facing "this preset's natural range is X..Y" hint) — nothing in
    /// the fill contract reads them to rescale anything; the one and only place magnitude is decided is
    /// <see cref="ShaperFillDef.heightFieldScale"/>, authored on the fill itself. See
    /// <c>D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0111\TAPESTRY-SPEC.md</c> Part 3 for the
    /// normalise-vs-scale decision this records.
    /// </summary>
    [CreateAssetMenu(menuName = "Laubrary/Shaper Height Field Preset", fileName = "HeightField")]
    public class ShaperHeightFieldPreset : ScriptableObject
    {
        /// <summary>The RFloat field texture. A sub-asset of this same file, embedded at import.</summary>
        public Texture2D field;

        /// <summary>The Kiln draw this came from, e.g. <c>"lines/GEN8/002"</c> — for provenance, never parsed.</summary>
        public string sourceId;

        /// <summary>Square resolution — 256 for all 245 imported presets (T-0111 could not locate the 3 at 128 the planning pass described; see VERIFICATION.md Part 1).</summary>
        public int resolution;

        /// <summary>The field's own minimum, measured at import. Informational only — see the class doc.</summary>
        public float measuredMin;

        /// <summary>The field's own maximum, measured at import. Informational only — see the class doc.</summary>
        public float measuredMax;
    }
}
