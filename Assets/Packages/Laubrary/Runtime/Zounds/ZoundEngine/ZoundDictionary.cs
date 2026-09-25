using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEngine;

#if ADDRESSABLES_INSTALLED
using UnityEngine.AddressableAssets;
#endif

namespace Laubrary.Zounds {

    public static class ZoundDictionary {

#if UNITY_EDITOR
        internal static List<ClipZound> editorAudioClipZoundsCache;
#endif

#if ADDRESSABLES_INSTALLED
        internal static void Initialize() {
            if (!Application.isPlaying) {
                Debug.LogError("Can't initialize ZoundDictionary during edit mode.");
                return;
            }
            var inst = ZoundEngine.Instance;
            inst.zoundDictionary.Clear();
            inst.runtimeClipFolders.Clear();
            InitZoundsDictionary();
            var ao = Addressables.InitializeAsync();
            ao.WaitForCompletion();
            foreach (var zound in inst.zoundDictionary.Values) {
                if (zound is IZoundAudioClip zoundAudioClip) {
                    var clipRef = zoundAudioClip.GetAudioClipReference();
                    GetOrLoadClip(clipRef);
                }
            }
        }

        internal static async Task InitializeAsync() {
            if (!Application.isPlaying) {
                Debug.LogError("Can't initialize ZoundDictionary during edit mode.");
                return;
            }
            var inst = ZoundEngine.Instance;
            inst.zoundDictionary.Clear();
            inst.runtimeClipFolders.Clear();
            InitZoundsDictionary();
            await Addressables.InitializeAsync().Task;
            var tasks = new List<Task>();
            foreach (var zound in inst.zoundDictionary.Values) {
                if (zound is IZoundAudioClip zoundAudioClip) {
                    var clipRef = zoundAudioClip.GetAudioClipReference();
                    if (!GetOrLoadClipAsync(tasks, zound, clipRef)) {
                        continue;
                    }
                }
            }
            await Task.WhenAll(tasks);
        }

        public static AudioClip GetOrLoadClip(AssetReference clipRef) {
            // A klip that has no clip yet (just created from an external file, not promoted) has no
            // reference at all; that is not an error, there is simply nothing to load.
            if (clipRef == null) return null;
#if UNITY_EDITOR
            if (!Application.isPlaying) {
                try { return clipRef.editorAsset as AudioClip; } catch { return null; }
            }
#endif
            var inst = ZoundEngine.Instance;
            if (!inst.loadedClips.TryGetValue(clipRef.RuntimeKey.ToString(), out AudioClip clip)) {
                if (clipRef.IsValid()) {
                    clip = clipRef.Asset as AudioClip;
                    inst.loadedClips.Add(clipRef.RuntimeKey.ToString(), clip);
                }
                else {
                    if (clipRef.RuntimeKeyIsValid()) {
#if UNITY_EDITOR
                        // An entry registered after Addressables initialised (a clip created during
                        // Play) is unknown to the running locators; the asset database still has it.
                        if (!AddressablesKnowsKey(clipRef.RuntimeKey)) {
                            clip = clipRef.editorAsset as AudioClip;
                            inst.loadedClips.Add(clipRef.RuntimeKey.ToString(), clip);
                            return clip;
                        }
#endif
                        var handle = clipRef.LoadAssetAsync<AudioClip>();
                        clip = handle.WaitForCompletion();
                        inst.loadedClips.Add(clipRef.RuntimeKey.ToString(), clip);
                    }
                    else {
                        Debug.LogError("Invalid AudioClip asset reference.");
                        return null;
                    }
                }
            }
            return clip;
        }

#if UNITY_EDITOR
        private static bool AddressablesKnowsKey(object runtimeKey) {
            foreach (var locator in Addressables.ResourceLocators) {
                if (locator.Locate(runtimeKey, typeof(AudioClip), out _)) return true;
            }
            return false;
        }
#endif

