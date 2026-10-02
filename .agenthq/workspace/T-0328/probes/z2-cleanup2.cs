// A TextSplash generates a sibling "Border Fonts/<name> (<font>) Border Font.asset" the moment it is
// authored — the same class of generated sibling as the Zoe's .states.cs round 14 recorded. Both of mine
// go, with the folders they created.
var sb = new System.Text.StringBuilder();
string[] paths = {
    "Assets/Border Fonts/AuditT328SplashA (LiberationSans SDF) Border Font.asset",
    "Assets/TextSplash/Border Fonts/AuditT328SplashB (LiberationSans SDF) Border Font.asset",
};
foreach (var p in paths)
{
    bool existed = System.IO.File.Exists(p);
    sb.Append(p).Append(" existed=").Append(existed);
    if (existed) sb.Append(" deleted=").Append(UnityEditor.AssetDatabase.DeleteAsset(p));
    sb.Append("\n");
}
foreach (var f in new string[]{ "Assets/TextSplash/Border Fonts", "Assets/TextSplash", "Assets/Border Fonts" })
{
    if (!UnityEditor.AssetDatabase.IsValidFolder(f)) { sb.Append(f).Append(": absent\n"); continue; }
    int n = System.IO.Directory.GetFileSystemEntries(f).Length;
    sb.Append(f).Append(" entries=").Append(n);
    if (n == 0) sb.Append(" deleted=").Append(UnityEditor.AssetDatabase.DeleteAsset(f));
    sb.Append("\n");
}
UnityEditor.AssetDatabase.Refresh();
return sb.ToString();
