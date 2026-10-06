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
    public class KlipEditorWindowTK : ZuiWindow, IHasCustomMenu {

        [SerializeField] int targetZoundID;
        [SerializeField] bool isLocalZound;

        const float Row = 10f;   // the Zounds sheet's verticalSpacing (ZUI.RowSpace)

        Klip klip;
        ZoundFieldsRowTK fields;
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
            if (klip == null) { root.Add(new Label(ZoundsProject.isJSONLoaded ? "Klip no longer exists in the project." : "Zounds Project is not loaded.")); return; }
            titleContent = new GUIContent(TitleFor(klip));
            EnsureAudition();

            // ── header row (ZoundInspector.DrawSimple) ──
            root.Add(VSpace(Row));
            fields = new ZoundFieldsRowTK(klip, isLocalZound, () => titleContent = new GUIContent(TitleFor(klip)));
            root.Add(fields);
            // A Klip with no reference at all is a legitimate placeholder: say so, and let the Source field below take one.
            bool hasInternalSource = klip.audioClipRef != null && klip.audioClipRef.RuntimeKeyIsValid();
            bool hasExternalSource = !string.IsNullOrEmpty(klip.externalSourcePath);
            bool hasValidClip = hasInternalSource || hasExternalSource;
            if (!hasValidClip) root.Add(new HelpBox("No audio assigned yet. Assign one in the 'Clip References' tab or the Source field below.", HelpBoxMessageType.Info));
            root.Add(VSpace(Row));

            // ── content box (ZUI.Box, "Default") ──
            var box = new VisualElement();
            box.AddToClassList("zs-box-default");
            box.AddToClassList("zs-klip-editor__box");
            root.Add(box);
            box.Add(VSpace(Row));

            EnsureSpectrum();
            RefreshSpectrum();
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
            else {
                // Source (EditorGUILayout.ObjectField "Source:")
                var source = new UnityEditor.UIElements.ObjectField("Source:") { objectType = typeof(AudioClip), allowSceneObjects = false };
                source.AddToClassList("zs-sourcefield");
                source.SetValueWithoutNotify(sourceAsset);
                source.RegisterValueChangedCallback(e => ReplaceSource(e.newValue as AudioClip));
                box.Add(source);
            }
            // With no source on this machine, the rendered output is the only audio left to show and preview.
            if (!sourceAvailable && outputAsset != null) spectrum.audioSource.clip = outputAsset;

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("zs-klip-editor__scroll");
            box.Add(scroll);
            scroll.Add(VSpace(Row));

            // Waveform block: toolbar, half a row, the waveform with its trim and envelopes (T-0468).
            waveform = new KlipWaveformTK(spectrum, klip) { onReleased = EndWaveformDrag };
            scroll.Add(waveform);

            // ── action row ──
            scroll.Add(VSpace(Row));
            scroll.Add(BuildActionRow(sourceAvailable));
            scroll.Add(VSpace(Row * 2f));

            // ── time-stretch strip, then the chain editor (T-0462 onward) ──
            scroll.Add(VSpace(Row));
            scroll.Add(new LooperTK(klip));   // T-0476
            scroll.Add(VSpace(4f));
            scroll.Add(new TimeStretchTK(klip));
            scroll.Add(VSpace(Row));
            scroll.Add(new ChainEditorTK(klip, this));

            syncTick = root.schedule.Execute(Sync).Every(200);
        }

        VisualElement BuildActionRow(bool sourceAvailable) {
            const float h = 20f;
            var r = HRow(h);
            // The file actions need the source (disabled on a machine without it), as in the old row.
            var render = ZS.Button("Render", "Bounce the source and its processing into an audio file for this sound.", "RichButton", () => { KlipEditorWindow.ValidateKlip(klip); spectrum.audioSource.clip = KlipEditorWindow.RenderKlip(klip); }, ZUICornerMask.All, 60f, h);
            render.SetEnabled(sourceAvailable);
            r.Add(render);
            r.Add(Gap(4f));
            var remove = ZS.Button("Remove", "Remove this sound after confirmation.", "RichButton", Remove, ZUICornerMask.All, 70f, h);
            remove.SetEnabled(sourceAvailable);
            r.Add(remove);
            if (klip.parentId == 0 && ZoundsProject.Instance.browserSettings.showConvertToZequence) {
                r.Add(Gap(4f));
                var convert = ZS.Button("Convert to Zeq", "Make a sequence containing this sound as a local entry.", "RichButton", ConvertToZeq, ZUICornerMask.All, 100f, h);
                convert.SetEnabled(sourceAvailable);
                r.Add(convert);
            }
            r.Add(Flex());
            r.Add(Gap(4f));
            r.Add(ZS.Button("Force GC", EditorTools.ZoundGcStressTest.Tooltip + "\n\n" + EditorTools.ZoundGcStressTest.lastResult, "RichButton",
                            () => EditorTools.ZoundGcStressTest.Run(IsPlaying()), ZUICornerMask.All, 72f, h));
            r.Add(Gap(5f));
            retriggerButton = ZS.Toggle("Retrigger", "When enabled, every trigger starts this Klip several times. Right-click to set plays, gap and timing.", klip.retriggerEnabled,
                value => { ZoundsWindow.ModifyZoundsProject("toggle klip retrigger", () => { if (value) klip.EnableRetrigger(); else klip.retriggerEnabled = false; }); SyncRetriggerButton(); },
                "RichToggle", ZUICornerMask.All, 88f, h);
            retriggerButton.RegisterCallback<PointerDownEvent>(e => {
                if (e.button != 1) return;
                e.StopPropagation();
                RetriggerPopupTK.Show(retriggerButton, klip, Sync);
            });
            r.Add(retriggerButton);
            r.Add(Gap(5f));
            playButton = ZS.Button("Play", "", "RichButton", PlayOrStop, ZUICornerMask.All, 60f, h);
            WireAuditionMenu(playButton);
            r.Add(playButton);
            SyncPlayButton();
            return r;
        }

        /// <summary>Right-click on Play opens the audition card (T-0486).</summary>
        void WireAuditionMenu(Button b) {
            b.RegisterCallback<PointerDownEvent>(e => {
                if (e.button != 1) return;
                e.StopPropagation();
                EnsureAudition();
                AuditionPopupTK.Show(b, audition);
            });
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
                               + "\n\nRight-click: Play on change, Burst, Loop.";
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
            if (klip == null) return;
            if (FindKlip(targetZoundID) != klip) { Rebuild(); return; }
            fields?.Sync();
            SyncPlayButton();
            SyncRetriggerButton();
        }
    }
}
