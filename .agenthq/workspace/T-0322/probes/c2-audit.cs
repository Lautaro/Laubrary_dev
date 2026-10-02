var sb=new System.Text.StringBuilder();
string wn = UnityEditor.EditorPrefs.GetString("T322.auditWin","");
var w = ZWin(wn); if (w==null) return "no window "+wn;
var secT = ZType("ZuiSection"); var isOpen = secT.GetProperty("IsOpen"); int n=0;
foreach (var e in ZAll(w.rootVisualElement)) if (secT.IsInstanceOfType(e)) { isOpen.SetValue(e, true); n++; }
var boxT = ZType("ZuiBox"); var bOpen = boxT.GetProperty("IsOpen"); int nb=0;
foreach (var e in ZAll(w.rootVisualElement)) if (boxT.IsInstanceOfType(e) && bOpen!=null) { bOpen.SetValue(e, true); nb++; }
w.Repaint();
string rep = ZAudit(w, wn);
sb.Append(wn).Append(" sections=").Append(n).Append(" boxes=").Append(nb).Append(" ");
foreach (var k in new string[]{"elements","drawn","controls","leafControls","captionShort","overflowParentX","overflowParentY","overflowWindow","noTooltip","inertNoReason"}) sb.Append(k).Append("=").Append(ZCount.ContainsKey(k)?ZCount[k]:-1).Append(" ");
sb.Append("\n").Append(ZDump("audit-"+wn, rep));
return sb.ToString();
