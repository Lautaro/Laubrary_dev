using UnityEditor;

namespace Laubrary.AssetKit.Editor
{
    /// Shared "make sure this nested Assets/A/B/C folder path exists" helper — was duplicated inline by
    /// AssetLibrary&lt;T&gt; and by every ad-hoc "New (Aseprite)"-style creator across the codebase.
    public static class AssetFolders
    {
        /// Create every missing folder along an "Assets/A/B/C" path; returns the deepest valid folder.
        public static string EnsureFolder(string folder)
        {
            folder = folder.Replace('\\', '/').TrimEnd('/');
            if (AssetDatabase.IsValidFolder(folder)) return folder;
            var parts = folder.Split('/');
            if (parts.Length == 0 || parts[0] != "Assets") return "Assets";
            string cur = "Assets";
            for (int i = 1; i < parts.Length; i++)
            {
                string next = cur + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(cur, parts[i]);
                cur = next;
            }
            return cur;
        }
    }
}
