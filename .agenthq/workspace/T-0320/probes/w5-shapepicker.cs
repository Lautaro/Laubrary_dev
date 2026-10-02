var win = ZWin("ShaperWindow"); var sb = new System.Text.StringBuilder();
foreach (var e in ZAll(win.rootVisualElement)) {
  if (!ZDrawn(e)) continue;
  var c = ZCaption(e);
  if (c == "Rectangle" || c == "Edge" || c == "Add edge") sb.AppendLine(e.GetType().Name + " '" + c + "' " + e.worldBound + " cls=" + ZCls(e) + " path=" + ZPath(e));
}
return sb.ToString();
