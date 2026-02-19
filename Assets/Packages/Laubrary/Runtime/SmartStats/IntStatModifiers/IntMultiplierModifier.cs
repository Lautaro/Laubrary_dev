using System;
using System.Collections.Generic;
using UnityEngine;

namespace Lautaro.Stats
{
    /// <summary>
    /// Multiplies the stat by a percentage value.
    /// modValue represents the multiplier as a decimal (0.5 = +50%, 1.0 = +100%)
    /// Result is rounded to nearest integer.
    /// 
    /// Supports merging with other multipliers to combine their percentages additively.
    /// </summary>
    [Serializable]
    public class IntMultiplierModifier : IntStatModifier
    {
        /// <summary>
        /// Multipliers that have been merged into this one.
        /// Their percentages are combined additively.
        /// </summary>
        public List<IntMultiplierModifier> mergedMultipliers = new List<IntMultiplierModifier>();

        /// <summary>
        /// True if this multiplier has been merged into another and should be skipped during calculation.
        /// </summary>
        public bool IsMergedIntoAnother = false;

        public IntMultiplierModifier(float multiplier, string description = "") 
            : base(Mathf.RoundToInt(multiplier * 100), description)
        {
        }

        public override int ApplyTo(int currentValue)
        {
            float totalMultiplier = modValue / 100f;

            foreach (var merged in mergedMultipliers)
            {
                if (!merged.IsMergedIntoAnother)
                {
                    totalMultiplier += merged.ModValue() / 100f;
                }
            }

            return Mathf.RoundToInt(currentValue * (1f + totalMultiplier));
        }

        /// <summary>
        /// Merges another multiplier into this one, combining their percentages additively.
        /// The merged multiplier is marked to be skipped during calculation.
        /// </summary>
        public void MergeModifier(IntMultiplierModifier other)
        {
            if (other != this && !other.IsMergedIntoAnother)
            {
                mergedMultipliers.Add(other);
                other.IsMergedIntoAnother = true;
            }
        }
    }
}
