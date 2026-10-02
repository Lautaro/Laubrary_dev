// Drives the Shaper Views bar as a user does. Step is EditorPrefs "T0307.vstep":
//   flipall  — flip every ZuiBox's fold state (the "rearrange by hand" gesture)
//   saveas   — type EditorPrefs "T0307.vname" into the bar's name field and press "Save as"
//   apply    — press "Apply"
//   update   — press "Update"
//   delete   — press "Delete view"
//   pick     — set the picker to "T0307.vname" (a real ChangeEvent, as a user's pick raises)
// Always reports the fold pattern before and after.
var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");
var win = UnityEditor.EditorWindow.GetWindow<Laubrary.Shaper.Editor.ShaperWindow>();
var BFi = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy;
string step = UnityEditor.EditorPrefs.GetString("T0307.vstep", "read");
string vname = UnityEditor.EditorPrefs.GetString("T0307.vname", "T0307View");

System.Action<UnityEngine.UIElements.VisualElement, System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> Walk = null;
Walk = (e, into) => { if (e == null) return; into.Add(e); for (int i = 0; i < e.hierarchy.childCount; i++) Walk(e.hierarchy[i], into); };
System.Func<System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> Tree = () =>
{ var l = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>(); Walk(win.rootVisualElement, l); return l; };

System.Func<string> Pattern = () =>
{
    var s = new System.Text.StringBuilder();
    foreach (var v in Tree())
    {
        if (v.GetType().Name != "ZuiBox") continue;
        var p = v.GetType().GetProperty("IsOpen", BFi) ?? v.GetType().GetProperty("Expanded", BFi);
        s.Append(p != null && (bool)p.GetValue(v) ? "1" : "0");
    }
    return s.ToString();
};
sb.Append("step=").Append(step).Append("  before=").Append(Pattern()).Append("\n");

UnityEngine.UIElements.VisualElement bar = null;
foreach (var v in Tree()) if (v.GetType().Name == "ZuiViewBar") bar = v;
if (bar == null) return sb.Append("NO VIEW BAR\n").ToString();
var barAll = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>();
Walk(bar, barAll);
System.Func<string, UnityEngine.UIElements.Button> Btn = txt =>
{ foreach (var v in barAll) if (v is UnityEngine.UIElements.Button b && b.text == txt) return b; return null; };
System.Action<UnityEngine.UIElements.Button> Press = b =>
{ using (var ev = UnityEngine.UIElements.NavigationSubmitEvent.GetPooled()) { ev.target = b; b.SendEvent(ev); } };

if (step == "flipall")
{
    foreach (var v in Tree())
    {
        if (v.GetType().Name != "ZuiBox") continue;
        var p = v.GetType().GetProperty("IsOpen", BFi) ?? v.GetType().GetProperty("Expanded", BFi);
        if (p != null && p.CanWrite) p.SetValue(v, !(bool)p.GetValue(v));
    }
}
else if (step == "saveas")
{
    UnityEngine.UIElements.TextField tf = null;
    foreach (var v in barAll) if (v is UnityEngine.UIElements.TextField t) tf = t;
    if (tf == null) return sb.Append("NO NAME FIELD\n").ToString();
    tf.value = vname;
    var b = Btn("Save as");
    sb.Append("Save as found=").Append(b != null).Append("\n");
    Press(b);
}
else if (step == "pick")
{
    UnityEngine.UIElements.DropdownField df = null;
    foreach (var v in barAll) if (v is UnityEngine.UIElements.DropdownField d) df = d;
    if (df == null) return sb.Append("NO PICKER\n").ToString();
    sb.Append("picker choices=").Append(string.Join(";", df.choices)).Append(" value='").Append(df.value).Append("'\n");
    df.value = vname;
}
else if (step == "apply" || step == "update" || step == "delete")
{
    string txt = step == "apply" ? "Apply" : step == "update" ? "Update" : "Delete view";
    var b = Btn(txt);
    sb.Append("'").Append(txt).Append("' found=").Append(b != null).Append("\n");
    Press(b);
}

win.Repaint();
sb.Append("after=").Append(Pattern()).Append("\n");

UnityEngine.UIElements.DropdownField df2 = null;
foreach (var v in barAll) if (v is UnityEngine.UIElements.DropdownField d) df2 = d;
if (df2 != null) sb.Append("picker now value='").Append(df2.value).Append("' choices=[").Append(string.Join(";", df2.choices)).Append("]\n");

var store = UnityEditor.AssetDatabase.LoadMainAssetAtPath("Assets/Shaper/ShaperViews.asset");
sb.Append("views asset=").Append(store == null ? "<none>" : "present dirty=" + UnityEditor.EditorUtility.IsDirty(store)).Append("\n");
if (store != null)
{
    var pf = store.GetType().GetField("presets", BFi);
    var list = pf.GetValue(store) as System.Collections.IList;
    sb.Append("presets=").Append(list == null ? -1 : list.Count);
    if (list != null) foreach (var p in list)
    {
        var nf = p.GetType().GetField("name", BFi); var ef = p.GetType().GetField("entries", BFi);
        var el = ef.GetValue(p) as System.Collections.IList;
        sb.Append(" ['").Append(nf.GetValue(p)).Append("' entries=").Append(el == null ? -1 : el.Count).Append("]");
    }
    sb.Append("\n");
}
sb.Append("PREF Shaper.lastView=").Append(UnityEditor.EditorPrefs.GetString("Shaper.lastView", "<none>")).Append("\n");
return sb.ToString();
