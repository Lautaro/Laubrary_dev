var win = ZWin("PyreWindow"); if (win==null) return "no pyre";
var sb = new System.Text.StringBuilder();
foreach (var e in ZAll(win.rootVisualElement)) {
  if (!ZDrawn(e)) continue;
  var te = e as UnityEngine.UIElements.TextElement; if (te == null) continue;
  if (!e.ClassListContains("zui-microslider__value")) continue;
  float need = ZNeed(te), have = ZHave(te);
  if (need <= have + ZTOL) continue;
  // walk up to the ZuiValueControl and report its label + the row it sits in
  UnityEngine.UIElements.VisualElement vc = null, row = null;
  for (var p = e.hierarchy.parent; p != null; p = p.hierarchy.parent) {
    if (vc == null && p.GetType().Name == "ZuiValueControl") vc = p;
    if (p.ClassListContains("zui-row") || (p.resolvedStyle.flexWrap == UnityEngine.UIElements.Wrap.Wrap && p.hierarchy.childCount > 1)) { row = p; break; }
  }
  sb.AppendLine("CLIPPED '" + te.text + "' need=" + need.ToString("F1") + " have=" + have.ToString("F1") + " rect=" + e.worldBound);
  if (vc != null) { sb.AppendLine("  valueControl='" + ZCaption(vc) + "' rect=" + vc.worldBound + " tip=" + ZTip(vc).Substring(0, System.Math.Min(70, ZTip(vc).Length))); }
  if (row != null) { sb.AppendLine("  row=" + row.GetType().Name + " " + row.worldBound + " children:");
    for (int i=0;i<row.hierarchy.childCount;i++) { var c = row.hierarchy[i]; sb.AppendLine("    " + i + " " + c.GetType().Name + " '" + ZCaption(c) + "' " + c.worldBound); } }
}
return sb.Length == 0 ? "none clipped" : sb.ToString();
