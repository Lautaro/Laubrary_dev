var w = ZWin("ShaperWindow");
UnityEngine.UIElements.VisualElement hit = null;
foreach (var e in ZAll(w.rootVisualElement)) { var b = e as UnityEngine.UIElements.Button; if (b != null && ZDrawn(b) && b.text == "Bake") hit = b; }
if (hit == null) return "no Bake";
UnityEngine.UIElements.ScrollView sv = null;
for (var p = hit.hierarchy.parent; p != null; p = p.hierarchy.parent) { var s = p as UnityEngine.UIElements.ScrollView; if (s != null) { sv = s; break; } }
if (sv != null) sv.ScrollTo(hit);
w.Repaint();
return "bake at " + hit.worldBound + " winRoot=" + w.rootVisualElement.worldBound + (sv != null ? " scrolled" : " (no scrollview)");
