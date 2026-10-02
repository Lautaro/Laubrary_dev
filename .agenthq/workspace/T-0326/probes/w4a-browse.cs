// Turn the in-window library browser ON for a set of ZuiAssetWindows (private field + RefreshBrowse + Rebuild).
string names = UnityEditor.EditorPrefs.GetString("T326.wins", "");
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
var sb = new System.Text.StringBuilder();
foreach (var n in names.Split('|'))
{
    if (string.IsNullOrEmpty(n)) continue;
    var w = ZWin(n); if (w == null) { sb.Append(n).Append(": NOT OPEN\n"); continue; }
    System.Reflection.FieldInfo bf = null; System.Reflection.MethodInfo rf = null, rb = null;
    for (var t = w.GetType(); t != null; t = t.BaseType)
    {
        if (bf == null) bf = t.GetField("browsing", BFi|System.Reflection.BindingFlags.DeclaredOnly);
        if (rf == null) rf = t.GetMethod("RefreshBrowse", BFi|System.Reflection.BindingFlags.DeclaredOnly);
        if (rb == null) rb = t.GetMethod("Rebuild", BFi|System.Reflection.BindingFlags.DeclaredOnly);
    }
    if (bf == null) { sb.Append(n).Append(": no browsing field\n"); continue; }
    bf.SetValue(w, true);
    if (rf != null) rf.Invoke(w, null);
    if (rb != null) rb.Invoke(w, null);
    w.Repaint();
    sb.Append(n).Append(": browsing=true\n");
}
return sb.ToString();
