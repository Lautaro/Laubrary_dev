using System;
using UnityEngine;
using Lautaro.Stats.Engine;

namespace Lautaro.Stats
{
    /// <summary>
    /// Base class for all IntStat modifiers. Works identically to FloatStatModifier but with integer values.
    /// All calculations are rounded to integers to maintain type consistency.
    /// </summary>
    [Serializable]
    public class IntStatModifier : IUpdatable
    {
        public int modValue;
        public string description;

        [SerializeField] public int? priority = null;
        [SerializeReference] public bool isPaused;

        public StatModifierTimer timer;
        public TimerMode timerMode = TimerMode.None;

        protected bool remove = false;
        public bool FlaggedForRemoval => remove;

        public void Remove() 
        {
            remove = true;
            if (this is IUpdatable) 
            {
                UpdateEngine.UnRegister((IUpdatable)this);
            }
        }

        public virtual int ModValue()
        {
            if (timerMode == TimerMode.None || timer == null)
            {
                return modValue;
            }

            if (timer.Remaining <= 0)
            {
                Remove();
                return 0;
            }

            switch (timerMode)
            {
                case TimerMode.Timer:
                    return modValue;

                case TimerMode.TimerDecreasing:
                    return Mathf.RoundToInt(modValue * timer.RemainingPercentage);

                case TimerMode.TimerIncreasing:
                    return Mathf.RoundToInt(modValue * (1f - timer.RemainingPercentage));

                default:
                    return modValue;
            }
        }

        public virtual int ApplyTo(int currentValue)
        {
            return currentValue + ModValue();
        }

        public IntStatModifier(int _modValue, string description = "")
        {
            this.description = description;
            modValue = _modValue;
        }

        public void Update()
        {
            if (timerMode != TimerMode.None && timer != null && timer.Remaining <= 0)
            {
                Remove();
            }
        }

        internal void EnableTimer(float duration, TimerMode mode)
        {
            timer = new StatModifierTimer(duration);
            timerMode = mode;
            UpdateEngine.Register(this);
        }
    }
}
