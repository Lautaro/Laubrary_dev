var win = ZWin("ZoeWindow"); if (win == null) return "no window";
UnityEngine.UIElements.ScrollView sv = null;
foreach (var e in ZAll(win.rootVisualElement)) { var s = e as UnityEngine.UIElements.ScrollView; if (s != null && ZDrawn(s)) { sv = s; break; } }
if (sv == null) return "no scrollview";
sv.scrollOffset = new Vector2(sv.scrollOffset.x, sv.scrollOffset.y + 640f);
win.Repaint();
var sb = new System.Text.StringBuilder();
sb.Append("offset=").Append(sv.scrollOffset).Append("\n");
foreach (var e in ZAll(win.rootVisualElement))
{
    var d = e as UnityEngine.UIElements.DropdownField;
    if (d == null || !ZDrawn(d)) continue;
    if (d.value == "None declared" || d.value.StartsWith("Muzzle") || d.value == "Upper")
        sb.Append("  '").Append(d.value).Append("' at ").Append(d.worldBound).Append("\n");
}
return sb.ToString();
