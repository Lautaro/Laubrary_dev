#if ADDRESSABLES_INSTALLED
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;

namespace Laubrary.Zounds
{

    public class ZoundsAssetPostProcessor : AssetPostprocessor
    {

        private static List<ClipZound> s_audioClipZoundsCache = null;
        internal static List<ClipZound> audioClipZoundsCache
        {
            get
            {
                if (s_audioClipZoundsCache == null)
                {
                    RefreshAudioClipsCache();
                }
                return s_audioClipZoundsCache;
            }
        }

        [InitializeOnLoadMethod]
        private static void OnLoad()
        {
            AssemblyReloadEvents.afterAssemblyReload += OnAfterAssemblyReload;
            ZoundsProject.onProjectLoaded += OnProjectLoaded;
        }

        private static void OnProjectLoaded()
        {
            RefreshAudioClipsCache();
            ZoundsWindow.RepaintWindow();
        }

        private static void OnAfterAssemblyReload()
        {
            RefreshAudioClipsCache();
        }

        public static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            // Handle active project file deletion or modification before any other processing.
            string activeProjectPath = ZoundsProjectInitialization.GetZoundsProjectPath();
            if (!string.IsNullOrEmpty(activeProjectPath)) {
                if (deletedAssets.Contains(activeProjectPath)) {
                    ZoundsProjectInitialization.SetZoundsProjectPath(string.Empty);
                    ZoundsProject.ResetToDefault();
                    ZoundsWindow.RepaintWindow();
                }
                else if (importedAssets.Contains(activeProjectPath)) {
                    // Skip reloading if WE triggered this import by saving — the in-memory
                    // state is already correct and the stale TextAsset would overwrite it.
                    if (!ZoundsWindow.isSavingJSON) {
                        var projectJSONAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(activeProjectPath);
                        if (projectJSONAsset != null) {
                            ZoundsProject.LoadFromJSON(projectJSONAsset);
                            ZoundsWindow.RepaintWindow();
                        }
                    }
                }
            }

            var project = ZoundsProject.Instance;
            if (project == null || project.projectSettings == null)
                return;

            var projectSettings = project.projectSettings;

            // Passively check if Addressable settings exist first
            AddressableAssetSettings addressableSettings = AddressableAssetSettingsDefaultObject.Settings;

            // Only attempt creation if settings are missing AND we actually have Zounds-relevant assets to process
            if (addressableSettings == null)
            {
                bool hasTargetAssets = importedAssets.Concat(movedAssets).Any(path =>
                    path.StartsWith(projectSettings.libraryFolderPath) ||
                    path.StartsWith(projectSettings.sourcesFolderPath) ||
                    path.StartsWith(projectSettings.workFolderPath) ||
                    path.StartsWith(projectSettings.zoundFilesFolderPath));

                if (hasTargetAssets)
                {
                    addressableSettings = AddressableAssetSettingsDefaultObject.GetSettings(true);
                }
            }

            // addressableSettings may still be null here (e.g. during package import with no audio files).
            // All downstream methods guard against this.
            bool hasAtLeast1AudioClip = false;
            bool dirty = EnsureUniqueAudioClipNames(addressableSettings, importedAssets, movedAssets);

            List<string> modifiedAssets = null;

            foreach (var assetPath in importedAssets)
            {
                if (ProcessAsset(projectSettings, addressableSettings, assetPath, true))
                {
                    if (modifiedAssets == null) modifiedAssets = new List<string>();
                    modifiedAssets.Add(assetPath);
                    dirty = true;
                }
                if (!hasAtLeast1AudioClip)
                {
                    if (AssetDatabase.GetMainAssetTypeAtPath(assetPath) == typeof(AudioClip))
                    {
                        hasAtLeast1AudioClip = true;
                    }
                }
            }
            foreach (var assetPath in movedAssets)
            {
                if (ProcessAsset(projectSettings, addressableSettings, assetPath, false))
                {
                    if (modifiedAssets == null) modifiedAssets = new List<string>();
                    modifiedAssets.Add(assetPath);
                    dirty = true;
                }
                if (!hasAtLeast1AudioClip)
                {
                    if (AssetDatabase.GetMainAssetTypeAtPath(assetPath) == typeof(AudioClip))
                    {
                        hasAtLeast1AudioClip = true;
                    }
                }
            }

