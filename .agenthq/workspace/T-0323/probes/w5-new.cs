var sb=new System.Text.StringBuilder();
var w=ZWin("SpriteFxStackWindow");
UnityEngine.UIElements.Button b=null;
foreach (var e in ZAll(w.rootVisualElement)) { var x=e as UnityEngine.UIElements.Button; if (x!=null && ZDrawn(x) && x.text=="New") b=x; }
if (b==null) return "no New";
ZClick(b);
return "pressed New";
