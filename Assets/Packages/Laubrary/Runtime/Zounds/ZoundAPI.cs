using System.Collections.Generic;
using System.Linq;
using UnityEngine;
#if ADDRESSABLES_INSTALLED
using UnityEngine.AddressableAssets;
#endif

namespace Laubrary.Zounds {

    public static class ZoundAPI {

#if UNITY_EDITOR
        internal static System.Action<Klip> onEditorAPIKlipCreated;
        internal static System.Action<Zequence> onEditorAPIZequenceCreated;
        internal static System.Action<Zequence, Zound, bool> onEditorAPIZequenceAddZound;
        internal static System.Action onSetAllTabsDirty;
        internal static System.Action<string, System.Action, bool> onModifyZoundsProject;
#endif

        public static Klip CreateKlip(AudioClip audioClip, string name = null) {
            AssetReference assetRef = GetClipAssetReference(audioClip);

            if (assetRef.RuntimeKeyIsValid()) {
                return CreateNewKlip(name == null ? audioClip.name : name, assetRef, audioClip.length);
            }
            else {
                Debug.Log("Error creating a Klip: Audio clip " + audioClip.name + " is not an addressable.", audioClip);
                return null;
            }
        }

        public static Klip CreateKlip(AssetReference audioClipReference, string name = null) {
            AudioClip audioClip = GetAudioClipFromAssetReference(audioClipReference);
            if (audioClip != null) {
                return CreateNewKlip(name == null ? audioClip.name : name, audioClipReference, audioClip.length);
            }
            else {
                Debug.Log("Error creating a Klip: Invalid audio clip reference.");
                return null;
            }
        }

#if UNITY_EDITOR
        /// <summary>
        /// Creates a Klip from an external audio file on disk (outside the Unity project).
        /// The caller must provide the clip length (from decoding the WAV header) since
        /// the runtime assembly cannot reference editor-only WavDecoder.
        /// </summary>
        public static Klip CreateKlipFromExternalSource(string absolutePath, float clipLength, string name = null) {
            if (string.IsNullOrEmpty(absolutePath)) {
                Debug.LogError("[Zounds] CreateKlipFromExternalSource: No path provided.");
                return null;
            }

            var newKlip = new Klip(ZoundLibrary.GetUniqueZoundId());
            newKlip.externalSourcePath = absolutePath;
            newKlip.name = ZoundDictionary.EnsureUniqueZoundName(
                name ?? System.IO.Path.GetFileNameWithoutExtension(absolutePath));
            newKlip.trimStart = 0f;
            newKlip.trimEnd = clipLength;
            newKlip.volumeEnvelope = new Envelope(Zound.MinVolumeRange, Zound.MaxVolumeRange);
            newKlip.pitchEnvelope = new Envelope(Zound.MinPitchRange, Zound.MaxPitchRange);

            if (ZoundEngine.IsInitialized()) {
                ZoundDictionary.ValidateZoundRuntime(newKlip);
            }

            onModifyZoundsProject?.Invoke("create klip from external source", () => {
                onEditorAPIKlipCreated?.Invoke(newKlip);
            }, false);

            return newKlip;
        }
#endif

        public static Zequence CreateZequence(string name) {
            var newZequence = new Zequence(ZoundLibrary.GetUniqueZoundId());
            newZequence.name = ZoundDictionary.EnsureUniqueZoundName(name);

#if UNITY_EDITOR
            onModifyZoundsProject?.Invoke("create new zequence", () => {
                onEditorAPIZequenceCreated?.Invoke(newZequence);
            }, false);
#else
            var zoundKey = ZoundDictionary.ZoundNameToKey(newZequence.name);
            var inst = ZoundEngine.Instance;
            if (inst.zoundDictionary.ContainsKey(zoundKey)) {
                inst.zoundDictionary.Remove(zoundKey);
            }
            var zoundLibrary = ZoundsProject.Instance.zoundLibrary;
            zoundLibrary.zequences.Add(newZequence);
            zoundLibrary.zequences = zoundLibrary.zequences.OrderBy(it => it.name).ToList();
            if (ZoundEngine.IsInitialized()) {
                ZoundDictionary.ValidateZoundRuntime(newZequence);
            }
#endif

            return newZequence;
        }

        public static void AddSharedZoundToZequence(Zound childZoundToAdd, Zequence existingZequence) {
#if UNITY_EDITOR
            onEditorAPIZequenceAddZound?.Invoke(existingZequence, childZoundToAdd, false);
#else
            var newEntry = new CompositeZound.ZoundEntry();
            newEntry.zoundId = childZoundToAdd.id;
            newEntry.local = false;
            existingZequence.zoundEntries.Add(newEntry);
#endif
        }

