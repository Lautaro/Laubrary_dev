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
// remove every card but the first, by pressing its own X
int guard=0;
while (rig.lights.Count > 1 && guard++ < 20) {
  var card = Host().hierarchy[rig.lights.Count-1];
  UnityEngine.UIElements.Button x = null;
  foreach (var v in Sub(card)) if (v is UnityEngine.UIElements.Button b && b.text=="×") { x=b; break; }
  if (x==null) { SB.Append("NO X on last card\n"); break; }
  Click(x);
}
SB.Append("after removing: lights=").Append(rig.lights.Count).Append(" cards=").Append(Host().hierarchy.childCount).Append(" name0=").Append(rig.lights[0].name).Append('\n');
UnityEngine.UIElements.Button add = null; foreach (var v in Tree()) if (v is UnityEngine.UIElements.Button b && b.text=="+ Add light") add=b;
SB.Append("addEnabled=").Append(add.enabledInHierarchy).Append('\n');
// enable toggle off
var c0 = Host().hierarchy[0];
UnityEngine.UIElements.VisualElement tog=null, seg=null;
foreach (var v in Sub(c0)) { if (v.GetType().Name=="ZuiToggleButton" && tog==null) tog=v; if (v.GetType().Name=="ZuiSegmented" && seg==null) seg=v; }
Click(tog);
SB.Append("after enable-toggle: enabled=").Append(rig.lights[0].enabled).Append(" sectionTip=\"").Append(((UnityEngine.UIElements.VisualElement)F(wt,"lightsSection").GetValue(win)).tooltip.Substring(0,Math.Min(80,((UnityEngine.UIElements.VisualElement)F(wt,"lightsSection").GetValue(win)).tooltip.Length))).Append("\"\n");
c0 = Host().hierarchy[0]; foreach (var v in Sub(c0)) if (v.GetType().Name=="ZuiToggleButton") { Click(v); break; }
SB.Append("after re-toggle: enabled=").Append(rig.lights[0].enabled).Append('\n');
// kind flip: click each option button inside ZuiSegmented
c0 = Host().hierarchy[0]; seg=null; foreach (var v in Sub(c0)) if (v.GetType().Name=="ZuiSegmented") { seg=v; break; }
SB.Append("segmented children: "); foreach (var v in Sub(seg)) SB.Append(v.GetType().Name).Append('[').Append(TextOf(v)).Append("] "); SB.Append('\n');
foreach (var v in Sub(seg)) if (v is UnityEngine.UIElements.Button b && b.text=="Point") { Click(b); break; }
SB.Append("after Point: kind=").Append(rig.lights[0].kind).Append('\n');
c0 = Host().hierarchy[0];
foreach (var v in Sub(c0)) if (v.GetType().Name=="ZuiValue2DControl" || v.GetType().Name=="ZuiValueControl") SB.Append("   ").Append(v.GetType().Name).Append(" en=").Append(v.enabledInHierarchy).Append(" tip=\"").Append(v.tooltip.Substring(0,Math.Min(50,v.tooltip.Length))).Append("\"\n");
return SB.ToString();
