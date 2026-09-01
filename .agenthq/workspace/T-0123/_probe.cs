var wins = Resources.FindObjectsOfTypeAll<EditorWindow>();
var sb = new System.Text.StringBuilder();
sb.AppendLine("ppp=" + EditorGUIUtility.pixelsPerPoint);
foreach (var w in wins)
{
    if (w.GetType().Name != "ChunksMockWindow") continue;
    sb.AppendLine("FOUND " + w.GetType().FullName + " pos=" + w.position + " docked=" + w.docked);
}
sb.AppendLine("count=" + wins.Length);
return sb.ToString();
