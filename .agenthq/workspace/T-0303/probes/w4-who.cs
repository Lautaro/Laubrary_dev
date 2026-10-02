var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");
var win = UnityEditor.EditorWindow.GetWindow<Laubrary.Shaper.Editor.ShaperWindow>();
System.Action<UnityEngine.UIElements.VisualElement, System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> Walk = null;
Walk = (e, into) => { if (e == null) return; into.Add(e); for (int i = 0; i < e.childCount; i++) Walk(e[i], into); };
var all = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>(); Walk(win.rootVisualElement, all);
System.Func<UnityEngine.UIElements.VisualElement,string> Chain = v => {
    var s = new System.Text.StringBuilder();
    for (var p = v; p != null; p = p.parent) { s.Append(p.GetType().Name); if (p.ClassListContains("unity-scroll-view__content-viewport")) s.Append("(viewport)"); var cs = p.GetClasses(); foreach (var c in cs) if (c.StartsWith("zui-")) { s.Append(".").Append(c); break; } s.Append(" < "); }
    return s.ToString(); };
foreach (var v in all)
{
    UnityEngine.UIElements.VisualElement box = null;
    for (var p = v.parent; p != null; p = p.parent) { var n = p.GetType().Name; if (n == "ZuiBox" || n == "ZuiSection") { box = p; break; } }
    if (box == null) continue;
    var b = box.worldBound; var r = v.worldBound;
    if (r.width <= 0 || r.height <= 0) continue;
    if (r.xMax > b.xMax + 1.5f || r.xMin < b.xMin - 1.5f)
    { sb.Append("OVERFLOW at x=").Append(r.xMin.ToString("F0")).Append(": ").Append(Chain(v)).Append("\n"); }
}
foreach (var v in all)
{
    if (!(v is UnityEngine.UIElements.Label lb) || lb.text != "●") continue;
    sb.Append("BULLET chain: ").Append(Chain(v)).Append("  w=").Append(lb.resolvedStyle.width.ToString("F1")).Append("\n"); break;
}
foreach (var v in all)
{
    if (!(v is UnityEngine.UIElements.Label lb2) || lb2.text == null || !lb2.text.Contains("Shaper Document")) continue;
    sb.Append("NAME chain: ").Append(Chain(v)).Append("  w=").Append(lb2.resolvedStyle.width.ToString("F1")).Append("\n"); break;
}
return sb.ToString();
