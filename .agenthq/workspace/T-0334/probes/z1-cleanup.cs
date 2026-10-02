// T-0334 cleanup, part 1: every asset and folder this task created, deleted through AssetDatabase
// (which removes the .meta with it), plus the generated .states.cs sibling a Zoe copy leaves behind.
var sb = new System.Text.StringBuilder();
string[] paths = {
    "Assets/Mirage/AuditT334View.asset",
    "Assets/Pyre/AuditT334Pyre.asset",
    "Assets/Zoetrope/AuditT334Zoe.asset",
    "Assets/Zoetrope/AuditT334Zoe.states.cs",
    "Assets/Chunks/AuditT334ChunkA.asset",
    "Assets/Chunks/AuditT334ChunkB.asset",
    "Assets/AuditT334SplashA.asset",
    "Assets/TextSplash/AuditT334SplashB.asset",
    "Assets/Shaper/Audit0277/rfield0277.asset",
};
foreach (var p in paths)
{
    bool existed = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(p) != null
                   || System.IO.File.Exists(p);
    sb.Append(p).Append(" existed=").Append(existed);
    if (existed) sb.Append(" deleted=").Append(UnityEditor.AssetDatabase.DeleteAsset(p));
    sb.Append("\n");
}
// folders this task created (only if empty)
foreach (var f in new string[]{ "Assets/Shaper/Audit0277", "Assets/TextSplash" })
{
    if (!UnityEditor.AssetDatabase.IsValidFolder(f)) { sb.Append(f).Append(": absent\n"); continue; }
    var left = UnityEditor.AssetDatabase.FindAssets("", new string[]{ f });
    sb.Append(f).Append(" holds ").Append(left.Length);
    if (left.Length == 0) sb.Append(" deleted=").Append(UnityEditor.AssetDatabase.DeleteAsset(f));
    sb.Append("\n");
}
UnityEditor.AssetDatabase.Refresh();
sb.Append("Assets/Shaper now: ");
foreach (var g in UnityEditor.AssetDatabase.FindAssets("", new string[]{ "Assets/Shaper" }))
    sb.Append(UnityEditor.AssetDatabase.GUIDToAssetPath(g)).Append(" | ");
return sb.ToString();