        private static bool GetOrLoadClipAsync(List<Task> tasks, Zound zound, AssetReference clipRef) {
            var inst = ZoundEngine.Instance;
            if (!inst.loadedClips.TryGetValue(clipRef.RuntimeKey.ToString(), out AudioClip clip)) {
                if (clipRef.IsValid()) {
                    clip = clipRef.Asset as AudioClip;
                    inst.loadedClips.Add(clipRef.RuntimeKey.ToString(), clip);
                    return true;
                }
                else {
                    if (clipRef.RuntimeKeyIsValid()) {
                        var handle = clipRef.LoadAssetAsync<AudioClip>();
                        handle.Completed += h => {
                            if (!inst.loadedClips.ContainsKey(clipRef.RuntimeKey.ToString()))
                                inst.loadedClips.Add(clipRef.RuntimeKey.ToString(), h.Result);
                        };
                        tasks.Add(handle.Task);
                        return true;
                    }
                    else {
                        if (zound != null) {
                            Debug.LogError("Invalid AudioClip asset reference at zound: " + zound.name);
                        }
                        return false;
                    }
                }
            }
            return false;
        }

#endif

        public static bool TryGetZoundById(int zoundId, out Zound zound) {
            zound = GetZoundById(zoundId);
            return zound != null;
        }

        public static Zound GetZoundById(int zoundId) {
            var inst = ZoundEngine.Instance;
            if (inst.zoundDictionaryById.TryGetValue(zoundId, out Zound zound)) {
                return zound;
            }
            else {
                var library = ZoundsProject.Instance.zoundLibrary;
                zound = library.FindZound(z => z.id == zoundId);
                if (zound != null) {
                    inst.zoundDictionaryById.Add(zoundId, zound);
                    return zound;
                }
            }
            return null;
        }

        public static bool TryGetZoundByName(string zoundName, out Zound zound) {
            zound = GetZoundByName(zoundName);
            return zound != null;
        }

        public static Zound GetZoundByName(string zoundName) {
            var inst = ZoundEngine.Instance;
            string key = ZoundNameToKey(zoundName);
            if (inst.zoundDictionary.TryGetValue(key, out Zound zound)) {
                return zound;
            }
            else {
                var library = ZoundsProject.Instance.zoundLibrary;
                zound = library.FindZound(z => ZoundNameToKey(z.name) == key);
                if (zound != null) {
                    inst.zoundDictionary.Add(key, zound);
                    return zound;
                }
            }
            return null;
        }

        public static string ZoundNameToKey(string zoundName) {
            return zoundName.ToLower()
                .Replace(" ", "")
                .Replace("_", "")
                .Replace("-", "");
        }

        public static string EnsureUniqueZoundName(string zoundName, Zound zoundToIgnore = null) {
            var zoundsProject = ZoundsProject.Instance;
            var library = zoundsProject.zoundLibrary;
            string key = ZoundNameToKey(zoundName);
            string currentKey = key;
            int iteration = 0;
            bool isUnique = false;

            bool hasDuplicateNumber = false;
            bool nameHasDuplicateNumber = Regex.IsMatch(zoundName, @"\(\d+\)$");

            while (true) {
                // Only top-level Zounds (parentId == 0) are keyed by name — see AddZoundToKeysDictionary /
                // InitZoundsDictionary, which skip children. Local Zounds nested inside a Zequence
                // (localKlips / localZequences) are addressed by id, never by name, so they must not
                // block a top-level rename. Excluding them here matches the name-dictionary's keying rule.
                isUnique = library.FindZound(z => z.parentId == 0 && z != zoundToIgnore && ZoundNameToKey(z.name) == currentKey) == null;
                if (isUnique) break;
                iteration++;
                hasDuplicateNumber = Regex.IsMatch(currentKey, @"\(\d+\)$");
                if (hasDuplicateNumber) {
                    currentKey = Regex.Replace(currentKey, @"\(\d+\)$", $"({iteration})");
                }
                else {
                    currentKey = key + "(" + iteration.ToString() + ")";
                }
            }

            if (iteration == 0) return zoundName;
            else {
                if (nameHasDuplicateNumber) return Regex.Replace(zoundName, @"\(\d+\)$", $"({iteration})");
                return zoundName + " (" + iteration + ")";
            }
        }

