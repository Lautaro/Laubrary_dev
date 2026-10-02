// Press the nth drawn Button whose text is T334.pressText, scrolling it into view first if needed.
// Returns "scrolled — rerun" when it had to scroll, because a synthesized press only reaches an
// element that is actually on screen.
string bt = UnityEditor.EditorPrefs.GetString("T334.pressText", "");
int nth = UnityEditor.EditorPrefs.GetInt("T334.pressNth", 0);
var w = ZWin(UnityEditor.EditorPrefs.GetString("T334.walkWin", "ShaperWindow"));
if (w == null) return "no window";
var wr = w.rootVisualElement.worldBound;
UnityEngine.UIElements.Button hit = null; int seen = 0;
foreach (var e in ZAll(w.rootVisualElement))
{
    var b = e as UnityEngine.UIElements.Button;
    if (b == null || !ZDrawn(b) || b.text != bt) continue;
    if (seen++ != nth) continue;
    hit = b; break;
}
if (hit == null) return "NO BUTTON '" + bt + "' #" + nth;
if (hit.worldBound.yMax > wr.yMax || hit.worldBound.yMin < wr.yMin)
{
    UnityEngine.UIElements.ScrollView sv = null;
    for (var p = hit.hierarchy.parent; p != null; p = p.hierarchy.parent) { var s = p as UnityEngine.UIElements.ScrollView; if (s != null) { sv = s; break; } }
    if (sv == null) return "'" + bt + "' is off-window at " + hit.worldBound + " with NO ScrollView ancestor — UNREACHABLE";
    sv.ScrollTo(hit); w.Repaint();
    return "'" + bt + "' was off-window (" + hit.worldBound + "); scrolled offset=" + sv.scrollOffset + " — rerun";
}
bool en = hit.enabledInHierarchy;
ZClick(hit);
return "pressed '" + bt + "' #" + nth + " enabled=" + en + " at " + hit.worldBound;
