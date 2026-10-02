// Navigates from the Pyre dial flow's unit T0304.unit (int) down the hierarchy child-index path
// T0304.node ("2,0,3"), lists that node's children, and applies T0304.cmask ('0' = display:none) to them.
// Also restores display:Flex on every element under the whole flow first when T0304.reset == "1".
System.Func<string, System.Type> FT = n => { foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()) foreach (var t in a.GetTypes()) if (t.Name == n) return t; return null; };
var BFi = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy;
var pyreT = FT("PyreWindow");
UnityEditor.EditorWindow win = null;
foreach (var w0 in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>()) if (w0 != null && w0.GetType() == pyreT) win = w0;
System.Action<UnityEngine.UIElements.VisualElement, System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> Walk = null;
Walk = (e, into) => { if (e == null) return; into.Add(e); for (int i = 0; i < e.hierarchy.childCount; i++) Walk(e.hierarchy[i], into); };
var all = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>(); Walk(win.rootVisualElement, all);
UnityEngine.UIElements.VisualElement flow = null;
foreach (var v in all) if (v.GetType().Name == "ZuiColumnFlow") { flow = v; break; }
var units = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>();
var row = flow.hierarchy[0];
for (int c = 0; c < row.hierarchy.childCount; c++) { var col = row.hierarchy[c]; for (int i = 0; i < col.hierarchy.childCount; i++) units.Add(col.hierarchy[i]); }
var sb = new System.Text.StringBuilder();
if (UnityEditor.EditorPrefs.GetString("T0304.reset", "0") == "1")
{
    var under = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>(); Walk(flow, under);
    int cleared = 0;
    foreach (var v in under) if (v.style.display.keyword == UnityEngine.UIElements.StyleKeyword.Undefined) { v.style.display = UnityEngine.UIElements.StyleKeyword.Null; cleared++; }
    sb.Append("reset display on ").Append(cleared).Append("\n");
}
int unitIdx = int.Parse(UnityEditor.EditorPrefs.GetString("T0304.unit", "4"));
var node = units[unitIdx];
string path = UnityEditor.EditorPrefs.GetString("T0304.node", "");
if (path.Length > 0)
    foreach (var p in path.Split(','))
    { int k = int.Parse(p.Trim()); if (k < 0 || k >= node.hierarchy.childCount) { sb.Append("PATH OUT OF RANGE at ").Append(k).Append("\n"); break; } node = node.hierarchy[k]; }
sb.Append("node=").Append(node.GetType().Name).Append(" children=").Append(node.hierarchy.childCount).Append("\n");
string cmask = UnityEditor.EditorPrefs.GetString("T0304.cmask", "");
for (int i = 0; i < node.hierarchy.childCount; i++)
{
    var ch = node.hierarchy[i];
    bool vis = i >= cmask.Length || cmask[i] != '0';
    ch.style.display = vis ? UnityEngine.UIElements.DisplayStyle.Flex : UnityEngine.UIElements.DisplayStyle.None;
    var found = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>(); Walk(ch, found);
    string txt = "";
    foreach (var f in found) { var lb = f as UnityEngine.UIElements.Label; if (lb != null && !string.IsNullOrEmpty(lb.text)) { txt = lb.text; break; } }
    string cls = ""; foreach (var c in ch.GetClasses()) { cls = c; break; }
    sb.Append(i).Append(':').Append(ch.GetType().Name).Append('.').Append(cls).Append(" '").Append(txt).Append("' n=").Append(found.Count).Append(vis ? " ON" : " off").Append("\n");
}
win.Repaint();
var lt = System.Type.GetType("UnityEditor.LogEntries,UnityEditor");
lt.GetMethod("Clear", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public).Invoke(null, null);
return sb.ToString();
