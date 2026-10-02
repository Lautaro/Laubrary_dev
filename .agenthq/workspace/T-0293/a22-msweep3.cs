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
var docp = P(wt,"Current"); var doc = docp.GetValue(win) as Laubrary.Shaper.ShaperDocument;
var m = doc.layers[0].mask;
System.Func<int,UnityEngine.Color32[]> Px = f => Laubrary.Shaper.ShaperDocumentRenderer.RenderFrame(doc, f);
System.Func<UnityEngine.Color32[],int> LitOf = a => { int n=0; foreach (var c in a) if (c.a>0) n++; return n; };
System.Func<UnityEngine.Color32[],UnityEngine.Color32[],int> Diff = (a,b) => { int n=0; for (int i=0;i<a.Length;i++) if (!a[i].Equals(b[i])) n++; return n; };
System.Func<System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> MaskKids = () => {
  UnityEngine.UIElements.VisualElement box = null;
  foreach (var v in Tree()) if (_boxT.IsInstanceOfType(v) && BoxTitle(v)=="Mask") box = v;
  var l = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>(); if (box!=null) WalkT(box, l); return l; };
System.Func<string,UnityEngine.UIElements.VisualElement> Opt = label => { foreach (var v in MaskKids()) if (TextOf(v)==label) return v; return null; };
F(wt,"selectedLayer").SetValue(win, 0); M(wt,"Rebuild").Invoke(win,null); OnlySection("Layers"); OpenBoxes("Mask"); Layout();
// Opacity vs Height, with the source now extruded
Click(Opt("Opacity"),""); Layout(); var op = Px(0);
SB.Append("source EXTRUDED — Opacity lit=").Append(LitOf(op)).Append('\n');
Click(Opt("Height"),""); Layout(); var hg = Px(0);
SB.Append("               Height  lit=").Append(LitOf(hg)).Append(" changed vs Opacity=").Append(Diff(op,hg)).Append(" q=").Append(m.quantity).Append('\n');
// "Fully masked at" really is read: 1 -> 8
var fa = m.fullAt; m.fullAt = new ZUIValue(8f); var hg8 = Px(0);
SB.Append("               Fully masked at 1 -> 8 changed=").Append(Diff(hg,hg8)).Append('\n');
m.fullAt = fa;
// ── missing source: delete Layer 2 through its own × button ──
Click(Opt("Opacity"),""); Layout();
F(wt,"selectedLayer").SetValue(win, 1); M(wt,"Rebuild").Invoke(win,null); OnlySection("Layers"); Layout();
// the layer row's × for row index 1
var xs = new System.Collections.Generic.List<UnityEngine.UIElements.Button>();
foreach (var v in Tree()) if (v is UnityEngine.UIElements.Button b && b.text=="×" && b.tooltip!=null && b.tooltip.Contains("layer")) xs.Add(b);
SB.Append("layer × buttons found=").Append(xs.Count).Append('\n');
if (xs.Count>=2) { Click(xs[1],""); }
SB.Append("layers now=").Append(doc.layers.Count).Append(" mask.sourceLayerId=").Append(m.sourceLayerId).Append(" IsSet=").Append(m.IsSet).Append('\n');
F(wt,"selectedLayer").SetValue(win, 0); M(wt,"Rebuild").Invoke(win,null); OnlySection("Layers"); OpenBoxes("Mask"); Layout();
var t3 = new System.Text.StringBuilder();
foreach (var v in MaskKids()) { var t = TextOf(v); if (!string.IsNullOrEmpty(t)) t3.Append('"').Append(t).Append("\" "); }
SB.Append("mask card after the source was deleted: ").Append(t3).Append('\n');
var missBtn = Opt("Missing layer");
SB.Append("  \"Missing layer\" button present=").Append(missBtn!=null).Append(" tip=\"").Append(missBtn==null?"":(missBtn.tooltip.Length>90?missBtn.tooltip.Substring(0,90)+"…":missBtn.tooltip)).Append("\"\n");
var clr = Opt("Clear");
SB.Append("  \"Clear\" present=").Append(clr!=null).Append('\n');
if (clr!=null) { Click(clr,""); Layout(); SB.Append("  pressed Clear -> IsSet=").Append(m.IsSet).Append(" mask card present=").Append(MaskKids().Count>0).Append('\n'); }
return SB.ToString();
