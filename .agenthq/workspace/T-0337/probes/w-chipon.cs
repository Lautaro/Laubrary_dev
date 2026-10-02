// press the first OFF chip in the section toggle bar (one per eval — a press rebuilds the window).
var w = ZWin("ShaperWindow"); if (w == null) return "no window";
var offs = new System.Collections.Generic.List<UnityEngine.UIElements.Button>();
int total = 0;
foreach (var e in ZAll(w.rootVisualElement)) {
  var b = e as UnityEngine.UIElements.Button; if (b == null || !ZDrawn(b)) continue;
  bool inBar = false; for (var p = b.hierarchy.parent; p != null; p = p.hierarchy.parent) if (p.GetType().Name == "ZuiSectionToggleBar") { inBar = true; break; }
  if (!inBar) continue;
  if (b.text == "Sections" || b.text == "Toggle Bar") continue; // the bar's own MODE segment, not a section chip
  total++;
  if (!ZCls(b).Contains("zui-segmented__on") && b.enabledInHierarchy) offs.Add(b);
}
if (offs.Count == 0) return "all on (chips=" + total + ")";
var r = ZPress(w, offs[0]);
return "off=" + offs.Count + " of " + total + " -> " + r;
