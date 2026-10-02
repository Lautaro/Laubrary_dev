var win = ZWin("ShaperWindow");
// close any popover: press Escape-equivalent by removing scrim via its own click
var vt = win.rootVisualElement.panel.visualTree;
foreach (var e in ZAll(vt)) if ((ZCls(e)??"").Contains("zui-popover__scrim")) { ZClick(e); }
win.position = new Rect(20,20,1400,900);
win.Repaint();
return "resized";
