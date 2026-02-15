using System;

namespace Laubrary.SimpleMenu
{
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
    public class SimpleMenuSliderAttribute : Attribute
    {
        public string Label { get; }
        public float MinValue { get; }
        public float MaxValue { get; }
        public bool WholeNumbers { get; }
        public string OnValueChanged { get; }
        public bool ShowTitleLabel { get; }
        public bool ShowValueLabel { get; }

        public SimpleMenuSliderAttribute(string label, float minValue = 0f, float maxValue = 1f, bool wholeNumbers = false, string onValueChanged = null, bool showTitleLabel = true, bool showValueLabel = false)
        {
            Label = label;
            MinValue = minValue;
            MaxValue = maxValue;
            WholeNumbers = wholeNumbers;
            OnValueChanged = onValueChanged;
            ShowTitleLabel = showTitleLabel;
            ShowValueLabel = showValueLabel;
        }
    }
}
