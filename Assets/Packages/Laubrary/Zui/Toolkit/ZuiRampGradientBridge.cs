// ZuiRampGradientBridge — the conversion between an IZuiRamp (Pyre's PyreRamp, or any future ramp type) and a
// ZuiGradient, so a ramp and a gradient exchange stops through the SAME project library (ZuiGradientPresetLibrary)
// without either one being rewritten to store the other's shape.
//
// LOSSLESS BOTH WAYS since T-0221. It used to convert through a UnityEngine.Gradient, which caps at 8 colour keys —
// so a ramp with more stops (PyreRampPresets.Ember() ships 10) was evenly subsampled on the way into the library
// and could never come back whole. ZuiGradient now owns its own unbounded stop list and its own interpolation
// space, which is exactly the shape a PyreRamp has, so the conversion is now stop-for-stop with the blend mode
// carried across as its index: nothing is dropped in either direction, at any stop count.
//
// The blend-mode index is passed straight through because both enums declare the same two members in the same
// order (LinearLight = 0, Srgb = 1 — see ZuiGradientSpace's own comment saying so); the name lists are compared
// first so a future ramp type with a different mode set is left at its own default rather than silently re-blended.
//
// Neither direction runs on deserialize — only when a caller explicitly invokes "Load from library" / "Save to
// library". Existing PyreRamp and ZuiGradient assets are therefore untouched by this file unless a human picks a
// saved gradient for that specific field.
using UnityEngine;

namespace Laubrary.Zui
{
    public static class ZuiRampGradientBridge
    {
        /// UnityEngine.Gradient's own key cap, kept for the legacy Gradient-shaped overloads below.
        public const int MaxGradientKeys = ZuiGradient.MaxGradientKeys;

        /// <summary>The ramp's CURRENT stops as a fresh ZuiGradient the caller owns outright — every stop, its
        /// colour and its blend space. Null/empty ramp in, null out (an empty ramp — Pyre's legal "no colour here"
        /// state — has nothing to save).</summary>
        public static ZuiGradient ToZuiGradient(IZuiRamp ramp)
        {
            if (ramp == null || ramp.Count == 0) return null;

            var zg = new ZuiGradient();
            var stops = zg.Stops;
            stops.Clear();
            for (int i = 0; i < ramp.Count; i++)
                stops.Add(new ZuiGradientStop(ramp.GetPos(i), ramp.GetColor(i)));
            stops.Sort((a, b) => a.pos.CompareTo(b.pos));
            zg.MarkStopsChanged();
            if (SameModes(ramp)) zg.BlendMode = ramp.BlendMode;
            return zg;
        }

        /// <summary>Replaces ALL of `ramp`'s stops with `zg`'s — every stop, no cap, no subsampling — and carries
        /// the blend space across when both types name the same modes. Caller owns the Undo gesture
        /// (OnBeforeMutate before calling this, OnChanged after), so this composes under ZuiRampControl's existing
        /// Mutate() wrapper.</summary>
        public static void ApplyZuiGradient(IZuiRamp ramp, ZuiGradient zg)
        {
            if (ramp == null || zg == null) return;
            var stops = zg.Stops;
            if (stops.Count == 0) return;

            while (ramp.Count > 0) ramp.RemoveAt(ramp.Count - 1);
            foreach (var s in stops) ramp.Insert(s.pos, s.color);
            if (SameModes(ramp)) ramp.BlendMode = zg.BlendMode;
        }

        /// True when the ramp's blend-mode labels match ZuiGradient's own, so the index means the same thing on
        /// both sides. A ramp type with a different mode set keeps whatever mode it already had.
        static bool SameModes(IZuiRamp ramp)
        {
            var mine = new ZuiGradient().BlendModeNames;
            var theirs = ramp.BlendModeNames;
            if (theirs == null || mine == null || theirs.Length != mine.Length) return false;
            for (int i = 0; i < mine.Length; i++)
                if (!string.Equals(mine[i], theirs[i], System.StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }

        // ── legacy UnityEngine.Gradient shapes ───────────────────────────────────────────────────────────
        // Kept for callers that genuinely deal in a Gradient (an import from outside, an old saved library
        // entry). Both go through ZuiGradient, so the 8-key cap now only ever applies where a real Gradient is
        // demanded — never between a ramp and the library.

        /// <summary>The ramp's stops as a Gradient — exact up to 8 stops, evenly subsampled beyond (endpoints
        /// kept). Use <see cref="ToZuiGradient"/> instead unless a UnityEngine.Gradient is genuinely required.</summary>
        public static Gradient ToGradient(IZuiRamp ramp) => ToZuiGradient(ramp)?.gradient;

        /// <summary>Replaces ALL of `ramp`'s stops with `g`'s colour keys (alpha keys are folded into each stop's
        /// own colour at the union of both key sets, matching IZuiRamp's "alpha is the opacity there, not a
        /// separate key" contract). Always exact: a Gradient has at most 8 keys, IZuiRamp has no upper bound.</summary>
        public static void ApplyGradient(IZuiRamp ramp, Gradient g)
        {
            if (ramp == null || g == null) return;
            var zg = new ZuiGradient();
            zg.SetGradient(g);
            ApplyZuiGradient(ramp, zg);
        }
    }
}
