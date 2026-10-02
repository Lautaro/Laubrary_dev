// Scroll a MicroSlider whose caption matches T324.find into view, so a desktop capture can see it.
string wn = UnityEditor.EditorPrefs.GetString("T324.win", "PyreWindow");
string find = UnityEditor.EditorPrefs.GetString("T324.find", "Size");
int nth = UnityEditor.EditorPrefs.GetInt("T324.findNth", 0);
var win = ZWin(wn); if (win == null) return "no window " + wn;
UnityEngine.UIElements.VisualElement hit = null; int seen = 0;
foreach (var e in ZAll(win.rootVisualElement))
{
    if (e.GetType().Name != "ZuiMicroSlider" || !ZDrawn(e)) continue;
    UnityEngine.UIElements.Label cap = null;
    foreach (var c in ZAll(e)) { if (ZCls(c).Contains("zui-microslider__caption")) { cap = c as UnityEngine.UIElements.Label; break; } }
    if (cap == null || cap.text != find) continue;
    if (seen++ != nth) continue;
    hit = e; break;
}
if (hit == null) return "no MicroSlider captioned '" + find + "' #" + nth;
UnityEngine.UIElements.ScrollView sv = null;
for (var p = hit.hierarchy.parent; p != null; p = p.hierarchy.parent) { var s = p as UnityEngine.UIElements.ScrollView; if (s != null) { sv = s; break; } }
if (sv != null) sv.ScrollTo(hit);
win.Repaint();
return "found '" + find + "' #" + nth + " at " + hit.worldBound + (sv != null ? " scrolled offset=" + sv.scrollOffset : " (no scrollview)");
