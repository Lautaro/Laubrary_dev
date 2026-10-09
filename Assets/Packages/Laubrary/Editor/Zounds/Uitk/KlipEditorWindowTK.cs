using System.Collections.Generic;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zounds.Uitk {

    /// <summary>
    /// The UI Toolkit twin of the Klip editor window (T-0456 port, T-0461 shell). It edits the very same Klip through
    /// the very same project paths as the IMGUI window, so the two can be open side by side on one sound and every edit
    /// shows in both. The layout reproduces the old window's measure for measure: the sheet's 10 px row spacing, the
    /// header row at single-line height, the content box, the Source field, the waveform, the action row, the
    /// time-stretch strip and the effect chain.
    ///
    /// Opened from the old window's temporary "UITK" button (owner's decision D3), which exists only while both exist.
    /// </summary>
    public partial class KlipEditorWindowTK : ZuiWindow, IHasCustomMenu {

        [SerializeField] int targetZoundID;
        [SerializeField] bool isLocalZound;

        // The window's vertical rhythm (tightened 2026-10-08, owner's review: loose empty bands): a small gap between
        // bands, a slightly larger one before the chain, nothing else.
        const float Gap4 = 4f, Band = 6f;

        Klip klip;
        ZoundFieldsRowTK fields;
        VisualElement box;
        IVisualElementScheduledItem syncTick;
        Button playButton;
        ZuiToggleButton retriggerButton;

        // Every play this window starts (Play, Play on change, Burst, Loop) goes through its audition session and dies
        // with the window (T-0486). The session itself is never serialized, so a reload or a restored layout always
        // comes back silent; only the Play-on-change switch is kept while the window stays open.
        [System.NonSerialized] ZoundAudition audition;
        [SerializeField] bool auditionPlayOnChange;

        void EnsureAudition() {
            if (audition != null && !audition.IsDisposed) return;
            audition = new ZoundAudition(targetZoundID, () => this != null, PlayKlipOnce,
                                         () => klip != null && ZoundAudition.ContainsLooper(klip)) { playOnChange = auditionPlayOnChange };
            ZoundPreviewPlayback.Register(this, audition);
            audition.changed += () => { auditionPlayOnChange = audition != null && audition.playOnChange; SyncPlayButton(); };
            audition.Attach(rootVisualElement);
        }

        void KillAudition() {
            if (audition == null) return;
            auditionPlayOnChange = audition.playOnChange;
            audition.Dispose();
            audition = null;
        }

        // The waveform's model: the old window's own view object, wired by its own WireSpectrumView (T-0468). Kept across
        // rebuilds; created on first build, destroyed with the window.
        // Not kept across a script reload: Unity's reload saves an editor window's private fields too, and brings this
        // one back half-built (its constructor is not run, so its envelope state is missing and the waveform threw on
        // every refresh — found 2026-09-28). It is disposed before the reload and made fresh afterwards instead.
        [System.NonSerialized] AudioSpectrumView spectrum;
        KlipWaveformTK waveform;
        bool draggingWaveform;
        [System.NonSerialized] bool shownTrimOnce;
        // The audition card pinned into the window (owner, 2026-10-08) instead of behind Play's right-click; per machine.
        const string PinKey = "Laubrary.Zounds.AuditionPinned.Klip";
        static bool AuditionPinned { get => EditorPrefs.GetBool(PinKey, false); set => EditorPrefs.SetBool(PinKey, value); }
        VisualElement pinnedSlot;

        // ── destructive editing (2026-10-09) ──
        // After an edit went to a copy of a shared sound, the window follows the copy; an undo of that brings the copy's id
        // back off the books, and the window follows the original again (a redo, forward again).
        [SerializeField] int swappedFromId, swappedToId;
        Label badge;
        TextField sourceName;

        // The edit bar's audio edits, in KlipEditorWindowTK.AudioEdits.cs.
        partial void WireAudioEdits();
        partial void UnwireAudioEdits();
        partial void Report(string message);

        /// <summary>Before the waveform block edits the sound's trim or curves: a shared sound goes to a copy (per Settings).</summary>
        bool GuardSoundEdit() {
            if (klip == null) return false;
            bool willSwap = Destructive.ZoundsProtection.IsShared(klip) && !Destructive.ZoundsEditGuard.EditsOriginal(klip);
            if (willSwap) audition?.StopAll();
            if (!Destructive.ZoundsEditGuard.BeforeSoundEdit(klip, out var swap)) return false;
            if (swap.swapped) {
                FollowCopy(swap);
                if (Destructive.ZoundsEditGuard.Mode == ZoundsProject.ProjectSettings.ProtectedEditPrompt.Notice)
                    waveform?.notice.Show("'" + swap.originalName + "' is " + swap.usedIn + ", so this change goes to a copy, '" + swap.copyName + "'. Everything else keeps the original.",
                                          () => EditOriginal(swap));
            }
            return true;
        }

        void FollowCopy(Destructive.ZoundsEditGuard.Swap swap) {
            swappedFromId = swap.originalId; swappedToId = swap.copyId;
            targetZoundID = swap.copyId;
            // The audition session is keyed to the sound's id: start a fresh one for the copy.
            KillAudition(); EnsureAudition(); SyncPlayButton();
            titleContent = new GUIContent(TitleFor(klip));
            fields?.Sync();
            SyncBadge();
        }

        void EditOriginal(Destructive.ZoundsEditGuard.Swap swap) {
            audition?.StopAll();
            if (!Destructive.ZoundsEditGuard.SwapBack(klip, swap.originalId)) { Report("The original is gone, so there is nothing to go back to."); return; }
            targetZoundID = swap.originalId;
            swappedFromId = swappedToId = 0;
            KillAudition(); EnsureAudition(); SyncPlayButton();
            titleContent = new GUIContent(TitleFor(klip));
            fields?.Sync();
            SyncBadge();
            Report("Now editing the original, '" + klip.name + "'.");
        }

        void SyncBadge() {
            if (badge == null || klip == null) return;
            // The source's name too: an audio edit of a protected file points the sound at the new one.
            var src = spectrum?.sourceClip;
            if (sourceName != null && src != null && sourceName.value != src.name) { sourceName.SetValueWithoutNotify(src.name); sourceName.tooltip = AssetDatabase.GetAssetPath(src); }
            Destructive.ZoundsProtection.Badge(klip, out string text, out string tip, out bool locked);
            if (badge.text != text) badge.text = text;
            badge.tooltip = tip;
            badge.EnableInClassList("zs-klip-editor__badge--locked", locked);
        }

        Label MakeBadge() {
            badge = new Label();
            badge.AddToClassList("zs-lbl"); badge.AddToClassList("zs-greymini"); badge.AddToClassList("zs-klip-editor__badge");
            SyncBadge();
            return badge;
        }

        void EnsureSpectrum() {
            if (spectrum != null) return;
            spectrum = new AudioSpectrumView(this) { height = 150f };
            KlipEditorWindow.WireSpectrumView(spectrum, () => klip, d => draggingWaveform = d, RefreshSpectrum, () => { }, () => waveform?.Refresh());
            RefreshSpectrum();
        }

        /// <summary>The old window's RefreshSpectrumView: validate, then re-read the Klip into the view.</summary>
        void RefreshSpectrum() {
            if (spectrum == null || klip == null) return;
            KlipEditorWindow.ValidateKlip(klip);
            spectrum.InitFromKlip(klip, useChainEnvelopes: true);
            waveform?.Refresh();
        }

        /// <summary>The old window's mouse-up: close the drag's Undo step (validating the Klip first when it changed).</summary>
        void EndWaveformDrag() {
            if (!draggingWaveform) return;
            draggingWaveform = false;
            if (klip.needsRender && spectrum?.sourceClip != null) ZoundsWindow.EndDragUndo(() => KlipEditorWindow.ValidateKlip(klip));
            else ZoundsWindow.EndDragUndo();
        }

        protected override void OnDisable() {
            UnwireAudioEdits();
            ZoundPreviewPlayback.Dispose(this);
            // Closing, and the disable Unity sends before every script reload: nothing this window started may outlive it.
            KillAudition();
            EndWaveformDrag();
            // Before a script reload (and on close): release the model's hidden preview object, which would otherwise
            // outlive the reload with nothing pointing at it.
            spectrum?.Destroy();
            spectrum = null;
            base.OnDisable();
        }

        void OnDestroy() {
            KillAudition();
            spectrum?.Destroy();
            spectrum = null;
        }

        void OnFocus() {
            if (spectrum != null && klip != null && spectrum.NeedsSourceRefresh(klip)) RefreshSpectrum();
        }

        /// <summary>
        /// Opens this Klip's editor — the main Klip editor since 2026-09-28 (owner: "Make UITK version the main one").
        /// A Klip that already has one open gets that window brought forward instead of a second copy, as the old
        /// window always did.
        /// </summary>
        public static KlipEditorWindowTK Open(Klip klip, bool isLocalZound) {
            foreach (var open in Resources.FindObjectsOfTypeAll<KlipEditorWindowTK>()) {
                if (open == null || open.targetZoundID != klip.id) continue;
                if (isLocalZound) open.isLocalZound = true;
                if (open.docked) open.ShowTab(); else open.Focus();
                return open;
            }
            var w = CreateInstance<KlipEditorWindowTK>();
            w.targetZoundID = klip.id;
            w.isLocalZound = isLocalZound;
            w.titleContent = new GUIContent(TitleFor(klip));
            w.minSize = new Vector2(479.2f, 400f);
            w.Show();
            return w;
        }

        /// <summary>The tab's ⋮ menu: the old IMGUI editor for the same Klip, kept for side-by-side comparison.</summary>
        public void AddItemsToMenu(GenericMenu menu) {
            var k = klip ?? (ZoundsProject.isJSONLoaded ? FindKlip(targetZoundID) : null);
            if (k == null) return;
            bool local = isLocalZound;
            menu.AddItem(new GUIContent("Open IMGUI version"), false, () => { var w = KlipEditorWindow.OpenWindow(k); if (local) w.isLocalZound = true; });
        }

        static string TitleFor(Klip k) {
            string t = "Klip: " + k.name;
            if (k.parentId != 0 && ZoundDictionary.TryGetZoundById(k.parentId, out var parent)) t += " (" + parent.name + ")";
            return t;
        }

        /// <summary>The same search the old window does: top-level Klips, then each Zequence's local Klips.</summary>
        public static Klip FindKlip(int id) {
            var lib = ZoundsProject.Instance.zoundLibrary;
            var k = lib.klips.Find(x => x.id == id);
            if (k != null) return k;
            foreach (var z in lib.zequences) {
                k = z.localKlips.Find(x => x.id == id);
                if (k != null) return k;
                foreach (var lz in z.localZequences) {
                    k = lz.zequence.localKlips.Find(x => x.id == id);
                    if (k != null) return k;
                }
            }
            return null;
        }

        static VisualElement VSpace(float h) {
            var e = new VisualElement();
            e.style.height = h; e.AddToClassList("zs-klip-editor__vertical-space");
            return e;
        }

        static VisualElement HRow(float height) {
            var r = new VisualElement();
            r.AddToClassList("zs-klip-editor__row");
            r.AddToClassList("zs-klip-editor__row");
            if (height > 0f) r.style.height = height;
            return r;
        }

        protected override void BuildUI(VisualElement root) {
            // Straight after a script reload this window rebuilds before Unity's editor styles exist, and the shared
            // measurements below read them (see ZS.EditorStylesReady); build a moment later instead of half-building.
            if (!ZS.EditorStylesReady) { root.schedule.Execute(Rebuild).StartingIn(100); return; }
            ZS.Attach(root);
            klip = ZoundsProject.isJSONLoaded ? FindKlip(targetZoundID) : null;
            if (klip == null) {
                root.Add(new Label(ZoundsProject.isJSONLoaded ? "Klip no longer exists in the project." : "Zounds Project is not loaded."));
                // An undo may bring back the sound this window followed a copy from (destructive editing): keep watching.
                if (swappedFromId != 0) syncTick = root.schedule.Execute(Sync).Every(200);
                return;
            }
            titleContent = new GUIContent(TitleFor(klip));
            EnsureAudition();

            // ── header row (ZoundInspector.DrawSimple) ──
            root.Add(VSpace(Gap4));
            fields = new ZoundFieldsRowTK(klip, isLocalZound, () => titleContent = new GUIContent(TitleFor(klip)));
            root.Add(fields);
            // A Klip with no reference at all is a legitimate placeholder: say so, and let the Source field below take one.
            bool hasInternalSource = klip.audioClipRef != null && klip.audioClipRef.RuntimeKeyIsValid();
            bool hasExternalSource = !string.IsNullOrEmpty(klip.externalSourcePath);
            bool hasValidClip = hasInternalSource || hasExternalSource;
            if (!hasValidClip) root.Add(new HelpBox("No audio assigned yet. Assign one in the 'Clip References' tab or the Source field below.", HelpBoxMessageType.Info));
            root.Add(VSpace(Gap4));

            // ── content box (ZUI.Box, "Default"); its background is the Settings tab's when one is set ──
            box = new VisualElement();
            box.AddToClassList("zs-box-default");
            box.AddToClassList("zs-klip-editor__box");
            SettingsTabTK.ApplyEditorBackground(box);
            root.Add(box);
            box.Add(VSpace(Gap4));

            EnsureSpectrum();
            RefreshSpectrum();
            // On entering the editor only the trimmed part is shown (owner, 2026-10-08); the wheel zooms out to the rest.
            if (!shownTrimOnce) { spectrum.ShowTrim(); shownTrimOnce = true; }
            var sourceAsset = spectrum.sourceClip;
            var outputAsset = ResolveOutputAsset();
            bool sourceAvailable = sourceAsset != null;
            if (!sourceAvailable && outputAsset == null && hasValidClip) {
                // A reference was assigned but no longer resolves: genuinely broken, so nothing below can be shown.
                box.Add(new HelpBox(hasExternalSource ? "External source file not found:\n" + klip.externalSourcePath
                                                      : "Source Audio Clip is missing or invalid. Please fix it in the 'Clip References' tab.", HelpBoxMessageType.Error));
                box.Add(ZS.Button("Close Window", "Close this sound editor.", "Default", Close, ZUICornerMask.All, -1f, 20f));
                syncTick = root.schedule.Execute(Sync).Every(200);
                return;
            }
            if (!sourceAvailable && hasValidClip)
                box.Add(new HelpBox("Source clip is not available on this machine. Waveform edits are disabled.\nSettings (volume, pitch, chance, routing, tags) remain editable.", HelpBoxMessageType.Info));

            if (hasExternalSource) box.Add(BuildExternalSourceRow());
            else box.Add(BuildSourceRow(sourceAsset));
            // With no source on this machine, the rendered output is the only audio left to show and preview.
            if (!sourceAvailable && outputAsset != null) spectrum.audioSource.clip = outputAsset;

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("zs-klip-editor__scroll");
            box.Add(scroll);
            scroll.Add(VSpace(Gap4));

            // Waveform block: the curve bar, the edit bar, the waveform with its trim and envelopes (T-0468), its height grip.
            waveform = new KlipWaveformTK(spectrum, klip) { onReleased = EndWaveformDrag, beforeEdit = GuardSoundEdit };
            WireAudioEdits();
            scroll.Add(waveform);

            // ── the action row (Retrigger, Delete, Play), then the audition card when it is pinned ──
            scroll.Add(VSpace(Gap4));
            scroll.Add(BuildActionRow(sourceAvailable));
            pinnedSlot = new VisualElement();
            pinnedSlot.AddToClassList("zs-klip-editor__pinned");
            scroll.Add(pinnedSlot);
            SyncPinned();

            // ── the Looper and speed rows, then the chain editor (T-0462 onward) ──
            scroll.Add(VSpace(Band));
            scroll.Add(new LooperTK(klip));   // T-0476
            scroll.Add(VSpace(2f));
            scroll.Add(new TimeStretchTK(klip));
            scroll.Add(VSpace(Band));
            scroll.Add(new ChainEditorTK(klip, this));

            syncTick = root.schedule.Execute(Sync).Every(200);
        }

        VisualElement BuildActionRow(bool sourceAvailable) {
            const float h = 20f;
            var r = HRow(h);
            if (klip.parentId == 0 && ZoundsProject.Instance.browserSettings.showConvertToZequence) {
                var convert = ZS.Button("Convert to Zeq", "Make a sequence containing this sound as a local entry.", "RichButton", ConvertToZeq, ZUICornerMask.All, 100f, h);
                convert.SetEnabled(sourceAvailable);
                r.Add(convert);
            }
            // Retrigger, Delete, Play at the right, in the Zequence toolbar's order (Delete used to sit alone at the left, 2026-10-08).
            r.Add(Flex());
            retriggerButton = ZS.Toggle("Retrigger", "When enabled, every trigger starts this Klip several times. Right-click to set plays, gap and timing.", klip.retriggerEnabled,
                value => { ZoundsWindow.ModifyZoundsProject("toggle klip retrigger", () => { if (value) klip.EnableRetrigger(); else klip.retriggerEnabled = false; }); SyncRetriggerButton(); },
                "RichToggle", ZUICornerMask.All, 88f, h);
            retriggerButton.RegisterCallback<PointerDownEvent>(e => {
                if (e.button != 1) return;
                e.StopPropagation();
                RetriggerPopupTK.Show(retriggerButton, klip, Sync);
            });
            r.Add(retriggerButton);
            r.Add(Gap(6f));
            // Delete (the old "Remove": it deletes the sound from the project after a confirmation, so it says so).
            r.Add(ZequenceEditorWindowTK.IconButton("remove", "Delete this sound from the project (asks first). Cannot be undone.", "RichButton", ZUICornerMask.All, 30f, h, Remove));
            r.Add(Gap(6f));
            playButton = ZS.Button("Play", "", "RichButton", PlayOrStop, ZUICornerMask.All, 60f, h);
            WireAuditionMenu(playButton);
            r.Add(playButton);
            SyncPlayButton();
            return r;
        }

        /// <summary>Right-click on Play opens the audition card (T-0486), unless it is pinned into the window.</summary>
        void WireAuditionMenu(Button b) {
            b.RegisterCallback<PointerDownEvent>(e => {
                if (e.button != 1) return;
                e.StopPropagation();
                EnsureAudition();
                if (AuditionPinned) return;
                AuditionPopupTK.Show(b, audition, v => { AuditionPinned = v; SyncPinned(); });
            });
        }

        /// <summary>The pinned audition card under the action row, or nothing: the slot keeps no height while empty.</summary>
        void SyncPinned() {
            if (pinnedSlot == null) return;
            pinnedSlot.Clear();
            if (!AuditionPinned) return;
            EnsureAudition();
            var card = new AuditionCardTK(audition, true, v => { AuditionPinned = v; SyncPinned(); SyncPlayButton(); });
            card.AddToClassList("zs-audition-card--pinned");
            pinnedSlot.Add(card);
            SyncPlayButton();
        }

        void SyncPlayButton() {
            if (playButton == null) return;
            bool live = audition != null && audition.PlayControlStops;
            bool armed = audition != null ? audition.playOnChange : auditionPlayOnChange;
            playButton.text = (live ? "Stop" : "Play") + (armed ? " •" : "");
            playButton.style.backgroundColor = live ? new Color(.22f, .34f, .52f, 1f) : StyleKeyword.Null;
            playButton.tooltip = (live ? (audition.IsLoopPlaying(audition) ? "Stop loop" : "Stop the queued audition run.")
                                       : "Play this Klip.")
                               + (armed ? "\n\n• Play on change is on: every change you make here plays the sound again." : "")
                               + (AuditionPinned ? "\n\nPlay on change, Burst and Loop are on the pinned card below." : "\n\nRight-click: Play on change, Burst, Loop.");
        }

        void SyncRetriggerButton() {
            if (retriggerButton == null || klip == null) return;
            retriggerButton.SetValueWithoutNotify(klip.retriggerEnabled);
            retriggerButton.text = klip.retriggerEnabled ? "Retrigger " + RetriggerPopupTK.Summary(klip) : "Retrigger";
            retriggerButton.tooltip = klip.retriggerEnabled
                ? "Every trigger starts this Klip " + klip.retriggerCount + " times. Right-click to change plays, gap and timing."
                : "Enable several plays for every trigger. Right-click to set plays, gap and timing.";
        }

        static VisualElement Gap(float w) { var e = new VisualElement(); e.style.width = w; e.AddToClassList("zs-klip-editor__gap"); return e; }
        static VisualElement Flex() { var e = new VisualElement(); e.AddToClassList("zs-klip-editor__spacer"); return e; }

        /// <summary>Anything this window started still sounding or queued (the old single-token check, widened to the
        /// audition helpers).</summary>
        bool IsPlaying() => audition != null && audition.AnyLive;

        /// <summary>Play when silent, otherwise stop everything this window started (a Burst or Loop included).</summary>
        void PlayOrStop() {
            EnsureAudition();
            if (audition.BurstRunning || audition.LoopRunning) audition.StopRun();
            else audition.PlayOnce();
            Sync();
        }

        /// <summary>One play, the old window's Play (SimulatePlay) step for step: every play draws its own volume and pitch.</summary>
        ZoundToken PlayKlipOnce() {
            if (klip == null) return null;
            if (!Application.isPlaying && klip.needsRender) KlipEditorWindow.RenderToAudioClip(klip);
            bool needsRenderTemp = klip.needsRender;
            klip.needsRender = false;
            try {
                return ZoundPreviewPlayback.Play(this, klip, new ZoundArgs() {
                    startImmediately = true, delay = 0f,
                    volumeOverride = Random.Range(klip.minVolume, klip.maxVolume),
                    pitchOverride = Random.Range(klip.minPitch, klip.maxPitch),
                    chanceOverride = 1f, useFixedAverageValues = false, bypassGlobalSolo = isLocalZound, ignoreCooldown = true
                }, audition, false);
            }
            finally { klip.needsRender = needsRenderTemp; }
        }

        AudioClip ResolveOutputAsset() {
            var outputRef = klip.outputClipRef ?? klip.renderedClipRef;
            try { return outputRef == null ? null : outputRef.editorAsset as AudioClip; } catch { return null; }
        }

        /// <summary>
        /// The source row (2026-10-08, replacing Unity's object field): "Source:", the clip's name, Change… (the Zounds
        /// picker, single pick), Show file (the Project window). A clip dragged from the Project window onto the row
        /// still replaces the source, as the object field allowed.
        /// </summary>
        VisualElement BuildSourceRow(AudioClip sourceAsset) {
            var r = HRow(EditorGUIUtility.singleLineHeight + 2f);
            r.AddToClassList("zs-klip-editor__source-row");
            var label = new Label("Source:") { tooltip = "The clip this Klip plays from." };
            label.AddToClassList("zs-lbl");
            label.style.width = EditorGUIUtility.labelWidth; label.AddToClassList("zs-klip-editor__external-source-row-label");
            var name = new TextField { value = sourceAsset != null ? sourceAsset.name : "(none)", isReadOnly = true, tooltip = sourceAsset != null ? AssetDatabase.GetAssetPath(sourceAsset) : "No clip assigned yet." };
            name.AddToClassList("zs-klip-editor__external-source-row-name");
            sourceName = name;
            var change = ZS.Button("Change…", "Pick another clip of the workspace for this Klip to play from.", "RichButton",
                () => ZoundPickerWindowTK.Open(ZoundPickerRequests.KlipSource(klip, ReplaceSource, this)), ZUICornerMask.Left, 70f, EditorGUIUtility.singleLineHeight);
            change.AddToClassList("zs-klip-editor__external-source-row-browse");
            var show = ZS.Button("Show file", "Highlights the clip in the Project window.", "RichButton", () => { if (sourceAsset != null) EditorGUIUtility.PingObject(sourceAsset); }, ZUICornerMask.Right, 70f, EditorGUIUtility.singleLineHeight);
            show.SetEnabled(sourceAsset != null);
            show.AddToClassList("zs-klip-editor__external-source-row-reveal");
            r.Add(label); r.Add(name); r.Add(change); r.Add(show);
            // Whether audio edits rewrite this file or write a new one, and who else uses the sound (variable width: last).
            r.Add(MakeBadge());
            // A clip dropped from the Project window (or dragged out of the picker) replaces the source.
            r.RegisterCallback<DragUpdatedEvent>(e => { if (DroppedClip() != null) { DragAndDrop.visualMode = DragAndDropVisualMode.Link; e.StopPropagation(); } });
            r.RegisterCallback<DragPerformEvent>(e => { var c = DroppedClip(); if (c == null) return; DragAndDrop.AcceptDrag(); ReplaceSource(c); e.StopPropagation(); });
            return r;
        }

        static AudioClip DroppedClip() {
            if (DragAndDrop.GetGenericData(ZoundPickerWindowTK.DragKey) is List<ZoundPickerItem> items && items.Count > 0 && items[0].clip != null) return items[0].clip;
            foreach (var o in DragAndDrop.objectReferences) if (o is AudioClip c) return c;
            return null;
        }

        /// <summary>The old window's external-source row: "Source:" and the file's name (selectable, read-only), Browse, Reveal.</summary>
        VisualElement BuildExternalSourceRow() {
            var r = HRow(EditorGUIUtility.singleLineHeight + 2f);
            var label = new Label("Source:");
            label.AddToClassList("zs-lbl");
            label.style.width = EditorGUIUtility.labelWidth; label.AddToClassList("zs-klip-editor__external-source-row-label");
            var name = new TextField { value = System.IO.Path.GetFileName(klip.externalSourcePath), isReadOnly = true };
            name.AddToClassList("zs-klip-editor__external-source-row-name");
            var browse = new Button(() => {
                string dir = System.IO.Path.GetDirectoryName(klip.externalSourcePath);
                EditorApplication.delayCall += () => {
                    string selected = EditorUtility.OpenFilePanel("Select Source Audio File", dir, "wav");
                    if (string.IsNullOrEmpty(selected)) return;
                    ZoundsWindow.ModifyZoundsProject("replace external source", () => {
                        klip.externalSourcePath = selected;
                        klip.needsRender = true;
                        RefreshSpectrum();
                    });
                    Rebuild();
                };
            }) { text = "Browse" };
            browse.AddToClassList("zs-klip-editor__external-source-row-browse");
            var reveal = new Button(() => EditorUtility.RevealInFinder(klip.externalSourcePath)) { text = "Reveal" };
            reveal.AddToClassList("zs-klip-editor__external-source-row-reveal");
            r.Add(label); r.Add(name); r.Add(browse); r.Add(reveal);
            r.Add(MakeBadge());
            return r;
        }

        /// <summary>The old window's Source field change: stop this sound, point the Klip at the new clip, re-read the view.</summary>
        void ReplaceSource(AudioClip newSource) {
            if (newSource == null || newSource == spectrum?.sourceClip) return;
#if ADDRESSABLES_INSTALLED
            audition?.StopAll();
            ZoundsWindow.ModifyZoundsProject("replace source clip", () => {
                var assetPath = AssetDatabase.GetAssetPath(newSource);
                klip.audioClipRef = new UnityEngine.AddressableAssets.AssetReference(AssetDatabase.AssetPathToGUID(assetPath));
                klip.audioClipPath = assetPath;
                // As the old window: only a sound with a rendered output has something to re-render.
                if ((klip.outputClipRef ?? klip.renderedClipRef) != null) klip.needsRender = true;
                RefreshSpectrum();
            });
#endif
        }

        void Remove() {
            if (!AudioAssetUtility.DisplayZoundRemoveDialog(klip)) return;
            ZoundsWindow.ModifyZoundsProject("remove zound", () => AudioAssetUtility.RemoveZound(klip), true);
            Close();
        }

        void ConvertToZeq() {
            if (!EditorUtility.DisplayDialog("Convert to Zequence: " + klip.name,
                    "Convert this Klip into a Zequence containing it as a local klip?\n" + klip.name, "Convert", "Cancel")) return;
            BrowserTab.Instance?.ConvertKlipToZequence(klip);
            Close();
        }

        void Sync() {
            if (klip == null) {
                // The sound this window showed is gone: an undo of an edit that went to a copy brings the original back here.
                if (swappedFromId != 0 && ZoundsProject.isJSONLoaded && FindKlip(targetZoundID) == null && FindKlip(swappedFromId) != null) { targetZoundID = swappedFromId; Rebuild(); }
                return;
            }
            if (FindKlip(targetZoundID) != klip) {
                if (FindKlip(targetZoundID) == null && swappedFromId != 0 && targetZoundID == swappedToId && FindKlip(swappedFromId) != null) targetZoundID = swappedFromId;
                Rebuild(); return;
            }
            // A redo of that edit: the copy is back, so follow it again.
            if (swappedToId != 0 && targetZoundID == swappedFromId && FindKlip(swappedToId) != null) { targetZoundID = swappedToId; Rebuild(); return; }
            if (spectrum != null && spectrum.NeedsSourceRefresh(klip)) RefreshSpectrum();
            SyncBadge();
            fields?.Sync();
            SyncPlayButton();
            SyncRetriggerButton();
            SettingsTabTK.ApplyEditorBackground(box);   // the Settings tab's colour, live (and after an undo)
        }
    }
}
