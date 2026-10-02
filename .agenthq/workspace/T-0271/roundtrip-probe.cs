// T-0271 — save/load round trip. For every node kind: build the document the way the WINDOW builds it
// (ShaperWindow.NewLayer + the shape picker's own Apply), render frames 0/mid/last IN MEMORY, write the
// asset, ForceReserializeAssets + re-import + re-load, render the same three frames again, compare bytes.
// Any difference is a phantom serialized object acting where nothing was authored.
var PUB = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
        | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static;
var sb = new System.Text.StringBuilder();
string dir = "Assets/Shaper/Audit0271";
if (!UnityEditor.AssetDatabase.IsValidFolder("Assets/Shaper")) UnityEditor.AssetDatabase.CreateFolder("Assets", "Shaper");
if (!UnityEditor.AssetDatabase.IsValidFolder(dir)) UnityEditor.AssetDatabase.CreateFolder("Assets/Shaper", "Audit0271");

var winT = System.Type.GetType("Laubrary.Shaper.Editor.ShaperWindow, com.Lautaro-Arino.Laubrary.Shaper.Editor");
var newLayer = winT.GetMethod("NewLayer", PUB);
System.Func<string, Laubrary.Shaper.ShaperDocument, Laubrary.Shaper.ShaperLayer> NewLayer =
    (n, d) => (Laubrary.Shaper.ShaperLayer)newLayer.Invoke(null, new object[] { n, d });

// the picker's own catalog, exactly as the window shows it
var entries = Laubrary.Shaper.Editor.ShaperShapeCatalog.All();
var byLabel = new System.Collections.Generic.Dictionary<string, Laubrary.Shaper.Editor.ShaperShapeEntry>();
foreach (var e in entries) if (!byLabel.ContainsKey(e.Label)) byLabel[e.Label] = e;

// Composite generators that cost seconds a frame (T-0265's own note) are excluded from the sweep by name.
var slow = new System.Collections.Generic.HashSet<string>(new string[]
    { "Plasma Bloom", "Kiln Orb", "Jet", "Jets", "Ion Jet", "Plume Jet", "Vent Jet", "Torch", "Fire", "Fireball" });

System.Func<Laubrary.Shaper.ShaperDocument, int, UnityEngine.Color32[]> render =
    (d, fi) => Laubrary.Shaper.ShaperDocumentRenderer.RenderFrame(d, fi);
System.Func<UnityEngine.Color32[], UnityEngine.Color32[], int> diff = (a, b) =>
{
    if (a == null || b == null) return -1;
    if (a.Length != b.Length) return -2;
    int n = 0; for (int i = 0; i < a.Length; i++) if (a[i].r != b[i].r || a[i].g != b[i].g || a[i].b != b[i].b || a[i].a != b[i].a) n++;
    return n;
};
System.Func<UnityEngine.Color32[], int> lit = a => { int n = 0; for (int i = 0; i < a.Length; i++) if (a[i].a != 0) n++; return n; };

// ── the sweep ────────────────────────────────────────────────────────────────────────────────────────
sb.Append("case\tbuild\tlit0\tdiff_f0\tdiff_fmid\tdiff_flast\tverdict\n");
int rows = 0, bad = 0;

