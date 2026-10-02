var sb=new System.Text.StringBuilder();
var w = ZWin("PyreWindow");
foreach (var e in ZAll(w.rootVisualElement)) {
  if (!ZDrawn(e)) continue;
  var t = ZOwnText(e);
  if (!string.IsNullOrEmpty(t) && t.Contains("frame ")) sb.Append("'").Append(t).Append("' ").Append(e.GetType().Name).Append(" cls=").Append(ZCls(e)).Append(" wb=").Append(e.worldBound).Append(" path=").Append(ZPath(e)).Append("\n");
}
return sb.ToString();
