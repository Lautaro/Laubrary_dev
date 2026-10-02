var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");
System.Func<string, System.Type> FT = n => {
    foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()) foreach (var t in a.GetTypes()) if (t.Name == n) return t;
    return null; };
var pyreT = FT("PyreWindow");
var BFi = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy;
UnityEditor.EditorWindow win = null;
foreach (var w0 in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>()) if (w0 != null && w0.GetType() == pyreT) win = w0;
sb.Append("window=").Append(win.position.width.ToString("F1")).Append("x").Append(win.position.height.ToString("F1")).Append("\n");
var lpwF = pyreT.GetField("leftPaneWidth", BFi);
var leftPaneF = pyreT.GetField("leftPane", BFi);
var left = leftPaneF.GetValue(win) as UnityEngine.UIElements.VisualElement;
sb.Append("leftPaneWidth field=").Append(lpwF.GetValue(win)).Append("\n");
sb.Append("left pane resolved width=").Append(left == null ? -1f : left.resolvedStyle.width).Append(" world=").Append(left == null ? "?" : left.worldBound.ToString()).Append("\n");
// what is to the RIGHT of the splitter, and is it reachable?
System.Action<UnityEngine.UIElements.VisualElement, System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> Walk = null;
Walk = (e, into) => { if (e == null) return; into.Add(e); for (int i = 0; i < e.childCount; i++) Walk(e[i], into); };
var all = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>(); Walk(win.rootVisualElement, all);
int offRight = 0, total = 0;
foreach (var v in all)
{
    if (!(v is UnityEngine.UIElements.Button b)) continue;
    total++;
    if (v.worldBound.xMin >= win.position.width) { offRight++; if (offRight <= 8) sb.Append("  OFF-SCREEN BUTTON '").Append(b.text).Append("' xMin=").Append(v.worldBound.xMin.ToString("F1")).Append("\n"); }
}
sb.Append("buttons entirely right of the window edge = ").Append(offRight).Append(" of ").Append(total).Append("\n");
return sb.ToString();
