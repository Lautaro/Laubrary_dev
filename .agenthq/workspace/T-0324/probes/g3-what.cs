// What KIND of control is the thing next to a given label? Reports type, classes and rect.
string wn = UnityEditor.EditorPrefs.GetString("T324.win", "");
string near = UnityEditor.EditorPrefs.GetString("T324.near", "");
var w = ZWin(wn); if (w == null) return "no " + wn;
var pp = (float)typeof(UnityEditor.EditorGUIUtility).GetProperty("pixelsPerPoint", System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic).GetValue(null);
var sb = new System.Text.StringBuilder();
foreach (var e in ZAll(w.rootVisualElement))
{
    if (!ZDrawn(e)) continue;
    var te = e as UnityEngine.UIElements.TextElement;
    bool isLbl = te != null && te.text == near;
    bool isCtl = ZIsLeafCtrl(e) && (ZCaption(e) == near || (te != null && te.text == near));
    if (!isLbl && !isCtl) continue;
    var b = e.worldBound;
    sb.AppendLine((isCtl ? "CTRL " : "LABEL ") + e.GetType().Name + " [" + ZCls(e) + "] rect="
      + Mathf.RoundToInt(b.x*pp) + "," + Mathf.RoundToInt(b.y*pp) + "," + Mathf.RoundToInt(b.width*pp) + "," + Mathf.RoundToInt(b.height*pp));
    // siblings on the same row
    var p = e.hierarchy.parent;
    if (p != null) for (int i = 0; i < p.hierarchy.childCount; i++)
    { var s = p.hierarchy[i]; if (!ZDrawn(s)) continue;
      var sb2 = s.worldBound;
      sb.AppendLine("      sib " + s.GetType().Name + " [" + ZCls(s) + "] text='" + ZOwnText(s) + "' rect="
        + Mathf.RoundToInt(sb2.x*pp) + "," + Mathf.RoundToInt(sb2.y*pp) + "," + Mathf.RoundToInt(sb2.width*pp) + "," + Mathf.RoundToInt(sb2.height*pp)); }
}
return sb.Length == 0 ? "nothing captioned '" + near + "'" : sb.ToString();
