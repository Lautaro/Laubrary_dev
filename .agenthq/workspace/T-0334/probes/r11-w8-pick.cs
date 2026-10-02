// ZClick(e): press an element the way a mouse does — PointerDown then PointerUp at its own centre, with a
// real pointer id, which is what a Clickable manipulator listens for.  Returns false if it could not.
// The shape picker is the first Button inside the Shape section whose own text is the current shape name.
var win = ZWin("ShaperWindow");
var secT = ZType("ZuiSection");
UnityEngine.UIElements.VisualElement shapeSec = null;
foreach (var e in ZAll(win.rootVisualElement)) {
  if (!secT.IsInstanceOfType(e) || !ZDrawn(e)) continue;
  foreach (var c in ZAll(e)) { var l = c as UnityEngine.UIElements.Label;
    if (l != null && l.ClassListContains("zui-section__title") && l.text.StartsWith("Shape")) { shapeSec = e; break; } }
  if (shapeSec != null) break;
}
if (shapeSec == null) return "no Shape section";
UnityEngine.UIElements.Button pick = null;
foreach (var e in ZAll(shapeSec)) { var b = e as UnityEngine.UIElements.Button; if (b != null && ZDrawn(b) && b.hierarchy.childCount > 0) { pick = b; break; } }
if (pick == null) return "no picker button in the Shape section";
ZClick(pick);
return "clicked '" + ZCaption(pick) + "' at " + pick.worldBound + " (window " + win.position.width + " wide)";
