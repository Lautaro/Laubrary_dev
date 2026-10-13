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

        /// <summary>
        /// Releases a simulated press. UI Toolkit keeps one shared record of which mouse buttons are down, for every
        /// window; a press a check never releases stays "down" there, and the next real or simulated press in any
        /// window is then delivered as a pointer move instead of a press, so it clicks nothing. Every simulated press
        /// must end with this (or another release). Send it to the panel's root captured BEFORE the press, because the
        /// press may rebuild the element it landed on.
        /// </summary>
        internal static void PointerRelease(VisualElement root, Vector2 at, int button = 0)
        {
            using (var up = PointerUpEvent.GetPooled(new Event { type = EventType.MouseUp, button = button, mousePosition = at }))
                root.SendEvent(up);
        }

        static readonly Type PointerDeviceStateType = typeof(VisualElement).Assembly.GetType("UnityEngine.UIElements.PointerDeviceState");

        /// <summary>The mouse buttons UI Toolkit's shared pointer record holds as down (bit 0 left, bit 1 right), or -1 if unreadable.</summary>
        internal static int HeldMouseButtons()
        {
            var get = PointerDeviceStateType?.GetMethod("GetPressedButtons", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            try { return get == null ? -1 : (int)get.Invoke(null, new object[] { PointerId.mousePointerId }); } catch { return -1; }
        }

        /// <summary>
        /// Clears the shared pointer record before a check that sends whole-window clicks, so a press some earlier code
        /// left unreleased cannot turn the check's first click into a move. Returns what was held before (see
        /// <see cref="HeldMouseButtons"/>) so the check can report it.
        /// </summary>
        internal static int ReleaseHeldMouseButtons()
        {
            int held = HeldMouseButtons();
            if (held > 0)
                try { PointerDeviceStateType.GetMethod("ReleaseAllButtons", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)?.Invoke(null, new object[] { PointerId.mousePointerId }); } catch { }
            return held;
        }

        /// <summary>
        /// What a simulated click is about to do, for a check's failure message: the element and button clicked, where,
        /// the window and panel size, which element the panel reports under that point, and which mouse buttons the
        /// shared pointer record already holds as down.
        /// </summary>
        internal static string CheckClickTrace(UnityEditor.EditorWindow window, VisualElement element, int button, Vector2 at)
        {
            var hit = element.panel?.Pick(at);
            string Describe(VisualElement e) => e == null ? "nothing" : "'" + (string.IsNullOrEmpty(e.name) ? e.GetType().Name : e.name) + "'" + (e is TextElement t && !string.IsNullOrEmpty(t.text) ? " \"" + t.text + "\"" : "");
            var root = window.rootVisualElement;
            return $"clicked {Describe(element)} with button {button} at ({at.x:F0},{at.y:F0}), element rect {element.worldBound}; window {window.position.width:F0}x{window.position.height:F0}, panel {root.worldBound.width:F0}x{root.worldBound.height:F0}; hit {Describe(hit)}{(hit != null && hit != element && !element.Contains(hit) ? " (not the clicked element)" : "")}; mouse buttons already held {HeldMouseButtons()}";
        }

        static float TranslateY(VisualElement e) => e.style.translate.value.y.value;
        static float TranslateX(VisualElement e) => e.style.translate.value.x.value;
    }
}
