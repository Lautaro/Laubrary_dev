// ZClick(e): press an element the way a mouse does — PointerDown then PointerUp at its own centre, with a
// real pointer id, which is what a Clickable manipulator listens for.  Returns false if it could not.
var win = ZWin("ShaperWindow");
string want = UnityEditor.EditorPrefs.GetString("T334.pick",""); string wantCol = UnityEditor.EditorPrefs.GetString("T334.col","");
UnityEngine.UIElements.VisualElement scrim = null;
foreach (var e in ZAll(win.rootVisualElement)) if (e.name == "zui-popover-scrim") { scrim = e; break; }
if (scrim == null) return "menu not open";
UnityEngine.UIElements.VisualElement target = null; string seen = "";
foreach (var e in ZAll(scrim)) {
  if (!e.ClassListContains("zui-menu__item")) continue;
  string label = null;
  foreach (var c in ZAll(e)) { var l = c as UnityEngine.UIElements.Label; if (l != null && l.text != "✓" && !string.IsNullOrEmpty(l.text)) { label = l.text; break; } }
  if (label != want) continue;
  string col = "";
  for (var p = e.hierarchy.parent; p != null; p = p.hierarchy.parent) { foreach (var c in p.hierarchy.Children()) { var l = c as UnityEngine.UIElements.Label; if (l != null && l.ClassListContains("zui-menu__section")) { col = l.text; break; } } if (col.Length > 0) break; }
  seen += col + ";";
  if (wantCol.Length == 0 || col == wantCol) { target = e; break; }
}
if (target == null) return "entry '" + want + "' not found in '" + wantCol + "' (cols: " + seen + ")";
ZClick(target);
return "clicked '" + want + "' in '" + wantCol + "'";
