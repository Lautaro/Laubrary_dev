var sb=new System.Text.StringBuilder();
var secT = ZType("ZuiSection"); var isOpen = secT.GetProperty("IsOpen");
foreach (var n in new string[]{"ZoeWindow","MirageWindow","ChunkWindow"}) {
  var w = ZWin(n); if (w==null) continue;
  foreach (var e in ZAll(w.rootVisualElement)) if (secT.IsInstanceOfType(e)) isOpen.SetValue(e,true);
  string rep = ZAudit(w, n+"-populated");
  sb.Append(n).Append(" ");
  foreach (var k in new string[]{"elements","drawn","controls","captionShort","overflowParentX","overflowParentY","overflowWindow","noTooltip","inertNoReason"}) sb.Append(k).Append("=").Append(ZCount[k]).Append(" ");
  sb.Append("\n").Append(ZDump("audit-"+n+"-pop", rep)).Append("\n");
}
return sb.ToString();
