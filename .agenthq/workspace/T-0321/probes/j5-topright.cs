var win = ZWin("PyreWindow"); var sb=new System.Text.StringBuilder();
foreach (var e in ZAll(win.rootVisualElement)) {
  if (!ZDrawn(e)) continue; var wb=e.worldBound;
  if (wb.x < 380 || wb.y > 110 || wb.y < 30) continue;
  sb.Append(e.GetType().Name).Append(" '").Append(ZOwnText(e)).Append("' cls=").Append(ZCls(e)).Append(" wb=").Append(wb).Append(" path=").Append(ZPath(e)).Append("\n");
}
return sb.ToString();
