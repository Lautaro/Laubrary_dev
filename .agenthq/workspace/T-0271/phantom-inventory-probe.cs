// T-0271 probe 2 — (a) the phantom inventory: every plain-[Serializable] class field on the authored model,
// what the window holds in memory for a freshly built document, and what the YAML holds for the same document
// once saved; (b) the demo document rendered as it is now versus as the engine behaved before this fix;
// (c) the Fill card read back on a SAVED bag, and the bag fill dial measured on that saved document.
var PUB = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
        | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static;
var sb = new System.Text.StringBuilder();
string dir = "Assets/Shaper/Audit0271";
string projRoot = System.IO.Directory.GetParent(UnityEngine.Application.dataPath).FullName;
var winT = System.Type.GetType("Laubrary.Shaper.Editor.ShaperWindow, com.Lautaro-Arino.Laubrary.Shaper.Editor");
var newLayer = winT.GetMethod("NewLayer", PUB);
var entries = Laubrary.Shaper.Editor.ShaperShapeCatalog.All();
var byLabel = new System.Collections.Generic.Dictionary<string, Laubrary.Shaper.Editor.ShaperShapeEntry>();
foreach (var e in entries) if (!byLabel.ContainsKey(e.Label)) byLabel[e.Label] = e;

System.Func<Laubrary.Shaper.ShaperDocument, int, UnityEngine.Color32[]> render =
    (d, fi) => (UnityEngine.Color32[])Laubrary.Shaper.ShaperDocumentRenderer.RenderFrame(d, fi).Clone();
System.Func<UnityEngine.Color32[], UnityEngine.Color32[], int> diff = (a, b) =>
{ if (a == null || b == null || a.Length != b.Length) return -1;
  int n = 0; for (int i = 0; i < a.Length; i++) if (a[i].r != b[i].r || a[i].g != b[i].g || a[i].b != b[i].b || a[i].a != b[i].a) n++; return n; };

// ── (a) the phantom inventory ─────────────────────────────────────────────────────────────────────────
sb.Append("== A. PHANTOM INVENTORY ==\n");
string path = dir + "/Inventory.asset";
UnityEditor.AssetDatabase.DeleteAsset(path);
var doc = UnityEngine.ScriptableObject.CreateInstance<Laubrary.Shaper.ShaperDocument>();
doc.canvasWidth = 96; doc.canvasHeight = 64; doc.frameCount = 16; doc.seed = 7u;
doc.layers.Add((Laubrary.Shaper.ShaperLayer)newLayer.Invoke(null, new object[] { "Layer 1", doc }));
if (doc.lightRig.lights.Count == 0) doc.lightRig.lights.Add(new Laubrary.Shaper.ShaperLight { name = "Key", enabled = true });
var bag = doc.layers[0].root;
bag.kind = Laubrary.Shaper.ShaperNodeKind.Bag;
for (int i = 0; i < 2; i++)
{
    var m = new Laubrary.Shaper.ShaperNode { name = "m" + i, kind = Laubrary.Shaper.ShaperNodeKind.Primitive };
    byLabel["Ellipse"].Apply(m);
    m.transform.translateX = new ZUIValue(i == 0 ? -9f : 9f);
    bag.children.Add(m);
}

