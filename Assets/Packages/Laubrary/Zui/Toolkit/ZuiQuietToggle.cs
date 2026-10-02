using System;
using UnityEngine.UIElements;

namespace Laubrary.Zui
{
    /// A compact borderless view toggle for secondary chrome, with a subdued selected state.
    public class ZuiQuietToggle : Button
    {
        bool _value;
        readonly Action<bool> _onChanged;
        readonly Func<bool, string> _tooltip;

        public bool value
        {
            get => _value;
            set
            {
                _value = value;
                EnableInClassList("zui-quiet-toggle--on", value);
                if (_tooltip != null) tooltip = _tooltip(value);
            }
        }

        public void SetValueWithoutNotify(bool newValue) => value = newValue;

        public ZuiQuietToggle(string label, Func<bool, string> tooltip, bool value, Action<bool> onChanged)
        {
            text = label;
            AddToClassList("zui-quiet-toggle");
            _tooltip = tooltip;
            _onChanged = onChanged;
            this.value = value;
            clicked += () => { this.value = !_value; _onChanged?.Invoke(_value); };
        }
    }
}
