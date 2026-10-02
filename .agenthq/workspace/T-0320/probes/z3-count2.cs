var win = ZWin("PyreWindow"); var sb = new System.Text.StringBuilder();
foreach (var name in new string[]{ "Fracture", "Fracture 2", "Flash", "Chunks", "Gobs", "Dust" }) {
  Laubrary.Zui.ZuiBox box = null;
  foreach (var e in ZAll(win.rootVisualElement)) { var bx = e as Laubrary.Zui.ZuiBox; if (bx != null && bx.TitleText == name) { box = bx; break; } }
  if (box == null) { sb.AppendLine(name + ": BOX MISSING"); continue; }
  int dials = 0; var names = new System.Text.StringBuilder();
  foreach (var e in ZAll(box)) { if (e == box) continue;
    var tn = e.GetType().Name;
    if (tn == "ZuiMicroSlider" || tn == "ZuiMicroMinMax" || tn == "ZuiValue2DControl" || tn == "ZuiPad" || (e is UnityEngine.UIElements.Button && !(e is UnityEngine.UIElements.Toggle) && tn != "ZuiToggleButton") || tn == "ZuiToggleButton")
    { dials++; if (dials <= 20) names.Append(ZCaption(e)).Append(", "); } }
  sb.AppendLine(name + ": dials=" + dials + "  [" + names + "]");
}
return sb.ToString();
