using System;
using UnityEngine;

namespace Lautaro.Stats
{
    /// <summary>
    /// Divides the stat by a value and rounds to nearest integer.
    /// modValue represents the divisor (2 = divide by 2, 3 = divide by 3)
    /// </summary>
    [Serializable]
    public class IntDividerModifier : IntStatModifier
    {
        public IntDividerModifier(int divisor, string description = "") : base(divisor, description)
        {
        }

        public override int ApplyTo(int currentValue)
        {
            int divisor = ModValue();
            if (divisor == 0)
            {
                Debug.LogWarning($"IntDividerModifier '{description}' has divisor of 0. Returning current value unchanged.");
                return currentValue;
            }

            return Mathf.RoundToInt((float)currentValue / divisor);
        }
    }
}
