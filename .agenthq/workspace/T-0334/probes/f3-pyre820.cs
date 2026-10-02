var sb = new System.Text.StringBuilder();
foreach (var n in new string[]{"PyreWindow","ChoreographerWindow","TilesetBuilderWindow"})
{
  var w = ZWin(n);
  sb.Append("=== ").Append(n).Append(" pos=").Append(w.position).Append(" ===\n");
  var txt = ZAudit(w, n);
  int shown = 0;
  foreach (var line in txt.Split('\n'))
    if ((line.StartsWith("OFF-WINDOW") || line.StartsWith("OVERFLOW")) && shown++ < 8)
      sb.Append("  ").Append(line.Length > 220 ? line.Substring(0,220) : line).Append("\n");
  sb.Append("  (total shown ").Append(shown).Append(")\n");
}
return sb.ToString();
