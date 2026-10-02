// Turn every section of the focused window ON in its ZuiSectionToggleBar, so a toggled-off section's
// content is actually built (a hidden section is invisible to any element walk — a standing gotcha).
string wn = UnityEditor.EditorPrefs.GetString("T328.secWin", "ZoeWindow");
var win = ZWin(wn); if (win == null) return "no window " + wn;
var sb = new System.Text.StringBuilder();
string key = "ZuiSectionToggleBar." + wn + ".userSel";
sb.Append("PREV ").Append(key).Append(" = ").Append(UnityEditor.EditorPrefs.GetString(key, "<unset>")).Append("\n");
var cur = UnityEditor.EditorPrefs.GetString(key, "");
if (!string.IsNullOrEmpty(cur))
{
    var parts = cur.Split(';');
    for (int i = 0; i < parts.Length; i++)
    {
        int eq = parts[i].IndexOf('=');
        if (eq > 0) parts[i] = parts[i].Substring(0, eq) + "=1";
    }
    UnityEditor.EditorPrefs.SetString(key, string.Join(";", parts));
    sb.Append("NEW  ").Append(UnityEditor.EditorPrefs.GetString(key)).Append("\n");
}
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
foreach (var m in win.GetType().GetMethods(BFi)) if (m.Name == "Rebuild" && m.GetParameters().Length == 0) { m.Invoke(win, null); break; }
win.Repaint();
return sb.ToString();
