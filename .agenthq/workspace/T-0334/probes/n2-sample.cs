// One temporal sample: the transport's own readout, plus the audit counters while PLAYING.
var w = ZWin("ShaperWindow"); if (w == null) return "no window";
var sb = new System.Text.StringBuilder();
sb.Append("t=").Append((UnityEditor.EditorApplication.timeSinceStartup - UnityEditor.EditorPrefs.GetFloat("T334.t0", 0)).ToString("F2"));
foreach (var e in ZAll(w.rootVisualElement))
{
    var te = e as UnityEngine.UIElements.TextElement;
    if (te == null || !ZDrawn(te) || string.IsNullOrEmpty(te.text)) continue;
    if (te.text.StartsWith("frame ")) sb.Append(" readout='").Append(te.text).Append("'");
}
UnityEngine.UIElements.Button play = null;
foreach (var e in ZAll(w.rootVisualElement)) { var b = e as UnityEngine.UIElements.Button; if (b != null && ZDrawn(b) && b.text != null && (b.text.Contains("Play") || b.text.Contains("Pause"))) play = b; }
sb.Append(" btn='").Append(play != null ? play.text : "?").Append("'\n");
ZAudit(w, "shaper");
sb.Append("elements=").Append(ZCount["elements"]).Append(" drawn=").Append(ZCount["drawn"])
  .Append(" controls=").Append(ZCount["controls"])
  .Append(" captionShort=").Append(ZCount["captionShort"])
  .Append(" overflowX=").Append(ZCount["overflowParentX"])
  .Append(" offWindow=").Append(ZCount["overflowWindow"])
  .Append(" noTooltip=").Append(ZCount["noTooltip"])
  .Append(" inertNoReason=").Append(ZCount["inertNoReason"]).Append("\n");
return sb.ToString();
