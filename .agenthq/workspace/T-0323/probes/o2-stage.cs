var sb=new System.Text.StringBuilder();
foreach (var g in UnityEditor.AssetDatabase.FindAssets("MirageStage t:Scene")) sb.Append("stage: ").Append(UnityEditor.AssetDatabase.GUIDToAssetPath(g)).Append("\n");
foreach (var g in UnityEditor.AssetDatabase.FindAssets("t:Scene")) { var p=UnityEditor.AssetDatabase.GUIDToAssetPath(g); if (p.Contains("Mirage")) sb.Append("scene: ").Append(p).Append("\n"); }
return sb.ToString();
