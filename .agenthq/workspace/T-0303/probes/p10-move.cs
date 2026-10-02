var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");
if (!UnityEditor.AssetDatabase.IsValidFolder("Assets/Pyre/AuditA25"))
    sb.Append("created folder guid=").Append(UnityEditor.AssetDatabase.CreateFolder("Assets/Pyre", "AuditA25")).Append("\n");
var err = UnityEditor.AssetDatabase.MoveAsset("Assets/Pyre/AuditA25Pyre.asset", "Assets/Pyre/AuditA25/AuditA25Pyre.asset");
sb.Append("move err='").Append(err).Append("'\n");
UnityEditor.AssetDatabase.SaveAssets();
UnityEditor.AssetDatabase.Refresh();
foreach (var p in System.IO.Directory.GetFiles(UnityEngine.Application.dataPath + "/Pyre/AuditA25")) sb.Append("  ").Append(System.IO.Path.GetFileName(p)).Append("\n");
return sb.ToString();
