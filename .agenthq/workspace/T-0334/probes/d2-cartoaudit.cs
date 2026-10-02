var sb = new System.Text.StringBuilder();
foreach (var n in new string[] { "PropWindow", "TilesetBuilderWindow" })
{
    var w = ZWin(n);
    if (w == null) { sb.Append(n).Append(" NOT OPEN\n"); continue; }
    var txt = ZAudit(w, n + "-empty");
    ZDump(n + "-empty.txt", txt);
    sb.Append(ZSummary(n + "-empty"));
    foreach (var e in ZAll(w.rootVisualElement))
        if (ZDrawn(e) && e is UnityEngine.UIElements.TextElement te && !string.IsNullOrEmpty(te.text) && te.text.Length > 3)
            sb.Append("   text: '").Append(te.text.Length > 110 ? te.text.Substring(0,110) : te.text).Append("'\n");
}
return sb.ToString();
