// scroll the right pane so the Sprite ObjectField is in view
var w = ZWin("ShaperWindow"); if (w == null) return "no window";
UnityEditor.UIElements.ObjectField target = null;
foreach (var e in ZAll(w.rootVisualElement)) { var f = e as UnityEditor.UIElements.ObjectField; if (f != null && ZDrawn(f) && f.objectType == typeof(UnityEngine.Sprite)) { target = f; break; } }
if (target == null) return "no sprite field";
UnityEngine.UIElements.ScrollView sv = null;
for (var p = target.hierarchy.parent; p != null; p = p.hierarchy.parent) { var s = p as UnityEngine.UIElements.ScrollView; if (s != null) { sv = s; break; } }
if (sv == null) return "no scrollview";
sv.scrollOffset = new UnityEngine.Vector2(sv.scrollOffset.x, 100000f);
w.Repaint();
return "field=" + target.worldBound + " offset=" + sv.scrollOffset + " viewport=" + sv.contentViewport.worldBound + " content=" + sv.contentContainer.worldBound;
