using System;
using UnityEngine;

// A named 9-slice frame stored in a Zheet. Holds a source texture and four border insets (in the
// texture's own pixels): the corners stay fixed; the edges and the centre either STRETCH (default) or
// TILE (repeat at native pixel size), chosen PER AXIS. Renders at runtime because it references a
// Texture2D directly. A box/button references one by name (nineSliceId) to use it as its whole frame.
//
// One "tile" is the source's tileable unit — the centre region (texture size minus the insets). So
// SizeForTiles(1,1) equals the source texture's own size; each extra tile adds one centre width/height.
//
// The `texture` is the EDITABLE source reference. When Bake is pressed in the editor, a frozen PNG
// copy is stored in `bakedPng` and drawn instead — so it keeps working if the source is deleted or
// changed, and the Zheet becomes self-contained and portable.
[Serializable]
public class ZUINineSliceDef
{
    public string    name    = "New 9-Slice";
    public Texture2D texture;                    // editable source reference
    public byte[]    bakedPng;                   // baked self-contained copy (survives source loss)
    public int       left, right, top, bottom;   // border insets in source pixels
    public Color     tint    = Color.white;

    public bool tileCenterX;   // repeat the centre horizontally (else stretch)
    public bool tileCenterY;   // repeat the centre vertically
    public bool tileEdgesX;    // repeat the top & bottom edges horizontally
    public bool tileEdgesY;    // repeat the left & right edges vertically
    public bool snapToTiles;   // when drawn, snap the frame so tiled axes hold a whole number of tiles

    [NonSerialized] GUIStyle _style;
    [NonSerialized] Texture2D _builtFor;
    [NonSerialized] Texture2D _baked;
    [NonSerialized] int _builtL, _builtR, _builtT, _builtB;

    public bool IsBaked => bakedPng != null && bakedPng.Length > 0;
    bool AnyTiling => tileCenterX || tileCenterY || tileEdgesX || tileEdgesY;

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

    // Clamped insets + centre-tile size for the active texture. False when there's no texture.
    bool Metrics(out Texture2D tex, out int L, out int R, out int T, out int B, out float cW, out float cH)
    {
        tex = ActiveTexture; L = R = T = B = 0; cW = cH = 0f;
        if (tex == null) return false;
        float tw = tex.width, th = tex.height;
        L = Mathf.Clamp(left, 0, (int)tw); R = Mathf.Clamp(right, 0, (int)tw - L);
        T = Mathf.Clamp(top,  0, (int)th); B = Mathf.Clamp(bottom, 0, (int)th - T);
        cW = tw - L - R; cH = th - T - B;
        return true;
    }

    /// <summary>The size at which the centre holds exactly tilesX × tilesY whole tiles. SizeForTiles(1,1)
    /// is the source texture's own size. Use this to size a frame by multiples of the original.</summary>
    public Vector2 SizeForTiles(int tilesX, int tilesY)
    {
        tilesX = Mathf.Max(1, tilesX); tilesY = Mathf.Max(1, tilesY);
        if (!Metrics(out _, out int L, out int R, out int T, out int B, out float cW, out float cH))
            return new Vector2(tilesX, tilesY);
        return new Vector2(L + R + tilesX * cW, T + B + tilesY * cH);
    }

    /// <summary>A rect anchored at topLeft sized to hold tilesX × tilesY whole tiles.</summary>
    public Rect RectForTiles(Vector2 topLeft, int tilesX, int tilesY)
    {
        var s = SizeForTiles(tilesX, tilesY);
        return new Rect(topLeft.x, topLeft.y, s.x, s.y);
    }

    /// <summary>Round a desired size to the nearest whole-tile size on each axis that tiles (a stretched
    /// axis is left untouched). This is what "snap to whole tiles" applies when resizing by code.</summary>
    public Vector2 SnapSize(Vector2 desired)
    {
        if (!Metrics(out _, out int L, out int R, out int T, out int B, out float cW, out float cH))
            return desired;
        float w = desired.x, h = desired.y;
        if ((tileCenterX || tileEdgesX) && cW > 0.5f)
            w = L + R + Mathf.Max(1, Mathf.RoundToInt((desired.x - L - R) / cW)) * cW;
        if ((tileCenterY || tileEdgesY) && cH > 0.5f)
            h = T + B + Mathf.Max(1, Mathf.RoundToInt((desired.y - T - B) / cH)) * cH;
        return new Vector2(w, h);
    }

    /// <summary>Draw the 9-slice frame into rect (Repaint only). Corners fixed; edges/centre stretch or
    /// tile per axis. With snapToTiles on, the drawn size is rounded to whole tiles automatically.</summary>
    public void DrawFrame(Rect rect)
    {
        if (Event.current == null || Event.current.type != EventType.Repaint) return;
        var tex = ActiveTexture;
        if (tex == null || rect.width <= 1f || rect.height <= 1f) return;

        if (snapToTiles)
        {
            var s = SnapSize(new Vector2(rect.width, rect.height));
            rect = new Rect(rect.x, rect.y, s.x, s.y);
        }

        var prev = GUI.color;
        GUI.color = tint;
        if (!AnyTiling) Style().Draw(rect, false, false, false, false); // fast stretch path
        else DrawSliced(rect, tex);
        GUI.color = prev;
    }

