// A19 — can a saved view be RE-applied once it is the picker's current value? Two views, switched both ways.
var SB = new System.Text.StringBuilder();
var win = UnityEditor.EditorWindow.GetWindow<Laubrary.Shaper.Editor.ShaperWindow>();
System.Action<UnityEngine.UIElements.VisualElement, System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> WalkT = null;
WalkT = (e, into) => { if (e == null) return; into.Add(e); for (int i = 0; i < e.childCount; i++) WalkT(e[i], into); };
System.Func<System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> All = () =>
{ var l = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>(); WalkT(win.rootVisualElement, l); return l; };
System.Func<UnityEngine.UIElements.VisualElement> Bar = () => { UnityEngine.UIElements.VisualElement b = null; foreach (var v in All()) if (v.GetType().Name == "ZuiViewBar") b = v; return b; };
System.Func<System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> InBar = () =>
{ var l = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>(); var b = Bar(); if (b != null) WalkT(b, l); return l; };
System.Func<UnityEngine.UIElements.DropdownField> Picker = () => { UnityEngine.UIElements.DropdownField d = null; foreach (var x in InBar()) if (x is UnityEngine.UIElements.DropdownField dd) d = dd; return d; };
System.Func<UnityEngine.UIElements.TextField> NameF = () => { UnityEngine.UIElements.TextField t = null; foreach (var x in InBar()) if (x is UnityEngine.UIElements.TextField tf) t = tf; return t; };
System.Func<string, string> PressBar = s =>
{ foreach (var x in InBar()) if (x is UnityEngine.UIElements.Button b && b.text == s) { using (var ev = UnityEngine.UIElements.NavigationSubmitEvent.GetPooled()) { ev.target = b; b.SendEvent(ev); } return "pressed " + s; } return "NOT FOUND " + s; };
System.Func<string> State = () =>
{
    int so = 0, sn = 0, bo = 0, bn = 0;
    foreach (var v in All())
    {
        if (v.GetType().Name == "ZuiSection") { sn++; if ((bool)v.GetType().GetProperty("IsOpen").GetValue(v)) so++; }
        if (v.GetType().Name == "ZuiBox") { bn++; if ((bool)v.GetType().GetProperty("IsOpen").GetValue(v)) bo++; }
    }
    return "sections " + so + "/" + sn + ", boxes " + bo + "/" + bn;
};

SB.Append("now: ").Append(State()).Append("  picker='").Append(Picker().value).Append("' choices=[").Append(string.Join(",", Picker().choices)).Append("]\n");
// save the CURRENT (opposite) arrangement as a second view
NameF().value = "A19View2";
SB.Append(PressBar("Save as")).Append("  choices=[").Append(string.Join(",", Picker().choices)).Append("] value='").Append(Picker().value).Append("'\n");

// gesture 1: pick the OTHER view — a real value change
Picker().value = "A19View";
SB.Append("picked A19View -> ").Append(State()).Append('\n');
// gesture 2: pick the SAME view again (what a user does to 'restore' after rearranging)
foreach (var v in All()) if (v.GetType().Name == "ZuiBox") v.GetType().GetProperty("IsOpen").SetValue(v, false);
SB.Append("rearranged by hand -> ").Append(State()).Append('\n');
Picker().value = "A19View";
SB.Append("re-picked the SAME view -> ").Append(State()).Append('\n');
// gesture 3: away and back
Picker().value = "A19View2";
Picker().value = "A19View";
SB.Append("away and back -> ").Append(State()).Append('\n');
return SB.ToString();
