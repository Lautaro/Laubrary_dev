var win = ZWin("PyreWindow"); var sb=new System.Text.StringBuilder();
var boxT = ZType("ZuiBox"); var titleP = boxT.GetProperty("TitleText");
int titled=0, dup=0;
foreach (var e in ZAll(win.rootVisualElement)) {
  if (!boxT.IsInstanceOfType(e)) continue;
  string t = titleP.GetValue(e) as string; if (string.IsNullOrEmpty(t)) continue; titled++;
  for (var p = e.hierarchy.parent; p != null; p = p.hierarchy.parent) {
    if (!boxT.IsInstanceOfType(p)) continue;
    string pt = titleP.GetValue(p) as string;
    if (pt == t) { dup++; sb.Append("DUP '").Append(t).Append("'\n"); }
    break;
  }
}
sb.Insert(0, "titledBoxes=" + titled + " dupNesting=" + dup + "\n");
// per-group field counts + total dials
int dials=0; foreach (var e in ZAll(win.rootVisualElement)) if (ZDrawn(e) && ZIsLeafCtrl(e)) dials++;
sb.Append("drawnLeafControls=").Append(dials).Append("\n");
string rep = ZAudit(win, "Pyre-Jet-820");
foreach (var k in new string[]{"elements","drawn","controls","captionShort","overflowParentX","overflowParentY","overflowWindow","noTooltip","inertNoReason"}) sb.Append(k).Append("=").Append(ZCount[k]).Append(" ");
sb.Append("\n").Append(ZDump("pyre-jet-820", rep));
return sb.ToString();
