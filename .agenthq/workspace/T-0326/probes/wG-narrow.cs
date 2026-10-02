// Resize a window to a given width and audit it there. T326.narrowWins = names, T326.narrowW = width,
// or "min" to use each window's own declared minSize.
string names = UnityEditor.EditorPrefs.GetString("T326.narrowWins", "ShaperWindow|PyreWindow|ZoeWindow");
string mode = UnityEditor.EditorPrefs.GetString("T326.narrowW", "820");
var sb = new System.Text.StringBuilder();
foreach (var n in names.Split('|'))
{
    if (string.IsNullOrEmpty(n)) continue;
    var w = ZWin(n); if (w == null) { sb.Append(n).Append(": NOT OPEN\n"); continue; }
    var p = w.position;
    float target = mode == "min" ? w.minSize.x : float.Parse(mode);
    float targetH = mode == "min" ? Mathf.Max(w.minSize.y, 600f) : p.height;
    w.position = new Rect(p.x, p.y, target, targetH);
    w.Repaint();
    sb.Append(n).Append(": minSize=").Append(w.minSize).Append(" set to ").Append(target.ToString("F0")).Append("x").Append(targetH.ToString("F0")).Append("\n");
}
return sb.ToString();
