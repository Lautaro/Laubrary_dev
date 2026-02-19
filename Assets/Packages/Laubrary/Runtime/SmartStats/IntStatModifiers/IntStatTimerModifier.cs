using Lautaro.Stats.Engine;

namespace Lautaro.Stats
{
    /// <summary>
    /// A time-limited addition modifier that automatically removes itself after a duration.
    /// Works identically to FloatStatTimerModifier but with integer values.
    /// </summary>
    [System.Serializable]
    public class IntStatTimerModifier : IntAdditionModifier, IUpdatable
    {
        public StatModifierTimer timer;

        /// <summary>
        /// If true, the modifier value scales based on remaining time percentage.
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

        public IntStatTimerModifier(float duration, int value, string description = "", bool timeRelativeValue = false) 
            : base(value, description)
        {
            timer = new StatModifierTimer(duration);
            TimeRelativeValue = timeRelativeValue;
            UpdateEngine.Register(this);
        }

        public override int ModValue()
        {
            if (timer.Remaining <= 0)
            {
                Remove();
                return 0;
            }

            if (TimeRelativeValue)
                return UnityEngine.Mathf.RoundToInt(modValue * timer.RemainingPercentage);
            else
                return modValue;
        }

        public new void Update()
        {
            if (Remaining <= 0)
            {
                Remove();
            }
        }
    }
}
