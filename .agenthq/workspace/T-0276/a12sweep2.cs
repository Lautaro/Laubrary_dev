// A12 (T-0276) — the shapes and fills the earlier cards did not measure. Each fixture is a document
// built the way the window builds one, SAVED, re-imported and re-loaded; then every dial on the node's
// own card is turned once in BOTH directions and the changed pixels are counted at three frames.
string WHICH = "mask";

var SB = new System.Text.StringBuilder();
var BFs = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
var BFi = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public;
string DIR = "Assets/Shaper/AuditA12";

if (!UnityEditor.AssetDatabase.IsValidFolder("Assets/Shaper"))
    UnityEditor.AssetDatabase.CreateFolder("Assets", "Shaper");
if (!UnityEditor.AssetDatabase.IsValidFolder(DIR))
    UnityEditor.AssetDatabase.CreateFolder("Assets/Shaper", "AuditA12");

var newLayerM  = typeof(Laubrary.Shaper.Editor.ShaperWindow).GetMethod("NewLayer", BFs);
var newMemberM = typeof(Laubrary.Shaper.Editor.ShaperWindow).GetMethod("NewBagMember", BFs);
System.Func<string, Laubrary.Shaper.ShaperDocument, Laubrary.Shaper.ShaperLayer> NewLayer =
    (n, d) => (Laubrary.Shaper.ShaperLayer)newLayerM.Invoke(null, new object[] { n, d });
System.Func<string, Laubrary.Shaper.ShaperDocument, Laubrary.Shaper.ShaperNode> NewMember =
    (n, d) => (Laubrary.Shaper.ShaperNode)newMemberM.Invoke(null, new object[] { n, d });

// ── a real sprite sheet asset with an alpha shape, so Texture fill and Sprite primitive have content ──
string sheetPath = DIR + "/a12sheet.png";
if (UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(sheetPath) == null)
{
    var t = new UnityEngine.Texture2D(32, 32, UnityEngine.TextureFormat.RGBA32, false);
    var cols = new UnityEngine.Color[] { UnityEngine.Color.red, UnityEngine.Color.green,
                                         UnityEngine.Color.blue, UnityEngine.Color.yellow };
    for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++)
    {
        var c = cols[(x / 8) % 4];
        if (((x % 8) + y) % 4 == 0) c = UnityEngine.Color.white;
        if (x < 5 || x > 26 || y < 5 || y > 26) c = new UnityEngine.Color(c.r, c.g, c.b, 0f);
        t.SetPixel(x, y, c);
    }
    t.Apply();
    System.IO.File.WriteAllBytes(System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), sheetPath), t.EncodeToPNG());
    UnityEngine.Object.DestroyImmediate(t);
    UnityEditor.AssetDatabase.ImportAsset(sheetPath, UnityEditor.ImportAssetOptions.ForceSynchronousImport);
    var imp = (UnityEditor.TextureImporter)UnityEditor.AssetImporter.GetAtPath(sheetPath);
    imp.isReadable = true; imp.filterMode = UnityEngine.FilterMode.Point; imp.mipmapEnabled = false;
    imp.textureCompression = UnityEditor.TextureImporterCompression.Uncompressed;
    imp.npotScale = UnityEditor.TextureImporterNPOTScale.None;
    imp.textureType = UnityEditor.TextureImporterType.Sprite;
    imp.spriteImportMode = UnityEditor.SpriteImportMode.Single;
    imp.SaveAndReimport();
}
var sheetTex = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(sheetPath);
var sheetSpr = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Sprite>(sheetPath);
SB.Append("sheet tex=").Append(sheetTex != null).Append(" sprite=").Append(sheetSpr != null).Append('\n');

System.Func<ZuiGradient> Grad = () =>
{
    var g = new ZuiGradient(); g.stops.Clear();
    g.stops.Add(new ZuiGradientStop { pos = 0f, color = UnityEngine.Color.yellow });
    g.stops.Add(new ZuiGradientStop { pos = 1f, color = new UnityEngine.Color(0f, 0.2f, 1f, 1f) });
    return g;
};

System.Func<Laubrary.Shaper.ShaperDocument> Blank = () =>
{
    var d = UnityEngine.ScriptableObject.CreateInstance<Laubrary.Shaper.ShaperDocument>();
    d.canvasWidth = 96; d.canvasHeight = 64; d.frameCount = 8; d.seed = 7u; d.frameRate = 12;
    d.lightRig.lights.Add(new Laubrary.Shaper.ShaperLight { name = "Key", enabled = true });
    return d;
};
System.Func<string, Laubrary.Shaper.ShaperDocument, Laubrary.Shaper.ShaperDocument> Save = (name, d) =>
{
    string p = DIR + "/" + name + ".asset";
    UnityEditor.AssetDatabase.DeleteAsset(p);
    UnityEditor.AssetDatabase.CreateAsset(d, p);
    UnityEditor.AssetDatabase.SaveAssetIfDirty(d);
    UnityEditor.AssetDatabase.ImportAsset(p, UnityEditor.ImportAssetOptions.ForceUpdate | UnityEditor.ImportAssetOptions.ForceSynchronousImport);
    return UnityEditor.AssetDatabase.LoadAssetAtPath<Laubrary.Shaper.ShaperDocument>(p);
};

