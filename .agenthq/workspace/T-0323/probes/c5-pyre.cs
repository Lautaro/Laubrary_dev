var sb=new System.Text.StringBuilder();
var w=ZWin("PyreWindow"); if (w==null) return "no pyre";
// bind to Green Lantern? no - use a scratch copy. Find a PyreForm asset
var gs = UnityEditor.AssetDatabase.FindAssets("t:ScriptableObject", new string[]{"Assets/Pyre"});
foreach (var g in gs) sb.Append("pyre asset: ").Append(UnityEditor.AssetDatabase.GUIDToAssetPath(g)).Append("\n");
return sb.ToString();
