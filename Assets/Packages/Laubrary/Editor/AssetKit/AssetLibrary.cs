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

        /// Every T asset in the project (or under <paramref name="folder"/> if given), sorted by name.
        public static List<T> Enumerate(string folder = null)
        {
            var guids = FindGuids(folder);
            var list = new List<T>(guids.Length);
            foreach (var g in guids)
            {
                var a = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(g));
                if (a != null) list.Add(a);
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
            // T-0276 — flush THIS asset, never the project. AssetDatabase.SaveAssets() writes every dirty
            // asset there is, so pressing New in any AssetKit window also published whatever unsaved edits
            // some other window was holding. That is not a theory: during T-0265 the Shaper demo document
            // was written to disk by a task that never edited it, and this is the shape of call that does it.
            AssetDatabase.SaveAssetIfDirty(asset);
            return asset;
        }

        /// Duplicate an existing asset next to itself; returns the copy (or null on failure).
        public static T Duplicate(T src)
        {
            string path = PathOf(src);
            if (string.IsNullOrEmpty(path)) return null;
            string copy = AssetDatabase.GenerateUniqueAssetPath(path);
            // T-0285 — AssetDatabase.CopyAsset flushes EVERY dirty asset in the project as a Unity-internal
            // side effect (measured by T-0282), independent of any SaveAssets/SaveAssetIfDirty call nearby.
            // Object.Instantiate deep-copies the ScriptableObject (including its [SerializeReference] graph)
            // in memory instead, so CreateAsset below only ever touches the one asset being made.
            var made = Object.Instantiate(src);
            made.name = System.IO.Path.GetFileNameWithoutExtension(copy);
            AssetDatabase.CreateAsset(made, copy);
            AssetDatabase.SaveAssetIfDirty(made);   // T-0276 — this asset, not the project. See Create above.
            return made;
        }

        public static bool Rename(T asset, string newName)
        {
            string path = PathOf(asset);
            newName = newName?.Trim();
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(newName)) return false;
            string err = AssetDatabase.RenameAsset(path, newName);
            if (!string.IsNullOrEmpty(err)) { Debug.LogWarning($"[AssetKit] rename failed: {err}"); return false; }
            AssetDatabase.SaveAssetIfDirty(asset);   // T-0276 — this asset, not the project. See Create above.
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
