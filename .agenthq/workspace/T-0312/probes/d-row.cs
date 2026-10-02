// Dumps the PARENT row (and its whole subtree) of every overflowing .zui-field whose caption matches
// EditorPrefs "T0312.match" — the row is what has to fit, so it is the row that must be read.
string unit = UnityEditor.EditorPrefs.GetString("T0312.unit", "shaper");
string match = UnityEditor.EditorPrefs.GetString("T0312.match", "Image");
int maxDepth = UnityEditor.EditorPrefs.GetInt("T0312.depth", 4);
var win = ZWin(unit == "pyre" ? "PyreWindow" : "ShaperWindow");
if (win == null) return "NO WINDOW " + unit;
var sb = new System.Text.StringBuilder();

System.Action<UnityEngine.UIElements.VisualElement, int, int> Dump = null;
Dump = (e, d, cap) =>
{
    var wb = e.worldBound;
    sb.Append(new string(' ', d * 2)).Append(e.GetType().Name);
    if (!string.IsNullOrEmpty(e.name)) sb.Append("#").Append(e.name);
    sb.Append(" w=").Append(wb.width.ToString("F1"))
      .Append(" x=").Append(wb.xMin.ToString("F0")).Append("..").Append(wb.xMax.ToString("F0"))
      .Append(" flex=").Append(e.resolvedStyle.flexGrow).Append("/").Append(e.resolvedStyle.flexShrink)
      .Append(" styleW=").Append(e.style.width.ToString())
      .Append(" cls=").Append(ZCls(e));
    var t = ZOwnText(e); if (!string.IsNullOrEmpty(t)) sb.Append(" text='").Append(t).Append("'");
    sb.Append("\n");
    if (d >= cap) return;
    for (int i = 0; i < e.hierarchy.childCount; i++) Dump(e.hierarchy[i], d + 1, cap);
};

int hits = 0;
foreach (var e in ZAll(win.rootVisualElement))
{
    if (!ZDrawn(e) || !e.ClassListContains("zui-field") || ZCaption(e) != match) continue;
    var par = e.hierarchy.parent; if (par == null) continue;
    hits++;
    sb.Append("\n##### row holding '").Append(match).Append("' hit ").Append(hits)
      .Append("  rowContent=").Append(ZContentWorld(par).width.ToString("F1")).Append("\n");
    Dump(par, 0, maxDepth);
    if (hits >= 2) break;
}
var path = ZDump("row-" + match.Replace(' ', '_') + ".txt", sb.ToString());
return path + "\n" + sb.ToString();
