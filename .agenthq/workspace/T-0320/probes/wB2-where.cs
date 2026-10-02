var win = ZWin("ShaperWindow");
UnityEngine.UIElements.VisualElement scrim = null;
foreach (var e in ZAll(win.rootVisualElement)) if (e.name == "zui-popover-scrim") { scrim = e; break; }
if (scrim == null) return "menu not open";
var sb = new System.Text.StringBuilder();
foreach (var e in ZAll(scrim)) { if (!e.ClassListContains("zui-popover")) continue; sb.AppendLine("popover " + e.worldBound + " window h=" + win.position.height); }
foreach (var e in ZAll(scrim)) { if (!e.ClassListContains("zui-menu__item")) continue;
  string label = null; foreach (var c in ZAll(e)) { var l = c as UnityEngine.UIElements.Label; if (l != null && l.text != "✓" && !string.IsNullOrEmpty(l.text)) { label = l.text; break; } }
  if (label == "Star") sb.AppendLine("Star item " + e.worldBound + " insideWindow=" + (e.worldBound.yMax <= win.position.height)); }
return sb.ToString();