            if (dirty)
            {
                ValidateZoundPaths();
                AssetDatabase.SaveAssets();
                ZoundsFilter.RefreshFolders();

                RefreshAudioClipsCache();

                if (ZoundEngine.IsInitialized()) {
                    var inst = ZoundEngine.Instance;
                    inst.zoundDictionary.Clear(); // Refresh the dictionary
                }

                ZoundsWindow.SetZoundsProjectDirty();
            }
            else if (hasAtLeast1AudioClip)
            {
                RefreshAudioClipsCache();
            }

        }

        public static void RefreshAudioClipsCache()
        {
            // Passive getter only � never force-create settings from a cache refresh
            AddressableAssetSettings addressableSettings = AddressableAssetSettingsDefaultObject.Settings;

            if (addressableSettings == null)
                return;

            var zoundsProject = ZoundsProject.Instance;
            if (zoundsProject == null || zoundsProject.projectSettings == null)
                return;

            if (s_audioClipZoundsCache == null) s_audioClipZoundsCache = new List<ClipZound>();
            s_audioClipZoundsCache.Clear();

            var assets = new List<AddressableAssetEntry>();
            addressableSettings.GetAllAssets(assets, false);

            string libraryFolderPath = zoundsProject.projectSettings.libraryFolderPath;
            var zoundLibrary = zoundsProject.zoundLibrary;
            foreach (var asset in assets)
            {
                if (asset.address.StartsWith(libraryFolderPath))
                {
                    string normalizedAddress = asset.address.Replace('\\', '/');
                    string assetName = System.IO.Path.GetFileNameWithoutExtension(normalizedAddress);
                    if (asset.TargetAsset is AudioClip audioClip)
                    {
                        var zoundKey = ZoundDictionary.ZoundNameToKey(audioClip.name);
                        var klip = zoundLibrary.FindZound(z => ZoundDictionary.ZoundNameToKey(z.name) == zoundKey);
                        if (klip == null)
                        {
                            s_audioClipZoundsCache.Add(new ClipZound(audioClip, asset.address));
                        }
                    }
                }
            }

            ZoundDictionary.editorAudioClipZoundsCache = s_audioClipZoundsCache;
        }

        private static bool ProcessAsset(ZoundsProject.ProjectSettings projectSettings, AddressableAssetSettings addressableSettings, string assetPath, bool isNew)
        {
            bool inLibrary = assetPath.StartsWith(projectSettings.libraryFolderPath);
            bool inSources = assetPath.StartsWith(projectSettings.sourcesFolderPath);
            bool inWork = assetPath.StartsWith(projectSettings.workFolderPath);
            bool inZoundFiles = assetPath.StartsWith(projectSettings.zoundFilesFolderPath);

            if (!inLibrary && !inSources && !inWork && !inZoundFiles)
            {
                // If this is under the ZoundsData root but not a recognized folder (legacy path),
                // ensure it's NOT Addressable
                string zoundsDataRoot = projectSettings.systemFolderPath;
                int lastSlash = zoundsDataRoot.LastIndexOf('/');
                if (lastSlash > 0) zoundsDataRoot = zoundsDataRoot.Substring(0, lastSlash);

                if (assetPath.StartsWith(zoundsDataRoot) && addressableSettings != null)
                {
                    string legacyGuid = AssetDatabase.AssetPathToGUID(assetPath);
                    if (addressableSettings.FindAssetEntry(legacyGuid) != null)
                    {
                        addressableSettings.RemoveAssetEntry(legacyGuid);
                        return true;
                    }
                }
                return false;
            }

            if (AssetDatabase.GetMainAssetTypeAtPath(assetPath) != typeof(AudioClip))
            {
                return false;
            }

            // Update runtime dictionary regardless of addressable status
            if (ZoundEngine.IsInitialized())
            {
                var clipAsset = AssetDatabase.LoadAssetAtPath<AudioClip>(assetPath);
                if (isNew)
                {
                    var clipZound = new ClipZound(clipAsset, assetPath);
                    ZoundDictionary.ValidateZoundRuntime(clipZound);
                }
                else
                {
                    var clipZound = ZoundDictionary.FindClipZoundByAudioClip(clipAsset);
                    if (clipZound != null)
                    {
                        clipZound.name = clipAsset.name;
                        clipZound.audioPath = assetPath;
                        ZoundDictionary.ValidateZoundRuntime(clipZound);
                    }
                }
            }

            // Guard: addressableSettings may be null if Addressables not yet initialized
            if (addressableSettings == null)
                return false;

            string guid = AssetDatabase.AssetPathToGUID(assetPath);
            bool shouldBeAddressable = ShouldBeAddressable(guid, inLibrary, inSources, inWork, inZoundFiles);

            return SetAddressableState(addressableSettings, guid, assetPath, shouldBeAddressable);
        }

