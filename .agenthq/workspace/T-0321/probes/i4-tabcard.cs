var win = ZWin("ShaperWindow"); var sb=new System.Text.StringBuilder();
// the whole Shape section: snapshot every drawn element's rect, then focus each focusable leaf in turn and re-snapshot
var all0 = ZAll(win.rootVisualElement);
var focusables = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>();
foreach (var e in all0) if (ZDrawn(e) && e.focusable && ZIsLeafCtrl(e)) focusables.Add(e);
System.Func<string> snap = () => { var s=new System.Text.StringBuilder(); foreach (var e in ZAll(win.rootVisualElement)) if (ZDrawn(e)) s.Append(e.worldBound.x.ToString("F1")).Append(",").Append(e.worldBound.y.ToString("F1")).Append(",").Append(e.worldBound.width.ToString("F1")).Append(",").Append(e.worldBound.height.ToString("F1")).Append(";"); return s.ToString(); };
string baseSnap = snap();
int moved=0; var names=new System.Text.StringBuilder();
foreach (var f in focusables) {
  f.Focus();
  string s = snap();
  if (s != baseSnap) { moved++; if (moved<6) names.Append(f.GetType().Name).Append("'").Append(ZCaption(f)).Append("' "); }
}
win.rootVisualElement.Focus();
sb.Append("focusables=").Append(focusables.Count).Append(" causedShift=").Append(moved).Append(" ").Append(names).Append("\n");
return sb.ToString();
