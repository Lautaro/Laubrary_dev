// Can the named button be brought under the pointer at the current window size, without pressing it?
string want = UnityEditor.EditorPrefs.GetString("T337.reach", "Bake");
var w = ZWin("ShaperWindow"); if (w == null) return "no window";
UnityEngine.UIElements.Button btn = null;
foreach (var e in ZAll(w.rootVisualElement)) { var b = e as UnityEngine.UIElements.Button; if (b != null && ZDrawn(b) && b.text == want) { btn = b; break; } }
if (btn == null) return "no '" + want + "' button drawn";
var panel = w.rootVisualElement.panel;
var hit = panel.Pick(btn.worldBound.center);
bool reaches = false; for (var p = hit; p != null; p = p.hierarchy.parent) if (p == btn) { reaches = true; break; }
if (reaches) return "REACHABLE '" + want + "' at " + btn.worldBound + " (Pick hits it)";
UnityEngine.UIElements.ScrollView sv = null;
for (var p = btn.hierarchy.parent; p != null; p = p.hierarchy.parent) { var s = p as UnityEngine.UIElements.ScrollView; if (s != null) { sv = s; break; } }
if (sv == null) return "UNREACHABLE '" + want + "' at " + btn.worldBound + " — no ScrollView ancestor; Pick got " + (hit==null?"<null>":ZCls(hit));
sv.ScrollTo(btn); w.Repaint();
return "scrolled toward '" + want + "' (was " + btn.worldBound + ", Pick got " + (hit==null?"<null>":ZCls(hit)) + ") offset=" + sv.scrollOffset + " — rerun";
