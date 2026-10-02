var sb = new System.Text.StringBuilder();
foreach (var g in UnityEditor.AssetDatabase.FindAssets("AuditT328"))
  sb.Append("  ").Append(UnityEditor.AssetDatabase.GUIDToAssetPath(g)).Append("\n");
foreach (var g in UnityEditor.AssetDatabase.FindAssets("rfield0277"))
  sb.Append("  ").Append(UnityEditor.AssetDatabase.GUIDToAssetPath(g)).Append("\n");
sb.Append("-- folders --\n");
foreach (var d in new string[]{ "Assets/Cartographer/Tilesets", "Assets/Cartographer/Props", "Assets/Shaper/Audit0277", "Assets/Shaper" })
  sb.Append("  ").Append(d).Append(" exists=").Append(System.IO.Directory.Exists(d))
    .Append(System.IO.Directory.Exists(d) ? " entries=" + System.IO.Directory.GetFileSystemEntries(d).Length : "").Append("\n");
return sb.ToString();
