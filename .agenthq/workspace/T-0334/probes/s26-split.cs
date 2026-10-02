var w = ZWin("ShaperWindow"); if (w == null) return "no window";
var sb = new System.Text.StringBuilder();
sb.Append("window=").Append(w.position).Append("\n");
foreach (var e in ZAll(w.rootVisualElement)) {
  var sp = e as UnityEngine.UIElements.TwoPaneSplitView; if (sp == null) continue;
  sb.Append("SPLIT ").Append(sp.worldBound).Append(" fixedPaneIndex=").Append(sp.fixedPaneIndex)
    .Append(" fixedPaneInitialDimension=").Append(sp.fixedPaneInitialDimension)
    .Append(" orientation=").Append(sp.orientation).Append("\n");
  for (int i = 0; i < sp.hierarchy.childCount; i++) {
    var c = sp.hierarchy[i];
    sb.Append("   child[").Append(i).Append("] ").Append(c.GetType().Name).Append(" name=").Append(c.name)
      .Append(" ").Append(c.worldBound).Append(" cls=").Append(ZCls(c))
      .Append(" left=").Append(c.resolvedStyle.left).Append(" width=").Append(c.resolvedStyle.width)
      .Append(" pos=").Append(c.resolvedStyle.position).Append("\n");
  }
}
return sb.ToString();