        public static void ValidateZoundRuntime(Zound zoundToValidate = null) {
            var inst = ZoundEngine.Instance;
            string key = ZoundNameToKey(zoundToValidate.name);
            bool handled = false;
            foreach (var kvp in inst.zoundDictionary) {
                if (zoundToValidate == kvp.Value) {
                    inst.zoundDictionary.Remove(kvp.Key);
                    if (inst.zoundDictionary.TryGetValue(key, out var existingZound)) {
                        if (existingZound is ClipZound clipZound) {
                            inst.zoundDictionary.Remove(key);
                            inst.zoundDictionary.Add(key, zoundToValidate);
                        }
                        else if (existingZound.id == zoundToValidate.id) {
                            // Same zound, already handled
                        }
                        else {
                            if (existingZound.parentId == 0 && zoundToValidate.parentId == 0) {
                                Debug.LogError("Multiple zounds with the same key exist: " + key);
                            }
                        }
                    }
                    else {
                        inst.zoundDictionary.Add(key, zoundToValidate);
                    }
                    handled = true;
                    break;
                }
            }
            if (!handled) {
                if (inst.zoundDictionary.TryGetValue(key, out var existingZound)) {
                    if (existingZound is ClipZound clipZound) {
                        inst.zoundDictionary.Remove(key);
                    }
                    else {
                        return;
                    }
                }
                inst.zoundDictionary.Add(key, zoundToValidate);
                if (zoundToValidate is IZoundAudioClip zoundAudioClip) {
                    var clipRef = zoundAudioClip.GetAudioClipReference();
                    GetOrLoadClip(clipRef);
                }
            }
        }

        internal static ClipZound FindClipZoundByAudioClip(AudioClip audioClip) {
            var inst = ZoundEngine.Instance;
            foreach (var zound in inst.zoundDictionary.Values) {
                if (zound is ClipZound clipZound) {
                    if (clipZound.audioClip == audioClip) {
                        return clipZound;
                    }
                }
            }
            return null;
        }

        private static void InitZoundsDictionary() {
            var inst = ZoundEngine.Instance;
            if (inst.zoundDictionary == null || inst.zoundDictionary.Count == 0) {
                inst.zoundDictionary = new Dictionary<string, Zound>();
                var zoundLibrary = ZoundsProject.Instance.zoundLibrary;
                zoundLibrary.ForEachZound(z => {
                    if (z.parentId == 0) AddZoundToKeysDictionary(z);
                });
            }
            if (inst.zoundDictionaryById == null || inst.zoundDictionaryById.Count == 0) {
                inst.zoundDictionaryById = new Dictionary<int, Zound>();
                var zoundLibrary = ZoundsProject.Instance.zoundLibrary;
                zoundLibrary.ForEachZound(zound => {
                    if (inst.zoundDictionaryById.ContainsKey(zound.id)) {
                        Debug.LogError("Multiple zounds with the same id exist: " + zound.id + "(" + zound.name + " & " + inst.zoundDictionaryById[zound.id].name + ")");
                    }
                    else {
                        inst.zoundDictionaryById.Add(zound.id, zound);
                    }
                });
            }
        }

        private static void AddZoundToKeysDictionary(Zound zound) {
            var inst = ZoundEngine.Instance;
            string key = ZoundNameToKey(zound.name);
            if (inst.zoundDictionary.TryGetValue(key, out var existingZound)) {
                if (existingZound is ClipZound clipZound) {
                    inst.zoundDictionary.Remove(key);
                    inst.zoundDictionary.Add(key, zound);
                }
                else if (existingZound.id == zound.id) {
                    // It's the same zound instance, just ignore
                }
                else {
                    // Only log error if both are global sounds to avoid noise from local clips
                    if (existingZound.parentId == 0 && zound.parentId == 0) {
                        Debug.LogError("Multiple zounds with the same key exist: " + key);
                    }
                }
            }
            else {
                inst.zoundDictionary.Add(key, zound);
            }
        }
    }

}
