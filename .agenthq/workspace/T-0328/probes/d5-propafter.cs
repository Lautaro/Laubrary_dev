var sb = new System.Text.StringBuilder();
var w = ZWin("PropWindow");
var cur = w.GetType().GetProperty("Current", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
var a = cur == null ? null : cur.GetValue(w) as UnityEngine.Object;
sb.Append("bound=").Append(a == null ? "<null>" : UnityEditor.AssetDatabase.GetAssetPath(a)).Append("\n");
var txt = ZAudit(w, "prop-fresh"); ZDump("prop-fresh.txt", txt);
sb.Append(ZSummary("prop-fresh"));
foreach (var e in ZAll(w.rootVisualElement))
    if (ZDrawn(e) && e is UnityEngine.UIElements.TextElement te && !string.IsNullOrEmpty(te.text))
        sb.Append("  t:'").Append(te.text.Length > 80 ? te.text.Substring(0,80) : te.text).Append("'\n");
return sb.ToString();
