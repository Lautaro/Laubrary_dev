using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using System.Linq;
using System;


#if ADDRESSABLES_INSTALLED
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine.AddressableAssets;
#endif

namespace Laubrary.Zounds {

    public static class AudioAssetUtility {

#if ADDRESSABLES_INSTALLED
        public static List<AssetReferenceT<AudioClip>> FindAllAudioReferencesInFolder(string folderPath, bool requireAddressable = true) {
            string[] guids = AssetDatabase.FindAssets("t:AudioClip", new[] { folderPath });
            List<AssetReferenceT<AudioClip>> references = new List<AssetReferenceT<AudioClip>>();

            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;

            if (requireAddressable && settings == null) {
                Debug.Log("Please create Addressable Asset Settings first.");
                return references;
            }

            foreach (string guid in guids) {
                if (requireAddressable) {
                    AddressableAssetEntry entry = settings.FindAssetEntry(guid);
                    if (entry != null) { // check if the AudioClip is actually Addressable
                        AssetReferenceT<AudioClip> reference = new AssetReferenceT<AudioClip>(guid);
                        references.Add(reference);
                    }
                } else {
                    // For Source folders, return all clips so they can be picked
                    AssetReferenceT<AudioClip> reference = new AssetReferenceT<AudioClip>(guid);
                    references.Add(reference);
                }
            }

            return references;
        }

        public static void FindAllAudioReferencesInWorkspace(out List<AssetReferenceT<AudioClip>> libraryAudioRefs, out List<AssetReferenceT<AudioClip>> workAudioRefs, out List<AssetReferenceT<AudioClip>> sourcesAudioRefs, out List<AssetReferenceT<AudioClip>> zoundFileRefs) {
            var projectSettings = ZoundsProject.Instance.projectSettings;
            
            // Library and System folders require addressables for direct runtime lookup
            libraryAudioRefs = FindAllAudioReferencesInFolder(projectSettings.libraryFolderPath, true);
            workAudioRefs = FindAllAudioReferencesInFolder(projectSettings.workFolderPath, true);
            zoundFileRefs = FindAllAudioReferencesInFolder(projectSettings.zoundFilesFolderPath, true);

            // Sources folder returns ALL clips (even non-addressable) so they can be picked in the editor
            sourcesAudioRefs = FindAllAudioReferencesInFolder(projectSettings.sourcesFolderPath, false);
        }
#endif

        public static bool DisplayZoundRemoveDialog(Zound zoundToRemove) {
            List<Zound> directlyAffectedZounds = GetDirectZoundReferences(zoundToRemove);
            List<Zound> nestedAffectedZounds = GetNestedZoundReferences(zoundToRemove);

            int affectedCount = directlyAffectedZounds.Count + nestedAffectedZounds.Count;

            string affectedZoundsString;
            if (affectedCount == 0) {
                affectedZoundsString = "No other zound affected.";
            }
            else {
                if (affectedCount == 1) {
                    affectedZoundsString = "1 zound affected:\n";
                }
                else {
                    affectedZoundsString = directlyAffectedZounds.Count + " zounds affected:\n";
                }
                foreach (var zound in directlyAffectedZounds) {
                    affectedZoundsString += "- " + zound.name + " (Direct)\n";
                }
                foreach (var zound in nestedAffectedZounds) {
                    affectedZoundsString += "- " + zound.name + " (Nested)\n";
                }
            }

            return EditorUtility.DisplayDialog("Remove Zound", string.Format("Are you sure you want to remove this zound?\n> {0}\n\n{1}", zoundToRemove.name, affectedZoundsString), "Remove", "Cancel");
        }

        public static void RemoveZound(Zound zoundToRemove) {
            var library = ZoundsProject.Instance.zoundLibrary;

            // Handle rendered clip cleanup for Klips and Zequences
            string renderedPath = null;
            if (zoundToRemove is Klip klip) {
                renderedPath = klip.renderedClipPath;
                library.klips.Remove(klip);
            }
            else if (zoundToRemove is Zequence zequence) {
                renderedPath = zequence.renderedClipPath;
                library.zequences.Remove(zequence);
            }

            // Cleanup orphaned rendered file if this was the last usage
            if (!string.IsNullOrEmpty(renderedPath)) {
                if (library.CountRenderedPathUsages(renderedPath) == 0) {
                    if (AssetDatabase.LoadAssetAtPath<AudioClip>(renderedPath) != null) {
                        AssetDatabase.DeleteAsset(renderedPath);
                        Debug.Log("Deleted orphaned rendered clip: " + renderedPath);
                    }
                }
            }

            List<Zound> affectedZounds = GetDirectZoundReferences(zoundToRemove);
            foreach (var zound in affectedZounds) {
                zound.RemoveDependency(zoundToRemove);
            }
        }

        public static Zound DuplicateZound(Zound zoundToDuplicate) {
            var library = ZoundsProject.Instance.zoundLibrary;
            Zound result = null;

            if (zoundToDuplicate is Klip klip) {
                result = new Klip(ZoundLibrary.GetUniqueZoundId(), klip);
                library.klips.Add((Klip)result);
                library.klips = library.klips.OrderBy(it => it.name).ToList();
            }
            else if (zoundToDuplicate is Zequence zequence) {
                result = new Zequence(ZoundLibrary.GetUniqueZoundId(), zequence);
                library.zequences.Add((Zequence)result);
                library.zequences = library.zequences.OrderBy(it => it.name).ToList();
            }

            return result;
        }

        public static List<Zound> GetDirectZoundReferences(Zound zoundToRemove) {
            var library = ZoundsProject.Instance.zoundLibrary;
            return library.FindAllZounds(z => z.HasDirectDependency(zoundToRemove));
        }

        public static List<Zound> GetNestedZoundReferences(Zound zoundToRemove) {
            var library = ZoundsProject.Instance.zoundLibrary;
            return library.FindAllZounds(z => z.HasNestedDependency(zoundToRemove));
        }

    }

}