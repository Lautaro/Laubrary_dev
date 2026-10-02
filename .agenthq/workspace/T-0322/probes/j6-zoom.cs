var sb=new System.Text.StringBuilder();
var w = ZWin("MirageWindow");
var msT = ZType("ZuiMicroSlider");
foreach (var e in ZAll(w.rootVisualElement)) {
  if (!msT.IsInstanceOfType(e) || !ZDisplayed(e)) continue;
  sb.Append("MS '").Append(ZCaption(e)).Append("' val=").Append(msT.GetProperty("value").GetValue(e)).Append(" tip='").Append(ZTip(e)).Append("' path=").Append(ZPath(e)).Append("\n"); }
return sb.ToString();
