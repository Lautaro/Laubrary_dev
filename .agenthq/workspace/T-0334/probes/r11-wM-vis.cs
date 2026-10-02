var win = ZWin("ShaperWindow");
foreach (var e in ZAll(win.rootVisualElement)) if (e.ClassListContains("zui-popover"))
  return "popover " + e.worldBound + " visibility=" + e.resolvedStyle.visibility + " maxW=" + e.resolvedStyle.maxWidth;
return "no popover";
