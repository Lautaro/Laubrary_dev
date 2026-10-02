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
var BFs = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
var WT = typeof(Laubrary.Shaper.Editor.ShaperWindow);
var newLayerM = WT.GetMethod("NewLayer", BFs);
var newHeightM = WT.GetMethod("NewHeightStage", BFs);
var doc = UnityEngine.ScriptableObject.CreateInstance<Laubrary.Shaper.ShaperDocument>();
doc.canvasWidth = 64; doc.canvasHeight = 64; doc.frameCount = 4; doc.seed = 7u;
doc.lightRig.lights.Add(new Laubrary.Shaper.ShaperLight { name = "Key", enabled = true });
var A = (Laubrary.Shaper.ShaperLayer)newLayerM.Invoke(null, new object[]{ "Slab", doc });
A.root.primitive.kind = Laubrary.Shaper.ShaperPrimitiveKind.Rect;
A.root.primitive.rectHalfWDial = new ZUIValue(24f); A.root.primitive.rectHalfHDial = new ZUIValue(24f);
A.height = newHeightM != null ? (Laubrary.Shaper.ShaperHeightDef)newHeightM.Invoke(null, null) : new Laubrary.Shaper.ShaperHeightDef();
(A.response ?? (A.response = new Laubrary.Shaper.ShaperLightResponse())).normalKind = Laubrary.Shaper.ShaperNormalKind.Profile;
doc.layers.Add(A);
SB.Append("height stage seeded: depth=").Append(Laubrary.Shaper.ShaperValue.Sample(A.height.depth,0f,0u,0f)).Append(" technique=").Append(A.height.technique).Append(" bevel=").Append(A.height.bevel).Append('\n');
int W = doc.canvasWidth, H = doc.canvasHeight, S = 3, cols = 7, rows = 2;
var sheet = new UnityEngine.Texture2D(W*S*cols + (cols+1)*4, H*S*rows + (rows+1)*4, UnityEngine.TextureFormat.RGBA32, false);
var clear = new UnityEngine.Color32[sheet.width*sheet.height];
for (int i=0;i<clear.Length;i++) clear[i]=new UnityEngine.Color32(24,24,28,255);
sheet.SetPixels32(clear);
System.Action<int,int,UnityEngine.Color32[]> Blit = (cx,cy,px) => {
  int ox = 4 + cx*(W*S+4), oy = 4 + (rows-1-cy)*(H*S+4);
  for (int y=0;y<H*S;y++) for (int x=0;x<W*S;x++) {
    var c = px[(y/S)*W + (x/S)];
    sheet.SetPixel(ox+x, oy+y, c.a>0 ? (UnityEngine.Color)c : new UnityEngine.Color(0.10f,0.10f,0.12f,1f)); } };
System.Func<UnityEngine.Color32[],ulong> Hash = px => { ulong h=1469598103934665603UL; foreach (var c in px) { h^=c.r; h*=1099511628211UL; h^=c.g; h*=1099511628211UL; h^=c.b; h*=1099511628211UL; h^=c.a; h*=1099511628211UL; } return h; };
var seen = new System.Collections.Generic.Dictionary<ulong,string>();
// row 1 — the seven extrusion profiles, bevel None
int ci=0;
foreach (Laubrary.Shaper.ShaperExtrusionTechnique t in System.Enum.GetValues(typeof(Laubrary.Shaper.ShaperExtrusionTechnique))) {
  A.height.technique = t; A.height.bevel = Laubrary.Shaper.ShaperBevelTechnique.None;
  var px = Laubrary.Shaper.ShaperDocumentRenderer.RenderFrame(doc, 0);
  int lit=0; foreach (var c in px) if (c.a>0) lit++;
  var hh = Hash(px);
  SB.Append("profile ").Append(t).Append(" lit=").Append(lit).Append(" hash=").Append(hh.ToString("x16")).Append(seen.ContainsKey(hh)?("  SAME AS "+seen[hh]):"").Append('\n');
  if (!seen.ContainsKey(hh)) seen[hh] = "profile "+t;
  Blit(ci++, 0, px);
}
// row 2 — the six bevels on a Dome profile
A.height.technique = Laubrary.Shaper.ShaperExtrusionTechnique.Dome;
var seen2 = new System.Collections.Generic.Dictionary<ulong,string>();
ci=0;
foreach (Laubrary.Shaper.ShaperBevelTechnique b in System.Enum.GetValues(typeof(Laubrary.Shaper.ShaperBevelTechnique))) {
  A.height.bevel = b;
  var px = Laubrary.Shaper.ShaperDocumentRenderer.RenderFrame(doc, 0);
  int lit=0; foreach (var c in px) if (c.a>0) lit++;
  var hh = Hash(px);
  SB.Append("bevel ").Append(b).Append(" lit=").Append(lit).Append(" hash=").Append(hh.ToString("x16")).Append(seen2.ContainsKey(hh)?("  SAME AS "+seen2[hh]):"").Append('\n');
  if (!seen2.ContainsKey(hh)) seen2[hh] = "bevel "+b;
  Blit(ci++, 1, px);
}
sheet.Apply();
string path = @"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0293\shots\a22-height-sheet.png";
System.IO.File.WriteAllBytes(path, UnityEngine.ImageConversion.EncodeToPNG(sheet));
UnityEngine.Object.DestroyImmediate(sheet); UnityEngine.Object.DestroyImmediate(doc);
SB.Append("row 1 = Flat Linear Stepped Dome Round Taper Pyramid ; row 2 = bevel None Linear Rounded Cove Ogee Stepped (Dome profile)\n");
return SB.ToString();
