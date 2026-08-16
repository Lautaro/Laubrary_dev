using System.Linq;
using UnityEditor;
using UnityEngine;
using Laubrary.AssetKit.Editor;
using Laubrary.Launimator;
using Laubrary.Launimator.Editor;

namespace Laubrary.ZoetropeLaunimator.Editor
{
    /// <summary>
    /// "Which Lauminary owns this version, and open it for editing." A <see cref="LauminaryVersion"/> is a
    /// standalone asset with no back-reference to its Lauminary, so the owner is resolved by folder convention
    /// (every version lives under its Lauminary's own folder — see <see cref="LauminaryRepo"/>).
    ///
    /// <para>Registered with <see cref="LauAssetEditors"/> as the editor for a LauminaryVersion, which is what
    /// lets ANY window offer "author lauminations" for a version it holds without referencing the Launimator
    /// editor assembly — the Mirage sequence's "None declared" state needs exactly that, and per the standing
    /// rule an empty option list must offer the way to declare one rather than degrade to a text field.</para>
    /// </summary>
    [InitializeOnLoad]
    public static class LauminaryOwner
    {
        static LauminaryOwner() => LauAssetEditors.RegisterOpen<LauminaryVersion>(v => OpenBuilder(v, null));

        /// The Lauminary whose folder contains <paramref name="version"/>, or null.
        public static Lauminary Find(LauminaryVersion version)
        {
            if (version == null) return null;
            string versionPath = AssetDatabase.GetAssetPath(version);
            if (string.IsNullOrEmpty(versionPath)) return null;
            foreach (var lauminary in LauminaryRepo.EnumerateLauminaries())
            {
                string folder = LauminaryRepo.FolderOf(lauminary);
                if (!string.IsNullOrEmpty(folder) &&
                    versionPath.StartsWith(folder + "/", System.StringComparison.OrdinalIgnoreCase))
                    return lauminary;
            }
            return null;
        }

        /// Open the Laumination Builder on <paramref name="animName"/> (or the version's first laumination, or
        /// a blank draft when it has none — which is the case a "declare one" affordance exists for).
        public static void OpenBuilder(LauminaryVersion version, string animName)
        {
            var lauminary = Find(version);
            if (lauminary == null)
            {
                Debug.LogWarning($"[Launimator] Couldn't find the Lauminary owning " +
                                 $"'{AssetDatabase.GetAssetPath(version)}' (expected it under {LauminaryRepo.Root}).");
                return;
            }
            if (string.IsNullOrEmpty(animName) && version != null && version.animations != null)
                animName = version.animations.FirstOrDefault(a => a != null && !string.IsNullOrEmpty(a.name))?.name;
            LauminationBuilderWindow.OpenForEdit(lauminary, animName ?? "");
        }
    }
}
