using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using static Laubrary.Zounds.ZoundsWindowProperties.ZoundTabProperties;

namespace Laubrary.Zounds {

    /// <summary>
    /// Owns the filter and grouping caches for one browser tab.
    /// BrowserTab holds an instance and delegates GetFilteredZounds / EvaluateGroup / Invalidate to it.
    ///
    /// filterCache  — flat filtered+sorted list; rebuilt when dirty or when missing/played counts change.
    /// groupCache   — ordered (label, members) pairs; rebuilt on GroupBy change.
    /// prevGroupBy  — tracks last seen GroupBy so groupCache can be invalidated on change.
    /// </summary>
    internal class ZoundBrowserFilterEngine {

        internal List<Zound> filterCache = null;
        internal List<KeyValuePair<string, List<Zound>>> groupCache = null;
        private GroupBy prevGroupBy;
        private int missingZoundCount = 0;
        private int playedClipZoundCount = 0;

        internal void Invalidate() {
            filterCache = null;
            groupCache  = null;
        }

        internal void InvalidateGroup() {
            groupCache = null;
        }

        // ── Filter ────────────────────────────────────────────────────────────

        internal List<Zound> GetFilteredZounds(
            List<Zound> zoundsToDisplay,
            ZoundsWindowProperties.ZoundTabProperties tabProperties) {

            if (filterCache != null && !tabProperties.dirty) {
                int currentMissingZoundCount    = ZoundEngine.MissingZounds.Count;
                int currentPlayedClipZoundCount = 0;

                if (Application.isPlaying) {
                    var cullingGroups = ZoundEngine.CullingGroups;
                    foreach (var kvp in cullingGroups) {
                        if (kvp.Key is ClipZound && kvp.Value.Count > 0) {
                            currentPlayedClipZoundCount++;
                        }
                    }
                }
                if (currentMissingZoundCount == missingZoundCount && currentPlayedClipZoundCount == playedClipZoundCount) {
                    return filterCache;
                }
                else {
                    missingZoundCount    = currentMissingZoundCount;
                    playedClipZoundCount = currentPlayedClipZoundCount;
                }
            }

            tabProperties.dirty = false;
            groupCache = null;

            filterCache = new List<Zound>();
            if (string.IsNullOrEmpty(tabProperties.searchText)) {
                foreach (var obj in zoundsToDisplay) {
                    filterCache.Add(obj);
                }
            }
            else {
                string[] searchSplits = ObjectNames.NicifyVariableName(tabProperties.searchText).ToLower().Split(' ');
                for (int i = 0; i < zoundsToDisplay.Count; i++) {
                    var zoundName = zoundsToDisplay[i].name;
                    bool found = zoundName.ToLower().Contains(tabProperties.searchText.ToLower());
                    if (!found) {
                        found = true;
                        string nicifyLowerName = ObjectNames.NicifyVariableName(zoundName).ToLower();
                        for (int j = 0; j < searchSplits.Length; j++) {
                            if (searchSplits[j] == "") continue;
                            if (!nicifyLowerName.Contains(searchSplits[j])) {
                                found = false;
                                break;
                            }
                        }
                    }
                    if (found) filterCache.Add(zoundsToDisplay[i]);
                }
            }

            if (tabProperties.selectedFolders.Count > 0) {
                var clips = new List<AudioClip>();
                foreach (var folder in tabProperties.selectedFolders) {
                    clips.AddRange(ZoundsFilter.GetClipsAtFolder(folder));
                }
                var arr = filterCache.ToArray();
                foreach (var z in arr) {
                    if (!IsClipContainedInZound(clips, z)) filterCache.Remove(z);
                }
            }

            if (tabProperties.selectedTags.Count > 0) {
                var zoundsWithTag = new List<Zound>();
                foreach (var tagId in tabProperties.selectedTags) {
                    zoundsWithTag.AddRange(ZoundsFilter.GetZoundsByTag(tagId));
                }
                zoundsWithTag = zoundsWithTag.Distinct().ToList();
                var arr = filterCache.ToArray();
                foreach (var z in arr) {
                    if (!zoundsWithTag.Contains(z)) filterCache.Remove(z);
                }
            }

            filterCache = filterCache.Distinct().ToList();

            if (ZoundsProject.Instance.browserSettings.msOnly) {
                filterCache.RemoveAll(z => !z.mute && !z.solo);
            }

            return filterCache;
        }

        // ── Grouping ──────────────────────────────────────────────────────────

