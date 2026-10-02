var win = ZWin("ShaperWindow"); var sb = new System.Text.StringBuilder();
UnityEngine.UIElements.VisualElement scrim = null;
foreach (var e in ZAll(win.rootVisualElement)) if (e.name == "zui-popover-scrim") { scrim = e; break; }
if (scrim == null) return "no popover";
sb.AppendLine("scrim " + scrim.worldBound + " displayed=" + ZDisplayed(scrim) + " children=" + scrim.hierarchy.childCount);
int i = 0;
foreach (var e in ZAll(scrim)) { i++; if (i > 60) break; var te = e as UnityEngine.UIElements.TextElement;
  sb.AppendLine("  " + e.GetType().Name + " '" + (te!=null?te.text:e.name) + "' " + e.worldBound + " disp=" + ZDisplayed(e)); }
return sb.ToString();
