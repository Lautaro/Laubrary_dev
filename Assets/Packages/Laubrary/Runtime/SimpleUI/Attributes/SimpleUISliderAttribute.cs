using System;

namespace Laubrary.SimpleUI
{
    /// <summary>
    /// Marks a numeric field to be represented as a Slider in SimpleUI generation.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false)]
    public class SimpleUISliderAttribute : Attribute
    {
        public float MinValue { get; }
        public float MaxValue { get; }

        public SimpleUISliderAttribute(float minValue = 0f, float maxValue = 1f)
        {
            MinValue = minValue;
            MaxValue = maxValue;
        }
    }
}
