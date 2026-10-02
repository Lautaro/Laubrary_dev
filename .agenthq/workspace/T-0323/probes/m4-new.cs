var w=ZWin("MirageWindow");
foreach (var e in ZAll(w.rootVisualElement)) { var b=e as UnityEngine.UIElements.Button; if (b!=null && ZDrawn(b) && b.text=="New") { ZClick(b); return "pressed New"; } }
return "no New";
