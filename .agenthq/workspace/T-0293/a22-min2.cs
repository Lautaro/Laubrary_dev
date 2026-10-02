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
System.Func<string,System.Type> FT = n => { foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()) { System.Type[] ts; try { ts=a.GetTypes(); } catch { continue; } foreach (var t in ts) if (t.Name==n) return t; } return null; };
var _secT = FT("ZuiSection"); var _boxT = FT("ZuiBox");
var _secOpen = _secT.GetProperty("IsOpen"); var _boxOpen = _boxT.GetProperty("IsOpen");
var _secTitle = F(_secT,"_titleText"); var _boxTitle = F(_boxT,"_titleText");
System.Func<UnityEngine.UIElements.VisualElement,string> SecTitle = v => (string)(_secTitle.GetValue(v) ?? "");
System.Func<UnityEngine.UIElements.VisualElement,string> BoxTitle = v => (string)(_boxTitle.GetValue(v) ?? "");
System.Func<string,string> OnlySection = want => { int n=0; var names=new System.Text.StringBuilder(); foreach (var v in Tree()) if (_secT.IsInstanceOfType(v)) { string t = SecTitle(v); names.Append(t).Append('|'); _secOpen.SetValue(v, t==want); n++; } return "sections="+n+" ["+names+"]"; };
System.Func<string,string> OpenBoxes = want => { int n=0; foreach (var v in Tree()) if (_boxT.IsInstanceOfType(v)) { string t = BoxTitle(v); if (t!=null && want!=null && t.Contains(want)) { _boxOpen.SetValue(v, true); n++; } } return "boxes opened="+n; };
System.Func<UnityEngine.UIElements.VisualElement,string> RealClick = e => {
  if (e==null) return "NULL";
  var wb = e.worldBound; var p = new UnityEngine.Vector2(wb.x + wb.width*0.5f, wb.y + wb.height*0.5f);
  using (var d = UnityEngine.UIElements.PointerDownEvent.GetPooled()) { d.target=e; var t=d.GetType();
    t.GetProperty("position").SetValue(d, new UnityEngine.Vector3(p.x,p.y,0));
    t.GetProperty("localPosition").SetValue(d, new UnityEngine.Vector3(wb.width*0.5f,wb.height*0.5f,0));
    t.GetProperty("button").SetValue(d, 0); t.GetProperty("pointerId").SetValue(d, 0);
    e.SendEvent(d); }
  using (var u = UnityEngine.UIElements.PointerUpEvent.GetPooled()) { u.target=e; var t=u.GetType();
    t.GetProperty("position").SetValue(u, new UnityEngine.Vector3(p.x,p.y,0));
    t.GetProperty("localPosition").SetValue(u, new UnityEngine.Vector3(wb.width*0.5f,wb.height*0.5f,0));
    t.GetProperty("button").SetValue(u, 0); t.GetProperty("pointerId").SetValue(u, 0);
    e.SendEvent(u); }
  using (var c = UnityEngine.UIElements.ClickEvent.GetPooled()) { c.target=e; var t=c.GetType();
    t.GetProperty("position").SetValue(c, new UnityEngine.Vector3(p.x,p.y,0));
    t.GetProperty("localPosition").SetValue(c, new UnityEngine.Vector3(wb.width*0.5f,wb.height*0.5f,0));
    t.GetProperty("button").SetValue(c, 0);
    e.SendEvent(c); }
  return "clicked@"+p; };
System.Func<string,UnityEngine.UIElements.VisualElement> MenuItem = label => {
  foreach (var v in Tree()) if (v.ClassListContains("zui-menu__item")) {
    var l = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>(); WalkT(v,l);
    foreach (var c in l) { var tx = TextOf(c); if (tx==label) return v; } }
  return null; };
System.Action Layout = () => { var w = Win(); var p = w.rootVisualElement.panel; var mi = p.GetType().GetMethod("ValidateLayout", BF); if (mi!=null) mi.Invoke(p, null); };
var win = Win(); var wt = win.GetType();
System.Func<string,System.Type> _FT2 = n => { foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()) { System.Type[] ts; try { ts=a.GetTypes(); } catch { continue; } foreach (var t in ts) if (t.Name==n) return t; } return null; };
var cfT = _FT2("ZuiColumnFlow");
System.Func<float,float,bool,string> Scan = (w,h,openAll) => {
  var wn = Win(); wn.position = new UnityEngine.Rect(60,60,w,h);
  M(wn.GetType(),"Rebuild").Invoke(wn, null);
  if (openAll) { foreach (var v in Tree()) if (_secT.IsInstanceOfType(v)) _secOpen.SetValue(v, true);
                 foreach (var v in Tree()) if (_boxT.IsInstanceOfType(v)) _boxOpen.SetValue(v, true); }
  wn.Repaint();
  var p = wn.rootVisualElement.panel; var mi = p.GetType().GetMethod("ValidateLayout", BF);
  for (int k=0;k<4;k++) if (mi!=null) mi.Invoke(p,null);
  var list = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>(); WalkT(wn.rootVisualElement, list);
  int xhits=0; var worst = new System.Text.StringBuilder();
  foreach (var v in list) {
    if (v.resolvedStyle.display == UnityEngine.UIElements.DisplayStyle.None) continue;
    var r = v.worldBound; if (float.IsNaN(r.width) || r.width<=0) continue;
    var cls = string.Join(",", v.GetClasses());
    if (cls.Contains("unity-scroll-view") || cls.Contains("unity-scroller") || cls.Contains("dragline")) continue;
    float cr = r.xMax; UnityEngine.UIElements.VisualElement wx=null;
    for (int i=0;i<v.hierarchy.childCount;i++) { var c = v.hierarchy[i];
      if (c.resolvedStyle.display == UnityEngine.UIElements.DisplayStyle.None) continue;
      if (c.worldBound.xMax > cr) { cr = c.worldBound.xMax; wx = c; } }
    if (cr - r.xMax > 2f) { xhits++; if (xhits<=8) worst.Append("   X+").Append((cr-r.xMax).ToString("0.0")).Append(" ").Append(v.GetType().Name).Append("[").Append(cls).Append("] child=\"").Append(wx==null?"":TextOf(wx)).Append("\"\n"); } }
  // horizontal scrollers actually VISIBLE
  int hscroll=0;
  foreach (var v in list) if (v is UnityEngine.UIElements.Scroller sc && sc.direction==UnityEngine.UIElements.SliderDirection.Horizontal && sc.resolvedStyle.display!=UnityEngine.UIElements.DisplayStyle.None && sc.worldBound.width>1) hscroll++;
  // column count
  int cols=0; foreach (var v in list) if (cfT!=null && cfT.IsInstanceOfType(v)) { var f2 = F(cfT,"_columns"); var lst = f2.GetValue(v) as System.Collections.ICollection; if (lst!=null && lst.Count>cols) cols=lst.Count; }
  return "size "+w+"x"+h+(openAll?" ALL OPEN":" default")+": elements="+list.Count+" flowColumns="+cols+" Xoverflow="+xhits+" visibleHScrollers="+hscroll+"\n"+worst; };
SB.Append(Scan(820f,520f,false));
SB.Append(Scan(820f,520f,true));
SB.Append(Scan(1560f,1000f,true));
SB.Append(Scan(1960f,1100f,true));
return SB.ToString();
