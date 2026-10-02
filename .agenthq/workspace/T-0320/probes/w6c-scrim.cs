var win = ZWin("ShaperWindow"); var sb = new System.Text.StringBuilder();
UnityEngine.UIElements.VisualElement scrim = null;
foreach (var e in ZAll(win.rootVisualElement)) if (e.name == "zui-popover-scrim") { scrim = e; break; }
if (scrim == null) return "no popover open";
int n = 0;
foreach (var e in ZAll(scrim)) { if (!ZDrawn(e)) continue; if (ZIsLeafCtrl(e)) { n++; if (n < 40) sb.AppendLine("  " + e.GetType().Name + " '" + ZCaption(e) + "' " + e.worldBound); } }
return "popover open, leaf controls=" + n + "\n" + sb;
