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
System.Action<int> Layout4 = n => { var p = Win().rootVisualElement.panel; var mi = p.GetType().GetMethod("ValidateLayout", BF); for (int k=0;k<n;k++) if (mi!=null) mi.Invoke(p,null); };
// find the split view and widen the left pane
UnityEngine.UIElements.TwoPaneSplitView split = null;
foreach (var v in Tree()) if (v is UnityEngine.UIElements.TwoPaneSplitView tv) split = tv;
SB.Append("split view found=").Append(split!=null).Append('\n');
win.position = new UnityEngine.Rect(30,40,2100,1150); Layout4(3);
if (split!=null) {
  var f2 = split.GetType().GetField("m_FixedPaneInitialDimension", BF) ?? split.GetType().GetField("m_FixedPaneDimension", BF);
  SB.Append("fixed dim field=").Append(f2==null?"<none>":f2.Name).Append('\n');
  split.fixedPaneInitialDimension = 1520f;
  UnityEngine.UIElements.VisualElement drag = null;
  { var l2 = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>(); WalkT(split, l2); foreach (var e in l2) if (e.name=="unity-dragline-anchor") drag = e; }
  if (drag != null) { drag.style.left = 1520f; }
  var pane0 = split.contentContainer.hierarchy.childCount>0 ? split.contentContainer.hierarchy[0] : null;
  if (pane0 != null) { pane0.style.width = 1520f; pane0.style.flexBasis = 1520f; }
  Layout4(4);
}
foreach (var v in Tree()) if (_secT.IsInstanceOfType(v)) _secOpen.SetValue(v, true);
foreach (var v in Tree()) if (_boxT.IsInstanceOfType(v)) _boxOpen.SetValue(v, true);
Layout4(5);
int cols=0; UnityEngine.UIElements.VisualElement cf=null;
foreach (var v in Tree()) if (cfT!=null && cfT.IsInstanceOfType(v)) { cf=v; var lst = F(cfT,"_columns").GetValue(v) as System.Collections.ICollection; if (lst!=null) cols=lst.Count; }
SB.Append("ColumnFlow width=").Append(cf==null?"?":cf.worldBound.width.ToString("0.0")).Append(" columns=").Append(cols).Append('\n');
// overflow + overlap scan in this state
var list = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>(); WalkT(win.rootVisualElement, list);
int xhits=0, yhits=0; var worst = new System.Text.StringBuilder();
foreach (var v in list) {
  if (v.resolvedStyle.display == UnityEngine.UIElements.DisplayStyle.None) continue;
  var r = v.worldBound; if (float.IsNaN(r.width) || r.width<=0) continue;
  var cls = string.Join(",", v.GetClasses());
  if (cls.Contains("unity-scroll-view") || cls.Contains("unity-scroller") || cls.Contains("dragline")) continue;
  float cr=r.xMax, cb=r.yMax; UnityEngine.UIElements.VisualElement wx=null;
  for (int i=0;i<v.hierarchy.childCount;i++) { var c=v.hierarchy[i];
    if (c.resolvedStyle.display == UnityEngine.UIElements.DisplayStyle.None) continue;
    if (c.worldBound.xMax>cr) { cr=c.worldBound.xMax; wx=c; }
    if (c.worldBound.yMax>cb) cb=c.worldBound.yMax; }
  if (cr-r.xMax>2f) { xhits++; if (xhits<=8) worst.Append("   X+").Append((cr-r.xMax).ToString("0.0")).Append(' ').Append(v.GetType().Name).Append('[').Append(cls).Append("] child=\"").Append(wx==null?"":TextOf(wx)).Append("\"\n"); }
  if (cb-r.yMax>4f) { yhits++; if (yhits<=8) worst.Append("   Y+").Append((cb-r.yMax).ToString("0.0")).Append(' ').Append(v.GetType().Name).Append('[').Append(cls).Append("]\n"); } }
SB.Append("Xoverflow=").Append(xhits).Append(" Yoverflow=").Append(yhits).Append('\n').Append(worst);
// column geometry: do the columns overlap each other?
if (cf!=null) { var colsList = F(cfT,"_columns").GetValue(cf) as System.Collections.IEnumerable; int i=0; UnityEngine.Rect prev = new UnityEngine.Rect();
  foreach (UnityEngine.UIElements.VisualElement c in colsList) { var b=c.worldBound;
    SB.Append("  col ").Append(i).Append(' ').Append(b.ToString()).Append(i>0 ? (b.x < prev.xMax-0.5f ? "  OVERLAPS PREVIOUS" : "  clear") : "").Append('\n'); prev=b; i++; } }
return SB.ToString();
