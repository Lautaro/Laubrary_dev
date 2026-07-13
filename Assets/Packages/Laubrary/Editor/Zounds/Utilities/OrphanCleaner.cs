#if ADDRESSABLES_INSTALLED
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEngine;

namespace Laubrary.Zounds {

    /// <summary>
    /// Editor utility that finds and removes orphaned audio files in WorkFiles and ZoundFiles.
    /// An orphan is any file whose GUID is not referenced by any Klip or Zequence in the library.
    /// </summary>
    public static class OrphanCleaner {

        /// <summary>
        /// Returns a list of asset paths in WorkFiles and ZoundFiles that are not referenced
        /// by any Klip or Zequence in the current ZoundsProject library.
        /// </summary>
        public static List<string> FindOrphans() {
            var projectSettings = ZoundsProject.Instance.projectSettings;
            var zoundLibrary = ZoundsProject.Instance.zoundLibrary;

            // Collect all audio files in WorkFiles and ZoundFiles
            string[] searchFolders = new[] { projectSettings.workFolderPath, projectSettings.zoundFilesFolderPath };
            string[] allGuids = AssetDatabase.FindAssets("t:AudioClip", searchFolders);

            // Collect all referenced GUIDs from the library
            var referencedGuids = new HashSet<string>();
            zoundLibrary.ForEachZound(z => {
                if (z is Klip klip) {
                    if (klip.audioClipRef != null && !string.IsNullOrEmpty(klip.audioClipRef.AssetGUID))
                        referencedGuids.Add(klip.audioClipRef.AssetGUID);
                    if (klip.renderedClipRef != null && !string.IsNullOrEmpty(klip.renderedClipRef.AssetGUID))
                        referencedGuids.Add(klip.renderedClipRef.AssetGUID);
                    if (klip.outputClipRef != null && !string.IsNullOrEmpty(klip.outputClipRef.AssetGUID))
                        referencedGuids.Add(klip.outputClipRef.AssetGUID);
                    if (!string.IsNullOrEmpty(klip.audioClipPath))
                        referencedGuids.Add(AssetDatabase.AssetPathToGUID(klip.audioClipPath));
                    if (!string.IsNullOrEmpty(klip.renderedClipPath))
                        referencedGuids.Add(AssetDatabase.AssetPathToGUID(klip.renderedClipPath));
                    if (!string.IsNullOrEmpty(klip.outputClipPath))
                        referencedGuids.Add(AssetDatabase.AssetPathToGUID(klip.outputClipPath));
                }
                else if (z is Zequence zequence) {
                    if (zequence.renderedClipRef != null && !string.IsNullOrEmpty(zequence.renderedClipRef.AssetGUID))
                        referencedGuids.Add(zequence.renderedClipRef.AssetGUID);
                    if (!string.IsNullOrEmpty(zequence.renderedClipPath))
                        referencedGuids.Add(AssetDatabase.AssetPathToGUID(zequence.renderedClipPath));
                }
                return false;
            });

            var orphans = new List<string>();
            foreach (string guid in allGuids) {
                if (!referencedGuids.Contains(guid)) {
                    orphans.Add(AssetDatabase.GUIDToAssetPath(guid));
                }
            }

            return orphans;
        }

        /// <summary>
        /// Deletes the specified orphan files from disk and removes them from Addressables.
        /// </summary>
        public static void DeleteOrphans(List<string> orphanPaths) {
            if (orphanPaths == null || orphanPaths.Count == 0) return;

            foreach (string path in orphanPaths) {
                AssetDatabase.DeleteAsset(path);
                Debug.Log($"[Zounds] Deleted orphaned file: {path}");
            }

            AssetDatabase.Refresh();
        }
    }
}
#endif
