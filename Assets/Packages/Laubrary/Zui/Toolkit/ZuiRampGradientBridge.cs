// ZuiRampGradientBridge — T-0205. The lossless-both-ways conversion between an IZuiRamp (Pyre's PyreRamp,
// or any future ramp type) and a plain UnityEngine.Gradient, so ZuiRampControl can reach the SAME project
// gradient library Z.Gradient uses (ZuiGradientPresetLibrary) without PyreRamp itself ever being rewritten
// to store its stops as a Gradient.
//
// Why not just make every ramp a ZuiGradient (the owner's "consider if ramp can't just be replaced"
// question)? A UnityEngine.Gradient hard-caps at 8 colour keys — PyreRampPresets.Ember() alone ships 10 —
// and PyreShade evaluates a PyreRamp through PyreRampSpace (LinearLight/Srgb blending), which a plain
// Gradient cannot express. Flattening PyreRamp into a Gradient would silently truncate or re-blend an
// existing >8-stop asset on load: not lossless, and exactly the failure ZuiRampControl was built to avoid
// (see ZuiRampControl.cs's own file header and IZuiRamp.cs). So PyreRamp keeps its own unbounded stop list
// and its own control; what moves is the LIBRARY — a ramp can now pull a saved gradient IN (always exact:
// a Gradient has at most 8 keys, and IZuiRamp has no upper bound, so Gradient -> ramp never loses a stop)
// and push its own stops OUT to be saved (exact up to 8 stops; beyond that, evenly subsampled — stated, not
// hidden, the same approximation PyreShaperRampPresets.ToZuiGradient already ships and the project already
// accepted for the Pyre-preset-into-Shaper path).
//
// ToRamp is the ONLY half of this that mutates an existing ramp, and only when a caller explicitly invokes
// "Load from library" — never on deserialize, never automatically. Existing PyreRamp assets are therefore
// untouched by this file unless a human picks a saved gradient for that specific ramp.
using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Zui
{
    public static class ZuiRampGradientBridge
    {
        /// UnityEngine.Gradient's own key cap — a ramp beyond this many stops is evenly subsampled when saved
        /// to the shared library, never silently truncated from one end.
        public const int MaxGradientKeys = 8;

        /// <summary>Converts a ramp's CURRENT stops into a fresh Gradient the caller owns outright. Null/empty
        /// ramp in, null out (an empty ramp — Pyre's legal "no colour here" state — has nothing to save).</summary>
        public static Gradient ToGradient(IZuiRamp ramp)
        {
            if (ramp == null || ramp.Count == 0) return null;

            int n = ramp.Count;
            var positions = new List<float>(n);
            var colors = new List<Color>(n);
            for (int i = 0; i < n; i++) { positions.Add(ramp.GetPos(i)); colors.Add(ramp.GetColor(i)); }

            if (n > MaxGradientKeys)
            {
                var idx = new List<int>(MaxGradientKeys);
                for (int i = 0; i < MaxGradientKeys; i++)
                    idx.Add(Mathf.RoundToInt(i * (n - 1) / (float)(MaxGradientKeys - 1)));
                var p2 = new List<float>(MaxGradientKeys); var c2 = new List<Color>(MaxGradientKeys);
                foreach (var i in idx) { p2.Add(positions[i]); c2.Add(colors[i]); }
                positions = p2; colors = c2;
                n = MaxGradientKeys;
            }

            var ck = new GradientColorKey[n];
            var ak = new GradientAlphaKey[n];
            for (int i = 0; i < n; i++)
            {
                ck[i] = new GradientColorKey(colors[i], positions[i]);
                ak[i] = new GradientAlphaKey(colors[i].a, positions[i]);
            }
            var g = new Gradient();
            g.SetKeys(ck, ak);
            return g;
        }

        /// <summary>Replaces ALL of `ramp`'s stops with `g`'s colour keys (alpha keys are folded into each
        /// stop's own colour, matching IZuiRamp's "alpha is the opacity there, not a separate key" contract —
        /// sampling the gradient at each colour key's own position, not interleaving colour/alpha key lists
        /// positionally, so an authored alpha key that sits at a DIFFERENT position than any colour key still
        /// contributes correctly). Always exact: a Gradient has at most 8 keys, IZuiRamp has no upper bound.
        /// Caller owns the Undo gesture (OnBeforeMutate before calling this, OnChanged after) — this method
        /// does not open one itself, so it composes under ZuiRampControl's existing Mutate() wrapper.</summary>
        public static void ApplyGradient(IZuiRamp ramp, Gradient g)
        {
            if (ramp == null || g == null) return;
            var keys = g.colorKeys;
            if (keys == null || keys.Length == 0) return;

            while (ramp.Count > 0) ramp.RemoveAt(ramp.Count - 1);

            foreach (var k in keys)
            {
                var c = g.Evaluate(k.time);   // folds the alpha curve in at this exact position
                ramp.Insert(k.time, c);
            }
        }
    }
}