System.Action<string, System.Action<Laubrary.Shaper.ShaperDocument>> run = (label, build) =>
{
    string path = dir + "/RT_" + label.Replace("/", "_").Replace(" ", "") + ".asset";
    UnityEditor.AssetDatabase.DeleteAsset(path);
    var d0 = UnityEngine.ScriptableObject.CreateInstance<Laubrary.Shaper.ShaperDocument>();
    d0.canvasWidth = 96; d0.canvasHeight = 64; d0.frameCount = 16; d0.seed = 7u;
    // exactly what ShaperWindow.InitializeNewAsset does
    d0.layers.Add(NewLayer("Layer 1", d0));
    if (d0.lightRig.lights.Count == 0)
        d0.lightRig.lights.Add(new Laubrary.Shaper.ShaperLight { name = "Key", enabled = true });
    build(d0);
    var a0 = (UnityEngine.Color32[])render(d0, 0).Clone();
    var aM = (UnityEngine.Color32[])render(d0, 8).Clone();
    var aL = (UnityEngine.Color32[])render(d0, 15).Clone();
    UnityEditor.AssetDatabase.CreateAsset(d0, path);
    UnityEditor.AssetDatabase.SaveAssetIfDirty(d0);
    UnityEditor.AssetDatabase.ForceReserializeAssets(new string[] { path });
    UnityEditor.AssetDatabase.ImportAsset(path, UnityEditor.ImportAssetOptions.ForceUpdate | UnityEditor.ImportAssetOptions.ForceSynchronousImport);
    var d1 = UnityEditor.AssetDatabase.LoadAssetAtPath<Laubrary.Shaper.ShaperDocument>(path);
    var b0 = (UnityEngine.Color32[])render(d1, 0).Clone();
    var bM = (UnityEngine.Color32[])render(d1, 8).Clone();
    var bL = (UnityEngine.Color32[])render(d1, 15).Clone();
    int c0 = diff(a0, b0), cM = diff(aM, bM), cL = diff(aL, bL);
    rows++; if (c0 != 0 || cM != 0 || cL != 0) bad++;
    // PRE-FIX EMULATION: `fill != null` / `border != null` is exactly "every phantom counts", so flagging
    // every deserialized fill and border as authored reproduces the engine as it behaved before T-0271.
    System.Action<Laubrary.Shaper.ShaperNode> flagAll = null;
    flagAll = nn =>
    {
        if (nn == null) return;
        if (nn.fill != null) nn.fill.authored = true;
        if (nn.border != null) { nn.border.authored = true; if (nn.border.fill != null) nn.border.fill.authored = true; }
        if (nn.children != null) foreach (var c in nn.children) flagAll(c);
    };
    foreach (var ly in d1.layers) flagAll(ly.root);
    int p0 = diff(a0, render(d1, 0)), pM = diff(aM, render(d1, 8)), pL = diff(aL, render(d1, 15));
    sb.Append(label + "\t" + lit(a0) + "\t" + lit(aM) + "\t" + lit(aL) + "\t" + c0 + "\t" + cM + "\t" + cL + "\t"
            + ((c0 == 0 && cM == 0 && cL == 0) ? "IDENTICAL" : "DIFFERS")
            + "\t" + p0 + "\t" + pM + "\t" + pL + "\n");
};

foreach (var kv in byLabel)
{
    var e = kv.Value;
    if (slow.Contains(e.Label)) { sb.Append(e.Label + "\tSKIPPED (cost)\t\t\t\t\t\n"); continue; }
    var entry = e;
    run(entry.Label, d => { entry.Apply(d.layers[0].root); });
}

