var w = ZWin("ShaperWindow");
UnityEngine.UIElements.Button b2=null;
foreach (var e in ZAll(w.rootVisualElement)) { var b=e as UnityEngine.UIElements.Button; if (b!=null && ZDrawn(b) && b.text!=null && b.text.Contains("Pause")) b2=b; }
if (b2!=null) ZClick(b2);
return "paused";
