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
System.Func<UnityEngine.UIElements.VisualElement,System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> Sub = r => { var l = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>(); HW(r,l); return l; };
System.Action<UnityEngine.UIElements.VisualElement> Click = e => { using (var ev = UnityEngine.UIElements.NavigationSubmitEvent.GetPooled()) { ev.target=e; e.SendEvent(ev); } };
// restore enabled + get 3 lights
if (!rig.lights[0].enabled) { foreach (var v in Sub(Host().hierarchy[0])) if (v.GetType().Name=="ZuiToggleButton") { Click(v); break; } }
UnityEngine.UIElements.Button add=null; foreach (var v in Tree()) if (v is UnityEngine.UIElements.Button b && b.text=="+ Add light") add=b;
add.SetEnabled(true);
while (rig.lights.Count < 3) { Click(add); }
SB.Append("names before: "); foreach (var l in rig.lights) SB.Append(l.name).Append(' '); SB.Append('\n');
// drag card 0's grip down past card 2
var host = Host();
var card0 = host.hierarchy[0];
UnityEngine.UIElements.VisualElement grip=null; foreach (var v in Sub(card0)) if (TextOf(v)=="\u2261") { grip=v; break; }
SB.Append("grip found=").Append(grip!=null).Append('\n');
if (grip!=null) {
  var bp = typeof(UnityEngine.UIElements.PointerEventBase<UnityEngine.UIElements.PointerDownEvent>).GetProperty("button");
  var pp = typeof(UnityEngine.UIElements.PointerEventBase<UnityEngine.UIElements.PointerDownEvent>).GetProperty("position");
  var pm = typeof(UnityEngine.UIElements.PointerEventBase<UnityEngine.UIElements.PointerMoveEvent>).GetProperty("position");
  var pu = typeof(UnityEngine.UIElements.PointerEventBase<UnityEngine.UIElements.PointerUpEvent>).GetProperty("position");
  float yBottom = host.hierarchy[2].worldBound.yMax + 2f;
  var d1 = UnityEngine.UIElements.PointerDownEvent.GetPooled(); d1.target=grip;
  if (bp!=null && bp.CanWrite) bp.SetValue(d1,0);
  if (pp!=null && pp.CanWrite) pp.SetValue(d1, new UnityEngine.Vector3(grip.worldBound.center.x, grip.worldBound.center.y, 0f));
  grip.SendEvent(d1);
  var m1 = UnityEngine.UIElements.PointerMoveEvent.GetPooled(); m1.target=grip;
  if (pm!=null && pm.CanWrite) pm.SetValue(m1, new UnityEngine.Vector3(grip.worldBound.center.x, yBottom, 0f));
  grip.SendEvent(m1);
  var u1 = UnityEngine.UIElements.PointerUpEvent.GetPooled(); u1.target=grip;
  if (pu!=null && pu.CanWrite) pu.SetValue(u1, new UnityEngine.Vector3(grip.worldBound.center.x, yBottom, 0f));
  grip.SendEvent(u1);
}
SB.Append("names after:  "); foreach (var l in rig.lights) SB.Append(l.name).Append(' '); SB.Append('\n');
SB.Append("cards after:  "); for (int i=0;i<Host().hierarchy.childCount;i++) { foreach (var v in Sub(Host().hierarchy[i])) if (v is UnityEngine.UIElements.Label lb && lb.text!="\u25be" && lb.text!="\u2261" && lb.text!="?" ) { SB.Append(lb.text).Append(' '); break; } } SB.Append('\n');
return SB.ToString();
