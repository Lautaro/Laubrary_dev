using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Laubrary.Launimator;
using UnityEditor;
using UnityEngine;

namespace Laubrary.Launimator.Editor
{
    /// <summary>
    /// Storage for "orphaned" animations — standalone <see cref="AnimationAsset"/>s under
    /// <c>Assets/Launimator/Animations/</c> that don't yet belong to any lauminary. The Laumination Builder
    /// (opened standalone) saves here; the Lauminary Browser lists them and can INCLUDE one into a lauminary's
    /// draft (copying its recipe). Orphans store only the editable recipe — they are not baked.
    /// </summary>
    public static class AnimationLibrary
    {
        public const string Folder = "Assets/Launimator/Animations";

        public static List<AnimationAsset> Enumerate()
        {
            var list = new List<AnimationAsset>();
            if (!AssetDatabase.IsValidFolder(Folder)) return list;
            foreach (var guid in AssetDatabase.FindAssets("t:AnimationAsset", new[] { Folder }))
            {
                var a = AssetDatabase.LoadAssetAtPath<AnimationAsset>(AssetDatabase.GUIDToAssetPath(guid));
                if (a != null) list.Add(a);
            }
            return list.OrderBy(a => a.animation.name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>Create a new, empty orphaned animation with the given name (unique file).</summary>
        public static AnimationAsset Create(string name)
        {
            LauminaryBuilder.EnsureFolder(Folder);
            var a = ScriptableObject.CreateInstance<AnimationAsset>();
            a.animation = new Laumination { name = string.IsNullOrWhiteSpace(name) ? "Animation" : name.Trim() };
            string path = AssetDatabase.GenerateUniqueAssetPath($"{Folder}/{LauminaryBuilder.Sanitize(a.animation.name)}.asset");
            AssetDatabase.CreateAsset(a, path);
            AssetDatabase.SaveAssets();
            return a;
        }

        /// <summary>Write <paramref name="def"/>'s recipe into <paramref name="existing"/> (renaming its file to
        /// match), or create a new orphan if none is given. Returns the asset.</summary>
        public static AnimationAsset Save(Laumination def, AnimationAsset existing)
        {
            LauminaryBuilder.EnsureFolder(Folder);
            var copy = LauminaryRepo.CopyAnimation(def);

            if (existing != null)
            {
                existing.animation = copy;
                EditorUtility.SetDirty(existing);
                string path = AssetDatabase.GetAssetPath(existing);
                string safe = LauminaryBuilder.Sanitize(def.name);
                if (Path.GetFileNameWithoutExtension(path) != safe)
                    AssetDatabase.RenameAsset(path, safe);
                AssetDatabase.SaveAssets();
                return existing;
            }

            var a = ScriptableObject.CreateInstance<AnimationAsset>();
            a.animation = copy;
            string newPath = AssetDatabase.GenerateUniqueAssetPath($"{Folder}/{LauminaryBuilder.Sanitize(def.name)}.asset");
            AssetDatabase.CreateAsset(a, newPath);
            AssetDatabase.SaveAssets();
            return a;
        }

        /// <summary>Rename an orphaned animation: updates its <see cref="Laumination.name"/> and renames the
        /// asset file to match. No-op on a blank name.</summary>
        public static void Rename(AnimationAsset a, string newName)
        {
            if (a == null || a.animation == null || string.IsNullOrWhiteSpace(newName)) return;
            a.animation.name = newName.Trim();
            EditorUtility.SetDirty(a);
            string path = AssetDatabase.GetAssetPath(a);
            string safe = LauminaryBuilder.Sanitize(a.animation.name);
            if (!string.IsNullOrEmpty(path) && Path.GetFileNameWithoutExtension(path) != safe)
                AssetDatabase.RenameAsset(path, safe);
            AssetDatabase.SaveAssets();
        }

        public static void Delete(AnimationAsset a)
        {
            if (a != null) AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(a));
            AssetDatabase.SaveAssets();
        }
    }
}
