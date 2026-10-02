var sb = new System.Text.StringBuilder();
var ws = Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>();
sb.AppendLine("windows=" + ws.Length);
foreach (var w in ws) sb.AppendLine("  " + w.GetType().FullName + "  title='" + w.titleContent.text + "' pos=" + w.position + " hasFocus=" + w.hasFocus);
return sb.ToString();
