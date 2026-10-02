// Turns the scratch document's swarm on with a Circle spawn and the timing named by "T0304.timing",
// rebuilds Shaper at pane width "T0304.w", and reports where "Appearance order" lands relative to its box.
System.Action<UnityEngine.UIElements.VisualElement, System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> Walk = null;
Walk = (e, into) => { if (e == null) return; into.Add(e); for (int i = 0; i < e.hierarchy.childCount; i++) Walk(e.hierarchy[i], into); };
var BFi = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy;
UnityEditor.EditorWindow win = null;
foreach (var w0 in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>()) if (w0 != null && w0.GetType().Name == "ShaperWindow") win = w0;
var doc = UnityEditor.AssetDatabase.LoadAssetAtPath<Laubrary.Shaper.ShaperDocument>("Assets/Shaper/AuditT0305.asset");
System.Reflection.MethodInfo setAsset = null, rebuild = null;
for (var t = win.GetType(); t != null && setAsset == null; t = t.BaseType) setAsset = t.GetMethod("SetAsset", BFi | System.Reflection.BindingFlags.DeclaredOnly);
for (var t = win.GetType(); t != null && rebuild == null; t = t.BaseType) rebuild = t.GetMethod("Rebuild", BFi | System.Reflection.BindingFlags.DeclaredOnly);
setAsset.Invoke(win, new object[] { doc });
var s = doc.layers[0].root.swarm;
s.enabled = true; s.count = 8; s.EnsureDials(); s.spawnerRadius.staticValue = 24f;
string sh = UnityEditor.EditorPrefs.GetString("T0304.shape", "Circle");
s.shape = sh == "None" ? Laubrary.Shaper.ShaperSwarmShape.None : Laubrary.Shaper.ShaperSwarmShape.Circle;
string tm = UnityEditor.EditorPrefs.GetString("T0304.timing", "Stagger");
s.timing = tm == "Window" ? Laubrary.Shaper.ShaperSwarmTiming.Window
         : tm == "FrameStep" ? Laubrary.Shaper.ShaperSwarmTiming.FrameStep
         : Laubrary.Shaper.ShaperSwarmTiming.Stagger;
UnityEditor.EditorPrefs.SetFloat("ZUI.Split.shaper.window.split.v1", float.Parse(UnityEditor.EditorPrefs.GetString("T0304.w", "560")));
rebuild.Invoke(win, null); win.Repaint();
var lt = System.Type.GetType("UnityEditor.LogEntries,UnityEditor");
lt.GetMethod("Clear", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public).Invoke(null, null);
var all = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>(); Walk(win.rootVisualElement, all);
var sb = new System.Text.StringBuilder();
sb.Append("shape=").Append(s.shape).Append(" timing=").Append(s.timing).Append(" pane=").Append(UnityEditor.EditorPrefs.GetString("T0304.w", "560")).Append("\n");
foreach (var v in all)
{
    var lb = v as UnityEngine.UIElements.Label;
    if (lb == null || lb.text != "Appearance order") continue;
    var ctrl = lb.parent;
    UnityEngine.UIElements.VisualElement box = null;
    for (var p = lb.parent; p != null; p = p.parent) if (p.ClassListContains("zui-box")) { box = p; break; }
    var r = ctrl.worldBound; var br = box == null ? new UnityEngine.Rect() : box.worldBound;
    sb.Append("control rect=").Append(((int)r.x)).Append(',').Append((int)r.y).Append(' ').Append((int)r.width).Append('x').Append((int)r.height)
      .Append("  box rect=").Append((int)br.x).Append(',').Append((int)br.y).Append(' ').Append((int)br.width).Append('x').Append((int)br.height)
      .Append("  insideBox=").Append(box != null && r.xMax <= br.xMax + 0.5f && r.yMax <= br.yMax + 0.5f && r.xMin >= br.xMin - 0.5f)
      .Append("  enabled=").Append(lb.enabledInHierarchy)
      .Append("  visible=").Append(ctrl.resolvedStyle.display)
      .Append("\n");
    break;
}
return sb.ToString();
