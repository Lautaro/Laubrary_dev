var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");
string typeName = UnityEditor.EditorPrefs.GetString("A25.type", "ChunkWindow");
System.Func<string, System.Type> FT = n => { foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()) { System.Type[] ts; try { ts = a.GetTypes(); } catch { continue; } foreach (var t in ts) if (t.Name == n) return t; } return null; };
var wt = FT(typeName);
UnityEditor.EditorWindow win = null;
foreach (var w in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>()) if (w != null && w.GetType() == wt) win = w;
if (win == null) return "no window";
sb.Append(typeName).Append(" at ").Append(win.position.width.ToString("F0")).Append("x").Append(win.position.height.ToString("F0")).Append("\n");
System.Action<UnityEngine.UIElements.VisualElement, System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> Walk = null;
Walk = (e, into) => { if (e == null) return; into.Add(e); for (int i = 0; i < e.childCount; i++) Walk(e[i], into); };
var all = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>(); Walk(win.rootVisualElement, all);
int buttons = 0, noTip = 0;
foreach (var v in all) if (v is UnityEngine.UIElements.Button b) { buttons++; if (string.IsNullOrEmpty(b.tooltip)) noTip++; }
sb.Append("elements=").Append(all.Count).Append(" buttons=").Append(buttons).Append(" withoutTooltip=").Append(noTip).Append("\n");
int over = 0, checkedN = 0; var offenders = new System.Text.StringBuilder();
foreach (var v in all)
{
    UnityEngine.UIElements.VisualElement box = null; bool scrolled = false;
    for (var p = v.parent; p != null; p = p.parent) { var n = p.GetType().Name; if (n.Contains("ScrollView")) scrolled = true; if (n == "ZuiBox" || n == "ZuiSection") { box = p; break; } }
    if (box == null || scrolled) continue;
    var b2 = box.worldBound; var r = v.worldBound;
    if (r.width <= 0 || r.height <= 0) continue;
    checkedN++;
    if (r.xMax > b2.xMax + 1.5f || r.xMin < b2.xMin - 1.5f) { over++; if (over <= 8) offenders.Append("\n  OVERFLOW ").Append(v.GetType().Name).Append(" '").Append(v is UnityEngine.UIElements.Label lb ? lb.text : (v is UnityEngine.UIElements.Button bt ? bt.text : "")).Append("' r=").Append(r.ToString()).Append(" box=").Append(b2.ToString()); }
}
sb.Append("elements overflowing their own box = ").Append(over).Append(" of ").Append(checkedN).Append(offenders.ToString()).Append("\n");
int off = 0, tot = 0;
foreach (var v in all) { var n = v.GetType().Name; if (!(v is UnityEngine.UIElements.Button) && !n.Contains("Slider") && !n.Contains("Toggle")) continue; tot++; if (v.worldBound.xMin >= win.position.width) off++; }
sb.Append("controls entirely right of the window edge = ").Append(off).Append(" of ").Append(tot).Append("\n");
int trunc = 0; var tn2 = new System.Text.StringBuilder();
foreach (var v in all)
{
    if (!(v is UnityEngine.UIElements.Label lb) || string.IsNullOrEmpty(lb.text) || lb.resolvedStyle.width <= 0f) continue;
    var m = lb.MeasureTextSize(lb.text, 0, UnityEngine.UIElements.VisualElement.MeasureMode.Undefined, 0, UnityEngine.UIElements.VisualElement.MeasureMode.Undefined);
    float inner = lb.resolvedStyle.width - lb.resolvedStyle.paddingLeft - lb.resolvedStyle.paddingRight;
    if (m.x > inner + 1.0f && lb.resolvedStyle.whiteSpace == UnityEngine.UIElements.WhiteSpace.NoWrap) { trunc++; if (trunc <= 10) tn2.Append("\n  TRUNC '").Append(lb.text).Append("' needs ").Append(m.x.ToString("F0")).Append(" has ").Append(inner.ToString("F0")); }
}
sb.Append("labels truncated = ").Append(trunc).Append(tn2.ToString()).Append("\n");
return sb.ToString();
