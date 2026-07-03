#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

// Builds a small demo Zheet (ZUIStyleSheetAsset) with a few box styles, so the runtime test HUD has
// something to draw. This authors the sheet in code; normally you'd use the ZUI Style Editor. The
// point is to prove a Zheet's box styles render at RUNTIME via ZUISheet.DrawBox.
public static class ZuiZheetDemoBuilder
{
    const string Path = "Assets/ZUIDemo/DemoRuntimeZheet.asset";

    [MenuItem("ZUIDemo/Create Demo Runtime Zheet")]
    public static void Build()
    {
        var sheet = ScriptableObject.CreateInstance<ZUIStyleSheetAsset>();
        sheet.boxes.Clear();

        sheet.boxes.Add(SolidBox("Panel", new Color(0.13f, 0.14f, 0.18f, 0.96f), 22));
        sheet.boxes.Add(GradientBox("Card", new Color(0.24f, 0.27f, 0.36f), new Color(0.13f, 0.14f, 0.20f), 28,
            border: new Color(1f, 1f, 1f, 0.18f)));
        sheet.boxes.Add(SolidBox("Accent", new Color(0.20f, 0.45f, 0.78f), 16));
        sheet.boxes.Add(GradientBox("Danger", new Color(0.62f, 0.20f, 0.20f), new Color(0.40f, 0.12f, 0.12f), 40,
            border: new Color(1f, 0.5f, 0.5f, 0.30f)));

        // ── 9-slice: generate a frame texture asset, add a named 9-slice, and a box that uses it. ──
        var frameTex = SaveFrameTexture("Assets/ZUIDemo/DemoFrame.png", 64, 20);
        sheet.nineSlices.Clear();
        sheet.nineSlices.Add(new ZUINineSliceDef { name = "DemoFrame", texture = frameTex, left = 20, right = 20, top = 20, bottom = 20 });
        var framed = new ZUIBoxDef { name = "Framed", nineSliceId = "DemoFrame" };
        sheet.boxes.Add(framed);

        // Stretch-vs-tile proof: one patterned texture, two boxes — same insets, different fill mode.
        // The pattern (striped edges, checker centre) makes the difference obvious when drawn wide.
        var patTex = SavePatternTexture("Assets/ZUIDemo/DemoPattern.png", 48, 12);
        sheet.nineSlices.Add(new ZUINineSliceDef { name = "PatStretch", texture = patTex, left = 12, right = 12, top = 12, bottom = 12, tileCenter = false, tileEdges = false });
        sheet.nineSlices.Add(new ZUINineSliceDef { name = "PatTile",    texture = patTex, left = 12, right = 12, top = 12, bottom = 12, tileCenter = true,  tileEdges = true });
        sheet.boxes.Add(new ZUIBoxDef { name = "PatStretch", nineSliceId = "PatStretch" });
        sheet.boxes.Add(new ZUIBoxDef { name = "PatTile",    nineSliceId = "PatTile" });

        // A 9-slice (sprite) BUTTON: three frames sharing the texture, tinted per state, referenced
        // per state by the button def. Hover brightens, press darkens.
        sheet.nineSlices.Add(new ZUINineSliceDef { name = "BtnNormal",  texture = frameTex, left = 20, right = 20, top = 20, bottom = 20, tint = new Color(0.82f, 0.84f, 0.92f) });
        sheet.nineSlices.Add(new ZUINineSliceDef { name = "BtnHover",   texture = frameTex, left = 20, right = 20, top = 20, bottom = 20, tint = Color.white });
        sheet.nineSlices.Add(new ZUINineSliceDef { name = "BtnPressed", texture = frameTex, left = 20, right = 20, top = 20, bottom = 20, tint = new Color(0.55f, 0.56f, 0.64f) });
        var spriteBtn = new ZUIButtonDef
        {
            name = "SpriteButton",
            nineSliceNormal = "BtnNormal", nineSliceHover = "BtnHover", nineSliceActive = "BtnPressed",
            text = new ZUITextDef(Color.white) { fontSize = 16, fontStyle = FontStyle.Bold },
        };
        sheet.buttons.Add(spriteBtn);

        // First box is the "Default" fallback name too.
        sheet.boxes[0].name = "Panel";

        System.IO.Directory.CreateDirectory("Assets/ZUIDemo");
        AssetDatabase.CreateAsset(sheet, Path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Selection.activeObject = sheet;
        Debug.Log($"[ZUIDemo] created demo Zheet with {sheet.boxes.Count} box styles at {Path}");
    }

    // Generate a rounded panel-frame texture (dark fill + a lighter inner ring) and import it readable
    // so a 9-slice can stretch it. Border insets = corner radius, so the rounded corners stay fixed.
    static Texture2D SaveFrameTexture(string path, int size, int radius)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var fill   = new Color(0.16f, 0.18f, 0.24f, 0.98f);
        var ring   = new Color(0.55f, 0.72f, 1f, 0.9f);
        float r = radius;
        var px = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                // signed distance to a rounded-rect edge (negative = inside)
                float dx = Mathf.Max(Mathf.Abs(x + 0.5f - size * 0.5f) - (size * 0.5f - r), 0f);
                float dy = Mathf.Max(Mathf.Abs(y + 0.5f - size * 0.5f) - (size * 0.5f - r), 0f);
                float d = Mathf.Sqrt(dx * dx + dy * dy) - r;
                Color c;
                if (d > 0.5f) c = new Color(0, 0, 0, 0);              // outside
                else if (d > -3f) c = ring;                          // 3px inner ring (the visible frame)
                else c = fill;                                       // interior fill
                if (d > -0.5f && d <= 0.5f) c.a *= Mathf.Clamp01(0.5f - d); // 1px AA on the outer edge
                px[y * size + x] = c;
            }
        tex.SetPixels(px); tex.Apply();

