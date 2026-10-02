var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");
var win = UnityEditor.EditorWindow.GetWindow<Laubrary.Shaper.Editor.ShaperWindow>();
var WT = win.GetType();
var BFi = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.PropertyInfo curP = null;
for (var t = WT; t != null; t = t.BaseType) { curP = t.GetProperty("Current", BFi); if (curP != null) break; }
var doc = curP.GetValue(win) as Laubrary.Shaper.ShaperDocument;
var node = doc.layers[0].root;

System.Action<UnityEngine.UIElements.VisualElement, System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> Walk = null;
Walk = (e, into) => { if (e == null) return; into.Add(e); for (int i = 0; i < e.childCount; i++) Walk(e[i], into); };
System.Func<System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> Tree = () =>
{ var l = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>(); Walk(win.rootVisualElement, l); return l; };
System.Func<string, UnityEngine.UIElements.Button> Btn = txt =>
{ foreach (var v in Tree()) if (v is UnityEngine.UIElements.Button b && b.text == txt) return b; return null; };
System.Action<UnityEngine.UIElements.Button> Press = b =>
{ if (b == null) { sb.Append("!! MISSING BUTTON\n"); return; }
  using (var ev = UnityEngine.UIElements.NavigationSubmitEvent.GetPooled()) { ev.target = b; b.SendEvent(ev); } };
System.Func<int> Lit = () => { var px = Laubrary.Shaper.ShaperDocumentRenderer.RenderFrame(doc, 0); int n = 0; foreach (var c in px) if (c.a > 0) n++; return n; };

// ADD three members through the real button
for (int i = 0; i < 3; i++) { Press(Btn("+ Add member")); sb.Append("after +Add member #").Append(i + 1).Append(": children=").Append(node.children.Count).Append(" lit=").Append(Lit()).Append("\n"); }

// what does a member row offer?
int opens = 0, xs = 0, grips = 0, toggles = 0, names = 0;
foreach (var v in Tree())
{
    if (v is UnityEngine.UIElements.Button b) { if (b.text == "Open") opens++; if (b.text == "×") xs++; }
    if (v is UnityEngine.UIElements.TextField) names++;
    if (v is UnityEngine.UIElements.Toggle) toggles++;
}
sb.Append("rows offer: Open=").Append(opens).Append(" ×=").Append(xs).Append(" textfields=").Append(names).Append(" toggles=").Append(toggles).Append("\n");
for (int i = 0; i < node.children.Count; i++)
    sb.Append("  member ").Append(i).Append(" name='").Append(node.children[i].name).Append("' mode=").Append(node.children[i].mode).Append(" enabled=").Append(node.children[i].enabled).Append("\n");
return sb.ToString();