        public static Klip CreateLocalKlip(Zequence existingZequence, AudioClip audioClip, string name = null) {
            AssetReference assetRef = GetClipAssetReference(audioClip);

            if (assetRef.RuntimeKeyIsValid()) {
                return CreateNewLocalKlip(existingZequence, name == null ? audioClip.name : name, assetRef, audioClip.length);
            }
            else {
                Debug.Log("Error creating a Klip: Audio clip " + audioClip.name + " is not an addressable.", audioClip);
                return null;
            }
        }

        public static Klip CreateLocalKlip(Zequence existingZequence, AssetReference audioClipReference, string name = null) {
            AudioClip audioClip = GetAudioClipFromAssetReference(audioClipReference);
            if (audioClip != null) {
                return CreateNewLocalKlip(existingZequence,name == null ? audioClip.name : name, audioClipReference, audioClip.length);
            }
            else {
                Debug.Log("Error creating a Klip: Invalid audio clip reference.");
                return null;
            }
        }

        public static Zequence CreateLocalZequence(Zequence existingZequence, string name) {
            var newZequence = new Zequence(ZoundLibrary.GetUniqueZoundId());
            newZequence.name = ZoundDictionary.EnsureUniqueZoundName(name);

#if UNITY_EDITOR
            onModifyZoundsProject?.Invoke("add local zound entry", () => {
                newZequence.parentId = existingZequence.id;
                existingZequence.localZequences.Add(new CompositeZound.LocalZequence(newZequence));
            }, false);
            onEditorAPIZequenceAddZound?.Invoke(existingZequence, newZequence, true);
#else
            newZequence.parentId = existingZequence.id;
            existingZequence.localZequences.Add(new CompositeZound.LocalZequence(newZequence));
            var newEntry = new CompositeZound.ZoundEntry();
            newEntry.zoundId = newZequence.id;
            newEntry.local = true;
            existingZequence.zoundEntries.Add(newEntry);
#endif

            return newZequence;
        }

        public static void AddTagToZound(Zound zound, string tagKey, string tagValue = null) {
            var zoundsProject = ZoundsProject.Instance;
#if UNITY_EDITOR
            onModifyZoundsProject?.Invoke("add tag", () => {
#endif
                var zoundLibrary = zoundsProject.zoundLibrary;
                var projectTags = zoundLibrary.tags;
                var zoundTags = projectTags.Where(tag => zound.tags.Contains(tag.id));

                ZoundLibrary.Tag existingTag = null;
                foreach (var t in zoundTags) {
                    var split = t.name.Split(':');
                    if (split[0] == tagKey) {
                        existingTag = t;
                        break;
                    }
                }

                if (existingTag != null) {
                    zound.tags.Remove(existingTag.id);
                }

                if (!string.IsNullOrWhiteSpace(tagValue)) {
                    tagKey += ":" + tagValue;
                }
                if (!zoundLibrary.TryGetTag(tagKey, out var tagToAdd)) {
                    tagToAdd = zoundLibrary.CreateNewTag(tagKey);
                }
                zound.tags.Add(tagToAdd.id);
                zoundLibrary.RemoveUnusedTags();
#if UNITY_EDITOR
            }, false);
#endif
        }

        public static bool DoesZoundExist(string zoundName) {
            return ZoundDictionary.TryGetZoundByName(zoundName, out _);
        }

        public static List<Zound> GetAllZounds() {
#if UNITY_EDITOR
            if (!Application.isPlaying) {
                var zounds = ZoundsProject.Instance.zoundLibrary.GetAllZounds();
                zounds.AddRange(ZoundDictionary.editorAudioClipZoundsCache);
                return zounds;
            }
#endif
            return ZoundEngine.Instance.zoundDictionary.Values.ToList();
        }

        public static Zound GetZound(string zoundName) {
            return ZoundDictionary.GetZoundByName(zoundName);
        }

        public static IEnumerable<string> GetAllTags() {
            var zoundLibrary = ZoundsProject.Instance.zoundLibrary;
            return zoundLibrary.tags.Select(t => t.name);
        }

        public static IEnumerable<string> GetTagsInZound(string zoundName) {
            var zound = GetZound(zoundName);
            if (zound == null) {
                Debug.LogError("Zound doesn't exist: " + zoundName);
                return null;
            }
            else return GetTagsInZound(zound);
        }