    // Manual 9-region draw with per-axis stretch/tile. Corners always native; edges tile along their
    // own axis; the centre tiles per axis. GUI.DrawTextureWithTexCoords maps a UV sub-rect (y-up) to a
    // screen rect (y-down) and flips for us.
    void DrawSliced(Rect r, Texture2D tex)
    {
        float tw = tex.width, th = tex.height;
        int L = Mathf.Clamp(left, 0, (int)tw), R = Mathf.Clamp(right, 0, (int)tw - L);
        int T = Mathf.Clamp(top, 0, (int)th),  B = Mathf.Clamp(bottom, 0, (int)th - T);
        float cWpx = tw - L - R, cHpx = th - T - B; // native centre/edge tile sizes

        // Dest bands, clamped so corners never overlap on a small rect.
        float x1 = Mathf.Min(r.x + L, r.x + r.width * 0.5f);
        float x2 = Mathf.Max(r.xMax - R, x1);
        float y1 = Mathf.Min(r.y + T, r.y + r.height * 0.5f);
        float y2 = Mathf.Max(r.yMax - B, y1);

        // Source UV fractions (v flipped: screen top → high UV).
        float uL = L / tw, uR = 1f - R / tw, vT = 1f - T / th, vB = B / th;

        // Corners.
        DrawUV(Rect.MinMaxRect(r.x, r.y, x1, y1), tex, new Rect(0f, vT, uL, T / th));
        DrawUV(Rect.MinMaxRect(x2, r.y, r.xMax, y1), tex, new Rect(uR, vT, R / tw, T / th));
        DrawUV(Rect.MinMaxRect(r.x, y2, x1, r.yMax), tex, new Rect(0f, 0f, uL, vB));
        DrawUV(Rect.MinMaxRect(x2, y2, r.xMax, r.yMax), tex, new Rect(uR, 0f, R / tw, vB));

        // Edges — top/bottom tile on X, left/right tile on Y.
        FillH(Rect.MinMaxRect(x1, r.y, x2, y1), tex, new Rect(uL, vT, uR - uL, T / th), tileEdgesX, cWpx); // top
        FillH(Rect.MinMaxRect(x1, y2, x2, r.yMax), tex, new Rect(uL, 0f, uR - uL, vB), tileEdgesX, cWpx);   // bottom
        FillV(Rect.MinMaxRect(r.x, y1, x1, y2), tex, new Rect(0f, vB, uL, vT - vB), tileEdgesY, cHpx);      // left
        FillV(Rect.MinMaxRect(x2, y1, r.xMax, y2), tex, new Rect(uR, vB, R / tw, vT - vB), tileEdgesY, cHpx); // right

        // Centre — tile per axis (stretch the other).
        var cSrc = new Rect(uL, vB, uR - uL, vT - vB);
        var cDst = Rect.MinMaxRect(x1, y1, x2, y2);
        if (tileCenterX && tileCenterY) Fill2D(cDst, tex, cSrc, cWpx, cHpx);
        else if (tileCenterX)           FillH(cDst, tex, cSrc, true, cWpx);
        else if (tileCenterY)           FillV(cDst, tex, cSrc, true, cHpx);
        else                            DrawUV(cDst, tex, cSrc);
    }

    static void DrawUV(Rect dst, Texture tex, Rect uv)
    {
        if (dst.width <= 0f || dst.height <= 0f) return;
        GUI.DrawTextureWithTexCoords(dst, tex, uv, true);
    }

    // Stretch (one draw) or tile a strip horizontally at native tile width.
    static void FillH(Rect dst, Texture tex, Rect uv, bool tile, float tileW)
    {
        if (!tile || tileW <= 0.5f) { DrawUV(dst, tex, uv); return; }
        for (float x = dst.x; x < dst.xMax - 0.5f; x += tileW)
        {
            float w = Mathf.Min(tileW, dst.xMax - x);
            DrawUV(new Rect(x, dst.y, w, dst.height), tex, new Rect(uv.x, uv.y, uv.width * (w / tileW), uv.height));
        }
    }

    // Stretch or tile a strip vertically. Partial last tile shows the TOP of the source (high UV).
    static void FillV(Rect dst, Texture tex, Rect uv, bool tile, float tileH)
    {
        if (!tile || tileH <= 0.5f) { DrawUV(dst, tex, uv); return; }
        for (float y = dst.y; y < dst.yMax - 0.5f; y += tileH)
        {
            float h = Mathf.Min(tileH, dst.yMax - y);
            float f = h / tileH;
            DrawUV(new Rect(dst.x, y, dst.width, h), tex, new Rect(uv.x, uv.yMax - uv.height * f, uv.width, uv.height * f));
        }
    }

    static void Fill2D(Rect dst, Texture tex, Rect uv, float tileW, float tileH)
    {
        if (tileW <= 0.5f || tileH <= 0.5f) { DrawUV(dst, tex, uv); return; }
        for (float y = dst.y; y < dst.yMax - 0.5f; y += tileH)
        {
            float h = Mathf.Min(tileH, dst.yMax - y), fy = h / tileH;
            var rowUv = new Rect(uv.x, uv.yMax - uv.height * fy, uv.width, uv.height * fy);
            for (float x = dst.x; x < dst.xMax - 0.5f; x += tileW)
            {
                float w = Mathf.Min(tileW, dst.xMax - x);
                DrawUV(new Rect(x, y, w, h), tex, new Rect(rowUv.x, rowUv.y, rowUv.width * (w / tileW), rowUv.height));
            }
        }
    }

    // Cached GUIStyle for the fast stretch-only path. Border is clamped so it never renders degenerate.
    GUIStyle Style()
    {
        var tex = ActiveTexture;
        if (_style == null || _builtFor != tex || _builtL != left || _builtR != right || _builtT != top || _builtB != bottom)
        {
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

    public void Invalidate() { _style = null; _baked = null; }
}
