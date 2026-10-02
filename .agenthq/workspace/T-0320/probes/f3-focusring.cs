var win = ZWin("ShaperWindow"); if (win==null) return "no window";
var all = ZAll(win.rootVisualElement);
UnityEngine.UIElements.VisualElement pad = null, mmx = null;
foreach (var e in all) { var n = e.GetType().Name;
  if (pad == null && n == "ZuiValue2DControl" && ZDrawn(e) && !e.ClassListContains("unity-disabled")) pad = e;
  if (mmx == null && (n == "ZuiMicroMinMax") && ZDrawn(e)) mmx = e;
}
var sb = new System.Text.StringBuilder();
sb.AppendLine("pad=" + (pad!=null ? pad.GetType().Name + " " + pad.worldBound : "none") + " mmx=" + (mmx!=null ? mmx.GetType().Name + " " + mmx.worldBound : "none"));
// record geometry of the pad and its next 3 siblings before focus
System.Func<UnityEngine.UIElements.VisualElement,string> geo = v => {
  if (v == null) return "null";
  var s = new System.Text.StringBuilder();
  s.Append(v.worldBound.ToString()).Append(" bw=").Append(v.resolvedStyle.borderTopWidth).Append("/").Append(v.resolvedStyle.borderLeftWidth)
   .Append(" bcol=").Append(v.resolvedStyle.borderTopColor);
  return s.ToString();
};
sb.AppendLine("BEFORE pad " + geo(pad));
if (pad != null) { var p = pad.hierarchy.parent; for (int i=0;i<p.hierarchy.childCount;i++) sb.AppendLine("  sib" + i + " " + p.hierarchy[i].GetType().Name + " " + p.hierarchy[i].worldBound); }
if (pad != null) pad.Focus();
UnityEditor.EditorPrefs.SetString("T320.focusPadId", pad != null ? pad.GetHashCode().ToString() : "");
return sb.ToString();
