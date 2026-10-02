System.Func<string, System.Type> FT = n => { foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()) foreach (var t in a.GetTypes()) if (t.Name == n) return t; return null; };
var BFi = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy;
var pyreT = FT("PyreWindow");
UnityEditor.EditorWindow win = null;
foreach (var w0 in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>()) if (w0 != null && w0.GetType() == pyreT) win = w0;
var sb = new System.Text.StringBuilder();
sb.Append("window=").Append(win.position.ToString()).Append(" focused=").Append(UnityEditor.EditorWindow.focusedWindow == null ? "none" : UnityEditor.EditorWindow.focusedWindow.GetType().Name).Append("\n");
var lpwF = pyreT.GetField("leftPaneWidth", BFi);
var lpF = pyreT.GetField("leftPane", BFi);
var lp = lpF.GetValue(win) as UnityEngine.UIElements.VisualElement;
sb.Append("intent=").Append(lpwF.GetValue(win)).Append(" paneResolved=").Append(lp == null ? -1f : lp.resolvedStyle.width).Append("\n");
var sv = lp as UnityEngine.UIElements.ScrollView;
if (sv != null)
{
    sb.Append("scrollView mode=").Append(sv.mode).Append(" vScrollerVis=").Append(sv.verticalScrollerVisibility).Append("\n");
    sb.Append("vScroller display=").Append(sv.verticalScroller.resolvedStyle.display).Append(" w=").Append(sv.verticalScroller.resolvedStyle.width.ToString("F1")).Append("\n");
    sb.Append("viewport w=").Append(sv.contentViewport.resolvedStyle.width.ToString("F1")).Append(" h=").Append(sv.contentViewport.resolvedStyle.height.ToString("F1")).Append("\n");
    sb.Append("content w=").Append(sv.contentContainer.resolvedStyle.width.ToString("F1")).Append(" h=").Append(sv.contentContainer.resolvedStyle.height.ToString("F1")).Append("\n");
}
System.Action<UnityEngine.UIElements.VisualElement, System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> Walk = null;
Walk = (e, into) => { if (e == null) return; into.Add(e); for (int i = 0; i < e.childCount; i++) Walk(e[i], into); };
var all = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>(); Walk(win.rootVisualElement, all);
sb.Append("elements=").Append(all.Count).Append("\n");
foreach (var v in all) if (v.GetType().Name == "ZuiColumnFlow")
{ var row = v.hierarchy.childCount > 0 ? v.hierarchy[0] : null; sb.Append("flow w=").Append(v.resolvedStyle.width.ToString("F1")).Append(" h=").Append(v.resolvedStyle.height.ToString("F1")).Append(" columns=").Append(row == null ? -1 : row.childCount).Append("\n"); }
// section fold states
int open = 0, total = 0;
foreach (var v in all) if (v.GetType().Name == "ZuiSection")
{
    total++;
    var pf = v.GetType().GetProperty("Open", BFi) ?? v.GetType().GetProperty("IsOpen", BFi);
    bool o = pf != null && (bool)pf.GetValue(v);
    if (o) open++;
}
sb.Append("sections total=").Append(total).Append(" open=").Append(open).Append("\n");
return sb.ToString();
