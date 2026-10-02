var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");
var win = UnityEditor.EditorWindow.GetWindow<Laubrary.Shaper.Editor.ShaperWindow>();
var WT = win.GetType();
var BFi = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.PropertyInfo curP = null;
for (var t = WT; t != null; t = t.BaseType) { curP = t.GetProperty("Current", BFi); if (curP != null) break; }
var changeM = WT.GetMethod("Change", BFi);
System.Reflection.MethodInfo rb = null;
for (var t = WT; t != null; t = t.BaseType) { rb = t.GetMethod("Rebuild", BFi); if (rb != null) break; }
var doc = curP.GetValue(win) as Laubrary.Shaper.ShaperDocument;
var sw = doc.layers[0].root.swarm;
// default-ish state: no spawner shape, Stagger timing - the state where most dials are inert
changeM.Invoke(win, new object[] { (System.Action)(() => {
    sw.shape = Laubrary.Shaper.ShaperSwarmShape.None;
    sw.spawnMode = Laubrary.Shaper.ShaperSwarmSpawnMode.Area;
    sw.timing = Laubrary.Shaper.ShaperSwarmTiming.Stagger; }) });
// make sure the Swarm section is visible
UnityEditor.EditorPrefs.SetString("ZuiSectionToggleBar.ShaperWindow.userSel",
   UnityEditor.EditorPrefs.GetString("ZuiSectionToggleBar.ShaperWindow.userSel","").Replace("Swarm=0","Swarm=1"));
rb.Invoke(win, null);
System.Action<UnityEngine.UIElements.VisualElement, System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> Walk = null;
Walk = (e, into) => { if (e == null) return; into.Add(e); for (int i = 0; i < e.childCount; i++) Walk(e[i], into); };
var all = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>(); Walk(win.rootVisualElement, all);
int live = 0, grey = 0, noTip = 0;
foreach (var v in all)
{
    if (!(v is UnityEngine.UIElements.Button) && v.GetType().Name.IndexOf("Slider") < 0 && v.GetType().Name.IndexOf("Toggle") < 0) continue;
    // only inside the Swarm section
    bool inSwarm = false;
    for (var p = v.parent; p != null; p = p.parent)
        if (p.GetType().Name == "ZuiSection")
        { var tf = p.GetType().GetField("_title", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
          var tt = tf == null ? null : tf.GetValue(p) as string;
          if (tt != null && tt.Contains("Swarm")) inSwarm = true; break; }
    if (!inSwarm) continue;
    if (v.enabledInHierarchy) live++; else grey++;
    if (string.IsNullOrEmpty(v.tooltip)) noTip++;
}
sb.Append("Swarm section controls: live=").Append(live).Append(" GREYED=").Append(grey).Append(" withoutTooltip=").Append(noTip).Append("\n");
foreach (var v in all)
{
    bool inSwarm = false;
    for (var p = v.parent; p != null; p = p.parent)
        if (p.GetType().Name == "ZuiSection")
        { var tf = p.GetType().GetField("_title", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
          var tt = tf == null ? null : tf.GetValue(p) as string; if (tt != null && tt.Contains("Swarm")) inSwarm = true; break; }
    if (!inSwarm) continue;
    if ((v is UnityEngine.UIElements.Button || v.GetType().Name.Contains("Slider")) && !v.enabledInHierarchy)
        sb.Append("  GREY: ").Append(v.GetType().Name).Append(" '").Append(v is UnityEngine.UIElements.Button bb ? bb.text : "").Append("' tip=").Append(v.tooltip == null ? "" : (v.tooltip.Length > 70 ? v.tooltip.Substring(0,70) : v.tooltip)).Append("\n");
}
return sb.ToString();
