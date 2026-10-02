var w=ZWin("MirageWindow"); w.position=new Rect(20,20,900,880);
var secT=ZType("ZuiSection"); var isOpen=secT.GetProperty("IsOpen");
foreach (var e in ZAll(w.rootVisualElement)) if (secT.IsInstanceOfType(e)) isOpen.SetValue(e,true);
w.Repaint(); return "ok";
