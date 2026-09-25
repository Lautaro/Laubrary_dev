using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Laubrary.Zounds {

    public partial class CompositeZoundEditorWindow<TZound, TSelf> : BaseZoundEditorWindow<TZound, TSelf> where TZound : CompositeZound {

        private const float entryHeight = 118f + 18f;
        protected const float leftSectionWidth = 190f;
        private const float groupHeaderHeight = 68f;
        private const float groupEntryLeftOffset = 10f;

        [System.Serializable]
        public class EnvelopeCache {
            public int instanceID;
            public Envelope envelope;
            public EnvelopeGUI envelopeGUI { get; set; }
        }

        [SerializeField] private List<EnvelopeCache> envelopeCaches = new List<EnvelopeCache>();
        [SerializeField] private Vector2 scrollPos;
        private string addMenuSearchText;
        private string createKlipSearchText;

        private ZoundBrowserEditor<CompositeZound> localZequenceInspector;

        private GUIContent label_mode;
        private GUIContent label_maxDuration;
        private GUIContent label_overrideToggle;
        private GUIContent label_playEntry;
        private GUIContent label_stopEntry;
        private GUIContent icon_removeZound;
        private GUIContent icon_removeEntry;
        private GUIContent icon_duplicateEntry;
        private GUIContent icon_makeShared;
        private GUIContent icon_reconnectToShared;
        private GUIContent icon_breakToLocal;
        private GUIStyle durationTextStyle;
        private GUIContent muteLabel;
        private GUIContent soloLabel;
        private GUIContent reorderUpLabel;
        private GUIContent reorderDownLabel;

        protected Dictionary<CompositeZound.ZoundEntry, ZoundToken> entryTokens;

        protected override void OnInit() {
            localZequenceInspector = new ZoundBrowserEditor<CompositeZound>(null);
            label_mode = new GUIContent("Mode");
            label_maxDuration = new GUIContent("Duration", "This is only used to determine editor width, and doesn't affect runtime behaviour.");
            label_overrideToggle = new GUIContent("O", "Override.\n\nIf checked, then this will override the original value of the zound. If unchecked, then this will act as a multiplier of the original value.");
            label_playEntry = new GUIContent("►", "Play");
            label_stopEntry = new GUIContent("⏹", "Stop");
            icon_removeZound = new GUIContent(ZUI.FindIcon("remove") ?? Resources.Load<Texture>("ZoundsWindowIcons/remove"), "Remove this zound.");
            icon_removeEntry = new GUIContent(ZUI.FindIcon("remove") ?? Resources.Load<Texture>("ZoundsWindowIcons/remove"), "Remove this zound entry.");
            icon_duplicateEntry = new GUIContent(ZUI.FindIcon("duplicate") ?? Resources.Load<Texture>("ZoundsWindowIcons/duplicate"), "Duplicate this zound entry.");
            icon_makeShared = new GUIContent(ZUI.FindIcon("make-shared") ?? Resources.Load<Texture>("ZoundsWindowIcons/make-shared"), "<b>Convert to Shared Klip</b>\n\nConvert this Klip into a Shared Klip where it will be listed in Klip browser. Shared Klips can be used across different " + typeof(TZound).Name + ".");
            icon_breakToLocal = new GUIContent(ZUI.FindIcon("break-to-local") ?? Resources.Load<Texture>("ZoundsWindowIcons/break-to-local"), "<b>Break as Local Klip</b>\n\nConvert this Klip into a Local Klip where it will only be available internally in this " + typeof(TZound).Name + ". This will break the dependency from the original configuration of the Shared Klip, and the Shared Klip's configuration will also no longer affected by this Klip.");
            icon_reconnectToShared = new GUIContent(ZUI.FindIcon("reconnect-shared") ?? Resources.Load<Texture>("ZoundsWindowIcons/reconnect-shared"), "<b>Reconnect to Original Shared Klip</b>\n\nConvert this Klip back into its original Shared Klip. If the original Shared Klip has been removed, then this will fallback into creating a new Shared Klip.");
            muteLabel = new GUIContent("M", "Mute/Unmute");
            soloLabel = new GUIContent("S", "Toggle Solo");
            reorderUpLabel = new GUIContent("↑", "Reorder up.");
            reorderDownLabel = new GUIContent("↓", "Reorder down.");
            ValidateEnvelopeGUIs();
        }

        private void OnLostFocus() {
            ResetEnvelopGUIStates();
        }

        protected override void OnUndoRedoPerformed() {
            targetZound = FindZoundTarget();
            if (targetZound == null) return;
            ValidateEnvelopeGUIs();
        }

        private void ResetEnvelopGUIStates() {
            foreach (var envelopeCache in envelopeCaches) {
                if (envelopeCache == null) continue;
                if (envelopeCache.envelopeGUI == null) continue;
                envelopeCache.envelopeGUI.ResetStates();
            }
        }

        protected void ValidateEnvelopeGUIs() {
            OnValidateEnvelopeGUIs();

            for (int i = 0; i < targetZound.zoundEntries.Count; i++) {
                var entry = targetZound.zoundEntries[i];
                GetAndValidateEnvelopeCache(entry, true);
                if (entry.local && targetZound.TryGetEntryZound(entry, out var entryZound)) {
                    if (entryZound is CompositeZound compositeZound) {
                        for (int j = 0; j < compositeZound.zoundEntries.Count; j++) {
                            var entry2 = compositeZound.zoundEntries[j];
                            GetAndValidateEnvelopeCache(entry2, true);
                        }
                    }
                }
            }
            ResetEnvelopGUIStates();
        }

        private EnvelopeCache GetAndValidateEnvelopeCache(CompositeZound.ZoundEntry entry, bool recreateGUICache = false) {
            if (entry.editor_instanceID == 0) {
                int newInstanceID;
                do {
                    newInstanceID = UnityEngine.Random.Range(1, int.MaxValue);
                } while (envelopeCaches.Find(ec => ec.instanceID == newInstanceID) != null);
                entry.editor_instanceID = newInstanceID;
            }
            var envelopeCache = envelopeCaches.Find(ec => ec.instanceID == entry.editor_instanceID);
            if (envelopeCache == null) {
                if (entry.volumeEnvelope == null || entry.volumeEnvelope.Count == 0) {
                    entry.volumeEnvelope = new Envelope(Zound.MinVolumeRange, Zound.MaxVolumeRange);
                    EditorUtility.SetDirty(ZoundsProject.Instance);
                }
                envelopeCache = new EnvelopeCache() {
                    instanceID = entry.editor_instanceID,
                    envelope = entry.volumeEnvelope.DeepCopy()
                };
                envelopeCaches.Add(envelopeCache);
            }
            else if (recreateGUICache) {
                envelopeCache.envelope = entry.volumeEnvelope.DeepCopy();
            }

            if (envelopeCache.envelopeGUI == null) {
                envelopeCache.envelopeGUI = new EnvelopeGUI() {
                    name = entry.editor_instanceID.ToString(),
                };
            }

            return envelopeCache;
        }

        protected virtual void OnValidateEnvelopeGUIs() {
            
        }

        protected override bool OnDrawGUI() {
            bool remove = false;
            bool isPlaying = IsCurrentTokenPlaying();

            var zoundsProject = ZoundsProject.Instance;
            if (durationTextStyle == null) {
                durationTextStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel);
                durationTextStyle.normal.textColor = new Color32(121, 183, 255, 255);
            }

            float lineHeight = EditorGUIUtility.singleLineHeight;

            var fieldsRect = GUILayoutUtility.GetRect(1f, lineHeight, GUILayout.ExpandWidth(true));
            EditorGUI.BeginChangeCheck();
            inspector.DrawSimple(fieldsRect, targetZound, isLocalZound);
            if (EditorGUI.EndChangeCheck()) {
                RefreshWindowName();
            }

            GUILayout.Space(4f);

            if (targetZound == null) return remove;

            using (ZUI.Box())
            {

            //DrawAudioRenderingMenu();

            GUILayout.BeginHorizontal();
            {
                OnDrawHeaderLayout();

                GUILayout.FlexibleSpace();

                var prevLabelWidth = EditorGUIUtility.labelWidth;

                EditorGUIUtility.labelWidth = 38f;
                EditorGUI.BeginChangeCheck();
                var newMode = (CompositeZound.Mode)EditorGUILayout.EnumPopup(label_mode, targetZound.mode, GUILayout.Width(140f));
                if (EditorGUI.EndChangeCheck()) {
                    ZoundsWindow.ModifyZoundsProject("change zequence mode", () => {
                        targetZound.mode = newMode;
                    });
                }

                EditorGUIUtility.labelWidth = 55f;
                EditorGUI.BeginChangeCheck();
                float globalMaxDuration = targetZound.editor_maxDuration / targetZound.minPitch;
                float newMaxDuration = EditorGUILayout.FloatField(label_maxDuration, globalMaxDuration, GUILayout.Width(130f));
                if (EditorGUI.EndChangeCheck()) {
                    ZoundsWindow.ModifyZoundsProject("change max duration", () => {
                        targetZound.editor_maxDuration = newMaxDuration * targetZound.minPitch;
                        RecalculateMaxDuration();
                    });
                }

                EditorGUIUtility.labelWidth = prevLabelWidth;

                GUILayout.Space(5f);
                if (ZUI.Button(icon_removeZound, ZUI.Style.RichButton, ZUI.Tint.Danger, GUILayout.Width(30f), GUILayout.Height(lineHeight))) {
                    if (AudioAssetUtility.DisplayZoundRemoveDialog(targetZound)) {
                        remove = true;
                    }
                }

                GUILayout.Space(5f);
                var guiEnabled = GUI.enabled;
                GUI.enabled = guiEnabled && !Application.isPlaying;
                if (ZUI.Button("Render to Klip", ZUI.Style.Default, ZUICornerMask.Left, GUILayout.Width(100f))) {
                    RenderZequenceToKlipPopup.Show(Event.current.mousePosition, targetZound as Zequence, CalculateCompositeDuration(targetZound, 1f));
                }
                GUI.enabled = guiEnabled;
                DrawRenderToKlipExtras();

                GUILayout.Space(5f);
                if (ZUI.Button(isPlaying ? "Stop" : "Play", ZUI.Style.Default, isPlaying ? ZUI.Tint.Confirm : null, ZUICornerMask.Right, GUILayout.Width(60f))) {
                    if (!isPlaying) {
                        SimulatePlay();
                    }
                    else {
                        currentToken.Kill();
                    }
                }

                //bool isPaused = currentToken != null && currentToken.state == ZoundToken.State.Paused;
                //if (GUILayout.Button(isPaused ? "Resume" : "Pause", GUILayout.Width(80f))) {
                //    if (!isPaused) {
                //        if (currentToken != null && currentToken.state != ZoundToken.State.Killed) {
                //            currentToken.Pause();
                //        }
                //    }
                //    else {
                //        if (currentToken != null && currentToken.state != ZoundToken.State.Killed) {
                //            currentToken.Resume();
                //        }
                //    }
                //}
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(5f);

            if (targetZound.zoundEntries.Count == 0) {
                EditorGUILayout.LabelField("No zound entry.", EditorStyles.centeredGreyMiniLabel);
            }

            scrollPos = GUILayout.BeginScrollView(scrollPos);
            bool darkerBG = false;
            int entryIndexToRemove = -1;
            int entryIndexToDuplicate = -1;
            int entryIndexToConvert = -1;
            for (int i = 0; i < targetZound.zoundEntries.Count; i++) {
                var zoundEntry = targetZound.zoundEntries[i];
                bool isZoundFound = targetZound.TryGetEntryZound(zoundEntry, out var zound);

                Rect entryRect;
                if (isZoundFound && zoundEntry.local && zound is CompositeZound composite) {
                    float totalHeight = groupHeaderHeight;
                    if (zoundEntry.editor_foldoutExpanded) {
                        totalHeight += composite.zoundEntries.Count * (entryHeight + 4f);
                        totalHeight += lineHeight + 10f;
                    }
                    if (composite is Zequence zequence) {
                        float masterHeight = zoundEntry.volumeEnvelope.enabled ? lineHeight * 4f : lineHeight;
                        totalHeight += masterHeight;
                    }
                    entryRect = GUILayoutUtility.GetRect(1, totalHeight, GUILayout.ExpandWidth(true));
                }
                else {
                    entryRect = GUILayoutUtility.GetRect(1, entryHeight, GUILayout.ExpandWidth(true));
                }

                var color = darkerBG ? new Color(0.25f, 0.25f, 0.30f, 0.22f) : new Color(0.35f, 0.35f, 0.42f, 0.15f);
                var prevGUIColor = GUI.color;
                GUI.color = color;
                GUI.DrawTexture(entryRect, EditorGUIUtility.whiteTexture);
                GUI.color = prevGUIColor;
                if (isZoundFound) {
                    DrawEntry(entryRect, targetZound, zound, zoundEntry, i, targetZound.minPitch, 0f, out bool toBeRemoved, out bool toBeDuplicated, out bool toBeConverted);
                    if (toBeRemoved) {
                        entryIndexToRemove = i;
                    }
                    if (toBeDuplicated) {
                        entryIndexToDuplicate = i;
                    }
                    if (toBeConverted) {
                        entryIndexToConvert = i;
                    }
                }
                else {
                    // Draw a placeholder for broken entries
                    var leftSection = new Rect(entryRect.x, entryRect.y, leftSectionWidth, entryRect.height);
                    var rightSection = new Rect(leftSection.xMax + 5f, entryRect.y, entryRect.width - leftSection.width - 5f, entryRect.height);
                    
                    GUI.color = new Color(1f, 0.4f, 0.4f, 0.2f);
                    GUI.DrawTexture(entryRect, EditorGUIUtility.whiteTexture);
                    GUI.color = Color.white;
                    
                    var labelRect = new Rect(leftSection.x + 5f, leftSection.y + 5f, leftSection.width - 10f, EditorGUIUtility.singleLineHeight);
                    EditorGUI.LabelField(labelRect, "BROKEN ENTRY", EditorStyles.boldLabel);
                    
                    var msgRect = new Rect(leftSection.x + 5f, labelRect.yMax, leftSection.width - 10f, entryRect.height - 30f);
                    EditorGUI.HelpBox(msgRect, "Zound data is missing or invalid for this entry.", MessageType.Error);
                    
                    var removeBtnRect = new Rect(rightSection.xMax - 64f, rightSection.y + 5f, 60f, 20f);
                    if (ZUI.Button(removeBtnRect, "Remove", ZUI.Style.RichButton, ZUI.Tint.Danger)) {
                        entryIndexToRemove = i;
                    }
                }
                darkerBG = !darkerBG;
                GUILayout.Space(4f);
            }

            OnEndOfScrollView(entryHeight);

            GUILayout.EndScrollView();


            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            bool addExistingZound = false;
            bool addNewKlip = false;
            bool addNewZequence = false;
            if (ZUI.Button("+ Local Klip", ZUI.Style.RichButton, ZUICornerMask.Left, GUILayout.Width(85f))) {
                addNewKlip = true;
            }
            if (ZUI.Button("+ Local Zequence", ZUI.Style.RichButton, ZUICornerMask.None, GUILayout.Width(125f))) {
                addNewZequence = true;
            }
            if (ZUI.Button("+ Shared Zound", ZUI.Style.RichButton, ZUICornerMask.Right, GUILayout.Width(105f))) {
                addExistingZound = true;
            }
            GUILayout.EndHorizontal();

            if (entryIndexToRemove >= 0) {
                RemoveEntry(targetZound, entryIndexToRemove);
            }

            if (entryIndexToDuplicate >= 0) {
                DuplicateEntry(targetZound, entryIndexToDuplicate);
            }

            if (entryIndexToConvert >= 0) {
                ConvertEntry(targetZound, entryIndexToConvert);
            }

            if (addExistingZound) {
                AddNewEntryFromExisting(targetZound);
            }
            if (addNewKlip) {
                BrowserTab.OpenCreateNewKlipDialog(Event.current.mousePosition, klip => {
                    klip.parentId = targetZound.id;
                    targetZound.localKlips.Add(klip);
                    AddNewZoundEntry(targetZound, klip, true);
                }, createKlipSearchText, text => createKlipSearchText = text);
            }
            if (addNewZequence) {
                var newZequence = new Zequence(ZoundLibrary.GetUniqueZoundId());
                newZequence.name = ZoundDictionary.EnsureUniqueZoundName("Zequence");
                newZequence.parentId = targetZound.id;
                targetZound.localZequences.Add(new CompositeZound.LocalZequence(newZequence));
                AddNewZoundEntry(targetZound, newZequence, true);
            }

            GUILayout.FlexibleSpace();

            if (isPlaying || HasAnyInstancePlaying()) {
                //Debug.Log("Repaint: " + targetZound.name);
                Repaint();
            }

            } // end ZUI.Box

            return remove;
        }

        protected override void OnPressSpaceKey() {
            if (IsCurrentTokenPlaying()) {
                currentToken.Kill();
            }
            else {
                SimulatePlay();
            }
        }

        private void SimulatePlay() {
            currentToken = ZoundEngine.PlayZound(targetZound, new ZoundArgs() {
                startImmediately = true,
                delay = 0f,
                volumeOverride = -1f,
                pitchOverride = -1f,
                chanceOverride = -1f,
                useFixedAverageValues = true,
                bypassGlobalSolo = isLocalZound,
                ignoreCooldown = true
            });
        }

        protected virtual void DrawAudioRenderingMenu() { }

        private void DuplicateEntry(CompositeZound parentZound, int entryIndexToDuplicate) {
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
            ValidateEnvelopeGUIs();
        }

        private void RemoveEntry(CompositeZound parentZound, int entryIndexToRemove) {
            ZoundsWindow.ModifyZoundsProject("remove zound entry", () => {
                var entryToRemove = parentZound.zoundEntries[entryIndexToRemove];
                if (entryToRemove.local) {
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
            ValidateEnvelopeGUIs();
        }

        private void ConvertEntry(CompositeZound parentZound, int entryIndexToConvert) {
            ZoundsProject zoundsProject = ZoundsProject.Instance;
            ZoundsWindow.ModifyZoundsProject("convert zound entry", () => {
                var entryToConvert = parentZound.zoundEntries[entryIndexToConvert];
                if (entryToConvert.local) {
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
            targetZound = FindZoundTarget();
            ValidateEnvelopeGUIs();
            ZoundsAssetPostProcessor.RefreshAudioClipsCache();
            ZoundsWindow.RepaintWindow();
        }

        private static void BreakEntryAsLocal(CompositeZound parentZound, CompositeZound.ZoundEntry entryToConvert, Zound zoundToConvert, Zound convertedZound) {
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

        protected virtual void OnDrawHeaderLayout() {
            
        }

        protected virtual void OnEndOfScrollView(float entryHeight) {
            
        }

        /// <summary>
        /// Draw a specific zound entry of a zequence.
        /// </summary>
        /// <returns>Returns true if this entry needs to be removed.</returns>
        private void DrawEntry(Rect rect, CompositeZound parentZound, Zound zound, CompositeZound.ZoundEntry entry, int entryIndex, float parentPitch, float parentDelay, out bool toBeRemoved, out bool toBeDuplicated, out bool toBeConverted) {
            float entryDuration = GetEntryDuration(parentZound, entry, parentPitch);
            bool isGroupChild = parentZound != targetZound;

            Rect contentRect;
            if (isGroupChild) contentRect = new Rect(rect.x, rect.y + 4f, rect.width, rect.height - 8f);
            else contentRect = new Rect(rect.x + 4f, rect.y + 4f, rect.width - 8f, rect.height - 8f);

            if (entry.local && zound is CompositeZound composite) {
                DrawEntryGroup(contentRect, parentZound, entry, composite, entryIndex, entryDuration, out toBeRemoved, out toBeDuplicated, out toBeConverted);
            }
            else {
                float leftOffset = isGroupChild ? groupEntryLeftOffset : 0f;
                var leftSection = new Rect(contentRect.x + leftOffset, contentRect.y, leftSectionWidth - leftOffset, contentRect.height);
                var rightSection = new Rect(leftSection.xMax + 5f, contentRect.y + EditorGUIUtility.singleLineHeight, contentRect.width - leftSection.width - 5f - leftOffset, contentRect.height - EditorGUIUtility.singleLineHeight);
                DrawEntryLeftSection(leftSection, parentZound, entry, zound, entryDuration, parentDelay, contentRect);
                DrawEntryRightSection(contentRect, rightSection, parentZound, entry, zound, parentPitch, parentDelay, entryDuration, entryIndex, out toBeRemoved, out toBeDuplicated, out toBeConverted);
            }

        }

        protected Rect DrawEntryChanceWeight(Rect leftSection, CompositeZound.Mode playMode, CompositeZound.ZoundEntry entry) {
            if (playMode == CompositeZound.Mode.Randomizer) {
                var zoundsProject = ZoundsProject.Instance;
                var chanceWeightRect = new Rect(leftSection.position, new Vector2(22f, 20f));

                var chanceWeight = EditorGUI.IntField(chanceWeightRect, entry.chanceWeight);
                if (chanceWeight != entry.chanceWeight) {
                    ZoundsWindow.ModifyZoundsProject("changed entry chance weight", () => {
                        entry.chanceWeight = chanceWeight;
                    });
                }

                float offset = chanceWeightRect.width + 2f;
                leftSection.x += offset;
                leftSection.width -= offset;
            }

            return leftSection;
        }

        protected virtual void DrawEntryLeftSection(Rect leftSection, CompositeZound parentZound, CompositeZound.ZoundEntry entry, Zound zound, float entryDuration, float parentDelay, Rect contentRect) {
            var zoundsProject = ZoundsProject.Instance;
            float lineHeight = EditorGUIUtility.singleLineHeight;
            float currentY = leftSection.y;

            float playButtonWidth = 18f;

            var labelRect = new Rect(leftSection.x, currentY, (contentRect.width - playButtonWidth) * 0.8f, lineHeight);
            if (ZUI.Button(labelRect, zound.name, ZUI.Style.RichButton, ZUICornerMask.Left)) {
                int buttonCode = Event.current.button;
                if (buttonCode == 0) {
                    if (zound is Klip k) {
                        var w = KlipEditorWindow.OpenWindow(k);
                        if (entry.local) w.isLocalZound = true;
                    }
                    else if (zound is Zequence z) {
                        var w = ZequenceEditorWindow.OpenWindow(z);
                        if (entry.local) w.isLocalZound = true;
                    }
                }
                else if (buttonCode == 1) {

                }
            }

            var playButtonRect = new Rect(contentRect.xMax - playButtonWidth, currentY, playButtonWidth, lineHeight);
            bool isPlaying = entryTokens != null && entryTokens.TryGetValue(entry, out var entryToken) && entryToken.TryGetEntryToken(entry, out var childToken) && childToken.state != ZoundToken.State.Killed;
            if (ZUI.Button(playButtonRect, isPlaying ? label_stopEntry : label_playEntry, ZUI.Style.RichButton, isPlaying ? ZUI.Tint.Confirm : null, ZUICornerMask.Right)) {
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

            // currentY is still at the first line (name row)
            currentY += lineHeight - 3f; // Start VPC slightly higher to use space better
            var durationRect = new Rect(leftSection.x, currentY, leftSection.width * 0.75f, lineHeight);
            string durationString = entryDuration.ToString("0.00") + " sec";
            float delay = parentDelay + entry.delay;
            if (delay > 0f) {
                durationString += " (" + (entryDuration + delay).ToString("0.00") + " sec)";
            }
            EditorGUI.LabelField(durationRect, durationString, durationTextStyle);

            var prevLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 16;
            var prevFieldWidth = EditorGUIUtility.fieldWidth;
            EditorGUIUtility.fieldWidth = 40f;

            Rect vRect;
            if (entry.local) {
                vRect = DrawLocalEntryVPC(ref leftSection, parentZound, entry, zoundsProject, lineHeight, ref currentY);
            }
            else {
                vRect = DrawSharedEntryVPC(ref leftSection, entry, zoundsProject, lineHeight, ref currentY);
            }

            EditorGUIUtility.fieldWidth = prevFieldWidth;
            EditorGUIUtility.labelWidth = prevLabelWidth;

        }

        private GUIContent tempContent = new GUIContent();
        private Rect DrawLocalEntryVPC(ref Rect leftSection, CompositeZound parentZound, CompositeZound.ZoundEntry entry, ZoundsProject zoundsProject, float lineHeight, ref float currentY) {
            
            currentY += lineHeight + 1f;
            var sliderSpacing = 2f;
            var vRect = new Rect(leftSection.x, currentY, leftSection.width - 1f, lineHeight);
            if (!parentZound.TryGetEntryZound(entry, out var entryZound)) return vRect;

            tempContent.text = "V";
            EditorFieldsUtility.DrawMinMaxSlider(
                vRect, tempContent,
                entryZound.minVolume,
                newMin => {
                    ZoundsWindow.ModifyZoundsProject("change entry volume", () => {
                        entryZound.minVolume = ZoundBrowserEditor<Klip>.RoundTo3DecimalPlaces(newMin);
                    });
                },
                entryZound.maxVolume,
                newMax => {
                    ZoundsWindow.ModifyZoundsProject("change entry volume", () => {
                        entryZound.maxVolume = ZoundBrowserEditor<Klip>.RoundTo3DecimalPlaces(newMax);
                    });
                },
                Zound.MinVolumeRange, Zound.MaxVolumeRange);

            currentY += lineHeight + 3f + sliderSpacing;
            var pRect = new Rect(leftSection.x, currentY, vRect.width, lineHeight);
            tempContent.text = "P";
            EditorFieldsUtility.DrawMinMaxSlider(
                pRect, tempContent,
                entryZound.minPitch,
                newMin => {
                    ZoundsWindow.ModifyZoundsProject("change entry pitch", () => {
                        entryZound.minPitch = ZoundBrowserEditor<Klip>.RoundTo3DecimalPlaces(newMin);
                    });
                },
                entryZound.maxPitch,
                newMax => {
                    ZoundsWindow.ModifyZoundsProject("change entry pitch", () => {
                        entryZound.maxPitch = ZoundBrowserEditor<Klip>.RoundTo3DecimalPlaces(newMax);
                    });
                },
                Zound.MinPitchRange, Zound.MaxPitchRange);

            currentY += lineHeight + 3f + sliderSpacing;
            var cRect = new Rect(leftSection.x, currentY, vRect.width, lineHeight);
            tempContent.text = "C";
            EditorFieldsUtility.DrawMinMaxSlider(
                cRect, tempContent,
                entryZound.chance,
                newMin => ZoundsWindow.ModifyZoundsProject("change entry chance", () => { entryZound.chance = ZoundBrowserEditor<Klip>.RoundTo3DecimalPlaces(newMin); }),
                entryZound.chance,
                newMax => ZoundsWindow.ModifyZoundsProject("change entry chance", () => { entryZound.chance = ZoundBrowserEditor<Klip>.RoundTo3DecimalPlaces(newMax); }),
                Zound.MinChanceRange, Zound.MaxChanceRange);

            return vRect;
        }

        private Rect DrawSharedEntryVPC(ref Rect leftSection, CompositeZound.ZoundEntry entry, ZoundsProject zoundsProject, float lineHeight, ref float currentY) {
            currentY += lineHeight + 1f;
            var vRect = new Rect(leftSection.x, currentY, leftSection.width - 36f, lineHeight);
            tempContent.text = "V";
            EditorFieldsUtility.DrawMinMaxSlider(vRect, tempContent,
                entry.volume,
                newVal => ZoundsWindow.ModifyZoundsProject("change entry volume", () => { entry.volume = newVal; }),
                entry.volume,
                newVal => ZoundsWindow.ModifyZoundsProject("change entry volume", () => { entry.volume = newVal; }),
                Zound.MinVolumeRange, Zound.MaxVolumeRange);

            var vOverrideRect = new Rect(vRect.xMax + 4f, currentY, 20f, lineHeight);
            EditorGUI.BeginChangeCheck();
            bool overrideV = EditorGUI.Toggle(vOverrideRect, label_overrideToggle, entry.overrideVolume);
            if (EditorGUI.EndChangeCheck()) {
                ZoundsWindow.ModifyZoundsProject("toggle override entry volume", () => { entry.overrideVolume = overrideV; });
            }

            currentY += lineHeight + 3f;
            var pRect = new Rect(leftSection.x, currentY, vRect.width, lineHeight);
            tempContent.text = "P";
            EditorFieldsUtility.DrawMinMaxSlider(pRect, tempContent,
                entry.pitch,
                newVal => ZoundsWindow.ModifyZoundsProject("change entry pitch", () => { entry.pitch = newVal; }),
                entry.pitch,
                newVal => ZoundsWindow.ModifyZoundsProject("change entry pitch", () => { entry.pitch = newVal; }),
                Zound.MinPitchRange, Zound.MaxPitchRange);

            var pOverrideRect = new Rect(vRect.xMax + 4f, currentY, 20f, lineHeight);
            EditorGUI.BeginChangeCheck();
            bool overrideP = EditorGUI.Toggle(pOverrideRect, label_overrideToggle, entry.overridePitch);
            if (EditorGUI.EndChangeCheck()) {
                ZoundsWindow.ModifyZoundsProject("toggle override entry pitch", () => { entry.overridePitch = overrideP; });
            }

            currentY += lineHeight + 3f;
            var cRect = new Rect(leftSection.x, currentY, vRect.width, lineHeight);
            tempContent.text = "C";
            EditorFieldsUtility.DrawMinMaxSlider(cRect, tempContent,
                entry.chance,
                newVal => ZoundsWindow.ModifyZoundsProject("change entry chance", () => { entry.chance = newVal; }),
                entry.chance,
                newVal => ZoundsWindow.ModifyZoundsProject("change entry chance", () => { entry.chance = newVal; }),
                Zound.MinChanceRange, Zound.MaxChanceRange);

            var cOverrideRect = new Rect(vRect.xMax + 4f, currentY, 20f, lineHeight);
            EditorGUI.BeginChangeCheck();
            bool overrideC = EditorGUI.Toggle(cOverrideRect, label_overrideToggle, entry.overrideChance);
            if (EditorGUI.EndChangeCheck()) {
                ZoundsWindow.ModifyZoundsProject("toggle override entry pitch", () => { entry.overrideChance = overrideC; });
            }

            return vRect;
        }

        private void DrawEntryRightSection(Rect flashRect, Rect rightSection, CompositeZound parentZound, CompositeZound.ZoundEntry entry, Zound zound,
                float parentPitch, float parentDelay, float entryDuration, int entryIndex, out bool toBeRemoved, out bool toBeDuplicated, out bool toBeConverted) {
            
            var zoundsProject = ZoundsProject.Instance;
            var editorStyle = zoundsProject.projectSettings.editorStyle;

            float fieldBoxWidth = EditorGUIUtility.fieldWidth;

            // no more middle values
            float globalMaxDuration = targetZound.editor_maxDuration / parentPitch;

            float totalWidth = rightSection.width - fieldBoxWidth - 15f;
            //Debug.Log("Total WIdth: " + fieldBoxWidth);
            var parentOffset = (parentDelay / globalMaxDuration) * totalWidth;
            float delayRectWidth = totalWidth - parentOffset;
            var delayRect = new Rect(rightSection.x + parentOffset, rightSection.y, delayRectWidth + fieldBoxWidth + 15f, EditorGUIUtility.singleLineHeight);

            EditorGUI.BeginChangeCheck();
            // no more middle values
            float restDuration = globalMaxDuration - parentDelay;
            //Debug.Log(zound.name + ": " + globalMaxDuration);
            float newDelay = EditorGUI.Slider(delayRect, entry.delay / parentPitch, 0f, restDuration);
            if (EditorGUI.EndChangeCheck()) {
                ZoundsWindow.ModifyZoundsProject("change zequence entry delay", () => {
                    entry.delay = newDelay * parentPitch;
                    RecalculateMaxDuration();
                });
            }

            var timelineRect = new Rect(rightSection.x + 5f, rightSection.y + 18f, totalWidth, rightSection.height - 18f);
            var timelineBGRect = new Rect(rightSection.x + parentOffset + 5f, rightSection.y + 18f, delayRectWidth, rightSection.height - 18f);
            var prevGUIColor = GUI.color;
            //GUI.DrawTexture(timelineRect, EditorGUIUtility.whiteTexture); // DELETE LATER
            GUI.color = new Color(1f, 1f, 1f, 0.1f);
            GUI.DrawTexture(timelineBGRect, EditorGUIUtility.whiteTexture);

            // no more middle values
            float accumulativeDelay = parentDelay + (entry.delay / parentPitch);

            float spectrumX = (accumulativeDelay / globalMaxDuration) * timelineRect.width;
            float spectrumWidth = (entryDuration / globalMaxDuration) * timelineRect.width;
            if (spectrumX + spectrumWidth > timelineRect.width) {
                //Debug.Log((spectrumX + spectrumWidth) + " > " + timelineRect.width);
                RecalculateMaxDuration();
                spectrumX = (accumulativeDelay / globalMaxDuration) * timelineRect.width;
                spectrumWidth = (entryDuration / globalMaxDuration) * timelineRect.width;
            }

            var spectrumRect = new Rect(timelineRect.x + spectrumX, timelineRect.y, spectrumWidth, timelineRect.height);

            if (zound is Klip klip) {
                GUI.color = editorStyle.klipWaveformBGColor;
                GUI.DrawTexture(spectrumRect, EditorGUIUtility.whiteTexture);
                var audioClip = klip.GetAudioClipReference().editorAsset as AudioClip;

                if (audioClip != null) {
                    GUI.color = Color.white;
                    var audioTexture = AudioWaveformUtility.GetWaveformSpectrumTexture(audioClip, Mathf.FloorToInt(spectrumRect.width), Mathf.FloorToInt(spectrumRect.height), editorStyle.waveformColor, klip.id.ToString());
                    if (audioTexture != null) {
                        GUI.DrawTexture(spectrumRect, audioTexture);
                    }
                }
                GUI.color = prevGUIColor;
            }
            else if (zound is Zequence zequence) {
                GUI.color = editorStyle.klipWaveformBGColor;
                GUI.DrawTexture(spectrumRect, EditorGUIUtility.whiteTexture);
            }
            GUI.color = prevGUIColor;

            // Clicking the waveform plays/stops the entry, identical to the play button.
            if (Event.current.type == EventType.MouseDown && Event.current.button == 0
                && spectrumRect.Contains(Event.current.mousePosition)) {
                bool isWaveformPlaying = entryTokens != null
                    && entryTokens.TryGetValue(entry, out var waveToken)
                    && waveToken.TryGetEntryToken(entry, out var waveChildToken)
                    && waveChildToken.state != ZoundToken.State.Killed;
                if (isWaveformPlaying) {
                    entryTokens[entry].Kill();
                }
                else {
                    if (entryTokens == null) entryTokens = new Dictionary<CompositeZound.ZoundEntry, ZoundToken>();
                    var token = ZoundEngine.PlayZound(targetZound, new ZoundArgs() {
                        startImmediately  = true,
                        delay             = 0f,
                        volumeOverride    = -1f,
                        pitchOverride     = -1f,
                        chanceOverride    = -1f,
                        useFixedAverageValues = true,
                        soloOverride      = entry,
                        ignoreCooldown    = true
                    });
                    if (entryTokens.ContainsKey(entry))
                        entryTokens[entry] = token;
                    else
                        entryTokens.Add(entry, token);
                }
                Event.current.Use();
            }

            if (entry.volumeEnvelope.enabled) {
                var envelopeCache = GetAndValidateEnvelopeCache(entry);
                if (envelopeCache.envelopeGUI.Draw(spectrumRect, envelopeCache.envelope, editorStyle.volumeEnvelopeColor, editorStyle.volumeEnvelopeThickness, true)) {
                    ZoundsWindow.ModifyZoundsProject("modify entry volume envelope", () => {
                        entry.volumeEnvelope = envelopeCache.envelope.DeepCopy();
                        entry.volumeEnvelope.enabled = true;
                    });
                }
            }

            if (ZoundEngine.CullingGroups.TryGetValue(parentZound, out var playingTokens)) {
                GUI.color = ZoundsProject.Instance.projectSettings.editorStyle.playerHeadColor;
                foreach (var playingToken in playingTokens) {
                    if (!playingToken.TryGetEntryToken(entry, out var childToken) || childToken.state == ZoundToken.State.Killed) {
                        continue;
                    }
                    if (playingToken.IsEntryMuted(entry)) continue;
                    if (playingToken.soloOverride != null && playingToken.soloOverride != entry) continue;
                    if (playingToken == null || playingToken.state == ZoundToken.State.Killed) continue;
                    if (playingToken.zound is Zequence tokenZeq && playingToken.isRealtime) {
                        if (tokenZeq.mode != CompositeZound.Mode.Parallel) {
                            if (playingToken.playedEntryIndex != entryIndex) continue;
                        }
                    }

                    FlashEntry(flashRect);

                    // Use the CHILD entry's own token for playback progress, not the parent
                    // zequence's. playingToken.time/duration are the whole zequence's elapsed
                    // time and total length — mapping that fraction onto this single entry's
                    // local pixel width moves the playhead at the wrong speed, and ignores this
                    // entry's own delay (childToken.time sits at 0 until this entry's delay
                    // actually elapses, matching when its audio starts). spectrumRect already
                    // accounts for the delay (via accumulativeDelay), so anchor there instead of
                    // timelineBGRect, which only reflects the ancestor group's offset.
                    float childDuration = childToken.duration;
                    if (childDuration > Mathf.Epsilon) {
                        float playerX = spectrumRect.x + (childToken.time / childDuration) * spectrumRect.width;
                        var playerRect = new Rect(playerX, timelineRect.y, 1.5f, timelineRect.height);
                        GUI.DrawTexture(playerRect, EditorGUIUtility.whiteTexture);
                    }
                }
                GUI.color = prevGUIColor;
            }

            if (entry.delay >= Mathf.Epsilon) {
                var preOffsetRect = new Rect(timelineBGRect.x + 2f, timelineBGRect.center.y + 10f, 50f, 20f);
                EditorGUI.LabelField(preOffsetRect, entry.delay.ToString("0.00") + " s", durationTextStyle);
            }

            if (entry.delay + entryDuration < targetZound.editor_maxDuration) {
                float postOffset = targetZound.editor_maxDuration - entry.delay - entryDuration;
                var postOffsetRect = new Rect(timelineBGRect.xMax - 52f, timelineBGRect.center.y + 10f, 50f, 20f);
                EditorGUI.LabelField(postOffsetRect, postOffset.ToString("0.00") + " s", durationTextStyle);
            }
            GUI.color = prevGUIColor;

            var dupRemoveRect = new Rect(timelineRect.xMax + 5f, timelineRect.y, rightSection.width - timelineRect.width - 10f, 20f);
            const float btnGap = 2f;
            float btnW = (dupRemoveRect.width - btnGap) / 2f;
            var duplicateRect = new Rect(dupRemoveRect.x,          dupRemoveRect.y, btnW, dupRemoveRect.height);
            var removeRect    = new Rect(duplicateRect.xMax + btnGap, dupRemoveRect.y, btnW, dupRemoveRect.height);

            toBeDuplicated = false;
            if (ZUI.Button(duplicateRect, icon_duplicateEntry, ZUI.Style.RichButton, ZUICornerMask.Left)) {
                toBeDuplicated = true;
            }

            toBeRemoved = false;
            if (ZUI.Button(removeRect, icon_removeEntry, ZUI.Style.RichButton, ZUI.Tint.Danger, ZUICornerMask.Right)) {
                toBeRemoved = true;
            }

            var muteSoloRect = new Rect(dupRemoveRect.x, dupRemoveRect.yMax + btnGap, dupRemoveRect.width, dupRemoveRect.height);
            var muteRect = new Rect(muteSoloRect.x,              muteSoloRect.y, btnW, muteSoloRect.height);
            var soloRect = new Rect(muteRect.xMax + btnGap, muteSoloRect.y, btnW, muteSoloRect.height);

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
            var conversionRect = new Rect(muteSoloRect.x, muteSoloRect.yMax + 2f, muteSoloRect.width, muteSoloRect.height);
            if (zound is Klip klip2) {
                if (entry.local) {
                    if (klip2.originalId == 0) {
                        if (ZUI.Button(conversionRect, icon_makeShared, ZUI.Style.RichButton)) {
                            toBeConverted = true;
                        }
                    }
                    else {
                        if (ZUI.Button(conversionRect, icon_reconnectToShared, ZUI.Style.RichButton)) {
                            toBeConverted = true;
                        }
                    }
                }
                else {
                    if (ZUI.Button(conversionRect, icon_breakToLocal, ZUI.Style.RichButton)) {
                        toBeConverted = true;
                    }
                }
            }
            else if (zound is Zequence zeq2) {
                if (!entry.local) {
                    if (ZUI.Button(conversionRect, icon_breakToLocal, ZUI.Style.RichButton)) {
                        toBeConverted = true;
                    }
                }
            }

            var reorderRect = new Rect(conversionRect.x, conversionRect.yMax + 2f, conversionRect.width, conversionRect.height);
            var reorderUpRect = new Rect(reorderRect.x, reorderRect.y, reorderRect.width / 2f, reorderRect.height);
            var reorderDownRect = new Rect(reorderUpRect.xMax, reorderUpRect.y, reorderUpRect.width, reorderUpRect.height);

            var zoundEntries = parentZound.zoundEntries;
            var guiEnabled = GUI.enabled;
            GUI.enabled = guiEnabled && entryIndex > 0;
            if (ZUI.Button(reorderUpRect, reorderUpLabel, ZUI.Style.RichButton, ZUICornerMask.Top)) {
                ZoundsWindow.ModifyZoundsProject("reorder up", () => {
                    var temp = zoundEntries[entryIndex - 1];
                    zoundEntries[entryIndex - 1] = zoundEntries[entryIndex];
                    zoundEntries[entryIndex] = temp;
                });
            }
            GUI.enabled = guiEnabled && entryIndex < (zoundEntries.Count - 1);
            if (ZUI.Button(reorderDownRect, reorderDownLabel, ZUI.Style.RichButton, ZUICornerMask.Bottom)) {
                ZoundsWindow.ModifyZoundsProject("reorder down", () => {
                    var temp = zoundEntries[entryIndex + 1];
                    zoundEntries[entryIndex + 1] = zoundEntries[entryIndex];
                    zoundEntries[entryIndex] = temp;
                });
            }
            GUI.enabled = guiEnabled;
        }

        private static void FlashEntry(Rect rect) {
            var prevColor = GUI.color;
            var colorStart = new Color(1f, 1f, 1f, 0f);
            var colorEnd = new Color(1f, 1f, 1f, 0.25f);
            float t = (Time.realtimeSinceStartup % 0.5f) / 0.5f;
            t = 4 * t * (1 - t); // yoyo interpolation
            GUI.color = Color.Lerp(colorStart, colorEnd, t);
            GUI.DrawTexture(rect, EditorGUIUtility.whiteTexture);
            GUI.color = prevColor;
        }

        private void AddNewEntryFromExisting(CompositeZound parentZound) {
            var zoundsProject = ZoundsProject.Instance;
            var library = zoundsProject.zoundLibrary;

            List<Zound> allZounds = library.GetAllZounds();
            var sortedZounds = allZounds.OrderBy(z => z.name).ToList();

            var genericMenu = new GenericMenu();
            foreach (var z in sortedZounds) {
                if (z.id == parentZound.id) continue;
                if (z is CompositeZound cz && ZequenceHandler.CheckRecursiveness(cz, parentZound)) {
                    continue;
                }

                var zound = z;
                genericMenu.AddItem(new GUIContent(zound.GetType().Name + "/" + zound.name), false, userData => {
                    AddNewZoundEntry(parentZound, zound, false);

                }, zound);
            }

            GenericMenuPopup.Show(
                genericMenu,
                "Add Zound(s)",
                Event.current.mousePosition,
                new List<string>(),
                addMenuSearchText,
                newSearch => addMenuSearchText = newSearch,
                userData => ZoundEngine.PlayZound(userData as Zound));
        }

        internal void AddNewZoundEntry(CompositeZound parentZound, Zound zound, bool local) {
            ZoundsWindow.ModifyZoundsProject("add local zound entry", () => {
                var newEntry = new CompositeZound.ZoundEntry();
                newEntry.zoundId = zound.id;
                newEntry.local = local;
                parentZound.zoundEntries.Add(newEntry);
                RecalculateMaxDuration();
            });
            ValidateEnvelopeGUIs();
        }

        internal static void AddNewZoundEntryNoEditor(CompositeZound parentZound, Zound zound, bool local) {
            ZoundsWindow.ModifyZoundsProject("add local zound entry", () => {
                var newEntry = new CompositeZound.ZoundEntry();
                newEntry.zoundId = zound.id;
                newEntry.local = local;
                parentZound.zoundEntries.Add(newEntry);
            });
        }


        protected virtual void DrawRenderToKlipExtras() { }

        private void RecalculateMaxDuration() {
            float max = CalculateCompositeDuration(targetZound, 1f);
            if (max > targetZound.editor_maxDuration) {
                targetZound.editor_maxDuration = max;
                EditorUtility.SetDirty(ZoundsProject.Instance);
            }
            if (autoDuration) AutoApplyDuration();
        }

        // Subclasses set this to true when the Auto-duration toggle is on.
        protected virtual bool autoDuration => false;

        // Sets editor_maxDuration to exactly the computed content duration.
        protected void AutoApplyDuration() {
            float exact = CalculateCompositeDuration(targetZound, 1f);
            if (!Mathf.Approximately(exact, targetZound.editor_maxDuration)) {
                targetZound.editor_maxDuration = exact;
                EditorUtility.SetDirty(ZoundsProject.Instance);
            }
        }

        protected float CalculateCompositeDuration(CompositeZound compositeZound, float parentPitch) {
            float max = 0f;
            foreach (var entry in compositeZound.zoundEntries) {
                if (!compositeZound.TryGetEntryZound(entry, out var zound)) continue;
                if (zound is CompositeZound cz && ZequenceHandler.CheckRecursiveness(cz, compositeZound)) {
                    Debug.LogError(compositeZound.name + " is contained recursively in " + cz.name);
                    continue;
                }
                float effectiveDuration = GetEntryDuration(compositeZound, entry, parentPitch) + (entry.delay / parentPitch);
                if (effectiveDuration > max) {
                    max = effectiveDuration;
                    //Debug.Log("New max - " + zound.name + ": " + max);
                }
            }
            //Debug.Log(compositeZound.name + ": " + max);
            return max;
        }

        private float GetEntryDuration(CompositeZound parentZound, CompositeZound.ZoundEntry entry, float parentPitch) {
            if (!parentZound.TryGetEntryZound(entry, out var zound)) return 0f;

            float effectivePitch = entry.pitch;
            if (!entry.overridePitch) {
                //effectivePitch *= (zound.maxPitch + zound.minPitch) / 2f;
                // no more middle values
                effectivePitch *= zound.minPitch;
            }

            effectivePitch *= parentPitch;

            float zoundDuration;
            if (zound is Klip klip) {
                // Priority 1: Use the actual rendered clip if it exists and is valid.
                // This is the absolute truth of what the user will hear.
                if (klip.HasActiveEdits() && !string.IsNullOrEmpty(klip.renderedClipPath)) {
                    var renderedClip = AssetDatabase.LoadAssetAtPath<AudioClip>(klip.renderedClipPath);
                    if (renderedClip != null) {
                        return renderedClip.length / parentPitch;
                    }
                }

                // Priority 2: Fallback to calculation if no render exists yet (newly created or duplicated).
                // We use trim settings as a placeholder for the visual width.
                if (klip.trimEnabled) {
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
    }

}
