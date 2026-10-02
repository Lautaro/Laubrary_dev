var win = ZWin("PyreWindow"); var sb=new System.Text.StringBuilder();
foreach (var e in ZAll(win.rootVisualElement.panel.visualTree)) {
  if (!ZLaidOut(e)) continue; var wb=e.worldBound;
  if (wb.height > 3f) continue; if (wb.width < 100f) continue;
  sb.Append(e.GetType().Name).Append(" cls=").Append(ZCls(e)).Append(" wb=").Append(wb).Append(" bg=").Append(e.resolvedStyle.backgroundColor).Append(" disp=").Append(ZDisplayed(e)).Append("\n");
}
return sb.ToString();
