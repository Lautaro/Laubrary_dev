// ZUIPositionPad.cs
// A small, compact 2D drag-pad for a PLAIN Vector2 (screen/pixel-space offsets, UV nudges, anything that isn't
// an animatable per-frame value). ZUIValue2DControl already solves "drag to aim a position" for the
// animatable ZUIValue pair case (Pyre layer offsets, etc.) — this is its lightweight cousin for the much more
// common case of a single static setting that just needs "drag a dot in a box" instead of two plain float
// fields. Deliberately tiny: no numeric readout, no curve mode, no reset button — just the pad. Compose it
// with other controls (an object picker, a MicroSlider) via ZUI.HRow for a compact combined widget.

using UnityEditor;
using UnityEngine;

public static partial class ZUI
{
    static readonly Color PositionPadPointColor = new Color(0.4f, 0.85f, 1f);

    /// <summary>
    /// Draws a square drag-pad of `size` points and returns the (possibly updated) value. `range` bounds the
    /// value on both axes — dragging to the pad's edge yields `range.xMin/xMax`/`range.yMin/yMax`.
    /// `flipY` (default true) makes dragging UP increase Y — the natural feel for a fresh "position" value
    /// with no prior convention. Pass `flipY: false` when wiring this to a value that already has an
    /// ESTABLISHED Y-down convention elsewhere (e.g. a field some other, already-shipped screen-space drag
    /// interaction reads directly) — matching that convention matters more than the pad's own default feel,
    /// since the same field being visually inverted between two different controls editing it is far more
    /// confusing than the pad alone not feeling "natural" in isolation.
    /// </summary>
    public static Vector2 PositionPad(Vector2 value, Rect range, float size = 56f, bool flipY = true)
    {
        Rect rect = GUILayoutUtility.GetRect(size, size, GUILayout.Width(size), GUILayout.Height(size));
        return PositionPad(rect, value, range, flipY);
    }

    public static Vector2 PositionPad(Rect rect, Vector2 value, Rect range, bool flipY = true)
    {
        var e = Event.current;
        int id = GUIUtility.GetControlID(FocusType.Passive, rect);
        Vector2 result = value;

        bool isDrag = GUIUtility.hotControl == id;
        switch (e.type)
        {
            case EventType.MouseDown:
                if (e.button == 0 && rect.Contains(e.mousePosition))
                {
                    GUIUtility.hotControl = id;
                    result = SamplePad(e.mousePosition, rect, range, flipY);
                    GUI.changed = true;
                    e.Use();
                }
                break;
            case EventType.MouseDrag:
                if (isDrag)
                {
                    result = SamplePad(e.mousePosition, rect, range, flipY);
                    GUI.changed = true;
                    e.Use();
                }
                break;
            case EventType.MouseUp:
                if (isDrag) { GUIUtility.hotControl = 0; e.Use(); }
                break;
            case EventType.Repaint:
                EditorGUI.DrawRect(rect, new Color(0f, 0f, 0f, 0.25f));
                // Centre crosshair — a fixed reference so "dead centre" (usually the default) is easy to land on.
                var cross = new Color(1f, 1f, 1f, 0.15f);
                EditorGUI.DrawRect(new Rect(rect.center.x - 0.5f, rect.y, 1f, rect.height), cross);
                EditorGUI.DrawRect(new Rect(rect.x, rect.center.y - 0.5f, rect.width, 1f), cross);

                Vector2 p = PadToScreen(result, rect, range, flipY);
                Handles.color = PositionPadPointColor;
                Handles.DrawSolidDisc(new Vector3(p.x, p.y, 0f), Vector3.forward, 3f);
                break;
        }
        return result;
    }

    static Vector2 SamplePad(Vector2 mousePos, Rect rect, Rect range, bool flipY)
    {
        float tx = Mathf.InverseLerp(rect.x, rect.xMax, mousePos.x);
        float ty = Mathf.InverseLerp(rect.y, rect.yMax, mousePos.y);   // screen Y-down
        float x = Mathf.Lerp(range.xMin, range.xMax, tx);
        float y = flipY ? Mathf.Lerp(range.yMax, range.yMin, ty) : Mathf.Lerp(range.yMin, range.yMax, ty);
        return new Vector2(x, y);
    }

    static Vector2 PadToScreen(Vector2 value, Rect rect, Rect range, bool flipY)
    {
        float tx = Mathf.InverseLerp(range.xMin, range.xMax, value.x);
        float ty = Mathf.InverseLerp(range.yMin, range.yMax, value.y);
        float py = flipY ? rect.yMax - ty * rect.height : rect.y + ty * rect.height;
        return new Vector2(rect.x + tx * rect.width, py);
    }
}
