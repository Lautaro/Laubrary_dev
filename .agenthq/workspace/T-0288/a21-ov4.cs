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
System.Action<UnityEngine.UIElements.VisualElement, System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> HW = null;
HW = (e,l) => { if (e==null) return; l.Add(e); for (int i=0;i<e.hierarchy.childCount;i++) HW(e.hierarchy[i], l); };
System.Func<System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> HAll = () => { var l = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>(); HW(Win().rootVisualElement, l); return l; };
System.Func<UnityEngine.UIElements.VisualElement,System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> HSub = r => { var l = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>(); HW(r,l); return l; };
System.Action<UnityEngine.UIElements.VisualElement> Submit = e => { using (var ev = UnityEngine.UIElements.NavigationSubmitEvent.GetPooled()) { ev.target=e; e.SendEvent(ev); } };
var _pdPos = typeof(UnityEngine.UIElements.PointerEventBase<UnityEngine.UIElements.PointerDownEvent>).GetProperty("localPosition");
var _pdPos2 = typeof(UnityEngine.UIElements.PointerEventBase<UnityEngine.UIElements.PointerDownEvent>).GetProperty("position");
var _pdBtn = typeof(UnityEngine.UIElements.PointerEventBase<UnityEngine.UIElements.PointerDownEvent>).GetProperty("button");
var _puPos = typeof(UnityEngine.UIElements.PointerEventBase<UnityEngine.UIElements.PointerUpEvent>).GetProperty("localPosition");
var _puPos2 = typeof(UnityEngine.UIElements.PointerEventBase<UnityEngine.UIElements.PointerUpEvent>).GetProperty("position");
// Find the ZuiMicroSlider whose caption Label reads `cap`, then click it at fraction f of its width.
System.Func<string,float,string> DragSlider = (cap, f) => {
  UnityEngine.UIElements.VisualElement sl = null;
  foreach (var v in HAll()) if (v.GetType().Name=="ZuiMicroSlider") { foreach (var c in HSub(v)) { var t = TextOf(c); if (t==cap) { sl=v; break; } } if (sl!=null) break; }
  if (sl==null) return "NO SLIDER \""+cap+"\"";
  float w = sl.worldBound.width, h = sl.worldBound.height;
  var d = UnityEngine.UIElements.PointerDownEvent.GetPooled(); d.target = sl;
  if (_pdBtn!=null && _pdBtn.CanWrite) _pdBtn.SetValue(d, 0);
  if (_pdPos!=null && _pdPos.CanWrite) _pdPos.SetValue(d, new UnityEngine.Vector3(w*f, h*0.5f, 0f));
  if (_pdPos2!=null && _pdPos2.CanWrite) _pdPos2.SetValue(d, new UnityEngine.Vector3(sl.worldBound.x + w*f, sl.worldBound.center.y, 0f));
  sl.SendEvent(d);
  var u = UnityEngine.UIElements.PointerUpEvent.GetPooled(); u.target = sl;
  if (_puPos!=null && _puPos.CanWrite) _puPos.SetValue(u, new UnityEngine.Vector3(w*f, h*0.5f, 0f));
  if (_puPos2!=null && _puPos2.CanWrite) _puPos2.SetValue(u, new UnityEngine.Vector3(sl.worldBound.x + w*f, sl.worldBound.center.y, 0f));
  sl.SendEvent(u);
  return "dragged \""+cap+"\" to "+f;
};
var win = Win();
System.Func<System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> PAll = () => { var l = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>(); HW(Win().rootVisualElement.panel.visualTree, l); return l; };
foreach (var v in PAll()) {
  if (!v.ClassListContains("zui-hgroup")) continue;
  bool zound=false; foreach (var c in HSub(v)) if (TextOf(c)=="Plays on slot") zound=true;
  if (!zound) continue;
  var rs = v.resolvedStyle;
  SB.Append("HGroup w=").Append(rs.width).Append(" padL=").Append(rs.paddingLeft).Append(" padR=").Append(rs.paddingRight).Append(" flexWrap=").Append(rs.flexWrap).Append(" style.width=").Append(v.style.width.ToString()).Append(" flexGrow=").Append(rs.flexGrow).Append(" flexShrink=").Append(rs.flexShrink).Append('\n');
  for (int i=0;i<v.hierarchy.childCount;i++) { var c=v.hierarchy[i]; var cr=c.resolvedStyle;
    SB.Append("  child ").Append(c.GetType().Name).Append(" \"").Append(TextOf(c)).Append("\" w=").Append(cr.width).Append(" mL=").Append(cr.marginLeft).Append(" mR=").Append(cr.marginRight).Append(" style.w=").Append(c.style.width.ToString()).Append('\n'); }
  var par = v.parent; SB.Append("parent zui-field w=").Append(par.resolvedStyle.width).Append(" flexDir=").Append(par.resolvedStyle.flexDirection).Append('\n');
  for (int i=0;i<par.hierarchy.childCount;i++) SB.Append("  fieldchild ").Append(par.hierarchy[i].GetType().Name).Append(" \"").Append(TextOf(par.hierarchy[i])).Append("\" w=").Append(par.hierarchy[i].resolvedStyle.width).Append(" flexGrow=").Append(par.hierarchy[i].resolvedStyle.flexGrow).Append('\n');
  break;
}
return SB.ToString();
