var win = ZWin("PyreWindow"); var sb=new System.Text.StringBuilder();
sb.Append("win=").Append(win.position).Append("\n");
foreach (var e in ZAll(win.rootVisualElement)) {
  if (!ZDrawn(e)) continue; var wb=e.worldBound;
  if (System.Math.Abs(wb.y-540f) > 12f && System.Math.Abs(wb.yMax-540f) > 12f) continue;
  if (wb.width < 200f) continue;
  sb.Append(e.GetType().Name).Append(" cls=").Append(ZCls(e)).Append(" wb=").Append(wb).Append(" bg=").Append(e.resolvedStyle.backgroundColor).Append(" bB=").Append(e.resolvedStyle.borderBottomWidth).Append(" bBc=").Append(e.resolvedStyle.borderBottomColor).Append(" bT=").Append(e.resolvedStyle.borderTopWidth).Append(" bTc=").Append(e.resolvedStyle.borderTopColor).Append("\n");
}
return sb.ToString();
