var sb=new System.Text.StringBuilder();
sb.Append(UnityEditor.AssetDatabase.MoveAsset("Assets/Demos/ShaperDemo/AuditT321Doc.asset","Assets/Shaper/AuditT321Doc.asset")).Append("|");
UnityEditor.AssetDatabase.Refresh();
sb.Append("demoFolder: ");
foreach (var g in UnityEditor.AssetDatabase.FindAssets("", new string[]{"Assets/Demos/ShaperDemo"})) sb.Append(UnityEditor.AssetDatabase.GUIDToAssetPath(g)).Append(" | ");
return sb.ToString();
