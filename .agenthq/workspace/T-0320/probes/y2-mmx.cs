// Isolate the min-max nudge: focus it, read _low/_high through the control's own fields, press Right.
var win = ZWin("ShaperWindow");
UnityEngine.UIElements.VisualElement mmx = null;
foreach (var e in ZAll(win.rootVisualElement)) if (e.GetType().Name == "ZuiMicroMinMax" && ZDrawn(e) && ZCaption(e) == "Lifetime") { mmx = e; break; }
if (mmx == null) return "no Lifetime min-max";
var BF = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
var lo = mmx.GetType().GetField("_low", BF); var hi = mmx.GetType().GetField("_high", BF); var dec = mmx.GetType().GetField("_decimals", BF);
var sb = new System.Text.StringBuilder("before low=" + lo.GetValue(mmx) + " high=" + hi.GetValue(mmx) + " decimals=" + dec.GetValue(mmx) + " focusable=" + mmx.focusable + "\n");
mmx.Focus();
var fc = win.rootVisualElement.panel.focusController;
sb.AppendLine("focused=" + (fc.focusedElement == null ? "null" : fc.focusedElement.GetType().Name) + " isTarget=" + object.ReferenceEquals(fc.focusedElement, mmx));
for (int i=0;i<3;i++) {
  var kd = UnityEngine.UIElements.KeyDownEvent.GetPooled('\0', UnityEngine.KeyCode.RightArrow, UnityEngine.EventModifiers.None);
  kd.target = mmx; using (kd) mmx.SendEvent(kd);
}
sb.AppendLine("after  low=" + lo.GetValue(mmx) + " high=" + hi.GetValue(mmx));
return sb.ToString();
