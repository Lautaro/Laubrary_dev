var win = ZWin("PyreWindow"); if (win==null) return "no pyre";
var sb = new System.Text.StringBuilder();
foreach (var e in ZAll(win.rootVisualElement)) {
  if (!ZDrawn(e)) continue;
  var c = ZCaption(e);
  if (c != null && c.StartsWith("Life (frames)")) sb.AppendLine(e.GetType().Name + " '" + c + "' " + e.worldBound + " cls=" + ZCls(e) + " tip=" + (ZTip(e).Length>60?ZTip(e).Substring(0,60)+"...":ZTip(e)));
}
sb.AppendLine(ZSummary("post"));
return sb.ToString();
