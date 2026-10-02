// A19 — bind a document whose single layer hosts one Pyre generator, then read back the REAL card:
// every drawn control's caption, whether it is enabled, and whether its tooltip names a condition.
var SB = new System.Text.StringBuilder();
var BFi = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy;
var WT = typeof(Laubrary.Shaper.Editor.ShaperWindow);
string GEN = UnityEditor.EditorPrefs.GetString("A19.gen");

System.Func<string, object> MakeSource = g =>
{
    if (g == "Fire") return new Laubrary.PyreShaper.FireCompositeSource();
    if (g == "Fireball") return new Laubrary.PyreShaper.FireballCompositeSource();
    Laubrary.Pyre.PyreForm f = null;
    if (g == "Orb") f = new Laubrary.Pyre.Forms.Kiln.OrbForm();
    else if (g == "Torch") f = new Laubrary.Pyre.Forms.Kiln.TorchForm();
    else if (g == "Jet") f = new Laubrary.Pyre.Forms.Kiln.JetForm();
    else if (g == "RadialJet") f = new Laubrary.Pyre.Forms.Kiln.RadialJetForm();
    else if (g == "ExplosiveJet") f = new Laubrary.Pyre.Forms.Kiln.ExplosiveJetForm();
    else if (g == "Inferno") f = new Laubrary.Pyre.Forms.Kiln.InfernoForm();
    else if (g == "ForkBlast") f = new Laubrary.Pyre.Forms.Kiln.ForkBlastForm();
    else if (g == "ArcBurst") f = new Laubrary.Pyre.Forms.Kiln.ArcBurstForm();
    else if (g == "PlasmaBloom") f = new Laubrary.Pyre.Forms.Kiln.PlasmaBloomForm();
    return new Laubrary.PyreShaper.PyreFormCompositeSource { form = f };
};
var SRC = (Laubrary.Shaper.IShaperCompositeSource)MakeSource(GEN);

string DIR = "Assets/Shaper/AuditA19";
if (!UnityEditor.AssetDatabase.IsValidFolder(DIR)) UnityEditor.AssetDatabase.CreateFolder("Assets/Shaper", "AuditA19");
string path = DIR + "/A19Card.asset";
UnityEditor.AssetDatabase.DeleteAsset(path);
var doc = UnityEngine.ScriptableObject.CreateInstance<Laubrary.Shaper.ShaperDocument>();
doc.canvasWidth = 96; doc.canvasHeight = 96; doc.frameCount = 8; doc.seed = 12345u;
doc.lightRig.lights.Add(new Laubrary.Shaper.ShaperLight { name = "Key", enabled = true });
var node0 = new Laubrary.Shaper.ShaperNode { name = "N", kind = Laubrary.Shaper.ShaperNodeKind.Composite,
    composite = new Laubrary.Shaper.ShaperCompositeDef { source = SRC, halfExtentX = 48f, halfExtentY = 48f, bakeWidth = 96, bakeHeight = 96 } };
doc.layers = new System.Collections.Generic.List<Laubrary.Shaper.ShaperLayer> { new Laubrary.Shaper.ShaperLayer { name = "L", enabled = true, root = node0 } };
UnityEditor.AssetDatabase.CreateAsset(doc, path);

var win = UnityEditor.EditorWindow.GetWindow<Laubrary.Shaper.Editor.ShaperWindow>();
System.Reflection.MethodInfo setAssetM = null;
for (var t = WT; t != null; t = t.BaseType) { setAssetM = t.GetMethod("SetAsset", BFi); if (setAssetM != null) break; }
setAssetM.Invoke(win, new object[] { doc });
System.Reflection.FieldInfo selF = null;
for (var t = WT; t != null; t = t.BaseType) { selF = t.GetField("selectedLayer", BFi); if (selF != null) break; }
selF.SetValue(win, 0);
System.Reflection.MethodInfo rebuildM = null;
for (var t = WT; t != null; t = t.BaseType) { rebuildM = t.GetMethod("Rebuild", BFi, null, System.Type.EmptyTypes, null); if (rebuildM != null) break; }
if (rebuildM != null) rebuildM.Invoke(win, null);
Laubrary.Zui.ZuiAudit.ExpandAll(win);
win.Repaint();

System.Action<UnityEngine.UIElements.VisualElement, System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> WalkT = null;
WalkT = (e, into) => { if (e == null) return; into.Add(e); for (int i = 0; i < e.childCount; i++) WalkT(e[i], into); };
var all = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>(); WalkT(win.rootVisualElement, all);
System.Func<UnityEngine.UIElements.VisualElement, string> TextOf = v =>
{
    var p = v.GetType().GetProperty("text", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
    if (p != null && p.PropertyType == typeof(string)) { try { return (string)p.GetValue(v); } catch { } }
    return null;
};
SB.Append("GEN=").Append(GEN).Append(" elements=").Append(all.Count).Append('\n');
int n = 0, dis = 0, notip = 0;
bool terse = UnityEditor.EditorPrefs.GetBool("A19.terse", false);
var counts = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<string>>();
foreach (var v in all)
{
    if (v.resolvedStyle.display == UnityEngine.UIElements.DisplayStyle.None) continue;
    var t = TextOf(v);
    if (string.IsNullOrEmpty(t) || t.Length > 40) continue;
    bool hasTextChild = false;
    for (int i = 0; i < v.childCount; i++) if (!string.IsNullOrEmpty(TextOf(v[i]))) hasTextChild = true;
    if (hasTextChild) continue;
    bool numeric = t.Length > 0 && (char.IsDigit(t[0]) || t[0] == '-' || t[0] == '+' || t[0] == '.');
    if (numeric) continue;
    n++;
    string tip = v.tooltip; var pv = v.parent;
    for (int k = 0; k < 4 && string.IsNullOrEmpty(tip) && pv != null; k++) { tip = pv.tooltip; pv = pv.parent; }
    if (string.IsNullOrEmpty(tip)) notip++;
    if (!v.enabledInHierarchy) dis++;
    var wb = v.worldBound;
    if (!counts.ContainsKey(t)) counts[t] = new System.Collections.Generic.List<string>();
    counts[t].Add("@" + (int)wb.x + "," + (int)wb.y);
    if (string.IsNullOrEmpty(tip)) SB.Append("NOTIP  ").Append(t).Append('\n');
    if (!terse)
        SB.Append(v.enabledInHierarchy ? "  " : "D ").Append(t)
          .Append(string.IsNullOrEmpty(tip) ? "\t<NO TOOLTIP>" : "\t" + (tip.Length > 60 ? tip.Substring(0, 60) : tip))
          .Append('\n');
}
SB.Append("--- repeated captions ---\n");
foreach (var kv in counts) if (kv.Value.Count > 1) SB.Append("REPEATED \"").Append(kv.Key).Append("\" x").Append(kv.Value.Count).Append("  ").Append(string.Join(" ", kv.Value)).Append('\n');
SB.Append("captions=").Append(n).Append(" disabled=").Append(dis).Append(" noTooltip=").Append(notip).Append('\n');
return SB.ToString();
