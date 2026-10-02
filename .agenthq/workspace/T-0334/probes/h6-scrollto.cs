// Scroll the Zoe window to the first "None declared" dropdown (the Muzzle Event Name picker).
var win = ZWin("ZoeWindow"); if (win == null) return "no window";
UnityEngine.UIElements.VisualElement hit = null;
foreach (var e in ZAll(win.rootVisualElement))
{
    var d = e as UnityEngine.UIElements.DropdownField;
    if (d == null || !ZDrawn(d) || d.value != "None declared") continue;
    hit = d; break;
}
if (hit == null) return "not found";
UnityEngine.UIElements.ScrollView sv = null;
for (var p = hit.hierarchy.parent; p != null; p = p.hierarchy.parent) { var s = p as UnityEngine.UIElements.ScrollView; if (s != null) { sv = s; break; } }
if (sv != null) sv.ScrollTo(hit);
win.Repaint();
return "scrolled to " + hit.worldBound + (sv != null ? " offset=" + sv.scrollOffset : " (no scrollview)");
