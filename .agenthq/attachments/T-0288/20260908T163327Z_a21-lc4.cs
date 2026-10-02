var SB = new System.Text.StringBuilder();
var BF = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.FlattenHierarchy;
System.Func<System.Type,string,System.Reflection.FieldInfo> F = (t,n) => { for (var x=t; x!=null; x=x.BaseType) { var f=x.GetField(n,BF); if (f!=null) return f; } return null; };
System.Func<System.Type,string,System.Reflection.MethodInfo> M = (t,n) => { for (var x=t; x!=null; x=x.BaseType) { var m=x.GetMethod(n,BF); if (m!=null) return m; } return null; };
System.Func<System.Type,string,System.Reflection.PropertyInfo> P = (t,n) => { for (var x=t; x!=null; x=x.BaseType) { var p=x.GetProperty(n,BF); if (p!=null) return p; } return null; };
System.Func<Laubrary.Shaper.Editor.ShaperWindow> Win = () => UnityEditor.EditorWindow.GetWindow<Laubrary.Shaper.Editor.ShaperWindow>();
System.Action<UnityEngine.UIElements.VisualElement, System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> WalkT = null;
WalkT = (e, into) => { if (e==null) return; into.Add(e); for (int i=0;i<e.childCount;i++) WalkT(e[i], into); };
System.Func<System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> Tree = () => { var l = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>(); WalkT(Win().rootVisualElement, l); return l; };
System.Func<UnityEngine.UIElements.VisualElement,string> TextOf = v => { var p = v.GetType().GetProperty("text", System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public); if (p!=null && p.PropertyType==typeof(string)) { try { return (string)p.GetValue(v); } catch {} } return null; };
System.Func<string,UnityEngine.UIElements.Button> FindBtn = s => { foreach (var v in Tree()) if (v is UnityEngine.UIElements.Button b && b.text!=null && b.text.Contains(s) && v.resolvedStyle.display!=UnityEngine.UIElements.DisplayStyle.None) return b; return null; };
System.Func<string,string> Press = s => { var b = FindBtn(s); if (b==null) return "NOTFOUND:"+s; bool en=b.enabledInHierarchy; using (var ev = UnityEngine.UIElements.NavigationSubmitEvent.GetPooled()) { ev.target=b; b.SendEvent(ev); } return "pressed \""+b.text+"\" en="+en; };
System.Func<string> Sample = () => {
  var w = Win(); var wt = w.GetType();
  object playing = F(wt,"playing")?.GetValue(w); object fr = F(wt,"currentFrame")?.GetValue(w);
  var docp = P(wt,"Current"); var doc = docp==null?null:docp.GetValue(w) as Laubrary.Shaper.ShaperDocument;
  string hash="n/a"; int lit=-1;
  var stage = F(wt,"stage")?.GetValue(w);
  if (stage!=null) { var tex = F(stage.GetType(),"_tex")?.GetValue(stage) as UnityEngine.Texture2D;
    if (tex!=null) { var px = tex.GetPixels32(); ulong h=1469598103934665603UL; lit=0;
      foreach (var c in px) { if (c.a>0) lit++; h^=c.r; h*=1099511628211UL; h^=c.g; h*=1099511628211UL; h^=c.b; h*=1099511628211UL; h^=c.a; h*=1099511628211UL; } hash=h.ToString("x16"); } }
  return "t="+System.DateTime.Now.ToString("HH:mm:ss.fff")+" playing="+playing+" frame="+fr+" doc="+(doc==null?"<none>":doc.name)+" lit="+lit+" hash="+hash; };
var win = Win(); var wt = win.GetType();
var doc = (Laubrary.Shaper.ShaperDocument)P(wt,"Current").GetValue(win);
var rig = doc.lightRig;
System.Func<UnityEngine.UIElements.VisualElement> Host = () => (UnityEngine.UIElements.VisualElement)F(wt,"lightListHost").GetValue(win);
System.Action<UnityEngine.UIElements.VisualElement,System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> HW = null;
HW = (e,l) => { l.Add(e); for (int i=0;i<e.hierarchy.childCount;i++) HW(e.hierarchy[i], l); };
System.Func<System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> HTree = () => { var l = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>(); HW(Host(), l); return l; };
System.Func<UnityEngine.UIElements.VisualElement,string,string> Click = (e,lbl) => { using (var ev = UnityEngine.UIElements.NavigationSubmitEvent.GetPooled()) { ev.target = e; e.SendEvent(ev); } return lbl; };
System.Func<string,int,string> ClickNth = (typeName, n) => { int k=0; foreach (var v in HTree()) if (v.GetType().Name==typeName) { if (k==n) { Click(v,""); return "clicked "+typeName+"#"+n; } k++; } return "NOTFOUND "+typeName+"#"+n; };

// 1. Add lights up to the cap
var addBtn = (UnityEngine.UIElements.Button)null;
foreach (var v in Tree()) if (v is UnityEngine.UIElements.Button b && b.text=="+ Add light") addBtn = b;
SB.Append("cap=").Append(Laubrary.Shaper.ShaperLightRig.MaxLights).Append(" start=").Append(rig.lights.Count).Append(" addEnabled=").Append(addBtn.enabledInHierarchy).Append('\n');
for (int i=0;i<9;i++) { Click(addBtn,""); }
foreach (var v in Tree()) if (v is UnityEngine.UIElements.Button b && b.text=="+ Add light") addBtn = b;
SB.Append("after 9 presses: lights=").Append(rig.lights.Count).Append(" addEnabled=").Append(addBtn.enabledInHierarchy).Append(" tip=\"").Append(addBtn.tooltip).Append("\"\n");
SB.Append("cards in list = ").Append(Host().hierarchy.childCount).Append('\n');
SB.Append("names: "); foreach (var l in rig.lights) SB.Append(l.name).Append(' '); SB.Append('\n');
return SB.ToString();
