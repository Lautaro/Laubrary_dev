var win = ZWin("PyreWindow"); var sb = new System.Text.StringBuilder();
foreach (var name in new string[]{ "Fracture", "Fracture 2", "Flash", "Chunks", "Gobs", "Dust" }) {
  Laubrary.Zui.ZuiBox box = null;
  foreach (var e in ZAll(win.rootVisualElement)) { var bx = e as Laubrary.Zui.ZuiBox; if (bx != null && bx.TitleText == name) { box = bx; break; } }
  if (box == null) { sb.AppendLine(name + ": BOX MISSING"); continue; }
  int leaves = 0; foreach (var e in ZAll(box)) { if (e == box) continue; if (ZIsLeafCtrl(e) && ZDisplayed(e)) leaves++; }
  sb.AppendLine(name + ": leafControls=" + leaves + " open=" + box.IsOpen);
}
return sb.ToString();
