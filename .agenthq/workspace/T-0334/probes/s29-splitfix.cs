var w = ZWin("ShaperWindow"); if (w == null) return "no window";
var sb = new System.Text.StringBuilder();
string mode = UnityEditor.EditorPrefs.GetString("T334.splitMode", "report");
foreach (var e in ZAll(w.rootVisualElement)) {
  var sp = e as UnityEngine.UIElements.TwoPaneSplitView; if (sp == null) continue;
  var cc = sp.hierarchy[0]; var leftPane = cc.hierarchy[0]; var anchor = sp.hierarchy[1];
  var t = sp.GetType();
  var BF = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic;
  var fpd = t.GetProperty("fixedPaneDimension", BF);
  sb.Append("BEFORE leftW=").Append(leftPane.resolvedStyle.width).Append(" anchorLeft=").Append(anchor.resolvedStyle.left)
    .Append(" fixedPaneDimension=").Append(fpd == null ? "<none>" : fpd.GetValue(sp).ToString())
    .Append(" fixedPaneInitialDimension=").Append(sp.fixedPaneInitialDimension).Append("\n");
  float target = leftPane.resolvedStyle.width;
  if (mode == "fpd" && fpd != null) { fpd.SetValue(sp, target); sb.Append("set fixedPaneDimension=").Append(target).Append("\n"); }
  else if (mode == "init") { sp.fixedPaneInitialDimension = target; sb.Append("set fixedPaneInitialDimension=").Append(target).Append("\n"); }
  else if (mode == "offset") { var m = t.GetMethod("SetDragLineOffset", BF); if (m != null) { m.Invoke(sp, new object[]{ target }); sb.Append("SetDragLineOffset(").Append(target).Append(")\n"); } }
}
return sb.ToString();
