var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");
var win = UnityEditor.EditorWindow.GetWindow<Laubrary.Shaper.Editor.ShaperWindow>();
System.Action<UnityEngine.UIElements.VisualElement, System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> Walk = null;
Walk = (e, into) => { if (e == null) return; into.Add(e); for (int i = 0; i < e.childCount; i++) Walk(e[i], into); };
System.Func<System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> Tree = () =>
{ var l = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>(); Walk(win.rootVisualElement, l); return l; };
System.Type secT = null, boxT = null;
foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()) foreach (var t in a.GetTypes())
{ if (t.Name == "ZuiSection") secT = t; if (t.Name == "ZuiBox") boxT = t; }
var BF = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
var secOpen = secT.GetProperty("IsOpen", BF);
var boxOpen = boxT.GetProperty("IsOpen", BF);
// open every section, twice (opening one reveals nested ones)
for (int pass = 0; pass < 3; pass++)
    foreach (var v in Tree()) { if (secT.IsInstanceOfType(v)) secOpen.SetValue(v, true); if (boxT.IsInstanceOfType(v)) boxOpen.SetValue(v, true); }
int ns = 0, nbx = 0;
foreach (var v in Tree()) { if (secT.IsInstanceOfType(v)) ns++; if (boxT.IsInstanceOfType(v)) nbx++; }
sb.Append("sections=").Append(ns).Append(" boxes=").Append(nbx).Append("\n");
var btns = new System.Text.StringBuilder();
foreach (var v in Tree()) if (v is UnityEngine.UIElements.Button b) btns.Append('[').Append(b.text).Append(']');
sb.Append("buttons: ").Append(btns.ToString()).Append("\n");
return sb.ToString();
