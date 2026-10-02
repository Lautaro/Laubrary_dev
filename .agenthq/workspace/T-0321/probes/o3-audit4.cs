var sb=new System.Text.StringBuilder();
string[] wants = {"ChunkWindow","ZoeWindow","MirageWindow","LauminationBuilderWindow"};
var secT = ZType("ZuiSection"); var isOpen = secT.GetProperty("IsOpen");
var boxT = ZType("ZuiBox");
foreach (var n in wants) {
  var w = ZWin(n); if (w==null) { sb.Append(n).Append(" NULL\n"); continue; }
  w.position = new Rect(20, 20, 900, 880); w.Repaint();
  int ns=0; foreach (var e in ZAll(w.rootVisualElement)) if (secT.IsInstanceOfType(e)) { isOpen.SetValue(e,true); ns++; }
  string rep = ZAudit(w, n);
  sb.Append(n).Append(" sections=").Append(ns).Append(" ");
  foreach (var k in new string[]{"elements","drawn","controls","captionShort","overflowParentX","overflowParentY","overflowWindow","noTooltip","inertNoReason"}) sb.Append(k).Append("=").Append(ZCount[k]).Append(" ");
  sb.Append("\n").Append(ZDump("audit-"+n, rep)).Append("\n");
}
return sb.ToString();
