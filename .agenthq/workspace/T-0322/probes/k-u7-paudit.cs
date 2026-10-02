var sb=new System.Text.StringBuilder();
foreach (var n in new string[]{"ShaperWindow","PyreWindow"}) { var w=ZWin(n); if (w==null) continue;
  string rep = ZAudit(w, n+"-paused");
  sb.Append(n).Append("(paused) ");
  foreach (var k in new string[]{"elements","captionShort","overflowParentX","overflowParentY","overflowWindow","noTooltip","inertNoReason"}) sb.Append(k).Append("=").Append(ZCount[k]).Append(" ");
  sb.Append("\n").Append(ZDump("audit-"+n+"-paused", rep)).Append("\n"); }
return sb.ToString();