int[] FR = new int[] { 0, 4, 7 };
Laubrary.Shaper.ShaperDocument DOC = null;
System.Func<UnityEngine.Color32[][]> Shot = () =>
{
    var o = new UnityEngine.Color32[FR.Length][];
    for (int i = 0; i < FR.Length; i++) o[i] = Laubrary.Shaper.ShaperDocumentRenderer.RenderFrame(DOC, FR[i]);
    return o;
};
System.Func<UnityEngine.Color32[][], UnityEngine.Color32[][], int> Diff = (a, b) =>
{
    int n = 0;
    for (int f = 0; f < a.Length; f++) for (int i = 0; i < a[f].Length; i++)
        if (!a[f][i].Equals(b[f][i])) n++;
    return n;
};

System.Func<object, System.Reflection.FieldInfo, int> Perturb = (owner, fi) =>
{
    var before = Shot();
    object orig = fi.GetValue(owner);
    var ra = (UnityEngine.RangeAttribute)System.Attribute.GetCustomAttribute(fi, typeof(UnityEngine.RangeAttribute));
    var ft = fi.FieldType;
    int best = 0;
    var tries = new System.Collections.Generic.List<object>();

    if (ft == typeof(ZUIValue))
    {
        var zv = orig as ZUIValue; if (zv == null) return -1;
        var keepMode = zv.mode; var keepStatic = zv.staticValue;
        var pts = new System.Collections.Generic.List<ZUIEnvelopePoint>();
        foreach (var p in zv.points) pts.Add(p);
        float lo = ra != null ? ra.min : (keepStatic == 0f ? -8f : -UnityEngine.Mathf.Abs(keepStatic) * 2f);
        float hi = ra != null ? ra.max : (keepStatic == 0f ?  8f :  UnityEngine.Mathf.Abs(keepStatic) * 3f);
        foreach (var tv in new float[] { lo, hi })
        {
            zv.mode = ZUIValue.Mode.Static; zv.staticValue = tv;
            int dd = Diff(before, Shot()); if (dd > best) best = dd;
        }
        zv.mode = keepMode; zv.staticValue = keepStatic;
        zv.points.Clear(); foreach (var p in pts) zv.points.Add(p);
        return best;
    }
    if (ft == typeof(float))
    {
        float v = (float)orig;
        if (ra != null) { tries.Add(ra.min); tries.Add(ra.max); }
        else { tries.Add(v == 0f ? 1f : v * 3f); tries.Add(v == 0f ? -1f : -v); tries.Add(0f); }
    }
    else if (ft == typeof(int))
    {
        int v = (int)orig;
        if (ra != null) { tries.Add((int)ra.min); tries.Add((int)ra.max); }
        else { tries.Add(v + 5); tries.Add(UnityEngine.Mathf.Max(0, v - 5)); tries.Add(v * 3 + 1); }
    }
    else if (ft == typeof(bool)) tries.Add(!(bool)orig);
    else if (ft.IsEnum) { foreach (var e in System.Enum.GetValues(ft)) if (!e.Equals(orig)) tries.Add(e); }
    else if (ft == typeof(UnityEngine.Color))
    { tries.Add(UnityEngine.Color.magenta); tries.Add(new UnityEngine.Color(0f, 0f, 0f, 1f)); }
    else if (ft == typeof(string))
    { tries.Add(((string)orig) == "SHAPE" ? "WWWW" : "SHAPE"); }
    else return -1;

    foreach (var tv in tries)
    {
        try { fi.SetValue(owner, tv); } catch { continue; }
        int dd = Diff(before, Shot()); if (dd > best) best = dd;
    }
    fi.SetValue(owner, orig);
    return best;
};

System.Action<string, object> SweepObj = (tag, owner) =>
{
    if (owner == null) { SB.Append(tag).Append("\t<null>\n"); return; }
    foreach (var fi in owner.GetType().GetFields(BFi))
    {
        if (fi.IsNotSerialized) continue;
        int px = Perturb(owner, fi);
        if (px < 0) continue;
        SB.Append(tag).Append('\t').Append(fi.Name).Append('\t').Append(fi.FieldType.Name)
          .Append('\t').Append(px).Append(px == 0 ? "\tDEAD\n" : "\tlive\n");
    }
};

System.Action Lit = () =>
{
    var s = Shot(); int lit = 0;
    foreach (var f in s) foreach (var c in f) if (c.a > 0) lit++;
    SB.Append("lit over ").Append(FR.Length).Append(" frames = ").Append(lit)
      .Append(" of ").Append(s.Length * s[0].Length).Append('\n');
};

