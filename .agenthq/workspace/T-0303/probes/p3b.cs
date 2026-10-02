var sb = new System.Text.StringBuilder();
System.Func<string, System.Type> FT = n => { foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()) foreach (var t in a.GetTypes()) if (t.Name == n) return t; return null; };
var pyreT = FT("PyreWindow");
UnityEditor.EditorWindow win = null;
foreach (var w in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>()) if (w != null && w.GetType() == pyreT) win = w;
System.Action<UnityEngine.UIElements.VisualElement, System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> Walk = null;
Walk = (e, into) => { if (e == null) return; into.Add(e); for (int i = 0; i < e.childCount; i++) Walk(e[i], into); };
var all = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>(); Walk(win.rootVisualElement, all);
System.Func<UnityEngine.UIElements.VisualElement,string> Sec = v => {
    for (var p = v.parent; p != null; p = p.parent) { var n = p.GetType().Name; if (n == "ZuiSection" || n == "ZuiBox") { var tf = p.GetType().GetField("_titleText", System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic); return (tf == null ? "?" : tf.GetValue(p) as string) ?? "?"; } }
    return "<root>"; };
foreach (var v in all)
    if (v is UnityEngine.UIElements.Button b) sb.Append("[").Append(Sec(v)).Append("] '").Append(b.text).Append("'").Append(b.enabledInHierarchy ? "" : "(GREY)").Append("\n");
return sb.ToString();
