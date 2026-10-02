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
var win = Win(); var wt = win.GetType();
SB.Append(Press("❚❚ Pause")).Append('\n');
SB.Append("playing now = ").Append(F(wt,"playing").GetValue(win)).Append('\n');
// --- re-entry: close every instance, reopen from the menu ---
foreach (var w0 in UnityEngine.Resources.FindObjectsOfTypeAll<Laubrary.Shaper.Editor.ShaperWindow>()) w0.Close();
bool opened = UnityEditor.EditorApplication.ExecuteMenuItem("Laubrary/Shaper");
win = Win(); wt = win.GetType(); win.position = new UnityEngine.Rect(80,80,1500,1200);
var docp = P(wt,"Current"); var d2 = docp.GetValue(win) as Laubrary.Shaper.ShaperDocument;
SB.Append("re-entry: menu=").Append(opened).Append(" bound=").Append(d2==null?"<none>":d2.name).Append(" elements=").Append(Tree().Count).Append('\n');
// --- second New in the same session ---
SB.Append(Press("New")).Append('\n');
UnityEngine.UIElements.TextField tf = null;
foreach (var v in Tree()) if (v is UnityEngine.UIElements.TextField t) { tf = t; break; }
tf.value = "AuditA22b";
SB.Append(Press("Create")).Append('\n');
var d3 = docp.GetValue(win) as Laubrary.Shaper.ShaperDocument;
SB.Append("document = ").Append(d3==null?"<none>":d3.name).Append('\n');
if (d3!=null) {
  SB.Append("layers=").Append(d3.layers.Count).Append(" frames=").Append(d3.frameCount).Append(" size=").Append(d3.canvasWidth).Append('x').Append(d3.canvasHeight).Append(" lights=").Append(d3.lightRig.lights.Count).Append('\n');
  var px = Laubrary.Shaper.ShaperDocumentRenderer.RenderFrame(d3, 0);
  int lit=0; foreach (var c in px) if (c.a>0) lit++;
  SB.Append("lit at frame 0 = ").Append(lit).Append('\n');
}
return SB.ToString();
