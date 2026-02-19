using System;

namespace Lautaro.Stats
{
    /// <summary>
    /// An addition modifier whose value switches between modValue and 0 based on a condition.
    /// Unlike ConditionalModifier, this doesn't pause - it actively returns 0 when condition is false.
    /// Does NOT auto-update - condition is checked only when ModValue() is called.
    /// 
    /// Use ConditionalModifier if you want pause/resume behavior.
    /// Use SwitchModifier if you want the modifier to always exist but conditionally return 0.
    /// 
    /// Example:
    /// // +20 damage when combo active (checked on-demand, not every frame)
    /// var comboBonus = new FloatStatSwitchModifier(
    ///     20f,
    ///     () => player.comboCount > 5,
    ///     "Combo Damage"
    /// );
    /// </summary>
    [Serializable]
    public class FloatStatSwitchModifier : FloatAdditionModifier
    {
        private Func<bool> condition;

        public override float ModValue()
        {
            if (condition())
                return modValue;
            else
                return 0;
        }

        /// <summary>
        /// Creates a switch modifier that conditionally returns its value or 0.
        /// </summary>
        /// <param name="value">The value to return when condition is true.</param>
        /// <param name="keepActiveCondition">Function that returns true when modifier should return its value.</param>
        /// <param name="description">Human-readable description.</param>
        public FloatStatSwitchModifier(float value, Func<bool> keepActiveCondition, string description = "") 
            : base(value, description)
        {
            condition = keepActiveCondition;
        }

        public override string ToString()
        {
            return $"[{ModValue().ToString()}] :{description}";
        }
    }
}
