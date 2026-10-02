// Reads the asset toolbar row after a layout pass: children, their x extents, and the slack to the window edge.
var sb = new System.Text.StringBuilder();
var win = UnityEditor.EditorWindow.GetWindow<Laubrary.Shaper.Editor.ShaperWindow>();
sb.Append("window=").Append(win.position).Append("\n");
System.Action<UnityEngine.UIElements.VisualElement, System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> Walk = null;
Walk = (e, into) => { if (e == null) return; into.Add(e); for (int i = 0; i < e.hierarchy.childCount; i++) Walk(e.hierarchy[i], into); };
var all = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>();
Walk(win.rootVisualElement, all);

// the toolbar row is the row that holds the "Save" button
UnityEngine.UIElements.VisualElement row = null;
foreach (var v in all)
    if (v is UnityEngine.UIElements.Button b && b.text == "Save") { row = b.parent; break; }
if (row == null) return "NO TOOLBAR ROW";
float rowRight = row.worldBound.xMax, winRight = win.rootVisualElement.worldBound.xMax;
sb.Append("row w=").Append(row.resolvedStyle.width.ToString("F1"))
  .Append(" wrap=").Append(row.resolvedStyle.flexWrap)
  .Append(" rootW=").Append(win.rootVisualElement.resolvedStyle.width.ToString("F1")).Append("\n");
float used = 0f, maxX = 0f;
for (int i = 0; i < row.childCount; i++)
{
    var c = row[i];
    string txt = c is UnityEngine.UIElements.Button bb ? bb.text : (c is UnityEngine.UIElements.Label ll ? ll.text : c.GetType().Name);
    used += c.resolvedStyle.width + c.resolvedStyle.marginLeft + c.resolvedStyle.marginRight;
    maxX = UnityEngine.Mathf.Max(maxX, c.worldBound.xMax);
    sb.Append("  child ").Append(i).Append(" '").Append(txt).Append("' w=").Append(c.resolvedStyle.width.ToString("F1"))
      .Append(" x=").Append(c.worldBound.xMin.ToString("F1")).Append("..").Append(c.worldBound.xMax.ToString("F1"))
      .Append(" y=").Append(c.worldBound.yMin.ToString("F1")).Append("\n");
}
sb.Append("children total width (incl margins) = ").Append(used.ToString("F1"))
  .Append("   rightmost x = ").Append(maxX.ToString("F1"))
  .Append("   window right = ").Append(winRight.ToString("F1"))
  .Append("   SLACK = ").Append((winRight - maxX).ToString("F1")).Append("\n");
return sb.ToString();
