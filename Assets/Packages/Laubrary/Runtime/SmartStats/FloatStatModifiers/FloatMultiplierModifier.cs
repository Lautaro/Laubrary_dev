using System;
using System.Collections.Generic;
using UnityEngine;

namespace Lautaro.Stats
{
    /// <summary>
    /// Multiplies the stat by a percentage bonus.
    /// 
    /// Value Interpretation:
    /// - 0.5 = +50% bonus
    /// - 1.0 = +100% bonus (doubles the value)
    /// - -0.25 = -25% penalty
    /// 
    /// Merging Multipliers:
    /// Use MergeModifier() to combine multiple multipliers additively:
    /// - multiplierA (50%) + multiplierB (25%) = multiplierA (75%)
    /// - multiplierB is marked as merged and won't apply separately
    /// 
    /// This allows implementing industry-standard stacking within the sequential calculation system.
    /// </summary>
    [Serializable]
    public class FloatMultiplierModifier : FloatStatModifier
    {
        /// <summary>
        /// List of multipliers that have been merged into this one.
        /// </summary>
        [SerializeReference] public List<FloatMultiplierModifier> mergedMultipliers = new List<FloatMultiplierModifier>();

        /// <summary>
        /// If true, this multiplier has been merged into another and should not apply independently.
        /// </summary>
        [SerializeField] public bool isMergedIntoAnother = false;

        /// <summary>
        /// Gets whether this modifier has been merged into another multiplier.
        /// </summary>
        public bool IsMergedIntoAnother => isMergedIntoAnother;

        /// <summary>
        /// Creates a multiplier modifier that scales the stat by a percentage.
        /// </summary>
        /// <param name="multiplier">Percentage bonus as decimal (0.5 = 50% increase, -0.25 = 25% reduction).</param>
        /// <param name="description">Human-readable description of this modifier.</param>
        public FloatMultiplierModifier(float multiplier, string description = "") 
            : base(multiplier, description)
        {
        }

        /// <summary>
        /// Merges another multiplier into this one, combining their percentages additively.
        /// The other multiplier is marked as merged and will be skipped during calculation.
        /// </summary>
        /// <param name="other">The multiplier to merge into this one.</param>
        public void MergeModifier(FloatMultiplierModifier other)
        {
            if (other == null || other == this)
                return;

            modValue += other.modValue;
            other.isMergedIntoAnother = true;
            
            if (!mergedMultipliers.Contains(other))
            {
                mergedMultipliers.Add(other);
            }
        }

        /// <summary>
        /// Unmerges a previously merged multiplier, restoring it to an independent state.
        /// </summary>
        /// <param name="other">The multiplier to unmerge.</param>
        public void UnmergeModifier(FloatMultiplierModifier other)
        {
            if (other == null || !mergedMultipliers.Contains(other))
                return;

            modValue -= other.modValue;
            other.isMergedIntoAnother = false;
            mergedMultipliers.Remove(other);
        }

        /// <summary>
        /// Applies this multiplier to the current value.
        /// Formula: currentValue * (1 + modValue)
        /// </summary>
        public override float ApplyTo(float currentValue)
        {
            return currentValue * (1f + ModValue());
        }
    }
}