// ── fixtures ────────────────────────────────────────────────────────────────────────────────────────
if (WHICH == "text")
{
    var d = Blank(); var l = NewLayer("Text", d);
    l.root.kind = Laubrary.Shaper.ShaperNodeKind.Primitive;
    l.root.primitive.kind = Laubrary.Shaper.ShaperPrimitiveKind.Text;
    d.layers.Add(l);
    DOC = Save("A12Text", d);
    Lit();
    SweepObj("Text/primitive", DOC.layers[0].root.primitive);
}
else if (WHICH == "sprite")
{
    var d = Blank(); var l = NewLayer("Sprite", d);
    l.root.kind = Laubrary.Shaper.ShaperNodeKind.Primitive;
    l.root.primitive.kind = Laubrary.Shaper.ShaperPrimitiveKind.Sprite;
    d.layers.Add(l);
    DOC = Save("A12Sprite", d);
    var pd = DOC.layers[0].root.primitive;
    foreach (var fi in pd.GetType().GetFields(BFi))
    {
        if (fi.FieldType == typeof(UnityEngine.Sprite)) { fi.SetValue(pd, sheetSpr); SB.Append("sprite -> ").Append(fi.Name).Append('\n'); }
        else if (fi.FieldType == typeof(UnityEngine.Texture2D)) { fi.SetValue(pd, sheetTex); SB.Append("texture -> ").Append(fi.Name).Append('\n'); }
    }
    Lit();
    SweepObj("Sprite/primitive", pd);
}
else if (WHICH == "ring")
{
    var d = Blank(); var l = NewLayer("Ring", d);
    l.root.kind = Laubrary.Shaper.ShaperNodeKind.Solid;
    l.root.solid.form = Laubrary.Shaper.ShaperSolidForm.Ring;
    d.layers.Add(l);
    DOC = Save("A12Ring", d);
    Lit();
    SweepObj("Ring/solid", DOC.layers[0].root.solid);
}
else if (WHICH == "fill-texture" || WHICH == "fill-strip" || WHICH == "fill-pattern")
{
    var kind = WHICH == "fill-texture" ? Laubrary.Shaper.ShaperFillKind.Texture
             : WHICH == "fill-strip"   ? Laubrary.Shaper.ShaperFillKind.IndexedStrip
                                       : Laubrary.Shaper.ShaperFillKind.Procedural;
    var d = Blank(); var l = NewLayer("N", d);
    l.root.primitive.kind = Laubrary.Shaper.ShaperPrimitiveKind.Ellipse;
    l.root.primitive.ellipseRxDial = new ZUIValue(34f);
    l.root.primitive.ellipseRyDial = new ZUIValue(22f);
    l.root.fill.kind = kind;
    d.layers.Add(l);
    DOC = Save("A12" + WHICH.Replace("-", ""), d);
    var f = DOC.layers[0].root.fill;
    f.kind = kind;
    if (kind == Laubrary.Shaper.ShaperFillKind.Texture) f.texture = sheetTex;
    if (kind == Laubrary.Shaper.ShaperFillKind.Procedural) f.proceduralGradient = Grad();
    Lit();
    SweepObj("Fill/" + WHICH, f);
}
else if (WHICH == "bag")
{
    var d = Blank(); var l = NewLayer("Bag", d);
    l.root.kind = Laubrary.Shaper.ShaperNodeKind.Bag;
    l.root.children.Clear();
    var m1 = NewMember("Member 1", d);
    var m2 = NewMember("Member 2", d);
    m2.mode = Laubrary.Shaper.ShaperCombineMode.Subtract;      // the card's "Cut out"
    m2.transform.translateX = new ZUIValue(12f);
    l.root.children.Add(m1); l.root.children.Add(m2);
    d.layers.Add(l);
    DOC = Save("A12Bag", d);
    Lit();
    SweepObj("Bag/member2", DOC.layers[0].root.children[1]);
    SweepObj("Bag/member2.blend", DOC.layers[0].root.children[1].blend);
    SweepObj("Bag/bagfill", DOC.layers[0].root.fill);
}
else if (WHICH == "mask")
{
    var d = Blank();
    var a0 = NewLayer("Painted", d); a0.id = 1;
    a0.root.primitive.kind = Laubrary.Shaper.ShaperPrimitiveKind.Ellipse;
    a0.root.primitive.ellipseRxDial = new ZUIValue(34f);
    a0.root.primitive.ellipseRyDial = new ZUIValue(22f);
    var b0 = NewLayer("Mask source", d); b0.id = 2;
    b0.root.primitive.rectHalfWDial = new ZUIValue(18f);
    b0.root.primitive.rectHalfHDial = new ZUIValue(26f);
    b0.contributesToPicture = false;
    a0.mask.sourceLayerId = 2;
    d.layers.Add(a0); d.layers.Add(b0);
    DOC = Save("A12Mask", d);
    Lit();
    SB.Append("mask IsSet=").Append(DOC.layers[0].mask.IsSet).Append('\n');
    SweepObj("Mask", DOC.layers[0].mask);
}
else if (WHICH == "cleanup")
{
    bool ok = UnityEditor.AssetDatabase.DeleteAsset(DIR);
    SB.Append("deleted ").Append(DIR).Append(" ok=").Append(ok)
      .Append(" stillExists=").Append(UnityEditor.AssetDatabase.IsValidFolder(DIR)).Append('\n');
}
return SB.ToString();
