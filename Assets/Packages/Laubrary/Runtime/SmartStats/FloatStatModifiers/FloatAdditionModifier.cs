using System;

namespace Lautaro.Stats
{
    /// <summary>
    /// Adds a flat value to the stat. This is the most common modifier type.
    /// 
    /// Within a priority category, all addition modifiers sum together before being applied.
    /// 
    /// Example:
    /// - FloatAdditionModifier(10, "Strength Bonus")
    /// - FloatAdditionModifier(-5, "Weakness Debuff")
    /// If both are in the same priority: total addition = +5
    /// </summary>
    [Serializable]
    public class FloatAdditionModifier : FloatStatModifier
    {
        /// <summary>
        /// Creates an addition modifier that adds a flat value to the stat.
        /// </summary>
        /// <param name="value">The amount to add (can be negative for debuffs).</param>
        /// <param name="description">Human-readable description of this modifier.</param>
        public FloatAdditionModifier(float value, string description = "") 
            : base(value, description)
        {
        }

        /// <summary>
        /// Applies this modifier by adding its value to the current stat value.
        /// </summary>
        public override float ApplyTo(float currentValue)
        {
            return currentValue + ModValue();
        }
    }
}
