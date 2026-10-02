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
var layers = spec.GetType().GetField("layers", System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public).GetValue(spec) as System.Collections.IList;
var s = layers[0];
var sfF = s.GetType().GetField("shapeForm");
sb.Append("shapeForm before = ").Append(sfF.GetValue(s)).Append("\n");
// open the shape menu the way the caret button does
var show = pyreT.GetMethod("ShowShapeMenu", BFi);
sb.Append("ShowShapeMenu found=").Append(show != null).Append("\n");
show.Invoke(win, new object[]{ s, win.rootVisualElement });
// find the popup's items across every panel
int items = 0; var found = new System.Text.StringBuilder();
System.Action<UnityEngine.UIElements.VisualElement, System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> Walk = null;
Walk = (e, into) => { if (e == null) return; into.Add(e); for (int i = 0; i < e.childCount; i++) Walk(e[i], into); };
UnityEngine.UIElements.VisualElement target = null;
foreach (var w in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>())
{
    if (w == null || w.rootVisualElement == null) continue;
    var l = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>(); Walk(w.rootVisualElement, l);
    foreach (var v in l)
    {
        if (!v.ClassListContains("zui-menu__item")) continue;
        items++;
        string lab = ""; for (int i = 0; i < v.childCount; i++) if (v[i] is UnityEngine.UIElements.Label lb && lb.ClassListContains("zui-menu__label")) lab = lb.text;
        if (items <= 40) found.Append(lab).Append(" | ");
        if (lab == "Star") target = v;
    }
}
sb.Append("menu items=").Append(items).Append(": ").Append(found.ToString()).Append("\n");
if (target != null)
{
    using (var ev = UnityEngine.UIElements.ClickEvent.GetPooled()) { ev.target = target; target.SendEvent(ev); }
    sb.Append("clicked 'Star'\n");
}
sb.Append("shapeForm after = ").Append(sfF.GetValue(s)).Append("\n");
return sb.ToString();
