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
    /// The UI Toolkit twin of the Zequence editor window (T-0469): the header fields row, then in the content box the
    /// toolbar (No-Play for a randomizer, Mode, Duration, remove, Render to Klip, Auto-duration, Force GC, Play), one card
    /// per entry (plain entries, and local Zequences as groups holding their own entries), the MASTER section and the add
    /// buttons. Every edit goes through the old window's own paths (<see cref="CompositeZoundEditing"/> and the project's
    /// modify calls), and every card is laid out with the old window's rect maths, so the two windows agree.
    ///
    /// The tree is rebuilt only when the Zequence's structure changes; a 5 Hz tick refreshes values, and a 30 Hz tick moves
    /// playheads and entry flashes while anything plays.
    /// </summary>
    public class ZequenceEditorWindowTK : ZuiWindow, IHasCustomMenu {

        [SerializeField] int targetZoundID;
        [SerializeField] bool isLocalZound;
        [SerializeField] bool autoDuration;

        internal const float EntryHeight = 118f + 18f;
        internal const float LeftSectionWidth = 190f;
        internal const float GroupHeaderHeight = 68f;
        internal const float GroupEntryLeftOffset = 10f;

        internal Zequence zeq;
        internal ZoundToken currentToken;
        internal Dictionary<CompositeZound.ZoundEntry, ZoundToken> entryTokens;
        internal string addMenuSearchText, createKlipSearchText;
        internal readonly List<Action> refreshers = new List<Action>();
        internal readonly List<Action> liveRefreshers = new List<Action>();
        string builtSig;
        ZoundFieldsRowTK fields;
        Button playButton;
        ZuiToggleButton retriggerButton;

        // -- the shared timeline (non-destructive editing, T-0558) --
        /// <summary>One time window, selection and set of switches for every track (view state: not saved with the sound).</summary>
        internal ZequenceTimeline timeline;
        TimelineHeaderTK header;
        internal readonly List<TrackStripTK> strips = new List<TrackStripTK>();
        [SerializeField] float viewT0, viewT1 = 1f;
        [SerializeField] bool viewFitted = true, viewFollow, viewRipple, viewLoop;
        [NonSerialized] ZoundToken hereToken;
        [NonSerialized] float hereOffset;
        [NonSerialized] readonly Dictionary<CompositeZound.ZoundEntry, List<ZoundToken>> auditionTokens = new Dictionary<CompositeZound.ZoundEntry, List<ZoundToken>>();
        [NonSerialized] ZoundToken auditionWhole;
        [NonSerialized] float auditionFrom;

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
                               + "\n\nRight-click: Play on change, Burst, Loop.";
        }

        internal void Modify(string undo, Action a) { ZoundsWindow.ModifyZoundsProject(undo, a); Tick(); }

        // ─────────────────────────── build ───────────────────────────

        protected override void BuildUI(VisualElement root) {
            // Straight after a script reload this window rebuilds before Unity's editor styles exist, and the shared
            // measurements below read them (see ZS.EditorStylesReady); build a moment later instead of half-building.
            if (!ZS.EditorStylesReady) { root.schedule.Execute(Rebuild).StartingIn(100); return; }
            ZS.Attach(root);
            root.AddToClassList("zs-zequence");
            // The old window draws its fields row from the very top of the window (no leading row space, unlike the Klip
            // window), so none of the Klip window's 3 px origin offset applies here (measured: every row 3.1 px low with it).
            root.AddToClassList("zs-zequence-editor__root");
            refreshers.Clear(); liveRefreshers.Clear();
            zeq = ZoundsProject.isJSONLoaded ? FindZequence(targetZoundID) : null;
            if (zeq == null) { root.Add(new Label(ZoundsProject.isJSONLoaded ? "Zequence no longer exists in the project." : "Zounds Project is not loaded.")); return; }
            titleContent = new GUIContent("Zequence: " + zeq.name);
            EnsureAudition();
            EnsureEnvelopes();
            EnsureTimeline();
            strips.Clear();

            fields = new ZoundFieldsRowTK(zeq, isLocalZound, () => titleContent = new GUIContent("Zequence: " + zeq.name));
            root.Add(fields);
            root.Add(Space(4f));

            var box = new VisualElement();
            box.AddToClassList("zs-box-default");
            box.AddToClassList("zs-zequence-editor__box");
            root.Add(box);
            box.Add(Toolbar());
            box.Add(Space(5f));
            header = new TimelineHeaderTK(this);
            box.Add(header);
            box.Add(Space(3f));
            if (zeq.zoundEntries.Count == 0) {
                var none = new Label("No zound entry.");
                none.AddToClassList("zs-lbl"); none.AddToClassList("zs-greymini");
                none.style.height = EditorGUIUtility.singleLineHeight;
                box.Add(none);
            }

            var scroll = new ScrollView(ScrollViewMode.Vertical);
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
            sb.Append(timeline != null && timeline.focus != null ? timeline.focus.GetHashCode() : 0);
            return sb.ToString();
        }

        static void Append(StringBuilder sb, CompositeZound c) {
            sb.Append((int)c.mode).Append('[');
            foreach (var e in c.zoundEntries) {
                bool found = c.TryGetEntryZound(e, out var z);
                sb.Append(e.zoundId).Append(e.local ? 'L' : 'S').Append(found ? 'f' : 'x').Append(e.volumeEnvelope != null && e.volumeEnvelope.enabled ? 'E' : 'e')
                  .Append(e.editor_foldoutExpanded ? 'X' : 'c').Append(e.editor_isRenaming ? 'R' : 'r');
                if (z is Klip k) sb.Append(k.originalId != 0 ? 'o' : '-');
                if (found && e.local && z is CompositeZound cc) Append(sb, cc);
                sb.Append(';');
            }
            sb.Append(']');
        }

        void Tick() {
            if (zeq == null) return;
            if (FindZequence(targetZoundID) != zeq || Signature() != builtSig) { EnsureEnvelopes(); Rebuild(); return; }
            fields?.Sync();
            SyncPlayButton();
            SyncRetriggerButton();
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

        VisualElement Toolbar() {
            var r = new VisualElement();
            r.AddToClassList("zs-zeq-toolbar");
            r.AddToClassList("zs-zequence-editor__toolbar-row");
            if (zeq.mode == CompositeZound.Mode.Randomizer) {
                var noPlay = new IntegerField("No-Play") { value = zeq.noPlayWeight, tooltip = "Chance weight for this randomizer to not play any sound." };
                noPlay.AddToClassList("zs-imgui-field"); noPlay.AddToClassList("zs-label-50");
                noPlay.AddToClassList("zs-zequence-editor__toolbar-no-play");
                noPlay.RegisterValueChangedCallback(e => Modify("change no play weight", () => zeq.noPlayWeight = e.newValue));
                refreshers.Add(() => { if (noPlay.focusController?.focusedElement != noPlay) noPlay.SetValueWithoutNotify(zeq.noPlayWeight); });
                r.Add(noPlay);
            }
            r.Add(Flex());
            var mode = new EnumField("Mode", zeq.mode);
            mode.AddToClassList("zs-imgui-field"); mode.AddToClassList("zs-label-38");
            mode.AddToClassList("zs-zequence-editor__toolbar-mode");
            mode.RegisterValueChangedCallback(e => Modify("change zequence mode", () => zeq.mode = (CompositeZound.Mode)e.newValue));
            r.Add(mode);
            var duration = new FloatField("Duration") { tooltip = "This is only used to determine editor width, and doesn't affect runtime behaviour." };
            duration.AddToClassList("zs-imgui-field"); duration.AddToClassList("zs-label-55");
            duration.AddToClassList("zs-zequence-editor__toolbar-duration");
            duration.SetValueWithoutNotify(zeq.editor_maxDuration / zeq.minPitch);
            duration.RegisterValueChangedCallback(e => Modify("change max duration", () => {
                zeq.editor_maxDuration = e.newValue * zeq.minPitch;
                CompositeZoundEditing.RecalculateMaxDuration(zeq, autoDuration);
            }));
            refreshers.Add(() => { if (duration.focusController?.focusedElement != duration) duration.SetValueWithoutNotify(zeq.editor_maxDuration / zeq.minPitch); });
            r.Add(duration);
            // Space(5) plus the IMGUI layout margin after a standard field (measured 4 px on the old window).
            r.Add(Gap(5f + 4f));
            r.Add(IconButton("remove", "Remove this zound.", "RichButton", ZUICornerMask.All, 30f, EditorGUIUtility.singleLineHeight, RemoveZound));
            r.Add(Gap(5f));
            Button render = null;
            render = ZS.Button("Render to Klip", "", "Default", () => RenderZequenceToKlipPopup.Show(render.worldBound.position,
                                    zeq, CompositeZoundEditing.CalculateCompositeDuration(zeq, 1f), this), ZUICornerMask.Left, 100f, -1f);
            render.AddToClassList("zs-layoutbutton");
            render.SetEnabled(!Application.isPlaying);
            r.Add(render);
            // Space(4) plus the IMGUI layout margin before ToggleLeft's box (measured 6 px on the old window).
            r.Add(Gap(4f + 6f));
            var auto = new Toggle { text = "Auto-duration", value = autoDuration, tooltip = "Automatically set Duration to the length of the longest nested klip on every change." };
            auto.AddToClassList("zs-toggleleft");
            auto.AddToClassList("zs-zequence-editor__toolbar-auto");
            auto.RegisterValueChangedCallback(e => { autoDuration = e.newValue; if (autoDuration) CompositeZoundEditing.AutoApplyDuration(zeq); Tick(); });
            r.Add(auto);
            r.Add(Gap(5f));
            var gc = ZS.Button("Force GC", EditorTools.ZoundGcStressTest.Tooltip + "\n\n" + EditorTools.ZoundGcStressTest.lastResult, "Default",
                               () => EditorTools.ZoundGcStressTest.Run(IsPlaying()), ZUICornerMask.All, 72f, -1f);
            gc.AddToClassList("zs-layoutbutton");
            r.Add(gc);
            r.Add(Gap(5f));
            retriggerButton = ZS.Toggle("Retrigger", "When enabled, every trigger starts this Zequence several times. Right-click to set plays, gap and timing.", zeq.retriggerEnabled,
                value => { Modify("toggle zequence retrigger", () => { if (value) zeq.EnableRetrigger(); else zeq.retriggerEnabled = false; }); SyncRetriggerButton(); },
                "RichToggle", ZUICornerMask.All, 88f, EditorGUIUtility.singleLineHeight);
            retriggerButton.AddToClassList("zs-layoutbutton");
            retriggerButton.RegisterCallback<PointerDownEvent>(e => {
                if (e.button != 1) return;
                e.StopPropagation();
                RetriggerPopupTK.Show(retriggerButton, zeq, Tick);
            });
            r.Add(retriggerButton);
            r.Add(Gap(5f));
            playButton = ZS.Button("Play", "", "Default", () => {
                EnsureAudition();
                if (audition.BurstRunning || audition.LoopRunning) audition.StopRun();
                else audition.PlayOnce();
                Tick();
            }, ZUICornerMask.Right, 60f, -1f);
            playButton.AddToClassList("zs-layoutbutton");
            // Right-click: the audition card (T-0486).
            var pb = playButton;
            pb.RegisterCallback<PointerDownEvent>(e => {
                if (e.button != 1) return;
                e.StopPropagation();
                EnsureAudition();
                AuditionPopupTK.Show(pb, audition);
            });
            r.Add(playButton);
            SyncPlayButton();
            SyncRetriggerButton();
            return r;
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

        // ─────────────────────────── master section (the Zequence window's OnEndOfScrollView) ───────────────────────────

        VisualElement MasterSection() {
            float lh = EditorGUIUtility.singleLineHeight;
            bool enabled = zeq.masterVolumeEnvelope.enabled;
            var rect = new VisualElement();
            rect.style.height = enabled ? EntryHeight : lh * 2f + 10f; rect.AddToClassList("zs-zequence-editor__master-section-rect");
            var label = new Label("MASTER");
            label.AddToClassList("zs-lbl"); label.AddToClassList("zs-text-subheader"); label.AddToClassList("zs-subheader");
            Place(label, 4f, 4f, LeftSectionWidth, lh);
            label.AddToClassList("zs-zequence-editor__master-section-label");
            rect.Add(label);
            var use = ZS.Toggle("Use Volume Envelope", "", enabled, v => Modify("toggle master volume envelope", () => zeq.masterVolumeEnvelope.enabled = v),
                                "Default", ZUICornerMask.None, LeftSectionWidth, lh);
            Place(use, 4f, 4f + lh, LeftSectionWidth, lh);
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
                    var content = new Rect(4f, 4f, w - 8f, rect.layout.height - 8f);
                    var right = new Rect(content.x + LeftSectionWidth + 5f, content.y, content.width - LeftSectionWidth - 5f, content.height);
                    float fieldBox = EditorGUIUtility.fieldWidth;
                    var timeline = new Rect(right.x + 5f, right.y, right.width - fieldBox - 15f, right.height - 20f);
                    float globalMax = zeq.editor_maxDuration / zeq.minPitch;
                    float dur = CompositeZoundEditing.CalculateCompositeDuration(zeq, zeq.minPitch);
                    var bgRect = new Rect(timeline.x, content.y + lh, dur / globalMax * timeline.width, lh * 4f);
                    Place(bg, bgRect.x, bgRect.y, bgRect.width, bgRect.height);
                    Place(curve, bgRect.x, bgRect.y, bgRect.width, bgRect.height);
                    bool playing = currentToken != null && currentToken.state != ZoundToken.State.Killed;
                    head.style.display = playing ? DisplayStyle.Flex : DisplayStyle.None;
                    if (playing) {
                        float actual = currentToken.duration;
                        float adjusted = timeline.width / globalMax * actual;
                        Place(head, timeline.x - 1f + currentToken.time / actual * adjusted, timeline.y, 1f, timeline.height);
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
            localKlip = ZS.Button("+ Local Klip", "", "RichButton", () => AddLocalKlip(parent, localKlip), ZUICornerMask.Left, 85f, -1f);
            localKlip.AddToClassList("zs-layoutbutton");
            r.Add(localKlip);
            var localZeq = ZS.Button("+ Local Zequence", "", "RichButton", () => {
                var newZequence = new Zequence(ZoundLibrary.GetUniqueZoundId());
                newZequence.name = ZoundDictionary.EnsureUniqueZoundName("Zequence");
                newZequence.parentId = parent.id;
                parent.localZequences.Add(new CompositeZound.LocalZequence(newZequence));
                CompositeZoundEditing.AddNewZoundEntry(zeq, parent, newZequence, true, autoDuration);
                Tick();
            }, ZUICornerMask.None, 125f, -1f);
            localZeq.AddToClassList("zs-layoutbutton");
            r.Add(localZeq);
            sharedZound = ZS.Button("+ Shared Zound", "", "RichButton", () => AddShared(parent, sharedZound), ZUICornerMask.Right, 105f, -1f);
            sharedZound.AddToClassList("zs-layoutbutton");
            r.Add(sharedZound);
            return r;
        }

        internal void AddLocalKlip(CompositeZound parent, VisualElement from) {
            BrowserTab.OpenCreateNewKlipDialog(from.worldBound.position, klip => {
                klip.parentId = parent.id;
                parent.localKlips.Add(klip);
                CompositeZoundEditing.AddNewZoundEntry(zeq, parent, klip, true, autoDuration);
                Tick();
            }, createKlipSearchText, text => createKlipSearchText = text, previewOwner: this);
        }

        internal void AddShared(CompositeZound parent, VisualElement from) {
            CompositeZoundEditing.AddNewEntryFromExisting(parent, from.worldBound.position, addMenuSearchText, s => addMenuSearchText = s,
                zound => { CompositeZoundEditing.AddNewZoundEntry(zeq, parent, zound, false, autoDuration); Tick(); }, this);
        }

        internal bool AutoDuration => autoDuration;

        // ─────────────────────────── the shared timeline ───────────────────────────

        void EnsureTimeline() {
            if (timeline == null) {
                timeline = new ZequenceTimeline { t0 = viewT0, t1 = Mathf.Max(viewT0 + 0.01f, viewT1), fitted = viewFitted, follow = viewFollow, ripple = viewRipple, loop = viewLoop };
                timeline.changed += () => { foreach (var s in strips) s.MarkDirtyRepaint(); header?.Repaint(); header?.Sync(); };
            }
            if (!ReferenceEquals(timeline.zeq, zeq)) { timeline.zeq = zeq; timeline.ClearSelection(); timeline.focus = null; timeline.editingCurve.Clear(); }
            timeline.Rebuild();
        }

        void SaveView() {
            if (timeline == null) return;
            viewT0 = timeline.t0; viewT1 = timeline.t1; viewFitted = timeline.fitted;
            viewFollow = timeline.follow; viewRipple = timeline.ripple; viewLoop = timeline.loop;
        }

        /// <summary>The lane every track and the ruler draw in: the first top-level card's timeline area (the old window's
        /// rect maths), so all of them line up whatever the scroll bar does.</summary>
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

        /// <summary>Applies a structural view change now (a focused track is taller, so the cards are rebuilt).</summary>
        internal void RefreshNow() => Tick();

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
