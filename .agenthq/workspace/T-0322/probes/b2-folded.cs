var w = ZWin("PyreWindow"); var sb=new System.Text.StringBuilder();
var boxT = ZType("ZuiBox"); var titleP = boxT.GetProperty("TitleText");
var openP = boxT.GetProperty("IsOpen");
foreach (var e in ZAll(w.rootVisualElement)) { if (!boxT.IsInstanceOfType(e) || !ZDrawn(e)) continue;
  string t = titleP.GetValue(e) as string; if (string.IsNullOrEmpty(t)) continue;
  if (e.worldBound.x > 360) continue;
  sb.Append("'").Append(t).Append("' open=").Append(openP!=null?openP.GetValue(e).ToString():"?").Append(" w=").Append(e.worldBound.width.ToString("F1")).Append(" x=").Append(e.worldBound.x.ToString("F1")).Append(" alignSelf=").Append(e.resolvedStyle.alignSelf).Append("\n"); }
return sb.ToString();
