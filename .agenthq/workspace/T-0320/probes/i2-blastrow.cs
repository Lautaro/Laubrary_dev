var win = ZWin("PyreWindow"); if (win==null) return "no pyre";
var sb = new System.Text.StringBuilder();
// find every element whose caption mentions blast, plus the scroll offset needed to see it
foreach (var e in ZAll(win.rootVisualElement)) {
  if (!ZDrawn(e)) continue;
  var c = ZCaption(e);
  if (c != null && c.ToLower().Contains("blast")) sb.AppendLine(e.GetType().Name + " '" + c + "' " + e.worldBound + " cls=" + ZCls(e));
}
var s = ZSummary("jet");
return sb.ToString() + "\n" + s;
