// Where is each numeric-input MicroSlider on screen right now, in CAPTURE pixels (worldBound * pixelsPerPoint)?
string wn = UnityEditor.EditorPrefs.GetString("T324.win", "PyreWindow");
var win = ZWin(wn); if (win == null) return "no window " + wn;
var pp = (float)typeof(UnityEditor.EditorGUIUtility).GetProperty("pixelsPerPoint", System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic).GetValue(null);
var sb = new System.Text.StringBuilder();
foreach (var e in ZAll(win.rootVisualElement))
{
    if (e.GetType().Name != "ZuiMicroSlider" || !ZDrawn(e)) continue;
    UnityEngine.UIElements.VisualElement num = null; UnityEngine.UIElements.Label cap = null;
    foreach (var c in ZAll(e)) { var cl = ZCls(c);
        if (cl.Contains("zui-microslider__numfield") && num == null) num = c;
        if (cl.Contains("zui-microslider__caption") && cap == null) cap = c as UnityEngine.UIElements.Label; }
    if (num == null || num.resolvedStyle.display != UnityEngine.UIElements.DisplayStyle.Flex) continue;
    var b = e.worldBound;
    sb.AppendLine((cap != null ? cap.text : "?") + "  rect=" + Mathf.RoundToInt(b.x*pp) + "," + Mathf.RoundToInt(b.y*pp)
        + "," + Mathf.RoundToInt(b.width*pp) + "," + Mathf.RoundToInt(b.height*pp)
        + "   fieldW=" + num.resolvedStyle.width.ToString("0.##") + " capRight=" + (cap != null ? cap.resolvedStyle.right.ToString("0.##") : "?"));
}
return sb.ToString();
