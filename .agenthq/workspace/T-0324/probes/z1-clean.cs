var sb = new System.Text.StringBuilder();
string[] paths = {
  "Assets/Shaper/AuditT324Pyre.asset", "Assets/Shaper/AuditT324Lathe.asset",
  "Assets/Shaper/AuditT324Fx.asset", "Assets/Shaper/AuditT324Splash.asset",
  "Assets/Larder/AuditT324Ware.asset", "Assets/Larder/AuditT324Ware2.asset",
  "Assets/Mirage/AuditT324Mirage.asset",
  "Assets/Zoetrope/AuditT324Zoe.asset", "Assets/Zoetrope/AuditT324Zoe2.asset",
  "Assets/Zoetrope/AuditT324Zoe.states.cs", "Assets/Zoetrope/AuditT324Zoe2.states.cs",
  "Assets/Shaper/Audit0277/rfield0277.asset", "Assets/Shaper/Audit0277",
};
foreach (var p in paths)
{
    if (UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(p) == null
        && !UnityEditor.AssetDatabase.IsValidFolder(p)) { sb.AppendLine("absent  " + p); continue; }
    sb.AppendLine((UnityEditor.AssetDatabase.DeleteAsset(p) ? "deleted " : "FAILED  ") + p);
}
// anything else this task left behind?
foreach (var g in UnityEditor.AssetDatabase.FindAssets("AuditT324")) sb.AppendLine("STILL THERE: " + UnityEditor.AssetDatabase.GUIDToAssetPath(g));
foreach (var g in UnityEditor.AssetDatabase.FindAssets("Audit0277")) sb.AppendLine("STILL THERE: " + UnityEditor.AssetDatabase.GUIDToAssetPath(g));
UnityEditor.AssetDatabase.Refresh();
return sb.ToString();
