// T-0328 cleanup, part 1: every asset and folder this task created, deleted through AssetDatabase
// (which removes the .meta with it), plus the generated .states.cs sibling a Zoe copy leaves behind.
var sb = new System.Text.StringBuilder();
string[] paths = {
    "Assets/Mirage/AuditT328View.asset",
    "Assets/Pyre/AuditT328Pyre.asset",
    "Assets/Zoetrope/AuditT328Zoe.asset",
    "Assets/Zoetrope/AuditT328Zoe.states.cs",
    "Assets/Chunks/AuditT328ChunkA.asset",
    "Assets/Chunks/AuditT328ChunkB.asset",
    "Assets/AuditT328SplashA.asset",
    "Assets/TextSplash/AuditT328SplashB.asset",
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
