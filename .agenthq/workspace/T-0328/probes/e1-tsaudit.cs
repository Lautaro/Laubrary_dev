var sb = new System.Text.StringBuilder();
var w = ZWin("TilesetBuilderWindow");
var txt = ZAudit(w, "tileset-fresh"); ZDump("tileset-fresh.txt", txt);
sb.Append(ZSummary("tileset-fresh"));
foreach (var e in ZAll(w.rootVisualElement))
    if (ZDrawn(e) && e is UnityEngine.UIElements.TextElement te && !string.IsNullOrEmpty(te.text))
        sb.Append("  t:'").Append(te.text.Length > 90 ? te.text.Substring(0,90) : te.text).Append("'\n");
return sb.ToString();
