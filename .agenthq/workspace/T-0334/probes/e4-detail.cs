var sb = new System.Text.StringBuilder();
foreach (var n in new string[]{"BackSplashWindow","SpriteFxStackWindow","LatheWindow","ZoeWindow","TextSplashWindow"})
{
    var w = ZWin(n); if (w == null) continue;
    var txt = ZAudit(w, n);
    sb.Append("=== ").Append(n).Append(" ===\n");
    bool cap=false, ovf=false;
    foreach (var line in txt.Split('\n'))
    {
        if (line.StartsWith("CAPTION") || line.StartsWith("OVERFLOW"))
            sb.Append("  ").Append(line.Length > 260 ? line.Substring(0,260) : line).Append("\n");
    }
}
return sb.ToString();
