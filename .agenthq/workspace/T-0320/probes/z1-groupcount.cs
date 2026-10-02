// Open every box, then count the leaf controls inside each named box — the flattening must not have lost a dial.
var win = ZWin("PyreWindow"); if (win==null) return "no pyre";
var secT = ZType("ZuiSection"); var isOpen = secT.GetProperty("IsOpen");
foreach (var e in ZAll(win.rootVisualElement)) { if (secT.IsInstanceOfType(e)) isOpen.SetValue(e, true); var bx = e as Laubrary.Zui.ZuiBox; if (bx != null) bx.IsOpen = true; }
return "opened";
