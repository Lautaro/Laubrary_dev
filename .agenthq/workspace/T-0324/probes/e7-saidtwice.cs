// How many times does one sentence appear on screen — as visible TEXT, and as a TOOLTIP?
string wn = UnityEditor.EditorPrefs.GetString("T324.win", "MirageWindow");
string find = UnityEditor.EditorPrefs.GetString("T324.find", "");
var w = ZWin(wn); if (w == null) return "no " + wn;
int asText = 0, asTip = 0; var sb = new System.Text.StringBuilder();
foreach (var e in ZAll(w.rootVisualElement))
{
    if (!ZDrawn(e)) continue;
    var te = e as UnityEngine.UIElements.TextElement;
    if (te != null && !string.IsNullOrEmpty(te.text) && te.text.Contains(find)) { asText++; sb.AppendLine("TEXT  [" + ZCls(e) + "] " + te.text); }
    if (!string.IsNullOrEmpty(e.tooltip) && e.tooltip.Contains(find)) { asTip++; sb.AppendLine("TIP   " + e.GetType().Name + " [" + ZCls(e) + "] enabled=" + e.enabledInHierarchy + " : " + e.tooltip); }
}
return "'" + find + "': " + asText + " as visible text, " + asTip + " as a tooltip\n" + sb.ToString();
