// ZuiAnchorGrid — the nine-point picker: which corner, edge or centre of a box is the point that gets
// attached to something else.
//
// It is a SPATIAL choice, so it is drawn spatially. The alternative every tool reaches for first is a
// nine-entry dropdown or a wrapped radio strip, and both make the author translate "Bottom right" into a
// position in their head on every visit; the grid IS the box, so the cell you press is where the anchor
// sits. Single-select by definition — a box has one anchor — so pressing the lit cell keeps it lit rather
// than clearing to nothing, which is the difference between this and a row of independent toggles.
//
// The cells are ZuiToggleButtons, so the "which one is chosen" tint is the toolkit's own accent, tuned in
// one place with every other latched control, and the grid can never drift into looking like a foreign
// widget. The chosen anchor's NAME is spelled out beside the grid: the grid answers "where" at a glance,
// the label answers "what is this value called" for anyone writing the number down or reading a spec.
//
// Order is row-major from the TOP-LEFT — index 0..8 = Top left, Top, Top right, Left, Center, Right,
// Bottom left, Bottom, Bottom right — matching the reading order of the grid itself, so a host can map an
// index straight to its own anchor enum without a lookup table.
using System;
using UnityEngine.UIElements;

namespace Laubrary.Zui
{
    public class ZuiAnchorGrid : VisualElement
    {
        /// Index → name, row-major from the top-left. Public so a host can label a value it stores as an
        /// int, or drive its own enum's ToString, without restating the order (and getting it wrong).
        public static readonly string[] Names =
        {
            "Top left",    "Top",    "Top right",
            "Left",        "Center", "Right",
            "Bottom left", "Bottom", "Bottom right",
        };

        readonly ZuiToggleButton[] _cells = new ZuiToggleButton[9];
        readonly Label _name;
        readonly Action<int> _onChanged;
        readonly Action _onBeforeMutate;
        int _value;

        /// The chosen anchor, 0..8. Setting it re-lights the grid WITHOUT notifying — the same contract as
        /// every other ZUI control, so a host rebuilding from data never re-enters its own change handler.
        public int value
        {
            get => _value;
            set => SetValueWithoutNotify(value);
        }

        public void SetValueWithoutNotify(int newValue)
        {
            _value = newValue < 0 || newValue > 8 ? 4 : newValue;   // out of range reads as Center, never as "nothing chosen"
            for (int i = 0; i < 9; i++) _cells[i].SetValueWithoutNotify(i == _value);
            _name.text = Names[_value];
        }

        public ZuiAnchorGrid(int selected, string tooltip, Action<int> onChanged, Action onBeforeMutate = null)
        {
            _onChanged = onChanged;
            _onBeforeMutate = onBeforeMutate;
            this.tooltip = tooltip;
            AddToClassList("zui-anchorgrid");

            var grid = new VisualElement();
            grid.AddToClassList("zui-anchorgrid__grid");
            Add(grid);

            for (int r = 0; r < 3; r++)
            {
                var row = new VisualElement();
                row.AddToClassList("zui-anchorgrid__row");
                if (r == 0) row.AddToClassList("zui-anchorgrid__row--first");
                grid.Add(row);

                for (int c = 0; c < 3; c++)
                {
                    int index = r * 3 + c;
                    // Every cell says which point it is, so hovering the grid teaches the nine names without
                    // the author having to press anything to find out.
                    var cell = new ZuiToggleButton("", tooltip + "  ·  " + Names[index] + ".", index == selected,
                        // A ZuiToggleButton flips its own latch before telling us; for a radio that is the wrong
                        // answer half the time (pressing the lit cell would clear it), so the reported bool is
                        // ignored and the whole grid is re-lit from the index instead.
                        _ => Select(index));
                    cell.AddToClassList("zui-anchorgrid__cell");
                    if (c == 0) cell.AddToClassList("zui-anchorgrid__cell--first");
                    _cells[index] = cell;
                    row.Add(cell);
                }
            }

            _name = Z.Text(Names[0], ZuiText.Body, tooltip);
            _name.AddToClassList("zui-anchorgrid__name");
            Add(_name);

            SetValueWithoutNotify(selected);
        }

        void Select(int index)
        {
            bool changed = index != _value;
            SetValueWithoutNotify(index);
            // Pressing the already-chosen cell is a no-op, not a repeat edit: it must not open an Undo step
            // for a change that did not happen.
            if (!changed) return;
            _onBeforeMutate?.Invoke();
            _onChanged?.Invoke(index);
        }
    }
}
