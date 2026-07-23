// ZuiToggleButton — the old IMGUI ZUI button-style toggle: a button that stays visibly pressed when on,
// instead of a checkbox with a tick. Reads at a glance in a row of them (a set of feature flags), and
// costs less width than "checkbox + label" because the label IS the button.
using System;
using UnityEngine.UIElements;

namespace Laubrary.Zui
{
    public class ZuiToggleButton : Button
    {
        bool _value;
        readonly Action<bool> _onChanged;

        public bool value
        {
            get => _value;
            set { _value = value; EnableInClassList("zui-togglebutton--on", _value); }
        }

        public ZuiToggleButton(string label, string tooltip, bool value, Action<bool> onChanged)
        {
            text = label;
            this.tooltip = tooltip;
            _onChanged = onChanged;
            AddToClassList("zui-togglebutton");
            this.value = value;
            clicked += () => { this.value = !_value; _onChanged?.Invoke(_value); };
        }
    }
}
