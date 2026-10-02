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
System.Action<int> L = n => { var p = Win().rootVisualElement.panel; var mi = p.GetType().GetMethod("ValidateLayout", BF); for (int k=0;k<n;k++) if (mi!=null) mi.Invoke(p,null); };
var keyF = F(_boxT, "_key");
System.Func<string,UnityEngine.UIElements.VisualElement> Box = t => { foreach (var v in Tree()) if (_boxT.IsInstanceOfType(v) && BoxTitle(v)==t) return v; return null; };
System.Func<string,string> St = t => { var b = Box(t); return b==null?"?":((bool)_boxOpen.GetValue(b)?"OPEN":"closed"); };
foreach (var v in Tree()) if (_secT.IsInstanceOfType(v)) _secOpen.SetValue(v, true); L(2);
// which pane is each box in?
UnityEngine.UIElements.TwoPaneSplitView sp = null; foreach (var v in Tree()) if (v is UnityEngine.UIElements.TwoPaneSplitView tv) sp = tv;
var leftPane = sp.contentContainer.hierarchy[0]; var rightPane = sp.contentContainer.hierarchy[1];
foreach (var v in Tree()) if (_boxT.IsInstanceOfType(v)) {
  bool inLeft=false, inRight=false; for (var p2=v; p2!=null; p2=p2.parent) { if (p2==leftPane) inLeft=true; if (p2==rightPane) inRight=true; }
  SB.Append("box \"").Append(BoxTitle(v)).Append("\" pane=").Append(inLeft?"LEFT":(inRight?"RIGHT":"?")).Append(" keyed=").Append(((string)(keyF.GetValue(v)??"")).StartsWith("shaper.")).Append('\n'); }
// author: close the two right-pane boxes, open everything else
foreach (var v in Tree()) if (_boxT.IsInstanceOfType(v)) _boxOpen.SetValue(v, true);
_boxOpen.SetValue(Box("Preview backdrop"), false); _boxOpen.SetValue(Box("Bake"), false); L(2);
SB.Append("AUTHORED: Preview backdrop=").Append(St("Preview backdrop")).Append(" Bake=").Append(St("Bake")).Append(" Sweep=").Append(St("Sweep")).Append('\n');
UnityEngine.UIElements.TextField nameF = null;
foreach (var v in Tree()) if (v is UnityEngine.UIElements.TextField t2 && t2.tooltip!=null && t2.tooltip.Contains("Save as")) nameF = t2;
if (nameF!=null) nameF.value = "A22Panes";
SB.Append(Press("Save as")).Append('\n');
// disturb: open both right-pane boxes, close Sweep
_boxOpen.SetValue(Box("Preview backdrop"), true); _boxOpen.SetValue(Box("Bake"), true); _boxOpen.SetValue(Box("Sweep"), false); L(2);
SB.Append("DISTURBED: Preview backdrop=").Append(St("Preview backdrop")).Append(" Bake=").Append(St("Bake")).Append(" Sweep=").Append(St("Sweep")).Append('\n');
SB.Append(Press("Apply")).Append('\n'); L(2);
SB.Append("APPLIED: Preview backdrop=").Append(St("Preview backdrop")).Append(" Bake=").Append(St("Bake")).Append(" Sweep=").Append(St("Sweep")).Append('\n');
return SB.ToString();
