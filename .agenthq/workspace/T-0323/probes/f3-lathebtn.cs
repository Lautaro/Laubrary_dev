var sb=new System.Text.StringBuilder();
var w=ZWin("LatheWindow");
var secT=ZType("ZuiSection"); var isOpen=secT.GetProperty("IsOpen");
foreach (var e in ZAll(w.rootVisualElement)) if (secT.IsInstanceOfType(e)) isOpen.SetValue(e,true);
w.Repaint();
return "opened";
