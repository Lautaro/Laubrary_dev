using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace Laubrary.Zounds {

    public static class ZoundsFilter {

        private static Dictionary<string, List<AudioClip>> folders = new Dictionary<string, List<AudioClip>>();

        public static string[] GetFolders() {
            return folders.Keys.ToArray();
        }

        public static List<AudioClip> GetClipsAtFolder(string folder) {
            var result = new List<AudioClip>();
            //string[] pathOrder = folder.Split('/');
            foreach (var kvp in folders) {
                //string[] currentPathOrder = kvp.Key.Split('/');
                //bool include = true;
                //for (int i = 0; i < pathOrder.Length; i++) {
                //    if (i >= currentPathOrder.Length) {
                //        include = false; break;
                //    }
                //    if (pathOrder[i] != currentPathOrder[i]) {
                //        include = false; break;
                //    }
                //}
                bool include = kvp.Key == folder;
                if (include) {
                    result.AddRange(kvp.Value);
                }
            }
            return result;
        }

        public static List<Zound> GetZoundsByTag(string tagName) {
            var zoundLibrary = ZoundsProject.Instance.zoundLibrary;
            var result = new List<Zound>();
            if (ZoundsProject.Instance.zoundLibrary.TryGetTag(tagName, out var tag)) {
                result.AddRange(zoundLibrary.FindAllZounds(z => z.tags.Contains(tag.id)));
            }

            // add other kvp tags which key match the specified tag
            var nameSplit = tagName.Split(':');
            if (nameSplit.Length == 1) {
                string keyTag = nameSplit[0];
                foreach (var otherTag in zoundLibrary.tags) {
                    if (tag != null && otherTag == tag) continue;
                    var otherNameSplit = otherTag.name.Split(':');
                    if (otherNameSplit.Length > 1 && otherNameSplit[0] == keyTag) {
                        result.AddRange(zoundLibrary.FindAllZounds(z => z.tags.Contains(otherTag.id)));
                    }
                }
            }

            return result;
        }

        public static void RefreshFolders() {
#if ADDRESSABLES_INSTALLED
            folders.Clear();
            AddressableAssetSettings addressableSettings = AddressableAssetSettingsDefaultObject.Settings;

            if (addressableSettings == null) {
                //Debug.Log("Please create Addressable Asset Settings first.");
                return;
            }

            var assets = new List<AddressableAssetEntry>();
            addressableSettings.GetAllAssets(assets, false);

            var reservedZoundKeys = new HashSet<string>();

            string sourcesFolderPath = ZoundsProject.Instance.projectSettings.sourcesFolderPath;
            string workFolderPath = ZoundsProject.Instance.projectSettings.workFolderPath;
            string libraryFolderPath = ZoundsProject.Instance.projectSettings.libraryFolderPath;
            string zoundFilesFolderPath = ZoundsProject.Instance.projectSettings.zoundFilesFolderPath;

            foreach (var asset in assets) {
                IncludeFolders(libraryFolderPath, asset);
                IncludeFolders(sourcesFolderPath, asset);
                IncludeFolders(workFolderPath, asset);
                // ZoundFiles are rendered outputs; exclude from folder filter to avoid clutter
            }
#endif
        }

        private static void IncludeFolders(string parentFolderPath, AddressableAssetEntry asset) {
            if (asset.address.StartsWith(parentFolderPath)) {
                var clipAsset = AssetDatabase.LoadAssetAtPath<AudioClip>(asset.address);
                if (clipAsset == null) return;
                string folderPath = System.IO.Path.GetDirectoryName(asset.address);
                folderPath = folderPath.Substring(parentFolderPath.Length, folderPath.Length - parentFolderPath.Length);
                folderPath = folderPath.Replace('\\', '/');
                if (string.IsNullOrEmpty(folderPath)) folderPath = "/";
                if (!folders.TryGetValue(folderPath, out var clips)) {
                    clips = new List<AudioClip>();
                    folders.Add(folderPath, clips);
                }
                clips.Add(clipAsset);
                //Debug.Log(clipAsset.name + " is added to " + folderPath);
            }
        }
    }

}
