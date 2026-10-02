var SB = new System.Text.StringBuilder();
var BF = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.FlattenHierarchy;
System.Func<System.Type,string,System.Reflection.FieldInfo> F = (t,n) => { for (var x=t; x!=null; x=x.BaseType) { var f=x.GetField(n,BF); if (f!=null) return f; } return null; };
System.Func<System.Type,string,System.Reflection.MethodInfo> M = (t,n) => { for (var x=t; x!=null; x=x.BaseType) { var m=x.GetMethod(n,BF); if (m!=null) return m; } return null; };
System.Func<System.Type,string,System.Reflection.PropertyInfo> P = (t,n) => { for (var x=t; x!=null; x=x.BaseType) { var p=x.GetProperty(n,BF); if (p!=null) return p; } return null; };
System.Func<Laubrary.Shaper.Editor.ShaperWindow> Win = () => UnityEditor.EditorWindow.GetWindow<Laubrary.Shaper.Editor.ShaperWindow>();
System.Action<UnityEngine.UIElements.VisualElement, System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> WalkT = null;
WalkT = (e, into) => { if (e==null) return; into.Add(e); for (int i=0;i<e.hierarchy.childCount;i++) WalkT(e.hierarchy[i], into); };
System.Func<System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> Tree = () => { var l = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>(); WalkT(Win().rootVisualElement, l); return l; };
System.Func<UnityEngine.UIElements.VisualElement,string> TextOf = v => { var p = v.GetType().GetProperty("text", System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public); if (p!=null && p.PropertyType==typeof(string)) { try { return (string)p.GetValue(v); } catch {} } return null; };
System.Func<string,UnityEngine.UIElements.Button> FindBtn = s => { foreach (var v in Tree()) if (v is UnityEngine.UIElements.Button b && b.text!=null && b.text.Contains(s) && v.resolvedStyle.display!=UnityEngine.UIElements.DisplayStyle.None) return b; return null; };
System.Func<UnityEngine.UIElements.VisualElement,string,string> Click = (e,lbl) => { using (var ev = UnityEngine.UIElements.NavigationSubmitEvent.GetPooled()) { ev.target = e; e.SendEvent(ev); } return lbl; };
System.Func<string,string> Press = s => { var b = FindBtn(s); if (b==null) return "NOTFOUND:"+s; bool en=b.enabledInHierarchy; Click(b,""); return "pressed \""+b.text+"\" en="+en; };
// --- close every instance, reopen from the menu, read the empty state ---
foreach (var w0 in UnityEngine.Resources.FindObjectsOfTypeAll<Laubrary.Shaper.Editor.ShaperWindow>()) { w0.Close(); }
SB.Append("instances after close = ").Append(UnityEngine.Resources.FindObjectsOfTypeAll<Laubrary.Shaper.Editor.ShaperWindow>().Length).Append('\n');
bool opened = UnityEditor.EditorApplication.ExecuteMenuItem("Laubrary/Shaper");
SB.Append("menu Laubrary/Shaper -> ").Append(opened).Append('\n');
var win = Win(); var wt = win.GetType();
win.position = new UnityEngine.Rect(80, 80, 1500, 1200);
M(wt,"Rebuild").Invoke(win, null); win.Repaint();
var docp = P(wt,"Current"); var doc = docp==null?null:docp.GetValue(win) as Laubrary.Shaper.ShaperDocument;
SB.Append("bound document = ").Append(doc==null?"<none>":doc.name).Append('\n');
var tree = Tree();
SB.Append("elements = ").Append(tree.Count).Append('\n');
int noTip = 0;
foreach (var v in tree) {
  bool ctl = v is UnityEngine.UIElements.Button || v is UnityEngine.UIElements.Toggle || v.GetType().Name.StartsWith("Zui");
  if (!ctl) continue;
  if (v.resolvedStyle.display == UnityEngine.UIElements.DisplayStyle.None) continue;
  if (v is UnityEngine.UIElements.Button bb) {
    SB.Append("  BTN \"").Append(bb.text).Append("\" en=").Append(bb.enabledInHierarchy).Append(" tip=\"").Append(bb.tooltip==null?"":(bb.tooltip.Length>70?bb.tooltip.Substring(0,70)+"…":bb.tooltip)).Append("\"\n");
    if (string.IsNullOrEmpty(bb.tooltip)) noTip++;
  }
}
SB.Append("buttons without a tooltip = ").Append(noTip).Append('\n');
SB.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append('\n');
return SB.ToString();
