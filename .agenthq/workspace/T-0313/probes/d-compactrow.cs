// T-0313 — the compact list row (ZuiReflect.BuildList's `compact` branch: index + N dials + × on ONE
// NoWrap row).  Finds every row that contains a '×' Button, and reports the row's own width against the
// content box it has to fit in, every child's width/flex/min, and whether the ancestor that clips it has
// overflow:Hidden — i.e. whether the parts past the edge are DRAWN or GONE.
string unit = UnityEditor.EditorPrefs.GetString("T0312.unit", "pyre");
var win = ZWin(unit == "pyre" ? "PyreWindow" : "ShaperWindow");
if (win == null) return "NO WINDOW " + unit;
var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append(" window=").Append(win.position).Append("\n");

int hits = 0;
foreach (var e in ZAll(win.rootVisualElement))
{
    if (!ZDrawn(e)) continue;
    // a compact row = an element whose LAST child is a 22px Button labelled '×'
    int n = e.hierarchy.childCount;
    if (n < 3) continue;
    var last = e.hierarchy[n - 1];
    if (!(last is UnityEngine.UIElements.Button) || ZOwnText(last) != "×") continue;
    var par = e.hierarchy.parent; if (par == null) continue;
    var pc = ZContentWorld(par);
    float rowW = e.worldBound.width, rowMax = e.worldBound.xMax;
    hits++;
    sb.Append("\n##### row ").Append(hits).Append("  rowWidth=").Append(rowW.ToString("F1"))
      .Append(" rowX=").Append(e.worldBound.xMin.ToString("F1")).Append("..").Append(rowMax.ToString("F1"))
      .Append("  parentContent=").Append(pc.xMin.ToString("F1")).Append("..").Append(pc.xMax.ToString("F1"))
      .Append(" (").Append(pc.width.ToString("F1")).Append("px)")
      .Append("  spill=").Append((rowMax - pc.xMax).ToString("F1")).Append("px\n")
      .Append("      path=").Append(ZPath(e)).Append("\n");
    for (int i = 0; i < n; i++)
    {
        var c = e.hierarchy[i];
        var b = c.worldBound;
        sb.Append("   [").Append(i).Append("] ").Append(c.GetType().Name)
          .Append(" '").Append(ZCaption(c)).Append("'")
          .Append(" w=").Append(b.width.ToString("F1"))
          .Append(" x=").Append(b.xMin.ToString("F1")).Append("..").Append(b.xMax.ToString("F1"))
          .Append(" grow/shrink=").Append(c.resolvedStyle.flexGrow).Append("/").Append(c.resolvedStyle.flexShrink)
          .Append(" minW=").Append(c.resolvedStyle.minWidth.value.ToString("F0"))
          .Append(b.xMax > pc.xMax + ZTOL ? "  << PAST THE BOX" : "")
          .Append("\n");
    }
    // which ancestor clips, and how
    int d = 0;
    for (var p = e.hierarchy.parent; p != null && d < 6; p = p.hierarchy.parent, d++)
        sb.Append("      anc[").Append(d).Append("] ").Append(p.GetType().Name)
          .Append(" w=").Append(p.worldBound.width.ToString("F1"))
          .Append(" overflow=").Append(p.style.overflow.keyword + ":" + (p.style.overflow.keyword == UnityEngine.UIElements.StyleKeyword.Undefined ? p.style.overflow.value.ToString() : "-"))
          .Append(" cls=").Append(ZCls(p)).Append("\n");
    if (hits >= 3) break;
}
sb.Append("\ncompact rows found=").Append(hits).Append("\n");
var path = ZDump("compactrow-" + UnityEditor.EditorPrefs.GetString("T0312.tag", "x") + ".txt", sb.ToString());
return path + "\n" + sb.ToString();
