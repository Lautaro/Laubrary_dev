// Turn Swarm on through its section header toggle (the sanctioned chrome checkbox), the way a user does.
var win = ZWin("ShaperWindow"); var secT = ZType("ZuiSection");
UnityEngine.UIElements.VisualElement swarm = null;
foreach (var e in ZAll(win.rootVisualElement)) { if (!secT.IsInstanceOfType(e) || !ZDrawn(e)) continue;
  foreach (var c in ZAll(e)) { var l = c as UnityEngine.UIElements.Label; if (l != null && l.ClassListContains("zui-section__title") && l.text == "Swarm") { swarm = e; break; } } if (swarm != null) break; }
if (swarm == null) return "no Swarm section";
UnityEngine.UIElements.Toggle t2 = null;
foreach (var c in ZAll(swarm)) if (c is UnityEngine.UIElements.Toggle tg && c.ClassListContains("zui-section__toggle")) { t2 = tg; break; }
if (t2 == null) return "no Swarm header toggle";
bool before = t2.value; t2.value = !before;   // a header toggle's own value change is what a click does
return "swarm toggle " + before + " -> " + t2.value;
