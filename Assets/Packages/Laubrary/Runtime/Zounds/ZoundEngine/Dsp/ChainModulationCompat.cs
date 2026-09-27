using UnityEngine;

namespace Laubrary.Zounds.Dsp {

    /// <summary>
    /// Reads a modulator binding saved before modulation moved onto the control's own travel, and says what it means now.
    ///
    /// **Why a conversion rather than simply reinterpreting the numbers.** The old depth was a raw amount in the target
    /// parameter's own units; the new one is a fraction of the parameter's control. Those two readings of the same stored
    /// number are wrong in opposite and equally destructive directions. A filter sweep authored with a depth of three
    /// thousand hertz, read as a fraction, would peg the cutoff at its maximum and stay there. A resonance wobble authored
    /// with a depth of a fifth, read as raw units, would move it by a fifth of its whole control instead — which is much
    /// too much. Either way the sound changes and nothing announces it. So old bindings are marked as old and translated.
    ///
    /// **One of the three old ways of combining cannot be translated faithfully, and that is the point.** Multiplying a
    /// parameter by an oscillator was broken: an oscillator swings symmetrically about zero, so the product went negative
    /// for half of every cycle and was clamped to the parameter's minimum, where it sat. Measured on a cutoff set to
    /// 2000 Hz, it produced a value ranging from 1999.6 Hz down to exactly the 20 Hz floor — half the cycle pinned at the
    /// bottom. There is no faithful translation of that, because reproducing it would mean reproducing the fault. It
    /// becomes a shift around the authored value, which is what attaching an oscillator was always meant to do.
    /// </summary>
    public static class ChainModulationCompat {

        /// <summary>Bindings saved with this schema number or higher already speak in fractions of the control.</summary>
        public const int CURRENT_SCHEMA = 1;

        public static ModulationCombine CombineOf(ZoundModifierBinding b) {
            if (b.schema >= CURRENT_SCHEMA) return b.combine;
            switch (b.op) {
                // Replacing the value outright is the one old operation with a direct successor.
                case ModifierOp.Replace: return ModulationCombine.Set;
                // Both adding and multiplying were reaching for "move this parameter about while it plays", and both did it
                // in units that made the amount unguessable. They land on the same operation, which does it properly.
                default: return ModulationCombine.Shift;
            }
        }

        /// <summary>
        /// The old depth expressed as a fraction of the target's control.
        ///
        /// For an addition this is an exact translation on an evenly spaced control — an amount divided by the span it was
        /// an amount of. On a control spaced by ratio it is an approximation, because the old amount did not mean the same
        /// thing at the two ends of the range; the fraction chosen is what that amount was worth around the middle, which
        /// is the closest a single number can get to something that was never consistent.
        /// </summary>
        public static float DepthOf(ZoundModifierBinding b, float min, float max, bool ratioSpaced) {
            if (b.schema >= CURRENT_SCHEMA) return Mathf.Clamp(b.depth, -1f, 1f);

            float d = Mathf.Abs(b.depth);
            switch (b.op) {
                case ModifierOp.Multiply:
                    // The old multiply had no depth in the parameter's units at all — the depth scaled the modulator, and a
                    // depth of one was the normal setting. Carry the intensity across and let the shift be a strong one,
                    // since that is what a multiply by a full-swing oscillator was trying to be.
                    return Mathf.Clamp01(d) * 0.5f;
                case ModifierOp.Replace:
                    // Taking the control over completely, which is a full blend.
                    return 1f;
                default: {
                    if (ratioSpaced) {
                        // What that amount was worth as a fraction, taken around the middle of the range.
                        float mid = Mathf.Sqrt(Mathf.Max(min, 1e-4f) * Mathf.Max(max, 1e-4f));
                        float pos = ModulationMath.ToPosition(mid + d, min, max, true)
                                  - ModulationMath.ToPosition(mid, min, max, true);
                        return Mathf.Clamp01(Mathf.Abs(pos));
                    }
                    float span = max - min;
                    return span > 1e-9f ? Mathf.Clamp01(d / span) : 0f;
                }
            }
        }

        /// <summary>Rewrites a binding into the current form, so the conversion stops happening on every load.</summary>
        public static void MakePermanent(ZoundModifierBinding b, float min, float max, bool ratioSpaced) {
            if (b == null || b.schema >= CURRENT_SCHEMA) return;
            var combine = CombineOf(b);
            float depth = DepthOf(b, min, max, ratioSpaced);
            b.combine = combine;
            b.depth = depth;
            b.schema = CURRENT_SCHEMA;
        }
    }
}
