var w = ZWin("ZoeWindow"); if (w == null) return "no ZoeWindow";
var sb = new System.Text.StringBuilder();
foreach (var e in ZAll(w.rootVisualElement))
{
    var s = e as UnityEngine.UIElements.TextElement;
    if (s == null || string.IsNullOrEmpty(s.text) || !ZDrawn(s)) continue;
    if (s.text.Contains("Muzzle") || s.text.Contains("unresolved") || s.text.Contains("None declared"))
    { var b = s.worldBound; sb.Append("'").Append(s.text).Append("' at ").Append(b.x.ToString("F0")).Append(",").Append(b.y.ToString("F0")).Append("\n"); }
}
sb.Append("scrollY: ");
foreach (var e in ZAll(w.rootVisualElement)) { var sv = e as UnityEngine.UIElements.ScrollView; if (sv != null) sb.Append(sv.scrollOffset.y).Append(" "); }
return sb.ToString();
