// Controlled test: does picking a shape through the picker's own menu write to the document?
var w = ZWin("ShaperWindow"); if (w == null) return "no window";
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.FieldInfo assetF=null; for (var t=w.GetType(); t!=null && assetF==null; t=t.BaseType) assetF=t.GetField("asset",BFi);
System.Func<string> kind = () => {
  var doc = assetF.GetValue(w);
  var layers = doc.GetType().GetField("layers", BFi).GetValue(doc) as System.Collections.IList;
  var root = layers[0].GetType().GetField("root", BFi).GetValue(layers[0]);
  var prim = root.GetType().GetField("primitive", BFi).GetValue(root);
  return root.GetType().GetField("kind", BFi).GetValue(root) + "/" + prim.GetType().GetField("kind", BFi).GetValue(prim);
};
var sb = new System.Text.StringBuilder();
int nWin = 0; foreach (var x in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>()) if (x.GetType().Name=="ShaperWindow") nWin++;
sb.Append("shaperWindows=").Append(nWin).Append(" asset=").Append(UnityEditor.AssetDatabase.GetAssetPath(assetF.GetValue(w) as UnityEngine.Object)).Append("\n");
sb.Append("BEFORE ").Append(kind()).Append("\n");
// find the Shape section picker
var secT = ZType("ZuiSection"); UnityEngine.UIElements.VisualElement shapeSec = null;
foreach (var e in ZAll(w.rootVisualElement)) { if (!secT.IsInstanceOfType(e) || !ZDrawn(e)) continue;
  foreach (var c in ZAll(e)) { var l=c as UnityEngine.UIElements.Label; if (l!=null && l.ClassListContains("zui-section__title") && l.text.StartsWith("Shape")) { shapeSec=e; break; } }
  if (shapeSec != null) break; }
if (shapeSec == null) return sb.Append("no Shape section").ToString();
UnityEngine.UIElements.Button pick = null;
foreach (var e in ZAll(shapeSec)) { var b=e as UnityEngine.UIElements.Button; if (b!=null && ZDrawn(b) && b.hierarchy.childCount>0) { pick=b; break; } }
var wr = w.rootVisualElement.worldBound;
sb.Append("picker=").Append(pick.worldBound).Append(" root=").Append(wr).Append("\n");
if (pick.worldBound.yMax > wr.yMax) { UnityEngine.UIElements.ScrollView sv=null; for (var p=pick.hierarchy.parent;p!=null;p=p.hierarchy.parent){var s=p as UnityEngine.UIElements.ScrollView; if(s!=null){sv=s;break;}} if(sv!=null){sv.ScrollTo(pick); w.Repaint(); return sb.Append("scrolled - rerun").ToString(); } }
ZClick(pick);
var panelRoot = w.rootVisualElement.panel.visualTree;
UnityEngine.UIElements.VisualElement target = null;
foreach (var e in ZAll(panelRoot)) {
  if (!e.ClassListContains("zui-menu__item")) continue;
  string label = null;
  foreach (var c in ZAll(e)) { var l=c as UnityEngine.UIElements.Label; if (l!=null && l.text!="✓" && !string.IsNullOrEmpty(l.text)) { label=l.text; break; } }
  if (label == "Ellipse") { target = e; break; }
}
if (target == null) return sb.Append("no Ellipse entry").ToString();
sb.Append("entry rect=").Append(target.worldBound).Append(" drawn=").Append(ZDrawn(target)).Append("\n");
ZClick(target);
sb.Append("AFTER-immediate ").Append(kind()).Append("\n");
return sb.ToString();
