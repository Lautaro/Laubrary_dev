var win = ZWin("ShaperWindow"); int n=0; foreach (var e in ZAll(win.rootVisualElement)) if (ZDrawn(e)) n++;
return "drawnAfter=" + n;
