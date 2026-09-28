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
    public class ZequenceEditorWindowTK : ZuiWindow {

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

        public static ZequenceEditorWindowTK Open(Zequence zequence, bool isLocalZound) {
            var w = CreateInstance<ZequenceEditorWindowTK>();
            w.targetZoundID = zequence.id;
            w.isLocalZound = isLocalZound;
            w.titleContent = new GUIContent("Zequence: " + zequence.name + " (UITK)");
            w.minSize = new Vector2(350f, 200f);
            w.Show();
            return w;
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

        internal bool IsPlaying() => currentToken != null && currentToken.state == ZoundToken.State.Playing;

        internal void Modify(string undo, Action a) { ZoundsWindow.ModifyZoundsProject(undo, a); Tick(); }

        // ─────────────────────────── build ───────────────────────────

        protected override void BuildUI(VisualElement root) {
            ZS.Attach(root);
            root.AddToClassList("zs-zequence");
            // The old window draws its fields row from the very top of the window (no leading row space, unlike the Klip
            // window), so none of the Klip window's 3 px origin offset applies here (measured: every row 3.1 px low with it).
            root.style.paddingTop = 0;
            refreshers.Clear(); liveRefreshers.Clear();
            zeq = ZoundsProject.isJSONLoaded ? FindZequence(targetZoundID) : null;
            if (zeq == null) { root.Add(new Label(ZoundsProject.isJSONLoaded ? "Zequence no longer exists in the project." : "Zounds Project is not loaded.")); return; }
            titleContent = new GUIContent("Zequence: " + zeq.name + " (UITK)");
            EnsureEnvelopes();

            fields = new ZoundFieldsRowTK(zeq, isLocalZound, () => titleContent = new GUIContent("Zequence: " + zeq.name + " (UITK)"));
            root.Add(fields);
            root.Add(Space(4f));

            var box = new VisualElement();
            box.AddToClassList("zs-box-default");
            box.style.flexGrow = 1;
            root.Add(box);
            box.Add(Toolbar());
            box.Add(Space(5f));
            if (zeq.zoundEntries.Count == 0) {
                var none = new Label("No zound entry.");
                none.AddToClassList("zs-lbl"); none.AddToClassList("zs-greymini");
                none.style.height = EditorGUIUtility.singleLineHeight;
                box.Add(none);
            }

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 0; scroll.style.flexShrink = 1;
            box.Add(scroll);
            bool darker = false;
            for (int i = 0; i < zeq.zoundEntries.Count; i++) {
                scroll.Add(new ZequenceEntryTK(this, zeq, zeq.zoundEntries[i], i, zeq.minPitch, 0f, darker, false));
                scroll.Add(Space(4f));
                darker = !darker;
            }
            scroll.Add(MasterSection());
            box.Add(AddRow(zeq, true));

            builtSig = Signature();
            root.schedule.Execute(Tick).Every(200);
            root.schedule.Execute(LiveTick).Every(33);
        }

        internal static VisualElement Space(float h) { var e = new VisualElement(); e.style.height = h; e.style.flexShrink = 0; return e; }
        internal static VisualElement Gap(float w) { var e = new VisualElement(); e.style.width = w; e.style.flexShrink = 0; return e; }
        internal static VisualElement Flex() { var e = new VisualElement(); e.style.flexGrow = 1; return e; }

        /// <summary>Everything that decides the tree's shape; when it changes, the window is rebuilt.</summary>
        string Signature() {
            if (zeq == null) return "";
            var sb = new StringBuilder();
            Append(sb, zeq);
            sb.Append(zeq.masterVolumeEnvelope != null && zeq.masterVolumeEnvelope.enabled ? 'M' : 'm');
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
            if (playButton != null) {
                bool p = IsPlaying();
                playButton.text = p ? "Stop" : "Play";
            }
            foreach (var r in refreshers) r();
        }

        void LiveTick() {
            if (zeq == null) return;
            foreach (var r in liveRefreshers) r();
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
            r.style.flexDirection = FlexDirection.Row; r.style.flexShrink = 0; r.style.alignItems = Align.FlexStart;
            if (zeq.mode == CompositeZound.Mode.Randomizer) {
                var noPlay = new IntegerField("No-Play") { value = zeq.noPlayWeight, tooltip = "Chance weight for this randomizer to not play any sound." };
                noPlay.AddToClassList("zs-imgui-field"); noPlay.AddToClassList("zs-label-50");
                noPlay.style.width = 120f;
                noPlay.RegisterValueChangedCallback(e => Modify("change no play weight", () => zeq.noPlayWeight = e.newValue));
                refreshers.Add(() => { if (noPlay.focusController?.focusedElement != noPlay) noPlay.SetValueWithoutNotify(zeq.noPlayWeight); });
                r.Add(noPlay);
            }
            r.Add(Flex());
            var mode = new EnumField("Mode", zeq.mode);
            mode.AddToClassList("zs-imgui-field"); mode.AddToClassList("zs-label-38");
            mode.style.width = 140f;
            mode.RegisterValueChangedCallback(e => Modify("change zequence mode", () => zeq.mode = (CompositeZound.Mode)e.newValue));
            r.Add(mode);
            var duration = new FloatField("Duration") { tooltip = "This is only used to determine editor width, and doesn't affect runtime behaviour." };
            duration.AddToClassList("zs-imgui-field"); duration.AddToClassList("zs-label-55");
            duration.style.width = 130f;
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
                                    zeq, CompositeZoundEditing.CalculateCompositeDuration(zeq, 1f)), ZUICornerMask.Left, 100f, -1f);
            render.AddToClassList("zs-layoutbutton");
            render.SetEnabled(!Application.isPlaying);
            r.Add(render);
            // Space(4) plus the IMGUI layout margin before ToggleLeft's box (measured 6 px on the old window).
            r.Add(Gap(4f + 6f));
            var auto = new Toggle { text = "Auto-duration", value = autoDuration, tooltip = "Automatically set Duration to the length of the longest nested klip on every change." };
            auto.AddToClassList("zs-toggleleft");
            auto.style.width = 105f;
            auto.RegisterValueChangedCallback(e => { autoDuration = e.newValue; if (autoDuration) CompositeZoundEditing.AutoApplyDuration(zeq); Tick(); });
            r.Add(auto);
            r.Add(Gap(5f));
            var gc = ZS.Button("Force GC", EditorTools.ZoundGcStressTest.Tooltip + "\n\n" + EditorTools.ZoundGcStressTest.lastResult, "Default",
                               () => EditorTools.ZoundGcStressTest.Run(IsPlaying()), ZUICornerMask.All, 72f, -1f);
            gc.AddToClassList("zs-layoutbutton");
            r.Add(gc);
            r.Add(Gap(5f));
            playButton = ZS.Button("Play", "", "Default", () => {
                if (!IsPlaying()) currentToken = CompositeZoundEditing.SimulatePlay(zeq, isLocalZound);
                else currentToken.Kill();
                Tick();
            }, ZUICornerMask.Right, 60f, -1f);
            playButton.AddToClassList("zs-layoutbutton");
            r.Add(playButton);
            return r;
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
            rect.style.height = enabled ? EntryHeight : lh * 2f + 10f; rect.style.flexShrink = 0;
            var label = new Label("MASTER");
            label.AddToClassList("zs-lbl"); label.AddToClassList("zs-text-subheader"); label.AddToClassList("zs-subheader");
            Place(label, 4f, 4f, LeftSectionWidth, lh);
            label.style.unityTextAlign = TextAnchor.UpperLeft;
            rect.Add(label);
            var use = ZS.Toggle("Use Volume Envelope", "", enabled, v => Modify("toggle master volume envelope", () => zeq.masterVolumeEnvelope.enabled = v),
                                "Default", ZUICornerMask.None, LeftSectionWidth, lh);
            Place(use, 4f, 4f + lh, LeftSectionWidth, lh);
            rect.Add(use);
            if (enabled) {
                // The master curve over the Zequence's own length, edited on a copy written back through the modify path.
                var bg = new VisualElement { pickingMode = PickingMode.Ignore };
                bg.style.position = Position.Absolute; bg.style.backgroundColor = new Color(0.75f, 0.75f, 0.75f, 0.1f);
                rect.Add(bg);
                var copy = zeq.masterVolumeEnvelope.DeepCopy();
                var es = ZoundsProject.Instance.projectSettings.editorStyle;
                var curve = new EnvelopeTK(copy, es.volumeEnvelopeColor) { thickness = es.volumeEnvelopeThickness };
                curve.style.position = Position.Absolute;
                curve.onChanged = () => Modify("modify master volume envelope", () => { zeq.masterVolumeEnvelope = copy.DeepCopy(); zeq.masterVolumeEnvelope.enabled = true; });
                rect.Add(curve);
                var head = new VisualElement { pickingMode = PickingMode.Ignore };
                head.style.position = Position.Absolute; head.style.width = 1f;
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
            e.style.position = Position.Absolute;
            e.style.left = x; e.style.top = y;
            if (w >= 0f) e.style.width = Mathf.Max(0f, w);
            if (h >= 0f) e.style.height = Mathf.Max(0f, h);
        }

        // ─────────────────────────── add buttons ───────────────────────────

        /// <summary>The "+ Local Klip / + Local Zequence / + Shared Zound" row under the entries (the top level's).</summary>
        VisualElement AddRow(CompositeZound parent, bool topLevel) {
            var r = new VisualElement();
            r.style.flexDirection = FlexDirection.Row; r.style.flexShrink = 0;
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
            }, createKlipSearchText, text => createKlipSearchText = text);
        }

        internal void AddShared(CompositeZound parent, VisualElement from) {
            CompositeZoundEditing.AddNewEntryFromExisting(parent, from.worldBound.position, addMenuSearchText, s => addMenuSearchText = s,
                zound => { CompositeZoundEditing.AddNewZoundEntry(zeq, parent, zound, false, autoDuration); Tick(); });
        }

        internal bool AutoDuration => autoDuration;
    }
}
