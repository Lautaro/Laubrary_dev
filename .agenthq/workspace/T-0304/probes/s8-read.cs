System.Action<UnityEngine.UIElements.VisualElement, System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> Walk = null;
Walk = (e, into) => { if (e == null) return; into.Add(e); for (int i = 0; i < e.hierarchy.childCount; i++) Walk(e.hierarchy[i], into); };
UnityEditor.EditorWindow win = null;
foreach (var w0 in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>()) if (w0 != null && w0.GetType().Name == "ShaperWindow") win = w0;
var doc = UnityEditor.AssetDatabase.LoadAssetAtPath<Laubrary.Shaper.ShaperDocument>("Assets/Shaper/AuditT0305.asset");
var s = doc.layers[0].root.swarm;
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
