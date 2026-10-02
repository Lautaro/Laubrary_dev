var pyreT = ZType("PyreWindow"); if (pyreT==null) return "no PyreWindow type";
var guids = UnityEditor.AssetDatabase.FindAssets("t:Pyre");
var sb = new System.Text.StringBuilder();
foreach (var g in guids) sb.AppendLine(UnityEditor.AssetDatabase.GUIDToAssetPath(g));
return sb.ToString();
