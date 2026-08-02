using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Laubrary.AssetKit.Editor
{
    /// Where a sprite sheet CAME FROM, and how a tool sliced it — persisted as a sidecar JSON next to the
    /// texture (`<sheet>.png.source.json`), the same pattern Launimator's region sidecar established.
    ///
    /// The load-bearing field is the CONTENT HASH: identity follows the pixels, not the name — a source
    /// file renamed, moved, or re-downloaded from a changed URL is still recognised as the same sheet, so
    /// nothing is imported twice and settings never need reconfiguring. The `origin` string is lineage for
    /// humans (and for re-fetching); the hash is lineage for machines.
    [Serializable]
    public class SheetSource
    {
        public int version = 1;
        public string origin;             // absolute path or URL the pixels came from
        public string originalFileName;
        public string md5;                // hash of the file CONTENT — identity that survives renames
        public string importedUtc;

        // Tileset Builder slicing settings. Other tools keep their own sidecars for tool state; origin +
        // md5 above are the shared part every sheet-consuming tool benefits from.
        public bool hasTilesetSettings;
        public int tileSize = 16;
        public int spacingX, spacingY, originX, originY;
    }

    public static class SheetProvenance
    {
        public static string PathFor(string assetPath) => assetPath + ".source.json";

        public static SheetSource Load(Texture2D tex)
        {
            if (tex == null) return null;
            return Load(AssetDatabase.GetAssetPath(tex));
        }

        public static SheetSource Load(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return null;
            string p = PathFor(assetPath);
            if (!File.Exists(p)) return null;
            try { return JsonUtility.FromJson<SheetSource>(File.ReadAllText(p)); }
            catch { return null; }
        }

        public static void Save(string assetPath, SheetSource source)
        {
            if (string.IsNullOrEmpty(assetPath) || source == null) return;
            File.WriteAllText(PathFor(assetPath), JsonUtility.ToJson(source, true));
            AssetDatabase.ImportAsset(PathFor(assetPath), ImportAssetOptions.ForceSynchronousImport);
        }

        public static string Md5OfFile(string absolutePath)
        {
            try
            {
                using var md5 = System.Security.Cryptography.MD5.Create();
                using var stream = File.OpenRead(absolutePath);
                return BitConverter.ToString(md5.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
            }
            catch { return null; }
        }

        // ── memory for sheets that need NOT be project assets ──────────────────
        // A sidecar can only sit next to a file we own; a source sheet opened straight from a bought pack
        // on another drive is not ours to decorate. So settings ALSO persist in EditorPrefs keyed by the
        // content hash — the same identity rule, minus the requirement that the sheet was ever imported.

        static string SettingsKey(string md5) => "Laubrary.Sheets.Settings." + md5;
        const string RecentsKey = "Laubrary.Sheets.Recents";

        [Serializable]
        public class RecentSheet { public string path; public string md5; }

        [Serializable]
        class RecentList { public List<RecentSheet> items = new(); }

        public static SheetSource LoadByMd5(string md5)
        {
            if (string.IsNullOrEmpty(md5)) return null;
            string json = EditorPrefs.GetString(SettingsKey(md5), null);
            if (string.IsNullOrEmpty(json)) return null;
            try { return JsonUtility.FromJson<SheetSource>(json); }
            catch { return null; }
        }

        public static void SaveByMd5(string md5, SheetSource source)
        {
            if (string.IsNullOrEmpty(md5) || source == null) return;
            EditorPrefs.SetString(SettingsKey(md5), JsonUtility.ToJson(source));
        }

        /// Most-recent-first list of sheets any builder opened, project member or not.
        public static List<RecentSheet> Recents()
        {
            try { return JsonUtility.FromJson<RecentList>(EditorPrefs.GetString(RecentsKey, "{}"))?.items ?? new List<RecentSheet>(); }
            catch { return new List<RecentSheet>(); }
        }

        public static void PushRecent(string path, string md5)
        {
            if (string.IsNullOrEmpty(path)) return;
            var list = new RecentList { items = Recents() };
            list.items.RemoveAll(r => string.Equals(r.path, path, StringComparison.OrdinalIgnoreCase));
            list.items.Insert(0, new RecentSheet { path = path, md5 = md5 });
            if (list.items.Count > 12) list.items.RemoveRange(12, list.items.Count - 12);
            EditorPrefs.SetString(RecentsKey, JsonUtility.ToJson(list));
        }

        /// The already-imported sheet with this content, if any — the dedup that makes re-importing a
        /// renamed source land on the SAME asset instead of a copy.
        public static Texture2D FindByHash(string md5)
        {
            if (string.IsNullOrEmpty(md5)) return null;
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string sidecar = PathFor(path);
                if (!File.Exists(sidecar)) continue;
                try
                {
                    if (File.ReadAllText(sidecar).Contains(md5))
                        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                }
                catch { }
            }
            return null;
        }
    }
}
