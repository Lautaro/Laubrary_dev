using System;
using UnityEngine;

namespace Lautaro.Stats
{
    /// <summary>
    /// Divides the stat value by a divisor. Internally converted to a multiplier for consistent stacking.
    /// 
    /// Value Interpretation:
    /// - 2.0 = divide by 2 (halves the value, equivalent to *0.5)
    /// - 4.0 = divide by 4 (quarters the value, equivalent to *0.25)
    /// - 0.5 = divide by 0.5 (doubles the value, equivalent to *2.0)
    /// 
    /// Safety:
    /// - Division by zero is prevented with a warning and the modifier is ignored
    /// 
    /// Implementation Note:
    /// Within a priority category, division is converted to multiplication for consistent stacking:
    /// - Dividing by 2 becomes multiplying by 0.5
    /// - This is then added to the multiplier bonus pool: -0.5 (since 0.5 - 1.0 = -0.5)
    /// 
    /// Example:
    /// Input: 100
    /// Modifier: FloatDividerModifier(2.0, "Slow") in priority with +50% multiplier
    /// Calculation:
    /// - /2 converts to *0.5 → contributes -0.5 to multiplier sum
    /// - Total multipliers: +0.5 (from other mod) + (-0.5) = 0.0
    /// - Result: 100 * (1 + 0) = 100
    /// </summary>
    [Serializable]
    public class FloatDividerModifier : FloatStatModifier
    {
        /// <summary>
        /// Creates a divider modifier that divides the stat by the specified value.
        /// </summary>
        /// <param name="divisor">The value to divide by. Must not be zero.</param>
        /// <param name="description">Human-readable description of this modifier.</param>
        public FloatDividerModifier(float divisor, string description = "") 
            : base(divisor, description)
        {
            if (Mathf.Approximately(divisor, 0f))
            {
                Debug.LogWarning($"FloatDividerModifier '{description}' created with divisor 0. This will be ignored in calculations.");
            }
        }

        /// <summary>
        /// NOTE: This is NOT used in the default calculation flow.
        /// Dividers are converted to multipliers and grouped with other multipliers for industry-standard stacking.
        /// This method exists for custom calculations or debugging.
        /// </summary>
        public override float ApplyTo(float currentValue)
        {
            float divisor = ModValue();
            if (Mathf.Approximately(divisor, 0f))
            {
                Debug.LogWarning($"FloatDividerModifier '{description}' attempted division by zero. Ignoring modifier.");
                return currentValue;
            }
            return currentValue / divisor;
        }
    }
}
