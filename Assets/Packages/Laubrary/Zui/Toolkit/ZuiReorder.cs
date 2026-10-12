// ZuiReorder — animated drag-to-reorder for any ordered set of elements: a vertical list, a horizontal
// strip or a wrapping flow. While dragging, the picked item follows the pointer and the other items slide
// (a short eased transition) into the places they would take if it were dropped there; nothing in the
// caller's data changes until the drop, which reports one (from, to) move after the item has settled
// into its slot. A press that never travels past a few pixels is a click, not a drag.
//
// Two ways in:
//  * ZuiReorder.MakeGrip(grip, row, container, onMoved) — the classic "≡ grip" form: the items are the
//    direct children of `container`, stacked vertically.
//  * new ZuiReorderList(layout, onMoved).Add(item, …) — an explicit item list in any layout, for items
//    that are not the only children of one container or are absolutely placed (a canvas). An item may
//    name followers: other elements that travel with it (a pattern column under its header).
//
// The motion is drawn with `translate` only, so it never reflows the layout. Items laid out by flexbox
// keep their hierarchy order; absolutely placed items are raised so the dragged one draws on top.
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zui
{
    /// How the items of a reorderable set are arranged on screen.
    public enum ZuiReorderLayout { Vertical, Horizontal, Wrap }

    /// An explicit, ordered set of draggable items. Indices reported to `onMoved` are positions in the
    /// order items were added (remove at `from`, insert at `to`).
    public sealed class ZuiReorderList
    {
        internal readonly ZuiReorderLayout layout;
        internal readonly Action<int, int> onMoved;
        internal readonly List<ZuiReorder.Entry> entries = new List<ZuiReorder.Entry>();

        public ZuiReorderList(ZuiReorderLayout layout, Action<int, int> onMoved)
        {
            this.layout = layout;
            this.onMoved = onMoved;
        }

        public int Count => entries.Count;

        /// Add the next item. `grip` is what the pointer presses (the item itself when null); `onClick`
        /// runs on a press released without dragging; `followers` (asked when a drag starts) are extra
        /// elements that move exactly as the item does.
        public ZuiReorderList Add(VisualElement item, VisualElement grip = null, Action onClick = null,
            Func<IEnumerable<VisualElement>> followers = null)
        {
            var entry = new ZuiReorder.Entry { item = item, onClick = onClick, followers = followers };
            entries.Add(entry);
            ZuiReorder.Wire(grip ?? item, () => this, entry);
            return this;
        }
    }

    public static class ZuiReorder
    {
        /// Pointer travel (px) before a press becomes a drag.
        public const float StartDistance = 4f;
        /// Duration of the slide of the other items and of the dropped item into its slot.
        public const int SlideMs = 110;

        internal sealed class Entry
        {
            public VisualElement item;
            public Action onClick;
            public Func<IEnumerable<VisualElement>> followers;
        }

        /// Make `grip` drag-reorder `row` among the direct children of `container`, which are stacked
        /// vertically. `onMoved(from, to)` fires on drop with list-move semantics: remove at `from`, then
        /// insert at `to`. Only fires on a real move.
        public static void MakeGrip(VisualElement grip, VisualElement row, VisualElement container,
            Action<int, int> onMoved)
        {
            var self = new Entry { item = row };
            Wire(grip, () =>
            {
                var list = new ZuiReorderList(ZuiReorderLayout.Vertical, onMoved);
                foreach (var child in container.Children())
                    list.entries.Add(child == row ? self : new Entry { item = child });
                return list;
            }, self);
        }

        /// True while an item is being dragged or is settling into its slot.
        public static bool Busy => session != null;

        /// Commit a drop that is still settling, at once (a new press does this too).
        public static void FinishPending()
        {
            if (session != null && session.settling) session.Commit();
        }

        static Session session;

        internal static void Wire(VisualElement grip, Func<ZuiReorderList> resolve, Entry entry)
        {
            int pointer = -1;
            Vector2 press = default;
            ZuiReorderList list = null;

            // All three pointer handlers trickle down so they run before a Button grip's own click
            // handling, which would otherwise swallow the press or click on the release of a drag.
            grip.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0) return;
                FinishPending();
                if (session != null) return;
                list = resolve();
                if (list.entries.IndexOf(entry) < 0) { list = null; return; }
                pointer = e.pointerId;
                press = e.position;
                grip.CapturePointer(pointer);
            }, TrickleDown.TrickleDown);
            // The press stops here only after the grip's own handlers ran: a parent (a card header that
            // folds on click) must not also act on it.
            grip.RegisterCallback<PointerDownEvent>(e =>
            {
                if (list != null) e.StopPropagation();
            });

            grip.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (list == null || e.pointerId != pointer) return;
                if (session == null)
                {
                    if (((Vector2)e.position - press).magnitude < StartDistance) return;
                    session = new Session(list, list.entries.IndexOf(entry), press);
                }
                if (session.list == list) session.Drag(e.position);
                e.StopPropagation();
            }, TrickleDown.TrickleDown);

            grip.RegisterCallback<PointerUpEvent>(e =>
            {
                if (list == null || e.pointerId != pointer) return;
                var pressed = list;
                list = null;
                pointer = -1;
                if (session != null && session.list == pressed)
                {
                    session.Drop();
                    if (grip.HasPointerCapture(e.pointerId)) grip.ReleasePointer(e.pointerId);
                    e.StopImmediatePropagation();
                    return;
                }
                entry.onClick?.Invoke();
            }, TrickleDown.TrickleDown);

            // A plain click keeps the capture until the grip's own handlers (a Button's click) have seen
            // the release, then lets it go.
            grip.RegisterCallback<PointerUpEvent>(e =>
            {
                if (list == null && grip.HasPointerCapture(e.pointerId)) grip.ReleasePointer(e.pointerId);
            });

            grip.RegisterCallback<PointerCaptureOutEvent>(e =>
            {
                if (list == null) return;
                if (session != null && session.list == list && !session.settling) session.Cancel();
                list = null;
                pointer = -1;
            });

            grip.RegisterCallback<DetachFromPanelEvent>(e =>
            {
                if (list != null && session != null && session.list == list && !session.settling) session.Cancel();
                list = null;
                pointer = -1;
            });
        }

        sealed class Session
        {
            public readonly ZuiReorderList list;
            readonly int from;
            readonly Vector2 press;
            readonly VisualElement[] items;
            readonly List<VisualElement>[] followers;
            readonly Rect[] rects;
            readonly Rect bounds;
            readonly float gapMain, gapCross, wrapRight;
            readonly Vector2 origin;
            int target;
            Vector2 offset;
            public bool settling;

            public Session(ZuiReorderList list, int from, Vector2 press)
            {
                this.list = list;
                this.from = from;
                this.press = press;
                target = from;
                int n = list.entries.Count;
                items = new VisualElement[n];
                followers = new List<VisualElement>[n];
                rects = new Rect[n];
                for (int i = 0; i < n; i++)
                {
                    var en = list.entries[i];
                    items[i] = en.item;
                    followers[i] = new List<VisualElement>();
                    if (en.followers != null)
                        foreach (var f in en.followers()) if (f != null) followers[i].Add(f);
                    rects[i] = en.item.worldBound;
                }
                bounds = rects[0];
                foreach (var r in rects) bounds = Rect.MinMaxRect(Mathf.Min(bounds.xMin, r.xMin), Mathf.Min(bounds.yMin, r.yMin),
                    Mathf.Max(bounds.xMax, r.xMax), Mathf.Max(bounds.yMax, r.yMax));
                origin = list.layout == ZuiReorderLayout.Wrap ? new Vector2(bounds.xMin, rects[0].yMin) : rects[0].position;
                wrapRight = bounds.xMax + 0.5f;
                MeasureGaps(out gapMain, out gapCross);

                bool raise = items[from].resolvedStyle.position == Position.Absolute;
                for (int i = 0; i < n; i++)
                {
                    bool dragged = i == from;
                    Each(i, el => el.EnableInClassList("zui-reorder__shifting", !dragged));
                }
                if (raise)
                {
                    // Followers first, so the item itself ends up topmost.
                    foreach (var f in followers[from]) f.BringToFront();
                    items[from].BringToFront();
                }
                items[from].AddToClassList("zui-reorder__dragging");
            }

            void Each(int i, Action<VisualElement> act)
            {
                act(items[i]);
                foreach (var f in followers[i]) act(f);
            }

            void MeasureGaps(out float main, out float cross)
            {
                var along = new List<float>();
                var lines = new List<float>();
                for (int i = 0; i + 1 < rects.Length; i++)
                {
                    Rect a = rects[i], b = rects[i + 1];
                    switch (list.layout)
                    {
                        case ZuiReorderLayout.Vertical: along.Add(b.yMin - a.yMax); break;
                        case ZuiReorderLayout.Horizontal: along.Add(b.xMin - a.xMax); break;
                        default:
                            if (b.yMin >= a.yMax - 0.5f) lines.Add(b.yMin - a.yMax);
                            else along.Add(b.xMin - a.xMax);
                            break;
                    }
                }
                main = Median(along);
                cross = Median(lines);
            }

            static float Median(List<float> v)
            {
                if (v.Count == 0) return 0f;
                v.Sort();
                return Mathf.Max(0f, v[v.Count / 2]);
            }

            /// Top-left of each item (by original index) when the dragged item sits at `slot`.
            Vector2[] Layout(int slot)
            {
                int n = items.Length;
                var order = new List<int>(n);
                for (int i = 0; i < n; i++) if (i != from) order.Add(i);
                order.Insert(slot, from);
                var pos = new Vector2[n];
                Vector2 cursor = origin;
                float lineHeight = 0f;
                bool lineStart = true;
                foreach (int i in order)
                {
                    Rect r = rects[i];
                    switch (list.layout)
                    {
                        case ZuiReorderLayout.Vertical:
                            pos[i] = new Vector2(r.xMin, cursor.y);
                            cursor.y += r.height + gapMain;
                            break;
                        case ZuiReorderLayout.Horizontal:
                            pos[i] = new Vector2(cursor.x, r.yMin);
                            cursor.x += r.width + gapMain;
                            break;
                        default:
                            if (!lineStart && cursor.x + r.width > wrapRight)
                            {
                                cursor = new Vector2(origin.x, cursor.y + lineHeight + gapCross);
                                lineHeight = 0f;
                            }
                            pos[i] = cursor;
                            cursor.x += r.width + gapMain;
                            lineHeight = Mathf.Max(lineHeight, r.height);
                            lineStart = false;
                            break;
                    }
                }
                return pos;
            }

            public void Drag(Vector2 pointer)
            {
                if (settling) return;
                Vector2 delta = pointer - press;
                if (list.layout == ZuiReorderLayout.Vertical) delta.x = 0f;
                if (list.layout == ZuiReorderLayout.Horizontal) delta.y = 0f;
                // Keep the dragged item's centre inside the set's area.
                Rect r = rects[from];
                Vector2 c = r.center + delta;
                c.x = Mathf.Clamp(c.x, bounds.xMin + r.width * 0.5f, Mathf.Max(bounds.xMin + r.width * 0.5f, bounds.xMax - r.width * 0.5f));
                c.y = Mathf.Clamp(c.y, bounds.yMin + r.height * 0.5f, Mathf.Max(bounds.yMin + r.height * 0.5f, bounds.yMax - r.height * 0.5f));
                if (list.layout == ZuiReorderLayout.Vertical) c.x = r.center.x;
                if (list.layout == ZuiReorderLayout.Horizontal) c.y = r.center.y;
                offset = c - r.center;
                Each(from, el => el.style.translate = new Translate(offset.x, offset.y));

                // The landing slot is the one whose predicted place for the dragged item is nearest to it.
                float Distance(int slot) => (Layout(slot)[from] + r.size * 0.5f - c).magnitude;
                int best = target;
                float bestDistance = Distance(target) - 3f; // a little stickiness stops flicker at a boundary
                for (int slot = 0; slot < items.Length; slot++)
                {
                    if (slot == target) continue;
                    float d = Distance(slot);
                    if (d < bestDistance) { best = slot; bestDistance = d; }
                }
                if (best != target || !placed)
                {
                    target = best;
                    placed = true;
                    var pos = Layout(target);
                    for (int i = 0; i < items.Length; i++)
                    {
                        if (i == from) continue;
                        Vector2 t = pos[i] - rects[i].position;
                        Each(i, el => el.style.translate = new Translate(t.x, t.y));
                    }
                }
            }
            bool placed;

            public void Drop()
            {
                settling = true;
                settleFrom = offset;
                settleTo = Layout(target)[from] - rects[from].position;
                dropAt = EditorApplication.timeSinceStartup;
                EditorApplication.update += Tick;
            }
            Vector2 settleFrom, settleTo;
            double dropAt;

            // The dropped item eases into its slot by hand: a transition started in the same frame as the
            // class that declares it does not run.
            void Tick()
            {
                float p = Mathf.Clamp01((float)((EditorApplication.timeSinceStartup - dropAt) * 1000.0 / SlideMs));
                if (p >= 1f) { Commit(); return; }
                float k = 1f - Mathf.Pow(1f - p, 3f);
                Vector2 t = Vector2.LerpUnclamped(settleFrom, settleTo, k);
                Each(from, el => el.style.translate = new Translate(t.x, t.y));
            }

            public void Commit()
            {
                EditorApplication.update -= Tick;
                Reset();
                if (target != from) list.onMoved?.Invoke(from, target);
            }

            public void Cancel()
            {
                EditorApplication.update -= Tick;
                Reset();
            }

            void Reset()
            {
                if (session == this) session = null;
                for (int i = 0; i < items.Length; i++)
                    Each(i, el =>
                    {
                        el.RemoveFromClassList("zui-reorder__shifting");
                        el.style.translate = StyleKeyword.Null;
                    });
                items[from].RemoveFromClassList("zui-reorder__dragging");
            }
        }
    }
}
