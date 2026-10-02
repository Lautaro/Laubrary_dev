// Rebuild a set of windows from scratch, discarding any inline style a probe forced on them.
string names = UnityEditor.EditorPrefs.GetString("T326.wins", "");
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
var sb = new System.Text.StringBuilder();
foreach (var n in names.Split('|'))
{
    if (string.IsNullOrEmpty(n)) continue;
    var w = ZWin(n); if (w == null) { sb.Append(n).Append(": NOT OPEN\n"); continue; }
    var rb = w.GetType().GetMethod("Rebuild", BFi);
    if (rb != null) rb.Invoke(w, null);
    w.Repaint();
    int hidden = 0;
    foreach (var e in ZAll(w.rootVisualElement)) if (e.resolvedStyle.display == UnityEngine.UIElements.DisplayStyle.None) hidden++;
    sb.Append(n).Append(": rebuilt, hidden subtrees back to ").Append(hidden).Append("\n");
}
return sb.ToString();
