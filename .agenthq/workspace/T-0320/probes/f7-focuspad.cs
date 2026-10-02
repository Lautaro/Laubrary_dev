var win = ZWin("ShaperWindow"); if (win==null) return "no window";
foreach (var e in ZAll(win.rootVisualElement)) if (e.GetType().Name=="ZuiValue2DControl" && ZDrawn(e) && !e.ClassListContains("unity-disabled")) { e.Focus(); win.Repaint(); return "focused " + e.worldBound; }
return "none";
