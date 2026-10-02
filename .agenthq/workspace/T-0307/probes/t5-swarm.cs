// T-0305 by eye: with the swarm on, read "Appearance order" out of the live tree in all three timings
// against shape None and shape Circle — drawn?, enabled?, inside its box?, and the reason it carries.
// Shape/timing are set through the window's own edit path, then the window is rebuilt.
var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");
var win = UnityEditor.EditorWindow.GetWindow<Laubrary.Shaper.Editor.ShaperWindow>();
var WT = win.GetType();
var BFi = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.PropertyInfo curP = null;
for (var t = WT; t != null; t = t.BaseType) { curP = t.GetProperty("Current", BFi); if (curP != null) break; }
var doc = curP.GetValue(win) as Laubrary.Shaper.ShaperDocument;
if (doc == null) return "NO DOCUMENT BOUND";
var swarm = doc.layers[0].root.swarm;
if (swarm == null) return "NO SWARM ON LAYER 0";
swarm.enabled = true;

System.Reflection.MethodInfo rebuild = null;
for (var t = WT; t != null && rebuild == null; t = t.BaseType) rebuild = t.GetMethod("Rebuild", BFi | System.Reflection.BindingFlags.DeclaredOnly);

System.Action<UnityEngine.UIElements.VisualElement, System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> Walk = null;
Walk = (e, into) => { if (e == null) return; into.Add(e); for (int i = 0; i < e.hierarchy.childCount; i++) Walk(e.hierarchy[i], into); };

var timings = System.Enum.GetValues(typeof(Laubrary.Shaper.ShaperSwarmTiming));
foreach (var shape in new[] { Laubrary.Shaper.ShaperSwarmShape.None, Laubrary.Shaper.ShaperSwarmShape.Circle })
foreach (var tm in timings)
{
    swarm.shape = shape;
    var tf = swarm.GetType().GetField("timing", BFi);
    tf.SetValue(swarm, tm);
    rebuild.Invoke(win, null); win.Repaint();

    var all = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>();
    Walk(win.rootVisualElement, all);
    UnityEngine.UIElements.VisualElement dial = null;
    foreach (var v in all)
        if (v is UnityEngine.UIElements.Label lb && lb.text != null && lb.text.StartsWith("Appearance order")) { dial = lb; break; }
    string state;
    if (dial == null) state = "NOT DRAWN";
    else
    {
        // nearest ZuiBox ancestor, and whether the label's rect sits inside it
        UnityEngine.UIElements.VisualElement box = dial.parent;
        while (box != null && box.GetType().Name != "ZuiBox") box = box.parent;
        bool inside = false; string boxName = "<none>";
        if (box != null)
        {
            boxName = "ZuiBox";
            var wr = dial.worldBound; var br = box.worldBound;
            inside = wr.xMin >= br.xMin - 0.5f && wr.xMax <= br.xMax + 0.5f && wr.yMin >= br.yMin - 0.5f && wr.yMax <= br.yMax + 0.5f;
        }
        string tip = dial.tooltip ?? "";
        state = "DRAWN enabled=" + dial.enabledInHierarchy + " insideBox=" + inside + " box=" + boxName
              + " tip=" + (tip.Length > 90 ? tip.Substring(0, 90) + "…" : tip);
    }
    sb.Append("shape=").Append(shape).Append(" timing=").Append(tm).Append("  ").Append(state).Append("\n");
}

// restore a neutral state
swarm.shape = Laubrary.Shaper.ShaperSwarmShape.Circle;
rebuild.Invoke(win, null); win.Repaint();
sb.Append("doc dirty=").Append(UnityEditor.EditorUtility.IsDirty(doc)).Append("\n");
return sb.ToString();
