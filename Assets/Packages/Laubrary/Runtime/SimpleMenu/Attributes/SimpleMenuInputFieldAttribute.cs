using System;

namespace Laubrary.SimpleMenu
{
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
    public class SimpleMenuInputFieldAttribute : Attribute
    {
        public string Label { get; }
        public string Placeholder { get; }
        public string OnValueChanged { get; }

        public SimpleMenuInputFieldAttribute(string label, string placeholder = "", string onValueChanged = null)
        {
            Label = label;
            Placeholder = placeholder;
            OnValueChanged = onValueChanged;
        }
    }
}
