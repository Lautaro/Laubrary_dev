using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Laubrary.Zounds {

    public class KlipEditorWindow : BaseZoundEditorWindow<Klip, KlipEditorWindow>, IHasCustomMenu {

        /// <summary>
        /// Opens this sound's UI Toolkit twin (T-0456, owner's decision D3). In the window's own menu (the tab's ⋮)
        /// rather than as a button in the window, so the old window's layout stays exactly what the twin is compared
        /// against. Temporary: goes when the old window retires.
        /// </summary>
        public void AddItemsToMenu(GenericMenu menu) {
            if (targetZound == null) return;
            menu.AddItem(new GUIContent("Open UI Toolkit version"), false, () => Uitk.KlipEditorWindowTK.Open(targetZound, isLocalZound));
        }


        [SerializeField] private AudioSpectrumView spectrumView;


        private bool notFoundErrorAlreadyShown;

        private bool isDraggingSlider = false;
        private bool isDraggingWaveform;
        private bool autoRenderPending;
        private double lastRenderEditTime;
        private const double AutoRenderIdleSeconds = 0.25;
        private UnityEngine.AddressableAssets.AssetReference cachedOutputRef;
        private AudioClip cachedOutputAsset;
        private bool outputCacheValid;

        public static KlipEditorWindow OpenWindow(Klip klip) {
            return OpenWindow<KlipEditorWindow>(klip, new Vector2(479.2f, 400f));
        }

        protected override Klip FindZoundTarget() {
            var library = ZoundsProject.Instance.zoundLibrary;
            var result = library.klips.Find(k => k.id == targetZoundID);
            if (result == null) {
                foreach (var zequence in library.zequences) {
                    result = zequence.localKlips.Find(k => k.id == targetZoundID);
                    if (result != null) break;
                    foreach (var localZequence in zequence.localZequences) {
                        result = localZequence.zequence.localKlips.Find(k => k.id == targetZoundID);
                        if (result != null) break;
                    }
                    if (result != null) break;
                }
            }
            if (result == null) {
                if (!notFoundErrorAlreadyShown) {
                    notFoundErrorAlreadyShown = true;
                    Debug.LogError("Can't find klip target for zound id: " + targetZoundID);
                }
            }
            return result;
        }

        protected override void OnInit() {
            if (spectrumView != null) spectrumView.Destroy();
            spectrumView = new AudioSpectrumView(this);
            spectrumView.height = 100f; // Set a default height
            RefreshSpectrumView();
            RegisterSpectrumViewEvents();
            EditorApplication.update -= ProcessPendingAutoRender;
            EditorApplication.update += ProcessPendingAutoRender;
            EditorApplication.update -= TickContinuousRepaint;
            EditorApplication.update += TickContinuousRepaint;
        }

        /// <summary>
        /// Asks for a redraw once per editor tick, while anything in the chain editor is actually moving.
        ///
        /// **Why a redraw cannot be requested from inside the drawing, which is what this replaces.** A window that draws
        /// itself, notices something is moving and asks to be drawn again only keeps going for as long as it is being
        /// drawn — and a window without focus is drawn rarely and at the editor's convenience. So the request inherited
        /// exactly the rate it was trying to escape: the analyser animated slowly, unevenly, sped up when the mouse was
        /// over it, and sometimes stopped altogether. Asking from the editor's own tick instead is outside that loop, and
        /// runs whether this window has focus or not. The standalone analyser window has always done it this way; this is
        /// that same pattern, not a new one.
        ///
        /// Still gated on something actually moving, which the chain editor already knows and reports, so an idle editor
        /// costs nothing.
        /// </summary>
        void TickContinuousRepaint() {
            if (chainEditor == null || !chainEditor.wantsContinuousRepaint) return;
            // Sixty times a second is smooth motion; the editor's own tick can run far faster than that, and redrawing a
            // whole editor window on every one of them would cost real time for no visible gain.
            double now = EditorApplication.timeSinceStartup;
            if (now - lastContinuousRepaint < 1.0 / 60.0) return;
            lastContinuousRepaint = now;
            Repaint();
        }
        double lastContinuousRepaint;

        protected override void OnBaseDisable() {
            EditorApplication.update -= ProcessPendingAutoRender;
            EditorApplication.update -= TickContinuousRepaint;
            if (isDraggingWaveform || isDraggingSlider) {
                isDraggingWaveform = false;
                isDraggingSlider = false;
                ZoundsWindow.EndDragUndo();
            }
            if (autoRenderPending && targetZound != null && targetZound.needsRender) Render();
            // No revert/alert on close — edits are non-destructive.
            // Disabling trim, envelopes, gain boost, or EQ always restores the source.
        }

        protected override void OnDestroy() {
            EditorApplication.update -= ProcessPendingAutoRender;
            EditorApplication.update -= TickContinuousRepaint;
            chainEditor?.Dispose();
            if (spectrumView != null) {
                spectrumView.Destroy();
                spectrumView = null;
            }
            base.OnDestroy();
        }

        private void RefreshSpectrumView() {
            cachedOutputRef = null;
            cachedOutputAsset = null;
            outputCacheValid = false;
            if (targetZound != null && spectrumView != null) {
                ValidateKlip();
                spectrumView.InitFromKlip(targetZound, useChainEnvelopes: true);
            }
        }

        protected override void OnFocus() {
            base.OnFocus();
            outputCacheValid = false;
            if (spectrumView != null && targetZound != null && spectrumView.NeedsSourceRefresh(targetZound)) {
                RefreshSpectrumView();
            }
        }

        private AudioClip ResolveOutputAsset() {
            var outputRef = targetZound.outputClipRef ?? targetZound.renderedClipRef;
            if (!outputCacheValid || cachedOutputRef != outputRef) {
                cachedOutputRef = outputRef;
                try { cachedOutputAsset = outputRef == null ? null : outputRef.editorAsset as AudioClip; }
                catch { cachedOutputAsset = null; }
                outputCacheValid = true;
            }
            return cachedOutputAsset;
        }

        /// <summary>
        /// Deliberately does nothing now.
        ///
        /// Editing a sound used to quietly schedule a re-render, writing a new audio file with the edits burnt into
        /// it, because that file was the only thing playback ever played. Playback now reads the sound's original
        /// audio and applies its chain as it goes, so that file is no longer consumed by anything — and writing one
        /// per edit was the direct cause of a project filling up with small near-duplicate audio files.
        ///
        /// Rendering itself is kept and still works, because bouncing a sound to a file is occasionally genuinely
        /// wanted. It is simply no longer something that happens to you as a side effect of moving a slider: it has
        /// to be asked for.
        ///
        /// The call sites are left in place rather than removed. They mark the points where an edit changes the
        /// audible result, which is information worth keeping if this ever needs to invalidate something again.
        /// </summary>
        private void QueueAutoRender() {
        }

        private void ProcessPendingAutoRender() {
            if (!autoRenderPending || isDraggingWaveform || isDraggingSlider) return;
            if (EditorApplication.timeSinceStartup - lastRenderEditTime < AutoRenderIdleSeconds) return;
            autoRenderPending = false;
            if (targetZound == null || !targetZound.needsRender) return;
            Render();
            Repaint();
        }

        private void RegisterSpectrumViewEvents() {
            if (spectrumView == null) return;
            WireSpectrumView(spectrumView, () => targetZound, dragging => isDraggingWaveform = dragging, RefreshSpectrumView, QueueAutoRender, Repaint);
        }

        /// <summary>
        /// What each edit on the waveform view does to the Klip: the Undo step a drag opens, trim and clamp written through
        /// the project's modify path, the volume and pitch toggles creating or enabling the chain modulators, and envelope
        /// edits marking the chain changed. Shared with the UI Toolkit twin (T-0468) so both windows edit the sound the
        /// same way; the window supplies its own target, drag flag, refresh, render queue and repaint.
        /// </summary>
        internal static void WireSpectrumView(AudioSpectrumView spectrumView, System.Func<Klip> target, System.Action<bool> setDragging,
                                              System.Action refreshSpectrumView, System.Action queueAutoRender, System.Action repaint) {
            spectrumView.onTrimDragStarted = () => {
                if (target() != null) {
                    setDragging(true);
                    ZoundsWindow.BeginDragUndo("change klip trim");
                }
            };

            spectrumView.onVolumeDragStarted = () => {
                if (target() != null) {
                    setDragging(true);
                    ZoundsWindow.BeginDragUndo("edit volume envelope");
                }
            };

            spectrumView.onPitchDragStarted = () => {
                if (target() != null) {
                    setDragging(true);
                    ZoundsWindow.BeginDragUndo("edit pitch envelope");
                }
            };

            spectrumView.onTrimEnabledChanged = enabled => {
                var targetZound = target();
                if (targetZound != null) {
                    ZoundsWindow.ModifyAndSaveZoundsProject("toggle klip trim", () => {
                        targetZound.trimEnabled = enabled;
                        targetZound.needsRender = true;
                        queueAutoRender();
                    });
                } else {
                    Debug.LogWarning("[Zounds] KlipEditor: onTrimEnabledChanged fired but targetZound is NULL.");
                }
            };
            spectrumView.onTrimStartChanged = trimStart => {
                var targetZound = target();
                if (targetZound != null) {
                    targetZound.trimStart = trimStart;
                    targetZound.needsRender = true;
                    // A playing Looper hears its new loop start at once (T-0473).
                    if (targetZound.IsLooper) Dsp.SapVoiceRegistry.PushLoop(targetZound);
                    queueAutoRender();
                    repaint();
                }
            };

            spectrumView.onTrimEndChanged = trimEnd => {
                var targetZound = target();
                if (targetZound != null) {
                    targetZound.trimEnd = trimEnd;
                    targetZound.needsRender = true;
                    if (targetZound.IsLooper) Dsp.SapVoiceRegistry.PushLoop(targetZound);
                    queueAutoRender();
                    repaint();
                }
            };

            spectrumView.onClampToTrimChanged = clamp => {
                var targetZound = target();
                if (targetZound != null) {
                    ZoundsWindow.ModifyAndSaveZoundsProject("toggle klip clamp-to-trim", () => {
                        targetZound.clampToTrim = clamp;
                        targetZound.needsRender = true;
                        queueAutoRender();
                    });
                }
            };

            spectrumView.onVolumeEnvelopeChanged = envelope => {
                var targetZound = target();
                if (targetZound != null) {
                    // The envelope handed back here IS the chain modulator's own curve object
                    // (KlipChainEnvelopes hands out the live reference), already mutated in place by the
                    // drag — there's nothing to copy back. It's a structural change to the chain rather
                    // than a single live-pushable parameter, so it takes effect on the next play, not
                    // mid-drag: just mark the chain dirty so playback rebuilds its layout next time.
                    KlipChainEnvelopes.Touch(targetZound);
                    repaint();
                }
            };

            spectrumView.onVolumeEnabledChanged = enabled => {
                var targetZound = target();
                if (targetZound != null) {
                    ZoundsWindow.ModifyAndSaveZoundsProject("toggle klip volume", () => {
                        KlipChainEnvelopes.SetVolumeEnabled(targetZound, enabled);
                    });
                    // The toggle may have just created the volume modifier — re-read it so the overlay
                    // (and the next drag) reference the real curve instead of the disabled placeholder.
                    refreshSpectrumView();
                }
            };

            spectrumView.onPitchEnvelopeChanged = envelope => {
                var targetZound = target();
                if (targetZound != null) {
                    // See onVolumeEnvelopeChanged above.
                    KlipChainEnvelopes.Touch(targetZound);
                    repaint();
                }
            };

            spectrumView.onPitchEnabledChanged = enabled => {
                var targetZound = target();
                if (targetZound != null) {
                    ZoundsWindow.ModifyAndSaveZoundsProject("toggle klip pitch", () => {
                        KlipChainEnvelopes.SetPitchEnabled(targetZound, enabled);
                    });
                    refreshSpectrumView();
                }
            };

        }

        protected override void OnUndoRedoPerformed() {
            targetZound = FindZoundTarget();
            RefreshSpectrumView();
            if (ZoundsProject.Instance.projectSettings.editorStyle.autoRender) {
                targetZound.needsRender = true;
                QueueAutoRender();
            }
        }

        private void OnLostFocus() {
            if (isDraggingWaveform || isDraggingSlider) {
                isDraggingWaveform = false;
                isDraggingSlider = false;
                ZoundsWindow.EndDragUndo();
                QueueAutoRender();
            }
            if (spectrumView != null) {
                spectrumView.ResetStates();
            }
        }

        protected override bool OnDrawGUI() {
            var evt = Event.current;
            if (autoRenderPending && (evt.type == EventType.MouseMove || evt.type == EventType.MouseDrag ||
                                      evt.type == EventType.MouseDown || evt.type == EventType.ScrollWheel ||
                                      evt.type == EventType.KeyDown)) {
                lastRenderEditTime = EditorApplication.timeSinceStartup;
            }

            // Check for MouseUp to trigger a final render after dragging ends
            bool mouseReleased = evt.type == EventType.MouseUp || evt.type == EventType.Ignore;

            // Handle slider drag end for gain and EQ
            if (mouseReleased && isDraggingSlider) {
                isDraggingSlider = false;
                ZoundsWindow.EndDragUndo(() => {
                    targetZound.needsRender = true;
                    QueueAutoRender();
                });
            }

            ZUI.RowSpace(); // before klip name row
            var fieldsRect = GUILayoutUtility.GetRect(1f, EditorGUIUtility.singleLineHeight, GUILayout.ExpandWidth(true));

            // Validate that we have a clip to edit. A Klip with no reference at all is a legitimate
            // placeholder (reserving a sound cue before real audio exists) — it's allowed to stay
            // and reach the Source field below so a clip can be assigned whenever it's ready.
            bool hasInternalSource = targetZound != null && targetZound.audioClipRef != null && targetZound.audioClipRef.RuntimeKeyIsValid();
            bool hasExternalSource = targetZound != null && !string.IsNullOrEmpty(targetZound.externalSourcePath);
            bool hasValidClip = hasInternalSource || hasExternalSource;
            if (!hasValidClip) {
                EditorGUILayout.HelpBox("No audio assigned yet. Assign one in the 'Clip References' tab or the Source field below.", MessageType.Info);
            }

            EditorGUI.BeginChangeCheck();
            inspector.DrawSimple(fieldsRect, targetZound, isLocalZound);
            if (EditorGUI.EndChangeCheck()) {
                RefreshWindowName();
            }

            ZUI.RowSpace(); // after klip name row

            bool remove = false;
            bool convertToZequence = false;

            using (ZUI.Box())
            {

            ZUI.RowSpace(); // top of content box
            var guiColor = GUI.color;
            var guiEnabled = GUI.enabled;
            var labelWidth = EditorGUIUtility.labelWidth;

            // Load source clip — internal (AssetReference) or external (disk path).
            AudioClip sourceAsset = spectrumView == null ? null : spectrumView.sourceClip;
            bool isExternalSource = !string.IsNullOrEmpty(targetZound.externalSourcePath);
            AudioClip outputAsset = ResolveOutputAsset();

            bool sourceAvailable = sourceAsset != null;
            if (!sourceAvailable && outputAsset == null && hasValidClip) {
                // A reference was assigned (internal or external) but it no longer resolves —
                // genuinely broken, as opposed to hasValidClip == false, which is a Klip that
                // never had a reference assigned yet (a placeholder — handled above, and left
                // to fall through to the Source field below instead of dead-ending here).
                if (isExternalSource) {
                    EditorGUILayout.HelpBox($"External source file not found:\n{targetZound.externalSourcePath}", MessageType.Error);
                }
                else {
                    EditorGUILayout.HelpBox("Source Audio Clip is missing or invalid. Please fix it in the 'Clip References' tab.", MessageType.Error);
                }
                if (ZUI.Button("Close Window", ZUI.Style.Default)) Close();
                return false;
            }
            if (!sourceAvailable && hasValidClip) {
                EditorGUILayout.HelpBox("Source clip is not available on this machine. Waveform edits are disabled.\nSettings (volume, pitch, chance, routing, tags) remain editable.", MessageType.Info);
            }

            if (targetZound.parentId != 0) {
                if (ZoundDictionary.TryGetZoundById(targetZound.parentId, out var parentZound)) {
                    if (parentZound is CompositeZound parentComposite && parentComposite.localKlips.Find(k => k.id == targetZound.id) == null) {
                        // Close if this local klip is removed by its parent zequence
                        Close(); return false;
                    }
                }
            }

            // Source field — different UI for internal vs external sources.
            if (isExternalSource) {
                GUILayout.BeginHorizontal();
                EditorGUILayout.PrefixLabel("Source:");
                EditorGUILayout.SelectableLabel(Path.GetFileName(targetZound.externalSourcePath),
                    EditorStyles.textField, GUILayout.Height(EditorGUIUtility.singleLineHeight));
                if (GUILayout.Button("Browse", GUILayout.Width(60f))) {
                    // Defer the modal dialog to avoid corrupting the IMGUI layout stack.
                    string dir = Path.GetDirectoryName(targetZound.externalSourcePath);
                    EditorApplication.delayCall += () => {
                        string selected = EditorUtility.OpenFilePanel("Select Source Audio File", dir, "wav");
                        if (!string.IsNullOrEmpty(selected)) {
                            ZoundsWindow.ModifyZoundsProject("replace external source", () => {
                                targetZound.externalSourcePath = selected;
                                targetZound.needsRender = true;
                                RefreshSpectrumView();
                                RegisterSpectrumViewEvents();
                                QueueAutoRender();
                            });
                        }
                    };
                }
                if (GUILayout.Button("Reveal", GUILayout.Width(50f))) {
                    EditorUtility.RevealInFinder(targetZound.externalSourcePath);
                }
                GUILayout.EndHorizontal();
            }
            else {
                EditorGUI.BeginChangeCheck();
                var newSource = EditorGUILayout.ObjectField("Source:", sourceAsset, typeof(AudioClip), false) as AudioClip;
                if (EditorGUI.EndChangeCheck() && newSource != sourceAsset && newSource != null) {
#if ADDRESSABLES_INSTALLED
                    if (currentToken != null && currentToken.state == ZoundToken.State.Playing) {
                        currentToken.Kill();
                        currentToken = null;
                    }
                    ZoundsWindow.ModifyZoundsProject("replace source clip", () => {
                        var assetPath = AssetDatabase.GetAssetPath(newSource);
                        var assetGuid = AssetDatabase.AssetPathToGUID(assetPath);
                        var clipRef = new UnityEngine.AddressableAssets.AssetReference(assetGuid);
                        targetZound.audioClipRef = clipRef;
                        targetZound.audioClipPath = assetPath;
                        if (!ReferenceEquals(outputAsset, null)) {
                            targetZound.needsRender = true;
                        }
                        RefreshSpectrumView();
                        RegisterSpectrumViewEvents();
                        QueueAutoRender();
                    });
#endif
                }
            }

            // An "Output" row used to sit here, naming the rendered file — because that file was what playback
            // actually played, so which one it was mattered. It is removed rather than relabelled: playback now reads
            // the source and applies the chain as it goes, so pointing at a rendered file would state something
            // untrue about how the sound is produced, and a stale one would look authoritative.
            //
            // The rendered file is still looked up, for one narrow reason kept just below: when the SOURCE is missing,
            // it is the only audio left to draw a waveform from, and showing that beats showing nothing.

            GUI.enabled = guiEnabled;
            EditorGUIUtility.labelWidth = labelWidth;

            // When source is unavailable, use the output clip for waveform display.
            if (!sourceAvailable && outputAsset != null && spectrumView != null) {
                spectrumView.audioSource.clip = outputAsset;
            }

            if (spectrumView != null) {
                scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

                ZUI.RowSpace(); // above waveform

                // For the spectrum view, we use a fixed height or calculate it based on window
                float spectrumHeight = 150f; 
                spectrumView.height = spectrumHeight;

                ZoundEngine.CullingGroups.TryGetValue(targetZound, out var playingTokens);
                spectrumView.renderedClip = null;
                spectrumView.DrawLayout(playingTokens);

                // On MouseUp: close the undo group opened on MouseDown, persist to JSON,
                // and optionally render. Everything is collapsed into the single named entry.
                if (mouseReleased && targetZound.needsRender && sourceAvailable) {
                    ZoundsWindow.EndDragUndo(() => {
                        ValidateKlip();
                        QueueAutoRender();
                    });
                    isDraggingWaveform = false;
                }
                else if (mouseReleased) {
                    ZoundsWindow.EndDragUndo();
                    isDraggingWaveform = false;
                }

                ZUI.RowSpace();
                GUILayout.BeginHorizontal();
                {
                    const float btnHeight = 20f;

                    // Disable edit controls when source is not available (non-audio machine).
                    bool prevGuiEnabled = GUI.enabled;
                    if (!sourceAvailable) GUI.enabled = false;

                    // Group 1: File Actions
                    if (ZUI.Button("Render", ZUI.Style.RichButton, ZUICornerMask.All, GUILayout.Height(btnHeight), GUILayout.Width(60f))) {
                        ValidateKlip();
                        Render();
                    }

                    GUILayout.Space(4f);

                    if (ZUI.Button("Remove", ZUI.Style.RichButton, ZUI.Tint.Danger, ZUICornerMask.All, GUILayout.Height(btnHeight), GUILayout.Width(70f))) {
                        if (AudioAssetUtility.DisplayZoundRemoveDialog(targetZound)) {
                            remove = true;
                        }
                    }

                    // A local klip (parentId != 0) already lives inside a Zequence — converting it
                    // to a top-level Zequence would orphan it from its parent's zoundEntries, so
                    // this is only offered for top-level Klips (matches ConvertKlipToZequence, which
                    // moves the Klip out of zoundLibrary.klips and in as the first entry of a new
                    // top-level Zequence).
                    if (targetZound.parentId == 0 && ZoundsProject.Instance.browserSettings.showConvertToZequence) {
                        GUILayout.Space(4f);
                        if (ZUI.Button("Convert to Zeq", ZUI.Style.RichButton, ZUICornerMask.All, GUILayout.Height(btnHeight), GUILayout.Width(100f))) {
                            if (EditorUtility.DisplayDialog("Convert to Zequence: " + targetZound.name,
                                "Convert this Klip into a Zequence containing it as a local klip?\n" + targetZound.name, "Convert", "Cancel")) {
                                convertToZequence = true;
                            }
                        }
                    }

                    GUILayout.FlexibleSpace();

                    // The row of fixed on/off toggles for the seven built-in effects used to be here, and it is
                    // deliberately gone. Those effects only ever existed by being rendered into a new audio file
                    // ahead of time, which is why every one of them marked the sound as needing a re-render and why
                    // a project accumulated a small file per sound per revision.
                    //
                    // The effect chain below replaces them and is strictly more capable: any of sixteen effects in
                    // any order and any number, each with modulators driving its parameters, applied as the sound
                    // plays so an edit is heard immediately. Keeping both would have meant two authoring surfaces
                    // for overlapping jobs, only one of which could be heard without re-rendering -- and the old one
                    // sitting at the top is exactly why this window still looked like the version before chains.
                    //
                    // A sound authored with the old settings does not lose them: they are converted to an equivalent
                    // chain, either permanently through the migration action in the Zounds menu, or on the fly when
                    // the sound plays. Either way they become visible and editable in the chain editor.

                    // Restore GUI.enabled for display toggles and play button.
                    GUI.enabled = prevGuiEnabled;

                    GUILayout.Space(4f);

                    // Two toggles used to sit here and both are removed, because each had stopped controlling anything.
                    // "Preview" showed the second waveform that is gone, and "Auto Render" switched on a re-render
                    // that no longer happens on an edit. A control that does nothing when clicked is worse than one
                    // that is absent: it invites someone to conclude the feature behind it is broken.

                    // The owner's stutter test on demand (T-0448): a forced collection while this plays.
                    if (ZUI.Button(new GUIContent("Force GC", EditorTools.ZoundGcStressTest.Tooltip + "\n\n" + EditorTools.ZoundGcStressTest.lastResult),
                            ZUI.Style.RichButton, null, ZUICornerMask.All, GUILayout.Height(btnHeight), GUILayout.Width(72f)))
                        EditorTools.ZoundGcStressTest.Run(IsCurrentTokenPlaying());

                    GUILayout.Space(8f);

                    // Group 4: Play
                    var audioSource = spectrumView.audioSource;
                    GUI.enabled = audioSource != null;
                    bool isPlaying = IsCurrentTokenPlaying();
                    if (ZUI.Button(
                            !GUI.enabled || !isPlaying ? "Play" : "Stop",
                            ZUI.Style.RichButton,
                            isPlaying ? ZUI.Tint.Danger : null,
                            ZUICornerMask.All,
                            GUILayout.Height(btnHeight),
                            GUILayout.Width(60f))) {
                        if (currentToken != null && currentToken.state == ZoundToken.State.Playing) {
                            currentToken.Kill();
                            currentToken = null;
                        }
                        else {
                            SimulatePlay();
                        }
                    }
                    GUI.enabled = guiEnabled;
                }
                GUILayout.EndHorizontal();

                ZUI.RowSpace(2f);

                // A Looper (T-0473) is authored in the UI Toolkit Klip window; this one only says so, honestly.
                if (targetZound.IsLooper) {
                    float loopLen = sourceAsset != null
                        ? (targetZound.trimEnabled && targetZound.trimEnd > targetZound.trimStart ? Mathf.Min(targetZound.trimEnd, sourceAsset.length) : sourceAsset.length)
                          - (targetZound.trimEnabled ? targetZound.trimStart : 0f)
                        : 0f;
                    targetZound.loop.Effective(loopLen, out float xLo, out float xHi);
                    ZUI.Label("Looper · crossmix " + (xHi <= 0f ? "off" : Mathf.Approximately(xLo, xHi) ? xHi.ToString("0.00") + " s" : xLo.ToString("0.00") + "–" + xHi.ToString("0.00") + " s")
                              + " · edited in the new Klip window");
                }

                // === Time-stretch (source material: duration without pitch change) ===
                ZUI.RowSpace();
                if (stretchEditor == null) stretchEditor = new TimeStretchGUI();
                if (chainEditor == null) chainEditor = new ChainEditorGUI();
                var editorForMenu = chainEditor;
                var zoundForMenu = targetZound;
                stretchEditor.Draw(targetZound, sourceAsset, p => editorForMenu.ShowSourceParamMenu(zoundForMenu, p));

                // === Effect chain (per-voice DSP: nodes, modifiers, library presets) ===
                ZUI.RowSpace();
                chainEditor.Draw(targetZound);
                isDraggingSlider = isDraggingSlider || chainEditor.isDragging || stretchEditor.isDragging;
                // Deliberately NOT asking for a redraw from here. Keeping a live view alive is done from the editor's own
                // tick instead (see TickContinuousRepaint), because a redraw requested from inside a draw only happens as
                // often as the window is already being drawn — which, without focus, is barely at all.

                // The bodies of the seven built-in effects used to be drawn here, one section per enabled effect —
                // the gain boost, the equaliser and so on. Gone for the same reason their toggles went: they existed
                // only to be rendered into a file, the chain above expresses all of them and more, and keeping two
                // editors for overlapping jobs meant one of them could not be heard without re-rendering. The chain's
                // own Gain effect is the replacement for the gain boost, and it is heard as you drag it.
                //
                // The second waveform that used to sit below here is gone too. It drew the RENDERED file when one
                // existed and the source otherwise, so its whole purpose was to show what the bake had produced. With
                // no bake in the playback path it could only show one of two unhelpful things: the same source the
                // waveform above already shows, or a stale rendered file from before the current edits — which looks
                // authoritative and is not. The waveform above shows the source with the trim and the curves drawn on
                // it, which is what is actually about to be played.

                EditorGUILayout.EndScrollView();
            }

            } // end ZUI.Box

            if (convertToZequence) {
                BrowserTab.Instance?.ConvertKlipToZequence(targetZound);
                Close();
                return false;
            }

            return remove;
        }

        [SerializeField] private Vector2 scrollPos;
        private ChainEditorGUI chainEditor;
        private TimeStretchGUI stretchEditor;

        internal static float DrawEQBandSlider(Rect colRect, string label, float value, GUIStyle labelStyle) {
            float labelH  = EditorGUIUtility.singleLineHeight;
            float valueH  = EditorGUIUtility.singleLineHeight;
            float sliderH = colRect.height - labelH - valueH;

            var sliderRect = new Rect(colRect.x, colRect.y,              colRect.width, sliderH);
            var labelRect  = new Rect(colRect.x, sliderRect.yMax,        colRect.width, labelH);
            var valueRect  = new Rect(colRect.x, labelRect.yMax,         colRect.width, valueH);

            float newValue = ZUI.SliderVertical(sliderRect, value, -36f, 36f, label: "", style: ZUI.SliderStyle.SmallSlider, defaultValue: 0f);
            GUI.Label(labelRect, label, labelStyle);
            GUI.Label(valueRect, $"{newValue:+0.0;-0.0;0.0}", labelStyle);
            return newValue;
        }

        internal static float DrawFilterSlider(Rect totalRect, string label, float value, float min, float max, float resetValue, GUIStyle labelStyle) {
            const float k_PercentInputW = 34f;
            const float k_HzLabelW      = 58f;

            float logMin = Mathf.Log10(min);
            float logMax = Mathf.Log10(max);
            float t      = Mathf.InverseLerp(logMin, logMax, Mathf.Log10(Mathf.Clamp(value, min, max)));
            float resetT = Mathf.InverseLerp(logMin, logMax, Mathf.Log10(resetValue));

            float rowH     = totalRect.height * 0.5f;
            var   labelRow = new Rect(totalRect.x, totalRect.y, totalRect.width, rowH);
            var   sliderRow = new Rect(totalRect.x, totalRect.y + rowH, totalRect.width, totalRect.height - rowH);

            GUI.Label(labelRow, label, EditorStyles.miniLabel);

            float sliderW    = Mathf.Max(0f, sliderRow.width - k_PercentInputW - k_HzLabelW - 4f);
            var   sliderRect = new Rect(sliderRow.x, sliderRow.y, sliderW, sliderRow.height);
            var   inputRect  = new Rect(sliderRect.xMax + 2f, sliderRow.y, k_PercentInputW, sliderRow.height);
            var   hzRect     = new Rect(inputRect.xMax + 2f,  sliderRow.y, k_HzLabelW,      sliderRow.height);

            float newT = ZUI.Slider(sliderRect, t, 0f, 1f, label: "", style: ZUI.SliderStyle.SmallSlider,
                                    defaultValue: resetT, suppressValueField: true);

            EditorGUI.BeginChangeCheck();
            int percentDisplay = Mathf.RoundToInt(newT * 100f);
            int percentEdited  = EditorGUI.IntField(inputRect, percentDisplay, EditorStyles.miniTextField);
            if (EditorGUI.EndChangeCheck())
                newT = Mathf.Clamp01(percentEdited / 100f);

            float newValue = Mathf.Pow(10f, Mathf.Lerp(logMin, logMax, newT));
            newValue = Mathf.Clamp(newValue, min, max);
            GUI.Label(hzRect, $"{Mathf.Round(newValue)} Hz", labelStyle);

            return newValue;
        }

        internal static void DrawEQCurve(Rect rect, Klip klip) {
            Color bgColor   = ZUI.PaletteColor("EQ", new Color(0.1f, 0.1f, 0.1f, 1f));
            Color lineColor = ZUI.PaletteColor("EQ", Color.cyan);

            // Draw background
            EditorGUI.DrawRect(rect, bgColor);

            // Draw frequency grid lines (approximate log scale)
            Handles.color = new Color(0.3f, 0.3f, 0.3f, 0.5f);
            float[] gridFreqs = { 100, 1000, 10000 };
            foreach (var f in gridFreqs) {
                float x = Mathf.InverseLerp(Mathf.Log10(10), Mathf.Log10(22000), Mathf.Log10(f)) * rect.width;
                Handles.DrawLine(new Vector2(rect.x + x, rect.y), new Vector2(rect.x + x, rect.y + rect.height));
            }

            // Generate Curve Points
            int points = 100;
            Vector3[] curve = new Vector3[points];
            Handles.color = lineColor;

            for (int i = 0; i < points; i++) {
                float t = i / (float)(points - 1);
                // Logarithmic frequency scale from 10Hz to 22kHz
                float freq = Mathf.Pow(10, Mathf.Lerp(Mathf.Log10(10), Mathf.Log10(22000), t));

                float totalGain = 0;

                // Add EQ Bands influence
                totalGain += GetBandInfluence(freq, 60f,    klip.subGain,     0.7f);
                totalGain += GetBandInfluence(freq, 150f,   klip.lowGain,     0.8f);
                totalGain += GetBandInfluence(freq, 400f,   klip.lowMidGain,  1.0f);
                totalGain += GetBandInfluence(freq, 1000f,  klip.midGain,     1.0f);
                totalGain += GetBandInfluence(freq, 2500f,  klip.highMidGain, 1.0f);
                totalGain += GetBandInfluence(freq, 6000f,  klip.highGain,    0.8f);
                totalGain += GetBandInfluence(freq, 12000f, klip.airGain,     0.7f);

                // Add Filter cuts
                float filterCut = 0;
                if (freq < klip.hpFrequency) filterCut -= 40f * (1f - freq / klip.hpFrequency);
                if (freq > klip.lpFrequency) filterCut -= 40f * (freq / klip.lpFrequency - 1f);

                float y = Mathf.InverseLerp(36, -36, totalGain + filterCut) * rect.height;
                curve[i] = new Vector3(rect.x + t * rect.width, rect.y + y, 0);
            }

            Handles.DrawAAPolyLine(2f, curve);

            // Draw 0dB line
            Handles.color = new Color(1, 1, 1, 0.2f);
            float zeroY = rect.y + rect.height * 0.5f;
            Handles.DrawLine(new Vector2(rect.x, zeroY), new Vector2(rect.x + rect.width, zeroY));
        }

        internal static float GetBandInfluence(float freq, float center, float gain, float q) {
            // Simplified bell curve for visualization
            float width = center / q;
            float diff = Mathf.Abs(Mathf.Log10(freq) - Mathf.Log10(center));
            return gain * Mathf.Exp(-diff * diff * 5f); // 5f is a tuning constant for the visual "width"
        }

        protected override void OnDrawHeader() {
            GUILayout.Space(3f);
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
            if (!Application.isPlaying && targetZound.needsRender) {
                Render();
            }

            var needsRenderTemp = targetZound.needsRender;
            targetZound.needsRender = false; // Force playback of the rendered clip

            float targetPitch = Random.Range(targetZound.minPitch, targetZound.maxPitch);
            currentToken = ZoundEngine.PlayZound(targetZound, new ZoundArgs() {
                startImmediately = true,
                delay = 0f,
                volumeOverride = Random.Range(targetZound.minVolume, targetZound.maxVolume),
                pitchOverride = targetPitch,
                chanceOverride = 1f,
                useFixedAverageValues = false,
                bypassGlobalSolo = isLocalZound,
                ignoreCooldown = true
            });
            targetZound.needsRender = needsRenderTemp;
        }

        private void ValidateKlip() => ValidateKlip(targetZound);

        /// <summary>Keeps a Klip's trim inside its clip (and its gain above silence). Shared with the UI Toolkit twin.</summary>
        internal static void ValidateKlip(Klip targetZound) {
            var zoundsProject = ZoundsProject.Instance;
            if (targetZound.trimStart < 0) {
                targetZound.trimStart = 0;
                targetZound.needsRender = true;
                EditorUtility.SetDirty(zoundsProject);
            }
            if (targetZound.trimEnd < 0) {
                targetZound.trimEnd = 0;
                targetZound.needsRender = true;
                EditorUtility.SetDirty(zoundsProject);
            }
            if (targetZound.audioClipRef.editorAsset is AudioClip clip) {
                if (targetZound.trimStart > clip.length) {
                    targetZound.trimStart = clip.length;
                    targetZound.needsRender = true;
                    EditorUtility.SetDirty(zoundsProject);
                }
                if (targetZound.trimEnd <= targetZound.trimStart) {
                    targetZound.trimEnd = clip.length;
                    targetZound.needsRender = true;
                    Debug.Log($"[Zounds] ValidateKlip: Fixed trimEnd (was {targetZound.trimEnd}, now {clip.length})");
                    EditorUtility.SetDirty(zoundsProject);
                }
                
                // NEW: Ensure gain is never exactly 0 (which causes silence). If it's 0 (the default/reset state), fix to 1.0.
                if (Mathf.Abs(targetZound.gain) < 0.0001f) {
                    targetZound.gain = 1f;
                    targetZound.needsRender = true;
                    Debug.Log("[Zounds] ValidateKlip: Auto-corrected gain 0.0 to 1.0.");
                    EditorUtility.SetDirty(zoundsProject);
                }
            }
        }

        /// <summary>
        /// Replaces characters that are invalid in Addressable addresses or on common file systems
        /// with safe equivalents so rendered WAV file paths can be registered without errors.
        /// </summary>
        private static string SanitizeFileName(string name) {
            return name
                .Replace('[', '(')
                .Replace(']', ')')
                .Replace('*', '_');
        }

        /// <summary>
        /// Returns the single canonical output path for a Klip in ZoundFiles/.
        /// All output operations (render, no-edit copy) write to this path.
        /// </summary>
        private static string GetStableOutputPath(Klip klip) {
            var settings = ZoundsProject.Instance.projectSettings;

            // If the Klip already has an output path, reuse it.
            if (!string.IsNullOrEmpty(klip.outputClipPath)) {
                return klip.outputClipPath.Replace('\\', '/');
            }

            // Also reuse the rendered path if it exists (migration from pre-output-promotion Klips).
            if (!string.IsNullOrEmpty(klip.renderedClipPath)) {
                return klip.renderedClipPath.Replace('\\', '/');
            }

            // Generate a new stable path.
            string zoundName = SanitizeFileName(klip.name);
            if (klip.parentId != 0) {
                zoundName += " (" + klip.parentId + ")";
            }
            string filePath = Path.Combine(settings.zoundFilesFolderPath, zoundName + ".wav").Replace('\\', '/');

            // A file already at this path is this Klip's own earlier output unless another Klip claims it
            // (the reference can be lost while the file stays, e.g. after the project is reloaded from disk).
            // Only a path owned by another Klip forces a suffixed name; otherwise reuse it.
            if (AssetDatabase.LoadAssetAtPath<AudioClip>(filePath) != null && IsPathOwnedByAnotherKlip(filePath, klip)) {
                filePath = Path.Combine(settings.zoundFilesFolderPath, zoundName + "_" + klip.id + ".wav").Replace('\\', '/');
            }

            return filePath;
        }

        private static bool IsPathOwnedByAnotherKlip(string assetPath, Klip klip) {
            bool owned = false;
            ZoundsProject.Instance.zoundLibrary.ForEachZound(z => {
                if (z is Klip other && !ReferenceEquals(other, klip)) {
                    if (SamePath(other.outputClipPath, assetPath) || SamePath(other.renderedClipPath, assetPath)) {
                        owned = true;
                        return true;
                    }
                }
                return false;
            });
            return owned;
        }

        private static bool SamePath(string a, string b) {
            return !string.IsNullOrEmpty(a) && a.Replace('\\', '/') == b.Replace('\\', '/');
        }

        /// <summary>
        /// Ensures a Klip has an output clip in ZoundFiles/ so it can ship without the source file.
        /// All output operations write to a single stable path per Klip — no duplicates, no orphans.
        /// For edited Klips: the render result is the output (same file).
        /// For no-edit Klips: a byte-copy of the source is the output.
        /// </summary>
        public static void PromoteOutputClip(Klip klip) {
            if (klip == null) return;
            var zoundsProject = ZoundsProject.Instance;
            var settings = zoundsProject.projectSettings;

#if ADDRESSABLES_INSTALLED
            string outputPath = GetStableOutputPath(klip);

            // If the output already exists and is valid, nothing to do.
            if (klip.outputClipRef != null && klip.outputClipRef.RuntimeKeyIsValid()
                && klip.outputClipPath == outputPath
                && AssetDatabase.LoadAssetAtPath<AudioClip>(outputPath) != null) {
                return;
            }

            // An edited Klip's output is its render, never a copy of the raw source: adopt the existing render,
            // or render now if there is none. Copying the source here would overwrite the render.
            if (klip.HasActiveEdits()) {
                string renderedPath = string.IsNullOrEmpty(klip.renderedClipPath) ? null : klip.renderedClipPath.Replace('\\', '/');
                bool hasRender = klip.renderedClipRef != null && klip.renderedClipRef.RuntimeKeyIsValid()
                    && renderedPath != null && AssetDatabase.LoadAssetAtPath<AudioClip>(renderedPath) != null;
                if (hasRender) {
                    var renderRef = EnsureClipAddressable(renderedPath);
                    ZoundsWindow.ModifyZoundsProject("ensure output clip", () => {
                        klip.outputClipRef = renderRef;
                        klip.outputClipPath = renderedPath;
                    });
                    EditorUtility.SetDirty(zoundsProject);
                    AssetDatabase.SaveAssets();
                }
                else {
                    RenderToAudioClip(klip, true);
                }
                return;
            }

            // Determine the absolute source file path (internal or external).
            bool hasExternal = !string.IsNullOrEmpty(klip.externalSourcePath);
            bool hasInternal = klip.audioClipRef != null && klip.audioClipRef.RuntimeKeyIsValid();
            if (!hasExternal && !hasInternal) return;

            string absSourceFile = null;
            if (hasExternal) {
                if (!File.Exists(klip.externalSourcePath)) return;
                absSourceFile = klip.externalSourcePath;
            }
            else {
                AudioClip sourceAsset = null;
                try { sourceAsset = klip.audioClipRef.editorAsset as AudioClip; } catch { }
                if (sourceAsset == null) return;
                string sourcePath = AssetDatabase.GetAssetPath(sourceAsset);
                if (string.IsNullOrEmpty(sourcePath)) return;
                absSourceFile = Path.GetFullPath(Path.Combine(Application.dataPath, sourcePath.Substring("Assets/".Length)));
            }

            // Ensure the target directory exists.
            string absDir = Path.GetFullPath(Path.Combine(Application.dataPath, settings.zoundFilesFolderPath.Substring("Assets/".Length)));
            if (!Directory.Exists(absDir)) Directory.CreateDirectory(absDir);

            // Byte-copy the source file — preserves original bit depth and format.
            string absDst = Path.GetFullPath(Path.Combine(Application.dataPath, outputPath.Substring("Assets/".Length)));
            // Clear any existing read-only destination first — File.Copy(overwrite: true) can't
            // itself overwrite a read-only file on Windows.
            SavWav.ClearReadOnlyIfPresent(absDst);
            File.Copy(absSourceFile, absDst, overwrite: true);
            // File.Copy propagates the source's ReadOnly attribute (common for purchased/imported
            // SFX library files that ship read-only) — strip it so future renders can overwrite this file.
            SavWav.ClearReadOnlyIfPresent(absDst);

            AssetDatabase.ImportAsset(outputPath);
            var outputRef = EnsureClipAddressable(outputPath);

            ZoundsWindow.ModifyZoundsProject("ensure output clip", () => {
                klip.outputClipRef = outputRef;
                klip.outputClipPath = outputPath;
            });

            EditorUtility.SetDirty(zoundsProject);
            AssetDatabase.SaveAssets();
#endif
        }

#if ADDRESSABLES_INSTALLED
        /// <summary>
        /// Ensures an AudioClip at the given asset path is registered as Addressable.
        /// Returns the AssetReference, or null on failure.
        /// </summary>
        private static UnityEngine.AddressableAssets.AssetReference EnsureClipAddressable(string assetPath) {
            var audioClip = AssetDatabase.LoadAssetAtPath<AudioClip>(assetPath);
            var audioRef = AudioRenderUtility.GetAudioReference(audioClip);
            if (audioRef != null) return audioRef;

            var addrSettings = UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject.Settings;
            if (addrSettings == null) return null;

            string clipGuid = AssetDatabase.AssetPathToGUID(assetPath);
            if (string.IsNullOrEmpty(clipGuid)) return null;

            string groupName = "Zounds Default Local Group";
            var group = addrSettings.FindGroup(groupName);
            if (group == null) {
                group = addrSettings.CreateGroup(groupName, false, false, false, null,
                    typeof(UnityEditor.AddressableAssets.Settings.GroupSchemas.ContentUpdateGroupSchema),
                    typeof(UnityEditor.AddressableAssets.Settings.GroupSchemas.BundledAssetGroupSchema));
            }
            addrSettings.CreateOrMoveEntry(clipGuid, group);
            return AudioRenderUtility.GetAudioReference(audioClip);
        }
#endif

        public void Render() {
            autoRenderPending = false;
            AudioClip reloadedAudio = RenderKlip(targetZound);
            cachedOutputRef = null;
            cachedOutputAsset = null;
            outputCacheValid = false;
            spectrumView.audioSource.clip = reloadedAudio;
        }

        /// <summary>
        /// The Render button: bounce the Klip to its output file (or, with nothing to apply, promote the source to it),
        /// refresh the waveform cache and the runtime dictionary, and return the audio to preview. Shared with the UI Toolkit
        /// twin, which keeps its own preview source.
        /// </summary>
        internal static AudioClip RenderKlip(Klip targetZound) {
            AudioClip reloadedAudio = RenderToAudioClip(targetZound);
            if (reloadedAudio == null && !targetZound.HasActiveEdits()) {
                // No edits active — promote source to output in ZoundFiles/.
                PromoteOutputClip(targetZound);
                // Load from the appropriate source for preview.
                if (!string.IsNullOrEmpty(targetZound.externalSourcePath)) {
                    reloadedAudio = WavDecoder.LoadFromDisk(targetZound.externalSourcePath);
                }
                else {
                    try { reloadedAudio = targetZound.audioClipRef.editorAsset as AudioClip; } catch { }
                }
                AudioWaveformUtility.ClearCache(targetZound);
            }
            else if (reloadedAudio != null) {
                AudioWaveformUtility.ClearCache(targetZound);
            }

            // CRITICAL: Synchronize the runtime ZoundDictionary so that
            // the outside world (Zequences, Play calls) sees the new render immediately.
            if (Application.isPlaying && ZoundEngine.IsInitialized()) {
                ZoundDictionary.ValidateZoundRuntime(targetZound);
            }
            return reloadedAudio;
        }

        public static AudioClip RenderToAudioClip(Klip klipToRender) {
            return RenderToAudioClip(klipToRender, false);
        }

        public static AudioClip RenderToAudioClip(Klip klipToRender, bool force) {
            if (klipToRender == null) return null;
            if (!klipToRender.needsRender && !force) return null;

            // If all edits are disabled, fall back to the source clip and clean up any orphan rendered file.
            if (!klipToRender.HasActiveEdits()) {
                DeleteRenderedClip(klipToRender);
                return null;
            }

            // Refuse rather than mislead, when the sound has effects this renderer cannot reproduce.
            //
            // This renderer predates the effect chain. It knows how to apply a trim and the two waveform curves, and
            // nothing else — so for a sound with a delay, a reverb or any other chain effect it would cheerfully write
            // a file WITHOUT them and hand it back looking finished. A file that silently differs from what you hear is
            // the worst possible output here, because nothing about it looks wrong.
            //
            // Applying the chain properly is not a matter of bolting a step on the end: the curves are themselves part
            // of the chain now, so running both would apply them twice. Doing it correctly means routing this through
            // the same engine that plays the sound, which is a change worth making deliberately and verifying by ear
            // rather than slipping into a clean-up.
            //
            // Trimming still works, and that is the case worth keeping: a trim reduces what has to ship, and it is a
            // region of the source rather than an effect, so a trimmed file remains a perfectly good SOURCE for the
            // chain to play from.
            var authoredChain = Dsp.ZoundDspPlayback.ResolveChain(klipToRender, out _);
            if (authoredChain != null && authoredChain.nodes != null && authoredChain.nodes.Count > 0) {
                Debug.LogWarning("[Zounds] Not rendering '" + klipToRender.name + "' to a file: it has " +
                                 authoredChain.nodes.Count + " effect(s) in its chain, and this renderer cannot apply " +
                                 "them — the file would be missing them without saying so. The chain is applied as the " +
                                 "sound plays, so no file is needed for it to be heard.");
                return null;
            }

            AudioClip originalClip = null;
            if (!string.IsNullOrEmpty(klipToRender.externalSourcePath)) {
                originalClip = WavDecoder.LoadFromDisk(klipToRender.externalSourcePath);
            }
            else {
                try { originalClip = klipToRender.audioClipRef.editorAsset as AudioClip; } catch { }
            }
            if (originalClip == null) return null;

            originalClip.LoadAudioData();

            int channels = originalClip.channels;
            int sampleRate = originalClip.frequency;
            int sampleCount = originalClip.samples;
            float clipLength = originalClip.length;

            // Extract samples once — the entire pipeline works on this float[] array.
            float[] samples = new float[sampleCount * channels];
            originalClip.GetData(samples, 0);

            // === PHASE 1: Trim + Envelopes (mode-dependent ordering) ===

            // The volume and pitch curves drawn on the waveform now live on the sound's effect chain, because that is
            // what real-time playback reads. This render has to read the same place or it would quietly bake a
            // different sound than the one you hear — which is worse than not rendering at all, since the output looks
            // authoritative.
            //
            // A sound that has never been opened in the chain editor still keeps its curves in the older per-sound
            // fields, so those remain the fallback. The distinction that matters is "has no chain curve at all" versus
            // "has one that is switched off": the second must win, or turning a curve off here would silently revive
            // whatever the old field happened to hold.
            var chainVolumeCurve = KlipChainEnvelopes.VolumeCurve(klipToRender, false);
            var chainPitchCurve = KlipChainEnvelopes.PitchCurve(klipToRender, false);
            var volumeCurve = (chainVolumeCurve != null && !ReferenceEquals(chainVolumeCurve, KlipChainEnvelopes.Disabled))
                ? chainVolumeCurve : klipToRender.volumeEnvelope;
            var pitchCurve = (chainPitchCurve != null && !ReferenceEquals(chainPitchCurve, KlipChainEnvelopes.Disabled))
                ? chainPitchCurve : klipToRender.pitchEnvelope;

            if (klipToRender.clampToTrim && klipToRender.trimEnabled) {
                // Clamped mode: trim first, then apply envelopes to the trimmed segment
                samples = AudioRenderUtility.Trim(samples, channels, sampleRate,
                    klipToRender.trimStart, klipToRender.trimEnd, out sampleCount);
                float segmentLength = (float)sampleCount / sampleRate;

                if (volumeCurve.enabled) {
                    AudioRenderUtility.VolumeEnvelope(samples, channels, sampleRate, sampleCount, volumeCurve);
                }
                if (pitchCurve.enabled) {
                    samples = AudioRenderUtility.PitchEnvelope(samples, channels, sampleRate, sampleCount,
                        pitchCurve, segmentLength, out sampleCount, 0, segmentLength);
                }
            } else {
                // Global mode: apply envelopes to full clip, then trim
                if (volumeCurve.enabled) {
                    AudioRenderUtility.VolumeEnvelope(samples, channels, sampleRate, sampleCount, volumeCurve);
                }
                if (pitchCurve.enabled) {
                    samples = AudioRenderUtility.PitchEnvelope(samples, channels, sampleRate, sampleCount,
                        pitchCurve, clipLength, out sampleCount, 0, clipLength);
                }
                if (klipToRender.trimEnabled) {
                    float finalTrimStart = klipToRender.trimStart;
                    float finalTrimEnd = klipToRender.trimEnd;

                    if (pitchCurve.enabled) {
                        finalTrimStart = AudioRenderUtility.GetOutputTimeForSourceTime(klipToRender.trimStart, pitchCurve, clipLength);
                        finalTrimEnd = AudioRenderUtility.GetOutputTimeForSourceTime(klipToRender.trimEnd, pitchCurve, clipLength);
                    }

                    samples = AudioRenderUtility.Trim(samples, channels, sampleRate, finalTrimStart, finalTrimEnd, out sampleCount);
                }
            }

            // === PHASE 2: Effect Chain (EQ → Gain → [future: Compression → Normalization → Fade]) ===
            samples = KlipEffectChain.ProcessChain(samples, channels, sampleRate, klipToRender);

            // === Create final AudioClip ===
            // After Phase 1, sampleCount tracks the per-channel frame count.
            // The samples array length must match exactly.
            // Ensure sampleCount matches the array after all processing
            sampleCount = samples.Length / channels;
            AudioClip renderedClip = AudioClip.Create(originalClip.name + "_Rendered", sampleCount, channels, sampleRate, false);
            renderedClip.SetData(samples, 0);

            // Use the single stable output path — renders and copies always go to the same file.
            string filePath = GetStableOutputPath(klipToRender);

            AudioClip reloadedAudio;
            try {
                reloadedAudio = AudioRenderUtility.SaveAudio(renderedClip, filePath);
            }
            catch (System.Exception e) {
                // Writing the output file can fail for reasons outside our control (locked by another
                // process, disk full, permissions). Fail loudly but gracefully — an exception thrown
                // here mid-OnGUI corrupts the IMGUI layout stack (mismatched Begin/End) for the rest
                // of the frame, breaking the window until it's reopened. needsRender stays true so
                // the next Render attempt retries once the underlying cause is fixed.
                string hint = e is System.UnauthorizedAccessException
                    ? " This is normally the read-only attribute, which Zounds already clears automatically before writing — "
                      + "if you're seeing this, the file is likely open/locked in another program (or an antivirus/sync tool "
                      + "has it locked), or a folder-level Windows permission is blocking write access. Close anything else "
                      + "that might have the file open and try Render again."
                    : "";
                Debug.LogError($"[Zounds] Failed to write rendered output for '{klipToRender.name}' to {filePath}:{hint}\n{e}");
                return null;
            }

            // Clear the texture cache so the Zequence editor generates a fresh waveform
            AudioWaveformUtility.ClearCache(klipToRender);
            AudioWaveformUtility.ClearCache(reloadedAudio);

            // Ensure the rendered clip is Addressable (post-processor chicken-and-egg:
            // renderedClipRef isn't set until after this method, so IsOutputClip returns false during import).
#if ADDRESSABLES_INSTALLED
            var audioRef = EnsureClipAddressable(filePath);
#endif

            ZoundsWindow.ModifyZoundsProject("render klip", () => {
                klipToRender.needsRender = false;
#if ADDRESSABLES_INSTALLED
                klipToRender.renderedClipRef = audioRef;
                // Render writes to the stable output path — set output refs directly.
                klipToRender.outputClipRef = audioRef;
#endif
                klipToRender.renderedClipPath = filePath;
                klipToRender.outputClipPath = filePath;
            });

            // Always force-save so renderedClipRef/outputClipRef survive play mode exit
            EditorUtility.SetDirty(ZoundsProject.Instance);
            AssetDatabase.SaveAssets();

            return reloadedAudio;
        }

        /// <summary>
        /// Clears the rendered clip state when edits are disabled.
        /// Does NOT delete the output file — PromoteOutputClip overwrites it with a source copy.
        /// Single-file model: the output path stays the same, only the contents change.
        /// </summary>
        public static void DeleteRenderedClip(Klip klip) {
            if (klip == null) return;

            // Clear rendered refs but keep the output path stable.
            // PromoteOutputClip will overwrite the file with a source copy.
            ZoundsWindow.ModifyZoundsProject("clear rendered clip", () => {
                klip.needsRender = false;
                klip.renderedClipRef = null;
                klip.renderedClipPath = string.Empty;
                // Clear outputClipRef so PromoteOutputClip sees it needs to re-copy.
                klip.outputClipRef = null;
                // Keep outputClipPath — PromoteOutputClip reuses it via GetStableOutputPath.
            });

            AudioWaveformUtility.ClearCache(klip);

            // Re-copy source to the same output path.
            PromoteOutputClip(klip);

            EditorUtility.SetDirty(ZoundsProject.Instance);
        }

    }

}
