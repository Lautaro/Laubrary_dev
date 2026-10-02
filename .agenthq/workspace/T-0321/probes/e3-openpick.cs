var win = ZWin("ShaperWindow");
UnityEngine.UIElements.Button pick = null;
foreach (var e in ZAll(win.rootVisualElement)) { var b = e as UnityEngine.UIElements.Button;
  if (b==null||!ZDrawn(b)) continue; if (!string.IsNullOrEmpty(b.text)) continue;
  foreach (var c in ZAll(b)) { var l=c as UnityEngine.UIElements.Label; if (l!=null && l.text=="Star") { pick=b; break; } } if (pick!=null) break; }
if (pick==null) return "no picker";
ZClick(pick);
return "clicked " + pick.worldBound;
