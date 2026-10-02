var sb = new System.Text.StringBuilder();
System.Func<string, System.Type> FT = n => { foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()) foreach (var t in a.GetTypes()) if (t.Name == n) return t; return null; };
var pyreT = FT("PyreWindow");
UnityEditor.EditorWindow win = null;
foreach (var w in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>()) if (w != null && w.GetType() == pyreT) win = w;
System.Action<UnityEngine.UIElements.VisualElement, int> Dump = null;
Dump = (e, d) => {
    if (e == null || d > 6) return;
    var pad = new string(' ', d * 2);
    string txt = e is UnityEngine.UIElements.Label lb ? lb.text : (e is UnityEngine.UIElements.Button bt ? bt.text : "");
    sb.Append(pad).Append(e.GetType().Name).Append(" '").Append(txt).Append("'\n");
    for (int i = 0; i < e.childCount; i++) Dump(e[i], d + 1);
};
System.Action<UnityEngine.UIElements.VisualElement, System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> Walk = null;
Walk = (e, into) => { if (e == null) return; into.Add(e); for (int i = 0; i < e.childCount; i++) Walk(e[i], into); };
var all = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>(); Walk(win.rootVisualElement, all);
foreach (var v in all)
{
    if (v.GetType().Name != "ZuiSection") continue;
    var tf = v.GetType().GetField("_titleText", System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
    var title = tf == null ? "" : tf.GetValue(v) as string;
    if (title != "Shape") continue;
    Dump(v, 0);
    break;
}
return sb.ToString();
