using System;
using Laubrary.Zui;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.ZTracker.Editor
{
    public sealed partial class ZTrackerModelWindow
    {
        /// <summary>
        /// Drives a mouse drag through the panel the way the editor delivers it: a press on `grip`, `steps`
        /// moves dispatched by position (so pointer capture routes them), then a release. `midDrag` runs
        /// before the release, while the drag is live. The drop's settle is finished before returning.
        /// </summary>
        internal static void PointerDrag(VisualElement grip, Vector2 to, int steps = 6, Action midDrag = null, bool finish = true)
        {
            var root = grip.panel.visualTree;
            Vector2 start = grip.worldBound.center;
            using (var down = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, button = 0, mousePosition = start }))
            { down.target = grip; grip.SendEvent(down); }
            for (int i = 1; i <= steps; i++)
            {
                var at = Vector2.Lerp(start, to, i / (float)steps);
                using (var move = PointerMoveEvent.GetPooled(new Event { type = EventType.MouseDrag, button = 0, mousePosition = at }))
                    root.SendEvent(move);
            }
            midDrag?.Invoke();
            using (var up = PointerUpEvent.GetPooled(new Event { type = EventType.MouseUp, button = 0, mousePosition = to }))
                root.SendEvent(up);
            if (finish) ZuiReorder.FinishPending();
        }

        /// <summary>A press and release in place: a click, not a drag.</summary>
        internal static void PointerClick(VisualElement target)
        {
            var root = target.panel.visualTree;
            Vector2 at = target.worldBound.center;
            using (var down = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, button = 0, mousePosition = at }))
            { down.target = target; target.SendEvent(down); }
            using (var up = PointerUpEvent.GetPooled(new Event { type = EventType.MouseUp, button = 0, mousePosition = at }))
                root.SendEvent(up);
        }

        static float TranslateY(VisualElement e) => e.style.translate.value.y.value;
        static float TranslateX(VisualElement e) => e.style.translate.value.x.value;
    }
}
