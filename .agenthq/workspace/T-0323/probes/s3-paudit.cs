var sb=new System.Text.StringBuilder();
foreach (var wn in new string[]{"ShaperWindow","PyreWindow"}) {
  var w=ZWin(wn); if (w==null) continue;
  string rep=ZAudit(w,wn+"-paused");
  sb.Append(wn).Append("-paused ");
  foreach (var k in new string[]{"elements","drawn","controls","captionShort","overflowParentX","overflowParentY","overflowWindow","noTooltip","inertNoReason"}) sb.Append(k).Append("=").Append(ZCount.ContainsKey(k)?ZCount[k]:-1).Append(" ");
  sb.Append("\n"); ZDump("audit-"+wn+"-paused", rep);
}
return sb.ToString();
