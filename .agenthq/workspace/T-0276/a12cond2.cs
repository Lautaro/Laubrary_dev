// A12 — two follow-ups: Fit per gradient mode (FitReason only excludes ByEdgeDistance), and the Sprite
// primitive's Fit mode / Softness measured on a fixture that CAN show them (soft alpha, non-square box).
var SB = new System.Text.StringBuilder();
var BFs = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
var newLayerM = typeof(Laubrary.Shaper.Editor.ShaperWindow).GetMethod("NewLayer", BFs);

System.Func<ZuiGradient> Grad = () =>
{
    var g = new ZuiGradient(); g.stops.Clear();
    g.stops.Add(new ZuiGradientStop { pos = 0f, color = UnityEngine.Color.yellow });
    g.stops.Add(new ZuiGradientStop { pos = 0.5f, color = UnityEngine.Color.red });
    g.stops.Add(new ZuiGradientStop { pos = 1f, color = new UnityEngine.Color(0f, 0.2f, 1f, 1f) });
    return g;
};
System.Func<Laubrary.Shaper.ShaperDocument> Blank = () =>
{
    var d = UnityEngine.ScriptableObject.CreateInstance<Laubrary.Shaper.ShaperDocument>();
    d.canvasWidth = 96; d.canvasHeight = 64; d.frameCount = 4; d.seed = 7u;
    d.lightRig.lights.Add(new Laubrary.Shaper.ShaperLight { name = "Key", enabled = true });
    return d;
};
Laubrary.Shaper.ShaperDocument DOC = null;
System.Func<UnityEngine.Color32[]> R = () => Laubrary.Shaper.ShaperDocumentRenderer.RenderFrame(DOC, 0);
System.Func<UnityEngine.Color32[], UnityEngine.Color32[], int> Diff = (a, b) =>
{ int n = 0; for (int i = 0; i < a.Length; i++) if (!a[i].Equals(b[i])) n++; return n; };

// ── Fit x gradient mode, and Fit on the two kinds FitReason declares live but the sweep read dead ────
{
    var d = Blank();
    var l = (Laubrary.Shaper.ShaperLayer)newLayerM.Invoke(null, new object[] { "N", d });
    l.root.primitive.kind = Laubrary.Shaper.ShaperPrimitiveKind.Ellipse;
    l.root.primitive.ellipseRxDial = new ZUIValue(34f);
    l.root.primitive.ellipseRyDial = new ZUIValue(13f);
    d.layers.Add(l); DOC = d;
    var f = l.root.fill;
    f.gradient = Grad(); f.kind = Laubrary.Shaper.ShaperFillKind.Gradient;
    SB.Append("-- Fit x gradient mode (34x13 ellipse) --\n");
    foreach (var gm in System.Enum.GetValues(typeof(Laubrary.Shaper.ShaperGradientMode)))
    {
        f.gradientMode = (Laubrary.Shaper.ShaperGradientMode)gm;
        f.fit = Laubrary.Shaper.ShaperFillFit.Uniform;
        var b0 = R();
        f.fit = Laubrary.Shaper.ShaperFillFit.Stretch;
        SB.Append(gm).Append('\t').Append(Diff(b0, R())).Append('\n');
        f.fit = Laubrary.Shaper.ShaperFillFit.Uniform;
    }
    UnityEngine.Object.DestroyImmediate(d);
}

// ── the Sprite primitive, with a SOFT-alpha sprite in a NON-SQUARE box ───────────────────────────────
{
    string DIR = "Assets/Shaper/AuditA12";
    string sp = DIR + "/a12soft.png";
    if (UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Sprite>(sp) == null)
    {
        var t = new UnityEngine.Texture2D(32, 32, UnityEngine.TextureFormat.RGBA32, false);
        for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++)
        {
            float dx = (x - 15.5f) / 15.5f, dy = (y - 15.5f) / 15.5f;
            float r = UnityEngine.Mathf.Sqrt(dx * dx + dy * dy);
            float a = UnityEngine.Mathf.Clamp01(1f - r);          // a smooth alpha ramp, edge to centre
            t.SetPixel(x, y, new UnityEngine.Color(1f, 0.6f, 0.2f, a));
        }
        t.Apply();
        System.IO.File.WriteAllBytes(System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), sp), t.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(t);
        UnityEditor.AssetDatabase.ImportAsset(sp, UnityEditor.ImportAssetOptions.ForceSynchronousImport);
        var imp = (UnityEditor.TextureImporter)UnityEditor.AssetImporter.GetAtPath(sp);
        imp.isReadable = true; imp.filterMode = UnityEngine.FilterMode.Point; imp.mipmapEnabled = false;
        imp.textureCompression = UnityEditor.TextureImporterCompression.Uncompressed;
        imp.npotScale = UnityEditor.TextureImporterNPOTScale.None;
        imp.textureType = UnityEditor.TextureImporterType.Sprite;
        imp.spriteImportMode = UnityEditor.SpriteImportMode.Single;
        imp.SaveAndReimport();
    }
    var spr = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Sprite>(sp);

    var d = Blank();
    var l = (Laubrary.Shaper.ShaperLayer)newLayerM.Invoke(null, new object[] { "S", d });
    l.root.primitive.kind = Laubrary.Shaper.ShaperPrimitiveKind.Sprite;
    d.layers.Add(l); DOC = d;
    var p = l.root.primitive;
    foreach (var fi in p.GetType().GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public))
        if (fi.FieldType == typeof(UnityEngine.Sprite)) fi.SetValue(p, spr);
    p.spriteHalfWDial = new ZUIValue(34f);        // a WIDE box against a square sprite
    p.spriteHalfHDial = new ZUIValue(13f);
    p.spriteThresholdDial = new ZUIValue(0.5f);
    p.spriteSoftnessDial = new ZUIValue(0f);
    p.spriteFitMode = Laubrary.Shaper.ShaperSpriteFitMode.Uniform;
    var b0 = R();
    int lit = 0; foreach (var c in b0) if (c.a > 0) lit++;
    SB.Append("-- Sprite primitive (34x13 box, square soft sprite) lit=").Append(lit).Append(" --\n");
    foreach (var fm in System.Enum.GetValues(typeof(Laubrary.Shaper.ShaperSpriteFitMode)))
    {
        p.spriteFitMode = (Laubrary.Shaper.ShaperSpriteFitMode)fm;
        SB.Append("fitMode ").Append(fm).Append('\t').Append(Diff(b0, R())).Append('\n');
    }
    p.spriteFitMode = Laubrary.Shaper.ShaperSpriteFitMode.Uniform;
    foreach (var s in new float[] { 0f, 1f, 4f, 12f })
    {
        p.spriteSoftnessDial = new ZUIValue(s);
        SB.Append("softness ").Append(s).Append('\t').Append(Diff(b0, R())).Append('\n');
    }
    p.spriteSoftnessDial = new ZUIValue(0f);
    UnityEngine.Object.DestroyImmediate(d);
}
return SB.ToString();
