// ZUIPopover.cs
// Generic PopupWindowContent for the common case: arbitrary IMGUI content, no persistent internal state
// beyond what the caller closes over. Existing popups with real internal state/animation (color picker,
// gradient-stop editor, icon picker) are NOT migration targets for this — they stay hand-rolled
// PopupWindowContent subclasses. This exists for the case where a tool author would otherwise write a
// one-off PopupWindowContent subclass from scratch just to show a small fixed block of content.
//
// Usage:
//   if (GUILayout.Button("Options", GUILayout.Width(80)))
//       ZUI.Popover(GUILayoutUtility.GetLastRect(), new Vector2(180, 120), () =>
//       {
//           GUILayout.Label("Pick one:");
//           if (GUILayout.Button("A")) { ... }
//       });
//
// Note: like any PopupWindowContent, drawn content here gets NO automatic ZUI sheet scope (see zui.md's
// "Sheets, colours, icons, fonts" section) — wrap the body in `using (ZUI.UseSheet(ZUI.DefaultSheet))`
// yourself if it needs the skinned look.

using System;
using UnityEditor;
using UnityEngine;

public static partial class ZUI
{
    /// <summary>Shows a floating popup anchored to <paramref name="activatorRect"/>, drawing
    /// <paramref name="drawContent"/> every OnGUI pass. For the common "no complex internal state" case —
    /// see the file header for when NOT to reach for this instead.</summary>
    public static void Popover(Rect activatorRect, Vector2 size, Action drawContent)
        => PopupWindow.Show(activatorRect, new ZUIGenericPopoverContent(size, drawContent));

    class ZUIGenericPopoverContent : PopupWindowContent
    {
        readonly Vector2 _size;
        readonly Action _draw;

        public ZUIGenericPopoverContent(Vector2 size, Action draw)
        {
            _size = size;
            _draw = draw;
        }

        public override Vector2 GetWindowSize() => _size;
        public override void OnGUI(Rect rect) => _draw?.Invoke();
    }
}
