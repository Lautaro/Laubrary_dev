using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Audio;

namespace Laubrary.Zounds {

    public class  ZoundMixerCache {

        private static Dictionary<AssetReference, AudioMixerGroup> mixerGroupsCache = new Dictionary<AssetReference, AudioMixerGroup>();

        public static void Clear() {
            mixerGroupsCache.Clear();
        }

        public static AudioMixerGroup GetMixerGroup(AssetReference mixerRef) {
            if (Application.isPlaying) {
                if (!mixerGroupsCache.TryGetValue(mixerRef, out var mixerGroup)) {
                    if (mixerRef.IsValid()) {
                        mixerGroup = mixerRef.Asset as AudioMixerGroup;
                        mixerGroupsCache.Add(mixerRef, mixerGroup);
                    }
                    else {
                        if (mixerRef.RuntimeKeyIsValid()) {
                        //if (true) {
                            var handle = mixerRef.LoadAssetAsync<AudioMixerGroup>();
                            mixerGroup = handle.WaitForCompletion();
                            mixerGroupsCache.Add(mixerRef, mixerGroup);
                        }
                        else {
                            Debug.LogError("Invalid AudioMixerGroup asset reference.");
                            return null;
                        }
                    }
                }
                return mixerGroup;
            }
            else {
#if UNITY_EDITOR
                var audioMixer = mixerRef.editorAsset as AudioMixer;
                if (audioMixer != null) {
                    var mixerGroups = audioMixer.FindMatchingGroups(mixerRef.SubObjectName);
                    if (mixerGroups != null && mixerGroups.Length > 0) return mixerGroups[0];
                }
#endif
                return null;
            }
        }

    }

    [System.Serializable]
    public class ZoundRoutings {

        [System.Serializable]
        public class Condition {
            public enum ConditionType {
                Tag = 0,
#if ZOUNDS_CONSIDER_FOLDERS
                Folder = 1
#endif
            }
            public ConditionType type;
            public string name; // can be either folder name or tag name.
        }

        [System.Serializable]
        public class Rule {

            public List<Condition> conditions = new List<Condition>();
            public AssetReference mixerGroupRef;

            public AudioMixerGroup mixerGroup {
                get {
                    return ZoundMixerCache.GetMixerGroup(mixerGroupRef);
                }
            }
        }

        public List<Rule> rules = new List<Rule>();

        public AudioMixerGroup GetRouting(Zound zound
#if ZOUNDS_CONSIDER_FOLDERS
            , AudioClip clip, string clipPath
#endif
            ) {

            if (zound.manuallySetMixerGroupRef != null && zound.manuallySetMixerGroupRef.RuntimeKeyIsValid()) {
                if (Application.isPlaying) {
                    var manualMixerGroup = ZoundMixerCache.GetMixerGroup(zound.manuallySetMixerGroupRef);
                    if (manualMixerGroup == null) {
                        Debug.LogError(zound.name + ": Mixer group is manually set, but is currently invalid.");
                    }
                    else {
                        return manualMixerGroup;
                    }
                }
                else {
#if UNITY_EDITOR
                    var mixerRef = zound.manuallySetMixerGroupRef;
                    if (mixerRef != null) {
                        var audioMixer = mixerRef.editorAsset as AudioMixer;
                        if (audioMixer != null) {
                            var mixerGroups = audioMixer.FindMatchingGroups(mixerRef.SubObjectName);
                            if (mixerGroups != null && mixerGroups.Length > 0) return mixerGroups[0];
                        }
                    }
#endif
                }
            }

            var matchingRule = FindMatchingRoutingRule(zound
#if ZOUNDS_CONSIDER_FOLDERS
                , clip, clipPath
#endif
                );

            if (matchingRule != null) {
                return matchingRule.mixerGroup;
            }

            return null;
        }

        public Rule FindMatchingRoutingRule(Zound zound
#if ZOUNDS_CONSIDER_FOLDERS
            , AudioClip clip, string clipPath
#endif
        ) {
            Rule foundKeyTag = null; // this is a "weak" find, since tag with exact value is prefered.
            foreach (var set in rules) {
                foreach (var rule in set.conditions) {
#if ZOUNDS_CONSIDER_FOLDERS
                    if (ReferenceEquals(foundKeyTag, null) && rule.type == Condition.ConditionType.Folder) {
                        if (clip == null) continue;
                        if (!ZoundDictionary.runtimeClipFolders.TryGetValue(clip, out var clipFolder)) {
                            clipFolder = GetFolderFromClipPath(clipPath);
                            if (!string.IsNullOrEmpty(clipFolder)) {
                                ZoundDictionary.runtimeClipFolders.Add(clip, clipFolder);
                            }
                        }
                        if (clipFolder == rule.name) {
                            return set.mixerGroup;
                        }
                    }
                    else 
#endif
                    if (rule.type == Condition.ConditionType.Tag) {
                        var zoundLibrary = ZoundsProject.Instance.zoundLibrary;
                        foreach (var tagId in zound.tags) {
                            if (zoundLibrary.TryGetTag(tagId, out var tag)) {
                                if (tag.name == rule.name) {
                                    return set;
                                }
                                else if (ReferenceEquals(foundKeyTag, null)) {
                                    var nameSplit = tag.name.Split(':');
                                    if (nameSplit.Length > 1) {
                                        if (nameSplit[0] == rule.name) {
                                            foundKeyTag = set;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            return foundKeyTag;
        }

        public static string GetFolderFromClipPath(string clipPath) {
            if (string.IsNullOrEmpty(clipPath)) return null;

            var projectSettings = ZoundsProject.Instance.projectSettings;
            string parentFolderPath = projectSettings.sourcesFolderPath;
            if (!clipPath.StartsWith(parentFolderPath)) {
                parentFolderPath = projectSettings.workFolderPath;
                if (!clipPath.StartsWith(parentFolderPath)) {
                    parentFolderPath = projectSettings.zoundFilesFolderPath;
                    if (!clipPath.StartsWith(parentFolderPath)) {
                        parentFolderPath = projectSettings.libraryFolderPath;
                        if (!clipPath.StartsWith(parentFolderPath)) {
                            return null;
                        }
                    }
                }
            }
            string folderPath = System.IO.Path.GetDirectoryName(clipPath);
            folderPath = folderPath.Substring(parentFolderPath.Length, folderPath.Length - parentFolderPath.Length);
            folderPath = folderPath.Replace('\\', '/');
            return folderPath;
        }

    }

}
