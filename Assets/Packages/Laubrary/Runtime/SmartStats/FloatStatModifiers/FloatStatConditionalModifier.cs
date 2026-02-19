using System;
using Lautaro.Stats.Engine;

namespace Lautaro.Stats
{
    /// <summary>
    /// An addition modifier that is only active when a condition is met.
    /// Evaluates the condition every frame and pauses/resumes accordingly.
    /// 
    /// Features:
    /// - Active only while condition returns true
    /// - Optional auto-removal when condition becomes false
    /// - Useful for situational bonuses (e.g., "while health > 50%", "while grounded")
    /// 
    /// Example:
    /// // +10 speed while sprinting
    /// var sprintBonus = new FloatStatConditionalModifier(
    ///     10f, 
    ///     () => player.isSprinting, 
    ///     "Sprint Speed"
    /// );
    /// player.speed.AddModifier(sprintBonus);
    /// 
    /// // +50 armor while shield active, remove when shield breaks
    /// var shieldArmor = new FloatStatConditionalModifier(
    ///     50f,
    ///     () => player.hasShield,
    ///     "Shield Armor",
    ///     shouldSwitch: false  // Remove instead of pausing when condition fails
    /// );
    /// </summary>
    [Serializable]
    public class FloatStatConditionalModifier : FloatAdditionModifier, IUpdatable
    {
        private Func<bool> condition;

        /// <summary>
        /// If true, modifier pauses/resumes based on condition.
        /// If false, modifier is removed permanently when condition becomes false.
        /// </summary>
        public readonly bool shouldSwitch = true;

        public override float ModValue()
        {
            if (!isPaused)
                return modValue;
            else if (shouldSwitch == false)
                Remove();

            return 0;
        }

        /// <summary>
        /// Creates a conditional addition modifier.
        /// </summary>
        /// <param name="value">The value to add when condition is true.</param>
        /// <param name="keepActiveCondition">Function that returns true when modifier should be active.</param>
        /// <param name="description">Human-readable description.</param>
        /// <param name="shouldSwitch">If true, pauses when false. If false, removes permanently when false.</param>
        public FloatStatConditionalModifier(float value, Func<bool> keepActiveCondition, string description = "", bool shouldSwitch = true) 
            : base(value, description)
        {
            UpdateEngine.Register(this);
            condition = keepActiveCondition;
            this.shouldSwitch = shouldSwitch;
        }

        public override string ToString()
        {
            return $"[{ModValue().ToString()}] :{description}";
        }

        public void Update()
        {
            isPaused = !condition();
        }
    }
}
