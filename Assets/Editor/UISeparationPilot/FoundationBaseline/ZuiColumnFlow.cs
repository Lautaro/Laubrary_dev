// ZuiColumnFlow — a container that stacks its children vertically at a set "preferred" column width and,
// as it gets wide enough for two (or more) such columns, SPLITS the stack into contiguous runs and moves
// the tail of the stack into the next column. Unlike Z.Columns (masonry: items dealt round-robin into
// fixed independent stacks), this is ONE logical stack that is cut into pieces by width — the order down
// column 1 then column 2 then column 3 is exactly the order the children were added, so "the bottom half
// of the control stack moves to the second column" is literal.
//
// ── How the children live in the tree ────────────────────────────────────────────────────────────────
// contentContainer is overridden to the RIGHTMOST column, so `flow.Add(ctrl)` appends `ctrl` to the end
// of the logical stack (bottom of the last column). The authoritative order is recovered each layout pass
// by reading the columns left-to-right, top-to-bottom — a contiguous split preserves order, so this
// reconstruction always equals the add order. There is no separate shadow list to keep in sync and no
// hidden "intake" element, so a unit is never in a limbo/unpainted state: it always sits in a real column.
//
//   Caveat (documented, matching the single-contentContainer limits of UI Toolkit): `flow.Clear()`,
//   `flow.Remove(x)`, `flow.IndexOf(x)` and `flow[i]` delegate to the last column only (that is what
//   contentContainer is). Remove a single unit with `unit.RemoveFromHierarchy()` (always safe — it is
//   pruned on the next pass because gathering reads the live tree), and rebuild the whole flow rather
//   than calling Clear(). In practice these flows are composed once at window-build time.
//
// ── The geometry loop and why it terminates ──────────────────────────────────────────────────────────
// A redistribution re-parents units and (at a new column count) re-wraps their content, which changes
// heights, which fires another GeometryChangedEvent — the classic relayout feedback loop. It is damped by
// a signature guard: a pass only DOES work when the clamped column count changed, a unit was added or
// removed (the unit count changed), or the width crossed a whole-column boundary (the unclamped
// width/ColumnWidth bucket changed) — otherwise it returns immediately. Column WIDTH depends only on the
// column COUNT and the flow's own width (each column is flexBasis:0/flexGrow:1), never on how the split
// falls, so re-splitting at a fixed count never changes a column's width and therefore never re-wraps
// content or changes heights: within one width-bucket the layout reaches a fixed point in one pass. The
// only re-triggering inputs are genuine user resizes / add-remove, which are finite. A small bounded
// number of scheduled "settle" passes (capped, see _settleBudget) re-balances against the heights the NEW
// column width produces after a count change — bounded, so it cannot loop.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zui.FoundationBaseline
{
    public class ZuiColumnFlow : VisualElement
    {
        const int MaxColumns = 4;      // the user's cap
        const float Gutter = 6f;       // between-column gap, matching Z.Columns
        const int MaxSettleBudget = 2; // bounded follow-up passes after a real change (loop safety)

        readonly VisualElement _columnsRow;             // the Row that holds the column elements
        readonly List<VisualElement> _columns = new();  // current column elements, left → right

        float _columnWidth;

        // Layout signature from the last applied pass — the damping guard compares against these.
        int _lastColumnCount = -1;   // clamped column count (1..MaxColumns)
        int _lastUnitCount = -1;     // number of flow units (add/remove detection)
        int _lastBucket = int.MinValue; // unclamped floor(width/ColumnWidth) — the width-boundary detector

        bool _pendingSettle;         // a settle pass is scheduled; lets the guard run it
        int _settleBudget;           // remaining bounded settle passes for the current change

        /// The preferred column width. The container allows a second column once it is this wide × 2, a
        /// third at × 3, and so on up to MaxColumns.
        public float ColumnWidth
        {
            get => _columnWidth;
            set
            {
                _columnWidth = Mathf.Max(1f, value);
                _lastBucket = int.MinValue;   // force the next pass to recompute against the new width
                ScheduleRedistribute();
            }
        }

        /// Children added with `flow.Add(..)` land at the bottom of the last column — i.e. the end of the
        /// logical stack — and become flow units in add order.
        public override VisualElement contentContainer
            => _columns != null && _columns.Count > 0 ? _columns[_columns.Count - 1] : this;

        public ZuiColumnFlow(float columnWidth)
        {
            _columnWidth = Mathf.Max(1f, columnWidth);
            AddToClassList("zui-column-flow");

            _columnsRow = new VisualElement();
            _columnsRow.AddToClassList("zui-column-flow__row");
            _columnsRow.style.flexDirection = FlexDirection.Row;
            hierarchy.Add(_columnsRow);   // structural: contentContainer != this, so bypass it

            // Start with one column so early Adds (before the first layout) have a home; the first
            // GeometryChangedEvent splits the single stack into however many columns the width allows.
            var col0 = MakeColumn(0);
            _columns.Add(col0);
            _columnsRow.Add(col0);

            RegisterCallback<GeometryChangedEvent>(_ => Redistribute());
        }

        VisualElement MakeColumn(int index)
        {
            var col = new VisualElement();
            col.AddToClassList("zui-column-flow__column");
            col.style.flexDirection = FlexDirection.Column;
            col.style.flexGrow = 1f;      // share the row width equally …
            col.style.flexShrink = 1f;
            col.style.flexBasis = 0f;     // … regardless of content (equal columns)
            col.style.minWidth = 0f;      // let it shrink; content wraps inside
            col.style.marginLeft = index > 0 ? Gutter : 0f;
            return col;
        }

        void ScheduleRedistribute()
        {
            if (_pendingSettle) return;
            _pendingSettle = true;
            _settleBudget = MaxSettleBudget;
            schedule.Execute(Redistribute).ExecuteLater(0);
        }

        // The single work function. Guarded so a height-changing redistribute cannot loop forever.
        void Redistribute()
        {
            bool settlePass = _pendingSettle;
            _pendingSettle = false;

            if (panel == null) return;                 // detached — nothing to lay out
            float width = contentRect.width;
            if (float.IsNaN(width) || width <= 0f) return;   // not laid out yet

            int bucket = Mathf.Max(1, Mathf.FloorToInt(width / Mathf.Max(1f, _columnWidth)));
            int n = Mathf.Clamp(bucket, 1, MaxColumns);

            var units = GatherUnits();
            int unitCount = units.Count;

            bool countChanged = n != _lastColumnCount;
            bool membershipChanged = unitCount != _lastUnitCount;
            bool boundaryCrossed = bucket != _lastBucket;
            bool triggered = countChanged || membershipChanged || boundaryCrossed;

            // The damping guard: with nothing structurally different and no scheduled settle pass, do
            // nothing — this is where the geometry feedback loop terminates.
            if (!triggered && !settlePass) return;

            // Measure unit heights BEFORE re-parenting (re-parenting resets their resolved layout).
            var heights = new float[unitCount];
            float total = 0f;
            for (int i = 0; i < unitCount; i++)
            {
                float h = units[i].resolvedStyle.height;
                if (float.IsNaN(h) || h <= 0f) h = units[i].layout.height;   // fallback
                if (float.IsNaN(h) || h <= 0f) h = 0f;
                heights[i] = h;
                total += h;
            }
            // No usable height info yet (a pre-measure pass): fall back to an equal-COUNT split, and let a
            // settle pass re-balance once real heights exist.
            bool heightsKnown = unitCount == 0 || total > 0f;

            ApplyColumns(units, heights, n, heightsKnown);

            _lastColumnCount = n;
            _lastUnitCount = unitCount;
            _lastBucket = bucket;

            // Refill the settle budget on a genuine change; spend it on pure settle passes.
            if (triggered) _settleBudget = MaxSettleBudget;
            else if (settlePass) _settleBudget = Mathf.Max(0, _settleBudget - 1);

            // Re-balance after the fact when a count change re-wrapped content (the heights we split on
            // were measured at the OLD column width) or when heights were unknown. Bounded by the budget,
            // so this cannot become an endless loop.
            bool wantSettle = (triggered || !heightsKnown) && _settleBudget > 0;
            if (wantSettle && !_pendingSettle)
            {
                _pendingSettle = true;
                schedule.Execute(Redistribute).ExecuteLater(16);
            }
        }

        // Authoritative unit order: read the columns left-to-right, top-to-bottom. Because every split is
        // contiguous and order-preserving, this reconstructs the add order exactly.
        List<VisualElement> GatherUnits()
        {
            var list = new List<VisualElement>();
            foreach (var col in _columns)
                foreach (var child in col.Children())
                    list.Add(child);
            return list;
        }

        void ApplyColumns(List<VisualElement> units, float[] heights, int n, bool heightsKnown)
        {
            // Detach every unit first so trimming columns can never destroy one (references live in `units`).
            for (int i = 0; i < units.Count; i++) units[i].RemoveFromHierarchy();

            SetColumnCount(n);

            int[] ends = heightsKnown
                ? ContiguousSplitByHeight(heights, n)
                : EqualCountSplit(units.Count, n);

            int idx = 0;
            for (int c = 0; c < n; c++)
            {
                int end = ends[c];
                for (; idx < end && idx < units.Count; idx++)
                {
                    var u = units[idx];
                    u.style.flexGrow = 0f;              // in a column, flexGrow would grow HEIGHT
                    u.style.flexShrink = 0f;
                    u.style.alignSelf = Align.Stretch;  // consume the full column width
                    u.style.marginBottom = 2f;
                    _columns[c].Add(u);
                }
            }
        }

        void SetColumnCount(int n)
        {
            while (_columns.Count > n)
            {
                var col = _columns[_columns.Count - 1];
                col.RemoveFromHierarchy();
                _columns.RemoveAt(_columns.Count - 1);
            }
            while (_columns.Count < n)
            {
                var col = MakeColumn(_columns.Count);
                _columns.Add(col);
                _columnsRow.Add(col);
            }
            for (int i = 0; i < _columns.Count; i++)
                _columns[i].style.marginLeft = i > 0 ? Gutter : 0f;
        }

        // Equal-count contiguous split — the pre-measure fallback and the basis for the height split when
        // there are as many (or fewer) units than columns. `ends[c]` is the exclusive end index of column c.
        static int[] EqualCountSplit(int count, int n)
        {
            var ends = new int[n];
            if (count <= n)   // one unit per column, remaining columns empty
            {
                for (int c = 0; c < n; c++) ends[c] = Mathf.Min(c + 1, count);
                return ends;
            }
            int baseCount = count / n, rem = count % n, idx = 0;
            for (int c = 0; c < n; c++)
            {
                idx += baseCount + (c < rem ? 1 : 0);
                ends[c] = idx;
            }
            return ends;
        }

        // Height-balanced contiguous split: walk the stack, filling each column toward a running target of
        // (remaining height / remaining columns), rounding each unit to whichever side of the target it is
        // closer to, and always leaving at least one unit for every remaining column. For n = 2 with even
        // heights this cuts the stack near the middle — "the bottom half moves to the second column".
        static int[] ContiguousSplitByHeight(float[] h, int n)
        {
            int count = h.Length;
            var ends = new int[n];
            if (count <= n) return EqualCountSplit(count, n);
            if (n <= 1) { ends[0] = count; return ends; }

            float total = 0f;
            for (int i = 0; i < count; i++) total += Mathf.Max(0f, h[i]);

            int idx = 0;
            float placed = 0f;
            for (int c = 0; c < n; c++)
            {
                if (c == n - 1) { ends[c] = count; break; }   // last column takes the remainder

                int colsRemaining = n - c;
                int maxEnd = count - (colsRemaining - 1);      // leave ≥1 unit per remaining column
                int minEnd = idx + 1;                          // ≥1 unit in this column
                float colTarget = (total - placed) / colsRemaining;

                float acc = 0f;
                int end = idx;
                while (end < maxEnd)
                {
                    float hh = Mathf.Max(0f, h[end]);
                    if (end == idx || acc + hh <= colTarget)   // always take at least one; keep filling
                    {
                        acc += hh; end++;
                    }
                    else
                    {
                        // Overshoot: include this unit only if doing so lands closer to the target.
                        float without = colTarget - acc;       // ≥ 0
                        float with = (acc + hh) - colTarget;   // > 0
                        if (with < without) { acc += hh; end++; }
                        break;
                    }
                }
                end = Mathf.Clamp(end, minEnd, maxEnd);
                for (int k = idx; k < end; k++) placed += Mathf.Max(0f, h[k]);
                ends[c] = end;
                idx = end;
            }
            return ends;
        }
    }

    // ZuiHGroup — a horizontal row treated as ONE flow unit. Placed in a ZuiColumnFlow it occupies a single
    // stack slot and moves between columns as a whole; its own children lay out left-to-right and wrap onto
    // a second line if the column is too narrow. A distinct type (not a bare Z.Row) so flows and audits can
    // recognise a group as one unit. Children keep their natural widths; the group stretches to column width.
    public class ZuiHGroup : VisualElement
    {
        public ZuiHGroup(params VisualElement[] kids)
        {
            AddToClassList("zui-hgroup");
            style.flexDirection = FlexDirection.Row;
            style.flexWrap = Wrap.Wrap;          // fall onto a second line rather than overflow the column
            style.alignItems = Align.Center;
            style.alignSelf = Align.Stretch;     // the group fills the column width …
            foreach (var k in kids)
            {
                if (k == null) continue;
                k.style.marginRight = 6f;        // … while each child keeps its natural width
                k.style.marginBottom = 2f;
                Add(k);
            }
        }
    }
}
