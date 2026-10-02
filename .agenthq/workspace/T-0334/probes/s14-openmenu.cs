// open the Shape section's picker menu (and nothing else) — the menu lays out on the NEXT frame,
// so the entry must be clicked from a separate eval.
var w = ZWin("ShaperWindow"); if (w == null) return "no window";
var secT = ZType("ZuiSection"); UnityEngine.UIElements.VisualElement shapeSec = null;
foreach (var e in ZAll(w.rootVisualElement)) { if (!secT.IsInstanceOfType(e) || !ZDrawn(e)) continue;
  foreach (var c in ZAll(e)) { var l=c as UnityEngine.UIElements.Label; if (l!=null && l.ClassListContains("zui-section__title") && l.text.StartsWith("Shape")) { shapeSec=e; break; } }
  if (shapeSec != null) break; }
if (shapeSec == null) return "no Shape section";
UnityEngine.UIElements.Button pick = null;
foreach (var e in ZAll(shapeSec)) { var b=e as UnityEngine.UIElements.Button; if (b!=null && ZDrawn(b) && b.hierarchy.childCount>0) { pick=b; break; } }
if (pick == null) return "no picker button";
var wr = w.rootVisualElement.worldBound;
if (pick.worldBound.yMax > wr.yMax || pick.worldBound.yMin < wr.yMin) {
  UnityEngine.UIElements.ScrollView sv=null; for (var p=pick.hierarchy.parent;p!=null;p=p.hierarchy.parent){var s=p as UnityEngine.UIElements.ScrollView; if(s!=null){sv=s;break;}}
  if (sv==null) return "picker off-window, no scrollview";
  sv.ScrollTo(pick); w.Repaint(); return "scrolled — rerun";
}
ZClick(pick);
return "opened picker '" + ZCaption(pick) + "' at " + pick.worldBound;
