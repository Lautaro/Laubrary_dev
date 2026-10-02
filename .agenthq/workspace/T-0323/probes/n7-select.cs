var w=ZWin("MirageWindow");
foreach (var e in ZAll(w.rootVisualElement)) { var b=e as UnityEngine.UIElements.Button; if (b!=null && ZDrawn(b) && b.text!=null && b.text.StartsWith("PreviewShooterZoe")) { ZClick(b); return "selected "+b.text; } }
return "no entry button";
