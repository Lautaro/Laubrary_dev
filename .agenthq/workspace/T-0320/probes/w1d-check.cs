var win = ZWin("ShaperWindow"); if (win==null) return "no shaper";
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.FieldInfo assetF = null;
for (var t = win.GetType(); t != null && assetF == null; t = t.BaseType) assetF = t.GetField("asset", BFi);
var a = assetF.GetValue(win) as UnityEngine.Object;
var sb = new System.Text.StringBuilder();
sb.AppendLine("bound=" + (a != null ? a.name : "null") + " path=" + (a != null ? UnityEditor.AssetDatabase.GetAssetPath(a) : "-"));
foreach (var g in UnityEditor.AssetDatabase.FindAssets("t:ShaperDocument")) sb.AppendLine("  doc " + UnityEditor.AssetDatabase.GUIDToAssetPath(g));
return sb.ToString();
