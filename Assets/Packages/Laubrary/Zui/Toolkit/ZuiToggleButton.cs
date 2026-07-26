// ZuiToggleButton — the old IMGUI ZUI button-style toggle: a button that stays visibly pressed when on,
// instead of a checkbox with a tick. Reads at a glance in a row of them (a set of feature flags), and
// costs less width than "checkbox + label" because the label IS the button.
//
// An optional leading `icon` (a ZUI icon name) draws a tinted glyph before the label via the shared
// Z.FillButton helper — off by default, so a text-only toggle button is byte-identical to before.
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

        public ZuiToggleButton(string label, string tooltip, bool value, Action<bool> onChanged, string icon = null)
        {
            this.tooltip = tooltip;
            _onChanged = onChanged;
            AddToClassList("zui-togglebutton");
            Z.FillButton(this, label, icon);   // no icon ⇒ just sets .text, unchanged from before
            this.value = value;
            clicked += () => { this.value = !_value; _onChanged?.Invoke(_value); };
        }
    }
}
