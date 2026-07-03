using System;
using UnityEngine;

// A named 9-slice frame stored in a Zheet. Holds a source texture and four border insets (in the
// texture's own pixels): the corners stay fixed, the edges stretch, the center fills — native IMGUI
// 9-slicing via GUIStyle.border. Because it references a Texture2D directly (not a path), it renders
// at runtime. A box/button references one by name (nineSliceId) to use it as its whole frame.
//
// The `texture` is the EDITABLE source reference. When Bake is pressed in the editor, a frozen PNG
// copy is stored in `bakedPng`, and from then on the frame draws from that copy — so it keeps working
// if the source is deleted or changed, and the Zheet becomes fully self-contained and portable.
[Serializable]
public class ZUINineSliceDef
{
    public string    name    = "New 9-Slice";
    public Texture2D texture;                    // editable source reference
    public byte[]    bakedPng;                   // baked self-contained copy (survives source loss)
    public int       left, right, top, bottom;   // border insets in source pixels
    public Color     tint    = Color.white;

    [NonSerialized] GUIStyle _style;
    [NonSerialized] Texture2D _builtFor;
    [NonSerialized] Texture2D _baked;
    [NonSerialized] int _builtL, _builtR, _builtT, _builtB;

    public bool IsBaked => bakedPng != null && bakedPng.Length > 0;

    // The texture actually drawn: the baked copy if present (rebuilt once from bytes — works in a
    // player), otherwise the live source reference.
    public Texture2D ActiveTexture
    {
        get
        {
            if (IsBaked)
            {
                if (_baked == null)
                {
                    _baked = new Texture2D(2, 2, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                    _baked.LoadImage(bakedPng);
                }
                return _baked;
            }
            return texture;
        }
    }

    // Cached GUIStyle whose border drives the 9-slice of the active texture. Rebuilt when the texture
    // or any border inset changes (so the Zeditor sees edits live).
    GUIStyle Style()
    {
        var tex = ActiveTexture;
        if (_style == null || _builtFor != tex || _builtL != left || _builtR != right || _builtT != top || _builtB != bottom)
        {
            // Clamp so opposite insets never cross the texture — an over-set border would otherwise
            // render degenerate; the editor still flags the raw values as invalid (red).
            int cl = left, cr = right, ct = top, cb = bottom;
            if (tex != null)
            {
                cl = Mathf.Clamp(cl, 0, Mathf.Max(0, tex.width  - 1));
                cr = Mathf.Clamp(cr, 0, Mathf.Max(0, tex.width  - 1 - cl));
                ct = Mathf.Clamp(ct, 0, Mathf.Max(0, tex.height - 1));
                cb = Mathf.Clamp(cb, 0, Mathf.Max(0, tex.height - 1 - ct));
            }
            _style = new GUIStyle { border = new RectOffset(cl, cr, ct, cb) };
            _style.normal.background = tex;
            _builtFor = tex; _builtL = left; _builtR = right; _builtT = top; _builtB = bottom;
        }
        return _style;
    }

    /// <summary>Draw the 9-slice frame into rect (Repaint only). Corners fixed, edges stretched.</summary>
    public void DrawFrame(Rect rect)
    {
        if (Event.current == null || Event.current.type != EventType.Repaint) return;
        if (ActiveTexture == null || rect.width <= 1f) return;
        var prev = GUI.color;
        GUI.color = tint;
        Style().Draw(rect, false, false, false, false);
        GUI.color = prev;
    }

    public void Invalidate() { _style = null; _baked = null; }
}
