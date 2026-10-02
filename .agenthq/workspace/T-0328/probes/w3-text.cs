// Every DRAWN text element of a named window set, in layout order, with its rect — the "what does the
// user actually read on this screen" dump.
string names = UnityEditor.EditorPrefs.GetString("T328.wins", "");
var sb = new System.Text.StringBuilder();
foreach (var n in names.Split('|'))
{
    if (string.IsNullOrEmpty(n)) continue;
    var w = ZWin(n);
    if (w == null) { sb.Append(n).Append(": NOT OPEN\n"); continue; }
    sb.Append("=== ").Append(n).Append(" ").Append(w.position.width.ToString("F0")).Append("x").Append(w.position.height.ToString("F0")).Append(" ===\n");
    foreach (var e in ZAll(w.rootVisualElement))
    {
        if (!ZDrawn(e)) continue;
        var te = e as UnityEngine.UIElements.TextElement;
        if (te == null || string.IsNullOrEmpty(te.text)) continue;
        var b = te.worldBound;
        sb.Append("  [").Append(b.x.ToString("F0")).Append(",").Append(b.y.ToString("F0")).Append(" ").Append(b.width.ToString("F0")).Append("x").Append(b.height.ToString("F0")).Append("] ")
          .Append(te.GetType().Name).Append(" '").Append(te.text.Replace("\n", "\\n")).Append("'\n");
    }
    int n2 = 0;
    foreach (var e in ZAll(w.rootVisualElement)) if (ZDrawn(e) && ZIsCtrl(e)) n2++;
    sb.Append("  (drawn controls=").Append(n2).Append(")\n");
}
return sb.ToString();
