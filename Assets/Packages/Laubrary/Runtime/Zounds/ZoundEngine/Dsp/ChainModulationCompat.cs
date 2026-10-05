using Laubrary.Audio;
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
        public const int CURRENT_SCHEMA = 2;

        /// <summary>The schema at which depth became a fraction of the control (before that it was in the parameter's
        /// own units). Schema 2 made Shift's depth a share of the room available rather than of the whole control.</summary>
        public const int FRACTION_SCHEMA = 1;

        /// <summary>
        /// Whether an envelope binding can be converted to <see cref="ModulationCombine.Ratio"/> without changing how it
        /// sounds: an envelope, bound with Set, to a parameter spaced by ratio, and the envelope's only binding (converting
        /// the curve would otherwise change what its other bindings hear).
        /// </summary>
        public static bool CanConvertSetToRatio(ZoundEffectChain chain, ZoundModifierBinding b) {
            if (chain?.modifiers == null || b == null || b.modifierIndex < 0 || b.modifierIndex >= chain.modifiers.Count) return false;
            var m = chain.modifiers[b.modifierIndex];
            if (m.type != ZoundModifierType.Envelope || m.curve == null) return false;
            if (CombineOf(b) != ModulationCombine.Set) return false;
            if (!TryParam(chain, b, out var pd, out _) || !ModulationMath.IsRatioSpaced(pd.curve)) return false;
            int n = 0;
            foreach (var other in chain.bindings) if (other.modifierIndex == b.modifierIndex) n++;
            return n == 1;
        }

        /// <summary>
        /// Rewrites an envelope bound with Set onto the Ratio scale so it plays the SAME multiplier as before (T-0479):
        /// each point's value is replaced by the Ratio position that gives the value the old Set produced over the
        /// parameter's set value, the curve's range becomes 0..1 and the binding Ratio at full depth. Exact for every point
        /// within two octaves of the set value; a point beyond that is held at x4 or x1/4 (returned in
        /// <paramref name="clampedPoints"/>). Points sharing a segment stay on the same shape, since both scales are evenly
        /// spaced in ratio. The caller records Undo first.
        /// </summary>
        public static bool ConvertSetToRatio(ZoundEffectChain chain, ZoundModifierBinding b, out int clampedPoints) {
            clampedPoints = 0;
            if (!CanConvertSetToRatio(chain, b)) return false;
            TryParam(chain, b, out var pd, out float setValue);
            var curve = chain.modifiers[b.modifierIndex].curve;
            float depth = DepthOf(b, pd.min, pd.max, true);
            float baseValue = Mathf.Clamp(setValue, pd.min, pd.max);
            var pts = curve.GetPointsList();
            for (int i = 0; i < pts.Count; i++) {
                float heard = ModulationMath.Apply(ModulationCombine.SetFromZero, baseValue, pts[i].value, depth, pd.min, pd.max, true);
                float ratio = heard / Mathf.Max(baseValue, 1e-6f);
                float reach = ModulationMath.RatioFromPosition(1f);
                if (ratio > reach * 1.0001f || ratio < 1f / reach * 0.9999f) clampedPoints++;
                pts[i].value = ModulationMath.PositionFromRatio(ratio);
            }
            curve.yMin = 0f; curve.yMax = 1f;
            b.combine = ModulationCombine.Ratio;
            b.depth = 1f;
            b.schema = CURRENT_SCHEMA;
            chain.Touch();
            return true;
        }

        /// <summary>The parameter a binding drives and the value it is set to (a source-stage parameter rests at its
        /// default, as the render starts it).</summary>
        public static bool TryParam(ZoundEffectChain chain, ZoundModifierBinding b, out ParamDesc pd, out float setValue) {
            pd = default; setValue = 0f;
            if (b.nodeIndex < 0) {
                if (b.paramIndex < 0 || b.paramIndex >= SourceStageParam.Count) return false;
                pd = ZoundEffectDescriptors.SourceStageParams[b.paramIndex];
                setValue = pd.def;
                return true;
            }
            if (chain?.nodes == null || b.nodeIndex >= chain.nodes.Count) return false;
            var node = chain.nodes[b.nodeIndex];
            var d = ZoundEffectDescriptors.Get(node.type);
            if (b.paramIndex < 0 || b.paramIndex >= d.parameters.Length) return false;
            pd = d.parameters[b.paramIndex];
            setValue = node.p != null && b.paramIndex < node.p.Length ? node.p[b.paramIndex] : pd.def;
            return true;
        }

        /// <summary>
        /// How a binding combines, given what kind of modifier drives it: <see cref="CombineOf"/>, except that Set on a
        /// modifier whose output runs 0..1 (an envelope) becomes <see cref="ModulationCombine.SetFromZero"/>, so the
        /// envelope spans the parameter's whole range instead of only its top half. Every place that hands a binding to the
        /// engine goes through this.
        /// </summary>
        public static ModulationCombine EffectiveCombine(ZoundEffectChain chain, ZoundModifierBinding b) {
            var c = CombineOf(b);
            if (chain?.modifiers == null || b.modifierIndex < 0 || b.modifierIndex >= chain.modifiers.Count) return c;
            var type = chain.modifiers[b.modifierIndex].type;
            if (c == ModulationCombine.Set && (type == ZoundModifierType.Envelope || type == ZoundModifierType.Code))
                return ModulationCombine.SetFromZero;
            // A Code modifier's middle means "unchanged", so its Shift swings around one half rather than pushing only up.
            if (c == ModulationCombine.Shift && type == ZoundModifierType.Code)
                return ModulationCombine.ShiftFromCentre;
            return c;
        }

        /// <summary>
        /// The combine a new binding from a Code modifier starts with, chosen so the number game code sends means the obvious
        /// thing for that kind of control (T-0495): pitch and speed (ratio-spaced multipliers) get Ratio, so one half is
        /// unchanged, one is two octaves up and nought two down; a level (a linear multiplier such as a gain) gets Scale, so
        /// nought is silent and one as authored; anything else gets Set, so nought is the bottom of the control and one the top.
        /// </summary>
        public static ModulationCombine DefaultCombineForCode(ParamDesc pd) {
            if (pd.defaultOp == ModifierOp.Multiply && pd.curve == ParamCurve.Logarithmic) return ModulationCombine.Ratio;
            if (pd.defaultOp == ModifierOp.Multiply && pd.curve == ParamCurve.Linear && pd.min >= 0f) return ModulationCombine.Scale;
            return ModulationCombine.Set;
        }

        public static ModulationCombine CombineOf(ZoundModifierBinding b) {
            if (b.schema >= CURRENT_SCHEMA) return b.combine;
            // Saved as a fraction of the whole control: its Shift keeps that meaning until it is edited.
            if (b.schema >= FRACTION_SCHEMA) return b.combine == ModulationCombine.Shift ? ModulationCombine.ShiftWholeRange : b.combine;
            switch (b.op) {
                // Replacing the value outright is the one old operation with a direct successor.
                case ModifierOp.Replace: return ModulationCombine.Set;
                // Both adding and multiplying were reaching for "move this parameter about while it plays", and both did it
                // in units that made the amount unguessable. They land on the same operation, which does it properly.
                default: return ModulationCombine.ShiftWholeRange;
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
            if (b.schema >= FRACTION_SCHEMA) return Mathf.Clamp(b.depth, -1f, 1f);

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
