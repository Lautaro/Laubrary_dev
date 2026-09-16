using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Laubrary.AssetKit.Editor
{
    /// <summary>
    /// Generic create/read/update/delete over ScriptableObject assets of one type. Editor-only. This is the shared
    /// asset-management layer behind <see cref="LaubraryAssetWindow{T}"/>, but it's usable on its own by any window
    /// (including ones that don't adopt that base). Generalizes the hand-typed Zoetrope AnimationLibrary/ZoeRepo.
    /// </summary>
    public static class AssetLibrary<T> where T : ScriptableObject
    {
        static string Filter => "t:" + typeof(T).Name;

        public static string[] FindGuids(string folder = null)
            => string.IsNullOrEmpty(folder)
                ? AssetDatabase.FindAssets(Filter)
                : AssetDatabase.FindAssets(Filter, new[] { folder });

        /// Every T asset in the project (or under <paramref name="folder"/> if given), sorted by name. Excludes
        /// SUB-assets (e.g. a Zoe's embedded private Chunks, T-0250): when a path's main object is a different
        /// type than T, LoadAssetAtPath&lt;T&gt; falls back to the first T-typed sub-asset at that path instead
        /// of returning null (confirmed live) — without this guard, an asset meant to be embedded-and-hidden
        /// would still show up in every browser/picker built on this enumeration.
        public static List<T> Enumerate(string folder = null)
        {
            var guids = FindGuids(folder);
            var list = new List<T>(guids.Length);
            foreach (var g in guids)
            {
                var a = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(g));
                if (a != null && !AssetDatabase.IsSubAsset(a)) list.Add(a);
            }
            list.Sort((a, b) => string.Compare(a.name, b.name, System.StringComparison.OrdinalIgnoreCase));
            return list;
        }

        public static string PathOf(T asset) => asset != null ? AssetDatabase.GetAssetPath(asset) : null;

        /// Create a new T named <paramref name="name"/> in <paramref name="folder"/> (folders created as needed).
        public static T Create(string name, string folder)
        {
            if (string.IsNullOrWhiteSpace(name)) name = "New " + typeof(T).Name;
            folder = EnsureFolder(string.IsNullOrEmpty(folder) ? "Assets" : folder);
            string path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{name}.asset");
            var asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            // T-0369: SaveAssetIfDirty(asset), not SaveAssets() — the plain SaveAssets() call here saved
            // EVERY dirty asset in the project, which is how an unrelated Create rewrote two of the owner's
            // own tracked assets mid-session (T-0361/T-0366 verification incident). Scope the save to the
            // one asset this call actually created.
            AssetDatabase.SaveAssetIfDirty(asset);
            return asset;
        }

        /// Duplicate an existing asset next to itself; returns the copy (or null on failure).
        public static T Duplicate(T src)
        {
            string path = PathOf(src);
            if (string.IsNullOrEmpty(path)) return null;
            string copy = AssetDatabase.GenerateUniqueAssetPath(path);
            // AssetDatabase.CopyAsset already writes the copy to disk directly; T-0369 replaces the trailing
            // save-everything call with SaveAssetIfDirty on the loaded copy alone (a no-op today, since
            // CopyAsset leaves nothing dirty in memory, but scoped correctly if that ever changes).
            if (!AssetDatabase.CopyAsset(path, copy)) return null;
            var dup = AssetDatabase.LoadAssetAtPath<T>(copy);
            if (dup != null) AssetDatabase.SaveAssetIfDirty(dup);
            return dup;
        }

        public static bool Rename(T asset, string newName)
        {
            string path = PathOf(asset);
            newName = newName?.Trim();
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(newName)) return false;
            string err = AssetDatabase.RenameAsset(path, newName);
            if (!string.IsNullOrEmpty(err)) { Debug.LogWarning($"[AssetKit] rename failed: {err}"); return false; }
            // T-0369: RenameAsset already renames the file on disk directly; scope the save to this asset
            // alone instead of the removed SaveAssets() (see Create's note above for why that mattered).
            AssetDatabase.SaveAssetIfDirty(asset);
            return true;
        }

        /// Delete the asset from disk (caller is responsible for any confirmation prompt).
        public static bool Delete(T asset)
        {
            string path = PathOf(asset);
            return !string.IsNullOrEmpty(path) && AssetDatabase.DeleteAsset(path);
        }

        static string EnsureFolder(string folder) => AssetFolders.EnsureFolder(folder);
    }
}
