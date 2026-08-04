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
        // An empty toggle — no label, no icon — is standing in for a checkbox: a mute, an enable, a row's
        // tick. With nothing drawn in it, "on" and "off" differ only by a background tint, which is easy to
        // misread and impossible to read at all on a card that is already tinted for being muted. So an
        // empty one carries a MARK when it is on. A toggle that has a label or an icon says what it is
        // already and keeps saying it in both states.
        readonly bool _markWhenOn;

        public bool value
        {
            get => _value;
            set
            {
                _value = value;
                EnableInClassList("zui-togglebutton--on", _value);
                if (_markWhenOn) text = _value ? "✔" : "";
            }
        }

        /// API parity with UITK's Toggle, so callers that held the old native control keep compiling —
        /// the value setter never invokes the callback anyway.
        public void SetValueWithoutNotify(bool newValue) => value = newValue;

        public ZuiToggleButton(string label, string tooltip, bool value, Action<bool> onChanged, string icon = null)
        {
            this.tooltip = tooltip;
            _onChanged = onChanged;
            _markWhenOn = string.IsNullOrEmpty(label) && string.IsNullOrEmpty(icon);
            AddToClassList("zui-togglebutton");
            if (_markWhenOn) AddToClassList("zui-togglebutton--mark");
            Z.FillButton(this, label, icon);   // no icon ⇒ just sets .text, unchanged from before
            this.value = value;   // AFTER FillButton, so an empty toggle's mark wins over the blank label
            clicked += () => { this.value = !_value; _onChanged?.Invoke(_value); };
        }
    }
}
