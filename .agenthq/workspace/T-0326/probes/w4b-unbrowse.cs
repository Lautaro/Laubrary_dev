// Put the in-window library browser back to closed on every window this session forced it open on.
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
var sb = new System.Text.StringBuilder();
foreach (var w in Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>())
{
    System.Reflection.FieldInfo bf = null; System.Reflection.MethodInfo rb = null;
    for (var t = w.GetType(); t != null; t = t.BaseType)
    {
        if (bf == null) bf = t.GetField("browsing", BFi|System.Reflection.BindingFlags.DeclaredOnly);
        if (rb == null) rb = t.GetMethod("Rebuild", BFi|System.Reflection.BindingFlags.DeclaredOnly);
    }
    if (bf == null) continue;
    if (!(bool)bf.GetValue(w)) continue;
    bf.SetValue(w, false);
    if (rb != null) rb.Invoke(w, null);
    w.Repaint();
    sb.Append(w.GetType().Name).Append(": browsing=false\n");
}
return sb.Length == 0 ? "none were browsing" : sb.ToString();
