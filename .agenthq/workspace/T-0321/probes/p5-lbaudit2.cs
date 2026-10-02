var sb=new System.Text.StringBuilder();
var w = ZWin("LauminationBuilderWindow");
string rep = ZAudit(w, "LB-loaded");
sb.Append("LB-loaded ");
foreach (var k in new string[]{"elements","drawn","controls","captionShort","overflowParentX","overflowParentY","overflowWindow","noTooltip","inertNoReason"}) sb.Append(k).Append("=").Append(ZCount[k]).Append(" ");
sb.Append("\n").Append(ZDump("audit-LB-loaded", rep)).Append("\n");
// section titles carrying a sentence
var secT = ZType("ZuiSection");
foreach (var e in ZAll(w.rootVisualElement)) {
  var l = e as UnityEngine.UIElements.Label; if (l==null) continue;
  var cl = ZCls(l)??"";
  if (!cl.Contains("zui-section__title") && !cl.Contains("zui-box__title")) continue;
  sb.Append("TITLE '").Append(l.text).Append("' len=").Append(l.text.Length).Append(" tip='").Append((ZTip(l)??"").Length>40?(ZTip(l).Substring(0,40)+"…"):ZTip(l)).Append("'\n");
}
return sb.ToString();
