// Delete everything this task created, with its .meta (AssetDatabase.DeleteAsset removes both).
var sb = new System.Text.StringBuilder();
var keep = new System.Collections.Generic.HashSet<string>(new string[]{
  "Assets/Shaper/New Shaper.asset", "Assets/Shaper/New Shaper 1.asset" });
var paths = new System.Collections.Generic.List<string>();
foreach (var g in UnityEditor.AssetDatabase.FindAssets("", new string[]{"Assets/Shaper"})) {
  var p = UnityEditor.AssetDatabase.GUIDToAssetPath(g);
  if (keep.Contains(p)) continue;
  if (UnityEditor.AssetDatabase.IsValidFolder(p)) continue;   // folders last
  paths.Add(p); }
int n = 0;
foreach (var p in paths) if (UnityEditor.AssetDatabase.DeleteAsset(p)) { n++; sb.Append("deleted ").Append(p).Append("\n"); }
foreach (var f in new string[]{ "Assets/Shaper/Audit0277", "Assets/Shaper/AuditBake", "Assets/Shaper/AuditT334Long" })
  if (UnityEditor.AssetDatabase.IsValidFolder(f) && UnityEditor.AssetDatabase.DeleteAsset(f)) { n++; sb.Append("deleted folder ").Append(f).Append("\n"); }
UnityEditor.AssetDatabase.Refresh();
sb.Append("deleted=").Append(n).Append("\n-- what remains under Assets/Shaper --\n");
foreach (var g in UnityEditor.AssetDatabase.FindAssets("", new string[]{"Assets/Shaper"}))
  sb.Append("  ").Append(UnityEditor.AssetDatabase.GUIDToAssetPath(g)).Append("\n");
return sb.ToString();
