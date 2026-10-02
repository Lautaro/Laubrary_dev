var win = ZWin("ShaperWindow"); if (win==null) return "no window";
var all = ZAll(win.rootVisualElement);
var sb = new System.Text.StringBuilder();
bool inLights = false;
foreach (var e in all) {
  if (!ZDrawn(e)) continue;
  string cap = ZCaption(e);
  string cls = ZCls(e);
  if (cls.Contains("zui-section")) { }
  // print every leaf control plus its type/class/rect in the left pane
  if (ZIsLeafCtrl(e)) {
    var wb = e.worldBound;
    if (wb.x < 810) sb.AppendLine(e.GetType().Name + " | cap='" + cap + "' | cls=" + cls + " | " + wb.x.ToString("F0") + "," + wb.y.ToString("F0") + " " + wb.width.ToString("F0") + "x" + wb.height.ToString("F0") + " | tip='" + ZTip(e) + "'");
  }
}
return ZDump("lights-leaves", sb.ToString());
