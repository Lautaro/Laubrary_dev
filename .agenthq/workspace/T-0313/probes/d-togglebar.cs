// T-0313 — the section toggle bar's own fit.  For the window named by T313.win: the bar's width against
// the window's, its wrap/shrink styles, every button it holds, and the window's declared minSize — so
// "does the bar fit at the smallest size the window can be dragged to" is answerable as a number.
string wname = UnityEditor.EditorPrefs.GetString("T313.win", "ChunkWindow");
var win = ZWin(wname);
if (win == null) return "NO WINDOW " + wname;
var sb = new System.Text.StringBuilder();
var root = win.rootVisualElement;
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n")
  .Append(wname).Append(" window=").Append(win.position.width.ToString("F1")).Append("x").Append(win.position.height.ToString("F0"))
  .Append(" minSize=").Append(win.minSize).Append(" rootWorld=").Append(root.worldBound.xMin.ToString("F1"))
  .Append("..").Append(root.worldBound.xMax.ToString("F1")).Append("\n");

foreach (var e in ZAll(root))
{
    if (e.GetType().Name != "ZuiSectionToggleBar" || !ZDrawn(e)) continue;
    sb.Append("BAR ").Append(e.GetType().Name).Append(" w=").Append(e.worldBound.width.ToString("F1"))
      .Append(" x=").Append(e.worldBound.xMin.ToString("F1")).Append("..").Append(e.worldBound.xMax.ToString("F1"))
      .Append(" wrap=").Append(e.resolvedStyle.flexWrap).Append("\n");
    foreach (var s in ZAll(e))
    {
        if (s == e) continue;
        var n = s.GetType().Name;
        if (n != "ZuiSegmented" && !(s is UnityEngine.UIElements.Button)) continue;
        var b = s.worldBound;
        sb.Append(n == "ZuiSegmented" ? "  SEG " : "    btn ")
          .Append("'").Append(ZCaption(s)).Append("'")
          .Append(" w=").Append(b.width.ToString("F1"))
          .Append(" x=").Append(b.xMin.ToString("F1")).Append("..").Append(b.xMax.ToString("F1"))
          .Append(" wrap=").Append(s.resolvedStyle.flexWrap)
          .Append(" grow/shrink=").Append(s.resolvedStyle.flexGrow).Append("/").Append(s.resolvedStyle.flexShrink)
          .Append(b.xMax > root.worldBound.xMax + ZTOL ? "   << OFF THE WINDOW" : "")
          .Append("\n");
    }
}
var path = ZDump("togglebar-" + UnityEditor.EditorPrefs.GetString("T0312.tag", wname) + ".txt", sb.ToString());
return path + "\n" + sb.ToString();
