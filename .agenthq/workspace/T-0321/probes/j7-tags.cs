var win = ZWin("PyreWindow"); var sb=new System.Text.StringBuilder();
var secT = ZType("ZuiSection"); UnityEngine.UIElements.VisualElement tags=null;
foreach (var e in ZAll(win.rootVisualElement)) { if (!secT.IsInstanceOfType(e)) continue;
  foreach (var c in ZAll(e)) { var l=c as UnityEngine.UIElements.Label; if (l!=null && l.text=="Tags") { tags=e; break; } } if (tags!=null) break; }
foreach (var e in ZAll(tags)) if (ZDrawn(e)) sb.Append(e.GetType().Name).Append(" '").Append(ZOwnText(e)).Append("' cls=").Append(ZCls(e)).Append(" wb=").Append(e.worldBound).Append("\n");
// same in Shaper for comparison
var sw = ZWin("ShaperWindow");
foreach (var e in ZAll(sw.rootVisualElement)) { if (!secT.IsInstanceOfType(e)) continue;
  foreach (var c in ZAll(e)) { var l=c as UnityEngine.UIElements.Label; if (l!=null && l.text=="Tags") { sb.Append("SHAPER Tags section wb=").Append(e.worldBound).Append(" drawn=").Append(ZDrawn(e)).Append("\n"); break; } } }
return sb.ToString();
