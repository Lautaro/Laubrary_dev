// ZuiReorder — drag-to-reorder for a vertical list of row elements (the "≡ grip" pattern the IMGUI
// windows hand-rolled at least three times: Pyre's layer list, its modifier stacks, Mirage's clip
// sequence). Wire each row's grip with MakeGrip; the helper draws an insertion line while dragging
// and reports (from, to) on drop — the caller mutates its data list and rebuilds.
using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zui
{
    public static class ZuiReorder
    {
        /// Make `grip` drag-reorder `row` among the direct children of `container`.
        /// `onMoved(from, to)` fires on drop with the standard list-move semantics: remove at
        /// `from`, then insert at `to` (already adjusted for the removal). Only fires on a real move.
        public static void MakeGrip(VisualElement grip, VisualElement row, VisualElement container,
            Action<int, int> onMoved)
        {
            VisualElement line = null;
            int pressedIndex = -1;

            void RemoveLine()
            {
                line?.RemoveFromHierarchy();
                line = null;
            }

            grip.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0) return;
                pressedIndex = container.IndexOf(row);
                if (pressedIndex < 0) return;
                grip.CapturePointer(e.pointerId);
                row.AddToClassList("zui-reorder__dragging");
                e.StopPropagation();
            });

            grip.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (pressedIndex < 0 || !grip.HasPointerCapture(e.pointerId)) return;
                int target = TargetIndex(container, e.position.y);

                if (line == null)
                {
                    line = new VisualElement();
                    line.AddToClassList("zui-reorder__line");
                    line.style.position = Position.Absolute;
                    line.style.left = 0f;
                    line.style.right = 0f;
                    line.style.height = 2f;
                    line.pickingMode = PickingMode.Ignore;
                    container.Add(line);
                }
                float y = target < container.childCount && container[target] != line
                    ? container[target].layout.yMin
                    : ContentBottom(container, line);
                line.style.top = y - 1f;
                e.StopPropagation();
            });

            grip.RegisterCallback<PointerUpEvent>(e =>
            {
                if (pressedIndex < 0) return;
                if (grip.HasPointerCapture(e.pointerId)) grip.ReleasePointer(e.pointerId);
                row.RemoveFromClassList("zui-reorder__dragging");
                int from = pressedIndex;
                pressedIndex = -1;
                int target = TargetIndex(container, e.position.y);
                RemoveLine();
                if (target != from && target != from + 1)
                {
                    int to = target > from ? target - 1 : target;
                    onMoved?.Invoke(from, to);
                }
                e.StopPropagation();
            });
        }

        static int TargetIndex(VisualElement container, float worldY)
        {
            int i = 0;
            foreach (var child in container.Children())
            {
                if (child.ClassListContains("zui-reorder__line")) continue;
                if (worldY < child.worldBound.center.y) return i;
                i++;
            }
            return i;
        }

        static float ContentBottom(VisualElement container, VisualElement ignore)
        {
            float max = 0f;
            foreach (var child in container.Children())
                if (child != ignore) max = Mathf.Max(max, child.layout.yMax);
            return max;
        }
    }
}
