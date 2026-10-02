var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");
System.Func<string, System.Type> FT = n => { foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()) foreach (var t in a.GetTypes()) if (t.Name == n) return t; return null; };
var pyreT = FT("PyreWindow");
UnityEditor.EditorWindow win = null;
foreach (var w in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>()) if (w != null && w.GetType() == pyreT) win = w;
var BFi = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.PropertyInfo curP = null;
for (var t = pyreT; t != null; t = t.BaseType) { curP = t.GetProperty("Current", BFi); if (curP != null) break; }
var spec = curP.GetValue(win);
sb.Append("spec=").Append(((UnityEngine.Object)spec).name).Append("\n");
// what is layer 0's form right now?
var st = spec.GetType();
var layersF = st.GetField("layers", System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public);
var layers = layersF.GetValue(spec) as System.Collections.IList;
sb.Append("layers=").Append(layers.Count).Append("\n");
var l0 = layers[0];
foreach (var f in l0.GetType().GetFields(System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public))
{
    var v = f.GetValue(l0);
    if (f.Name == "form" || f.Name == "name" || f.Name == "enabled") sb.Append("  layer0.").Append(f.Name).Append(" = ").Append(v == null ? "null" : v.GetType().Name).Append("\n");
}
// the shape picker's own options, as a user sees them
System.Action<UnityEngine.UIElements.VisualElement, System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> Walk = null;
Walk = (e, into) => { if (e == null) return; into.Add(e); for (int i = 0; i < e.childCount; i++) Walk(e[i], into); };
var all = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>(); Walk(win.rootVisualElement, all);
int pickers = 0;
foreach (var v in all)
{
    var n = v.GetType().Name;
    if (n.Contains("Dropdown") || n.Contains("Popup"))
    { pickers++; var p = v.GetType().GetProperty("text"); sb.Append("  picker ").Append(n).Append(" text=").Append(p == null ? "?" : (p.GetValue(v) ?? "").ToString()).Append(" tip=").Append(v.tooltip == null ? "" : (v.tooltip.Length>60?v.tooltip.Substring(0,60):v.tooltip)).Append("\n"); }
}
sb.Append("pickers=").Append(pickers).Append("\n");
return sb.ToString();
