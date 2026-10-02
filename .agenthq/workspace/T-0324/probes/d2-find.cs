// Scroll a named MicroSlider caption into view and report its capture-pixel rect.
string wn   = UnityEditor.EditorPrefs.GetString("T324.win", "");
string find = UnityEditor.EditorPrefs.GetString("T324.find", "");
var win = ZWin(wn); if (win == null) return "no window " + wn;
var pp = (float)typeof(UnityEditor.EditorGUIUtility).GetProperty("pixelsPerPoint", System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic).GetValue(null);
var sb = new System.Text.StringBuilder();
foreach (var e in ZAll(win.rootVisualElement))
{
    var te = e as UnityEngine.UIElements.TextElement;
    if (te == null || !ZDrawn(e) || string.IsNullOrEmpty(te.text) || !te.text.Contains(find)) continue;
    var b = e.worldBound;
    sb.AppendLine("'" + te.text + "' [" + ZCls(e) + "] rect=" + Mathf.RoundToInt(b.x*pp) + "," + Mathf.RoundToInt(b.y*pp)
        + "," + Mathf.RoundToInt(b.width*pp) + "," + Mathf.RoundToInt(b.height*pp)
        + " need=" + ZNeed(te).ToString("0.##") + " have=" + ZHave(te).ToString("0.##"));
}
return sb.Length == 0 ? "no text containing '" + find + "'" : sb.ToString();
