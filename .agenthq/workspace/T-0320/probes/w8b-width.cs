var win = ZWin("ShaperWindow");
UnityEngine.UIElements.VisualElement pop = null;
foreach (var e in ZAll(win.rootVisualElement)) if (e.ClassListContains("zui-popover")) { pop = e; break; }
if (pop == null) return "no popover";
var sb = new System.Text.StringBuilder();
sb.AppendLine("window=" + win.position + " popover=" + pop.worldBound);
sb.AppendLine("spillRight=" + (pop.worldBound.xMax - win.position.width).ToString("F1") + " spillBottom=" + (pop.worldBound.yMax - (win.position.height + 26f)).ToString("F1"));
foreach (var e in ZAll(pop)) { var l = e as UnityEngine.UIElements.Label; if (l != null && l.ClassListContains("zui-menu__header")) sb.AppendLine("  header '" + l.text + "' " + l.worldBound); }
// headers may not carry that class; list the first label of each column instead
int col = 0;
foreach (var e in pop.hierarchy.Children()) foreach (var c in e.hierarchy.Children()) { col++; var first = ZAll(c).Find(x => x is UnityEngine.UIElements.Label lb && !string.IsNullOrEmpty(lb.text)); if (first != null) sb.AppendLine("  col" + col + " '" + ((UnityEngine.UIElements.Label)first).text + "' " + c.worldBound); }
return sb.ToString();
