var sb = new System.Text.StringBuilder();
foreach (var n in new string[]{ "PyreWindow", "ChoreographerWindow", "TilesetBuilderWindow" })
{
  var w = ZWin(n); if (w == null) continue;
  ZAudit(w, n);
  sb.Append(n).Append(" ").Append(w.position.width.ToString("F0")).Append("x").Append(w.position.height.ToString("F0"))
    .Append(" off=").Append(ZCount["overflowWindow"]).Append(" oy=").Append(ZCount["overflowParentY"])
    .Append(" ox=").Append(ZCount["overflowParentX"]).Append(" cs=").Append(ZCount["captionShort"]).Append("\n");
}
return sb.ToString();
