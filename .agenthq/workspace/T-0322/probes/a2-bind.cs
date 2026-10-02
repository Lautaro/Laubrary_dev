var sb=new System.Text.StringBuilder();
var BF2 = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
var w = ZWin("PyreWindow");
var af = w.GetType().GetField("asset", BF2);
sb.Append("assetFieldType=").Append(af.FieldType.FullName).Append("\n");
foreach (var g in UnityEditor.AssetDatabase.FindAssets("t:"+af.FieldType.Name)) sb.Append(UnityEditor.AssetDatabase.GUIDToAssetPath(g)).Append("\n");
return sb.ToString();
