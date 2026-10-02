// The asset toolbar row of the window named by T0312.unit: every child left→right with its width, x
// extents, flex and classes, plus the slack between the rightmost child and the window's right edge.
string unit = UnityEditor.EditorPrefs.GetString("T0312.unit", "shaper");
var win = ZWin(unit == "pyre" ? "PyreWindow" : "ShaperWindow");
if (win == null) return "NO WINDOW " + unit;
var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append(" window=").Append(win.position).Append("\n");

UnityEngine.UIElements.VisualElement row = null;
foreach (var e in ZAll(win.rootVisualElement))
    if (e is UnityEngine.UIElements.Button b && b.text == "Save") { row = b.hierarchy.parent; break; }
if (row == null) return sb.Append("NO TOOLBAR ROW\n").ToString();

var rootW = win.rootVisualElement.worldBound;
sb.Append("row w=").Append(row.worldBound.width.ToString("F1"))
  .Append(" content=").Append(ZContentWorld(row).width.ToString("F1"))
  .Append(" wrap=").Append(row.resolvedStyle.flexWrap)
  .Append(" rootW=").Append(rootW.width.ToString("F1")).Append("\n");
float used = 0f, maxX = 0f;
for (int i = 0; i < row.hierarchy.childCount; i++)
{
    var c = row.hierarchy[i];
    used += c.resolvedStyle.width + c.resolvedStyle.marginLeft + c.resolvedStyle.marginRight;
    maxX = UnityEngine.Mathf.Max(maxX, c.worldBound.xMax);
    sb.Append("  [").Append(i).Append("] '").Append(ZCaption(c)).Append("' ").Append(c.GetType().Name)
      .Append(" w=").Append(c.resolvedStyle.width.ToString("F1"))
      .Append(" x=").Append(c.worldBound.xMin.ToString("F1")).Append("..").Append(c.worldBound.xMax.ToString("F1"))
      .Append(" flex=").Append(c.resolvedStyle.flexGrow).Append("/").Append(c.resolvedStyle.flexShrink)
      .Append(" cls=").Append(ZCls(c)).Append("\n");
    // the name label inside the ObjectField
    foreach (var d in ZAll(c))
    {
        if (!d.ClassListContains("unity-object-field-display__label")) continue;
        var te = d as UnityEngine.UIElements.TextElement;
        sb.Append("       name label '").Append(te.text).Append("' need=").Append(ZNeed(te).ToString("F1"))
          .Append(" have=").Append(ZHave(te).ToString("F1")).Append("\n");
    }
}
sb.Append("children total (incl margins)=").Append(used.ToString("F1"))
  .Append("  rightmost=").Append(maxX.ToString("F1"))
  .Append("  windowRight=").Append(rootW.xMax.ToString("F1"))
  .Append("  SLACK=").Append((rootW.xMax - maxX).ToString("F1")).Append("\n");
return sb.ToString();