        /// <summary>
        /// Determines whether an audio clip should be included in builds as an Addressable.
        /// Delegates to <see cref="ZoundsClipClassifier"/> — the single source of truth.
        /// </summary>
        internal static bool ShouldBeAddressable(string assetGuid, bool inLibrary, bool inSources, bool inWork, bool inZoundFiles)
        {
            return ZoundsClipClassifier.ShouldBeAddressable(assetGuid, inLibrary, inSources, inWork, inZoundFiles);
        }

        /// <summary>
        /// Ensures the Addressable state of an asset matches the desired state.
        /// Registers or de-registers as needed. Returns true if any change was made.
        /// </summary>
        private static bool SetAddressableState(AddressableAssetSettings settings, string guid, string assetPath, bool shouldBeAddressable)
        {
            AddressableAssetEntry entry = settings.FindAssetEntry(guid);

            if (!shouldBeAddressable)
            {
                if (entry != null)
                {
                    settings.RemoveAssetEntry(guid);
                    return true;
                }
                return false;
            }

            bool dirty = false;
            if (entry == null)
            {
                string groupName = "Zounds Default Local Group";
                AddressableAssetGroup group = settings.FindGroup(groupName);
                if (group == null)
                {
                    group = settings.CreateGroup(groupName, false, false, false, null, typeof(ContentUpdateGroupSchema), typeof(BundledAssetGroupSchema));
                }
                entry = settings.CreateOrMoveEntry(guid, group);
                dirty = true;
            }

            if (entry.address != assetPath)
            {
                entry.address = assetPath;
                dirty = true;
            }

            if (dirty)
            {
                settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryMoved, entry, true);
            }
            return dirty;
        }

        private static bool EnsureUniqueAudioClipNames(AddressableAssetSettings addressableSettings, string[] importedAssets, string[] movedAssets)
        {
            // Guard: if no settings yet, nothing to deduplicate against
            if (addressableSettings == null)
                return false;

            var project = ZoundsProject.Instance;
            if (project == null || project.projectSettings == null)
                return false;

            string userFolderPath = project.projectSettings.libraryFolderPath;
            var assets = new List<AddressableAssetEntry>();
            addressableSettings.GetAllAssets(assets, false);

            var reservedZoundKeys = new HashSet<string>();

            foreach (var asset in assets)
            {
                if (asset.address.StartsWith(userFolderPath))
                {
                    if (importedAssets.Contains(asset.address) || movedAssets.Contains(asset.address))
                    {
                        continue;
                    }
                    string normalizedAddress = asset.address.Replace('\\', '/');
                    string assetName = System.IO.Path.GetFileNameWithoutExtension(normalizedAddress);
                    string zoundKey = ZoundDictionary.ZoundNameToKey(assetName);
                    reservedZoundKeys.Add(zoundKey);
                }
            }

            var allPaths = new List<string>();
            allPaths.AddRange(importedAssets);
            allPaths.AddRange(movedAssets);

            bool dirty = false;
            foreach (var assetPath in allPaths)
            {
                string normalizedAddress = assetPath.Replace('\\', '/');
                string assetName = System.IO.Path.GetFileNameWithoutExtension(normalizedAddress);
                string zoundKey = ZoundDictionary.ZoundNameToKey(assetName);
                if (reservedZoundKeys.Contains(zoundKey))
                {
                    int iterator = 0;
                    string uniqueZoundKey;

                    do
                    {
                        iterator++;
                        uniqueZoundKey = zoundKey + iterator;
                    } while (reservedZoundKeys.Contains(uniqueZoundKey));

                    assetName += " " + iterator;
                    reservedZoundKeys.Add(uniqueZoundKey);
                    dirty = true;
                    var errorMessage = AssetDatabase.RenameAsset(assetPath, assetName);
                    if (!string.IsNullOrEmpty(errorMessage))
                    {
                        Debug.LogError(errorMessage + ": " + assetPath);
                    }

                    if (ZoundEngine.IsInitialized())
                    {
                        if (ZoundDictionary.TryGetZoundByName(zoundKey, out var zound))
                        {
                            zound.name = assetName;
                            ZoundDictionary.ValidateZoundRuntime(zound);
                        }
                    }
                }
            }

            return dirty;
        }

        private static void ValidateZoundPaths()
        {
            var zoundsProject = ZoundsProject.Instance;
            var zoundLibrary = zoundsProject.zoundLibrary;

            if (Application.isPlaying && ZoundEngine.IsInitialized())
            {
                var inst = ZoundEngine.Instance;
                foreach (var zound in inst.zoundDictionary.Values)
                {
                    if (zound is ClipZound clipZound)
                    {
                        if (clipZound.audioClip != null)
                        {
                            clipZound.audioPath = AssetDatabase.GetAssetPath(clipZound.audioClip);
                        }
                        else
                        {
                            clipZound.audioPath = "";
                        }
                    }
                }
            }

            zoundLibrary.ForEachZound(zound => {
                AssignZoundPath(zound);
            });
            EditorUtility.SetDirty(zoundsProject);

            if (Application.isPlaying && ZoundEngine.IsInitialized())
            {
                var inst = ZoundEngine.Instance;
                var keys = inst.runtimeClipFolders.Keys.ToArray();
                foreach (var clip in keys)
                {
                    if (clip == null) continue;
                    string path = AssetDatabase.GetAssetPath(clip);
                    inst.runtimeClipFolders[clip] = ZoundRoutings.GetFolderFromClipPath(path);
                }
            }
        }

        private static void AssignZoundPath(Zound zound)
        {
            if (zound is Klip klip)
            {
                if (klip.audioClipRef != null && klip.audioClipRef.editorAsset != null)
                {
                    klip.audioClipPath = AssetDatabase.GetAssetPath(klip.audioClipRef.editorAsset);
                }
                else
                {
                    klip.audioClipPath = "";
                }
                if (klip.renderedClipRef != null && klip.renderedClipRef.editorAsset != null)
                {
                    klip.renderedClipPath = AssetDatabase.GetAssetPath(klip.renderedClipRef.editorAsset);
                }
                else
                {
                    klip.renderedClipPath = "";
                }
                if (klip.outputClipRef != null && klip.outputClipRef.editorAsset != null)
                {
                    klip.outputClipPath = AssetDatabase.GetAssetPath(klip.outputClipRef.editorAsset);
                }
                else
                {
                    klip.outputClipPath = "";
                }
            }
            else if (zound is Zequence zequence)
            {
                if (zequence.renderedClipRef != null && zequence.renderedClipRef.editorAsset != null)
                {
                    zequence.renderedClipPath = AssetDatabase.GetAssetPath(zequence.renderedClipRef.editorAsset);
                }
                else
                {
                    zequence.renderedClipPath = "";
                }
                foreach (var entry in zequence.zoundEntries)
                {
                    if (!entry.local) continue;
                    if (zequence.TryGetEntryZound(entry, out var childZound))
                    {
                        AssignZoundPath(childZound);
                    }
                }
            }
        }
    }

}
#endif