        public static IEnumerable<string> GetTagsInZound(Zound zound) {
            var projectTags = ZoundsProject.Instance.zoundLibrary.tags;
            var zoundTags = projectTags.Where(tag => zound.tags.Contains(tag.id));
            return zoundTags.Select(t => t.name);
        }

        public static List<Zound> GetAllZoundsByTag(string tagName) {
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

        public static List<Zound> GetAllZoundsByTags(params string[] tagNames) {
            var zoundLibrary = ZoundsProject.Instance.zoundLibrary;

            var result = zoundLibrary.FindAllZounds(zound => {
                bool include = true;
                foreach (var tagName in tagNames) {
                    if (ZoundsProject.Instance.zoundLibrary.TryGetTag(tagName, out var tag)) {
                        if (zound.tags.Contains(tag.id)) continue;
                    }

                    // add other kvp tags which key match the specified tag
                    var nameSplit = tagName.Split(':');
                    if (nameSplit.Length == 1) {
                        bool found = false;
                        string keyTag = nameSplit[0];
                        foreach (var otherTag in zoundLibrary.tags) {
                            if (found) break;
                            if (tag != null && otherTag == tag) continue;
                            var otherNameSplit = otherTag.name.Split(':');
                            if (otherNameSplit.Length > 1 && otherNameSplit[0] == keyTag) {
                                if (zound.tags.Contains(otherTag.id)) {
                                    found = true;
                                    break;
                                }
                            }
                        }
                        if (found) continue;
                    }
                    include = false;
                    break;
                }
                return include;
            });

            return result;
        }




        private static AssetReference GetClipAssetReference(AudioClip audioClip) {
#if UNITY_EDITOR
            string assetPath = UnityEditor.AssetDatabase.GetAssetPath(audioClip);
            string guid = UnityEditor.AssetDatabase.AssetPathToGUID(assetPath);
            var assetRef = new AssetReference(guid);
            if (!assetRef.RuntimeKeyIsValid()) {
                Debug.LogError("Can't find addressable AssetReference for audio clip " + audioClip.name + ". Make sure the clip is marked as addressable.");
            }
            return assetRef;
#else
            return new AssetReference();
#endif
        }

        private static AudioClip GetAudioClipFromAssetReference(AssetReference audioClipReference) {
            AudioClip audioClip;
            if (Application.isPlaying) {
                audioClip = ZoundDictionary.GetOrLoadClip(audioClipReference);
            }
            else {
#if UNITY_EDITOR
                audioClip = audioClipReference.editorAsset as AudioClip;
#else
                audioClip = null;
#endif
            }

            return audioClip;
        }

        private static Klip CreateNewKlip(string name, AssetReference assetRef, float clipLength) {
            var newKlip = new Klip(ZoundLibrary.GetUniqueZoundId());

            newKlip.audioClipRef = assetRef;
            newKlip.name = ZoundDictionary.EnsureUniqueZoundName(name);

            newKlip.trimStart = 0f;
            newKlip.trimEnd = clipLength;
            newKlip.volumeEnvelope = new Envelope(Zound.MinVolumeRange, Zound.MaxVolumeRange);
            newKlip.pitchEnvelope = new Envelope(Zound.MinPitchRange, Zound.MaxPitchRange);

            if (ZoundEngine.IsInitialized()) {
                ZoundDictionary.ValidateZoundRuntime(newKlip);
            }

#if UNITY_EDITOR
            onModifyZoundsProject?.Invoke("create new klip", () => {
                onEditorAPIKlipCreated?.Invoke(newKlip);
            }, false);
#else
            var zoundKey = ZoundDictionary.ZoundNameToKey(newKlip.name);
            var inst = ZoundEngine.Instance;
            if (inst.zoundDictionary.ContainsKey(zoundKey)) {
                inst.zoundDictionary.Remove(zoundKey);
            }
            var zoundLibrary = ZoundsProject.Instance.zoundLibrary;
            zoundLibrary.klips.Add(newKlip);
            zoundLibrary.klips = zoundLibrary.klips.OrderBy(it => it.name).ToList();
#endif

            return newKlip;
        }

        private static Klip CreateNewLocalKlip(Zequence existingZequence, string name, AssetReference assetRef, float clipLength) {
            var newKlip = new Klip(ZoundLibrary.GetUniqueZoundId());

            newKlip.audioClipRef = assetRef;
            newKlip.name = ZoundDictionary.EnsureUniqueZoundName(name);

            newKlip.trimStart = 0f;
            newKlip.trimEnd = clipLength;
            newKlip.volumeEnvelope = new Envelope(Zound.MinVolumeRange, Zound.MaxVolumeRange);
            newKlip.pitchEnvelope = new Envelope(Zound.MinPitchRange, Zound.MaxPitchRange);

            if (ZoundEngine.IsInitialized()) {
                ZoundDictionary.ValidateZoundRuntime(newKlip);
            }

#if UNITY_EDITOR
            onModifyZoundsProject?.Invoke("add local zound entry", () => {
                newKlip.parentId = existingZequence.id;
                existingZequence.localKlips.Add(newKlip);
            }, false);
            onEditorAPIZequenceAddZound?.Invoke(existingZequence, newKlip, true);
#else
            newKlip.parentId = existingZequence.id;
            existingZequence.localKlips.Add(newKlip);
            var newEntry = new CompositeZound.ZoundEntry();
            newEntry.zoundId = newKlip.id;
            newEntry.local = true;
            existingZequence.zoundEntries.Add(newEntry);
#endif

            return newKlip;
        }

        public static bool HasTag(Zound zound, string tag) {
            var projectTags = ZoundsProject.Instance.zoundLibrary.tags;
            var zoundTags = projectTags.Where(tag => zound.tags.Contains(tag.id));
            string prefix = tag + ":";
            foreach (var t in zoundTags) {
                if (t.name == tag) return true;
                if (t.name.StartsWith(prefix)) return true;
            }
            return false;
        }

        public static bool HasTagWithValue(Zound zound, string tag, string tagValue) {
            var projectTags = ZoundsProject.Instance.zoundLibrary.tags;
            var zoundTags = projectTags.Where(tag => zound.tags.Contains(tag.id));
            string prefix = tag + ":";
            List<string> values = null;
            foreach (var t in zoundTags) {
                if (!t.name.StartsWith(prefix)) continue;
                if (values == null) values = new List<string>();
                ParseZoundTagValues(t.name, values);
                foreach (var val in values) {
                    if (val == tagValue) return true;
                }

            }
            return false;
        }

        public static IEnumerable<KeyValuePair<string, List<string>>> GetZoundTagsAsKeyValuePairs(Zound zound) {
            return GetTagsInZound(zound.name)
                .Select(t => {
                    var tagKey = ParseZoundTagKey(t);
                    return new KeyValuePair<string, List<string>>(tagKey, GetZoundTagValuesByKey(tagKey));
                });
        }

        public static KeyValuePair<string, List<string>> GetZoundTagAsKeyValuePair(Zound zound, string tag) {
            var tagKey = ParseZoundTagKey(tag);
            return new KeyValuePair<string, List<string>>(tagKey, GetZoundTagValuesByKey(tagKey));
        }

        public static string ParseZoundTagKey(string tag) {
            int idx = tag.IndexOf(':');
            return idx >= 0 ? tag.Substring(0, idx) : tag;
        }

        public static string ParseZoundTagValue(string tag) {
            int idx = tag.IndexOf(':');
            return idx >= 0 ? tag.Substring(idx + 1) : "";
        }

        public static void ParseZoundTagValues(string tag, List<string> result) {
            result.Clear();
            string values = ParseZoundTagValue(tag);

            int start = 0;
            for (int i = 0; i <= values.Length; i++) {
                if (i == values.Length || values[i] == ',') {
                    int length = i - start;

                    while (length > 0 && char.IsWhiteSpace(values[start])) {
                        start++;
                        length--;
                    }
                    while (length > 0 && char.IsWhiteSpace(values[start + length - 1])) {
                        length--;
                    }

                    if (length > 0)
                        result.Add(values.Substring(start, length));

                    start = i + 1;
                }
            }
        }

        public static List<string> ParseZoundTagValues(string tagKey) {
            var result = new List<string>();
            ParseZoundTagValues(tagKey, result);
            return result;
        }

        public static void GetZoundTagValuesByKey(string tagKey, List<string> result) {
            var projectTags = ZoundsProject.Instance.zoundLibrary.tags;
            var foundTag = projectTags.Find(tag => ParseZoundTagKey(tag.name) == tagKey);
            if (foundTag == null) {
                result.Clear();
            }
            else {
                ParseZoundTagValues(foundTag.name, result);
            }
        }

        public static List<string> GetZoundTagValuesByKey(string tagKey) {
            var result = new List<string>();
            GetZoundTagValuesByKey(tagKey, result);
            return result;
        }

    }

}
