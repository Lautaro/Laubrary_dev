var sb = new System.Text.StringBuilder();
System.Func<string, System.Type> FT = n => { foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()) foreach (var t in a.GetTypes()) if (t.Name == n) return t; return null; };
var pyreT = FT("PyreWindow");
UnityEditor.EditorWindow win = null;
foreach (var w0 in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>()) if (w0 != null && w0.GetType() == pyreT) win = w0;
string want = UnityEditor.EditorPrefs.GetString("A25.open", "");
System.Action<UnityEngine.UIElements.VisualElement, System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> Walk = null;
Walk = (e, into) => { if (e == null) return; into.Add(e); for (int i = 0; i < e.childCount; i++) Walk(e[i], into); };
var all = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>(); Walk(win.rootVisualElement, all);
int n = 0;
foreach (var v in all)
{
    var tn = v.GetType().Name;
    if (tn != "ZuiSection" && tn != "ZuiBox") continue;
    var tf = v.GetType().GetField("_titleText", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
    var title = tf == null ? "" : (tf.GetValue(v) as string ?? "");
    var op = v.GetType().GetProperty("IsOpen");
    if (op == null) continue;
    bool open = want.Length > 0 && ("," + want + ",").Contains("," + title + ",");
    op.SetValue(v, open);
    n++;
    sb.Append(open ? "[OPEN] " : "").Append(title).Append(" | ");
}
win.Repaint();
return "sections=" + n + " opened='" + want + "'\n" + sb.ToString();
