using System;

namespace Laubrary.SimpleMenu
{
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
    public class SimpleMenuToggleAttribute : Attribute
    {
        public string Label { get; }
        public string OnValueChanged { get; }
        public string PersistenceId { get; }

        public SimpleMenuToggleAttribute(string label, string onValueChanged = null, string persistenceId = null)
        {
            Label = label;
            OnValueChanged = onValueChanged;
            PersistenceId = persistenceId;
        }
    }
}
