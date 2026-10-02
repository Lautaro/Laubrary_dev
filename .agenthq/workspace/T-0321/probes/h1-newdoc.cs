var win = ZWin("ShaperWindow");
UnityEngine.UIElements.Button nw=null;
foreach (var e in ZAll(win.rootVisualElement)) { var b=e as UnityEngine.UIElements.Button; if (b!=null && ZDrawn(b) && b.text=="New") nw=b; }
if (nw==null) return "no New";
ZClick(nw);
return "pressed New";
