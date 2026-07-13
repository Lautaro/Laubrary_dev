using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Laubrary.Zounds {
    public partial class CompositeZoundEditorWindow<TZound, TSelf> : BaseZoundEditorWindow<TZound, TSelf> where TZound : CompositeZound {
        
        private void DrawEntryGroup(Rect contentRect, CompositeZound parentZound, CompositeZound.ZoundEntry entry, CompositeZound compositeZound, int entryIndex, float entryDuration, out bool toBeRemoved, out bool toBeDuplicated, out bool toBeConverted) {
            var leftSection = new Rect(contentRect.x, contentRect.y, leftSectionWidth, contentRect.height);
            var rightSection = new Rect(leftSection.xMax + 5f, contentRect.y, contentRect.width - leftSection.width - 5f, contentRect.height);
            var flashRect = contentRect;
            flashRect.height = entryHeight;
            DrawEntryGroupLeftSection(leftSection, entry, compositeZound, entryDuration, contentRect);
            DrawEntryGroupRightSection(flashRect, rightSection, parentZound, entry, compositeZound, entryIndex, targetZound.minPitch, entryDuration, out toBeRemoved, out toBeDuplicated, out toBeConverted);

            if (entry.editor_foldoutExpanded) {
                float currentY = contentRect.y + groupHeaderHeight;
                if (compositeZound is Zequence zequence) {
                    float lineHeight = EditorGUIUtility.singleLineHeight;
                    float masterHeight = entry.volumeEnvelope.enabled ? lineHeight * 4f : lineHeight;
                    currentY += masterHeight;
                }
                bool darkerBG = false;
                int entryIndexToRemove = -1;
                int entryIndexToDuplicate = -1;
                int entryIndexToConvert = -1;

                for (int i = 0; i < compositeZound.zoundEntries.Count; i++) {
                    var childEntry = compositeZound.zoundEntries[i];
                    bool isZoundFound = compositeZound.TryGetEntryZound(childEntry, out var childZound);

                    Rect entryRect = new Rect(contentRect.x, currentY, contentRect.width, entryHeight);

                    var color = darkerBG ? new Color(0.25f, 0.25f, 0.25f, 0.4f) : new Color(0.45f, 0.45f, 0.45f, 0.4f);
                    var prevGUIColor = GUI.color;
                    GUI.color = color;
                    var bgRect = entryRect;
                    bgRect.x += groupEntryLeftOffset - 2f;
                    bgRect.width -= groupEntryLeftOffset - 4f;
                    GUI.DrawTexture(bgRect, EditorGUIUtility.whiteTexture);
                    GUI.color = prevGUIColor;
                    if (isZoundFound) {
                        // no more middle values
                        float parentPitch = targetZound.minPitch * compositeZound.minPitch;
                        float globalDelay = entry.delay / parentPitch;
                        DrawEntry(entryRect, compositeZound, childZound, childEntry, i, parentPitch, globalDelay, out bool childToBeRemoved, out bool childToBeDuplicated, out bool childToBeConverted);
                        if (childToBeRemoved) {
                            entryIndexToRemove = i;
                        }
                        if (childToBeDuplicated) {
                            entryIndexToDuplicate = i;
                        }
                        if (childToBeConverted) {
                            entryIndexToConvert = i;
                        }
                    }
                    darkerBG = !darkerBG;
                    currentY += entryHeight + 4f;
                }

                if (entryIndexToRemove >= 0) {
                    RemoveEntry(compositeZound, entryIndexToRemove);
                }
                if (entryIndexToDuplicate >= 0) {
                    DuplicateEntry(compositeZound, entryIndexToDuplicate);
                }
                if (entryIndexToConvert >= 0) {
                    ConvertEntry(compositeZound, entryIndexToConvert);
                }
            }
        
            
        }

        protected virtual void DrawEntryGroupLeftSection(Rect leftSection, CompositeZound.ZoundEntry entry, CompositeZound compositeZound, float entryDuration, Rect contentRect) {
            var zoundsProject = ZoundsProject.Instance;
            float lineHeight = EditorGUIUtility.singleLineHeight;
            float currentY = leftSection.y;

            float playButtonWidth = 18f;

            var labelRect = new Rect(leftSection.x, currentY, (contentRect.width - playButtonWidth) * 0.8f, lineHeight);
            if (entry.editor_isRenaming) {
                labelRect.width = 14f;
            }
            EditorGUI.BeginChangeCheck();
            var expanded = EditorGUI.BeginFoldoutHeaderGroup(labelRect, entry.editor_foldoutExpanded, compositeZound.name);
            if (EditorGUI.EndChangeCheck()) {
                ZoundsWindow.ModifyZoundsProject("toggle foldout expand", () => {
                    entry.editor_foldoutExpanded = expanded;
                });
            }
            EditorGUI.EndFoldoutHeaderGroup();

            if (entry.editor_isRenaming) {
                var renameRect = new Rect(labelRect.xMax, currentY, leftSection.width - labelRect.width, labelRect.height);
                EditorGUI.BeginChangeCheck();
                var newName = EditorGUI.TextField(renameRect, compositeZound.name);
                if (EditorGUI.EndChangeCheck()) {
                    newName = ZoundDictionary.EnsureUniqueZoundName(newName);
                    ZoundsWindow.ModifyZoundsProject("change local zequence name", () => {
                        compositeZound.name = newName;
                    });
                }
            }

            var playButtonRect = new Rect(contentRect.xMax - playButtonWidth, currentY, playButtonWidth, lineHeight);
            bool isPlaying = entryTokens != null && entryTokens.TryGetValue(entry, out var entryToken) && entryToken.TryGetEntryToken(entry, out var childToken) && childToken.state != ZoundToken.State.Killed;
            if (ZUI.Button(playButtonRect, isPlaying ? label_stopEntry : label_playEntry, ZUI.Style.RichButton, isPlaying ? ZUI.Tint.Confirm : null)) {
                if (isPlaying) {
                    entryTokens[entry].Kill();
                }
                else {
                    if (entryTokens == null) entryTokens = new Dictionary<CompositeZound.ZoundEntry, ZoundToken>();
                    var token = ZoundEngine.PlayZound(targetZound, new ZoundArgs() {
                        startImmediately = true,
                        delay = 0f,
                        volumeOverride = -1f,
                        pitchOverride = -1f,
                        chanceOverride = -1f,
                        useFixedAverageValues = true,
                        soloOverride = entry,
                        ignoreCooldown = true
                    });
                    if (entryTokens.ContainsKey(entry)) {
                        entryTokens[entry] = token;
                    }
                    else {
                        entryTokens.Add(entry, token);
                    }
                }
            }

            currentY += lineHeight + 2f;
            float xOffset = 0f;

            if (compositeZound.mode == CompositeZound.Mode.Randomizer) {
                xOffset += groupEntryLeftOffset;
                var noPlayRect = new Rect(leftSection.x + xOffset, currentY, 22f, 20f);
                xOffset += noPlayRect.width + 2f;

                var noPlayWeight = EditorGUI.IntField(noPlayRect, compositeZound.noPlayWeight);
                if (noPlayWeight != compositeZound.noPlayWeight) {
                    ZoundsWindow.ModifyZoundsProject("change local zequence no-play weight", () => {
                        compositeZound.noPlayWeight = noPlayWeight;
                    });
                }
            }

            var modeRect = new Rect(leftSection.x + xOffset, currentY + 1f, leftSection.width - xOffset, 20f);
            EditorGUI.BeginChangeCheck();
            var newMode = (CompositeZound.Mode)EditorGUI.EnumPopup(modeRect, compositeZound.mode);
            if (EditorGUI.EndChangeCheck()) {
                ZoundsWindow.ModifyZoundsProject("change local zequence mode", () => {
                    compositeZound.mode = newMode;
                });
            }

            currentY += 22f;

            var durationRect = new Rect(leftSection.x, currentY, leftSection.width * 0.75f, lineHeight);
            string durationString = entryDuration.ToString("0.00") + " sec";
            if (entry.delay > 0f) {
                durationString += " (" + (entryDuration + entry.delay).ToString("0.00") + " sec)";
            }
            EditorGUI.LabelField(durationRect, durationString, durationTextStyle);

        }

        private void DrawEntryGroupRightSection(Rect flashRect, Rect rightSection, CompositeZound parentZound, CompositeZound.ZoundEntry entry, CompositeZound compositeZound, int entryIndex, float parentPitch, float entryDuration, out bool toBeRemoved, out bool toBeDuplicated, out bool toBeConverted) {
            var zoundsProject = ZoundsProject.Instance;
            var editorStyle = zoundsProject.projectSettings.editorStyle;
            float currentY = rightSection.y;
            float lineHeight = EditorGUIUtility.singleLineHeight;

            toBeRemoved = toBeDuplicated = toBeConverted = false;

            var inspectorRect = new Rect(rightSection.x, currentY, rightSection.width, lineHeight);
            localZequenceInspector.DrawSimple(inspectorRect, compositeZound, true, false, false);

            currentY += lineHeight + 2f;

            var renameButtonRect = new Rect(rightSection.x, currentY, 60f, 20f);
            if (ZUI.Button(renameButtonRect, entry.editor_isRenaming ? "Done" : "Rename", ZUI.Style.RichButton)) {
                entry.editor_isRenaming = !entry.editor_isRenaming;
                EditorUtility.SetDirty(zoundsProject);
                if (!entry.editor_isRenaming) {
                    GUI.FocusControl(null);
                }
            }

            float buttonWidth = (rightSection.width - renameButtonRect.width - 2f) / 7f - 2f; // 7f for 7 buttons
            var duplicateRect = new Rect(renameButtonRect.xMax + 2f, currentY, buttonWidth, 20f);
            var removeRect = new Rect(duplicateRect.xMax + 2f, duplicateRect.y, duplicateRect.width, duplicateRect.height);
            var muteRect = new Rect(removeRect.xMax + 2f, duplicateRect.y, duplicateRect.width, duplicateRect.height);
            var soloRect = new Rect(muteRect.xMax + 2f, duplicateRect.y, duplicateRect.width, duplicateRect.height);
            var conversionRect = new Rect(soloRect.xMax + 2f, duplicateRect.y, duplicateRect.width, duplicateRect.height);
            var reorderUpRect = new Rect(conversionRect.xMax + 2f, duplicateRect.y, duplicateRect.width, duplicateRect.height);
            var reorderDownRect = new Rect(reorderUpRect.xMax + 2f, duplicateRect.y, duplicateRect.width, duplicateRect.height);

            toBeDuplicated = false;
            if (ZUI.Button(duplicateRect, icon_duplicateEntry, ZUI.Style.RichButton, ZUICornerMask.Left)) {
                toBeDuplicated = true;
            }

            toBeRemoved = false;
            if (ZUI.Button(removeRect, icon_removeEntry, ZUI.Style.RichButton, ZUI.Tint.Danger, ZUICornerMask.Right)) {
                toBeRemoved = true;
            }

            var muteOnColor = ZUI.PaletteColor("Warning", new Color(.70f,.42f,.08f,1f));
            var soloOnColor = ZUI.PaletteColor("Confirm", new Color(.14f,.34f,.14f,1f));
            bool newMute = ZUI.Toggle(muteRect, entry.mute, muteLabel, ZUI.Style.ZoundBtnFlatToggle, muteOnColor, ZUICornerMask.Left);
            if (newMute != entry.mute) {
                ZoundsWindow.ModifyZoundsProject("toggle mute", () => {
                    entry.mute = newMute;
                    if (entry.mute) entry.solo = false;
                });
            }
            bool newSolo = ZUI.Toggle(soloRect, entry.solo, soloLabel, ZUI.Style.ZoundBtnFlatToggle, soloOnColor, ZUICornerMask.Right);
            if (newSolo != entry.solo) {
                ZoundsWindow.ModifyZoundsProject("toggle solo", () => {
                    entry.solo = newSolo;
                    if (entry.solo) entry.mute = false;
                });
            }

            toBeConverted = false;
            if (compositeZound.originalId == 0) {
                if (ZUI.Button(conversionRect, icon_makeShared, ZUI.Style.RichButton)) {
                    toBeConverted = true;
                }
            }
            else {
                if (ZUI.Button(conversionRect, icon_reconnectToShared, ZUI.Style.RichButton)) {
                    toBeConverted = true;
                }
            }

            var zoundEntries = parentZound.zoundEntries;
            var guiEnabled = GUI.enabled;
            GUI.enabled = guiEnabled && entryIndex > 0;
            if (ZUI.Button(reorderUpRect, reorderUpLabel, ZUI.Style.RichButton, ZUICornerMask.Left)) {
                ZoundsWindow.ModifyZoundsProject("reorder up", () => {
                    var temp = zoundEntries[entryIndex - 1];
                    zoundEntries[entryIndex - 1] = zoundEntries[entryIndex];
                    zoundEntries[entryIndex] = temp;
                });
            }
            GUI.enabled = guiEnabled && entryIndex < (zoundEntries.Count - 1);
            if (ZUI.Button(reorderDownRect, reorderDownLabel, ZUI.Style.RichButton, ZUICornerMask.Right)) {
                ZoundsWindow.ModifyZoundsProject("reorder down", () => {
                    var temp = zoundEntries[entryIndex + 1];
                    zoundEntries[entryIndex + 1] = zoundEntries[entryIndex];
                    zoundEntries[entryIndex] = temp;
                });
            }
            GUI.enabled = guiEnabled;

            currentY += 22f;
            var delayRect = new Rect(rightSection.x, currentY, rightSection.width, lineHeight);

            EditorGUI.BeginChangeCheck();
            float newDelay = EditorGUI.Slider(delayRect, entry.delay, 0f, targetZound.editor_maxDuration);
            if (EditorGUI.EndChangeCheck()) {
                ZoundsWindow.ModifyZoundsProject("change zequence entry delay", () => {
                    entry.delay = newDelay;
                    RecalculateMaxDuration();
                });
            }

            currentY += lineHeight + 2f;

            if (compositeZound is Zequence zeq) {
                // no more middle values
                float globalMaxDuration = targetZound.editor_maxDuration / targetZound.minPitch;
                float globalDelay = entry.delay / targetZound.minPitch;
                float fieldBoxWidth = EditorGUIUtility.fieldWidth;
                float totalWidth = rightSection.width - fieldBoxWidth - 15f;
                var parentOffset = (globalDelay / globalMaxDuration) * totalWidth;
                float delayRectWidth = totalWidth - parentOffset;

                float controlWidth = (entryDuration / globalMaxDuration) * totalWidth;

                if (entry.volumeEnvelope.enabled) {
                    //var timelineRect = new Rect(rightSection.x + parentOffset + 5f, currentY, delayRectWidth, lineHeight * 4f);

                    var timelineRect = new Rect(rightSection.x + 5f, currentY, totalWidth, lineHeight * 4f);
                    var timelineBGRect = new Rect(rightSection.x + parentOffset + 5f, currentY, controlWidth, lineHeight * 4f);
                    var prevGUIColor = GUI.color;

                    GUI.color = new Color(0.75f, 0.75f, 0.75f, 0.1f);
                    GUI.DrawTexture(timelineBGRect, EditorGUIUtility.whiteTexture);

                    if (entry.volumeEnvelope.enabled) {
                        var envelopeCache = GetAndValidateEnvelopeCache(entry);
                        if (envelopeCache.envelopeGUI.Draw(timelineBGRect, envelopeCache.envelope, editorStyle.volumeEnvelopeColor, editorStyle.volumeEnvelopeThickness, true)) {
                            ZoundsWindow.ModifyZoundsProject("modify group volume envelope", () => {
                                entry.volumeEnvelope = envelopeCache.envelope.DeepCopy();
                                entry.volumeEnvelope.enabled = true;
                            });
                        }
                    }

                    if (ZoundEngine.CullingGroups.TryGetValue(compositeZound, out var playingTokens)) {
                        GUI.color = ZoundsProject.Instance.projectSettings.editorStyle.playerHeadColor;
                        foreach (var playingToken in playingTokens) {
                            if (playingToken.audioSource.mute) continue;
                            //float actualDuration = CalculateCompositeDuration(playingToken.zound as CompositeZound, parentPitch);
                            float actualDuration = playingToken.duration;
                            float adjustedWidth = timelineRect.width / globalMaxDuration * actualDuration;
                            float playerX = timelineBGRect.x - 1.5f + ((playingToken.time / actualDuration) * adjustedWidth);
                            var playerRect = new Rect(playerX, timelineRect.y, 1.5f, timelineRect.height);
                            GUI.DrawTexture(playerRect, EditorGUIUtility.whiteTexture);
                        }
                        GUI.color = prevGUIColor;
                    }

                    GUI.color = prevGUIColor;
                    currentY += timelineRect.height;
                }
                else {
                    currentY += lineHeight;
                }
            }

            if (ZoundEngine.CullingGroups.TryGetValue(compositeZound, out var playingTokens2)) {
                flashRect.height = currentY - flashRect.y; 
                foreach (var playingToken in playingTokens2) {
                    if (playingToken.audioSource.mute) continue;
                    if (playingToken == null || playingToken.state == ZoundToken.State.Killed) continue;
                    FlashEntry(flashRect);
                }
            }

            if (entry.editor_foldoutExpanded) {

                for (int i = 0; i < compositeZound.zoundEntries.Count; i++) {
                    currentY += entryHeight + 4f;
                }
                currentY += 6f;

                var localKlipRect = new Rect(rightSection.x, currentY, 85f, lineHeight);
                //var localZequenceRect = new Rect(localKlipRect.xMax + 4f, currentY, 125f, lineHeight);
                //var sharedZoundRect = new Rect(localZequenceRect.xMax + 4f, currentY, 105f, lineHeight);
                var sharedZoundRect = new Rect(localKlipRect.xMax + 4f, currentY, 105f, lineHeight);

                float xOffset = rightSection.xMax - sharedZoundRect.xMax;
                localKlipRect.x += xOffset;
                //localZequenceRect.x += xOffset;
                sharedZoundRect.x += xOffset;

                if (ZUI.Button(localKlipRect, "+ Local Klip", ZUI.Style.RichButton, ZUICornerMask.Left)) {
                    BrowserTab.OpenCreateNewKlipDialog(Event.current.mousePosition, klip => {
                        klip.parentId = compositeZound.id;
                        compositeZound.localKlips.Add(klip);
                        AddNewZoundEntry(compositeZound, klip, true);
                    }, createKlipSearchText, text => createKlipSearchText = text);
                }
                //if (ZUI.Button(localZequenceRect, "+ Local Zequence", ZUI.Style.RichButton)) {
                //    Debug.Log("Nested Local Zequence is not supported.");
                //}
                if (ZUI.Button(sharedZoundRect, "+ Shared Zound", ZUI.Style.RichButton, ZUICornerMask.Right)) {
                    AddNewEntryFromExisting(compositeZound);
                }
            }
        }

    }
}
