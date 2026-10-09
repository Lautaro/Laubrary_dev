using System;
using System.Collections.Generic;
using System.Text;
using Laubrary.Zui;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zounds.Uitk {

    /// <summary>
    /// The Zequence editor window (T-0469; laid out afresh 2026-10-08 on the owner's review): the header fields row, then in
    /// the content box one compact toolbar (Mode, No-Play for a randomizer, Auto length or the authored Length, the timeline
    /// edit tools switch, Tidy, then Retrigger, Bake…, Delete and Play), the audition card when it is pinned, the timeline
    /// header (edit bar and overview when the edit tools are on, the ruler always), one card per track (plain tracks, and
    /// local Zequences as groups holding their own), the MASTER row and the add buttons. Every edit goes through the
    /// project's modify path, so Undo and saving behave as everywhere else.
    ///
    /// The tree is rebuilt only when the Zequence's structure changes; a 5 Hz tick refreshes values, and a 30 Hz tick moves
    /// playheads and entry flashes while anything plays.
    /// </summary>
    public class ZequenceEditorWindowTK : ZuiWindow, IHasCustomMenu {

        [SerializeField] int targetZoundID;
        [SerializeField] bool isLocalZound;
        // Auto length is on by default (owner, 2026-10-08): the Zequence's length follows its longest track.
        [SerializeField] bool autoDuration = true;

        internal const float LeftSectionWidth = 190f;
        /// <summary>A group card's header: two 18 px rows (2026-10-08; it was three rows, 68 px).</summary>
        internal const float GroupHeaderHeight = 4f + 18f + 2f + 18f + 2f;
        internal const float GroupEntryLeftOffset = 10f;

        internal Zequence zeq;
        internal ZoundToken currentToken;
        internal Dictionary<CompositeZound.ZoundEntry, ZoundToken> entryTokens;
        internal readonly List<Action> refreshers = new List<Action>();
        internal readonly List<Action> liveRefreshers = new List<Action>();
        string builtSig;
        ZoundFieldsRowTK fields;
        VisualElement box;
        Button playButton;
        ZuiToggleButton retriggerButton, editToolsButton;
        ScrollView scroll;
        VisualElement pinnedSlot;
        [NonSerialized] bool suppressRebuild;

        // -- the shared timeline (non-destructive editing, T-0558) --
        /// <summary>One time window, selection and set of switches for every track (view state: not saved with the sound).</summary>
        internal ZequenceTimeline timeline;
        TimelineHeaderTK header;
        internal readonly List<TrackStripTK> strips = new List<TrackStripTK>();
        internal readonly List<ZequenceEntryTK> entryViews = new List<ZequenceEntryTK>();
        SwapNoticeTK notice;

        /// <summary>
        /// A shared track was just given its own copy of its sound (destructive editing, 2026-10-09): rebuild now, re-run the
        /// edit that asked for it on the rebuilt card, and in the "Tell me" mode say so, with "Edit the original instead".
        /// </summary>
        internal void AfterTrackSwap(CompositeZound.ZoundEntry entry, Destructive.ZoundsEditGuard.TrackSwap ts, Action<ZequenceEntryTK> redo) {
            Rebuild();
            timeline?.Rebuild();   // the track's placement now holds the copy, so its curves can be picked
            var view = entryViews.Find(v => ReferenceEquals(v.Entry, entry));
            if (view != null) redo?.Invoke(view);
            if (Destructive.ZoundsEditGuard.Mode == ZoundsProject.ProjectSettings.ProtectedEditPrompt.Notice && notice != null) {
                var undoSwap = ts.editOriginal;
                notice.Show(ts.notice, undoSwap == null ? null : (Action)(() => { undoSwap(); Rebuild(); }));
            }
            Say(ts.notice);
        }
        [SerializeField] float viewT0, viewT1 = 1f;
        [SerializeField] bool viewFitted = true, viewFollow, viewRipple, viewLoop;
        [NonSerialized] ZoundToken hereToken;
        [NonSerialized] float hereOffset;
        [NonSerialized] readonly Dictionary<CompositeZound.ZoundEntry, List<ZoundToken>> auditionTokens = new Dictionary<CompositeZound.ZoundEntry, List<ZoundToken>>();
        [NonSerialized] ZoundToken auditionWhole;
        [NonSerialized] float auditionFrom;

        // Per machine: whether the timeline's edit tools are shown, and whether the audition card is pinned into the window.
        const string EditToolsKey = "Laubrary.Zounds.Zequence.EditTools", PinKey = "Laubrary.Zounds.AuditionPinned.Zequence";
        static bool EditTools { get => EditorPrefs.GetBool(EditToolsKey, false); set => EditorPrefs.SetBool(EditToolsKey, value); }
        static bool AuditionPinned { get => EditorPrefs.GetBool(PinKey, false); set => EditorPrefs.SetBool(PinKey, value); }

        /// <summary>
        /// Opens this Zequence's editor — the main one since 2026-09-28. One that is already open is brought forward
        /// instead of opening a second copy, as the old window always did.
        /// </summary>
        public static ZequenceEditorWindowTK Open(Zequence zequence, bool isLocalZound) {
            foreach (var open in Resources.FindObjectsOfTypeAll<ZequenceEditorWindowTK>()) {
                if (open == null || open.targetZoundID != zequence.id) continue;
                if (isLocalZound) open.isLocalZound = true;
                if (open.docked) open.ShowTab(); else open.Focus();
                return open;
            }
            var w = CreateInstance<ZequenceEditorWindowTK>();
            w.targetZoundID = zequence.id;
            w.isLocalZound = isLocalZound;
            w.titleContent = new GUIContent("Zequence: " + zequence.name);
            w.minSize = new Vector2(350f, 200f);
            w.Show();
            return w;
        }

        /// <summary>The tab's ⋮ menu: the old IMGUI editor for the same Zequence, kept for side-by-side comparison.</summary>
        public void AddItemsToMenu(GenericMenu menu) {
            var z = zeq ?? (ZoundsProject.isJSONLoaded ? FindZequence(targetZoundID) : null);
            if (z == null) return;
            bool local = isLocalZound;
            menu.AddItem(new GUIContent("Open IMGUI version"), false, () => { var w = ZequenceEditorWindow.OpenWindow(z); if (local) w.isLocalZound = true; });
        }

        /// <summary>The old window's search: top-level Zequences, then each one's local Zequences.</summary>
        public static Zequence FindZequence(int id) {
            var lib = ZoundsProject.Instance.zoundLibrary;
            var z = lib.zequences.Find(k => k.id == id);
            if (z != null) return z;
            foreach (var zz in lib.zequences) {
                var local = zz.localZequences.Find(l => l.zequence.id == id);
                if (local != null) return local.zequence;
            }
            return null;
        }

        /// <summary>Anything this window's Play/audition started still sounding or queued (T-0486).</summary>
        internal bool IsPlaying() => audition != null && audition.AnyLive;

        // Every play of the whole Zequence (Play, Play on change, Burst, Loop) goes through the audition session and dies
        // with the window (T-0486). Never serialized: a reload or a restored layout always comes back silent.
        [NonSerialized] ZoundAudition audition;
        [SerializeField] bool auditionPlayOnChange;

        void EnsureAudition() {
            if (audition != null && !audition.IsDisposed) return;
            audition = new ZoundAudition(targetZoundID, () => this != null, PlayZequenceOnce,
                                         () => zeq != null && ZoundAudition.ContainsLooper(zeq)) { playOnChange = auditionPlayOnChange };
            ZoundPreviewPlayback.Register(this, audition);
            audition.changed += () => { auditionPlayOnChange = audition != null && audition.playOnChange; SyncPlayButton(); };
            audition.Attach(rootVisualElement);
        }

        /// <summary>One play of the whole Zequence, as the old window's Play; the latest one drives the playhead.</summary>
        ZoundToken PlayZequenceOnce() {
            if (zeq == null) return null;
            currentToken = CompositeZoundEditing.SimulatePlay(zeq, isLocalZound, this, audition);
            return currentToken;
        }

        /// <summary>Closing, and the disable Unity sends before every script reload: nothing this window started may
        /// outlive it — the audition's plays and queue, and the per-entry preview plays too.</summary>
        void KillAudition() {
            if (audition != null) {
                auditionPlayOnChange = audition.playOnChange;
                audition.Dispose();
                audition = null;
            }
            StopTimelinePlays();
            if (entryTokens != null) {
                foreach (var t in entryTokens.Values) {
                    try { if (t != null && t.state != ZoundToken.State.Killed) t.Kill(); }
                    catch (Exception e) { Debug.LogException(e); }
                }
                entryTokens.Clear();
            }
            currentToken = null;
        }

        protected override void OnDisable() {
            ZoundPreviewPlayback.Dispose(this);
            KillAudition();
            base.OnDisable();
        }

        void OnDestroy() => KillAudition();

        void SyncPlayButton() {
            if (playButton == null) return;
            bool live = audition != null && audition.PlayControlStops;
            bool armed = audition != null ? audition.playOnChange : auditionPlayOnChange;
            playButton.text = (live ? "Stop" : "Play") + (armed ? " •" : "");
            playButton.style.backgroundColor = live ? new Color(.22f, .34f, .52f, 1f) : StyleKeyword.Null;
            playButton.tooltip = (live ? (audition.IsLoopPlaying(audition) ? "Stop loop" : "Stop the queued audition run.")
                                       : "Play this Zequence.")
                               + (armed ? "\n\n• Play on change is on: every change you make here plays the sound again." : "")
                               + (AuditionPinned ? "\n\nPlay on change, Burst and Loop are on the pinned card below." : "\n\nRight-click: Play on change, Burst, Loop.");
        }

        internal void Modify(string undo, Action a) { ZoundsWindow.ModifyZoundsProject(undo, a); Tick(); }

        // ─────────────────────────── build ───────────────────────────

        protected override void BuildUI(VisualElement root) {
            // Straight after a script reload this window rebuilds before Unity's editor styles exist, and the shared
            // measurements below read them (see ZS.EditorStylesReady); build a moment later instead of half-building.
            if (!ZS.EditorStylesReady) { root.schedule.Execute(Rebuild).StartingIn(100); return; }
            ZS.Attach(root);
            root.AddToClassList("zs-zequence");
            root.AddToClassList("zs-zequence-editor__root");
            refreshers.Clear(); liveRefreshers.Clear();
            zeq = ZoundsProject.isJSONLoaded ? FindZequence(targetZoundID) : null;
            if (zeq == null) { root.Add(new Label(ZoundsProject.isJSONLoaded ? "Zequence no longer exists in the project." : "Zounds Project is not loaded.")); return; }
            titleContent = new GUIContent("Zequence: " + zeq.name);
            EnsureAudition();
            EnsureEnvelopes();
            EnsureTimeline();
            strips.Clear();
            entryViews.Clear();
            // Keep the scroll position across a rebuild (an edit that changes the tree must never snap the view to the top).
            float keepScroll = scroll != null ? scroll.scrollOffset.y : 0f;
            var es = ZoundsProject.Instance.projectSettings.editorStyle;

            fields = new ZoundFieldsRowTK(zeq, isLocalZound, () => titleContent = new GUIContent("Zequence: " + zeq.name));
            root.Add(fields);
            root.Add(Space(4f));

            box = new VisualElement();
            box.AddToClassList("zs-box-default");
            box.AddToClassList("zs-zequence-editor__box");
            SettingsTabTK.ApplyEditorBackground(box);
            root.Add(box);
            WireDropTarget(box);
            box.Add(Toolbar());
            pinnedSlot = new VisualElement();
            pinnedSlot.AddToClassList("zs-zequence-editor__pinned");
            box.Add(pinnedSlot);
            SyncPinned();
            box.Add(Space(4f));
            header = new TimelineHeaderTK(this);
            header.SetEditToolsVisible(EditTools);
            box.Add(header);
            box.Add(Space(3f));
            if (zeq.zoundEntries.Count == 0) {
                var none = new Label("No tracks yet: add one below.");
                none.AddToClassList("zs-lbl"); none.AddToClassList("zs-greymini");
                none.style.height = EditorGUIUtility.singleLineHeight;
                box.Add(none);
            }

            scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("zs-zequence-editor__scroll");
            box.Add(scroll);
            bool darker = false;
            for (int i = 0; i < zeq.zoundEntries.Count; i++) {
                scroll.Add(new ZequenceEntryTK(this, zeq, zeq.zoundEntries[i], i, ZequenceTimeline.Mid(zeq), 0f, darker, false));
                scroll.Add(Space(4f));
                darker = !darker;
            }
            scroll.Add(MasterSection());
            box.Add(AddRow(zeq, true));
            if (keepScroll > 0f) scroll.schedule.Execute(() => scroll.scrollOffset = new Vector2(0f, keepScroll));

            // The "Tell me" notice of destructive editing floats along the bottom of the window (never moves anything).
            notice = new SwapNoticeTK();
            notice.AddToClassList("zs-zequence-editor__notice");
            root.Add(notice);

            builtSig = Signature();
            root.focusable = true;
            root.RegisterCallback<KeyDownEvent>(OnKey, TrickleDown.TrickleDown);
            root.schedule.Execute(Tick).Every(200);
            root.schedule.Execute(LiveTick).Every(33);
        }

        internal static VisualElement Space(float h) { var e = new VisualElement(); e.style.height = h; e.AddToClassList("zs-zequence-editor__space"); return e; }
        internal static VisualElement Gap(float w) { var e = new VisualElement(); e.style.width = w; e.AddToClassList("zs-zequence-editor__gap"); return e; }
        internal static VisualElement Flex() { var e = new VisualElement(); e.AddToClassList("zs-zequence-editor__spacer"); return e; }

        /// <summary>Everything that decides the tree's shape; when it changes, the window is rebuilt.</summary>
        string Signature() {
            if (zeq == null) return "";
            var sb = new StringBuilder();
            Append(sb, zeq);
            sb.Append(zeq.masterVolumeEnvelope != null && zeq.masterVolumeEnvelope.enabled ? 'M' : 'm');
            sb.Append(autoDuration ? 'A' : 'a').Append(EditTools ? 'T' : 't').Append(AuditionPinned ? 'P' : 'p');
            return sb.ToString();
        }

        static void Append(StringBuilder sb, CompositeZound c) {
            sb.Append((int)c.mode).Append('[');
            foreach (var e in c.zoundEntries) {
                bool found = c.TryGetEntryZound(e, out var z);
                sb.Append(e.zoundId).Append(e.local ? 'L' : 'S').Append(found ? 'f' : 'x').Append(e.volumeEnvelope != null && e.volumeEnvelope.enabled ? 'E' : 'e')
                  .Append(e.editor_foldoutExpanded ? 'X' : 'c').Append(e.editor_isRenaming ? 'R' : 'r').Append((int)e.editor_height).Append('h');
                if (z is Klip k) sb.Append(k.originalId != 0 ? 'o' : '-');
                if (found && e.local && z is CompositeZound cc) Append(sb, cc);
                sb.Append(';');
            }
            sb.Append(']');
        }

        void Tick() {
            if (zeq == null) return;
            if (!suppressRebuild && (FindZequence(targetZoundID) != zeq || Signature() != builtSig)) { EnsureEnvelopes(); Rebuild(); return; }
            fields?.Sync();
            SyncPlayButton();
            SyncRetriggerButton();
            SettingsTabTK.ApplyEditorBackground(box);   // the Settings tab's colour, live (and after an undo)
            timeline?.Rebuild();
            foreach (var r in refreshers) r();
            header?.Sync();
            SaveView();
        }

        void LiveTick() {
            if (zeq == null) return;
            foreach (var r in liveRefreshers) r();
            if (timeline == null) return;
            UpdateLane();
            timeline.cursor = CursorTime();
            timeline.FollowCursor();
            foreach (var s in strips) s.Sync();
            header?.Repaint();
        }

        /// <summary>The old window's OnValidateEnvelopeGUIs: a Zequence always has a master volume envelope, and every entry a volume envelope.</summary>
        void EnsureEnvelopes() {
            if (zeq.masterVolumeEnvelope == null || zeq.masterVolumeEnvelope.Count == 0) {
                zeq.masterVolumeEnvelope = new Envelope(Zound.MinVolumeRange, Zound.MaxVolumeRange);
                EditorUtility.SetDirty(ZoundsProject.Instance);
            }
            void Ensure(CompositeZound c) {
                foreach (var e in c.zoundEntries) {
                    if (e.volumeEnvelope == null || e.volumeEnvelope.Count == 0) {
                        e.volumeEnvelope = new Envelope(Zound.MinVolumeRange, Zound.MaxVolumeRange);
                        EditorUtility.SetDirty(ZoundsProject.Instance);
                    }
                    if (e.local && c.TryGetEntryZound(e, out var z) && z is CompositeZound cc) Ensure(cc);
                }
            }
            Ensure(zeq);
        }

        // ─────────────────────────── toolbar ───────────────────────────

        const float ToolH = 20f;

        VisualElement Toolbar() {
            var r = new VisualElement();
            r.AddToClassList("zs-zeq-toolbar");
            r.AddToClassList("zs-zequence-editor__toolbar-row");
            // Mode: a segmented choice, every option in view.
            var modes = (CompositeZound.Mode[])Enum.GetValues(typeof(CompositeZound.Mode));
            var names = new string[modes.Length];
            for (int i = 0; i < modes.Length; i++) names[i] = modes[i] == CompositeZound.Mode.RoundRobin ? "Round robin" : modes[i].ToString();
            var mode = Z.Segmented(Array.IndexOf(modes, zeq.mode), names,
                "How the tracks play: all together (Parallel), one picked by weight (Randomizer), each in turn (Round robin), or in order (Playlist).",
                i => Modify("change zequence mode", () => zeq.mode = modes[i]));
            mode.AddToClassList("zs-zequence-editor__toolbar-mode");
            refreshers.Add(() => mode.SetOn(i => modes[i] == zeq.mode));
            r.Add(mode);
            r.Add(Gap(6f));
            if (zeq.mode == CompositeZound.Mode.Randomizer) {
                var noPlayLabel = new Label("No-play") { tooltip = "Weight for the randomizer to play nothing at all on a trigger." };
                noPlayLabel.AddToClassList("zs-lbl"); noPlayLabel.AddToClassList("zs-zequence-editor__toolbar-label");
                r.Add(noPlayLabel);
                var noPlay = Z.Int(zeq.noPlayWeight, noPlayLabel.tooltip, v => Modify("change no play weight", () => zeq.noPlayWeight = Mathf.Max(0, v)), 40f);
                noPlay.AddToClassList("zs-imgui-field"); noPlay.AddToClassList("zs-bare-int"); noPlay.AddToClassList("zs-zequence-editor__toolbar-field");
                refreshers.Add(() => { if (noPlay.focusController?.focusedElement != noPlay) noPlay.SetValueWithoutNotify(zeq.noPlayWeight); });
                r.Add(noPlay);
                r.Add(Gap(6f));
            }
            // Auto length (on by default), or the authored length when it is off.
            var auto = ZS.Toggle("Auto length", "", autoDuration, v => { autoDuration = v; if (autoDuration) CompositeZoundEditing.AutoApplyDuration(zeq); Tick(); }, "RichToggle", ZUICornerMask.All, 80f, ToolH);
            r.Add(auto);
            FloatField length = null;
            if (!autoDuration) {
                r.Add(Gap(4f));
                var lengthLabel = new Label("Length") { tooltip = "The Zequence's authored length in seconds: how far the timeline reaches. Playback is not cut here; it only sets the authored end." };
                lengthLabel.AddToClassList("zs-lbl"); lengthLabel.AddToClassList("zs-zequence-editor__toolbar-label");
                r.Add(lengthLabel);
                length = Z.Float(zeq.editor_maxDuration / zeq.minPitch, lengthLabel.tooltip, v => Modify("change max duration", () => {
                    zeq.editor_maxDuration = Mathf.Max(0.01f, v) * zeq.minPitch;
                    CompositeZoundEditing.RecalculateMaxDuration(zeq, autoDuration);
                }), 56f, 2);
                length.AddToClassList("zs-imgui-field"); length.AddToClassList("zs-bare-int"); length.AddToClassList("zs-zequence-editor__toolbar-field");
                refreshers.Add(() => { if (length.focusController?.focusedElement == null || !length.Contains(length.focusController.focusedElement as VisualElement)) length.SetValueWithoutNotify(zeq.editor_maxDuration / zeq.minPitch); });
                r.Add(length);
            }
            refreshers.Add(() => {
                auto.SetValueWithoutNotify(autoDuration);
                auto.tooltip = autoDuration ? "The Zequence's length follows its longest track. Click to set a length of your own."
                                            : "The length is set by hand (the Length box). Click to have it follow the longest track.";
            });
            r.Add(Gap(6f));
            editToolsButton = ZS.Toggle("Edit tools", "", EditTools, v => { EditTools = v; header?.SetEditToolsVisible(v); Tick(); }, "RichToggle", ZUICornerMask.Left, 70f, ToolH);
            refreshers.Add(() => editToolsButton.tooltip = EditTools ? "The timeline's edit tools (zoom, follow, ripple, trim, split, delete, copy and paste, and the overview strip) are shown. Click to hide them and keep the window lean."
                                                                     : "Show the timeline's edit tools: zoom, follow, ripple, trim, split, delete, copy and paste, and the overview strip.");
            r.Add(editToolsButton);
            r.Add(ZS.Button("Tidy", "Show everything in the least space: fit the whole Zequence into view, every track at its default height, and clear the selection.", "RichButton", Tidy, ZUICornerMask.Right, 44f, ToolH));
            r.Add(Flex());
            retriggerButton = ZS.Toggle("Retrigger", "When enabled, every trigger starts this Zequence several times. Right-click to set plays, gap and timing.", zeq.retriggerEnabled,
                value => { Modify("toggle zequence retrigger", () => { if (value) zeq.EnableRetrigger(); else zeq.retriggerEnabled = false; }); SyncRetriggerButton(); },
                "RichToggle", ZUICornerMask.All, 88f, ToolH);
            retriggerButton.RegisterCallback<PointerDownEvent>(e => {
                if (e.button != 1) return;
                e.StopPropagation();
                RetriggerPopupTK.Show(retriggerButton, zeq, Tick);
            });
            r.Add(retriggerButton);
            r.Add(Gap(6f));
            r.Add(ZS.Button("Bake…", "Write the Zequence, or the selected time range, to a new audio file and a new Klip. The Zequence itself stays as it is.", "RichButton", OpenBake, ZUICornerMask.All, 52f, ToolH));
            r.Add(Gap(6f));
            r.Add(IconButton("remove", "Delete this Zequence from the project (asks first). Cannot be undone.", "RichButton", ZUICornerMask.All, 30f, ToolH, RemoveZound));
            r.Add(Gap(6f));
            playButton = ZS.Button("Play", "", "RichButton", () => {
                EnsureAudition();
                if (audition.BurstRunning || audition.LoopRunning) audition.StopRun();
                else audition.PlayOnce();
                Tick();
            }, ZUICornerMask.All, 60f, ToolH);
            // Right-click: the audition card (T-0486), unless it is pinned into the window.
            var pb = playButton;
            pb.RegisterCallback<PointerDownEvent>(e => {
                if (e.button != 1) return;
                e.StopPropagation();
                EnsureAudition();
                if (AuditionPinned) return;
                AuditionPopupTK.Show(pb, audition, v => { AuditionPinned = v; SyncPinned(); Tick(); });
            });
            r.Add(playButton);
            SyncPlayButton();
            SyncRetriggerButton();
            return r;
        }

        /// <summary>The pinned audition card under the toolbar, or nothing: the slot keeps no height while empty.</summary>
        void SyncPinned() {
            if (pinnedSlot == null) return;
            pinnedSlot.Clear();
            if (!AuditionPinned) { SyncPlayButton(); return; }
            EnsureAudition();
            var card = new AuditionCardTK(audition, true, v => { AuditionPinned = v; SyncPinned(); Tick(); });
            card.AddToClassList("zs-audition-card--pinned");
            pinnedSlot.Add(card);
            SyncPlayButton();
        }

        /// <summary>Tidy: fit the view, every track back to its default height, no selection, scrolled to the top.</summary>
        void Tidy() {
            bool any = false;
            void Reset(CompositeZound c) {
                foreach (var e in c.zoundEntries) {
                    if (e.editor_height != 0f) { e.editor_height = 0f; any = true; }
                    if (e.local && c.TryGetEntryZound(e, out var z) && z is CompositeZound cc) Reset(cc);
                }
            }
            Reset(zeq);
            if (any) EditorUtility.SetDirty(ZoundsProject.Instance);
            timeline?.ClearSelection();
            timeline?.Fit();
            if (scroll != null) scroll.scrollOffset = Vector2.zero;
            Tick();
        }

        void SyncRetriggerButton() {
            if (retriggerButton == null || zeq == null) return;
            retriggerButton.SetValueWithoutNotify(zeq.retriggerEnabled);
            retriggerButton.text = zeq.retriggerEnabled ? "Retrigger " + RetriggerPopupTK.Summary(zeq) : "Retrigger";
            retriggerButton.tooltip = zeq.retriggerEnabled
                ? "Every trigger starts this Zequence " + zeq.retriggerCount + " times. Right-click to change plays, gap and timing."
                : "Enable several plays for every trigger. Right-click to set plays, gap and timing.";
        }

        void RemoveZound() {
            if (!AudioAssetUtility.DisplayZoundRemoveDialog(zeq)) return;
            ZoundsWindow.ModifyZoundsProject("remove zound", () => AudioAssetUtility.RemoveZound(zeq), true);
            Close();
        }

        /// <summary>A ZUI button whose face is one of the Zounds window icons (remove, duplicate, make-shared…).</summary>
        internal static Button IconButton(string icon, string tooltip, string style, ZUICornerMask corners, float w, float h, Action onClick) {
            var b = ZS.Button("", tooltip, style, onClick, corners, w, h);
            var tex = ZUI.FindIcon(icon) ?? Resources.Load<Texture>("ZoundsWindowIcons/" + icon);
            var img = new Image { image = tex, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
            img.AddToClassList("zs-buttonicon");
            b.Add(img);
            return b;
        }

        // ─────────────────────────── master section ───────────────────────────

        /// <summary>MASTER: one row with the master volume curve switch; with it on, the curve over the Zequence's own length,
        /// drawn in the shared lane under the tracks.</summary>
        VisualElement MasterSection() {
            float lh = EditorGUIUtility.singleLineHeight;
            bool enabled = zeq.masterVolumeEnvelope.enabled;
            var rect = new VisualElement();
            rect.style.height = enabled ? lh * 4f + 8f : lh + 8f; rect.AddToClassList("zs-zequence-editor__master-section-rect");
            var label = new Label("MASTER") { tooltip = "The whole Zequence's own volume curve, over its length." };
            label.AddToClassList("zs-lbl"); label.AddToClassList("zs-text-subheader"); label.AddToClassList("zs-subheader");
            Place(label, 4f, 4f, 70f, lh);
            label.AddToClassList("zs-zequence-editor__master-section-label");
            rect.Add(label);
            var use = ZS.Toggle("Volume curve", enabled ? "The master volume curve shapes every play of the Zequence. Click to switch it off." : "Switch on a volume curve over the whole Zequence.",
                                enabled, v => Modify("toggle master volume envelope", () => zeq.masterVolumeEnvelope.enabled = v), "RichToggle", ZUICornerMask.All, 90f, lh);
            Place(use, 4f + 74f, 4f, 90f, lh);
            rect.Add(use);
            if (enabled) {
                // The master curve over the Zequence's own length, edited on a copy written back through the modify path.
                var bg = new VisualElement { pickingMode = PickingMode.Ignore };
                bg.AddToClassList("zs-zequence-editor__master-section-bg");
                rect.Add(bg);
                var copy = zeq.masterVolumeEnvelope.DeepCopy();
                var es = ZoundsProject.Instance.projectSettings.editorStyle;
                var curve = new EnvelopeTK(copy, es.volumeEnvelopeColor) { thickness = es.volumeEnvelopeThickness };
                curve.AddToClassList("zs-zequence-editor__master-section-curve");
                curve.onChanged = () => Modify("modify master volume envelope", () => { zeq.masterVolumeEnvelope = copy.DeepCopy(); zeq.masterVolumeEnvelope.enabled = true; });
                rect.Add(curve);
                var head = new VisualElement { pickingMode = PickingMode.Ignore };
                head.AddToClassList("zs-zequence-editor__master-section-head");
                head.style.backgroundColor = es.playerHeadColor;
                rect.Add(head);
                void Layout() {
                    float w = rect.layout.width;
                    if (float.IsNaN(w) || w <= 0f) return;
                    // In the shared lane: the curve spans the Zequence's drawn length on the same time axis as the tracks.
                    float laneX = ZequenceEntryTK.LaneLeft, laneW = Mathf.Max(10f, w - ZequenceEntryTK.LaneLeft - ZequenceEntryTK.LaneRight);
                    float dur = timeline != null ? timeline.FitEnd : CompositeZoundEditing.CalculateCompositeDuration(zeq, zeq.minPitch);
                    float x0 = laneX + (timeline != null ? timeline.TimeToLaneX(0f) * laneW / Mathf.Max(1f, timeline.laneWorld.width) : 0f);
                    float x1 = laneX + (timeline != null ? timeline.TimeToLaneX(dur) * laneW / Mathf.Max(1f, timeline.laneWorld.width) : laneW);
                    var bgRect = new Rect(Mathf.Max(laneX, x0), 4f + lh + 2f, Mathf.Max(1f, Mathf.Min(laneX + laneW, x1) - Mathf.Max(laneX, x0)), lh * 3f);
                    Place(bg, bgRect.x, bgRect.y, bgRect.width, bgRect.height);
                    Place(curve, bgRect.x, bgRect.y, bgRect.width, bgRect.height);
                    bool playing = currentToken != null && currentToken.state != ZoundToken.State.Killed;
                    head.style.display = playing ? DisplayStyle.Flex : DisplayStyle.None;
                    if (playing && timeline != null) {
                        float hx = laneX + timeline.TimeToLaneX(currentToken.time) * laneW / Mathf.Max(1f, timeline.laneWorld.width);
                        Place(head, hx, bgRect.y, 1f, bgRect.height);
                    }
                }
                rect.RegisterCallback<GeometryChangedEvent>(_ => Layout());
                liveRefreshers.Add(Layout);
            }
            return rect;
        }

        internal static void Place(VisualElement e, float x, float y, float w, float h) {
            e.AddToClassList("zs-zequence-editor__positioned-control");
            e.style.left = x; e.style.top = y;
            if (w >= 0f) e.style.width = Mathf.Max(0f, w);
            if (h >= 0f) e.style.height = Mathf.Max(0f, h);
        }

        // ─────────────────────────── add buttons ───────────────────────────

        /// <summary>The "+ Local Klip / + Local Zequence / + Shared Zound" row under the entries (the top level's).</summary>
        VisualElement AddRow(CompositeZound parent, bool topLevel) {
            var r = new VisualElement();
            r.AddToClassList("zs-zequence-editor__add-row-row");
            r.Add(Flex());
            Button localKlip = null, sharedZound = null;
            localKlip = ZS.Button("+ Local Klip", "Add a track with a new sound of its own (a copy of a library sound, or a new recording).", "RichButton", () => AddLocalKlip(parent, localKlip), ZUICornerMask.Left, 85f, ToolH);
            r.Add(localKlip);
            var localZeq = ZS.Button("+ Local Zequence", "Add a nested Zequence of this one's own.", "RichButton", () => {
                var newZequence = new Zequence(ZoundLibrary.GetUniqueZoundId());
                newZequence.name = ZoundDictionary.EnsureUniqueZoundName("Zequence");
                newZequence.parentId = parent.id;
                parent.localZequences.Add(new CompositeZound.LocalZequence(newZequence));
                CompositeZoundEditing.AddNewZoundEntry(zeq, parent, newZequence, true, autoDuration);
                Tick();
            }, ZUICornerMask.None, 125f, ToolH);
            r.Add(localZeq);
            sharedZound = ZS.Button("+ Shared Zound", "Add a track that plays a library sound (shared: editing that sound changes it everywhere).", "RichButton", () => AddShared(parent, sharedZound), ZUICornerMask.Right, 105f, ToolH);
            r.Add(sharedZound);
            return r;
        }

        internal void AddLocalKlip(CompositeZound parent, VisualElement from) {
            BrowserTab.OpenCreateNewKlipDialog(klip => {
                klip.parentId = parent.id;
                parent.localKlips.Add(klip);
                CompositeZoundEditing.AddNewZoundEntry(zeq, parent, klip, true, autoDuration);
                Tick();
            }, previewOwner: this);
        }

        internal void AddShared(CompositeZound parent, VisualElement from) {
            CompositeZoundEditing.AddNewEntryFromExisting(parent, zound => { CompositeZoundEditing.AddNewZoundEntry(zeq, parent, zound, false, autoDuration); Tick(); }, this);
        }

        internal bool AutoDuration => autoDuration;

        // ── drop target (2026-10-08): rows dragged out of the picker, or clips from the Project window, become tracks ──

        /// <summary>What a drag over the editor would add: picker items (clips become local Klips, sounds shared tracks),
        /// or Project-window clips as local Klips; null when the drag carries nothing of the kind.</summary>
        List<ZoundPickerItem> Dropped() {
            if (DragAndDrop.GetGenericData(ZoundPickerWindowTK.DragKey) is List<ZoundPickerItem> items && items.Count > 0) return items;
            List<ZoundPickerItem> clips = null;
            foreach (var o in DragAndDrop.objectReferences) {
                if (!(o is AudioClip c)) continue;
#if ADDRESSABLES_INSTALLED
                var r = AudioRenderUtility.GetAudioReference(c);
                if (r == null) continue;
                (clips ??= new List<ZoundPickerItem>()).Add(new ZoundPickerItem { key = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(c)), name = c.name, kind = ZoundPickerItem.Kind.Clip, clip = c, audioRef = r });
#endif
            }
            return clips;
        }

        void WireDropTarget(VisualElement target) {
            target.RegisterCallback<DragUpdatedEvent>(e => {
                if (Dropped() == null) return;
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                e.StopPropagation();
            });
            target.RegisterCallback<DragPerformEvent>(e => {
                var items = Dropped();
                if (items == null) return;
                DragAndDrop.AcceptDrag();
                e.StopPropagation();
                int group = Undo.GetCurrentGroup();
                foreach (var it in items) {
                    if (it.zound != null) {
                        if (it.zound.id == zeq.id || (it.zound is CompositeZound cz && ZequenceHandler.CheckRecursiveness(cz, zeq))) continue;
                        CompositeZoundEditing.AddNewZoundEntry(zeq, zeq, it.zound, false, autoDuration);
                    }
#if ADDRESSABLES_INSTALLED
                    else if (it.audioRef != null) {
                        ZoundsWindow.ModifyZoundsProject("add local klip", () => {
                            var klip = BrowserTab.CreateKlipFromAudioRef(it.audioRef, null);
                            if (klip == null) return;
                            klip.parentId = zeq.id;
                            zeq.localKlips.Add(klip);
                            CompositeZoundEditing.AddNewZoundEntry(zeq, zeq, klip, true, autoDuration);
                        });
                    }
#endif
                }
                Undo.CollapseUndoOperations(group);
                Tick();
            });
        }

        // ─────────────────────────── the shared timeline ───────────────────────────

        void EnsureTimeline() {
            if (timeline == null) {
                timeline = new ZequenceTimeline { t0 = viewT0, t1 = Mathf.Max(viewT0 + 0.01f, viewT1), fitted = viewFitted, follow = viewFollow, ripple = viewRipple, loop = viewLoop };
                timeline.changed += () => { foreach (var s in strips) s.MarkDirtyRepaint(); header?.Repaint(); header?.Sync(); };
            }
            if (!ReferenceEquals(timeline.zeq, zeq)) { timeline.zeq = zeq; timeline.ClearSelection(); timeline.editingCurve.Clear(); }
            timeline.Rebuild();
        }

        void SaveView() {
            if (timeline == null) return;
            viewT0 = timeline.t0; viewT1 = timeline.t1; viewFitted = timeline.fitted;
            viewFollow = timeline.follow; viewRipple = timeline.ripple; viewLoop = timeline.loop;
        }

        /// <summary>The lane every track and the ruler draw in: the first top-level card's timeline area, so all of them
        /// line up whatever the scroll bar does.</summary>
        void UpdateLane() {
            foreach (var s in strips) {
                if (!(s.parent is ZequenceEntryTK card) || card.IsGroupChild) continue;
                var wb = card.worldBound;
                if (wb.width < 50f) continue;
                float x = wb.x + ZequenceEntryTK.LaneLeft;
                float w = wb.width - ZequenceEntryTK.LaneLeft - ZequenceEntryTK.LaneRight;
                var lane = new Rect(x, 0f, Mathf.Max(10f, w), 0f);
                if (lane != timeline.laneWorld) { timeline.laneWorld = lane; header?.Repaint(); }
                return;
            }
            if (header != null) {
                var wb = header.worldBound;
                timeline.laneWorld = new Rect(wb.x + ZequenceEntryTK.LaneLeft, 0f, Mathf.Max(10f, wb.width - ZequenceEntryTK.LaneLeft - ZequenceEntryTK.LaneRight), 0f);
            }
        }

        /// <summary>After any edit on the timeline: placements again, then every lane and the header.</summary>
        internal void OnTimelineChanged() {
            if (timeline == null) return;
            timeline.Rebuild();
            foreach (var s in strips) s.Sync();
            header?.Sync(); header?.Repaint();
            foreach (var r in refreshers) r();
        }

        /// <summary>After the view moved (zoom, pan): redraw only.</summary>
        internal void OnViewChanged() {
            foreach (var s in strips) s.Sync();
            header?.Repaint();
        }

        /// <summary>While a track's height grip is dragged: no rebuild (it would drop the grip mid-drag); the card resizes itself.</summary>
        internal void OnTrackHeightChanged(ZequenceEntryTK card) { suppressRebuild = true; }

        /// <summary>Applies a structural view change now (a track's new height is kept, so the cards are rebuilt).</summary>
        internal void RefreshNow() { suppressRebuild = false; Tick(); }

        internal void Say(string s) { if (timeline != null) { timeline.readout = s ?? ""; header?.Sync(); } }

        /// <summary>The Zequence time the window's own plays are at: Play from here, the audition, or Play.</summary>
        float CursorTime() {
            if (hereToken != null && hereToken.state != ZoundToken.State.Killed) return hereOffset + hereToken.time;
            if (auditionWhole != null && auditionWhole.state != ZoundToken.State.Killed) {
                float t = auditionFrom + auditionWhole.time;
                // The audition of a range stops at the range's end (a tail still rings out); with Loop it starts again.
                if (timeline.hasSel && t > timeline.selB) {
                    auditionWhole.Kill(timeline.loop ? 0.03f : 0.25f);
                    if (timeline.loop) { auditionWhole = TimelineEdits.PlayFrom(zeq, timeline.selA, isLocalZound, this); return timeline.selA; }
                    return -1f;
                }
                return t;
            }
            foreach (var kv in auditionTokens)
                foreach (var t in kv.Value) if (t != null && t.state != ZoundToken.State.Killed) return auditionFrom + t.time;
            if (currentToken != null && currentToken.state != ZoundToken.State.Killed) return currentToken.time;
            return -1f;
        }

        /// <summary>Every play of <paramref name="entry"/> this window can see: through its Zequence's plays, and auditions.</summary>
        internal List<ZoundToken> TokensPlaying(CompositeZound.ZoundEntry entry) {
            s_tokens.Clear();
            if (timeline != null && timeline.byEntry.TryGetValue(entry, out var p) && p.parent != null && ZoundEngine.CullingGroups.TryGetValue(p.parent, out var playing)) {
                foreach (var token in playing) {
                    if (token == null || token.state == ZoundToken.State.Killed) continue;
                    if (!token.TryGetEntryToken(entry, out var child) || child.state == ZoundToken.State.Killed) continue;
                    if (token.IsEntryMuted(entry)) continue;
                    s_tokens.Add(child);
                }
            }
            if (auditionTokens.TryGetValue(entry, out var aud)) foreach (var t in aud) if (t != null && t.state != ZoundToken.State.Killed) s_tokens.Add(t);
            return s_tokens;
        }
        static readonly List<ZoundToken> s_tokens = new List<ZoundToken>();

        void StopTimelinePlays() {
            try { if (hereToken != null && hereToken.state != ZoundToken.State.Killed) hereToken.Kill(); } catch (Exception e) { Debug.LogException(e); }
            try { if (auditionWhole != null && auditionWhole.state != ZoundToken.State.Killed) auditionWhole.Kill(); } catch (Exception e) { Debug.LogException(e); }
            foreach (var kv in auditionTokens) foreach (var t in kv.Value) { try { if (t != null && t.state != ZoundToken.State.Killed) t.Kill(); } catch (Exception e) { Debug.LogException(e); } }
            auditionTokens.Clear();
            hereToken = null; auditionWhole = null;
        }

        bool TimelinePlaying() {
            if (hereToken != null && hereToken.state != ZoundToken.State.Killed) return true;
            if (auditionWhole != null && auditionWhole.state != ZoundToken.State.Killed) return true;
            foreach (var kv in auditionTokens) foreach (var t in kv.Value) if (t != null && t.state != ZoundToken.State.Killed) return true;
            return false;
        }

        /// <summary>Play the whole Zequence from the clicked moment (or the selection's start). Pressed again: stop.</summary>
        internal void PlayFromHere() {
            if (TimelinePlaying()) { StopTimelinePlays(); return; }
            float from = timeline.hasSel ? timeline.selA : Mathf.Max(0f, timeline.t0);
            hereOffset = from;
            hereToken = TimelineEdits.PlayFrom(zeq, from, isLocalZound, this);
            Say(hereToken != null ? "Playing from " + ZequenceTimeline.Seconds(from) + "." : "Nothing to play from there.");
        }

        /// <summary>
        /// Plays one track on its own (a click on its waveform: from its start; a right-click: from the clicked source second,
        /// 2026-10-08). A track already sounding this way is stopped instead. A nested Zequence plays through the
        /// Zequence soloed to it, as its play button does.
        /// </summary>
        internal void PlayTrackFrom(CompositeZound.ZoundEntry entry, float sourceSeconds) {
            if (timeline == null || !timeline.byEntry.TryGetValue(entry, out var p) || !p.found) return;
            if (auditionTokens.TryGetValue(entry, out var live) && live.Exists(t => t != null && t.state != ZoundToken.State.Killed)) {
                foreach (var t in live) { try { if (t != null && t.state != ZoundToken.State.Killed) t.Kill(0.03f); } catch (Exception e) { Debug.LogException(e); } }
                auditionTokens.Remove(entry);
                return;
            }
            if (p.klip == null) { CompositeZoundEditing.ToggleEntryPlay(zeq, ref entryTokens, entry, this); return; }
            float a = float.IsNaN(sourceSeconds) ? p.exA : Mathf.Clamp(sourceSeconds, p.exA, p.exB);
            var token = TimelineEdits.PlayTrack(p, a, this);
            if (token == null) return;
            auditionFrom = p.SourceToTime(a);
            auditionTokens[entry] = new List<ZoundToken> { token };
        }

        /// <summary>Play only the selection (Space). Pressed again while it sounds: stop.</summary>
        internal void AuditionSelection() {
            if (TimelinePlaying()) { StopTimelinePlays(); return; }
            if (!timeline.hasSel || timeline.selB <= timeline.selA) { Say("Select a time range to audition."); return; }
            auditionFrom = timeline.selA;
            var tracks = TimelineEdits.Selected(timeline);
            if (tracks.Count == 0) { auditionWhole = TimelineEdits.PlayFrom(zeq, timeline.selA, isLocalZound, this); return; }
            var tokens = TimelineEdits.Audition(timeline, isLocalZound, this);
            // Pair each audition play with its track, for its playhead (Audition plays them in the same order).
            int i = 0;
            foreach (var p in tracks) {
                if (p.klip == null || i >= tokens.Count) continue;
                float a = Mathf.Max(p.exA, p.TimeToSource(timeline.selA)), b = Mathf.Min(p.exB, p.TimeToSource(timeline.selB));
                if (b <= a + 0.002f) continue;
                if (!auditionTokens.TryGetValue(p.entry, out var list)) auditionTokens[p.entry] = list = new List<ZoundToken>();
                list.Add(tokens[i++]);
            }
            if (tokens.Count == 0) Say("The selection holds no audio of the selected tracks.");
        }

        internal float PasteTime() => timeline.hasSel ? timeline.selA : timeline.cursor >= 0f ? timeline.cursor : Mathf.Max(0f, timeline.t0);

        internal void OpenBake() => ZequenceBakePopup.Show(header != null ? header.worldBound.position + new Vector2(200f, 20f) : Vector2.zero, zeq, timeline, isLocalZound);

        void OnKey(KeyDownEvent e) {
            if (timeline == null) return;
            // Typing into a field is never a timeline command.
            if (e.target is VisualElement ve && (ve is TextField || ve is FloatField || ve is IntegerField
                || ve.GetFirstAncestorOfType<TextField>() != null || ve.GetFirstAncestorOfType<FloatField>() != null || ve.GetFirstAncestorOfType<IntegerField>() != null)) return;
            bool ctrl = e.ctrlKey || e.commandKey;
            switch (e.keyCode) {
                case KeyCode.Space: AuditionSelection(); break;
                case KeyCode.T when !ctrl: Say(TimelineEdits.TrimToSelection(this, timeline)); break;
                case KeyCode.S when !ctrl: Say(TimelineEdits.Split(this, timeline)); break;
                case KeyCode.Delete: Say(TimelineEdits.Delete(this, timeline)); break;
                case KeyCode.C when ctrl: Say(TimelineEdits.Copy(timeline)); break;
                case KeyCode.X when ctrl: Say(TimelineEdits.Copy(timeline) + " " + TimelineEdits.Delete(this, timeline)); break;
                case KeyCode.V when ctrl: Say(TimelineEdits.Paste(this, timeline, PasteTime())); break;
                case KeyCode.Escape: timeline.ClearSelection(); break;
                default: return;
            }
            OnTimelineChanged();
            e.StopPropagation();
        }

    }
}
