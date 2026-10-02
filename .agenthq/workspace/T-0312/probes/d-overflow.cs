// Deep detail for every horizontal parent-overflow the audit found in the window named by T0312.unit:
// the FULL ancestor chain with types + names + classes + widths, and the offender's own children.
string unit = UnityEditor.EditorPrefs.GetString("T0312.unit", "shaper");
var win = ZWin(unit == "pyre" ? "PyreWindow" : "ShaperWindow");
if (win == null) return "NO WINDOW " + unit;
var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append(" ").Append(win.position).Append("\n");

int cols = 0;
foreach (var v in ZAll(win.rootVisualElement))
    if (v.GetType().Name == "ZuiColumnFlow")
    { cols = v.hierarchy.childCount > 0 ? v.hierarchy[0].hierarchy.childCount : 0;
      sb.Append("ZuiColumnFlow w=").Append(v.worldBound.width.ToString("F0")).Append(" columns=").Append(cols).Append("\n"); }

foreach (var e in ZAll(win.rootVisualElement))
{
    if (!ZDrawn(e)) continue;
    var par = e.hierarchy.parent;
    if (par == null || !ZDrawn(par) || ZIsChrome(e) || ZIsChrome(par)) continue;
    var pc = ZContentWorld(par);
    if (float.IsNaN(pc.width) || pc.width <= 0f) continue;
    var b = e.worldBound;
    float sx = UnityEngine.Mathf.Max(pc.xMin - b.xMin, b.xMax - pc.xMax);
    if (sx <= ZTOL) continue;

    sb.Append("\n### OVERFLOW-X ").Append(sx.ToString("F1")).Append("px  '").Append(ZCaption(e)).Append("'\n");
    var chain = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>();
    for (var p = e; p != null; p = p.hierarchy.parent) chain.Add(p);
    chain.Reverse();
    for (int i = 0; i < chain.Count; i++)
    {
        var c = chain[i];
        var cw = ZContentWorld(c);
        sb.Append(new string(' ', i)).Append(i == chain.Count - 1 ? "» " : "  ")
          .Append(c.GetType().Name);
        if (!string.IsNullOrEmpty(c.name)) sb.Append("#").Append(c.name);
        sb.Append(" w=").Append(c.worldBound.width.ToString("F1"))
          .Append(" content=").Append(cw.width.ToString("F1"))
          .Append(" x=").Append(c.worldBound.xMin.ToString("F0")).Append("..").Append(c.worldBound.xMax.ToString("F0"))
          .Append(" flex=").Append(c.resolvedStyle.flexGrow).Append("/").Append(c.resolvedStyle.flexShrink)
          .Append(" dir=").Append(c.resolvedStyle.flexDirection).Append(" wrap=").Append(c.resolvedStyle.flexWrap)
          .Append(" cls=").Append(ZCls(c));
        var t = ZOwnText(c); if (!string.IsNullOrEmpty(t)) sb.Append(" text='").Append(t).Append("'");
        sb.Append("\n");
    }
    sb.Append("   children of the offender:\n");
    for (int i = 0; i < e.hierarchy.childCount; i++)
    {
        var c = e.hierarchy[i];
        sb.Append("     [").Append(i).Append("] ").Append(c.GetType().Name)
          .Append(" '").Append(ZCaption(c)).Append("' w=").Append(c.worldBound.width.ToString("F1"))
          .Append(" x=").Append(c.worldBound.xMin.ToString("F0")).Append("..").Append(c.worldBound.xMax.ToString("F0"))
          .Append(" cls=").Append(ZCls(c)).Append("\n");
    }
}
var path = ZDump("overflow-" + UnityEditor.EditorPrefs.GetString("T0312.tag", unit) + ".txt", sb.ToString());
return path + "\n" + sb.ToString();