// Bag with two members, built the same way.
run("Bag (2 members)", d =>
{
    var n = d.layers[0].root;
    n.kind = Laubrary.Shaper.ShaperNodeKind.Bag;
    for (int i = 0; i < 2; i++)
    {
        var m = new Laubrary.Shaper.ShaperNode { name = "m" + i, kind = Laubrary.Shaper.ShaperNodeKind.Primitive };
        byLabel["Ellipse"].Apply(m);
        m.primitive.ellipseRxDial = new ZUIValue(14f); m.primitive.ellipseRyDial = new ZUIValue(12f);
        m.transform.translateX = new ZUIValue(i == 0 ? -9f : 9f);
        n.children.Add(m);
    }
});
// Bag whose OWN fill is authored to something visible and whose members own nothing — the T-0277 case.
run("Bag authored bag fill", d =>
{
    var n = d.layers[0].root;
    n.kind = Laubrary.Shaper.ShaperNodeKind.Bag;
    n.fill = new Laubrary.Shaper.ShaperFillDef { authored = true, solidColor = UnityEngine.Color.red };
    for (int i = 0; i < 2; i++)
    {
        var m = new Laubrary.Shaper.ShaperNode { name = "m" + i, kind = Laubrary.Shaper.ShaperNodeKind.Primitive };
        byLabel["Ellipse"].Apply(m);
        m.primitive.ellipseRxDial = new ZUIValue(14f); m.primitive.ellipseRyDial = new ZUIValue(12f);
        m.transform.translateX = new ZUIValue(i == 0 ? -9f : 9f);
        n.children.Add(m);
    }
});
// Bag with an authored CHILD fill — the child must keep winning inside its own coverage.
run("Bag authored child fill", d =>
{
    var n = d.layers[0].root;
    n.kind = Laubrary.Shaper.ShaperNodeKind.Bag;
    n.fill = new Laubrary.Shaper.ShaperFillDef { authored = true, solidColor = UnityEngine.Color.red };
    for (int i = 0; i < 2; i++)
    {
        var m = new Laubrary.Shaper.ShaperNode { name = "m" + i, kind = Laubrary.Shaper.ShaperNodeKind.Primitive };
        byLabel["Ellipse"].Apply(m);
        m.primitive.ellipseRxDial = new ZUIValue(14f); m.primitive.ellipseRyDial = new ZUIValue(12f);
        m.transform.translateX = new ZUIValue(i == 0 ? -9f : 9f);
        if (i == 1) m.fill = new Laubrary.Shaper.ShaperFillDef { authored = true, solidColor = UnityEngine.Color.green };
        n.children.Add(m);
    }
});
// A primitive whose fill was authored and left at every default — the one case the migration cannot tell
// from a phantom. Authored via the window's own "Add fill" shape, i.e. with the flag set.
run("Primitive default-valued fill (flagged)", d =>
{
    var n = d.layers[0].root;
    byLabel["Star"].Apply(n);
    n.fill = new Laubrary.Shaper.ShaperFillDef { authored = true };
});
// The same, but WITHOUT the flag — a document authored before T-0271. This is the honest edge case.
run("Primitive default-valued fill (pre-flag)", d =>
{
    var n = d.layers[0].root;
    byLabel["Star"].Apply(n);
    n.fill = new Laubrary.Shaper.ShaperFillDef();
});
// A CHILD with a pre-flag default-valued fill inside a bag whose own fill is red — the case where the
// unflagged edge case is visible rather than invisible.
run("Bag child pre-flag default fill", d =>
{
    var n = d.layers[0].root;
    n.kind = Laubrary.Shaper.ShaperNodeKind.Bag;
    n.fill = new Laubrary.Shaper.ShaperFillDef { authored = true, solidColor = UnityEngine.Color.red };
    for (int i = 0; i < 2; i++)
    {
        var m = new Laubrary.Shaper.ShaperNode { name = "m" + i, kind = Laubrary.Shaper.ShaperNodeKind.Primitive };
        byLabel["Ellipse"].Apply(m);
        m.primitive.ellipseRxDial = new ZUIValue(14f); m.primitive.ellipseRyDial = new ZUIValue(12f);
        m.transform.translateX = new ZUIValue(i == 0 ? -9f : 9f);
        if (i == 1) m.fill = new Laubrary.Shaper.ShaperFillDef();   // no flag, all defaults
        n.children.Add(m);
    }
});
// An authored EDGE, to prove the border round trip is unaffected by the border flag.
run("Star with authored edge", d =>
{
    var n = d.layers[0].root;
    byLabel["Star"].Apply(n);
    n.border = new Laubrary.Shaper.ShaperBorderDef { authored = true, enabled = true,
        width = new ZUIValue(3f), fill = new Laubrary.Shaper.ShaperFillDef { authored = true, solidColor = UnityEngine.Color.cyan } };
});

sb.Append("\nROWS " + rows + "  DIFFERING " + bad + "\n");
sb.Append("dataPath=" + UnityEngine.Application.dataPath + "\n");
return sb.ToString();
