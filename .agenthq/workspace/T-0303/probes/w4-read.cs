var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");
var win = UnityEditor.EditorWindow.GetWindow<Laubrary.Shaper.Editor.ShaperWindow>();
sb.Append("window=").Append(win.position.width.ToString("F0")).Append("x").Append(win.position.height.ToString("F0")).Append("\n");
System.Action<UnityEngine.UIElements.VisualElement, System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> Walk = null;
Walk = (e, into) => { if (e == null) return; into.Add(e); for (int i = 0; i < e.childCount; i++) Walk(e[i], into); };
var all = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>(); Walk(win.rootVisualElement, all);

// the column flow and its columns
UnityEngine.UIElements.VisualElement flow = null;
foreach (var v in all) if (v.GetType().Name == "ZuiColumnFlow") { flow = v; break; }
sb.Append("flow found=").Append(flow != null);
if (flow != null)
{
    sb.Append(" width=").Append(flow.resolvedStyle.width.ToString("F0"));
    var row = flow.hierarchy.childCount > 0 ? flow.hierarchy[0] : null;
    int cols = 0; var cw = new System.Text.StringBuilder();
    if (row != null) { cols = row.hierarchy.childCount; for (int i = 0; i < row.hierarchy.childCount; i++) cw.Append(row.hierarchy[i].resolvedStyle.width.ToString("F0")).Append("/").Append(row.hierarchy[i].hierarchy.childCount).Append("u "); }
    sb.Append(" COLUMNS=").Append(cols).Append(" [widthPx/units: ").Append(cw.ToString()).Append("]");
}
sb.Append("\n");

// overflow: any element whose world rect leaves its nearest ZuiBox/ZuiSection ancestor
int over = 0, checkedN = 0; var offenders = new System.Text.StringBuilder();
foreach (var v in all)
{
    UnityEngine.UIElements.VisualElement box = null; bool scrolled = false;
    for (var p = v.parent; p != null; p = p.parent) { var n = p.GetType().Name; if (n.Contains("ScrollView")) scrolled = true; if (n == "ZuiBox" || n == "ZuiSection") { box = p; break; } }
    if (box == null || scrolled) continue;
    var b = box.worldBound; var r = v.worldBound;
    if (r.width <= 0 || r.height <= 0) continue;
    checkedN++;
    if (r.xMax > b.xMax + 1.5f || r.xMin < b.xMin - 1.5f)
    { over++; if (over <= 12) offenders.Append("\n  OVERFLOW ").Append(v.GetType().Name).Append(" '").Append(v is UnityEngine.UIElements.Label lb0 ? lb0.text : (v is UnityEngine.UIElements.Button bt0 ? bt0.text : "")).Append("' r=").Append(r.ToString()).Append(" box=").Append(b.ToString()); }
}
sb.Append("elements overflowing their own box = ").Append(over).Append(" of ").Append(checkedN).Append(offenders.ToString()).Append("\n");

// truncated labels: a Label whose measured text is wider than its own resolved width
int trunc = 0; var tnames = new System.Text.StringBuilder();
foreach (var v in all)
{
    if (!(v is UnityEngine.UIElements.Label lb)) continue;
    if (string.IsNullOrEmpty(lb.text)) continue;
    if (lb.resolvedStyle.width <= 0f) continue;
    var m = lb.MeasureTextSize(lb.text, 0, UnityEngine.UIElements.VisualElement.MeasureMode.Undefined, 0, UnityEngine.UIElements.VisualElement.MeasureMode.Undefined);
    float inner = lb.resolvedStyle.width - lb.resolvedStyle.paddingLeft - lb.resolvedStyle.paddingRight;
    if (m.x > inner + 1.0f && lb.resolvedStyle.whiteSpace == UnityEngine.UIElements.WhiteSpace.NoWrap)
    { trunc++; if (trunc <= 14) tnames.Append("\n  TRUNC '").Append(lb.text).Append("' needs ").Append(m.x.ToString("F0")).Append(" has ").Append(inner.ToString("F0")); }
}
sb.Append("labels truncated (NoWrap, text wider than the box) = ").Append(trunc).Append(tnames.ToString()).Append("\n");

// buttons/controls entirely off the window's right edge
int off = 0, tot = 0;
foreach (var v in all) { var n = v.GetType().Name; if (!(v is UnityEngine.UIElements.Button) && !n.Contains("Slider") && !n.Contains("Toggle")) continue; tot++; if (v.worldBound.xMin >= win.position.width) off++; }
sb.Append("controls entirely right of the window edge = ").Append(off).Append(" of ").Append(tot).Append("\n");
return sb.ToString();
