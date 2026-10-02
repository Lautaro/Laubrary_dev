var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");
var doc0 = UnityEditor.AssetDatabase.LoadAssetAtPath<Laubrary.Shaper.ShaperDocument>("Assets/Shaper/AuditA25a.asset");
Laubrary.Shaper.Editor.ShaperWindow.OpenFor(doc0);
var win = UnityEditor.EditorWindow.GetWindow<Laubrary.Shaper.Editor.ShaperWindow>();
win.position = new UnityEngine.Rect(60, 60, 1600, 1150);
var WT = win.GetType();
var BFi = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.PropertyInfo curP = null;
for (var t = WT; t != null; t = t.BaseType) { curP = t.GetProperty("Current", BFi); if (curP != null) break; }
var changeM = WT.GetMethod("Change", BFi);
System.Reflection.MethodInfo rb = null;
for (var t = WT; t != null; t = t.BaseType) { rb = t.GetMethod("Rebuild", BFi); if (rb != null) break; }
var doc = curP.GetValue(win) as Laubrary.Shaper.ShaperDocument;
sb.Append("doc=").Append(doc == null ? "<none>" : doc.name).Append("\n");
var sw = doc.layers[0].root.swarm;
changeM.Invoke(win, new object[]{ (System.Action)(() => sw.enabled = true) });

UnityEditor.EditorPrefs.SetString("ZuiSectionToggleBar.ShaperWindow.userSel",
   UnityEditor.EditorPrefs.GetString("ZuiSectionToggleBar.ShaperWindow.userSel","").Replace("Swarm=0","Swarm=1"));

System.Action<UnityEngine.UIElements.VisualElement, System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> Walk = null;
Walk = (e, into) => { if (e == null) return; into.Add(e); for (int i = 0; i < e.childCount; i++) Walk(e[i], into); };
System.Func<UnityEngine.UIElements.VisualElement, bool> InSwarm = v => {
    for (var p = v.parent; p != null; p = p.parent)
        if (p.GetType().Name == "ZuiSection") {
            var tf = p.GetType().GetField("_titleText", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var tt = tf == null ? null : tf.GetValue(p) as string;
            return tt != null && tt.Contains("Swarm"); }
    return false; };

var openAll = new System.Action(() => {
    var swF2 = WT.GetField("swarmSection", BFi); var sec = swF2 == null ? null : swF2.GetValue(win);
    if (sec != null) { var op = sec.GetType().GetProperty("IsOpen"); if (op != null) op.SetValue(sec, true); }
    foreach (var v0 in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEngine.ScriptableObject>()) { }
});
System.Func<string, string> Report = tag => {
    rb.Invoke(win, null);
    openAll();
    rb.Invoke(win, null);
    openAll();
    var all = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>(); Walk(win.rootVisualElement, all);
    var labels = new System.Text.StringBuilder();
    int greyed = 0, noTip = 0, ctrls = 0;
    foreach (var v in all)
    {
        if (!InSwarm(v)) continue;
        if (v is UnityEngine.UIElements.Label lb)
        {
            var txt = lb.text;
            if (string.IsNullOrEmpty(txt)) continue;
            // skip value read-outs (numbers) and option chips inside radios
            bool isChip = false;
            for (var p = lb.parent; p != null; p = p.parent) { var n = p.GetType().Name; if (n.Contains("Radio") || n.Contains("Segmented") || n.Contains("Toggle")) { isChip = true; break; } }
            if (isChip) continue;
            float dummy; if (float.TryParse(txt, out dummy)) continue;
            labels.Append(txt).Append(lb.enabledInHierarchy ? "" : "[GREY]").Append(" | ");
        }
        var tn = v.GetType().Name;
        if (v is UnityEngine.UIElements.Button || tn.Contains("Slider") || tn.Contains("Toggle") || tn.Contains("Radio") || tn.Contains("Segmented") || tn.Contains("Value2D"))
        {
            ctrls++;
            if (!v.enabledInHierarchy) { greyed++; labels.Append("<<GREYCTRL ").Append(tn).Append(" tip=").Append(v.tooltip == null ? "" : (v.tooltip.Length > 60 ? v.tooltip.Substring(0,60) : v.tooltip)).Append(">> "); }
            if (string.IsNullOrEmpty(v.tooltip)) noTip++;
        }
    }
    return tag + "\n    labels: " + labels.ToString() + "\n    controls=" + ctrls + " greyed=" + greyed + " withoutTooltip=" + noTip + "\n";
};

System.Action<System.Action> St = a => { changeM.Invoke(win, new object[]{ (System.Action)a }); };

St(() => { sw.shape = Laubrary.Shaper.ShaperSwarmShape.None; sw.spawnMode = Laubrary.Shaper.ShaperSwarmSpawnMode.Area; sw.timing = Laubrary.Shaper.ShaperSwarmTiming.Stagger; });
sb.Append(Report("A. shape=None, timing=Stagger (the default)"));
St(() => { sw.shape = Laubrary.Shaper.ShaperSwarmShape.Circle; });
sb.Append(Report("B. shape=Circle, spawn=Area, timing=Stagger"));
St(() => { sw.spawnMode = Laubrary.Shaper.ShaperSwarmSpawnMode.Path; });
sb.Append(Report("C. shape=Circle, spawn=Path, evenSpacing=" + sw.evenSpacing));
St(() => { sw.evenSpacing = true; });
sb.Append(Report("D. shape=Circle, spawn=Path, evenSpacing=true"));
St(() => { sw.evenSpacing = false; });
sb.Append(Report("E. shape=Circle, spawn=Path, evenSpacing=false"));
St(() => { sw.shape = Laubrary.Shaper.ShaperSwarmShape.Line; });
sb.Append(Report("F. shape=Line"));
St(() => { sw.shape = Laubrary.Shaper.ShaperSwarmShape.Circle; sw.spawnMode = Laubrary.Shaper.ShaperSwarmSpawnMode.Area; sw.timing = Laubrary.Shaper.ShaperSwarmTiming.Window; });
sb.Append(Report("G. timing=Window"));
St(() => { sw.timing = Laubrary.Shaper.ShaperSwarmTiming.FrameStep; });
sb.Append(Report("H. timing=FrameStep"));
St(() => { sw.shape = Laubrary.Shaper.ShaperSwarmShape.None; sw.spawnMode = Laubrary.Shaper.ShaperSwarmSpawnMode.Area; sw.timing = Laubrary.Shaper.ShaperSwarmTiming.Stagger; sw.evenSpacing = false; });
sb.Append(Report("Z. back to default"));
return sb.ToString();
