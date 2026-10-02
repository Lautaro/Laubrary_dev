// measure the split: pane widths vs the dragline anchor's offset. Round 19 §3.2's invariant is
// "anchor offset == fixed pane width".
var w = ZWin("ShaperWindow"); if (w == null) return "no window";
var sb = new System.Text.StringBuilder();
sb.Append("window=").Append(w.position).Append("\n");
foreach (var e in ZAll(w.rootVisualElement)) {
  var sp = e as UnityEngine.UIElements.TwoPaneSplitView; if (sp == null) continue;
  sb.Append("split world=").Append(sp.worldBound).Append(" fixedIndex=").Append(sp.fixedPaneIndex)
    .Append(" fixedDim=").Append(sp.fixedPaneInitialDimension).Append("\n");
  int i = 0;
  foreach (var c in sp.Children()) {
    sb.Append("  child[").Append(i++).Append("] ").Append(c.GetType().Name)
      .Append(" cls=").Append(ZCls(c))
      .Append(" world=").Append(c.worldBound)
      .Append(" styleLeft=").Append(c.style.left.ToString())
      .Append(" styleWidth=").Append(c.style.width.ToString())
      .Append(" resolvedW=").Append(c.resolvedStyle.width.ToString("F2")).Append("\n");
  }
  foreach (var d in ZAll(sp)) {
    string cl = ZCls(d);
    if (cl.Contains("dragline")) sb.Append("  DRAG ").Append(cl).Append(" world=").Append(d.worldBound)
        .Append(" styleLeft=").Append(d.style.left.ToString()).Append("\n");
  }
}
return sb.ToString();
