// DotSelectors.cs
// The three selectors. A selector answers one question per dot — "how much does this one count?" — and
// answers it the same way every time. It never moves or removes anything; a mutator or a drawer decides what
// a weight means to it, which is why the same selector can drive a nudge and a fill at once.

using System;
using UnityEngine;

namespace Laubrary.DotGen
{
    /// Weight by how close a dot is to the edge of its area.
    [Serializable]
    [DotModule("margin", "Margin selector", "Edge margin", Order = 0)]
    public class DotMarginSelector : DotSelector
    {
        [Range(0f, 50f)]
        [Tooltip("How far in from the edge the band reaches.")]
        public float bandWidth = 22f;

        [Range(0f, 40f)]
        [Tooltip("How gradually the band fades into the core.")]
        public float softness = 8f;

        [Range(0f, 100f)]
        [Tooltip("Weight given to dots inside the edge band.")]
        public float edgeBandValue = 100f;

        [Range(0f, 100f)]
        [Tooltip("Weight given to dots in the middle, past the band.")]
        public float innerCoreValue = 0f;

        public override float Weight(DotGenerator gen, in DotArea area, float worldX, float worldY, int pointIndex, int globalSeed)
        {
            Vector2 q = DotGenMath.Invform(worldX, worldY, area);
            float d = DotGenMath.EdgeDepth(q.x, q.y, gen.shape);
            float th = DotGenMath.Clamp01(bandWidth / 50f);
            float soft = Mathf.Max(0.001f, softness / 100f);
            float band = 1f - DotGenMath.Clamp01((d - th + soft) / (soft * 2f));
            return DotGenMath.Clamp01(DotGenMath.Lerp(innerCoreValue / 100f, edgeBandValue / 100f, band));
        }
    }

    /// A linear ramp or a wave across the area.
    public enum DotGradientField { Linear, Wave }

    [Serializable]
    [DotModule("gradient", "Gradient selector", "Directional gradient", Order = 1)]
    public class DotGradientSelector : DotSelector
    {
        [Tooltip("A one-way ramp across the area, or a repeating wave along the same direction.")]
        public DotGradientField field = DotGradientField.Linear;

        [Range(-180f, 180f)]
        [Tooltip("Which way the ramp runs.")]
        public float angle = 0f;

        [Range(-100f, 100f)]
        [Tooltip("Slides the ramp along its own direction, moving where the halfway point falls.")]
        public float offset = 0f;

        [Range(10f, 250f)]
        [Tooltip("How sharply the ramp goes from nothing to everything.")]
        public float contrast = 100f;

        [Range(1f, 8f)]
        [ZUIShowIf("field", "Wave")]
        [Tooltip("How many wave cycles fit across the area.")]
        public float frequency = 2f;

        [Range(0f, 100f)]
        [ZUIShowIf("field", "Wave")]
        [Tooltip("Slides the wave along its own direction.")]
        public float phase = 0f;

        [Tooltip("Swaps which end of the ramp counts.")]
        public bool invert = false;

        public override float Weight(DotGenerator gen, in DotArea area, float worldX, float worldY, int pointIndex, int globalSeed)
        {
            Vector2 q = DotGenMath.Invform(worldX, worldY, area);
            float an = angle * Mathf.Deg2Rad;
            float proj = (q.x * Mathf.Cos(an) + q.y * Mathf.Sin(an)) * 1.42f + offset / 100f;
            float w = DotGenMath.Clamp01(0.5f + proj * (contrast / 100f));
            if (field == DotGradientField.Wave) w = 0.5f + 0.5f * Mathf.Sin((proj * frequency + phase / 100f) * DotGenMath.Tau);
            if (invert) w = 1f - w;
            return DotGenMath.Clamp01(w);
        }
    }

    /// A deterministic binary mask: a dot is either in or out.
    [Serializable]
    [DotModule("random", "Random selector", "Random mask", Order = 2)]
    public class DotRandomSelector : DotSelector
    {
        [Range(0f, 100f)]
        [Tooltip("Roughly what share of the dots this selector picks.")]
        public float amount = 55f;

        [Range(0, 999)]
        [Tooltip("Changes which dots get picked, without changing how many.")]
        public int seedOffset = 19;

        public override float Weight(DotGenerator gen, in DotArea area, float worldX, float worldY, int pointIndex, int globalSeed)
            // The threshold is built in double, not float: the reference compares against `amount/100` at
            // double precision, and a float divide moves it by about 3e-8 — enough to flip one dot in a
            // thousand from in to out and quietly change the picture for the same seed.
            => DotGenMath.Hash01(globalSeed + seedOffset, pointIndex, area.idx) < (double)amount / 100.0 ? 1f : 0f;
    }
}
