var sb = new System.Text.StringBuilder();
var w = ZWin("ShaperWindow");
string rep = ZAudit(w, "ShaperDemo-820");
sb.Append("Shaper@820 ");
foreach (var k in new string[]{"elements","drawn","controls","captionShort","overflowParentX","overflowParentY","overflowWindow","noTooltip","inertNoReason"})
  sb.Append(k).Append("=").Append(ZCount.ContainsKey(k)?ZCount[k]:-1).Append(" ");
sb.Append("\n").Append(ZDump("audit-shaper-820", rep));
return sb.ToString();
