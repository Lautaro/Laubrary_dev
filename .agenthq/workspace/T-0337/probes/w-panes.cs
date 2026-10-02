// At the current size: can everything in each pane be REACHED? For each ScrollView report the viewport,
// the content height and whether its vertical scroller is drawn; then list every control that is outside
// the window and has NO ScrollView ancestor (genuinely unreachable, T-0331's class of bug).
var w = ZWin("ShaperWindow"); if (w == null) return "no window";
var sb = new System.Text.StringBuilder();
var wr = w.rootVisualElement.worldBound;
sb.Append("window=").Append(w.position).Append(" root=").Append(wr).Append("\n");
int i = 0;
foreach (var e in ZAll(w.rootVisualElement)) {
  var sv = e as UnityEngine.UIElements.ScrollView; if (sv == null || !ZDrawn(sv)) continue;
  var vp = sv.contentViewport; var cc = sv.contentContainer;
  bool vis = sv.verticalScroller != null && ZDrawn(sv.verticalScroller);
  sb.Append("SCROLLVIEW#").Append(i++).Append(" rect=").Append(sv.worldBound)
    .Append(" viewport=").Append(vp.worldBound.height.ToString("F1"))
    .Append(" content=").Append(cc.worldBound.height.ToString("F1"))
    .Append(" vScrollerDrawn=").Append(vis)
    .Append(" offset=").Append(sv.scrollOffset)
    .Append(" mode=").Append(sv.verticalScrollerVisibility)
    .Append("\n");
}
int unreachable = 0;
foreach (var e in ZAll(w.rootVisualElement)) {
  if (!ZDrawn(e) || !ZIsLeafCtrl(e)) continue;
  var b = e.worldBound;
  bool outside = b.yMax > wr.yMax + 1.5f || b.yMin < wr.yMin - 1.5f || b.xMax > wr.xMax + 1.5f || b.xMin < wr.xMin - 1.5f;
  if (!outside) continue;
  bool inScroll = false;
  for (var p = e.hierarchy.parent; p != null; p = p.hierarchy.parent) if (p is UnityEngine.UIElements.ScrollView) { inScroll = true; break; }
  if (inScroll) continue;
  unreachable++;
  sb.Append("UNREACHABLE '").Append(ZCaption(e)).Append("' ").Append(e.GetType().Name).Append(" ").Append(b).Append(" | ").Append(ZPath(e)).Append("\n");
}
sb.Append("unreachableLeafControls=").Append(unreachable).Append("\n");
return sb.ToString();
