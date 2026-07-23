// ZuiSegmented — a joined row of buttons that reads as one control. Single-select is a radio (the old
// ZUI MiniRadio look, but custom-drawn so it themes with the rest of the set); multi-select is a set of
// independently-latching segments (the old ZUI MultiToggle) — which is the right control for a flag set
// like Pyre's matte channels, instead of a row of loose checkboxes.
using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Laubrary.Zui
{
    public class ZuiSegmented : VisualElement
    {
        readonly List<Button> _segs = new();
        readonly bool _multi;
        readonly Action<int> _onSingle;
        readonly Action<int, bool> _onMulti;

        /// Single-select (radio): `selected` is the lit index, `onChanged(index)` fires on a pick.
        public static ZuiSegmented Radio(int selected, string[] labels, string tooltip, Action<int> onChanged)
            => new ZuiSegmented(labels, tooltip, i => i == selected, onChanged, null);

        /// Multi-select: `isOn(index)` says which segments are lit, `onToggled(index, on)` fires per tap.
        public static ZuiSegmented Multi(Func<int, bool> isOn, string[] labels, string tooltip,
            Action<int, bool> onToggled)
            => new ZuiSegmented(labels, tooltip, isOn, null, onToggled);

        ZuiSegmented(string[] labels, string tooltip, Func<int, bool> isOn,
            Action<int> onSingle, Action<int, bool> onMulti)
        {
            _multi = onMulti != null;
            _onSingle = onSingle; _onMulti = onMulti;
            this.tooltip = tooltip;
            AddToClassList("zui-segmented");

            for (int i = 0; i < labels.Length; i++)
            {
                int idx = i;
                var b = new Button { text = labels[i], tooltip = tooltip };
                b.AddToClassList("zui-segmented__seg");
                if (i == 0) b.AddToClassList("zui-segmented__first");
                if (i == labels.Length - 1) b.AddToClassList("zui-segmented__last");
                b.EnableInClassList("zui-segmented__on", isOn(i));
                b.clicked += () => OnTap(idx);
                _segs.Add(b);
                Add(b);
            }
        }

        void OnTap(int idx)
        {
            if (_multi)
            {
                bool now = !_segs[idx].ClassListContains("zui-segmented__on");
                _segs[idx].EnableInClassList("zui-segmented__on", now);
                _onMulti?.Invoke(idx, now);
            }
            else
            {
                for (int i = 0; i < _segs.Count; i++)
                    _segs[i].EnableInClassList("zui-segmented__on", i == idx);
                _onSingle?.Invoke(idx);
            }
        }

        /// Re-light the segments from an external source (after a rebuild that didn't recreate this).
        public void SetOn(Func<int, bool> isOn)
        {
            for (int i = 0; i < _segs.Count; i++) _segs[i].EnableInClassList("zui-segmented__on", isOn(i));
        }
    }
}
