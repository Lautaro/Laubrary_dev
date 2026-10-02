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
// work on a DUPLICATE, never a user asset
string src = "Assets/Pyre/New Pyre Plus.asset", dst = "Assets/Shaper/AuditA22Pyre.asset";
if (UnityEditor.AssetDatabase.LoadAssetAtPath(dst, assetT) == null) {
  UnityEditor.AssetDatabase.CopyAsset(src, dst); UnityEditor.AssetDatabase.ImportAsset(dst); }
var copy = UnityEditor.AssetDatabase.LoadAssetAtPath(dst, assetT);
SB.Append("duplicate loaded=").Append(copy!=null).Append(" at ").Append(dst).Append('\n');
var pw = UnityEditor.EditorWindow.GetWindow(pT);
pT.GetMethod("SetAsset", BF).Invoke(pw, new object[]{ copy });
pw.position = new UnityEngine.Rect(60,60,1600,1150); pw.Show(); pw.Repaint();
var p = pw.rootVisualElement.panel; var vm = p.GetType().GetMethod("ValidateLayout", BF);
for (int k=0;k<4;k++) if (vm!=null) vm.Invoke(p,null);
System.Action<UnityEngine.UIElements.VisualElement, System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> W2 = null;
W2 = (e,l) => { if (e==null) return; l.Add(e); for (int i=0;i<e.hierarchy.childCount;i++) W2(e.hierarchy[i], l); };
var all = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>(); W2(pw.rootVisualElement, all);
SB.Append("Pyre window elements=").Append(all.Count).Append('\n');
// open every section and box
foreach (var v in all) if (_secT.IsInstanceOfType(v)) _secOpen.SetValue(v, true);
foreach (var v in all) if (_boxT.IsInstanceOfType(v)) _boxOpen.SetValue(v, true);
for (int k=0;k<4;k++) if (vm!=null) vm.Invoke(p,null);
all.Clear(); W2(pw.rootVisualElement, all);
SB.Append("after opening everything: elements=").Append(all.Count).Append('\n');
int xh=0, yh=0; var worst = new System.Text.StringBuilder();
foreach (var v in all) {
  if (v.resolvedStyle.display == UnityEngine.UIElements.DisplayStyle.None) continue;
  var r = v.worldBound; if (float.IsNaN(r.width)||r.width<=0) continue;
  var cls = string.Join(",", v.GetClasses());
  if (cls.Contains("unity-scroll-view")||cls.Contains("unity-scroller")||cls.Contains("dragline")||cls.Contains("unity-base-slider")) continue;
  if (v.GetType().Name.Contains("Filmstrip")) continue;
  float cr=r.xMax, cb=r.yMax; UnityEngine.UIElements.VisualElement wx=null, wy=null;
  for (int i=0;i<v.hierarchy.childCount;i++) { var c=v.hierarchy[i];
    if (c.resolvedStyle.display==UnityEngine.UIElements.DisplayStyle.None) continue;
    if (c.worldBound.xMax>cr) { cr=c.worldBound.xMax; wx=c; }
    if (c.worldBound.yMax>cb) { cb=c.worldBound.yMax; wy=c; } }
  if (cr-r.xMax>2f) { xh++; if (xh<=6) worst.Append("   X+").Append((cr-r.xMax).ToString("0.0")).Append(' ').Append(v.GetType().Name).Append('[').Append(cls).Append("] child=\"").Append(wx==null?"":TextOf(wx)).Append("\"\n"); }
  if (cb-r.yMax>4f) { yh++; if (yh<=6) worst.Append("   Y+").Append((cb-r.yMax).ToString("0.0")).Append(' ').Append(v.GetType().Name).Append('[').Append(cls).Append("] child=\"").Append(wy==null?"":TextOf(wy)).Append("\"\n"); } }
SB.Append("Pyre overflow scan: X=").Append(xh).Append(" Y=").Append(yh).Append('\n').Append(worst);
// the programme's ZUI touchpoints
int applyBtns=0, tilePx=0, tileSize=0, minMax=0, hdrToggles=0, wrapRadios=0, fields=0, stampedFields=0;
foreach (var v in all) {
  var t = TextOf(v);
  if (v is UnityEngine.UIElements.Button b) { if (b.text=="Apply") applyBtns++; }
  if (t=="Tile px") tilePx++; if (t=="Tile size") tileSize++;
  if (v.GetType().Name.Contains("MicroMinMax")) minMax++;
  if (v.ClassListContains("zui-radio--wrap")) wrapRadios++;
  if (v.ClassListContains("zui-field")) { fields++; if (v.style.height.keyword == UnityEngine.UIElements.StyleKeyword.Undefined) stampedFields++; }
}
SB.Append("Apply buttons=").Append(applyBtns).Append("  \"Tile px\"=").Append(tilePx).Append("  \"Tile size\"=").Append(tileSize)
  .Append("  MicroMinMax=").Append(minMax).Append("  wrapping radios=").Append(wrapRadios)
  .Append("  zui-fields=").Append(fields).Append(" (height-stamped=").Append(stampedFields).Append(")\n");
return SB.ToString();
