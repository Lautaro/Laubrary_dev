var sb = new System.Text.StringBuilder();
string[] names = { "PyreWindow", "ChoreographerWindow", "TilesetBuilderWindow", "ShaperWindow", "ChunkWindow", "LatheWindow", "ZoeWindow", "MirageWindow" };
foreach (int h in new int[] { 520, 620, 720, 820, 900 })
{
    foreach (var n in names) { var w = ZWin(n); if (w != null) w.position = new UnityEngine.Rect(30, 30, 820, h); }
    sb.Append("h=").Append(h).Append(": ");
    foreach (var n in names)
    {
        var w = ZWin(n); if (w == null) continue;
        ZAudit(w, n);
        int off = ZCount["overflowWindow"], oy = ZCount["overflowParentY"], ox = ZCount["overflowParentX"], cs = ZCount["captionShort"];
        if (off + oy + ox + cs > 0) sb.Append(n).Append("(off=").Append(off).Append(" oy=").Append(oy).Append(" ox=").Append(ox).Append(" cs=").Append(cs).Append(") ");
    }
    sb.Append("\n");
}
return sb.ToString();
