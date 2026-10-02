var w = ZWin("ShaperWindow"); if (w == null) return "no ShaperWindow";
var pp = (float)typeof(UnityEditor.EditorGUIUtility).GetProperty("pixelsPerPoint", System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic).GetValue(null);
var sb = new System.Text.StringBuilder();
foreach (var e in ZAll(w.rootVisualElement))
{
    if (!ZDrawn(e)) continue;
    if (!(e is UnityEngine.UIElements.IMGUIContainer) && e.GetType().Name.IndexOf("Stage", System.StringComparison.OrdinalIgnoreCase) < 0
        && e.GetType().Name.IndexOf("Canvas", System.StringComparison.OrdinalIgnoreCase) < 0
        && e.GetType().Name.IndexOf("Preview", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
    var b = e.worldBound;
    sb.AppendLine(e.GetType().Name + " [" + ZCls(e) + "] rect=" + Mathf.RoundToInt(b.x*pp) + "," + Mathf.RoundToInt(b.y*pp)
      + "," + Mathf.RoundToInt(b.width*pp) + "," + Mathf.RoundToInt(b.height*pp));
}
return sb.Length == 0 ? "no IMGUI/stage/canvas/preview element found" : sb.ToString();