        System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        ti.textureType = TextureImporterType.Default;
        ti.isReadable = true;
        ti.filterMode = FilterMode.Bilinear;
        ti.wrapMode = TextureWrapMode.Clamp;
        ti.mipmapEnabled = false;
        ti.textureCompression = TextureImporterCompression.Uncompressed;
        ti.alphaIsTransparency = true;
        ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // A frame whose regions carry a visible pattern so stretch-vs-tile is unmistakable when drawn wide:
    // solid corners, striped edges (perpendicular to the edge), a checker centre.
    static Texture2D SavePatternTexture(string path, int size, int inset)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var corner = new Color(0.95f, 0.55f, 0.15f);   // orange corners
        var stripeA = new Color(0.30f, 0.65f, 0.95f);  // edge stripes
        var stripeB = new Color(0.12f, 0.22f, 0.35f);
        var checkA = new Color(0.85f, 0.85f, 0.90f);   // centre checker
        var checkB = new Color(0.20f, 0.22f, 0.30f);
        var px = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                bool left = x < inset, right = x >= size - inset;
                bool bot = y < inset, top = y >= size - inset;
                Color c;
                if ((left || right) && (top || bot)) c = corner;                 // corners
                else if (left || right) c = ((y / 3) % 2 == 0) ? stripeA : stripeB; // L/R edges: horizontal stripes
                else if (top || bot)    c = ((x / 3) % 2 == 0) ? stripeA : stripeB; // T/B edges: vertical stripes
                else c = (((x / 4) + (y / 4)) % 2 == 0) ? checkA : checkB;        // centre: checker
                px[y * size + x] = c;
            }
        tex.SetPixels(px); tex.Apply();
        System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        ti.textureType = TextureImporterType.Default;
        ti.isReadable = true; ti.filterMode = FilterMode.Point; ti.wrapMode = TextureWrapMode.Clamp;
        ti.mipmapEnabled = false; ti.textureCompression = TextureImporterCompression.Uncompressed;
        ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static ZUIBoxDef SolidBox(string name, Color c, int radius)
    {
        var b = new ZUIBoxDef { name = name };
        b.background = new ZUIColor(c);
        b.shape = new ZUIShapeDef { cornerRadius = radius, roundTL = true, roundTR = true, roundBL = true, roundBR = true };
        b.showBorder = false;
        WithText(b);
        return b;
    }

    static ZUIBoxDef GradientBox(string name, Color a, Color bCol, int radius, Color border)
    {
        var b = new ZUIBoxDef { name = name };
        b.background = new ZUIColor(a, bCol, 90f);
        b.background.isGradient = true;
        b.shape = new ZUIShapeDef { cornerRadius = radius, roundTL = true, roundTR = true, roundBL = true, roundBR = true };
        b.showBorder = true;
        b.border = new ZUIBorderDef { color = new ZUIColor(border), edgeWidth = new ZUIEdgeValuesFloat(1.5f) };
        WithText(b);
        return b;
    }

    // Give the box distinct title/content text styles so they're visibly different at runtime
    // (bold ~19px title, dimmer ~13px content) — proving the Zheet's text properties apply.
    static void WithText(ZUIBoxDef b)
    {
        b.titleText   = new ZUITextDef(new Color(1f, 1f, 1f, 0.95f))   { fontSize = 19, fontStyle = FontStyle.Bold };
        b.contentText = new ZUITextDef(new Color(0.78f, 0.82f, 0.9f))  { fontSize = 13 };
    }
}
#endif
