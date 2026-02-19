using System;
using UnityEngine;

namespace Lautaro.Stats.Engine
{
    [System.Serializable]
    public class StatModifierTimer
    {
        public float StartTime;
        public float Duration;

        public StatModifierTimer(float duration)
        {
            Duration = duration;
            StartTime = Time.time;
        }

        public float Elapsed => Time.time - StartTime;

        public float Remaining
        {
            get
            {
                if (Elapsed > Duration) return 0f;
                return Duration - Elapsed;
            }
        }

        public float RemainingPercentage
        {
            get
            {
                if (Elapsed > Duration) 
                    return 0f;
                
                var elapsedPercentage = Elapsed / Duration;
                return 1f - elapsedPercentage;
            }
        }
    }
}
