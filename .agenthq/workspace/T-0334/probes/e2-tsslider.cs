var sb = new System.Text.StringBuilder();
var w = ZWin("TilesetBuilderWindow");
foreach (var e in ZAll(w.rootVisualElement))
{
  if (!ZDrawn(e) || e.GetType().Name != "ZuiMicroSlider") continue;
  var cap = ZFirstText(e);
  sb.Append("MicroSlider '").Append(cap).Append("' w=").Append(e.worldBound.width.ToString("F1"))
    .Append(" parentW=").Append(e.hierarchy.parent.worldBound.width.ToString("F1"))
    .Append(" | ").Append(ZPath(e)).Append("\n");
}
return sb.ToString();
