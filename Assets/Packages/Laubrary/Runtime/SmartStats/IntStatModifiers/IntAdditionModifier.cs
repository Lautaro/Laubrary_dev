using System;

namespace Lautaro.Stats
{
    /// <summary>
    /// Adds a fixed integer value to the stat.
    /// Example: +10 armor, +5 damage
    /// </summary>
    [Serializable]
    public class IntAdditionModifier : IntStatModifier
    {
        public IntAdditionModifier(int modValue, string description = "") : base(modValue, description)
        {
        }

        public override int ApplyTo(int currentValue)
        {
            return currentValue + ModValue();
        }
    }
}
