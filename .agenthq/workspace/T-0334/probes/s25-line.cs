// Hunt the element responsible for a 2 px dark vertical rule at logical x ~= 557 spanning the right pane.
var w = ZWin("ShaperWindow"); if (w == null) return "no window";
var sb = new System.Text.StringBuilder();
foreach (var e in ZAll(w.rootVisualElement)) {
  if (!ZDrawn(e)) continue;
  var b = e.worldBound;
  if (b.width <= 4.5f && b.height >= 60f)
    sb.Append("THIN ").Append(e.GetType().Name).Append(" '").Append(ZCaption(e)).Append("' ").Append(b).Append(" cls=").Append(ZCls(e)).Append(" | ").Append(ZPath(e)).Append("\n");
}
sb.Append("-- elements whose left edge is within 3px of x=557 --\n");
foreach (var e in ZAll(w.rootVisualElement)) {
  if (!ZDrawn(e)) continue; var b = e.worldBound;
  if (UnityEngine.Mathf.Abs(b.xMin - 557f) < 3f || UnityEngine.Mathf.Abs(b.xMax - 557f) < 3f)
    sb.Append("  ").Append(e.GetType().Name).Append(" '").Append(ZCaption(e)).Append("' ").Append(b).Append(" cls=").Append(ZCls(e)).Append("\n");
}
sb.Append("-- IMGUIContainers --\n");
foreach (var e in ZAll(w.rootVisualElement)) { if (e is UnityEngine.UIElements.IMGUIContainer && ZDrawn(e))
  sb.Append("  IMGUI ").Append(e.worldBound).Append(" ").Append(" cls=").Append(ZCls(e)).Append(" | ").Append(ZPath(e)).Append("\n"); }
return sb.ToString();
