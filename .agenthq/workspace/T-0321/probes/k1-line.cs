var win = ZWin("PyreWindow"); var sb=new System.Text.StringBuilder();
foreach (var e in ZAll(win.rootVisualElement)) {
  if (!ZDrawn(e)) continue; var wb=e.worldBound;
  if (wb.y < 520 || wb.y > 560) continue;
  var rs = e.resolvedStyle;
  sb.Append(e.GetType().Name).Append(" cls=").Append(ZCls(e)).Append(" wb=").Append(wb)
    .Append(" bg=").Append(rs.backgroundColor).Append(" bT=").Append(rs.borderTopWidth).Append(" bB=").Append(rs.borderBottomWidth)
    .Append(" bTc=").Append(rs.borderTopColor).Append(" bBc=").Append(rs.borderBottomColor).Append("\n");
}
return sb.ToString();
