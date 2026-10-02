var w = ZWin("CartographerWindow");
foreach (var e in ZAll(w.rootVisualElement)) { var b=e as UnityEngine.UIElements.Button; if (b!=null && ZDrawn(b) && b.text=="Cancel") { ZClick(b); return "cancelled"; } }
return "no cancel";
