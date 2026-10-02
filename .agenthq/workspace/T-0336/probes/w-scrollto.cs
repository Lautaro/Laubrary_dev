// scroll whatever ScrollView owns the first drawn element whose caption/text contains T336.scrollTo
string want = UnityEditor.EditorPrefs.GetString("T336.scrollTo", "");
var w = ZWin("ShaperWindow"); if (w == null) return "no window";
UnityEngine.UIElements.VisualElement target = null;
foreach (var e in ZAll(w.rootVisualElement)) {
  if (!ZDrawn(e)) continue;
  string t = ZOwnText(e);
  if (string.IsNullOrEmpty(t) || !t.Contains(want)) continue;
  target = e; break;
}
if (target == null) return "no element containing '" + want + "'";
UnityEngine.UIElements.ScrollView sv = null;
for (var p = target.hierarchy.parent; p != null; p = p.hierarchy.parent) { var s = p as UnityEngine.UIElements.ScrollView; if (s != null) { sv = s; break; } }
if (sv == null) return "found '" + want + "' at " + target.worldBound + " but no ScrollView ancestor";
sv.ScrollTo(target); w.Repaint();
return "scrolled to '" + want + "' was " + target.worldBound + " offset=" + sv.scrollOffset;
