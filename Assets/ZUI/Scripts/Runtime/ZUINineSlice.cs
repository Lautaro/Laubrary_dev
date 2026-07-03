using System;
using UnityEngine;

// A named 9-slice frame stored in a Zheet. Holds a source texture and four border insets (in the
// texture's own pixels): the corners stay fixed, the edges stretch, the center fills — native IMGUI
// 9-slicing via GUIStyle.border. Because it references a Texture2D directly (not a path), it renders
// at runtime. A box/button references one by name (nineSliceId) to use it as its whole frame.
[Serializable]
public class ZUINineSliceDef
{
    public string    name    = "New 9-Slice";
    public Texture2D texture;
    public int       left, right, top, bottom;   // border insets in source pixels
    public Color     tint    = Color.white;

    [NonSerialized] GUIStyle _style;
    [NonSerialized] Texture2D _builtFor;
    [NonSerialized] int _builtL, _builtR, _builtT, _builtB;

    // Cached GUIStyle whose border drives the 9-slice of the background texture. Rebuilt when the
    // texture or any border inset changes (so the Zeditor sees edits live).
    GUIStyle Style()
    {
        if (_style == null || _builtFor != texture || _builtL != left || _builtR != right || _builtT != top || _builtB != bottom)
        {
            _style = new GUIStyle { border = new RectOffset(left, right, top, bottom) };
            _style.normal.background = texture;
            _builtFor = texture; _builtL = left; _builtR = right; _builtT = top; _builtB = bottom;
        }
        return _style;
    }

    /// <summary>Draw the 9-slice frame into rect (Repaint only). Corners fixed, edges stretched.</summary>
    public void DrawFrame(Rect rect)
    {
        if (Event.current == null || Event.current.type != EventType.Repaint) return;
        if (texture == null || rect.width <= 1f) return;
        var prev = GUI.color;
        GUI.color = tint;
        Style().Draw(rect, false, false, false, false);
        GUI.color = prev;
    }

    public void Invalidate() { _style = null; }
}
