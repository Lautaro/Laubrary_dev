using System;
using UnityEngine;
using Lautaro.Stats.Engine;
using Laubrary.LaubraryTicker;

namespace Lautaro.Stats
{
    /// <summary>
    /// Base class for all IntStat modifiers. Works identically to FloatStatModifier but with integer values.
    /// All calculations are rounded to integers to maintain type consistency.
    /// </summary>
    [Serializable]
    public class IntStatModifier : StatModifierBase, ITickable
    {
        public int modValue;

        [SerializeField] public int? priority = null;

        public Ticker.Timer timer;
        public TimerMode timerMode = TimerMode.None;

        public virtual int ModValue()
        {
            if (timerMode == TimerMode.None || timer == null)
                return modValue;

            if (timer.IsExpired)
            {
                Remove();
                return 0;
            }

            switch (timerMode)
            {
                case TimerMode.Timer:
                    return modValue;
                case TimerMode.TimerDecreasing:
                    return Mathf.RoundToInt(modValue * timer.RemainingNormalized);
                case TimerMode.TimerIncreasing:
                    return Mathf.RoundToInt(modValue * timer.ElapsedNormalized);
                default:
                    return modValue;
            }
        }

        public virtual int ApplyTo(int currentValue)
        {
            return currentValue + ModValue();
        }

        public IntStatModifier(int _modValue, string description = "") : base(description)
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
