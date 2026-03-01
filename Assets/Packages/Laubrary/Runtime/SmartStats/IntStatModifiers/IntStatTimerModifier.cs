using Lautaro.Stats.Engine;
using Laubrary.LaubraryTicker;

namespace Lautaro.Stats
{
    /// <summary>
    /// A time-limited addition modifier that automatically removes itself after a duration.
    /// Works identically to FloatStatTimerModifier but with integer values.
    /// </summary>
    [System.Serializable]
    public class IntStatTimerModifier : IntAdditionModifier
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

        public IntStatTimerModifier(float duration, int value, string description = "", bool timeRelativeValue = false)
            : base(value, description)
        {
            TimeRelativeValue = timeRelativeValue;
            timer = new Ticker.Timer(duration);
            timerMode = timeRelativeValue ? TimerMode.TimerDecreasing : TimerMode.Timer;
            Ticker.Register(this);
        }
    }
}
