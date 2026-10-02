// Every ObjectField's display label: does the asset name fit, and does it ELIDE when it does not?
string wn = UnityEditor.EditorPrefs.GetString("T324.win", "TextSplashWindow");
var win = ZWin(wn); if (win == null) return "no window " + wn;
var pp = (float)typeof(UnityEditor.EditorGUIUtility).GetProperty("pixelsPerPoint", System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic).GetValue(null);
var sb = new System.Text.StringBuilder(); int n = 0, over = 0;
foreach (var e in ZAll(win.rootVisualElement))
{
    if (!ZCls(e).Contains("unity-object-field-display__label")) continue;
    if (!ZDrawn(e)) continue;
    var te = e as UnityEngine.UIElements.TextElement; if (te == null) continue;
    n++;
    float need = ZNeed(te), have = ZHave(te);
    var b = e.worldBound;
    if (need > have + ZTOL) over++;
    sb.AppendLine((need > have + ZTOL ? "OVER " : "     ") + "'" + te.text + "' need=" + need.ToString("0.##")
        + " have=" + have.ToString("0.##") + " textOverflow=" + e.resolvedStyle.textOverflow
        + " whiteSpace=" + e.resolvedStyle.whiteSpace
        + " rect=" + Mathf.RoundToInt(b.x*pp) + "," + Mathf.RoundToInt(b.y*pp) + "," + Mathf.RoundToInt(b.width*pp) + "," + Mathf.RoundToInt(b.height*pp));
}
return wn + ": " + n + " ObjectField labels, " + over + " longer than their field\n" + sb.ToString();
