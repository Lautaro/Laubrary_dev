using System;
using UnityEngine;
using Lautaro.Stats.Engine;
using Laubrary.LaubraryTicker;

namespace Lautaro.Stats
{
    /// <summary>
    /// Base class for all FloatStat modifiers. Modifiers temporarily adjust stat values without changing the base.
    /// Supports priority-based ordering where modifiers are grouped by priority and resolved using industry-standard
    /// math within each priority category.
    ///
    /// Priority System:
    /// - Lower priority numbers apply first (e.g., -10 before 0 before 10)
    /// - Default priority is 0 if not specified
    /// - Within each priority category, modifiers use industry-standard resolution:
    ///   * All additions sum together
    ///   * All multipliers stack additively (50% + 25% = 75% total)
    ///   * Result = (input + sumOfAdditions) * (1 + sumOfMultipliers)
    /// - Categories apply sequentially (output of priority N becomes input of priority N+1)
    ///
    /// Example:
    /// Base: 100
    /// Priority 0: +20, *0.5 → (100 + 20) * 1.5 = 180
    /// Priority 10: +30 → 180 + 30 = 210
    /// </summary>
    [Serializable]
    public class FloatStatModifier : StatModifierBase, ITickable
    {
        /// <summary>
        /// The numeric value of this modifier. Interpretation depends on modifier type.
        /// Addition: added to stat. Multiplication: percentage bonus (0.5 = +50%). Division: divisor.
        /// </summary>
        public float modValue;

        /// <summary>
        /// Priority category for this modifier. Lower values apply first.
        /// Null means use default priority (0).
        /// </summary>
        [SerializeField] public int? priority = null;

        /// <summary>
        /// Optional timer for this modifier. If set, the modifier will expire or scale based on the timer mode.
        /// </summary>
        public Ticker.Timer timer;

        /// <summary>
        /// Defines how the timer affects this modifier's value.
        /// </summary>
        public TimerMode timerMode = TimerMode.None;

        /// <summary>
        /// Returns the current effective value of this modifier.
        /// Can be overridden for dynamic modifiers (e.g., time-based, conditional).
        /// Automatically applies timer scaling based on timerMode.
        /// </summary>
        public virtual float ModValue()
        {
            if (timerMode == TimerMode.None || timer == null)
                return modValue;

            if (timer.IsExpired)
            {
                Remove();
                return 0f;
            }

            switch (timerMode)
            {
                case TimerMode.Timer:
                    return modValue;
                case TimerMode.TimerDecreasing:
                    return modValue * timer.RemainingNormalized;
                case TimerMode.TimerIncreasing:
                    return modValue * timer.ElapsedNormalized;
                default:
                    return modValue;
            }
        }

        /// <summary>
        /// Applies this modifier to a current value. Default implementation adds the modifier value.
        /// Override this in derived classes for different operations (multiply, divide, etc.).
        /// </summary>
        /// <param name="currentValue">The stat value before this modifier is applied.</param>
        /// <returns>The stat value after this modifier is applied.</returns>
        public virtual float ApplyTo(float currentValue)
        {
            return currentValue + ModValue();
        }

        public FloatStatModifier(float _modValue, string description = "") : base(description)
        {
            modValue = _modValue;
        }

        /// <summary>
        /// Called every frame by Ticker when this modifier has an active timer.
        /// Removes the modifier once the timer expires.
        /// </summary>
        public void Tick()
        {
            if (timerMode != TimerMode.None && timer != null && timer.IsExpired)
                Remove();
        }

        /// <summary>
        /// Enables timer for this modifier and registers it with Ticker for automatic expiry.
        /// </summary>
        internal void EnableTimer(float duration, TimerMode mode)
        {
            timer = new Ticker.Timer(duration);
            timerMode = mode;
            Ticker.Register(this);
        }
    }
}
