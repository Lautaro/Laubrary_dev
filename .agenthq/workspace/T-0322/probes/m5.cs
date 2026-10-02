var w = ZWin("CartographerWindow");
UnityEngine.UIElements.Button nb=null;
foreach (var e in ZAll(w.rootVisualElement)) { var b=e as UnityEngine.UIElements.Button; if (b!=null && ZDrawn(b) && b.text=="New") nb=b; }
if (nb==null) return "no New";
ZClick(nb); w.Repaint();
return "pressed";
