var sb=new System.Text.StringBuilder();
var w=ZWin("TextSplashWindow");
foreach (var e in ZAll(w.rootVisualElement)) {
  var of = e as UnityEditor.UIElements.ObjectField; if (of==null || !ZDrawn(of)) continue;
  bool isBorder=false;
  foreach (var d in ZAll(of)) { var l=d as UnityEngine.UIElements.Label; if (l!=null && l.text!=null && l.text.Contains("Border Font")) isBorder=true; }
  if (!isBorder) continue;
  sb.Append("FIELD ").Append(of.worldBound).Append(" tip=").Append(ZTip(of)).Append("\n");
  var p = of.hierarchy.parent;
  for (int lvl=0; lvl<4 && p!=null; lvl++, p=p.hierarchy.parent) {
    sb.Append("  P").Append(lvl).Append(" ").Append(p.GetType().Name).Append(" cls=").Append(ZCls(p))
      .Append(" bound=").Append(p.worldBound).Append(" content=").Append(ZContentWorld(p)).Append(" dir=").Append(p.resolvedStyle.flexDirection)
      .Append(" kids=").Append(p.hierarchy.childCount).Append("\n");
    for (int i=0;i<p.hierarchy.childCount;i++) { var c=p.hierarchy[i]; sb.Append("      ").Append(i).Append(" ").Append(c.GetType().Name).Append(" '").Append(ZOwnText(c)).Append("' ").Append(c.worldBound).Append("\n"); }
  }
}
return sb.ToString();
