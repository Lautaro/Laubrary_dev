var sb=new System.Text.StringBuilder();
string wn = UnityEditor.EditorPrefs.GetString("T322.auditWin","LatheWindow");
var w = ZWin(wn);
var secT = ZType("ZuiSection"); var isOpen = secT.GetProperty("IsOpen");
foreach (var e in ZAll(w.rootVisualElement)) if (secT.IsInstanceOfType(e)) isOpen.SetValue(e, true);
w.Repaint();
foreach (var e in ZAll(w.rootVisualElement)) {
  if (!ZDrawn(e) || e.parent==null || ZIsChrome(e)) continue;
  var pc = ZContentWorld(e.parent); var wb = e.worldBound;
  float sp = wb.xMax - pc.xMax;
  if (sp <= 1.5f) continue;
  sb.Append("SPILL ").Append(sp.ToString("F1")).Append(" ").Append(e.GetType().Name).Append(" '").Append(ZOwnText(e)).Append("' cap='").Append(ZCaption(e)).Append("' cls=").Append(ZCls(e)).Append(" wb=").Append(wb).Append("\n");
  sb.Append("   parent=").Append(e.parent.GetType().Name).Append(" cls=").Append(ZCls(e.parent)).Append(" pc=").Append(pc).Append(" path=").Append(ZPath(e)).Append("\n");
  var kids=new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>(); ZWalk(e,kids);
  int shown=0; foreach (var k in kids) { if (k==e||!ZDrawn(k)) continue; if (shown++>6) break; sb.Append("   > ").Append(k.GetType().Name).Append(" '").Append(ZOwnText(k)).Append("' cls=").Append(ZCls(k)).Append(" wb=").Append(k.worldBound).Append("\n"); }
}
return sb.ToString();
