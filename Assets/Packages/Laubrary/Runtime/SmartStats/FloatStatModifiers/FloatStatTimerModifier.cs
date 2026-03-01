using Lautaro.Stats.Engine;
using Laubrary.LaubraryTicker;

namespace Lautaro.Stats
{
    /// <summary>
    /// A time-limited addition modifier that automatically removes itself after a duration.
    ///
    /// Features:
    /// - Automatically expires after the specified duration
    /// - Optional time-relative scaling (TimerDecreasing: value decreases as time runs out,
    ///   TimerIncreasing: value grows from 0 to full over the duration)
    /// - Self-registers with Ticker for automatic time tracking
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
    public class FloatStatTimerModifier : FloatAdditionModifier
    {
        /// <summary>
        /// If true, the modifier value scales based on remaining time percentage (TimerDecreasing mode).
        /// If false, the modifier holds its full value until expiry (Timer mode).
        /// </summary>
        public bool TimeRelativeValue = false;

        public float StartTime => timer.StartTime;
        public float Duration  => timer.Duration;
        public float Elapsed   => timer.Elapsed;
        public float Remaining => timer.Remaining;

        /// <summary>Remaining time as a 0–1 fraction. Alias for timer.RemainingNormalized.</summary>
        public float RemainingPercentage => timer.RemainingNormalized;

        /// <summary>
        /// Creates a time-limited addition modifier.
        /// </summary>
        /// <param name="duration">How long the modifier lasts in seconds.</param>
        /// <param name="value">The value to add to the stat.</param>
        /// <param name="description">Human-readable description.</param>
        /// <param name="timeRelativeValue">If true, value decays proportionally to remaining time.</param>
        public FloatStatTimerModifier(float duration, float value, string description = "", bool timeRelativeValue = false)
            : base(value, description)
        {
            TimeRelativeValue = timeRelativeValue;
            timer = new Ticker.Timer(duration);
            timerMode = timeRelativeValue ? TimerMode.TimerDecreasing : TimerMode.Timer;
            Ticker.Register(this);
        }
    }
}
