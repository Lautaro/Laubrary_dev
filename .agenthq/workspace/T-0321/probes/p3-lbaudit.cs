var sb=new System.Text.StringBuilder();
var w = ZWin("LauminationBuilderWindow");
string rep = ZAudit(w, "LauminationBuilder-populated");
sb.Append("LB-populated ");
foreach (var k in new string[]{"elements","drawn","controls","captionShort","overflowParentX","overflowParentY","overflowWindow","noTooltip","inertNoReason"}) sb.Append(k).Append("=").Append(ZCount[k]).Append(" ");
sb.Append("\n").Append(ZDump("audit-LB-populated", rep));
return sb.ToString();
