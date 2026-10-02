// ChunkWindow — the authoring window for a chunk recipe.
//
// A recipe is an ordered STACK of capabilities, so the window is a stack of cards rather than a fixed set of
// panels: the surfaces on screen are the ones the recipe's own capabilities bring, and a recipe holding one
// thing shows no timing, no layer slots and no delay anywhere. That is the whole reason the old fixed-panel
// window could not survive the model change — its sections described a data shape the recipe no longer has.
//
// The layout copies Pyre's (dials left, workspace right, one section-toggle bar across the top) because the
// two tools are read the same way and a Laubrary tool that invents its own shape costs the user the ability
// to transfer what they already know.
using System;
using System.Collections.Generic;
using Laubrary.AssetKit.Editor;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Chunks.Editor
{
    public partial class ChunkWindow : ZuiAssetWindow<ChunkSpec>
    {
        [MenuItem("Laubrary/Chunks")]
        public static void Open() => GetWindow<ChunkWindow>("Chunks");

        /// Same entry-point shape as PyreWindow.OpenFor / MirageWindow.OpenFor — lets a LauAssetField's Edit
        /// button jump straight into this recipe's own editor.
        public static void OpenFor(ChunkSpec spec)
        {
            var w = GetWindow<ChunkWindow>("Chunks");
            if (spec != null) w.SetAsset(spec);
        }

        protected override string TypeLabel => "Chunk";
        protected override string NewAssetName => "Chunks";
        protected override string DefaultFolder => "Assets/Chunks";

        // ── window state ──────────────────────────────────────────────────────────────────────────────────
        // Serialized so a domain reload puts the workspace back exactly as it was; the transport's own clock
        // deliberately is NOT (playback stops at a reload, which is honest — nothing was running across it).

        const float DefaultLeftWidth = 460f;   // the widest packed card row, measured on the mock
        const float PreviewHeightMin = 140f;
        const float PreviewHeightMax = 640f;

        [SerializeField] float previewHeight = 260f;
        [SerializeField] Vector2 leftScroll;

        /// Where the recipe's clock is, in seconds. THE transport's time, shared by the scrub slider, the
        /// timing playhead and (from W2.4) the preview stage — one clock, so nothing can disagree about
        /// "now". A later preview partial reads this and draws the recipe's state at that instant.
        internal float previewTime;

        /// Whether the clock is advancing at wall-clock speed.
        internal bool playing;

        /// Whether reaching the end wraps back to the start instead of stopping there.
        internal bool loop = true;

        ScrollView leftPane;
        ZuiSection viewsSection;
        ZuiSection recipeSection;
        ZuiSection previewSection;
        ZuiSection timingSection;

        // Thumbnails for every LauAsset picker this window draws, shared so one asset is rendered once no
        // matter how many cards reference it.
        readonly Dictionary<UnityEngine.Object, Texture2D> thumbCache = new Dictionary<UnityEngine.Object, Texture2D>();

        // ── lifecycle ─────────────────────────────────────────────────────────────────────────────────────

        protected override void OnEnable()
        {
            base.OnEnable();
            lastTick = EditorApplication.timeSinceStartup;
            EditorApplication.update += Tick;
            rootVisualElement.RegisterCallback<PointerDownEvent>(OnAnyPointerDown, TrickleDown.TrickleDown);
            rootVisualElement.RegisterCallback<PointerUpEvent>(OnAnyPointerUp, TrickleDown.TrickleDown);
            // A domain reload always lands between pointer events, so a mid-drag never survives one to be
            // continued — belt-and-suspenders alongside [NonSerialized] on draggingBlastCap (T-0367).
            draggingBlastCap = null;
            dragUndoGroup = -1;
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            // Without this a closed window keeps ticking against destroyed elements — the "no console errors
            // after close" half of the transport contract.
            EditorApplication.update -= Tick;
            rootVisualElement.UnregisterCallback<PointerDownEvent>(OnAnyPointerDown, TrickleDown.TrickleDown);
            rootVisualElement.UnregisterCallback<PointerUpEvent>(OnAnyPointerUp, TrickleDown.TrickleDown);
            pointerHeld = false;
        }

        protected override void OnAssetChanged()
        {
            // A recipe's clock means nothing on another recipe: carrying the playhead across would show a
            // time the new recipe may not even have.
            previewTime = 0f;
            playing = false;
            leftScroll = Vector2.zero;
            shownReach = -1f;   // a new recipe is framed at once, not zoomed into from the last one
        }

        protected override void OnBeforeRebuild()
        {
            base.OnBeforeRebuild();
            // A rebuild is the one moment worth re-measuring, re-cutting and re-sampling the assets the cards
            // point at: a Pyre's frames are a real render and a cut is a full pass over a texture, far too
            // expensive per dial edit and far too stale to keep for the life of the editor. Dropped HERE,
            // before the cards are built, because a card's state line reads the same caches the stage does.
            ChunkPreviewSim.ClearCaches();
            // Every element reference below is about to be destroyed with the tree.
            cardStates.Clear();
            cards.Clear();
            cardBodies.Clear();
            cardChips.Clear();
            lanes = null;
            stage = null;
            playButton = null;
            scrubSlider = null;
            timeReadout = null;
            stackHost = null;
        }

        // ── layout ────────────────────────────────────────────────────────────────────────────────────────

        protected override void BuildAsset(VisualElement root, ChunkSpec c)
        {
            if (c == null) return;

            // ── left: the recipe ─────────────────────────────────────────────────
            leftPane = new ScrollView(ScrollViewMode.Vertical);
            leftPane.AddToClassList("lau-chunks__recipe-pane");
            leftPane.contentContainer.AddToClassList("lau-tool-shell__column");
            // The offset is written back on every scroll so it survives a rebuild — a card that grows or
            // shrinks under the cursor must not also throw the user back to the top of the stack.
            leftPane.verticalScroller.valueChanged += _ => leftScroll = leftPane.scrollOffset;

            var viewBar = BuildViewBar(root);
            viewsSection = Z.Section("Views", "Save and recall named presets of which cards are folded open.",
                                     "Chunks.views");
            viewsSection.Add(viewBar);
            leftPane.Add(viewsSection);

            BuildRecipeSection(leftPane, c);

            // ── right: the workspace ─────────────────────────────────────────────
            // A ScrollView, not a plain column: the stage's height is fixed by its own resize bar, so in a
            // short window the transport, the backdrop and the timing lanes together ask for more room than
            // is left. Left to a plain column they overlap each other — measured at 820x480, where the
            // timing lanes drew straight over the transport. Scrolling is the honest answer; shrinking the
            // stage instead would move the picture every time a capability was added.
            var rightPane = new ScrollView(ScrollViewMode.Vertical);
            rightPane.AddToClassList("lau-tool-shell__pane");
            rightPane.contentContainer.AddToClassList("lau-tool-shell__column");

            BuildPreviewSection(rightPane, c);
            BuildTimingSection(rightPane, c);

            // Z.Split, not a hand-rolled divider: it persists the width AND resets it on a double-click,
            // which is the route back a persisted width otherwise has no way to offer (a cold open is not a
            // first run). Pyre's own splitter predates that control and has no reset.
            var split = Z.Split("Chunks.dials", DefaultLeftWidth, leftPane, rightPane);

            root.AddToClassList("lau-tool-shell");

            // The toggle bar spans the whole window above everything. Tags was parented by the base class
            // before BuildAsset ran; re-adding it here moves it (VisualElement.Add detaches first) to just
            // under the bar, where it reads as one more section rather than chrome above it.
            root.Add(BuildSectionToggleBar());
            if (TagsSection != null) root.Add(TagsSection);
            root.Add(split);

            viewBar.RestoreLast();
            RestoreScroll(leftScroll);
            SyncTransport();
        }

        // ── saved views ───────────────────────────────────────────────────────────────────────────────────
        const string ViewStorePath = "Assets/Chunks/ChunkViews.asset";
        const string ViewPrefsKey = "Chunks.lastView";

        // A "view" is which cards are folded open — fold/gear/shown-control state on every ZuiBox under the
        // asset root, never an authored value. Cards are keyed by capability id, so a saved view follows a
        // capability through a reorder instead of following a position.
        ZuiViewBar BuildViewBar(VisualElement paneRoot)
        {
            Dictionary<string, bool> Capture()
            {
                var d = new Dictionary<string, bool>();
                foreach (var b in paneRoot.Query<ZuiBox>().ToList()) b.CaptureView(d);
                return d;
            }
            void Apply(IReadOnlyDictionary<string, bool> from)
            {
                foreach (var b in paneRoot.Query<ZuiBox>().ToList()) b.ApplyView(from);
            }
            return new ZuiViewBar(
                () => AssetDatabase.LoadAssetAtPath<ZuiViewStore>(ViewStorePath),
                CreateViewStore, Capture, Apply, ViewPrefsKey);
        }

        // Only ever called from the bar's own Save-as when no store exists yet.
        ZuiViewStore CreateViewStore()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Chunks"))
                AssetDatabase.CreateFolder("Assets", "Chunks");
            var store = ScriptableObject.CreateInstance<ZuiViewStore>();
            AssetDatabase.CreateAsset(store, ViewStorePath);
            Undo.RegisterCreatedObjectUndo(store, "Create Chunk Views");
            return store;
        }

        // Timing is listed only when the recipe HAS a timing surface. The bar draws a segment for a null
        // section too — inert, but still a button promising a section that does not exist — and it is last,
        // so dropping it leaves every other segment's saved on/off state on the same index it was.
        VisualElement BuildSectionToggleBar()
        {
            var entries = new List<(string, ZuiSection)>
            {
                ("Tags", TagsSection),
                ("Views", viewsSection),
                ("Recipe", recipeSection),
                ("Preview", previewSection),
            };
            if (timingSection != null) entries.Add(("Timing", timingSection));
            return new ZuiSectionToggleBar("Chunks", entries.ToArray());
        }

        // ── edits ─────────────────────────────────────────────────────────────────────────────────────────

        /// The ONE way a card writes to the recipe: undo recorded before the mutation, the asset marked
        /// dirty after it, and the preview told that what it drew is now stale. Nothing else in this window
        /// touches recipe data.
        internal void Dial(string undoLabel, Action apply)
        {
            var c = Current;
            if (c == null || apply == null) return;
            Undo.RecordObject(c, undoLabel);
            apply();
            EditorUtility.SetDirty(c);
            InvalidatePreview();
            SyncTiming();
        }

        /// The Undo half of a Z.Envelope wiring: an envelope mutates its caller-owned point list IN PLACE
        /// (there is no "new value" to hand to Dial's own apply callback), so undo must be recorded BEFORE
        /// that mutation happens rather than wrapped around it. Pass this as onBeforeMutate and
        /// <see cref="EnvelopeChanged"/> as onChanged — together they do exactly what Dial does for every
        /// other control, just split across the two hooks Z.Envelope actually offers.
        internal void EnvelopeUndo(string undoLabel)
        {
            var c = Current;
            if (c == null) return;
            Undo.RecordObject(c, undoLabel);
        }

        /// The after half of a Z.Envelope wiring — see <see cref="EnvelopeUndo"/>.
        internal void EnvelopeChanged()
        {
            var c = Current;
            if (c == null) return;
            EditorUtility.SetDirty(c);
            InvalidatePreview();
            SyncTiming();
        }

        /// Dial an edit that changes WHICH controls the card shows (a mode switch, a pattern change), so the
        /// card is rebuilt around the new answer. Only that card — the rest of the stack, the scroll position
        /// and the playhead all stay exactly where they were.
        internal void DialAndRebuildCard(string capabilityId, string undoLabel, Action apply)
        {
            Dial(undoLabel, apply);
            RebuildCard(capabilityId);
        }

        /// What the preview drew is out of date. The stage repaints from live data, so this is a repaint
        /// request rather than a cache drop — and it never stops or resets the clock, because dialling while
        /// something is playing has to change the next frame, not restart the run.
        internal void InvalidatePreview()
        {
            // The picture itself is recomputed from live data every repaint, so it needs nothing dropped. The
            // counter is for the one thing that CANNOT be redone per frame — how far the recipe reaches, which
            // sets the stage's zoom — and which must not go stale after an edit either.
            previewGeneration++;
            ChunkPreviewSim.ForgetResolutions();
            RefreshCardStates();
            stage?.MarkDirtyRepaint();
        }

        void RestoreScroll(Vector2 offset)
        {
            if (leftPane == null || offset == Vector2.zero) return;
            // One frame late on purpose: the pane has no scrollable range until it has been laid out, so an
            // offset written now would be clamped to zero.
            leftPane.schedule.Execute(() => { if (leftPane != null) leftPane.scrollOffset = offset; }).ExecuteLater(0);
        }
    }
}
