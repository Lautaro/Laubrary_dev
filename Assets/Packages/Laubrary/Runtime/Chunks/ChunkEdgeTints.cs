using UnityEngine;

namespace Laubrary.Chunks
{
    /// The named edge-tint ramps a sampled cut can be scorched/wounded with, plus Custom for an author's own.
    /// Ordered so the C# default (0) is a real preset rather than "Custom with nothing authored in it" — a
    /// brand-new field therefore starts on Rust and already shows something, and the MiniRadio index on the
    /// card maps straight onto this enum with no lookup table.
    public enum ChunkEdgeTintPreset
    {
        Rust = 0,
        Blood = 1,
        Charred = 2,
        Custom = 3,
    }

    /// The small fixed library of edge-tint ramps, so "scorch the edges rusty" is one click rather than a
    /// hand-authored gradient every time — with Custom still there for anything the three presets don't cover.
    ///
    /// **What the gradient's own axis means, and it is NOT time.** t = 0 is a cut taken from the dead centre of
    /// the hit, t = 1 a cut taken from the outer rim of the hit radius (from the pivot / the sprite's half
    /// diagonal when no hit radius is set). So a ramp reads centre → rim: the hot, freshly-burned core on the
    /// left, the barely-touched outskirts on the right. The gradient's ALPHA rides along as a per-t strength
    /// multiplier on top of the card's own Strength dial, which is what lets a preset fade itself out toward the
    /// rim instead of stamping the same colour across the whole radius.
    ///
    /// Each preset is built once and handed out as a SHARED instance — callers only ever Evaluate() it, never
    /// mutate it. <see cref="Clone"/> is there for the one case that does need its own copy: the editor card
    /// seeding a Custom gradient from whichever preset was showing, so "start from Charred and tweak it" does
    /// not silently edit the preset for every other recipe in the project.
    public static class ChunkEdgeTints
    {
        /// Card labels, in enum order — the MiniRadio's options array.
        public static readonly string[] Names = { "Rust", "Blood", "Charred", "Custom" };

        static Gradient _rust, _blood, _charred;

        /// The ramp for a preset. <see cref="ChunkEdgeTintPreset.Custom"/> has no built-in ramp of its own and
        /// falls back to Rust here — use <see cref="Resolve"/> instead, which is the one that knows about the
        /// author's custom gradient.
        public static Gradient Of(ChunkEdgeTintPreset preset)
        {
            switch (preset)
            {
                case ChunkEdgeTintPreset.Blood:
                    return _blood ??= Build(
                        new Color(0.58f, 0.04f, 0.06f), new Color(0.40f, 0.02f, 0.04f), new Color(0.24f, 0.02f, 0.03f),
                        1f, 0.65f, 0.25f);
                case ChunkEdgeTintPreset.Charred:
                    return _charred ??= Build(
                        new Color(0.07f, 0.06f, 0.06f), new Color(0.18f, 0.16f, 0.15f), new Color(0.34f, 0.32f, 0.30f),
                        1f, 0.60f, 0.20f);
                default:
                    return _rust ??= Build(
                        new Color(0.34f, 0.15f, 0.05f), new Color(0.72f, 0.36f, 0.12f), new Color(0.50f, 0.33f, 0.18f),
                        1f, 0.70f, 0.25f);
            }
        }

        /// The ramp actually used: the author's own gradient on Custom (falling through to Rust when that is
        /// still empty, so picking Custom before authoring anything degrades to a visible tint rather than to a
        /// silent no-op), else the named preset.
        public static Gradient Resolve(ChunkEdgeTintPreset preset, Gradient custom)
            => preset == ChunkEdgeTintPreset.Custom && custom != null ? custom : Of(preset);

        /// A detached copy, for seeding an author's Custom gradient from a preset without aliasing the shared
        /// instance (same "a picked preset hands over a fresh Gradient" contract as ZuiGradientPresetPopup).
        public static Gradient Clone(Gradient g)
        {
            var clone = new Gradient();
            if (g != null) { clone.SetKeys(g.colorKeys, g.alphaKeys); clone.mode = g.mode; }
            return clone;
        }

        static Gradient Build(Color a, Color b, Color c, float alphaA, float alphaB, float alphaC)
        {
            var g = new Gradient();
            g.SetKeys(
                new[]
                {
                    new GradientColorKey(a, 0f),
                    new GradientColorKey(b, 0.5f),
                    new GradientColorKey(c, 1f),
                },
                new[]
                {
                    new GradientAlphaKey(alphaA, 0f),
                    new GradientAlphaKey(alphaB, 0.5f),
                    new GradientAlphaKey(alphaC, 1f),
                });
            return g;
        }
    }
}
