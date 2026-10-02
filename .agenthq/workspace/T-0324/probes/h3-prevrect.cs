var w = ZWin("ShaperWindow"); if (w == null) return "no ShaperWindow";
var pp = (float)typeof(UnityEditor.EditorGUIUtility).GetProperty("pixelsPerPoint", System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic).GetValue(null);
var sb = new System.Text.StringBuilder();
foreach (var e in ZAll(w.rootVisualElement))
{
    if (!ZDrawn(e)) continue;
    var img = e as UnityEngine.UIElements.Image;
    var bg = e.resolvedStyle.backgroundImage.texture;
    if (img == null && bg == null) continue;
    if (e.worldBound.width < 80) continue;
    var b = e.worldBound;
    sb.AppendLine(e.GetType().Name + " [" + ZCls(e) + "] rect=" + Mathf.RoundToInt(b.x*pp) + "," + Mathf.RoundToInt(b.y*pp)
      + "," + Mathf.RoundToInt(b.width*pp) + "," + Mathf.RoundToInt(b.height*pp)
      + " tex=" + (img != null && img.image != null ? img.image.name + " " + img.image.width + "x" + img.image.height
                   : bg != null ? bg.name + " " + bg.width + "x" + bg.height : "none"));
}
return sb.ToString();
