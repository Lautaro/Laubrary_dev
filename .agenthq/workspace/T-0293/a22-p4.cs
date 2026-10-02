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
System.Func<string,System.Type> FT2 = n => { foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()) { System.Type[] ts; try { ts=a.GetTypes(); } catch { continue; } foreach (var t in ts) if (t.Name==n) return t; } return null; };
var pT = FT2("PyreWindow"); var assetT = FT2("Pyre");
var pw = UnityEditor.EditorWindow.GetWindow(pT);
var copy = UnityEditor.AssetDatabase.LoadAssetAtPath("Assets/Shaper/AuditA22Pyre.asset", assetT);
var ce = assetT.GetField("cherryEnabled"); var cf = assetT.GetField("cherryFrames");
ce.SetValue(copy, true);
var listObj = cf.GetValue(copy) as System.Collections.IList;
var cfT = FT2("CherryFrame");
SB.Append("CherryFrame type=").Append(cfT==null?"?":cfT.FullName).Append(" slots before=").Append(listObj.Count).Append('\n');
while (listObj.Count < 3) { var s = System.Activator.CreateInstance(cfT); cfT.GetField("sourceIndex").SetValue(s, listObj.Count*3); listObj.Add(s); }
SB.Append("slots now=").Append(listObj.Count).Append('\n');
pT.GetMethod("SetAsset", BF).Invoke(pw, new object[]{ copy });
pw.Repaint();
var p = pw.rootVisualElement.panel; var vm = p.GetType().GetMethod("ValidateLayout", BF);
for (int k=0;k<4;k++) if (vm!=null) vm.Invoke(p,null);
System.Action<UnityEngine.UIElements.VisualElement, System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> W2 = null;
W2 = (e,l) => { if (e==null) return; l.Add(e); for (int i=0;i<e.hierarchy.childCount;i++) W2(e.hierarchy[i], l); };
System.Func<System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> PT = () => { var l = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>(); W2(pw.rootVisualElement, l); return l; };
foreach (var v in PT()) if (_secT.IsInstanceOfType(v)) _secOpen.SetValue(v, true);
foreach (var v in PT()) if (_boxT.IsInstanceOfType(v)) _boxOpen.SetValue(v, true);
for (int k=0;k<4;k++) if (vm!=null) vm.Invoke(p,null);
int tilePx=0, tileSize=0; foreach (var v in PT()) { var t = TextOf(v); if (t=="Tile px") tilePx++; if (t=="Tile size") tileSize++; }
SB.Append("cherry captions in Pyre: \"Tile px\"=").Append(tilePx).Append("  \"Tile size\"=").Append(tileSize).Append('\n');
// open a slot editor popover through the shared panel
var panelT = FT2("PyreCherryPanel");
var pf = pT.GetField("cherryPanel", BF); var panel = pf==null?null:pf.GetValue(pw);
SB.Append("cherryPanel instance=").Append(panel!=null).Append('\n');
if (panel!=null) {
  var show = panelT.GetMethod("ShowSlotEditor", BF);
  // find a slot card to anchor on
  UnityEngine.UIElements.VisualElement card = null;
  foreach (var v in PT()) if (v.ClassListContains("zui-box") || v.GetType().Name.Contains("Slot")) { }
  foreach (var v in PT()) if (v.name!=null && v.name.Contains("slot")) card = v;
  if (card==null) foreach (var v in PT()) if (v is UnityEngine.UIElements.Button b2 && b2.text=="+ Add slot") card = b2;
  SB.Append("anchor card=").Append(card==null?"NOTFOUND":card.GetType().Name).Append('\n');
  if (card!=null && show!=null) { show.Invoke(panel, new object[]{ card, 0 });
    for (int k=0;k<3;k++) if (vm!=null) vm.Invoke(p,null);
    var pop = new System.Text.StringBuilder(); int mm=0;
    foreach (var v in PT()) if (v.ClassListContains("zui-popover")) { var l3 = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>(); W2(v,l3);
      foreach (var e in l3) { var t = TextOf(e); if (!string.IsNullOrEmpty(t)) pop.Append('"').Append(t).Append("\"(").Append(e.enabledInHierarchy?"live":"GREY").Append(") ");
        if (e.GetType().Name.Contains("MicroMinMax")) mm++; } }
    SB.Append("slot popover: ").Append(pop).Append('\n').Append("  MicroMinMax controls=").Append(mm).Append('\n');
    // overlap check inside the popover
    foreach (var v in PT()) if (v.ClassListContains("zui-popover")) { var l3 = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>(); W2(v,l3);
      int ov=0; foreach (var e in l3) { var r=e.worldBound; if (float.IsNaN(r.width)) continue;
        float cb=r.yMax, cr=r.xMax; for (int i=0;i<e.hierarchy.childCount;i++) { var c=e.hierarchy[i]; if (c.resolvedStyle.display==UnityEngine.UIElements.DisplayStyle.None) continue; if (c.worldBound.yMax>cb) cb=c.worldBound.yMax; if (c.worldBound.xMax>cr) cr=c.worldBound.xMax; }
        if (cb-r.yMax>4f || cr-r.xMax>2f) ov++; }
      SB.Append("  elements overflowing inside the popover: ").Append(ov).Append(" of ").Append(l3.Count).Append('\n'); } }
}
return SB.ToString();
