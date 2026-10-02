var win = ZWin("PyreWindow"); var sb=new System.Text.StringBuilder();
var boxT = ZType("ZuiBox"); var titleP = boxT.GetProperty("TitleText");
string[] want = {"Fracture","Fracture 2","Flash","Chunks","Gobs","Dust"};
int total=0;
foreach (var e in ZAll(win.rootVisualElement)) {
  if (!boxT.IsInstanceOfType(e)) continue;
  string t = titleP.GetValue(e) as string;
  if (System.Array.IndexOf(want, t) < 0) continue;
  int n=0; var kinds=new System.Text.StringBuilder();
  foreach (var c in ZAll(e)) { if (c==e) continue; if (boxT.IsInstanceOfType(c)) continue; if (!ZIsLeafCtrl(c)) continue; n++; }
  // nested boxes inside?
  int inner=0; foreach (var c in ZAll(e)) if (c!=e && boxT.IsInstanceOfType(c)) inner++;
  sb.Append(t).Append(" leafCtrls=").Append(n).Append(" innerBoxes=").Append(inner).Append("\n");
  total+=n;
}
sb.Append("total=").Append(total).Append("\n");
return sb.ToString();
