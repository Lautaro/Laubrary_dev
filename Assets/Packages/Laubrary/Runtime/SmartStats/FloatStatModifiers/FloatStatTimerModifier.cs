using Lautaro.Stats.Engine;

namespace Lautaro.Stats
{
    /// <summary>
    /// A time-limited addition modifier that automatically removes itself after a duration.
    /// 
    /// Features:
    /// - Automatically expires after the specified duration
    /// - Optional time-relative scaling (value decreases as time runs out)
    /// - Self-registers with UpdateEngine for automatic time tracking
    /// 
    /// Example:
    /// // +50 health for 10 seconds
    /// var healthPotion = new FloatStatTimerModifier(10f, 50f, "Health Potion");
    /// player.health.AddModifier(healthPotion);
    /// 
    /// // +100% damage that decays over 5 seconds
    /// var rage = new FloatStatTimerModifier(5f, 1.0f, "Rage", timeRelativeValue: true);
    /// // At t=0: +100%, at t=2.5s: +50%, at t=5s: 0% (expires)
    /// </summary>
    [System.Serializable]
    public class FloatStatTimerModifier : FloatAdditionModifier, IUpdatable
    {
        public StatModifierTimer timer;

        /// <summary>
        /// If true, the modifier value scales based on remaining time percentage.
        /// At 100% remaining time: full value. At 0% remaining: 0 value.
        /// </summary>
        public bool TimeRelativeValue = false;

        public float StartTime => timer.StartTime;
        public float Duration => timer.Duration;
        public float Elapsed => timer.Elapsed;

        public float Remaining
        {
            get
            {
                if (timer.Remaining <= 0)
                    Remove();
                return timer.Remaining;
            }
        }
   
        public float RemainingPercentage
        {
            get
            {
                if (timer.RemainingPercentage <= 0)
                    Remove();
                return timer.RemainingPercentage;
            }
        }

        /// <summary>
        /// Creates a time-limited addition modifier.
        /// </summary>
        /// <param name="duration">How long the modifier lasts in seconds.</param>
        /// <param name="value">The value to add to the stat.</param>
        /// <param name="description">Human-readable description.</param>
        /// <param name="timeRelativeValue">If true, value scales with remaining time percentage.</param>
        public FloatStatTimerModifier(float duration, float value, string description = "", bool timeRelativeValue = false) 
            : base(value, description)
        {
            timer = new StatModifierTimer(duration);
            TimeRelativeValue = timeRelativeValue;
            UpdateEngine.Register(this);
        }

        public override float ModValue()
        {
            if (timer.Remaining <= 0)
            {
                Remove();
                return 0;
            }

            if (TimeRelativeValue)
                return modValue * (timer.RemainingPercentage);
            else
                return modValue;
        }

        public void Update()
        {
            if (Remaining <= 0)
            {
                Remove();
            }
        }
    }
}
