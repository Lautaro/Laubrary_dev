// Prints the whole subtree (types, widths, flex, classes, text) of every element whose caption matches
// EditorPrefs "T0312.match", in the window named by "T0312.unit". Depth capped by "T0312.depth".
string unit = UnityEditor.EditorPrefs.GetString("T0312.unit", "shaper");
string match = UnityEditor.EditorPrefs.GetString("T0312.match", "Colour bands");
int maxDepth = UnityEditor.EditorPrefs.GetInt("T0312.depth", 6);
var win = ZWin(unit == "pyre" ? "PyreWindow" : "ShaperWindow");
if (win == null) return "NO WINDOW " + unit;
var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");

System.Action<UnityEngine.UIElements.VisualElement, int, int> Dump = null;
Dump = (e, d, cap) =>
{
    var wb = e.worldBound; var cw = ZContentWorld(e);
    sb.Append(new string(' ', d * 2)).Append(e.GetType().Name);
    if (!string.IsNullOrEmpty(e.name)) sb.Append("#").Append(e.name);
    sb.Append(" w=").Append(wb.width.ToString("F1")).Append(" content=").Append(cw.width.ToString("F1"))
      .Append(" x=").Append(wb.xMin.ToString("F0")).Append("..").Append(wb.xMax.ToString("F0"))
      .Append(" h=").Append(wb.height.ToString("F1"))
      .Append(" flex=").Append(e.resolvedStyle.flexGrow).Append("/").Append(e.resolvedStyle.flexShrink)
      .Append(" basis=").Append(e.resolvedStyle.flexBasis.ToString())
      .Append(" styleW=").Append(e.style.width.ToString())
      .Append(" self=").Append(e.resolvedStyle.alignSelf)
      .Append(" dir=").Append(e.resolvedStyle.flexDirection)
      .Append(" cls=").Append(ZCls(e));
    var t = ZOwnText(e); if (!string.IsNullOrEmpty(t)) sb.Append(" text='").Append(t).Append("'");
    sb.Append("\n");
    if (d >= cap) return;
    for (int i = 0; i < e.hierarchy.childCount; i++) Dump(e.hierarchy[i], d + 1, cap);
};

int hits = 0;
foreach (var e in ZAll(win.rootVisualElement))
{
    if (!ZDrawn(e)) continue;
    if (!e.ClassListContains("zui-field")) continue;
    if (ZCaption(e) != match) continue;
    hits++;
    sb.Append("\n##### zui-field '").Append(match).Append("' hit ").Append(hits).Append("\n");
    var par = e.hierarchy.parent;
    if (par != null) sb.Append("parent ").Append(par.GetType().Name).Append(" content=")
                       .Append(ZContentWorld(par).width.ToString("F1")).Append(" cls=").Append(ZCls(par)).Append("\n");
    Dump(e, 0, maxDepth);
    if (hits >= 2) break;
}
var path = ZDump("subtree-" + match.Replace(' ', '_') + ".txt", sb.ToString());
return path + "\n" + sb.ToString();
