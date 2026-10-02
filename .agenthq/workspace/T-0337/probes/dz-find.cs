var w = ZWin("ShaperWindow"); if (w==null) return "no window";
string[] wants = { "Bloom Mode", "Lobe Count", "GIF scale", "Plume Amount", "Gate Gain", "Swirl" };
var sb=new System.Text.StringBuilder();
foreach (var want in wants) {
  int n=0;
  foreach (var e in ZAll(w.rootVisualElement)) {
    string cap = ZCaption(e);
    if (cap == null || cap != want) continue;
    if (!ZDrawn(e)) continue;
    n++;
    sb.Append(want).Append("  type=").Append(e.GetType().Name)
      .Append(" enabledSelf=").Append(e.enabledSelf).Append(" enabledInHierarchy=").Append(e.enabledInHierarchy)
      .Append("\n    tip=").Append(ZTip(e)).Append('\n');
    if (n>=2) break;
  }
  if (n==0) sb.Append(want).Append("  <not drawn>\n");
}
return sb.ToString();
