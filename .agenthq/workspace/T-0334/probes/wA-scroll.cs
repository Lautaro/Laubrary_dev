// Scroll the window's ScrollView so the first element whose caption contains T334.find is on screen.
string win = UnityEditor.EditorPrefs.GetString("T334.secWin", "ZoeWindow");
string find = UnityEditor.EditorPrefs.GetString("T334.find", "Muzzle Event Name");
var w = ZWin(win); if (w == null) return "no window";
UnityEngine.UIElements.VisualElement target = null;
foreach (var e in ZAll(w.rootVisualElement))
{
    var s = e as UnityEngine.UIElements.TextElement;
    if (s != null && ZDrawn(s) && !string.IsNullOrEmpty(s.text) && s.text.Contains(find)) { target = s; break; }
}
if (target == null) return "not found: " + find;
UnityEngine.UIElements.ScrollView sv = null;
for (var p = target.parent; p != null; p = p.parent) { var x = p as UnityEngine.UIElements.ScrollView; if (x != null) { sv = x; break; } }
if (sv == null) return "no ScrollView above " + find;
sv.ScrollTo(target);
w.Repaint();
return "scrolled to '" + find + "', offset now " + sv.scrollOffset.y;
