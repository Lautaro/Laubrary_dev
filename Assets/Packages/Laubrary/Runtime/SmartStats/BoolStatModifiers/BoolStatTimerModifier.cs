using System;
using Lautaro.Stats.Engine;
using Laubrary.LaubraryTicker;

namespace Lautaro.Stats
{
    [Serializable]
    public class BoolStatTimerModifier : BoolStatModifierBase, ITickable
    {
        public Ticker.Timer timer;

        public BoolStatTimerModifier(float duration, string description = "", BoolStatModifierType modifierType = BoolStatModifierType.Flip)
            : base(description)
        {
            this.modifierType = modifierType;
            timer = new Ticker.Timer(duration);
            Ticker.Register(this);
        }

        public float StartTime           => timer.StartTime;
        public float Duration            => timer.Duration;
        public float Elapsed             => timer.Elapsed;
        public float Remaining           => timer.Remaining;
        public float RemainingPercentage => timer.RemainingNormalized;

        /// <summary>Called every frame by Ticker. Removes the modifier once the timer expires.</summary>
        public void Tick()
        {
            if (timer.IsExpired)
                Remove();
        }
    }
}
