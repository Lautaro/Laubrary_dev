// T-0311 verification: every "★" library button in the window named by T0312.unit, with the width its
// glyph needs against the width its content box now gives.
string unit = UnityEditor.EditorPrefs.GetString("T0312.unit", "shaper");
var win = ZWin(unit == "pyre" ? "PyreWindow" : "ShaperWindow");
if (win == null) return "NO WINDOW " + unit;
var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append(" unit=").Append(unit).Append("\n");
int n = 0;
foreach (var e in ZAll(win.rootVisualElement))
{
    var te = e as UnityEngine.UIElements.TextElement;
    if (te == null || te.text != "★" || !ZDrawn(te)) continue;
    n++;
    sb.Append("STAR w=").Append(te.worldBound.width.ToString("F1"))
      .Append(" content=").Append(ZHave(te).ToString("F1"))
      .Append(" need=").Append(ZNeed(te).ToString("F1"))
      .Append(ZNeed(te) > ZHave(te) + ZTOL ? "  CLIPPED" : "  fits")
      .Append(" | ").Append(ZPath(te)).Append("\n");
}
return sb.Append("stars=").Append(n).Append("\n").ToString();
