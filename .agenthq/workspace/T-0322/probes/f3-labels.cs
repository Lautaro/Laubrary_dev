var sb=new System.Text.StringBuilder();
var w = ZWin("LatheWindow");
foreach (var e in ZAll(w.rootVisualElement)) {
  if (!ZDrawn(e)) continue;
  var t = ZOwnText(e);
  if (t!="X" && t!="Y" && t!="Z") continue;
  sb.Append("'").Append(t).Append("' w=").Append(e.worldBound.width.ToString("F2")).Append(" x=").Append(e.worldBound.x.ToString("F1")).Append(" y=").Append(e.worldBound.y.ToString("F1")).Append(" minW=").Append(e.resolvedStyle.minWidth).Append(" pl=").Append(e.resolvedStyle.paddingLeft).Append(" flexBasis=").Append(e.resolvedStyle.flexBasis).Append("\n");
}
// what is the outer field label?
foreach (var e in ZAll(w.rootVisualElement)) {
  if (!ZDrawn(e)) continue; var cl=ZCls(e);
  if (cl.IndexOf("zui-field__label")<0) continue;
  if (e.worldBound.y<340 || e.worldBound.y>470) continue;
  sb.Append("LBL '").Append(ZOwnText(e)).Append("' w=").Append(e.worldBound.width.ToString("F2")).Append(" x=").Append(e.worldBound.x.ToString("F1")).Append(" y=").Append(e.worldBound.y.ToString("F1")).Append("\n");
}
return sb.ToString();
