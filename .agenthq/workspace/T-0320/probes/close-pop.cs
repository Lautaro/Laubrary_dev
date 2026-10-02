var win = ZWin("ShaperWindow");
foreach (var e in ZAll(win.rootVisualElement)) if (e.name == "zui-popover-scrim") { e.RemoveFromHierarchy(); return "popover closed"; }
return "no popover";
