// Reimport every ZUI stylesheet and read the console for style-parse warnings afterwards.
var lt = System.Type.GetType("UnityEditor.LogEntries,UnityEditor");
lt.GetMethod("Clear", System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Public).Invoke(null, null);
var guids = UnityEditor.AssetDatabase.FindAssets("t:StyleSheet");
int n = 0; var sb = new System.Text.StringBuilder();
foreach (var g in guids) {
  var p = UnityEditor.AssetDatabase.GUIDToAssetPath(g);
  if (!p.Contains("Zui") && !p.Contains("ZUI")) continue;
  UnityEditor.AssetDatabase.ImportAsset(p, UnityEditor.ImportAssetOptions.ForceUpdate);
  sb.AppendLine("reimported " + p); n++;
}
UnityEditor.AssetDatabase.Refresh();
return n + " stylesheets\n" + sb;
