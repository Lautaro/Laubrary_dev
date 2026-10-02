// Sample the Shaper transport: the readout text the user reads, plus the window's own frame state.
var w = ZWin("ShaperWindow"); if (w == null) return "no ShaperWindow";
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
var sb = new System.Text.StringBuilder();
sb.Append("t=").Append((UnityEditor.EditorApplication.timeSinceStartup - UnityEditor.EditorPrefs.GetFloat("T328.t0", 0)).ToString("F2")).Append(" ");
foreach (var f in w.GetType().GetFields(BFi))
    if (f.Name.ToLower().Contains("frame") || f.Name.ToLower().Contains("play") || f.Name.ToLower().Contains("phase"))
        sb.Append(f.Name).Append("=").Append(f.GetValue(w)).Append(" ");
sb.Append("\n");
foreach (var e in ZAll(w.rootVisualElement))
{
    var te = e as UnityEngine.UIElements.TextElement;
    if (te == null || string.IsNullOrEmpty(te.text) || !ZDrawn(te)) continue;
    if (te.text.Contains("frame") || te.text.Contains("Frame") || te.text.Contains("/") || te.text.Contains("Play") || te.text.Contains("Pause"))
        sb.Append("  '").Append(te.text).Append("'\n");
}
string rep = ZAudit(w, "shaper");
sb.Append("audit: captionShort=").Append(ZCount["captionShort"]).Append(" overflowX=").Append(ZCount["overflowParentX"])
  .Append(" offWindow=").Append(ZCount["overflowWindow"]).Append(" noTooltip=").Append(ZCount["noTooltip"])
  .Append(" inertNoReason=").Append(ZCount["inertNoReason"])
  .Append(" elements=").Append(ZCount["elements"]).Append(" drawn=").Append(ZCount["drawn"]).Append(" controls=").Append(ZCount["controls"]).Append("\n");
return sb.ToString();
