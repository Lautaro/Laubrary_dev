var sb=new System.Text.StringBuilder();
string wn=UnityEditor.EditorPrefs.GetString("T323.pressWin","LatheWindow");
string bt=UnityEditor.EditorPrefs.GetString("T323.pressText","Twist");
string ap=UnityEditor.EditorPrefs.GetString("T323.pressAsset","");
var w=ZWin(wn); if (w==null) return "no window";
sb.Append("BEFORE ").Append(ZState(w,ap)).Append("\n");
UnityEngine.UIElements.VisualElement hit=null;
foreach (var e in ZAll(w.rootVisualElement.panel.visualTree)) if (ZCls(e).Contains("zui-popover") && e.worldBound.width<600) {
  foreach (var d in ZAll(e)) { var t=d as UnityEngine.UIElements.TextElement; if (t!=null && t.text==bt) { hit = (d.hierarchy.parent!=null && (d.hierarchy.parent is UnityEngine.UIElements.Button))? d.hierarchy.parent : d; } }
}
if (hit==null) return sb.Append("no item '").Append(bt).Append("'\n").ToString();
sb.Append("item ").Append(hit.GetType().Name).Append(" ").Append(hit.worldBound).Append("\n");
ZClick(hit);
sb.Append("AFTER  ").Append(ZState(w,ap)).Append("\n");
return sb.ToString();
