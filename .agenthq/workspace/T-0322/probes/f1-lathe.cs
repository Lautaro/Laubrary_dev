var sb=new System.Text.StringBuilder();
var w = ZWin("LatheWindow");
foreach (var e in ZAll(w.rootVisualElement)) {
  if (!ZDrawn(e)) continue;
  var wb = e.worldBound;
  if (Mathf.Abs(wb.y-356.44f)>1f && Mathf.Abs(wb.y-387.56f)>1f) continue;
  sb.Append(e.GetType().Name).Append(" '").Append(ZOwnText(e)).Append("' cap='").Append(ZCaption(e)).Append("' cls=").Append(ZCls(e)).Append(" wb=").Append(wb).Append(" name=").Append(e.name).Append("\n");
}
return sb.ToString();
