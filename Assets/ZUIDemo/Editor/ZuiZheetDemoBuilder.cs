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

        // First box is the "Default" fallback name too.
        sheet.boxes[0].name = "Panel";

        System.IO.Directory.CreateDirectory("Assets/ZUIDemo");
        AssetDatabase.CreateAsset(sheet, Path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Selection.activeObject = sheet;
        Debug.Log($"[ZUIDemo] created demo Zheet with {sheet.boxes.Count} box styles at {Path}");
    }

    static ZUIBoxDef SolidBox(string name, Color c, int radius)
    {
        var b = new ZUIBoxDef { name = name };
        b.background = new ZUIColor(c);
        b.shape = new ZUIShapeDef { cornerRadius = radius, roundTL = true, roundTR = true, roundBL = true, roundBR = true };
        b.showBorder = false;
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
        return b;
    }
}
#endif
