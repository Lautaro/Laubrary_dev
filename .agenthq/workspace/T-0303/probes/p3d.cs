var sb = new System.Text.StringBuilder();
System.Func<string, System.Type> FT = n => { foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()) foreach (var t in a.GetTypes()) if (t.Name == n) return t; return null; };
var pyreT = FT("PyreWindow");
UnityEditor.EditorWindow win = null;
foreach (var w in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>()) if (w != null && w.GetType() == pyreT) win = w;
System.Action<UnityEngine.UIElements.VisualElement, System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> Walk = null;
Walk = (e, into) => { if (e == null) return; into.Add(e); for (int i = 0; i < e.childCount; i++) Walk(e[i], into); };
var all = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>(); Walk(win.rootVisualElement, all);
foreach (var v in all)
{
    var n = v.GetType().Name;
    if (n != "PopupTextElement" && n != "DropdownField" && !n.Contains("ShapePicker") && !n.Contains("FormPicker")) continue;
    var chain = new System.Text.StringBuilder();
    for (var p = v; p != null; p = p.parent) chain.Append(p.GetType().Name).Append(" < ");
    // nearest label sibling
    string near = "";
    if (v.parent != null) for (int i = 0; i < v.parent.childCount; i++) if (v.parent[i] is UnityEngine.UIElements.Label l2 && !string.IsNullOrEmpty(l2.text)) { near = l2.text; break; }
    sb.Append(n).Append(" nearLabel='").Append(near).Append("' tip=").Append(v.tooltip).Append("\n   ").Append(chain).Append("\n");
}
// PyreWindow's own layer/form fields
var st = FT("PyreLayer");
if (st != null) { sb.Append("PyreLayer fields: "); foreach (var f in st.GetFields(System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public)) sb.Append(f.Name).Append('(').Append(f.FieldType.Name).Append(") "); sb.Append("\n"); }
return sb.ToString();
