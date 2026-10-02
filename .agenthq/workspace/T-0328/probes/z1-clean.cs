var sb = new System.Text.StringBuilder();
string[] paths = {
  "Assets/Shaper/AuditT328Pyre.asset", "Assets/Shaper/AuditT328Lathe.asset",
  "Assets/Shaper/AuditT328Fx.asset", "Assets/Shaper/AuditT328Splash.asset",
  "Assets/Larder/AuditT328Ware.asset", "Assets/Larder/AuditT328Ware2.asset",
  "Assets/Mirage/AuditT328Mirage.asset",
  "Assets/Zoetrope/AuditT328Zoe.asset", "Assets/Zoetrope/AuditT328Zoe2.asset",
  "Assets/Zoetrope/AuditT328Zoe.states.cs", "Assets/Zoetrope/AuditT328Zoe2.states.cs",
  "Assets/Shaper/Audit0277/rfield0277.asset", "Assets/Shaper/Audit0277",
};
foreach (var p in paths)
{
    if (UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(p) == null
        && !UnityEditor.AssetDatabase.IsValidFolder(p)) { sb.AppendLine("absent  " + p); continue; }
    sb.AppendLine((UnityEditor.AssetDatabase.DeleteAsset(p) ? "deleted " : "FAILED  ") + p);
}
// anything else this task left behind?
foreach (var g in UnityEditor.AssetDatabase.FindAssets("AuditT328")) sb.AppendLine("STILL THERE: " + UnityEditor.AssetDatabase.GUIDToAssetPath(g));
foreach (var g in UnityEditor.AssetDatabase.FindAssets("Audit0277")) sb.AppendLine("STILL THERE: " + UnityEditor.AssetDatabase.GUIDToAssetPath(g));
UnityEditor.AssetDatabase.Refresh();
return sb.ToString();
