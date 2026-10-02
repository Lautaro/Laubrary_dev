var sb = new System.Text.StringBuilder();
string[] paths = {
 "Assets/Shaper/AuditT336W1.asset","Assets/Shaper/AuditT336W2.asset",
 "Assets/Shaper/AuditT336W3.asset","Assets/Shaper/AuditT336W3.png","Assets/Shaper/AuditT336W3.anim","Assets/Shaper/AuditT336W3 Clip.asset",
 "Assets/Shaper/AuditT336W4.asset","Assets/Shaper/AuditT336W4.png","Assets/Shaper/AuditT336W4.anim","Assets/Shaper/AuditT336W4 Clip.asset",
 "Assets/Shaper/AuditT336Long/AuditT336 A Deliberately Very Long Backdrop Sprite Name For Measuring Clipping.png",
 "Assets/Shaper/Audit0277/rfield0277.asset",
 "Assets/Shaper/AuditT336Long","Assets/Shaper/Audit0277","Assets/Shaper/AuditT336Bake",
};
int ok=0, miss=0;
foreach (var p in paths) {
  if (UnityEditor.AssetDatabase.LoadMainAssetAtPath(p) == null && !UnityEditor.AssetDatabase.IsValidFolder(p)) { miss++; sb.Append("absent  ").Append(p).Append("\n"); continue; }
  bool r = UnityEditor.AssetDatabase.DeleteAsset(p);
  if (r) ok++; sb.Append(r?"deleted ":"FAILED  ").Append(p).Append("\n");
}
UnityEditor.AssetDatabase.Refresh();
sb.Append("deleted=").Append(ok).Append(" alreadyAbsent=").Append(miss).Append("\n");
sb.Append("-- Assets/Shaper now --\n");
foreach (var g in UnityEditor.AssetDatabase.FindAssets("", new string[]{"Assets/Shaper"}))
  sb.Append("  ").Append(UnityEditor.AssetDatabase.GUIDToAssetPath(g)).Append("\n");
return sb.ToString();
