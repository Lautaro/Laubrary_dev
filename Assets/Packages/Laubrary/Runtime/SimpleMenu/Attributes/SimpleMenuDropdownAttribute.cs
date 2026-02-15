using System;

namespace Laubrary.SimpleMenu
{
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
    public class SimpleMenuDropdownAttribute : Attribute
    {
        public string Label { get; }
        public string[] Options { get; }
        public string GetOptionsMethod { get; }
        public string OnValueChanged { get; }
        public string PersistenceId { get; }

        public SimpleMenuDropdownAttribute(string label, params string[] options)
        {
            Label = label;
            Options = options;
        }

        public SimpleMenuDropdownAttribute(string label, string onValueChanged, params string[] options)
        {
            Label = label;
            Options = options;
            OnValueChanged = onValueChanged;
        }
        public SimpleMenuDropdownAttribute(string label, string getOptionsMethod = null, string onValueChanged = null, string persistenceId = null)
        {
            Label = label;
            GetOptionsMethod = getOptionsMethod;
            OnValueChanged = onValueChanged;
            PersistenceId = persistenceId;
        }
    }
}
