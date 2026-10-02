var sb=new System.Text.StringBuilder();
var w = ZWin("CartographerWindow");
foreach (var e in ZAll(w.rootVisualElement)) {
  if (!ZDrawn(e) || !ZCls(e).Contains("zui-row")) continue;
  if (Mathf.Abs(e.worldBound.y-516.89f)>2f) continue;
  sb.Append("ROW ").Append(e.worldBound).Append(" content=").Append(ZContentWorld(e).width.ToString("F1")).Append("\n");
  for (int i=0;i<e.hierarchy.childCount;i++) { var c=e.hierarchy[i];
    sb.Append("  child ").Append(c.GetType().Name).Append(" cap='").Append(ZCaption(c)).Append("' wb=").Append(c.worldBound).Append(" grow=").Append(c.resolvedStyle.flexGrow).Append(" shrink=").Append(c.resolvedStyle.flexShrink).Append("\n"); }
}
return sb.ToString();
