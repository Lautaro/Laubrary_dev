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
        /// `icons` (optional, one per segment) draws a leading glyph in each — null stays text-only.
        public static ZuiSegmented Radio(int selected, string[] labels, string tooltip, Action<int> onChanged,
            string[] icons = null)
            => new ZuiSegmented(labels, tooltip, i => i == selected, onChanged, null, icons);

        /// Multi-select: `isOn(index)` says which segments are lit, `onToggled(index, on)` fires per tap.
        /// `icons` (optional, one per segment) draws a leading glyph in each.
        public static ZuiSegmented Multi(Func<int, bool> isOn, string[] labels, string tooltip,
            Action<int, bool> onToggled, string[] icons = null)
            => new ZuiSegmented(labels, tooltip, isOn, null, onToggled, icons);

        ZuiSegmented(string[] labels, string tooltip, Func<int, bool> isOn,
            Action<int> onSingle, Action<int, bool> onMulti, string[] icons = null)
        {
            _multi = onMulti != null;
            _onSingle = onSingle; _onMulti = onMulti;
            this.tooltip = tooltip;
            AddToClassList("zui-segmented");

            for (int i = 0; i < labels.Length; i++)
            {
                int idx = i;
                var b = new Button { tooltip = tooltip };
                Z.FillButton(b, labels[i], Z.IconAt(icons, i));   // no icon ⇒ just sets .text
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

        /// Let this group BREAK ONTO FURTHER ROWS when the width it is given cannot hold every segment side
        /// by side, instead of running off the end of whatever contains it. Opt-in, because a short group
        /// (a 2–3 option radio) should keep the one-line joined-strip look and never re-flow under the user.
        ///
        /// T-0315: a ZuiSegmented is `flex-shrink: 0` and `NoWrap`, so a long roster could not give a pixel
        /// however narrow its host got. Chunks' seventeen-section toggle bar measured 1008px inside an 820px
        /// window — its own declared minimum — and its last four sections sat off the window entirely: they
        /// could not be shown or hidden at all until the window was dragged past 1026px. Wrapping cannot
        /// overflow at any width (the worst case is one segment per line), and it is the shared control that
        /// gained it, so every tool's bar is fixed at once rather than Chunks' roster being trimmed.
        ///
        /// The wrap look is carried by `.zui-segmented--wrap` in the sheet: every segment gets its OWN left
        /// border so the first one on a broken row is not left open, and the 1px comes out of that segment's
        /// left padding, so the swap is width-neutral by construction (sub-pixel rounding either way when
        /// measured live) — and a group that can wrap absorbs a pixel by definition anyway.
        public ZuiSegmented Wrapping()
        {
            AddToClassList("zui-segmented--wrap");
            return this;
        }

        /// Re-light the segments from an external source (after a rebuild that didn't recreate this).
        public void SetOn(Func<int, bool> isOn)
        {
            for (int i = 0; i < _segs.Count; i++) _segs[i].EnableInClassList("zui-segmented__on", isOn(i));
        }

        /// The underlying button for a segment, so a caller (e.g. ZuiSectionToggleBar's solo handling) can
        /// wire extra behaviour — a right-click handler, an extra styling class — onto one segment without
        /// this control needing to know about it.
        public Button SegmentAt(int index) => _segs[index];
    }
}
