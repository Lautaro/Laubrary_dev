// T-0271 probe 3 — the CARD, read back from the built visual tree on a real window, for a SAVED document:
// a node whose fill is a phantom must offer "Add fill" (not "Remove fill"), and one whose fill was authored
// must offer "Remove fill". Same question for the Edge card. Then start 3 s of playback.
var PUB = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
        | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static;
var sb = new System.Text.StringBuilder();
string dir = "Assets/Shaper/Audit0271";
string path = dir + "/Card.asset";
UnityEditor.AssetDatabase.DeleteAsset(path);
var winT = System.Type.GetType("Laubrary.Shaper.Editor.ShaperWindow, com.Lautaro-Arino.Laubrary.Shaper.Editor");
var newLayer = winT.GetMethod("NewLayer", PUB);
var entries = Laubrary.Shaper.Editor.ShaperShapeCatalog.All();
var byLabel = new System.Collections.Generic.Dictionary<string, Laubrary.Shaper.Editor.ShaperShapeEntry>();
foreach (var e in entries) if (!byLabel.ContainsKey(e.Label)) byLabel[e.Label] = e;

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
    if (i == 1) m.fill = new Laubrary.Shaper.ShaperFillDef { authored = true, solidColor = UnityEngine.Color.green };
    bag.children.Add(m);
}
UnityEditor.AssetDatabase.CreateAsset(doc, path);
UnityEditor.AssetDatabase.SaveAssetIfDirty(doc);
UnityEditor.AssetDatabase.ForceReserializeAssets(new string[] { path });
UnityEditor.AssetDatabase.ImportAsset(path, UnityEditor.ImportAssetOptions.ForceUpdate | UnityEditor.ImportAssetOptions.ForceSynchronousImport);
var saved = UnityEditor.AssetDatabase.LoadAssetAtPath<Laubrary.Shaper.ShaperDocument>(path);

Laubrary.Shaper.Editor.ShaperWindow.OpenFor(saved);
var win = UnityEditor.EditorWindow.GetWindow(winT, false, null, false);
// point the window at the saved asset regardless of what the library selected
var docField = winT.GetField("document", PUB) ?? winT.BaseType.GetField("document", PUB);
if (docField == null)
{
    var p = winT.GetProperty("document", PUB); if (p != null) p.SetValue(win, saved);
}
else docField.SetValue(win, saved);

var buildFill = winT.GetMethod("BuildFillSection", PUB);
System.Func<Laubrary.Shaper.ShaperNode, string> readCard = n =>
{
    var host = new UnityEngine.UIElements.VisualElement();
    buildFill.Invoke(win, new object[] { host, n });
    var texts = new System.Collections.Generic.List<string>();
    System.Action<UnityEngine.UIElements.VisualElement> walk = null;
    walk = ve =>
    {
        var b = ve as UnityEngine.UIElements.Button;
        if (b != null && !string.IsNullOrEmpty(b.text)) texts.Add("[" + b.text + "]");
        for (int i = 0; i < ve.childCount; i++) walk(ve[i]);
    };
    walk(host);
    return string.Join(" ", texts);
};

var sv = saved.layers[0].root;
sb.Append("SAVED document, Fill card buttons per node\n");
sb.Append("  bag  (fill seeded by NewLayer, authored)      : " + readCard(sv) + "\n");
sb.Append("  m0   (never given a fill -> phantom on disk)  : " + readCard(sv.children[0]) + "\n");
sb.Append("  m1   (authored green fill)                    : " + readCard(sv.children[1]) + "\n");
sb.Append("  IsAuthored: bag=" + Laubrary.Shaper.ShaperFillDef.IsAuthored(sv.fill)
        + " m0=" + Laubrary.Shaper.ShaperFillDef.IsAuthored(sv.children[0].fill)
        + " m1=" + Laubrary.Shaper.ShaperFillDef.IsAuthored(sv.children[1].fill) + "\n");
sb.Append("  border IsAuthored: bag=" + Laubrary.Shaper.ShaperBorderDef.IsAuthored(sv.border)
        + " m0=" + Laubrary.Shaper.ShaperBorderDef.IsAuthored(sv.children[0].border) + "\n");

// ── start playback ────────────────────────────────────────────────────────────────────────────────────
var playingF = winT.GetField("playing", PUB);
var tick = winT.GetMethod("PlaybackTick", PUB);
playingF.SetValue(win, true);
var del = (UnityEditor.EditorApplication.CallbackFunction)System.Delegate.CreateDelegate(
    typeof(UnityEditor.EditorApplication.CallbackFunction), win, tick);
UnityEditor.EditorApplication.update -= del;
UnityEditor.EditorApplication.update += del;
sb.Append("\nplayback started on " + path + " at " + System.DateTime.Now.ToString("HH:mm:ss.fff") + "\n");
return sb.ToString();
