// ZuiThumbGrid — the multi-select + reorder MATH shared by every thumbnail-grid editor (Laumination Builder's
// sequence strip, PyrePlus CherryFraming's cherry slots). Click=select, Shift=range, Ctrl/Cmd=toggle, drag a
// tile onto another to reorder. This class holds only the technology-agnostic pieces (selection-set mutation,
// contiguous-block list reorder) — the actual mouse/pointer gesture recognition stays with the caller because
// Laumination's strip is an IMGUI island and PyrePlus's grid is UI Toolkit, and there is no single event API
// spanning both. Both callers apply the SAME algorithm through this class instead of hand-rolling it twice.
using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Zui
{
    public static class ZuiThumbGrid
    {
        /// Replace the selection with just `i`. The anchor moves here so the next Shift-extend starts from it.
        public static void SelectSingle(HashSet<int> selected, ref int primary, ref int anchor, int i)
        {
            selected.Clear();
            selected.Add(i);
            primary = i;
            anchor = i;
        }

        /// Toggle `i` in/out of the selection without touching the others.
        public static void Toggle(HashSet<int> selected, ref int primary, ref int anchor, int i)
        {
            if (!selected.Remove(i)) { selected.Add(i); primary = i; }
            else if (primary == i) primary = selected.Count > 0 ? Min(selected) : -1;
            anchor = i;
        }

        /// Extend the selection from the anchor to `i` (inclusive). If there's no anchor yet, behaves like
        /// SelectSingle.
        public static void RangeTo(HashSet<int> selected, ref int primary, ref int anchor, int i)
        {
            if (anchor < 0) { SelectSingle(selected, ref primary, ref anchor, i); return; }
            selected.Clear();
            for (int k = Mathf.Min(anchor, i); k <= Mathf.Max(anchor, i); k++) selected.Add(k);
            primary = i;
        }

        static int Min(HashSet<int> set)
        {
            int m = int.MaxValue;
            foreach (int v in set) if (v < m) m = v;
            return m;
        }

        /// Move `indices` (any order, any contiguity) out of `list` and reinsert them as one contiguous block
        /// landing at `targetIndex` — the standard ZuiReorder convention: `targetIndex` is the LIVE-LIST
        /// insertion point as seen BEFORE the block is removed (so dragging an item past itself downward still
        /// reads as "drop after here"). Returns the index the first (lowest-original-index) moved item lands
        /// at, so the caller can rebuild its selection around the moved block.
        public static int MoveBlock<T>(IList<T> list, List<int> indices, int targetIndex)
        {
            var sorted = new List<int>(indices);
            sorted.Sort();
            int from = sorted[0];
            int to = targetIndex > from ? targetIndex - 1 : targetIndex;
            var moving = new List<T>(sorted.Count);
            foreach (int k in sorted) moving.Add(list[k]);
            for (int k = sorted.Count - 1; k >= 0; k--) list.RemoveAt(sorted[k]);
            int firstNewIndex = Mathf.Clamp(to, 0, list.Count);
            for (int k = 0; k < moving.Count; k++) list.Insert(firstNewIndex + k, moving[k]);
            return firstNewIndex;
        }
    }
}
