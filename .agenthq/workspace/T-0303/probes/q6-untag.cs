var sb = new System.Text.StringBuilder();
System.Func<string,string> fix = null;
foreach (var w in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>())
{
    if (w == null) continue;
    var n = w.GetType().Name;
    if (n == "ChunkWindow" && w.titleContent.text == "AnyCapTag") { w.titleContent = new UnityEngine.GUIContent("Chunks"); sb.Append("Chunks title restored\n"); }
    if (n == "LauminationBuilderWindow" && w.titleContent.text == "AnyCapTag") { w.titleContent = new UnityEngine.GUIContent("Laumination Builder"); sb.Append("Laumination Builder title restored\n"); }
    if (n == "ShaperWindow" && w.titleContent.text == "ShaperCapTag") { w.titleContent = new UnityEngine.GUIContent("Shaper"); sb.Append("Shaper title restored\n"); }
}
return sb.ToString();
