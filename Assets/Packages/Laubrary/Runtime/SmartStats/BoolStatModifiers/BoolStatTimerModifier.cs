using System;
using Lautaro.Stats.Engine;

namespace Lautaro.Stats
{
    [Serializable]
    public class BoolStatTimerModifier : BoolStatModifierBase, IUpdatable
    {
        public StatModifierTimer timer;

        public BoolStatTimerModifier(float duration, string description = "", BoolStatModifierType modifierType = BoolStatModifierType.Flip) : base(description)
        {
            this.modifierType = modifierType;
            timer = new StatModifierTimer(duration);
            UpdateEngine.Register(this);
        }

        public float StartTime => timer.StartTime;
        public float Duration => timer.Duration;
        public float Elapsed => timer.Elapsed;
        public float Remaining
        {
            get
            {
                if (timer.Remaining <= 0)
                {
                    Remove();
                    return 0;
                }
                else
                {
                    return timer.Remaining;
                }
            }
        }
        public float RemainingPercentage => timer.RemainingPercentage;

        public void Update()
        {
            if (Remaining <= 0)
            {
                Remove();
            }
        }
    }
}