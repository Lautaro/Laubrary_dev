// Shaper sweep setup: open cold, bind a scratch document with a Circle swarm, size the window 1700x900,
// switch EVERY toggle-bar section on (so every card is laid out), and report the state.
var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");
foreach (var w0 in UnityEngine.Resources.FindObjectsOfTypeAll<Laubrary.Shaper.Editor.ShaperWindow>()) w0.Close();
UnityEditor.EditorApplication.ExecuteMenuItem("Laubrary/Shaper");
var win = UnityEditor.EditorWindow.GetWindow<Laubrary.Shaper.Editor.ShaperWindow>();
win.position = new UnityEngine.Rect(40, 40, 1700, 900);
win.Show(); win.Repaint();
var WT = win.GetType();
var BFi = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy;

// Every section on, in the bar's own pref format (label=1 for the nine entries).
UnityEditor.EditorPrefs.SetString("ZuiSectionToggleBar.ShaperWindow.userSel",
    "Views=1;Canvas=1;Layers=1;Shape=1;Fill=1;Swarm=1;SpriteFX=1;Lights=1;Tags=1");

var doc = UnityEditor.AssetDatabase.LoadAssetAtPath<Laubrary.Shaper.ShaperDocument>("Assets/Shaper/AuditT0307.asset");
if (doc == null)
{
    var BF = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
    var newLayerM = WT.GetMethod("NewLayer", BF);
    doc = UnityEngine.ScriptableObject.CreateInstance<Laubrary.Shaper.ShaperDocument>();
    doc.layers.Add((Laubrary.Shaper.ShaperLayer)newLayerM.Invoke(null, new object[] { "Layer 1", doc }));
    var root = doc.layers[0].root;
    root.swarm = new Laubrary.Shaper.ShaperSwarmDef { enabled = true, shape = Laubrary.Shaper.ShaperSwarmShape.Circle, count = 8 };
    doc.lightRig.lights.Add(new Laubrary.Shaper.ShaperLight { name = "Key", enabled = true });
    if (!UnityEditor.AssetDatabase.IsValidFolder("Assets/Shaper")) UnityEditor.AssetDatabase.CreateFolder("Assets", "Shaper");
    UnityEditor.AssetDatabase.CreateAsset(doc, "Assets/Shaper/AuditT0307.asset");
    UnityEditor.AssetDatabase.SaveAssetIfDirty(doc);
}
System.Reflection.MethodInfo setAsset = null;
for (var t = WT; t != null && setAsset == null; t = t.BaseType) setAsset = t.GetMethod("SetAsset", BFi | System.Reflection.BindingFlags.DeclaredOnly);
setAsset.Invoke(win, new object[] { doc });

System.Reflection.MethodInfo rebuild = null;
for (var t = WT; t != null && rebuild == null; t = t.BaseType) rebuild = t.GetMethod("Rebuild", BFi | System.Reflection.BindingFlags.DeclaredOnly);
rebuild.Invoke(win, null); win.Repaint();

System.Action<UnityEngine.UIElements.VisualElement, System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> Walk = null;
Walk = (e, into) => { if (e == null) return; into.Add(e); for (int i = 0; i < e.hierarchy.childCount; i++) Walk(e.hierarchy[i], into); };
var all = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>();
Walk(win.rootVisualElement, all);
int sections = 0, boxes = 0;
foreach (var v in all) { var n = v.GetType().Name; if (n == "ZuiSection") sections++; else if (n == "ZuiBox") boxes++; }
sb.Append("bound=").Append(doc.name).Append(" dirty=").Append(UnityEditor.EditorUtility.IsDirty(doc))
  .Append(" pos=").Append(win.position).Append(" elements=").Append(all.Count)
  .Append(" sections=").Append(sections).Append(" boxes=").Append(boxes).Append("\n");
var lt = System.Type.GetType("UnityEditor.LogEntries,UnityEditor");
lt.GetMethod("Clear", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public).Invoke(null, null);
return sb.ToString();
