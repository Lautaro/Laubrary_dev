using System;

namespace Laubrary.SimpleMenu
{
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
    public class SimpleMenuSliderAttribute : Attribute
    {
        public string Label { get; }
        public float? MinValue { get; }
        public float? MaxValue { get; }
        public bool? WholeNumbers { get; }
        public string OnValueChanged { get; }
        public bool ShowTitleLabel { get; }
        public bool ShowValueLabel { get; }
        public string PersistenceId { get; }

        private const float DEFAULT_VALUE = float.NaN;

        public SimpleMenuSliderAttribute(string label, string onValueChanged = null, bool showTitleLabel = true, bool showValueLabel = false, string persistenceId = null)
        {
            Label = label;
            MinValue = null;
            MaxValue = null;
            WholeNumbers = null;
            OnValueChanged = onValueChanged;
            ShowTitleLabel = showTitleLabel;
            ShowValueLabel = showValueLabel;
            PersistenceId = persistenceId;
        }

        public SimpleMenuSliderAttribute(
            string label, 
            float minValue, 
            float maxValue, 
            bool wholeNumbers = false, 
            string onValueChanged = null, 
            bool showTitleLabel = true, 
            bool showValueLabel = false,
            string persistenceId = null)
        {
            Label = label;
            MinValue = float.IsNaN(minValue) ? null : (float?)minValue;
            MaxValue = float.IsNaN(maxValue) ? null : (float?)maxValue;
            WholeNumbers = wholeNumbers;
            OnValueChanged = onValueChanged;
            ShowTitleLabel = showTitleLabel;
            ShowValueLabel = showValueLabel;
            PersistenceId = persistenceId;
        }
    }
}
