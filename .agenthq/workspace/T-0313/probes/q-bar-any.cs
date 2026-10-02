// T-0313 — find whatever plays the part of a section toggle bar in T313.win, whether or not it is a
// ZuiSectionToggleBar, and report every ZuiSegmented's fit against the window.
string wname = UnityEditor.EditorPrefs.GetString("T313.win", "ShaperWindow");
var win = ZWin(wname);
if (win == null) return "NO WINDOW " + wname;
var root = win.rootVisualElement;
var sb = new System.Text.StringBuilder();
sb.Append(wname).Append(" window=").Append(win.position.width.ToString("F1"))
  .Append(" minSize=").Append(win.minSize).Append(" root=").Append(root.worldBound.xMin.ToString("F1"))
  .Append("..").Append(root.worldBound.xMax.ToString("F1")).Append("\n");
foreach (var e in ZAll(root))
{
    var n = e.GetType().Name;
    if (n != "ZuiSegmented" && n != "ZuiSectionToggleBar") continue;
    var b = e.worldBound;
    sb.Append(n).Append(" drawn=").Append(ZDrawn(e))
      .Append(" w=").Append(b.width.ToString("F1")).Append(" x=").Append(b.xMin.ToString("F1")).Append("..").Append(b.xMax.ToString("F1"))
      .Append(" wrap=").Append(e.resolvedStyle.flexWrap).Append(" shrink=").Append(e.resolvedStyle.flexShrink)
      .Append(" children=").Append(e.hierarchy.childCount)
      .Append(b.xMax > root.worldBound.xMax + ZTOL ? "   << PAST THE WINDOW by " + (b.xMax - root.worldBound.xMax).ToString("F1") : "")
      .Append("\n");
    for (int i = 0; i < e.hierarchy.childCount; i++)
    {
        var c = e.hierarchy[i];
        sb.Append("    '").Append(ZCaption(c)).Append("' w=").Append(c.worldBound.width.ToString("F1"))
          .Append(" xMax=").Append(c.worldBound.xMax.ToString("F1"))
          .Append(c.worldBound.xMax > root.worldBound.xMax + ZTOL ? "  << OFF" : "").Append("\n");
    }
}
return sb.ToString();
