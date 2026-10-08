using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Laubrary.Zounds {

    /// <summary>
    /// What the composite (Zequence) editor does to a sound, independent of how it is drawn: duplicating, removing and
    /// converting entries (shared ↔ local), adding entries, the editor-width duration maths, and starting a play of the
    /// whole sound or of one entry. Moved out of CompositeZoundEditorWindow unchanged so the IMGUI window and its UI Toolkit
    /// twin (T-0469) edit a Zequence the same way; each window keeps only its own view bookkeeping (envelope caches, focus).
    /// </summary>
    internal static class CompositeZoundEditing {

        public static void DuplicateEntry(CompositeZound parentZound, int entryIndexToDuplicate) {
            ZoundsWindow.ModifyZoundsProject("duplicate zound entry", () => {
                var serialized = JsonUtility.ToJson(parentZound.zoundEntries[entryIndexToDuplicate]);
                var duplicated = JsonUtility.FromJson<CompositeZound.ZoundEntry>(serialized);
                if (duplicated.local && parentZound.TryGetEntryZound(duplicated, out var referencedZound)) {
                    if (referencedZound is Klip referencedKlip) {
                        var duplicatedKlip = new Klip(ZoundLibrary.GetUniqueZoundId(), referencedKlip);
                        duplicatedKlip.parentId = parentZound.id;
                        duplicatedKlip.tags.Clear();
                        duplicated.zoundId = duplicatedKlip.id;
                        parentZound.localKlips.Add(duplicatedKlip);

                        // If the source had a render, it means we should render this duplicate immediately
                        // so it is "true" to its inherited settings from the start.
                        if (referencedKlip.HasActiveEdits() && ZoundsProject.Instance.projectSettings.editorStyle.autoRender) {
                            KlipEditorWindow.RenderToAudioClip(duplicatedKlip, true);

                            // Force-save to ensure the renderedClipRef and paths are written to JSON immediately.
                            // This ensures that when the Zequence editor repaints, it finds the new file.
                            ZoundsWindow.SaveToJSON();
                        }
                    }
                    else if (referencedZound is Zequence referencedZequence) {
                        var duplicatedZequence = new Zequence(ZoundLibrary.GetUniqueZoundId(), referencedZequence);
                        duplicatedZequence.parentId = parentZound.id;
                        duplicatedZequence.tags.Clear();
                        duplicated.zoundId = duplicatedZequence.id;
                        parentZound.localZequences.Add(new CompositeZound.LocalZequence(duplicatedZequence));
                    }
                }
                parentZound.zoundEntries.Insert(entryIndexToDuplicate + 1, duplicated);
            });
        }

        public static void RemoveEntry(CompositeZound parentZound, int entryIndexToRemove) {
            ZoundsWindow.ModifyZoundsProject("remove zound entry", () => {
                var entryToRemove = parentZound.zoundEntries[entryIndexToRemove];
                // Pieces split from one sound share its local Klip (T-0565): it goes only with the last track using it.
                if (entryToRemove.local && !SharedLocally(parentZound, entryToRemove)) {
                    int klipIndex = parentZound.localKlips.FindIndex(k => k.id == entryToRemove.zoundId);
                    if (klipIndex >= 0) {
                        parentZound.localKlips.RemoveAt(klipIndex);
                    }
                    int randomizerIndex = parentZound.localZequences.FindIndex(lr => lr.zequence.id == entryToRemove.zoundId);
                    if (randomizerIndex >= 0) {
                        parentZound.localZequences.RemoveAt(randomizerIndex);
                    }
                }
                parentZound.zoundEntries.RemoveAt(entryIndexToRemove);
            });
        }

        /// <summary>Shared → local (a private copy with the entry's overrides folded in), or local → shared.</summary>
        public static void ConvertEntry(CompositeZound parentZound, int entryIndexToConvert) {
            ZoundsProject zoundsProject = ZoundsProject.Instance;
            ZoundsWindow.ModifyZoundsProject("convert zound entry", () => {
                var entryToConvert = parentZound.zoundEntries[entryIndexToConvert];
                if (entryToConvert.local && SharedLocally(parentZound, entryToConvert)
                    && parentZound.TryGetEntryZound(entryToConvert, out var sharedLocal) && sharedLocal is Klip sharedKlip) {
                    // Another piece still plays this local Klip (T-0565): this track takes a library copy, the others keep theirs.
                    var copy = new Klip(ZoundLibrary.GetUniqueZoundId(), sharedKlip);
                    copy.parentId = 0; copy.name = ZoundDictionary.EnsureUniqueZoundName(sharedKlip.name);
                    zoundsProject.zoundLibrary.klips.Add(copy);
                    entryToConvert.zoundId = copy.id;
                }
                else if (entryToConvert.local) {
                    int localZoundId = entryToConvert.zoundId;
                    if (parentZound.TryGetEntryZound(entryToConvert, out var zoundToConvert)) {
                        zoundToConvert.name = ZoundDictionary.EnsureUniqueZoundName(zoundToConvert.name);
                        if (zoundToConvert is Klip klipToConvert) {
                            klipToConvert.parentId = 0;
                            var zoundLibrary = zoundsProject.zoundLibrary;
                            if (klipToConvert.originalId != 0 && zoundLibrary.FindZound(z => z.id == klipToConvert.originalId) != null) {
                                entryToConvert.zoundId = klipToConvert.originalId;
                            }
                            else {
                                zoundLibrary.klips.Add(klipToConvert);
                            }
                        }
                        else if (zoundToConvert is Zequence zequenceToConvert) {
                            zequenceToConvert.parentId = 0;
                            zequenceToConvert.masterVolumeEnvelope = entryToConvert.volumeEnvelope.DeepCopy();
                            entryToConvert.volumeEnvelope = new Envelope(1, 1);
                            var zoundLibrary = zoundsProject.zoundLibrary;
                            if (zequenceToConvert.originalId != 0 && zoundLibrary.FindZound(z => z.id == zequenceToConvert.originalId) != null) {
                                entryToConvert.zoundId = zequenceToConvert.originalId;
                            }
                            else {
                                zoundLibrary.zequences.Add(zequenceToConvert);
                            }
                        }
                    }
                    parentZound.localKlips.RemoveAll(k => k.id == localZoundId);
                    parentZound.localZequences.RemoveAll(lr => lr.zequence.id == localZoundId);
                }
                else {
                    if (parentZound.TryGetEntryZound(entryToConvert, out var zoundToConvert)) {
                        if (zoundToConvert is Klip klipToConvert) {
                            var convertedKlip = new Klip(ZoundLibrary.GetUniqueZoundId(), klipToConvert);
                            BreakEntryAsLocal(parentZound, entryToConvert, klipToConvert, convertedKlip);
                            parentZound.localKlips.Add(convertedKlip);
                        }
                        else if (zoundToConvert is Zequence zequenceToConvert) {
                            foreach (var childEntry in zequenceToConvert.zoundEntries) {
                                if (!childEntry.local) continue;
                                if (zequenceToConvert.TryGetEntryZound(childEntry, out var childZound)) {
                                    if (childZound is Zequence) {
                                        EditorUtility.DisplayDialog("Can't Break into Local Zequence",
                                            string.Format("Can't break shared zound '{0}', as it contains a local zequence track '{1}'. Nested local zequence is not supported.", zequenceToConvert.name, childZound.name), "Close");
                                        return;
                                    }
                                }
                            }
                            var convertedZequence = new Zequence(ZoundLibrary.GetUniqueZoundId(), zequenceToConvert);
                            BreakEntryAsLocal(parentZound, entryToConvert, zequenceToConvert, convertedZequence);
                            entryToConvert.volumeEnvelope = convertedZequence.masterVolumeEnvelope.DeepCopy();
                            parentZound.localZequences.Add(new CompositeZound.LocalZequence(convertedZequence));
                        }
                    }
                }
                entryToConvert.local = !entryToConvert.local;
            });
            ZoundsAssetPostProcessor.RefreshAudioClipsCache();
            ZoundsWindow.RepaintWindow();
        }

        /// <summary>Whether another track of <paramref name="parent"/> plays the same local sound as <paramref name="entry"/>
        /// (pieces split from one sound share it, T-0565).</summary>
        public static bool SharedLocally(CompositeZound parent, CompositeZound.ZoundEntry entry) {
            if (!entry.local) return false;
            foreach (var e in parent.zoundEntries) if (!ReferenceEquals(e, entry) && e.local && e.zoundId == entry.zoundId) return true;
            return false;
        }

        static void BreakEntryAsLocal(CompositeZound parentZound, CompositeZound.ZoundEntry entryToConvert, Zound zoundToConvert, Zound convertedZound) {
            convertedZound.originalId = zoundToConvert.id;
            convertedZound.parentId = parentZound.id;
            // Tags describe a zound to the rest of the project; a local copy is known only to its parent.
            convertedZound.tags.Clear();

            if (entryToConvert.overrideVolume) {
                convertedZound.minVolume = entryToConvert.volume;
                convertedZound.maxVolume = entryToConvert.volume;
            }
            else {
                convertedZound.minVolume *= entryToConvert.volume;
                convertedZound.maxVolume *= entryToConvert.volume;
            }
            if (entryToConvert.overridePitch) {
                convertedZound.minPitch = entryToConvert.pitch;
                convertedZound.maxPitch = entryToConvert.pitch;
            }
            else {
                convertedZound.minPitch *= entryToConvert.pitch;
                convertedZound.maxPitch *= entryToConvert.pitch;
            }
            if (entryToConvert.overrideChance) {
                convertedZound.chance = entryToConvert.chance;
            }
            else {
                convertedZound.chance *= entryToConvert.chance;
            }

            entryToConvert.zoundId = convertedZound.id;
            entryToConvert.overrideVolume = false;
            entryToConvert.overridePitch = false;
            entryToConvert.overrideChance = false;
            entryToConvert.volume = 1f;
            entryToConvert.pitch = 1f;
            entryToConvert.chance = 1f;
        }

        /// <summary>"+ Shared Zound": the picker over every library sound that is not this one and does not contain it
        /// (2026-10-08, replacing the flat menu popup); the picker keeps its own search and place.</summary>
        public static void AddNewEntryFromExisting(CompositeZound parentZound, System.Action<Zound> onChosen, EditorWindow previewOwner) {
            Uitk.ZoundPickerWindowTK.Open(Uitk.ZoundPickerRequests.SharedZounds(parentZound, onChosen, previewOwner));
        }

        /// <summary>Adds an entry for <paramref name="zound"/> and widens the editor timeline if it now runs longer.</summary>
        public static void AddNewZoundEntry(CompositeZound target, CompositeZound parentZound, Zound zound, bool local, bool autoDuration) {
            ZoundsWindow.ModifyZoundsProject("add local zound entry", () => {
                var newEntry = new CompositeZound.ZoundEntry();
                newEntry.zoundId = zound.id;
                newEntry.local = local;
                parentZound.zoundEntries.Add(newEntry);
                RecalculateMaxDuration(target, autoDuration);
            });
        }

        public static void RecalculateMaxDuration(CompositeZound target, bool autoDuration) {
            float max = CalculateCompositeDuration(target, 1f);
            if (max > target.editor_maxDuration) {
                target.editor_maxDuration = max;
                EditorUtility.SetDirty(ZoundsProject.Instance);
            }
            if (autoDuration) AutoApplyDuration(target);
        }

        /// <summary>Sets editor_maxDuration to exactly the computed content duration.</summary>
        public static void AutoApplyDuration(CompositeZound target) {
            float exact = CalculateCompositeDuration(target, 1f);
            if (!Mathf.Approximately(exact, target.editor_maxDuration)) {
                target.editor_maxDuration = exact;
                EditorUtility.SetDirty(ZoundsProject.Instance);
            }
        }

        public static float CalculateCompositeDuration(CompositeZound compositeZound, float parentPitch) {
            float max = 0f;
            foreach (var entry in compositeZound.zoundEntries) {
                if (!compositeZound.TryGetEntryZound(entry, out var zound)) continue;
                if (zound is CompositeZound cz && ZequenceHandler.CheckRecursiveness(cz, compositeZound)) {
                    Debug.LogError(compositeZound.name + " is contained recursively in " + cz.name);
                    continue;
                }
                float effectiveDuration = GetEntryDuration(compositeZound, entry, parentPitch) + (entry.delay / parentPitch);
                if (effectiveDuration > max) max = effectiveDuration;
            }
            return max;
        }

        public static float GetEntryDuration(CompositeZound parentZound, CompositeZound.ZoundEntry entry, float parentPitch) {
            if (!parentZound.TryGetEntryZound(entry, out var zound)) return 0f;

            float effectivePitch = entry.pitch;
            if (!entry.overridePitch) {
                // no more middle values
                effectivePitch *= zound.minPitch;
            }
            effectivePitch *= parentPitch;

            float zoundDuration;
            if (zound is Klip klip) {
                // What a play actually lasts (T-0502): the original audio through the live chain -- trim or the track's own
                // excerpt, pitch and time curves, stretch -- worked out exactly as the play works it out. A rendered file is
                // never in the play's path any more, so it says nothing about the length.
                var excerpt = entry.ownTrim && entry.trimEnd > entry.trimStart ? Dsp.ZoundSapPlayback.Excerpt.Of(entry.trimStart, entry.trimEnd) : default;
                if (Dsp.ZoundSapPlayback.TryGetPlayLength(klip, excerpt, out float playLength))
                    return playLength / Mathf.Max(effectivePitch, 0.01f);
                // The source cannot be read: the trimmed length as a placeholder for the width.
                if (excerpt.on) zoundDuration = (excerpt.end - excerpt.start) / effectivePitch;
                else if (klip.trimEnabled) {
                    zoundDuration = (klip.trimEnd - klip.trimStart) / effectivePitch;
                }
                else if (klip.GetAudioClipReference().editorAsset is AudioClip audioClip) {
                    zoundDuration = audioClip.length / effectivePitch;
                }
                else {
                    zoundDuration = 0f;
                }
            }
            else if (zound is CompositeZound composite) {
                zoundDuration = CalculateCompositeDuration(composite, effectivePitch);
            }
            else {
                zoundDuration = 0f;
            }
            return zoundDuration;
        }

        /// <summary>The window's Play: the whole sound, at its average settings, bypassing global solo when it is local.</summary>
        public static ZoundToken SimulatePlay(CompositeZound target, bool isLocalZound, EditorWindow owner, object control) {
            return ZoundPreviewPlayback.Play(owner, target, new ZoundArgs() {
                startImmediately = true,
                delay = 0f,
                volumeOverride = -1f,
                pitchOverride = -1f,
                chanceOverride = -1f,
                useFixedAverageValues = true,
                bypassGlobalSolo = isLocalZound,
                ignoreCooldown = true
            }, control, false);
        }

        /// <summary>Whether this entry is currently sounding from a play started by its own play button.</summary>
        public static bool IsEntryPlaying(Dictionary<CompositeZound.ZoundEntry, ZoundToken> entryTokens, CompositeZound.ZoundEntry entry)
            => entryTokens != null && entryTokens.TryGetValue(entry, out var entryToken) && entryToken.TryGetEntryToken(entry, out var childToken) && childToken.state != ZoundToken.State.Killed;

        /// <summary>An entry's play button (or a click on its waveform): stop it if it is sounding, else play the whole
        /// sound soloed to this entry.</summary>
        public static void ToggleEntryPlay(CompositeZound target, ref Dictionary<CompositeZound.ZoundEntry, ZoundToken> entryTokens, CompositeZound.ZoundEntry entry, EditorWindow owner) {
            if (ZoundPreviewPlayback.IsLoopPlaying(owner, entry)) {
                ZoundPreviewPlayback.StopControl(owner, entry);
                return;
            }
            if (entryTokens == null) entryTokens = new Dictionary<CompositeZound.ZoundEntry, ZoundToken>();
            var token = ZoundPreviewPlayback.Play(owner, target, new ZoundArgs() {
                startImmediately = true,
                delay = 0f,
                volumeOverride = -1f,
                pitchOverride = -1f,
                chanceOverride = -1f,
                useFixedAverageValues = true,
                soloOverride = entry,
                ignoreCooldown = true
            }, entry);
            entryTokens[entry] = token;
        }
    }
}
