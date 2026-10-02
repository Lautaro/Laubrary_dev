// Every asset and folder this task created, deleted through AssetDatabase (which removes the .meta too).
var sb = new System.Text.StringBuilder();
string[] assets = {
  "Assets/BackSplash/AuditT328Back.asset",
  "Assets/Choreographer/AuditT328Choreo.asset",
  "Assets/Tapestry/AuditT328Tapestry.asset",
  "Assets/Lathe/Molds/AuditT328Mold.asset",
  "Assets/Zoetrope/AuditT328Weapon.asset",
  "Assets/Zoetrope/AuditT328Ammo.asset",
  "Assets/TextSplash/Border Fonts/AuditT328Splash (LiberationSans SDF) Border Font.asset",
  "Assets/TextSplash/AuditT328Splash.asset",
  "Assets/Mirage/AuditT328View.asset",
  "Assets/Shaper/Audit0277/rfield0277.asset",
};
foreach (var p in assets)
{
    if (UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(p) == null) { sb.Append("absent  ").Append(p).Append("\n"); continue; }
    sb.Append(UnityEditor.AssetDatabase.DeleteAsset(p) ? "deleted " : "FAILED  ").Append(p).Append("\n");
}
// generated siblings and stray folders
foreach (var g in UnityEditor.AssetDatabase.FindAssets("AuditT328"))
{
    string p = UnityEditor.AssetDatabase.GUIDToAssetPath(g);
    sb.Append(UnityEditor.AssetDatabase.DeleteAsset(p) ? "deleted* " : "FAILED*  ").Append(p).Append("\n");
}
string[] folders = { "Assets/Screenshots", "Assets/TextSplash/Border Fonts", "Assets/TextSplash",
                     "Assets/Shaper/Audit0277", "Assets/BackSplash", "Assets/Choreographer",
                     "Assets/Tapestry", "Assets/Lathe/Molds", "Assets/Border Fonts" };
foreach (var f in folders)
{
    if (!UnityEditor.AssetDatabase.IsValidFolder(f)) { sb.Append("no folder ").Append(f).Append("\n"); continue; }
    int n = UnityEditor.AssetDatabase.FindAssets("", new string[]{ f }).Length;
    if (n > 0) { sb.Append("KEPT (").Append(n).Append(" assets) ").Append(f).Append("\n"); continue; }
    sb.Append(UnityEditor.AssetDatabase.DeleteAsset(f) ? "rmdir   " : "RMFAIL  ").Append(f).Append("\n");
}
UnityEditor.AssetDatabase.Refresh();
return sb.ToString();
