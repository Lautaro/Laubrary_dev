var sb=new System.Text.StringBuilder();
string[] paths = {
 "Assets/Shaper/AuditT322Pyre.asset","Assets/Shaper/AuditT322Doc.asset","Assets/Shaper/AuditT322Ware.asset",
 "Assets/Shaper/AuditT322Lathe.asset","Assets/Shaper/AuditT322Mold.asset","Assets/Shaper/AuditT322Fx.asset",
 "Assets/Shaper/AuditT322Splash.asset","Assets/Shaper/AuditT322Zoe.asset","Assets/Shaper/AuditT322Mirage.asset",
 "Assets/Cartographer/Levels/AuditT322Level.asset","Assets/Shaper/Audit0277/rfield0277.asset","Assets/Shaper/Audit0277",
 "Assets/Shaper/ShaperViews.asset" };
foreach (var p in paths) if (UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(p)!=null || UnityEditor.AssetDatabase.IsValidFolder(p))
  sb.Append(p).Append("=").Append(UnityEditor.AssetDatabase.DeleteAsset(p)).Append(" ");
UnityEditor.AssetDatabase.Refresh();
sb.Append("\nremaining in Assets/Shaper: ");
foreach (var g in UnityEditor.AssetDatabase.FindAssets("", new string[]{"Assets/Shaper"})) sb.Append(UnityEditor.AssetDatabase.GUIDToAssetPath(g)).Append(" | ");
sb.Append("\nremaining in Assets/Cartographer/Levels: ");
if (UnityEditor.AssetDatabase.IsValidFolder("Assets/Cartographer/Levels"))
  foreach (var g in UnityEditor.AssetDatabase.FindAssets("", new string[]{"Assets/Cartographer/Levels"})) sb.Append(UnityEditor.AssetDatabase.GUIDToAssetPath(g)).Append(" | ");
return sb.ToString();
