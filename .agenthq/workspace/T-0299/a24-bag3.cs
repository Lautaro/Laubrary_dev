var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");
var win = UnityEditor.EditorWindow.GetWindow<Laubrary.Shaper.Editor.ShaperWindow>();
var WT = win.GetType();
var BFi = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.PropertyInfo curP = null;
for (var t = WT; t != null; t = t.BaseType) { curP = t.GetProperty("Current", BFi); if (curP != null) break; }
var doc = curP.GetValue(win) as Laubrary.Shaper.ShaperDocument;
var node = doc.layers[0].root;
var drillF = WT.GetField("drillPath", BFi);

System.Action<UnityEngine.UIElements.VisualElement, System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> Walk = null;
Walk = (e, into) => { if (e == null) return; into.Add(e); for (int i = 0; i < e.childCount; i++) Walk(e[i], into); };
System.Func<System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> Tree = () =>
{ var l = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>(); Walk(win.rootVisualElement, l); return l; };
System.Action<UnityEngine.UIElements.Button> Press = b =>
{ if (b == null) { sb.Append("!! MISSING\n"); return; } using (var ev = UnityEngine.UIElements.NavigationSubmitEvent.GetPooled()) { ev.target = b; b.SendEvent(ev); } };
System.Func<System.Collections.Generic.List<UnityEngine.UIElements.Button>> Opens = () =>
{ var l = new System.Collections.Generic.List<UnityEngine.UIElements.Button>();
  foreach (var v in Tree()) if (v is UnityEngine.UIElements.Button b && b.text == "Open") l.Add(b); return l; };

var dp = drillF.GetValue(win) as System.Collections.IList;
sb.Append("drillPath before=").Append(dp.Count).Append("\n");
// press the SECOND member's Open
var os = Opens();
sb.Append("Open buttons=").Append(os.Count).Append("\n");
Press(os[1]);
dp = drillF.GetValue(win) as System.Collections.IList;
sb.Append("drillPath after Open[1]=").Append(dp.Count).Append(" -> index ").Append(dp.Count > 0 ? dp[0] : -1).Append("\n");

// the breadcrumb
var crumbs = new System.Text.StringBuilder();
foreach (var v in Tree()) if (v is UnityEngine.UIElements.Button b && (b.tooltip == "Back to this layer's root node." || b.tooltip == "Back to this member."))
    crumbs.Append('[').Append(b.text).Append("] ");
sb.Append("BREADCRUMB: ").Append(crumbs.ToString()).Append("\n");

// the mode control inside the member
var btxt = new System.Text.StringBuilder();
foreach (var v in Tree()) if (v is UnityEngine.UIElements.Button b) btxt.Append('[').Append(b.text).Append(']');
sb.Append("buttons while drilled in: ").Append(btxt.ToString()).Append("\n");
return sb.ToString();
