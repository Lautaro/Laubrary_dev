var w = ZWin("ShaperWindow"); var sb = new System.Text.StringBuilder();
UnityEngine.UIElements.Button play = null;
foreach (var e in ZAll(w.rootVisualElement)) { var b = e as UnityEngine.UIElements.Button; if (b != null && ZDrawn(b) && b.text != null && (b.text.Contains("Play") || b.text.Contains("Pause"))) play = b; }
sb.Append("btn='").Append(play != null ? play.text : "?").Append("' ");
foreach (var e in ZAll(w.rootVisualElement))
  if (ZDrawn(e) && e is UnityEngine.UIElements.TextElement te && te.text != null && te.text.Contains("frame "))
    sb.Append("readout='").Append(te.text).Append("' ");
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
for (var t = w.GetType(); t != null; t = t.BaseType)
  foreach (var f in t.GetFields(BFi | System.Reflection.BindingFlags.DeclaredOnly))
    if (f.Name.ToLower().Contains("frame") && f.FieldType == typeof(int)) sb.Append(f.Name).Append("=").Append(f.GetValue(w)).Append(" ");
ZAudit(w, "shaper");
sb.Append("| captionShort=").Append(ZCount["captionShort"]).Append(" overflowX=").Append(ZCount["overflowParentX"])
  .Append(" offWindow=").Append(ZCount["overflowWindow"]).Append(" noTooltip=").Append(ZCount["noTooltip"])
  .Append(" inertNoReason=").Append(ZCount["inertNoReason"]).Append("\n");
return sb.ToString();