        internal List<Zound> EvaluateGroup(
            List<Zound> filteredZounds,
            ZoundsWindowProperties.ZoundTabProperties tabProperties) {

            if (groupCache == null || prevGroupBy != tabProperties.groupBy) {
                prevGroupBy = tabProperties.groupBy;
                groupCache  = new List<KeyValuePair<string, List<Zound>>>();

                if (prevGroupBy == GroupBy.None) {
                    filterCache    = null;
                    filteredZounds = GetFilteredZounds(filteredZounds, tabProperties);
                    groupCache     = new List<KeyValuePair<string, List<Zound>>>();
                }
#if ZOUNDS_CONSIDER_FOLDERS
                else if (prevGroupBy == GroupBy.Folder) {
                    var groupTemp  = new Dictionary<string, List<Zound>>();
                    var zoundsCopy = new List<Zound>(filteredZounds);
                    string[] folders = ZoundsFilter.GetFolders();
                    foreach (var folder in folders) {
                        var clips = ZoundsFilter.GetClipsAtFolder(folder);
                        var arr = zoundsCopy.ToArray();
                        foreach (var z in arr) {
                            if (IsClipContainedInZound(clips, z)) {
                                if (!groupTemp.TryGetValue(folder, out var members)) {
                                    members = new List<Zound>();
                                    groupTemp.Add(folder, members);
                                }
                                members.Add(z);
                                zoundsCopy.Remove(z);
                            }
                        }
                    }
                    if (zoundsCopy.Count > 0) {
                        if (!groupTemp.TryGetValue("[No Folder]", out var members)) {
                            members = new List<Zound>();
                            groupTemp.Add("[No Folder]", members);
                        }
                        foreach (var z in zoundsCopy) members.Add(z);
                    }
                    foreach (var key in groupTemp.Keys.OrderBy(k => k)) {
                        groupCache.Add(new KeyValuePair<string, List<Zound>>(key, groupTemp[key].Distinct().ToList()));
                    }
                    filterCache = new List<Zound>();
                    foreach (var members in groupCache) filterCache.AddRange(members.Value);
                    filteredZounds = filterCache;
                }
#endif
                else if (prevGroupBy == GroupBy.Tags) {
                    var groupTemp  = new Dictionary<string, Dictionary<int, List<Zound>>>();
                    var zoundLibrary = ZoundsProject.Instance.zoundLibrary;
                    foreach (var z in filterCache) {
                        if (z.tags == null || z.tags.Count == 0) {
                            if (!groupTemp.TryGetValue("-Untagged-", out var members)) {
                                members = new Dictionary<int, List<Zound>>();
                                groupTemp.Add("-Untagged-", members);
                            }
                            if (!members.TryGetValue(0, out var sorted)) {
                                sorted = new List<Zound>();
                                members.Add(0, sorted);
                            }
                            sorted.Add(z);
                        }
                        else {
                            foreach (var tagId in z.tags) {
                                if (zoundLibrary.TryGetTag(tagId, out var tag)) {
                                    string tagName = tag.name;
                                    var splits = tagName.Split(':');
                                    if (splits.Length > 1) tagName = splits[0];
                                    if (!groupTemp.TryGetValue(tagName, out var members)) {
                                        members = new Dictionary<int, List<Zound>>();
                                        groupTemp.Add(tagName, members);
                                    }
                                    if (!members.TryGetValue(tagId, out var sorted)) {
                                        sorted = new List<Zound>();
                                        members.Add(tagId, sorted);
                                    }
                                    sorted.Add(z);
                                }
                            }
                        }
                    }
                    foreach (var key in groupTemp.Keys.OrderBy(k => k)) {
                        var sortedMembers = new List<Zound>();
                        foreach (var kvp in groupTemp[key].Distinct().ToList()) {
                            sortedMembers.AddRange(kvp.Value);
                        }
                        groupCache.Add(new KeyValuePair<string, List<Zound>>(key, sortedMembers));
                    }
                    filterCache = new List<Zound>();
                    foreach (var members in groupCache) filterCache.AddRange(members.Value);
                    filteredZounds = filterCache;
                }

                else if (prevGroupBy == GroupBy.References) {
                    var zoundLibrary   = ZoundsProject.Instance.zoundLibrary;
                    var referenceCount = new Dictionary<Zound, int>();
                    foreach (var z in filterCache) {
                        if (!referenceCount.ContainsKey(z)) referenceCount.Add(z, 0);
                    }
                    foreach (var z in referenceCount.Keys.ToArray()) {
                        zoundLibrary.ForEachZound(otherZound => {
                            if (otherZound.HasDirectDependency(z) || otherZound.HasNestedDependency(z)) {
                                referenceCount[z]++;
                            }
                        });
                    }
                    foreach (var count in referenceCount.Values.Distinct().OrderByDescending(c => c)) {
                        var zoundMembers = referenceCount
                            .Where(kvp => kvp.Value == count)
                            .Select(kvp => kvp.Key)
                            .Distinct().ToList();
                        groupCache.Add(new KeyValuePair<string, List<Zound>>(count.ToString(), zoundMembers));
                    }
                    filterCache = new List<Zound>();
                    foreach (var members in groupCache) filterCache.AddRange(members.Value);
                    filteredZounds = filterCache;
                }

                else if (prevGroupBy == GroupBy.MixerGroup) {
                    var zoundsProject        = ZoundsProject.Instance;
                    var zoundRoutings        = zoundsProject.zoundRoutings;
                    var zoundsByMixerGroupName = new Dictionary<string, List<Zound>>();
                    var unroutedZounds       = new List<Zound>();

                    foreach (var z in filterCache) {
                        if (z.manuallySetMixerGroupRef != null && z.editor_hasManuallySetRouting) {
                            string mixerGroupName = z.manuallySetMixerGroupRef.SubObjectName;
                            if (!zoundsByMixerGroupName.ContainsKey(mixerGroupName))
                                zoundsByMixerGroupName.Add(mixerGroupName, new List<Zound>());
                            zoundsByMixerGroupName[mixerGroupName].Add(z);
                            continue;
                        }
                        var matchingRule = zoundRoutings.FindMatchingRoutingRule(z);
                        if (matchingRule != null && matchingRule.mixerGroupRef != null) {
                            string mixerGroupName = matchingRule.mixerGroupRef.SubObjectName;
                            if (!zoundsByMixerGroupName.ContainsKey(mixerGroupName))
                                zoundsByMixerGroupName.Add(mixerGroupName, new List<Zound>());
                            zoundsByMixerGroupName[mixerGroupName].Add(z);
                        }
                        else {
                            unroutedZounds.Add(z);
                        }
                    }

                    foreach (var mixerGroupName in zoundsByMixerGroupName.Keys.OrderBy(n => n)) {
                        groupCache.Add(new KeyValuePair<string, List<Zound>>(
                            mixerGroupName,
                            zoundsByMixerGroupName[mixerGroupName].OrderBy(z => z.name).ToList()));
                    }
                    groupCache.Add(new KeyValuePair<string, List<Zound>>("-Unrouted-", unroutedZounds));
                    filterCache = new List<Zound>();
                    foreach (var members in groupCache) filterCache.AddRange(members.Value);
                    filteredZounds = filterCache;
                }

                else if (prevGroupBy == GroupBy.Type) {
                    var audioClipList = new List<Zound>();
                    var klipList      = new List<Zound>();
                    var missingList   = new List<Zound>();
                    var zequenceList  = new List<Zound>();

                    foreach (var z in filterCache) {
                        if      (z is ClipZound)  audioClipList.Add(z);
                        else if (z is Klip)       klipList.Add(z);
                        else if (z is Zequence)   zequenceList.Add(z);
                        else                      missingList.Add(z);
                    }

                    if (audioClipList.Count > 0) groupCache.Add(new KeyValuePair<string, List<Zound>>("AudioClip", audioClipList));
                    if (klipList.Count > 0)      groupCache.Add(new KeyValuePair<string, List<Zound>>("Klip",      klipList));
                    if (zequenceList.Count > 0)  groupCache.Add(new KeyValuePair<string, List<Zound>>("Zequence",  zequenceList));
                    if (missingList.Count > 0)   groupCache.Add(new KeyValuePair<string, List<Zound>>("Missing",   missingList));
                    filterCache = new List<Zound>();
                    foreach (var members in groupCache) filterCache.AddRange(members.Value);
                    filteredZounds = filterCache;
                }
            }

            return filteredZounds;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static bool IsClipContainedInZound(List<AudioClip> clips, Zound z) {
            if (z is Klip klip) {
                var clip = klip.GetAudioClipReference().editorAsset as AudioClip;
                return clip != null && clips.Contains(clip);
            }
            if (z is Zequence zequence) {
                foreach (var entry in zequence.zoundEntries) {
                    if (zequence.TryGetEntryZound(entry, out var childZound)) {
                        if (IsClipContainedInZound(clips, childZound)) return true;
                    }
                }
                return false;
            }
            return false;
        }
    }
}
