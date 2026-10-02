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
var doc = UnityEngine.ScriptableObject.CreateInstance<Laubrary.Shaper.ShaperDocument>();
doc.canvasWidth = 96; doc.canvasHeight = 64; doc.frameCount = 8; doc.seed = 11u;
doc.lightRig.lights.Add(new Laubrary.Shaper.ShaperLight { name = "Key", enabled = true });
var A = (Laubrary.Shaper.ShaperLayer)newLayerM.Invoke(null, new object[]{ "Painted", doc });
A.root.primitive.kind = Laubrary.Shaper.ShaperPrimitiveKind.Ellipse;
A.root.primitive.ellipseRxDial = new ZUIValue(34f); A.root.primitive.ellipseRyDial = new ZUIValue(26f);
doc.layers.Add(A);
var B = (Laubrary.Shaper.ShaperLayer)newLayerM.Invoke(null, new object[]{ "Stencil", doc });
B.root.primitive.kind = Laubrary.Shaper.ShaperPrimitiveKind.Rect;
B.root.primitive.rectHalfWDial = new ZUIValue(22f); B.root.primitive.rectHalfHDial = new ZUIValue(34f);
B.root.transform.translate = new UnityEngine.Vector2(16f, 0f);
B.contributesToPicture = false;
doc.layers.Add(B);
int bid = doc.IdOf(B);
var m = A.mask;
int W = doc.canvasWidth, H = doc.canvasHeight, S = 3, cols = 3, rows = 2;
var sheet = new UnityEngine.Texture2D(W*S*cols + (cols+1)*4, H*S*rows + (rows+1)*4, UnityEngine.TextureFormat.RGBA32, false);
var clear = new UnityEngine.Color32[sheet.width*sheet.height];
for (int i=0;i<clear.Length;i++) clear[i]=new UnityEngine.Color32(24,24,28,255);
sheet.SetPixels32(clear);
System.Action<int,int,UnityEngine.Color32[]> Blit = (cx,cy,px) => {
  int ox = 4 + cx*(W*S+4), oy = 4 + (rows-1-cy)*(H*S+4);
  for (int y=0;y<H*S;y++) for (int x=0;x<W*S;x++) {
    var c = px[(y/S)*W + (x/S)];
    sheet.SetPixel(ox+x, oy+y, c.a>0 ? (UnityEngine.Color)c : new UnityEngine.Color(0.10f,0.10f,0.12f,1f)); } };
var order = new string[]{"no mask","Keep inside","Cut away","Keep overlap","Invert + Keep inside","Distance from edge fullAt 8"};
for (int i=0;i<order.Length;i++) {
  string k = order[i];
  m.invert = false; m.quantity = Laubrary.Shaper.ShaperMaskQuantity.Coverage; m.fullAt = new ZUIValue(1f);
  m.sourceLayerId = (k=="no mask") ? 0 : bid;
  if (k=="Keep inside") m.mode = Laubrary.Shaper.ShaperMaskMode.Clip;
  else if (k=="Cut away") m.mode = Laubrary.Shaper.ShaperMaskMode.Subtract;
  else if (k=="Keep overlap") m.mode = Laubrary.Shaper.ShaperMaskMode.Intersect;
  else if (k=="Invert + Keep inside") { m.mode = Laubrary.Shaper.ShaperMaskMode.Clip; m.invert = true; }
  else if (k.StartsWith("Distance")) { m.mode = Laubrary.Shaper.ShaperMaskMode.Clip; m.quantity = Laubrary.Shaper.ShaperMaskQuantity.EdgeDistance; m.fullAt = new ZUIValue(8f); }
  var px = Laubrary.Shaper.ShaperDocumentRenderer.RenderFrame(doc, 0);
  int lit=0; foreach (var c in px) if (c.a>0) lit++;
  Blit(i%cols, i/cols, px);
  SB.Append(i).Append(". ").Append(k).Append(" lit=").Append(lit).Append('\n');
}
sheet.Apply();
string path = @"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0293\shots\a22-mask-sheet.png";
System.IO.File.WriteAllBytes(path, UnityEngine.ImageConversion.EncodeToPNG(sheet));
UnityEngine.Object.DestroyImmediate(sheet);
UnityEngine.Object.DestroyImmediate(doc);
SB.Append("row 1: no mask | Keep inside | Cut away ; row 2: Keep overlap | Invert+Keep inside | Distance from edge fullAt 8\n");
return SB.ToString();
