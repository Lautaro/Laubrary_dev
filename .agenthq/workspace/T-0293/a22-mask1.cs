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
var win = Win(); var wt = win.GetType();
var docp = P(wt,"Current"); var doc = docp.GetValue(win) as Laubrary.Shaper.ShaperDocument;
SB.Append("doc=").Append(doc==null?"<none>":doc.name).Append(" layers=").Append(doc.layers.Count).Append('\n');
// add two layers through the real button
SB.Append(Press("+ Add layer")).Append('\n');
SB.Append(Press("+ Add layer")).Append('\n');
SB.Append("layers now = ").Append(doc.layers.Count).Append(" names: ");
foreach (var l in doc.layers) SB.Append(l.name).Append('/').Append(l.id).Append(' ');
SB.Append('\n');
// make layer 0 a big ellipse, layer 1 a big offset rectangle used as the mask source
var l0 = doc.layers[0]; var l1 = doc.layers[1];
l0.root.kind = Laubrary.Shaper.ShaperNodeKind.Primitive;
l0.root.primitive.kind = Laubrary.Shaper.ShaperPrimitiveKind.Ellipse;
l0.root.primitive.ellipseRxDial = new ZUIValue(34f); l0.root.primitive.ellipseRyDial = new ZUIValue(26f);
l1.root.kind = Laubrary.Shaper.ShaperNodeKind.Primitive;
l1.root.primitive.kind = Laubrary.Shaper.ShaperPrimitiveKind.Rect;
l1.root.primitive.rectHalfWDial = new ZUIValue(22f); l1.root.primitive.rectHalfHDial = new ZUIValue(34f);
l1.root.transform.translate = new UnityEngine.Vector2(16f, 0f);
UnityEditor.EditorUtility.SetDirty(doc);
System.Func<int,int> Lit = f => { var px = Laubrary.Shaper.ShaperDocumentRenderer.RenderFrame(doc, f); int n=0; foreach (var c in px) if (c.a>0) n++; return n; };
SB.Append("lit@0 with 3 layers = ").Append(Lit(0)).Append('\n');
// select layer 0 so the Mask card is for the ellipse
F(wt,"selectedLayer").SetValue(win, 0);
M(wt,"Rebuild").Invoke(win, null);
SB.Append("selectedLayer=0, mask set=").Append(doc.layers[0].mask==null?"<null>":doc.layers[0].mask.IsSet.ToString()).Append('\n');
return SB.ToString();
