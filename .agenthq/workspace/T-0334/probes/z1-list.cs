var sb = new System.Text.StringBuilder();
foreach (var g in UnityEditor.AssetDatabase.FindAssets("", new string[]{"Assets/Shaper"})) {
  var p = UnityEditor.AssetDatabase.GUIDToAssetPath(g);
  sb.Append(p).Append("\n"); }
sb.Append("-- Pyre folder --\n");
foreach (var g in UnityEditor.AssetDatabase.FindAssets("AuditT334")) sb.Append("  ").Append(UnityEditor.AssetDatabase.GUIDToAssetPath(g)).Append("\n");
return sb.ToString();
