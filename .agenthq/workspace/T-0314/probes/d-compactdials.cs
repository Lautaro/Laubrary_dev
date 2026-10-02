// T-0314 — the INSIDE of a compact list row: the wrapping dial strip.  Finds every row whose last child
// is a '×' Button, then reports the strip in the middle of it — its content box, every dial's width and
// world rect, which visual LINE each dial landed on, and whether anything sits outside the strip.
string unit = UnityEditor.EditorPrefs.GetString("T0312.unit", "pyre");
var win = ZWin(unit == "pyre" ? "PyreWindow" : "ShaperWindow");
if (win == null) return "NO WINDOW " + unit;
var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath)
  .Append(" window=").Append(win.position.width.ToString("F0")).Append("\n");

int hits = 0;
foreach (var e in ZAll(win.rootVisualElement))
{
    if (!ZDrawn(e)) continue;
    int n = e.hierarchy.childCount;
    if (n != 3) continue;
    var last = e.hierarchy[n - 1];
    if (!(last is UnityEngine.UIElements.Button) || ZOwnText(last) != "×") continue;
    var strip = e.hierarchy[1];
    if (strip.hierarchy.childCount == 0) continue;
    hits++;
    var pc = ZContentWorld(e.hierarchy.parent);
    var sc = ZContentWorld(strip);
    sb.Append("\n##### row ").Append(hits)
      .Append("  row=").Append(e.worldBound.xMin.ToString("F1")).Append("..").Append(e.worldBound.xMax.ToString("F1"))
      .Append("  card body=").Append(pc.xMin.ToString("F1")).Append("..").Append(pc.xMax.ToString("F1"))
      .Append(" (").Append(pc.width.ToString("F1")).Append("px)")
      .Append("  spill=").Append((e.worldBound.xMax - pc.xMax).ToString("F1")).Append("px")
      .Append("  rowHeight=").Append(e.worldBound.height.ToString("F1")).Append("\n")
      .Append("   index '").Append(ZCaption(e.hierarchy[0])).Append("' x=")
      .Append(e.hierarchy[0].worldBound.xMin.ToString("F1")).Append("..").Append(e.hierarchy[0].worldBound.xMax.ToString("F1")).Append("\n")
      .Append("   STRIP w=").Append(strip.worldBound.width.ToString("F1"))
      .Append(" content=").Append(sc.xMin.ToString("F1")).Append("..").Append(sc.xMax.ToString("F1"))
      .Append(" wrap=").Append(strip.resolvedStyle.flexWrap)
      .Append(" height=").Append(strip.worldBound.height.ToString("F1")).Append("\n");
    float firstY = float.NaN; int line = 0; float prevY = float.NaN;
    for (int i = 0; i < strip.hierarchy.childCount; i++)
    {
        var c = strip.hierarchy[i];
        var b = c.worldBound;
        if (float.IsNaN(prevY)) { firstY = b.yMin; prevY = b.yMin; }
        else if (b.yMin > prevY + 2f) { line++; prevY = b.yMin; }
        sb.Append("     [").Append(i).Append("] ").Append(c.GetType().Name)
          .Append(" '").Append(ZCaption(c)).Append("'")
          .Append(" w=").Append(b.width.ToString("F1"))
          .Append(" x=").Append(b.xMin.ToString("F1")).Append("..").Append(b.xMax.ToString("F1"))
          .Append(" line=").Append(line)
          .Append(" dy=").Append((b.yMin - firstY).ToString("F1"))
          .Append(b.xMax > sc.xMax + ZTOL || b.xMin < sc.xMin - ZTOL ? "  << OUTSIDE THE STRIP" : "")
          .Append("\n");
        UnityEngine.UIElements.TextElement te = null;
        foreach (var x in ZAll(c))
            if (te == null && x is UnityEngine.UIElements.Label && ZCls(x).Contains("caption"))
                te = (UnityEngine.UIElements.TextElement)x;
        if (te != null)
        {
            sb.Append("           caption '").Append(te.text).Append("' need=").Append(ZNeed(te).ToString("F1"))
              .Append(" have=").Append(ZHave(te).ToString("F1"))
              .Append(ZNeed(te) > ZHave(te) + ZTOL ? "  << CLIPPED" : "  ok").Append("\n");
        }
    }
    sb.Append("   remove '×' x=").Append(last.worldBound.xMin.ToString("F1")).Append("..").Append(last.worldBound.xMax.ToString("F1"))
      .Append(last.worldBound.xMax > pc.xMax + ZTOL ? "  << PAST THE CARD" : "  inside the card").Append("\n");
    if (hits >= 3) break;
}
sb.Append("\ncompact rows found=").Append(hits).Append("\n");
var path = ZDump("compactdials-" + UnityEditor.EditorPrefs.GetString("T0312.tag", "x") + ".txt", sb.ToString());
return path + "\n" + sb.ToString();
