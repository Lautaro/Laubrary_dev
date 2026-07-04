using UnityEngine;

// TEMP verification of the centre size scaler — safe to delete. Same texture & insets, centre tiled
// H+V, drawn at three scales so the tile size difference is obvious.
public class ScaleTestHud : MonoBehaviour
{
    Texture2D _tex;
    ZUINineSliceDef _half, _one, _two;

    void Build()
    {
        const int size = 48, inset = 12;
        _tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Point };
        var a = new Color(0.85f, 0.86f, 0.92f);
        var b = new Color(0.20f, 0.24f, 0.34f);
        var px = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                bool bx = x < inset || x >= size - inset, by = y < inset || y >= size - inset;
                Color c = (bx && by) ? new Color(0.95f, 0.55f, 0.15f)                 // solid corners
                                     : (((x / 4) + (y / 4)) % 2 == 0 ? a : b);        // 4px checker
                px[y * size + x] = c;
            }
        _tex.SetPixels(px); _tex.Apply();

        _half = Def(0.5f); _one = Def(1f); _two = Def(2f);
    }

    ZUINineSliceDef Def(float scale)
    {
        var d = new ZUINineSliceDef { texture = _tex, left = 12, right = 12, top = 12, bottom = 12 };
        d.tileCenterX = true; d.tileCenterY = true;   // tile centre both axes
        d.centerScale = scale;
        return d;
    }

    void OnGUI()
    {
        if (_tex == null) Build();
        Label(40, 24, "Centre size scaler — centre tiled H+V, same texture");
        Label(40, 64, "Scale 0.5  (smaller tiles, more repeats)"); _half.DrawFrame(new Rect(40, 84, 600, 90));
        Label(40, 194, "Scale 1.0  (native)");                     _one.DrawFrame(new Rect(40, 214, 600, 90));
        Label(40, 324, "Scale 2.0  (larger tiles, fewer repeats)"); _two.DrawFrame(new Rect(40, 344, 600, 90));
    }

    static void Label(float x, float y, string t)
    {
        var s = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold };
        s.normal.textColor = Color.white;
        GUI.Label(new Rect(x, y, 700, 20), t, s);
    }
}
