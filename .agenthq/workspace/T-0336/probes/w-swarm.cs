// press the Swarm section's own header enable toggle
var w = ZWin("ShaperWindow"); if (w == null) return "no window";
var secT = ZType("ZuiSection");
UnityEngine.UIElements.VisualElement sec = null;
foreach (var e in ZAll(w.rootVisualElement)) {
  if (!secT.IsInstanceOfType(e)) continue;
  foreach (var c in ZAll(e)) { var l=c as UnityEngine.UIElements.Label; if (l!=null && l.ClassListContains("zui-section__title") && l.text.StartsWith("Swarm")) { sec=e; break; } }
  if (sec != null) break;
}
if (sec == null) return "no Swarm section";
var sb = new System.Text.StringBuilder();
foreach (var c in ZAll(sec)) {
  if (!ZDrawn(c)) continue;
  var tb = c as UnityEngine.UIElements.Button;
  if (tb != null && ZCls(tb).Contains("togglebutton")) { sb.Append("btn '").Append(tb.text).Append("' cls=").Append(ZCls(tb)).Append(" -> ").Append(ZPress(w, tb)).Append("\n"); return sb.ToString(); }
  var t = c as UnityEngine.UIElements.Toggle;
  if (t != null && !ZCls(t).Contains("section__fold")) { sb.Append("toggle '").Append(ZCaption(t)).Append("' val=").Append(t.value).Append(" cls=").Append(ZCls(t)).Append(" -> ").Append(ZPress(w, t)).Append("\n"); return sb.ToString(); }
}
foreach (var c in ZAll(sec)) { if (!ZDrawn(c)) continue; sb.Append(c.GetType().Name).Append(" cls=").Append(ZCls(c)).Append(" txt=").Append(ZOwnText(c)).Append("\n"); }
return sb.ToString();
