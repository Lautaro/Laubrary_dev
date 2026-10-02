var w = ZWin("ShaperWindow"); if (w == null) return "no window";
var sb = new System.Text.StringBuilder();
sb.Append("window=").Append(w.position).Append("\n");
foreach (var e in ZAll(w.rootVisualElement)) {
  var sp = e as UnityEngine.UIElements.TwoPaneSplitView; if (sp == null) continue;
  sb.Append("SPLIT ").Append(sp.worldBound).Append(" fixedInit=").Append(sp.fixedPaneInitialDimension).Append("\n");
  var cc = sp.hierarchy[0];
  for (int i = 0; i < cc.hierarchy.childCount; i++) {
    var c = cc.hierarchy[i];
    sb.Append("   pane[").Append(i).Append("] ").Append(c.GetType().Name).Append(" ").Append(c.worldBound)
      .Append(" resolvedW=").Append(c.resolvedStyle.width).Append(" minW=").Append(c.resolvedStyle.minWidth)
      .Append(" flexBasis=").Append(c.resolvedStyle.flexBasis).Append(" grow=").Append(c.resolvedStyle.flexGrow)
      .Append(" shrink=").Append(c.resolvedStyle.flexShrink).Append("\n");
  }
  var anchor = sp.hierarchy[1];
  sb.Append("   anchor left=").Append(anchor.resolvedStyle.left).Append(" world=").Append(anchor.worldBound).Append("\n");
  // the dragline itself
  foreach (var d in ZAll(anchor)) if (d != anchor) sb.Append("     dragline ").Append(d.GetType().Name).Append(" ").Append(d.worldBound).Append(" cls=").Append(ZCls(d)).Append("\n");
}
return sb.ToString();
