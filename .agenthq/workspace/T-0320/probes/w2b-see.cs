var win = ZWin("ShaperWindow"); var sb = new System.Text.StringBuilder();
foreach (var e in ZAll(win.rootVisualElement)) { if (!ZDrawn(e)) continue; if (ZIsLeafCtrl(e)) sb.AppendLine(e.GetType().Name + " '" + ZCaption(e) + "' " + e.worldBound + " tip='" + ZTip(e) + "'"); }
return sb.ToString();
