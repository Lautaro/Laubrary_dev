// T-0337 §3 — the bound document animates; after a save + reimport it is static. Isolate WHICH step loses it.
// Nothing here touches the stage or its cache: both halves go straight through ShaperBaker.RenderFrame.
var win = ZWin("ShaperWindow"); if (win == null) return "no shaper";
var BFi = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.FieldInfo assetF = null;
for (var t = win.GetType(); t != null; t = t.BaseType) if (assetF == null) assetF = t.GetField("asset", BFi);
var doc = assetF.GetValue(win) as UnityEngine.ScriptableObject;
if (doc == null) return "no asset bound";
var bakerT = ZType("ShaperBaker");
var renderM = bakerT.GetMethod("RenderFrame", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
System.Func<object, int, string> h = (d, f) => {
    var px = renderM.Invoke(null, new object[] { d, f }) as UnityEngine.Color32[];
    if (px == null) return "null";
    unchecked { uint x = 2166136261u; foreach (var p in px) { x = (x ^ p.r) * 16777619u; x = (x ^ p.g) * 16777619u; x = (x ^ p.b) * 16777619u; x = (x ^ p.a) * 16777619u; } return x.ToString("X8"); } };
System.Func<object, string> strip = d => { var s = ""; foreach (int f in new int[] { 0, 1, 2, 4, 8, 11, 12, 15 }) s += h(d, f) + " "; return s; };

var sb = new System.Text.StringBuilder();
string path = UnityEditor.AssetDatabase.GetAssetPath(doc);
sb.Append("doc=").Append(doc.name).Append(" path=").Append(path).Append(" dirty=").Append(UnityEditor.EditorUtility.IsDirty(doc)).Append('\n');
sb.Append("A  in memory, BEFORE any save          : ").Append(strip(doc)).Append('\n');

// what is the layer's own Alpha, and does anything else vary over life?
System.Action<object, string> dumpLayer = (o, tag) => {
    if (o == null) { sb.Append(tag).Append(" <null>\n"); return; }
    foreach (var f in o.GetType().GetFields(BFi))
    {
        if (f.Name != "layers") continue;
        var l = f.GetValue(o) as System.Collections.IList;
        if (l == null || l.Count == 0) { sb.Append(tag).Append(" no layers\n"); return; }
        var ly = l[0];
        foreach (var g in ly.GetType().GetFields(BFi))
        {
            var v = g.GetValue(ly);
            if (v == null) { sb.Append(tag).Append(' ').Append(g.Name).Append("=<null>\n"); continue; }
            if (v.GetType().Name != "ZUIValue") continue;
            var mode = v.GetType().GetProperty("mode", BFi);
            var sv = v.GetType().GetProperty("staticValue", BFi);
            var cv = v.GetType().GetField("curve", BFi);
            var curve = cv == null ? null : cv.GetValue(v) as UnityEngine.AnimationCurve;
            sb.Append(tag).Append(' ').Append(g.Name).Append(" mode=").Append(mode == null ? "?" : mode.GetValue(v).ToString())
              .Append(" static=").Append(sv == null ? "?" : sv.GetValue(v).ToString())
              .Append(" curveKeys=").Append(curve == null ? "-" : curve.length.ToString()).Append('\n');
        }
    }
};
dumpLayer(doc, "A  layer[0]");

// B — write it, force it back off disk as a SECOND instance, and ask the renderer the same eight frames
UnityEditor.EditorUtility.SetDirty(doc);
UnityEditor.AssetDatabase.SaveAssetIfDirty(doc);
UnityEditor.AssetDatabase.ImportAsset(path, UnityEditor.ImportAssetOptions.ForceUpdate | UnityEditor.ImportAssetOptions.ForceSynchronousImport);
sb.Append("B  the SAME instance, after Save        : ").Append(strip(doc)).Append('\n');
dumpLayer(doc, "B  layer[0]");

var reloaded = UnityEditor.AssetDatabase.LoadMainAssetAtPath(path);
sb.Append("C  a fresh load off disk (id ").Append(reloaded == null ? 0 : reloaded.GetInstanceID()).Append(" vs ").Append(doc.GetInstanceID()).Append("): ")
  .Append(reloaded == null ? "<null>" : strip(reloaded)).Append('\n');
dumpLayer(reloaded, "C  layer[0]");

// D — the raw text on disk, so a lost field is visible as text rather than inferred
string txt = System.IO.File.ReadAllText(System.IO.Path.Combine(
    System.IO.Path.GetDirectoryName(UnityEngine.Application.dataPath), path));
sb.Append("D  asset file: ").Append(txt.Length).Append(" chars, ")
  .Append(txt.Split('\n').Length).Append(" lines; contains 'curve'=").Append(txt.Contains("curve"))
  .Append(" 'alpha'=").Append(txt.Contains("alpha")).Append('\n');
return sb.ToString();
