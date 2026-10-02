var win = ZWin("ShaperWindow"); var sb = new System.Text.StringBuilder();
foreach (var e in ZAll(win.rootVisualElement)) {
  if (!ZDrawn(e)) continue;
  var cap = ZCaption(e);
  if (cap == null) continue;
  bool inBake = false;
  for (var p = e.hierarchy.parent; p != null; p = p.hierarchy.parent) { var bx = p as Laubrary.Zui.ZuiBox; if (bx != null && bx.TitleText == "Bake") { inBake = true; break; } }
  if (!inBake) continue;
  if (ZIsLeafCtrl(e) || e is UnityEngine.UIElements.Label)
    sb.AppendLine(e.GetType().Name + " '" + cap + "' " + e.worldBound + " enabled=" + e.enabledInHierarchy + " tip=" + (ZTip(e).Length>60?ZTip(e).Substring(0,60)+"…":ZTip(e)));
}
return sb.Length == 0 ? "no Bake box found" : sb.ToString();
