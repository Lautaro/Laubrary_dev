var sb = new System.Text.StringBuilder();
foreach (var w in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>())
{
    if (w == null) continue;
    var n = w.GetType().Name;
    if (n != "ChunkWindow" && n != "PyreWindow" && n != "ShaperWindow" && n != "LauminationBuilderWindow") continue;
    var p = w.GetType().GetProperty("docked", System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public);
    sb.Append(n).Append(" title='").Append(w.titleContent.text).Append("' docked=").Append(p == null ? "?" : p.GetValue(w).ToString()).Append(" pos=").Append(w.position.ToString()).Append("\n");
}
return sb.ToString();
