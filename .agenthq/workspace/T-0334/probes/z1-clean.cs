var sb = new System.Text.StringBuilder();
string[] paths = {
  "Assets/Shaper/AuditT334Pyre.asset", "Assets/Shaper/AuditT334Lathe.asset",
  "Assets/Shaper/AuditT334Fx.asset", "Assets/Shaper/AuditT334Splash.asset",
  "Assets/Larder/AuditT334Ware.asset", "Assets/Larder/AuditT334Ware2.asset",
  "Assets/Mirage/AuditT334Mirage.asset",
  "Assets/Zoetrope/AuditT334Zoe.asset", "Assets/Zoetrope/AuditT334Zoe2.asset",
  "Assets/Zoetrope/AuditT334Zoe.states.cs", "Assets/Zoetrope/AuditT334Zoe2.states.cs",
  "Assets/Shaper/Audit0277/rfield0277.asset", "Assets/Shaper/Audit0277",
};
foreach (var p in paths)
{
    if (UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(p) == null
        && !UnityEditor.AssetDatabase.IsValidFolder(p)) { sb.AppendLine("absent  " + p); continue; }
    sb.AppendLine((UnityEditor.AssetDatabase.DeleteAsset(p) ? "deleted " : "FAILED  ") + p);
}
// anything else this task left behind?
foreach (var g in UnityEditor.AssetDatabase.FindAssets("AuditT334")) sb.AppendLine("STILL THERE: " + UnityEditor.AssetDatabase.GUIDToAssetPath(g));
foreach (var g in UnityEditor.AssetDatabase.FindAssets("Audit0277")) sb.AppendLine("STILL THERE: " + UnityEditor.AssetDatabase.GUIDToAssetPath(g));
UnityEditor.AssetDatabase.Refresh();
return sb.ToString();