// what the WINDOW holds in memory: walk every plain-[Serializable] class field on the model
var memNull = new System.Collections.Generic.List<string>();
var memObj = new System.Collections.Generic.List<string>();
System.Action<object, string> walk = null;
var seen = new System.Collections.Generic.HashSet<object>();
walk = (o, p) =>
{
    if (o == null) return;
    var t = o.GetType();
    foreach (var f in t.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
    {
        if (System.Attribute.IsDefined(f, typeof(System.NonSerializedAttribute))) continue;
        var ft = f.FieldType;
        bool isList = ft.IsGenericType && ft.GetGenericTypeDefinition() == typeof(System.Collections.Generic.List<>);
        var et = isList ? ft.GetGenericArguments()[0] : ft;
        bool plainClass = et.IsClass && !et.IsAbstract && et != typeof(string)
                          && !typeof(UnityEngine.Object).IsAssignableFrom(et)
                          && System.Attribute.IsDefined(et, typeof(System.SerializableAttribute))
                          && et.GetConstructor(System.Type.EmptyTypes) != null;
        if (!plainClass) continue;
        bool byRef = System.Attribute.IsDefined(f, typeof(UnityEngine.SerializeReference));
        var v = f.GetValue(o);
        string label = p + "." + f.Name + " : " + et.Name + (byRef ? " [SerializeReference]" : " [plain]");
        if (isList)
        {
            var l = v as System.Collections.IList;
            if (l != null) { int k = 0; foreach (var it in l) { if (it != null && seen.Add(it)) walk(it, p + "." + f.Name + "[" + k + "]"); k++; } }
            continue;
        }
        if (v == null) { if (!byRef) memNull.Add(label); }
        else { if (!byRef) memObj.Add(label); if (seen.Add(v)) walk(v, p + "." + f.Name); }
    }
};
walk(doc, "doc");
sb.Append("plain-class fields NULL in memory (Unity will materialise a default for each):\n");
foreach (var s in memNull) sb.Append("  PHANTOM-ON-SAVE  " + s + "\n");
sb.Append("plain-class fields that always exist in memory too (no phantom possible):\n");
foreach (var s in memObj) sb.Append("  ok               " + s + "\n");

UnityEditor.AssetDatabase.CreateAsset(doc, path);
UnityEditor.AssetDatabase.SaveAssetIfDirty(doc);
UnityEditor.AssetDatabase.ForceReserializeAssets(new string[] { path });
UnityEditor.AssetDatabase.ImportAsset(path, UnityEditor.ImportAssetOptions.ForceUpdate | UnityEditor.ImportAssetOptions.ForceSynchronousImport);
string yaml = System.IO.File.ReadAllText(System.IO.Path.Combine(projRoot, path));
sb.Append("YAML of that document: " + yaml.Length + " bytes, "
        + System.Text.RegularExpressions.Regex.Matches(yaml, @"(?m)^\s*fill:").Count + " fill blocks, "
        + System.Text.RegularExpressions.Regex.Matches(yaml, @"(?m)^\s*border:").Count + " border blocks, "
        + System.Text.RegularExpressions.Regex.Matches(yaml, @"(?m)^\s*authored:").Count + " authored keys\n");
var re = UnityEditor.AssetDatabase.LoadAssetAtPath<Laubrary.Shaper.ShaperDocument>(path);
var rb = re.layers[0].root;
sb.Append("after reload: bag.fill obj=" + (rb.fill != null) + " IsAuthored=" + Laubrary.Shaper.ShaperFillDef.IsAuthored(rb.fill)
        + " | bag.border obj=" + (rb.border != null) + " IsAuthored=" + Laubrary.Shaper.ShaperBorderDef.IsAuthored(rb.border)
        + " | m0.fill obj=" + (rb.children[0].fill != null) + " IsAuthored=" + Laubrary.Shaper.ShaperFillDef.IsAuthored(rb.children[0].fill)
        + " | m0.border obj=" + (rb.children[0].border != null) + " IsAuthored=" + Laubrary.Shaper.ShaperBorderDef.IsAuthored(rb.children[0].border) + "\n");

// ── (c) the bag's own Fill dial, on the SAVED document (T-0277's headline measurement) ────────────────
sb.Append("\n== C. BAG FILL DIAL ON A SAVED DOCUMENT ==\n");
var baseA = render(re, 0);
rb.fill = new Laubrary.Shaper.ShaperFillDef { authored = true, solidColor = UnityEngine.Color.red };
sb.Append("bag fill set to red on the reloaded document: " + diff(baseA, render(re, 0)) + " pixels changed at frame 0\n");
// and what the same edit did before the fix: every phantom counted
var re2 = UnityEditor.AssetDatabase.LoadAssetAtPath<Laubrary.Shaper.ShaperDocument>(path);
UnityEditor.AssetDatabase.ImportAsset(path, UnityEditor.ImportAssetOptions.ForceUpdate | UnityEditor.ImportAssetOptions.ForceSynchronousImport);
re2 = UnityEditor.AssetDatabase.LoadAssetAtPath<Laubrary.Shaper.ShaperDocument>(path);
var rb2 = re2.layers[0].root;
System.Action<Laubrary.Shaper.ShaperNode> flagAll = null;
flagAll = nn => { if (nn == null) return; if (nn.fill != null) nn.fill.authored = true;
                  if (nn.border != null) nn.border.authored = true;
                  if (nn.children != null) foreach (var c in nn.children) flagAll(c); };
flagAll(rb2);
var baseB = render(re2, 0);
rb2.fill = new Laubrary.Shaper.ShaperFillDef { authored = true, solidColor = UnityEngine.Color.red };
sb.Append("the same edit with the pre-T-0271 rule (every phantom counts): " + diff(baseB, render(re2, 0)) + " pixels changed\n");

// ── (b) the demo document — LOADED AND RENDERED, NEVER SAVED ──────────────────────────────────────────
sb.Append("\n== B. DEMO DOCUMENT (loaded, rendered, never saved) ==\n");
string demoPath = "Assets/Demos/ShaperDemo/ShaperDemoDoc.asset";
var demo = UnityEditor.AssetDatabase.LoadAssetAtPath<Laubrary.Shaper.ShaperDocument>(demoPath);
if (demo == null) sb.Append("NOT FOUND: " + demoPath + "\n");
else
{
    int fc = System.Math.Max(1, demo.frameCount);
    var now = new System.Collections.Generic.List<UnityEngine.Color32[]>();
    for (int f = 0; f < fc; f++) now.Add(render(demo, f));
    // count how many nodes carry a phantom, and how many carry a pre-flag default-valued fill
    int nodes = 0, phantomFill = 0, phantomBorder = 0, promoted = 0;
    System.Action<Laubrary.Shaper.ShaperNode> census = null;
    census = nn =>
    {
        if (nn == null) return; nodes++;
        if (nn.fill != null) { if (Laubrary.Shaper.ShaperFillDef.IsAuthored(nn.fill)) promoted++; else phantomFill++; }
        if (nn.border != null && !Laubrary.Shaper.ShaperBorderDef.IsAuthored(nn.border)) phantomBorder++;
        if (nn.children != null) foreach (var c in nn.children) census(c);
    };
    foreach (var ly in demo.layers) census(ly.root);
    sb.Append("nodes=" + nodes + " fills read as authored=" + promoted + " fills read as phantom=" + phantomFill
            + " borders read as phantom=" + phantomBorder + "\n");
    // pre-fix behaviour on the same in-memory copy
    System.Action<Laubrary.Shaper.ShaperNode> flag2 = null;
    flag2 = nn => { if (nn == null) return; if (nn.fill != null) nn.fill.authored = true;
                    if (nn.border != null) nn.border.authored = true;
                    if (nn.children != null) foreach (var c in nn.children) flag2(c); };
    foreach (var ly in demo.layers) flag2(ly.root);
    int total = 0, worst = 0;
    for (int f = 0; f < fc; f++) { int c = diff(now[f], render(demo, f)); total += c; if (c > worst) worst = c; }
    sb.Append("frames=" + fc + "  pixels that the pre-T-0271 rule would paint differently: total=" + total + " worst frame=" + worst + "\n");
    sb.Append("demo asset on disk untouched (never saved by this probe).\n");
}
sb.Append("\ndataPath=" + UnityEngine.Application.dataPath + "\n");
return sb.ToString();
