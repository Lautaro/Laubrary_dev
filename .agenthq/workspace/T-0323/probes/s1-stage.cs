var sb=new System.Text.StringBuilder();
var w=ZWin("ShaperWindow");
foreach (var e in ZAll(w.rootVisualElement)) {
  if (!ZDrawn(e)) continue;
  var bg = e.resolvedStyle.backgroundImage;
  if (bg.texture!=null && e.worldBound.width>60) sb.Append("BG ").Append(e.GetType().Name).Append(" ").Append(e.worldBound).Append(" tex=").Append(bg.texture.width).Append("x").Append(bg.texture.height).Append(" cls=").Append(ZCls(e)).Append("\n");
  var n=e.GetType().Name;
  if (n.Contains("Stage")||n.Contains("Pixel")||n.Contains("Preview")) sb.Append("EL ").Append(n).Append(" ").Append(e.worldBound).Append(" cls=").Append(ZCls(e)).Append("\n");
}
return sb.ToString();
