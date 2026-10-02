// Applies EditorPrefs "T0304.mask" (one char per top-level flow unit: '1' visible, '0' display:none),
// then clears the console. A mask shorter than the unit count leaves the remainder visible.
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
for (int c = 0; c < row.hierarchy.childCount; c++)
{ var col = row.hierarchy[c]; for (int i = 0; i < col.hierarchy.childCount; i++) units.Add(col.hierarchy[i]); }
string mask = UnityEditor.EditorPrefs.GetString("T0304.mask", "");
var sb = new System.Text.StringBuilder();
sb.Append("units=").Append(units.Count).Append(" mask='").Append(mask).Append("'\n");
for (int i = 0; i < units.Count; i++)
{
    bool vis = i >= mask.Length || mask[i] != '0';
    units[i].style.display = vis ? UnityEngine.UIElements.DisplayStyle.Flex : UnityEngine.UIElements.DisplayStyle.None;
    string nm = units[i].GetType().Name;
    var found = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>(); Walk(units[i], found);
    string txt = "?";
    foreach (var f in found) { var lb = f as UnityEngine.UIElements.Label; if (lb != null && !string.IsNullOrEmpty(lb.text)) { txt = lb.text; break; } }
    sb.Append(i).Append(':').Append(nm).Append('/').Append(txt).Append(" kids=").Append(found.Count).Append(vis ? " ON" : " off").Append("\n");
}
win.Repaint();
var lt = System.Type.GetType("UnityEditor.LogEntries,UnityEditor");
lt.GetMethod("Clear", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public).Invoke(null, null);
return sb.ToString();
