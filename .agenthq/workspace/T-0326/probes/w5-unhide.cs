// Force every display:None subtree visible, so the audit can see the controls that only appear when a
// toggle is on. Round 16's "not verified #7": the sweep is bounded by what DRAWS.
string names = UnityEditor.EditorPrefs.GetString("T326.wins", "");
var zaT = ZType("ZuiAudit");
var sb = new System.Text.StringBuilder();
foreach (var n in names.Split('|'))
{
    if (string.IsNullOrEmpty(n)) continue;
    var w = ZWin(n); if (w == null) { sb.Append(n).Append(": NOT OPEN\n"); continue; }
    zaT.GetMethod("ExpandAll").Invoke(null, new object[]{ w });
    int shown = 0;
    foreach (var e in ZAll(w.rootVisualElement))
        if (e.resolvedStyle.display == UnityEngine.UIElements.DisplayStyle.None)
        { e.style.display = UnityEngine.UIElements.DisplayStyle.Flex; shown++; }
    sb.Append(n).Append(": unhid ").Append(shown).Append("\n");
    w.rootVisualElement.MarkDirtyRepaint(); w.Repaint();
}
return sb.ToString();
