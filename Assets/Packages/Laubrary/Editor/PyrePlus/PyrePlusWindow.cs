// PyrePlusWindow — the authoring window for the PyrePlus prototype (see PYREPLUS_DESIGN.md).
//
// Built directly on the UI Toolkit toolkit (ZuiAssetWindow + Z.* controls + ZuiSection folding), NOT on
// Zolumn — the UITK migration already gives collapsible bordered sections and grow-to-fill layout, so the
// design's Zolumn dependency is dropped. SLICE 1: the Shape section + a live preview. Swarm and Modifiers
// sections follow. Deliberately a separate menu item and asset type from real Pyre, which is untouched.
using System.Collections.Generic;
using Laubrary.AssetKit.Editor;
using Laubrary.BackSplash.Editor;
using Laubrary.Zui;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.PyrePlus.Editor
{
    // Split across partials: PyrePlusWindow.cs = shell + left-pane dials; PyrePlusWindow.Preview.cs = the
    // IMGUI preview island + the Swarm authoring overlay (shape outline, spawn dots, drag handles).
    public partial class PyrePlusWindow : ZuiAssetWindow<PyrePlusSpec>
    {
        [MenuItem("Laubrary/Pyre Plus")]
        public static void Open() => GetWindow<PyrePlusWindow>("Pyre Plus");

        PyrePlusSpec spec => Current;
        protected override string TypeLabel => "Pyre Plus";
        protected override string NewAssetName => "New Pyre Plus";
        protected override string DefaultFolder => "Assets/PyrePlus";

        protected override Texture2D RenderThumbnail(PyrePlusSpec item)
        {
            int mid = Mathf.Clamp(item.frameCount / 2, 0, Mathf.Max(0, item.frameCount - 1));
            return PyrePlusRenderer.RenderFrameTexture(item, mid);
        }
        protected override bool AnimateThumbnails => true;
        protected override void UpdateAnimatedThumbnail(PyrePlusSpec item, Texture2D tex, double time)
        {
            if (item == null || item.frameCount <= 1) return;
            float fps = Mathf.Max(1f, item.previewFps);
            int f = Mathf.FloorToInt((float)(time * fps)) % item.frameCount;
            tex.SetPixels32(PyrePlusRenderer.RenderFrame(item, f));
            tex.Apply();
        }

        // preview state
        IMGUIContainer preview;
        Texture2D previewTex;
        double lastTime;
        float acc;
        bool playing = true;
        int frame;
        Button playButton;
        SliderInt scrubSlider;   // frame scrubber (transport parity with Pyre1); drives `frame`, follows playback
        Label frameReadout;      // "frame N/M" readout beside Zoom/Speed, kept in sync with `frame`
        VisualElement backdropHost;
        // The preview backdrop is an editor-only BackSplash (camera colour + one image), held on the WINDOW — not
        // the runtime spec, since PyrePlus's runtime asmdef doesn't reference BackSplash. Lazily created; a cosmetic
        // authoring aid, never baked. Mirrors how PyreWindow holds its BackSplash, but window-scoped rather than
        // per-asset.
        Laubrary.BackSplash.BackSplashSettings _backSplash;
        Laubrary.BackSplash.BackSplashSettings backSplash => _backSplash ??= new Laubrary.BackSplash.BackSplashSettings();

        // ── layer selection (window state, never serialized on the spec) ─────────────
        // Which layer the Shape / Swarm / Matte / Modifiers sections below edit. Defaults to the LAST layer on
        // asset load; a click in the layer list re-points it. SelLayer clamps so a stale index is always safe.
        int layerSel;
        PyrePlusLayer SelLayer
        {
            get
            {
                if (spec == null || spec.layers == null || spec.layers.Count == 0) return null;
                layerSel = Mathf.Clamp(layerSel, 0, spec.layers.Count - 1);
                return spec.layers[layerSel];
            }
        }
        VisualElement layerListHost;   // refilled by RebuildLayerList on any list change
        VisualElement matteBody;       // refilled by RebuildMatte on selection / role change

        protected override void OnEnable()
        {
            base.OnEnable();
            EditorApplication.update += Tick;
        }
        protected override void OnDisable()
        {
            base.OnDisable();
            EditorApplication.update -= Tick;
            if (previewTex != null) { DestroyImmediate(previewTex); previewTex = null; }
            DestroyStripCache();
        }
        protected override void OnAssetChanged()
        {
            frame = 0; previewDirty = true; DestroyStripCache();
            layerSel = int.MaxValue;   // a NEW asset defaults to its last layer (BuildAsset clamps)
        }

        void Tick()
        {
            if (!playing || spec == null) return;
            double now = EditorApplication.timeSinceStartup;
            float dt = Mathf.Clamp((float)(now - lastTime), 0f, 0.1f);
            lastTime = now;
            acc += dt * Mathf.Max(1f, spec.previewFps);
            bool advanced = false;
            while (acc >= 1f) { acc -= 1f; frame = (frame + 1) % Mathf.Max(1, spec.frameCount); advanced = true; }
            // In Strip mode the frames themselves don't change as playback advances — only the highlighted tile
            // moves — so DON'T set previewDirty (that would needlessly re-render every tile); just repaint. In
            // single-frame mode previewDirty forces the new frame to render.
            if (advanced) { if (!spec.previewStrip) previewDirty = true; preview?.MarkDirtyRepaint(); RefreshTransportReadout(); }
        }

        bool previewDirty = true;
        int lastRenderedFrame = -1;

        // Any authored edit routes through here (Dirty → MarkDirty). Refreshing the transport readout alongside
        // keeps the scrubber's range + value and the "frame N/M" label in sync when e.g. the frame count changes.
        void MarkDirty() { previewDirty = true; preview?.MarkDirtyRepaint(); RefreshTransportReadout(); }

        protected override void BuildAsset(VisualElement root, PyrePlusSpec s)
        {
            var split = new VisualElement();
            split.style.flexDirection = FlexDirection.Row;
            split.style.flexGrow = 1f;
            split.style.minHeight = 0f;

            // ── left: dials ──────────────────────────────────────────────────────
            var left = new ScrollView(ScrollViewMode.Vertical);
            left.style.width = 360f;
            left.style.flexShrink = 0f;
            var dials = left.contentContainer;

            // Saved-views bar rides at the very top of the dials pane. Its capture/apply aggregate every
            // ZuiBox under this asset root (Canvas / Solid / Transform / Modifiers): a "view" is their fold,
            // gear-open and shown-control state only, never an authored value. The query root is `root` (the
            // BuildAsset host), not rootVisualElement — `split` is added to `root` below BEFORE RestoreLast
            // runs, but the window has not yet parented `root` to its own rootVisualElement at this point.
            var viewBar = BuildViewBar(root);
            dials.Add(viewBar);

            // Selection is STICKY across rebuilds — only clamped to a valid index. (Defaulting to the
            // last layer on every rebuild silently jumped the overlay/sections to another layer after
            // any undo or structural edit; a fresh ASSET picks its last layer via OnAssetChanged.)
            if (s.layers != null) layerSel = Mathf.Clamp(layerSel, 0, Mathf.Max(0, s.layers.Count - 1));

            BuildLayerList(dials);      // the compact layer stack — under the views bar, above Canvas
            BuildCanvas(dials, s);
            BuildShape(dials, s);
            BuildSwarm(dials, s);
            BuildMatte(dials);          // the selected layer's matte role / channel / clip
            BuildModifiers(dials, s);   // PyrePlusWindow.Modifiers.cs

            // ── right: preview + transport + backdrop ────────────────────────────
            var rightPane = new VisualElement();
            rightPane.style.flexGrow = 1f;
            rightPane.style.minWidth = 200f;
            rightPane.style.minHeight = 0f;

            preview = new IMGUIContainer(() => DrawPreview(s));
            preview.style.flexGrow = 1f;
            preview.style.minWidth = 200f;
            preview.style.minHeight = 160f;
            preview.AddToClassList("zui-stage");
            rightPane.Add(preview);

            // Transport (Play/Pause + Frame border) and the shared BackSplash backdrop panel sit below the preview.
            var chrome = new VisualElement();
            chrome.style.flexShrink = 0f;
            BuildTransport(chrome, s);
            BuildBackdropPanel(chrome);
            rightPane.Add(chrome);

            split.Add(left);
            split.Add(rightPane);
            root.style.flexGrow = 1f;
            root.style.minHeight = 0f;
            root.Add(split);

            // Whole tree is now under `root`; re-apply the view the user left this window in.
            viewBar.RestoreLast();
        }

        // ── saved views (Z2 — the shared ZuiViewBar + committed ZuiViewStore) ──────────
        const string ViewStorePath = "Assets/PyrePlus/PyrePlusViews.asset";
        const string ViewPrefsKey = "PyrePlus.lastView";

        // Build the views bar. Capture/apply aggregate every ZuiBox under `paneRoot` via CaptureView/
        // ApplyView, so a view round-trips the Canvas / Solid / Transform / Modifiers boxes' fold + gear +
        // shown-control state. The store asset is committed (shared); only the per-user "last view" pointer
        // is in EditorPrefs. The bar never touches AssetDatabase — the host mints the asset (below).
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
                CreateViewStore,
                Capture,
                Apply,
                ViewPrefsKey);
        }

        // Mint the PyrePlus views asset — only ever called from the bar's Save-as when none exists yet.
        // Undo-registered per the Laubrary Undo rule; creates the folder if missing.
        ZuiViewStore CreateViewStore()
        {
            if (!AssetDatabase.IsValidFolder("Assets/PyrePlus"))
                AssetDatabase.CreateFolder("Assets", "PyrePlus");
            var store = ScriptableObject.CreateInstance<ZuiViewStore>();
            AssetDatabase.CreateAsset(store, ViewStorePath);
            Undo.RegisterCreatedObjectUndo(store, "Create Pyre Plus Views");
            return store;
        }

        // Preview transport: a Play/Pause button (flips its own label) + the Frame-border toggle + the Strip
        // (filmstrip / contact-sheet) toggle and, when Strip is on, a Tile-px size slider. Lives in a host that
        // rebuilds on the Strip toggle so the Tile-px slider appears/disappears (the same show/hide idiom the
        // Shape/Swarm sections use). The minimal mirror of PyreWindow's transport row.
        VisualElement transportHost;

        void BuildTransport(VisualElement root, PyrePlusSpec s)
        {
            transportHost = new VisualElement();
            root.Add(transportHost);
            RebuildTransport(s);
        }

        void RebuildTransport(PyrePlusSpec s)
        {
            if (transportHost == null) return;
            transportHost.Clear();
            playButton = Z.Button(playing ? "❚❚ Pause" : "▶ Play", "Play or pause the looping preview.", () =>
            {
                playing = !playing;
                playButton.text = playing ? "❚❚ Pause" : "▶ Play";
            });
            var kids = new List<VisualElement>
            {
                playButton,
                Z.Toggle("Frame",
                    "Draw a thin border around the canvas edge (cosmetic only — never baked). In Strip mode it "
                    + "outlines every frame tile.",
                    s.previewShowFrame, v => Dirty(() => s.previewShowFrame = v)),
                Z.Toggle("Strip",
                    "Show the whole animation as a contact sheet of every frame instead of one zoomed frame. Tiles "
                    + "lay out left-to-right and wrap to more rows when they overflow the width; click a tile to "
                    + "jump the transport to that frame. (No scrolling — a block taller than the view is clipped.)",
                    s.previewStrip, v => { Dirty(() => s.previewStrip = v); RebuildTransport(s); }),
            };
            if (s.previewStrip)
                kids.Add(Z.MicroSlider("Tile px", s.previewStripSize, 32f, 256f,
                    "Size of each frame tile in the contact sheet, in pixels (32–256). Only changes how the strip is "
                    + "laid out — the frames aren't re-rendered.",
                    v => DirtyRepaintOnly(() => s.previewStripSize = Mathf.Clamp(v, 32f, 256f)), 150f,
                    showValue: true, decimals: 0));
            // GIF export (G3): the button opens a Save dialog and writes an animated GIF; the packed scale field is
            // its nearest-neighbour upscale. The scale is a cosmetic spec field — record Undo + SetDirty, no
            // re-render (it doesn't touch the preview frames).
            kids.Add(Z.Button("GIF…",
                "Export the whole animation as an animated GIF — transparent background, loops forever, at the "
                + "frame rate above. Opens a Save dialog for the file location.",
                ExportGif));
            kids.Add(Z.Field("Scale",
                "Nearest-neighbour upscale applied to the exported GIF (1–8×). Bigger = a larger file with the "
                + "same crisp pixels.",
                Z.Int(s.previewGifScale, "Nearest-neighbour upscale for the exported GIF (1–8×).",
                    v =>
                    {
                        if (spec == null) return;
                        Undo.RecordObject(spec, "Edit Pyre Plus");
                        s.previewGifScale = Mathf.Clamp(v, 1, 8);
                        EditorUtility.SetDirty(spec);
                    }, 44f)));
            transportHost.Add(WrapRow(kids.ToArray()));

            // ── frame scrubber (transport parity with Pyre1's scrub field) ──────────────────────
            // A 1-based int slider over the whole frame range that drives the transport `frame`. Dragging it PAUSES
            // playback and holds that frame — mirroring PyreWindow's scrub (playing = false + reset the Play label).
            // It works in filmstrip mode too: there it just moves the highlighted tile, and since clicking a tile
            // also sets `frame` + calls RefreshTransportReadout, slider ↔ tile stay in sync. No previewDirty — the
            // frame change alone re-renders the single-frame view (DrawPreview: cur != lastRenderedFrame).
            int fcHigh = Mathf.Max(1, s.frameCount);
            scrubSlider = Z.SliderInt(Mathf.Clamp(frame, 0, fcHigh - 1) + 1, 1, fcHigh,
                "Scrub to an exact frame — dragging pauses playback and holds that frame (works in the filmstrip too).",
                v =>
                {
                    frame = Mathf.Clamp(v - 1, 0, Mathf.Max(0, s.frameCount - 1));
                    playing = false;
                    if (playButton != null) playButton.text = "▶ Play";
                    preview?.MarkDirtyRepaint();
                    RefreshTransportReadout();
                }, 200f);
            transportHost.Add(Z.Field("Frame",
                "Scrub to an exact frame — dragging pauses playback and holds that frame.", scrubSlider));

            // ── zoom + speed + the frame readout, grouped exactly like PyreWindow's transport ───
            // Zoom magnifies the single-frame view only (cosmetic layout, never re-renders the frames or the
            // filmstrip → DirtyRepaintOnly), rounded to an integer like Pyre1. Speed drives the preview frame rate
            // (previewFps) — an absolute fps rather than Pyre1's 0.1–3× multiplier, since previewFps IS PyrePlus's
            // playback rate; it also sets the exported GIF's rate. The readout mirrors Pyre1's "frame N/M" label.
            var zoomMs = Z.MicroSlider("Zoom", s.previewZoom, 1f, 16f,
                "Magnification of the single-frame preview (canvas pixels × zoom). Does not affect the filmstrip or "
                + "the baked frames.",
                v => DirtyRepaintOnly(() => s.previewZoom = Mathf.Max(1f, Mathf.Round(v))), 150f,
                showValue: true, decimals: 0);
            var speedMs = Z.MicroSlider("Speed", s.previewFps, 1f, 30f,
                "Preview playback rate in frames per second — how fast the loop plays (also the exported GIF's rate).",
                v => DirtyRepaintOnly(() => s.previewFps = Mathf.Clamp(Mathf.Round(v), 1f, 30f)), 150f,
                showValue: true, decimals: 0);
            frameReadout = Z.Text("", ZuiText.Subtle, "The frame currently shown / the total frame count.");
            transportHost.Add(WrapRow(zoomMs, speedMs, frameReadout));

            RefreshTransportReadout();
        }

        // Keep the scrubber value + range and the "frame N/M" readout in sync with the transport `frame` and the
        // current frame count. Called after every frame change (playback tick, a strip-tile click, a scrub drag)
        // and at the end of RebuildTransport. Null-guarded so it's safe before the transport is built / rebuilt.
        void RefreshTransportReadout()
        {
            if (spec == null) return;
            int fc = Mathf.Max(1, spec.frameCount);
            int cur = Mathf.Clamp(frame, 0, fc - 1);
            if (scrubSlider != null)
            {
                scrubSlider.highValue = fc;
                scrubSlider.SetValueWithoutNotify(cur + 1);
            }
            if (frameReadout != null) frameReadout.text = $"frame {cur + 1}/{fc}";
        }

        // GIF export (G3): render every frame and write an animated GIF at the chosen nearest-neighbour scale. The
        // path comes from a user Save dialog (cancel = empty path = no-op); RevealInFinder opens the result folder.
        // No AssetDatabase work here — if the user saves inside Assets/ the orchestrator owns the import refresh.
        void ExportGif()
        {
            if (spec == null) return;
            string path = EditorUtility.SaveFilePanel("Export GIF", "", (spec.name ?? "PyrePlus") + ".gif", "gif");
            if (string.IsNullOrEmpty(path)) return;
            PyrePlusGif.Export(spec, path, Mathf.Clamp(spec.previewGifScale, 1, 8));
            EditorUtility.RevealInFinder(path);
        }

        // The shared BackSplash backdrop panel (also used by Pyre / Mirage) — a cosmetic preview aid, never baked.
        // onStructureChanged rebuilds it when the backdrop image is picked or cleared (it adds/removes controls).
        void BuildBackdropPanel(VisualElement root)
        {
            backdropHost = new VisualElement();
            root.Add(backdropHost);
            FillBackdropPanel();
        }

        void FillBackdropPanel()
        {
            if (backdropHost == null) return;
            backdropHost.Clear();
            backdropHost.Add(BackSplashZui.Build(backSplash, "Preview backdrop",
                "A cosmetic backdrop for the preview only — a solid colour plus one optional image. Never baked and "
                + "has no effect on the render. A private copy: Recall copies values FROM a preset, Save writes them TO one.",
                onChanged: () => preview?.MarkDirtyRepaint(),
                onStructureChanged: () => { preview?.MarkDirtyRepaint(); FillBackdropPanel(); }));
        }

        void BuildCanvas(VisualElement root, PyrePlusSpec s)
        {
            var box = Z.BoxKeyed("Canvas", "The output resolution, frame count, seed and background.", "pyreplus.canvas");
            box.Add(WrapRow(
                Z.Field("Size", "Square canvas size in pixels.",
                    Z.Int(s.canvasSize, "Square canvas size in pixels.", v => Dirty(() => s.canvasSize = Mathf.Max(1, v)), 60f)),
                Z.Field("PPU", "Pixels per unit for the baked sprite.",
                    Z.Float(s.pixelsPerUnit, "Pixels per unit.", v => Dirty(() => s.pixelsPerUnit = Mathf.Max(1f, v)), 60f))));
            box.Add(WrapRow(
                // Frames is a label-inside int MicroSlider (NOT a thumbed SliderInt): a thumbed slider here read as a
                // frame scrubber and the user kept grabbing it by mistake. The transport's frame SCRUBBER stays a
                // thumbed Z.SliderInt on purpose (Pyre1 parity) — this "how many frames to bake" count does not.
                Z.MicroSlider("Frames", s.frameCount, 1f, 64f,
                    "How many frames the animation bakes to.",
                    v => Dirty(() => s.frameCount = Mathf.Clamp(Mathf.RoundToInt(v), 1, 64)), 150f,
                    showValue: true, decimals: 0),
                Z.Field("Seed", "Random seed — every particle's randomness derives from it.",
                    Z.Int(s.seed, "Random seed.", v => Dirty(() => s.seed = v), 70f))));
            box.Add(BackgroundFillRow(s));
            root.Add(box);
        }

        // The background clear, as a ZuiFill — Solid transparent by default (which bakes exactly as the old flat
        // clear). Editing it in ANY way flips backgroundUseFill on, so the renderer switches from the flat
        // `background` clear to evaluating this fill per pixel across the canvas (a gradient / noise / grid / dots /
        // sprite backdrop). No separate "use fill" toggle: a Solid-transparent fill already reproduces the empty
        // backdrop, so editing-turns-it-on is the whole control (ui-layout-rules: don't add a toggle you don't need).
        ZuiFillControl BackgroundFillRow(PyrePlusSpec s)
        {
            s.backgroundFill ??= new ZuiFill(new Color(0f, 0f, 0f, 0f));
            return Z.Fill("Background",
                s.backgroundFill,
                "The backdrop behind every layer. Default = a transparent clear (composites into a game scene). "
                + "Editing it turns on the per-pixel background fill: make it a solid colour, a gradient, noise, a "
                + "grid, dots, or a stamped sprite. Cosmetic backdrops for the PREVIEW only live in the backdrop "
                + "panel below — this one IS baked into the frames.",
                onChanged: () => { s.backgroundUseFill = true; if (spec != null) EditorUtility.SetDirty(spec); MarkDirty(); },
                onBeforeMutate: () => { if (spec != null) Undo.RecordObject(spec, "Edit Pyre Plus"); },
                new ZuiFillControl.Options().WithWidth(190f).WithGrow(2.2f));
        }

        // ── layer list (R3) ─────────────────────────────────────────────────────────
        // A compact stack at the top of the left pane. One row per layer — reorder grip, enable toggle, select
        // button, rename-in-place name field, per-row remove — plus an add / duplicate row. Selection drives which
        // layer the Shape / Swarm / Matte / Modifiers sections below edit. Mirrors PyreWindow's layer-list chrome.
        void BuildLayerList(VisualElement root)
        {
            var box = Z.BoxKeyed("Layers",
                "The paint stack — earlier (higher) layers composite BEHIND later (lower) ones. Click a layer to "
                + "edit its Shape / Swarm / Matte / Modifiers dials below; drag the grip to reorder.",
                "pyreplus.layers");
            layerListHost = new VisualElement();
            box.Add(layerListHost);
            RebuildLayerList();
            box.Add(WrapRow(
                Z.Button("+ Add layer", "Append a new default layer to the FRONT of the stack (undoable).", AddLayer),
                Z.Button("Duplicate", "Duplicate the selected layer just in front of itself (undoable).", DuplicateSelectedLayer)));
            root.Add(box);
        }

        void RebuildLayerList()
        {
            if (layerListHost == null || spec == null || spec.layers == null) return;
            layerListHost.Clear();
            for (int li = 0; li < spec.layers.Count; li++)
                layerListHost.Add(BuildLayerRow(layerListHost, li));
        }

        VisualElement BuildLayerRow(VisualElement listHost, int li)
        {
            var layer = spec.layers[li];
            bool sel = li == layerSel;

            var row = new VisualElement();
            row.AddToClassList("zui-row");
            if (sel) row.style.backgroundColor = new Color(0.35f, 0.55f, 0.95f, 0.18f);

            var grip = Z.Text("≡", ZuiText.Body, "Drag to reorder this layer in the paint stack.");
            grip.style.unityFontStyleAndWeight = FontStyle.Bold;
            grip.style.width = 16f;
            ZuiReorder.MakeGrip(grip, row, listHost, (from, to) =>
            {
                Dirty(() =>
                {
                    var l = spec.layers[from];
                    spec.layers.RemoveAt(from);
                    spec.layers.Insert(to, l);
                });
                layerSel = to;
                RebuildAllForSelection();
            });
            row.Add(grip);

            row.Add(Z.Toggle("", "Show or hide this layer in the render.", layer.enabled,
                v => { Dirty(() => layer.enabled = v); RebuildLayerList(); }));

            row.Add(Z.Button(sel ? "●" : "○", "Select this layer to edit it below.", () => SelectLayer(li)).W(24f));

            var name = Z.TextInput(layer.name ?? "", "This layer's name — rename it right here.", v =>
            {
                Undo.RecordObject(spec, "Rename layer");
                layer.name = v;
                EditorUtility.SetDirty(spec);
            }, 0f);
            name.style.width = StyleKeyword.Auto;
            name.style.flexGrow = 1f;
            name.style.flexShrink = 1f;
            name.style.minWidth = 50f;
            name.AddToClassList("zui-audit-allow-stretch");   // the rulebook's name-field stretch exception
            name.RegisterCallback<PointerDownEvent>(_ => { if (layerSel != li) SelectLayer(li); });
            row.Add(name);

            if (!layer.enabled) row.Add(Z.Text("off", ZuiText.Small, "This layer is currently hidden."));
            else if (layer.matteRole == MatteRole.WriteMatte)
                row.Add(Z.Text($"→{Mathf.Clamp(layer.matteChannel, 0, 3)}", ZuiText.Small,
                    "A matte layer — invisible; writes its coverage into the shown mask channel."));

            row.Add(Z.Button("✕", "Delete this layer (undoable).", () =>
            {
                if (spec.layers.Count <= 1) { ShowNotification(new GUIContent("A Pyre Plus asset needs at least one layer.")); return; }
                Dirty(() => spec.layers.RemoveAt(li));
                layerSel = Mathf.Clamp(layerSel, 0, spec.layers.Count - 1);
                RebuildAllForSelection();
            }).W(22f));
            return row;
        }

        void AddLayer()
        {
            if (spec == null) return;
            Dirty(() =>
            {
                spec.layers.Add(new PyrePlusLayer { name = $"Layer {spec.layers.Count + 1}" });
                layerSel = spec.layers.Count - 1;
            });
            RebuildAllForSelection();
        }

        void DuplicateSelectedLayer()
        {
            var srcLayer = SelLayer;
            if (srcLayer == null) return;
            Dirty(() =>
            {
                var copy = srcLayer.Clone();
                copy.name = (srcLayer.name ?? "Layer") + " copy";
                int at = Mathf.Clamp(layerSel + 1, 0, spec.layers.Count);
                spec.layers.Insert(at, copy);
                layerSel = at;
            });
            RebuildAllForSelection();
        }

        void SelectLayer(int li)
        {
            layerSel = li;
            RebuildAllForSelection();
        }

        // Re-point every selection-bound section at the (possibly new) SelLayer and refresh the list highlight.
        void RebuildAllForSelection()
        {
            RebuildLayerList();
            RebuildShape();
            RebuildSwarm();
            RebuildMatte();
            RebuildModifiers();
            MarkDirty();
        }

        // ── Matte (R3) — the selected layer's role in the stack ──────────────────────
        static readonly string[] MatteRoleLabels = { "Draw", "Write matte" };
        static readonly string[] MatteCombineLabels = { "Max", "Add", "Subtract" };
        static readonly List<string> ClipChannelChoices = new List<string> { "None", "0", "1", "2", "3" };

        void BuildMatte(VisualElement root)
        {
            var box = Z.BoxKeyed("Matte",
                "Turns this layer into a stencil. A Write-matte layer is INVISIBLE — instead of drawing, it writes "
                + "its coverage into one of four numbered mask channels. A Draw layer can then Clip its own opacity "
                + "by any channel a layer BELOW it wrote, so an earlier shape can mask or cut into a later one.",
                "pyreplus.matte");
            matteBody = new VisualElement();
            box.Add(matteBody);
            root.Add(box);
            RebuildMatte();
        }

        void RebuildMatte()
        {
            if (matteBody == null) return;
            matteBody.Clear();
            var sel = SelLayer;
            if (sel == null) return;

            matteBody.Add(Z.Field("Role",
                "Draw = composite this layer onto the frame normally. Write matte = don't draw it; write its "
                + "coverage into a mask channel for the Draw layers above to clip by.",
                Z.Segmented((int)sel.matteRole, MatteRoleLabels,
                    "Draw composites this layer. Write matte makes it invisible and stencils a channel instead.",
                    v => { Dirty(() => sel.matteRole = (MatteRole)v); RebuildMatte(); RebuildLayerList(); })));

            if (sel.matteRole == MatteRole.WriteMatte)
            {
                matteBody.Add(WrapRow(
                    Z.MicroSlider("Channel", sel.matteChannel, 0f, 3f,
                        "Which of the four mask channels (0–3) this layer's coverage writes into. A Draw layer above "
                        + "picks the same number in its Clip-by to be stencilled by this layer.",
                        v => Dirty(() => sel.matteChannel = Mathf.Clamp(Mathf.RoundToInt(v), 0, 3)), 150f,
                        showValue: true, decimals: 0),
                    Z.Field("Combine",
                        "How this layer's coverage merges with anything an earlier matte layer already wrote into "
                        + "the same channel. Max = union; Add = accumulate; Subtract = carve out.",
                        Z.Segmented((int)sel.matteCombine, MatteCombineLabels,
                            "Max = union of masks (default). Add = accumulate & clamp. Subtract = carve one mask out of another.",
                            v => Dirty(() => sel.matteCombine = (MatteCombine)v)))));
            }
            else
            {
                matteBody.Add(WrapRow(
                    Z.Field("Clip by",
                        "Multiply THIS layer's opacity by a mask channel an EARLIER (lower) layer wrote — None = no "
                        + "clipping. The layer then only shows where that channel is bright.",
                        Z.Dropdown(Mathf.Clamp(sel.clipByChannel + 1, 0, 4), ClipChannelChoices,
                            "None, or channel 0–3 written by a Write-matte layer below this one. This layer is clipped to it.",
                            v => Dirty(() => sel.clipByChannel = v - 1), 100f)),
                    Z.Toggle("Invert",
                        "Clip by (1 − channel) instead — show where the mask is DARK, hide where it's bright.",
                        sel.clipInvert, v => Dirty(() => sel.clipInvert = v))));
            }
        }

        // The Shape section is a stable header + a body container we clear/refill whenever the Advanced gate
        // flips — the same mechanism BuildSwarm uses for swarmBody/RebuildSwarm, so the opt-in Travel/Spin
        // controls appear/disappear without rebuilding the whole window.
        VisualElement shapeBody;

        void BuildShape(VisualElement root, PyrePlusSpec s)
        {
            var sec = Z.Section("Shape", "The particle's own look — colour, opacity and size over its life.");
            shapeBody = new VisualElement();
            sec.Add(shapeBody);
            root.Add(sec);
            RebuildShape();
        }

        static readonly List<string> ShapeFormChoices = new List<string> { "Disc", "Gem", "Crescent", "Sparkle", "Sprite", "Box", "Pyramid", "Can", "Orb", "Ring", "Text", "Streak", "Star" };
        static readonly List<string> TextFillModeChoices = new List<string> { "Per-char gradient", "Per-char step", "Text gradient" };

        void RebuildShape()
        {
            var s = SelLayer;   // every Shape control now edits the SELECTED layer's fields
            if (s == null || shapeBody == null) { shapeBody?.Clear(); return; }
            shapeBody.Clear();

            s.alpha ??= new ZUIValue(1f);
            s.shapeFill ??= new ZuiFill();   // defensive; the real OverLife-fire default comes from the spec factory

            // FORM selector — Disc / Gem / Crescent / Sparkle / Sprite / Box / Pyramid / Can. A Dropdown, NOT an
            // 8-wide Segmented row: content-sized segments (ZuiSegmented is flex-shrink:0 and never wraps) would
            // overflow the 360px pane into a horizontal scrollbar (ui-layout-rules: "A horizontal scrollbar is a
            // smell"); this also matches the Swarm section's own shape-kind Dropdown. Rebuild so the form-specific
            // rows swap in/out.
            shapeBody.Add(Z.Field("Form", "The particle's rendered form.",
                Z.Dropdown((int)s.shapeForm, ShapeFormChoices,
                    "Disc = a flat soft disc. Gem = a true-3D lit crystal. Crescent = a disc with an offset bite. "
                    + "Sparkle = twinkling lit cells. Sprite = a stamped image. Box / Pyramid / Can = true-3D lit "
                    + "solids sharing the Gem's facet lighting (tilt, light, edge lines, glows). Orb = a lit "
                    + "sphere; Ring = a flat tilted annulus (a Saturn ring) — both reuse that same lighting "
                    + "analytically. Text = a string as extruded SDF letters (one particle per character; the "
                    + "particle count follows the string). Streak = a root-anchored comet-tail capsule that grows "
                    + "forward along its orientation (great with the Swarm's Orient). Star = a filled star polygon "
                    + "(arms, reach, base width, swirl).",
                    // Rebuild BOTH sections: Text swaps in its own Shape box AND hides the Swarm's Count field.
                    v => { Dirty(() => s.shapeForm = (ShapeForm)v); RebuildShape(); RebuildSwarm(); }, 150f)));

            // Shared rows. For the Gem, Colour is its material tint and Size is its girdle radius; for the Sprite,
            // Colour is the optional tint. TEXT takes its colour from its own Fill / Border gradients instead, so
            // the Colour row is hidden for it (the Text box's Fill row tooltip says so). Size is the scale driver
            // for every form — Text reads it as the character HEIGHT.
            // Colour → a ZuiFill: Solid, Over life (a gradient, the default), or a spatial fill (linear/radial/
            // noise); every mode is alpha-capable, switched via the ⋯ menu. Hidden for Text (its own per-char fill
            // rules its colour). Z.Fill draws its own "Fill" label + the ⋯, so it isn't wrapped in a Z.Field; its
            // tooltip is composed for the CURRENT form.
            if (s.shapeForm != ShapeForm.Text)
                shapeBody.Add(FillRow("Fill", FillTooltip(s.shapeForm), s.shapeFill,
                    new ZuiFillControl.Options().WithWidth(190f).WithGrow(2.2f)));
            shapeBody.Add(Val("Alpha", "Opacity over the particle's own life (multiplies the final output alpha).", s.alpha, 0f, 1f));
            // Size drives every form's scale — except the Streak, which has its OWN Length/Width envelopes, so the
            // shared Size row is hidden for it (a dead control would be clutter per the layout rules).
            if (s.shapeForm != ShapeForm.Streak)
                shapeBody.Add(Val("Size (px)", SizeTooltip(s.shapeForm), s.size, 0f, 32f));

            // Form-specific rows. Edge softness applies to Disc (its rim) and Crescent (BOTH rims); Gem/Sparkle/
            // Sprite don't use it, so it's hidden for them. (EdgeRow is a bare MicroSlider — its own caption is
            // the "Edge" label, so no redundant Z.Field label wrapping it.)
            switch (s.shapeForm)
            {
                case ShapeForm.Gem:
                case ShapeForm.Box:
                case ShapeForm.Pyramid:
                case ShapeForm.Can:
                case ShapeForm.Orb:
                case ShapeForm.Ring:
                    BuildSolidBox(s);
                    break;
                case ShapeForm.Disc:
                    shapeBody.Add(EdgeRow(s, "Soft rim (1) vs a hard pixel edge (0)."));
                    break;
                case ShapeForm.Crescent:
                    BuildCrescentRows(s);
                    break;
                case ShapeForm.Sparkle:
                    BuildSparkleRows(s);
                    break;
                case ShapeForm.Sprite:
                    BuildSpriteRows(s);
                    break;
                case ShapeForm.Text:
                    BuildTextBox(s);
                    break;
                case ShapeForm.Streak:
                    BuildStreakRows(s);
                    break;
                case ShapeForm.Star:
                    BuildStarRows(s);
                    break;
            }

            // Advanced gate: the particle's OWN motion after birth (opt-in). Toggling rebuilds just this section.
            shapeBody.Add(Z.Toggle("Advanced",
                "Per-particle motion on the particle's own life clock: a travel path added to its spawn position, "
                + "and an in-place spin.",
                s.shapeAdvanced, v => { Dirty(() => s.shapeAdvanced = v); RebuildShape(); }));
            if (!s.shapeAdvanced) return;

            float half = Mathf.Max(1f, spec.canvasSize * 0.5f);
            s.particlePathX ??= new ZUIValue(0f);
            s.particlePathY ??= new ZUIValue(0f);
            s.particleSpin ??= new ZUIValue(0f);

            shapeBody.Add(Val2D("Travel",
                "The particle's own path after birth: canvas-pixel offsets ADDED to its spawn position, sampled on "
                + "the particle's OWN life (0 = birth, 1 = death). Author it as a Curve to make the particle "
                + "drift/arc as it lives; Static 0 = no travel.",
                s.particlePathX, s.particlePathY,
                new ZuiValue2DControl.Options().WithRange(-half, half, -half, half).WithDefault(Vector2.zero)));
            // Spin is the particle's own-life in-place rotation. For the 3D solid forms it IS the "Turn °" control
            // in the Solid box above (the same particleSpin field, shown once), so it's hidden here for them; 2D
            // forms and Text keep it. Its tooltip is composed for the CURRENT form.
            if (!IsSolidForm(s.shapeForm))
                shapeBody.Add(Val("Spin °", SpinTooltip(s.shapeForm), s.particleSpin, -720f, 720f));
        }

        // The 3D-solid controls — one framed box inside the Shape body, shown for Gem / Box / Pyramid / Can (all
        // true-3D convex facet solids sharing one renderer). Colour, Alpha and Size stay above (shared); this box
        // adds the per-form geometry then the SHARED tilt / lighting / lines / glows every 3D solid uses. Titled
        // "Solid" (not "Gem") since it now serves four forms. All plain sliders are label-inside MicroSliders; the
        // two glows are animatable ZUIValues (Val), each packed with its own colour; Specular packs with its colour.
        void BuildSolidBox(PyrePlusLayer s)
        {
            s.particleSpin ??= new ZUIValue(0f);
            s.gemTilt ??= new ZUIValue(18f);
            s.gemRoll ??= new ZUIValue(0f);
            s.gemEdgeGlow ??= new ZUIValue(0.5f);     // defensive; the real steady defaults come from the spec factories
            s.gemInnerGlow ??= new ZUIValue(0.35f);

            // BoxKeyed: view presets persist under this stable key — retitling the box or rewording
            // its tooltip must never orphan saved views.
            var box = Z.BoxKeyed("Solid", SolidBoxTooltip(s.shapeForm), "pyreplus.solid");

            // ── per-form geometry ──
            if (s.shapeForm == ShapeForm.Gem)
            {
                box.Add(Z.MicroSlider("Sides", s.gemSides, 3f, 8f,
                    "Girdle vertex count — 4 is the classic octahedral gem; more sides make a rounder crystal.",
                    v => Dirty(() => s.gemSides = Mathf.Clamp(Mathf.RoundToInt(v), 3, 8)), 150f, showValue: true, decimals: 0));

                box.Add(WrapRow(
                    Z.MicroSlider("Crown", s.gemCrown, 0.2f, 2.5f,
                        "Crown height (the top point) as a fraction of the gem's radius.",
                        v => Dirty(() => s.gemCrown = v), 150f, showValue: true),
                    Z.MicroSlider("Pavilion", s.gemPavilion, 0.2f, 2.5f,
                        "Pavilion depth (the bottom point) as a fraction of the gem's radius.",
                        v => Dirty(() => s.gemPavilion = v), 150f, showValue: true)));
            }
            else if (s.shapeForm == ShapeForm.Box || s.shapeForm == ShapeForm.Pyramid || s.shapeForm == ShapeForm.Can)
            {
                // Box / Pyramid / Can — Aspect (height) always applies. Depth (front-to-back) applies to Box and
                // Pyramid; the Can is a circular cross-section, so its Depth is meaningless — HIDDEN rather than
                // shown-disabled (a dead control is clutter per the layout rules), leaving Aspect alone.
                var geo = new List<VisualElement>
                {
                    Z.MicroSlider("Aspect", s.solidAspect, 0.3f, 3f,
                        "Height as a fraction of width (1 = as tall as wide). Box height, Pyramid apex height, Can "
                        + "height.",
                        v => Dirty(() => s.solidAspect = v), 150f, showValue: true),
                };
                if (s.shapeForm != ShapeForm.Can)
                    geo.Add(Z.MicroSlider("Depth", s.solidDepth, 0.2f, 2f,
                        "Depth (front-to-back) as a fraction of width. Box: its third dimension; Pyramid: its base "
                        + "front-to-back (1 = the square base).",
                        v => Dirty(() => s.solidDepth = v), 150f, showValue: true));
                box.Add(WrapRow(geo.ToArray()));
            }
            else if (s.shapeForm == ShapeForm.Ring)
            {
                // Ring — one geometry row: the hole radius. (Orb has NO geometry rows — a sphere needs none — so it
                // falls straight through to the shared tilt / light / lines / glows below.)
                box.Add(Z.MicroSlider("Inner", s.ringInner, 0.1f, 0.92f,
                    "Inner radius as a fraction of the outer radius — the size of the ring's hole (0.1 = a nearly "
                    + "solid disc, 0.92 = a thin hoop).",
                    v => Dirty(() => s.ringInner = v), 150f, showValue: true));
            }

            // ── shared: rotation trio / light / lines / glows (every 3D solid) ──
            // Turn (yaw = the shared particleSpin), Tilt (gemTilt) and Roll (gemRoll) — the three rotation axes in
            // plain words, each animatable over the particle's own life. Roll is geometrically inert for the
            // symmetric Ring (Turn + Tilt already shape its ellipse), so it's hidden there. Tooltips are composed
            // for the CURRENT form.
            box.Add(Val("Turn °", TurnTooltip(s.shapeForm), s.particleSpin, -1440f, 1440f));
            box.Add(Val("Tilt °", TiltTooltip(s.shapeForm), s.gemTilt, -1440f, 1440f));
            if (s.shapeForm != ShapeForm.Ring)
                box.Add(Val("Roll °", RollTooltip(s.shapeForm), s.gemRoll, -1440f, 1440f));

            // Light / Lines / Glow rows opt into the box's ⚙ gear (the Turn/Tilt/Roll trio above stays mandatory). Each group
            // toggle flips its whole cluster at once; keys are STABLE "solid.*" strings (never a display
            // label) so a saved view survives a relabel. A saved "view" round-trips these on/off states.
            box.ToggleGroup("Light", "Light");
            box.ToggleGroup("Lines", "Lines");
            box.ToggleGroup("Glow", "Glow");

            box.Add(Z.Divider("Light",
                "The single KEY light and how the surfaces respond to it. The key light is DIRECTIONAL — aim it with "
                + "the pad; Ambient is a separate non-directional base light. The edge Lines and the Glows have their "
                + "OWN strengths and do NOT obey this light."));
            // Light DIRECTION as one 2D pad (yaw × pitch) — a plain-Vector2 Z.Pad, so dragging aims the key light in
            // one gesture instead of two separate 1D sliders. X = yaw (gemLightYaw, −180..180), Y = pitch
            // (gemLightPitch, 0..85). Kept under the same "solid.light.angles" view key as the old angle sliders.
            const string lightDirTip = "The key light is DIRECTIONAL — drag the pad to aim it. X = yaw (which side it "
                + "comes FROM, left/right, −180..180°); Y = pitch (its height above the horizon, 0..85°). Only the lit "
                + "faces and the specular hotspot follow it — the Lines and Glows have their own strengths and do NOT "
                + "obey the light.";
            box.Add(box.Toggleable(
                Z.Field("Light dir", lightDirTip,
                    Z.Pad(new Vector2(s.gemLightYaw, s.gemLightPitch), new Rect(-180f, 0f, 360f, 85f), lightDirTip,
                        v => Dirty(() => { s.gemLightYaw = v.x; s.gemLightPitch = v.y; }), 56f)),
                "solid.light.angles", "Angle", "Light"));
            box.Add(box.Toggleable(WrapRow(
                Z.MicroSlider("Ambient", s.gemAmbient, 0f, 1f,
                    "Non-directional BASE light on every face (it doesn't come from a direction). Near zero keeps the "
                    + "solid contrasty; raise it to flatten the shading.",
                    v => Dirty(() => s.gemAmbient = v), 150f, showValue: true),
                Z.MicroSlider("Diffuse", s.gemDiffuse, 0f, 3f,
                    "The key light's DIFFUSE strength on the faces it hits (the Lambert term). 0 = only Ambient + "
                    + "Specular light the faces; 2.1 is the default look. This is the dial that was missing — with it "
                    + "at 0 and Ambient/Specular at 0 the faces finally go dark instead of staying diffuse-lit.",
                    v => Dirty(() => s.gemDiffuse = v), 150f, showValue: true),
                Z.MicroSlider("Specular", s.gemSpecular, 0f, 2f,
                    "Strength of the tight highlight (the bright hot spot) where the key light reflects — pair it with "
                    + "Spec power for the hotspot's tightness.",
                    v => Dirty(() => s.gemSpecular = v), 150f, showValue: true),
                Z.MicroSlider("Spec power", s.gemSpecPower, 2f, 128f,
                    "TIGHTNESS of the specular hotspot — higher = a smaller, sharper glint; lower spreads it into a "
                    + "broad sheen. (The old fixed 48 was so tight the highlight rarely showed — lower it to see it.)",
                    v => Dirty(() => s.gemSpecPower = v), 150f, showValue: true),
                SlotFill("Spec fill", "Fill for the specular highlight — Solid, or a gradient/spatial fill (alpha-capable).",
                    s.gemSpecularFill)),
                "solid.light.response", "Response", "Light"));

            box.Add(Z.Divider("Lines", "The hard facet edge lines that catch the light."));
            box.Add(box.Toggleable(WrapRow(
                Z.MicroSlider("Line width", s.gemLineWidth, 0f, 3f,
                    "Width of the hard facet edge lines in pixels (0 = no lines). The lines catch the key light.",
                    v => Dirty(() => s.gemLineWidth = v), 150f, showValue: true),
                SlotFill("Line fill", "Fill for the facet edge lines — Solid, or a gradient/spatial fill (alpha-capable).",
                    s.gemLineFill)),
                "solid.lines", "Width & fill", "Lines"));

            box.Add(Z.Divider("Glow", "A rim halo and an interior glow — steady by default (author a Curve to pulse), each with its own fill."));
            box.Add(box.Toggleable(WrapRow(
                Val("Edge glow",
                    "Strength (0-1) of the halo around the edge lines, over the particle's OWN life; it spills "
                    + "OUTSIDE the solid's silhouette. Static = a steady glow (the default); author a Curve to make "
                    + "it pulse over the particle's life.",
                    s.gemEdgeGlow, 0f, 1f),
                SlotFill("Edge fill", "Fill for the edge-line halo glow — Solid, or a gradient/spatial fill (alpha-capable).",
                    s.gemEdgeGlowFill)),
                "solid.glow.edge", "Edge", "Glow"));
            box.Add(box.Toggleable(WrapRow(
                Val("Inner glow",
                    "Strength (0-1) of the emissive glow rising from the facet interiors, over the particle's OWN "
                    + "life; interior only. Static = a steady glow (the default); author a Curve to make it pulse "
                    + "over the particle's life.",
                    s.gemInnerGlow, 0f, 1f),
                SlotFill("Inner fill", "Fill for the facet inner glow — Solid, or a gradient/spatial fill (alpha-capable).",
                    s.gemInnerGlowFill)),
                "solid.glow.inner", "Inner", "Glow"));

            shapeBody.Add(box);
        }

        // The Edge-softness slider — its own MicroSlider caption is the "Edge" label, so it isn't wrapped in a
        // Z.Field (that would print "Edge" twice). Shared by Disc (its single rim) and Crescent (both rims), each
        // passing its own tooltip.
        ZuiMicroSlider EdgeRow(PyrePlusLayer s, string tooltip) =>
            Z.MicroSlider("Edge", s.edgeSoftness, 0f, 1f, tooltip,
                v => Dirty(() => s.edgeSoftness = v), 150f, showValue: true);

        // Crescent form rows — the shared Edge row (drives BOTH rims), then the bite: size + facing packed, and
        // the push-out offset.
        void BuildCrescentRows(PyrePlusLayer s)
        {
            s.crescentBite ??= new ZUIValue(0.55f);
            s.crescentAngle ??= new ZUIValue(0f);

            shapeBody.Add(EdgeRow(s,
                "Soft rim (1) vs a hard pixel edge (0). For the Crescent it feathers BOTH rims — the outer disc "
                + "edge and the bite edge."));
            shapeBody.Add(WrapRow(
                Val("Bite",
                    "Size of the disc bitten out of the main disc, as a fraction of its radius (0 = no bite, a "
                    + "full disc; 1 = a bite as wide as the disc), over the particle's own life.",
                    s.crescentBite, 0f, 1f),
                Val("Angle °",
                    "Which way the bite faces, in degrees, over the particle's own life — swings the crescent's "
                    + "opening around.",
                    s.crescentAngle, -360f, 360f)));
            shapeBody.Add(Z.MicroSlider("Offset", s.crescentOffset, 0f, 1f,
                "How far the bite disc is pushed out from the centre, as a fraction of the radius. Larger = a "
                + "thinner sliver of a crescent; 0 = the bite sits dead centre (a hole/ring).",
                v => Dirty(() => s.crescentOffset = v), 150f, showValue: true));
        }

        // Sparkle form rows — no Edge row (sparkles are hard pixels). Density (animatable) + the pixel block size.
        void BuildSparkleRows(PyrePlusLayer s)
        {
            s.sparkleDensity ??= new ZUIValue(0.35f);
            shapeBody.Add(WrapRow(
                Val("Density",
                    "Fraction of the disc's cells that sparkle, 0..1, over the particle's own life — a rising "
                    + "curve makes the sparkles ignite as it lives. Each lit cell also twinkles on/off per frame.",
                    s.sparkleDensity, 0f, 1f),
                Z.MicroSlider("Size px", s.sparkleSize, 1f, 4f,
                    "Size of each lit sparkle block in pixels (1 = single pixels, up to 4).",
                    v => Dirty(() => s.sparkleSize = Mathf.Clamp(Mathf.RoundToInt(v), 1, 4)), 150f,
                    showValue: true, decimals: 0)));
        }

        // Sprite form rows — no Edge row. The stamped image picker + the tint toggle, packed.
        void BuildSpriteRows(PyrePlusLayer s)
        {
            shapeBody.Add(WrapRow(
                Z.Field("Sprite",
                    "The image stamped at each particle. Its texture MUST have Read/Write enabled in its import "
                    + "settings, or it can't be sampled and the particle falls back to a plain disc.",
                    Z.Object<Sprite>(s.spriteImage,
                        "The stamped image — its texture needs Read/Write enabled (import settings), else the "
                        + "particle renders a disc fallback.",
                        v => Dirty(() => s.spriteImage = v), 160f)),
                Z.Toggle("Tint",
                    "Multiply the sprite by the Colour gradient at the particle's own life. Off = the sprite's own "
                    + "raw colours.",
                    s.spriteTint, v => Dirty(() => s.spriteTint = v))));
        }

        // Streak form rows — the streak's own Length (its scale driver, replacing the hidden shared Size), then
        // Width + Back + Tip packed, then the shared Edge row (which feathers the streak's two long SIDES). The
        // streak grows FORWARD from its root: default up, steered by the Swarm's Orient (and its own Advanced Spin).
        void BuildStreakRows(PyrePlusLayer s)
        {
            s.streakLength ??= new ZUIValue(20f);   // defensive; the real shoot-out arc comes from the spec factory
            s.streakWidth ??= new ZUIValue(3f);

            shapeBody.Add(Val("Length (px)",
                "The streak's length forward (from its root) in pixels, over the particle's own life. Author a "
                + "Curve to make it shoot out then ease shorter — the default comet-tail arc.",
                s.streakLength, 0f, 48f));

            shapeBody.Add(WrapRow(
                Val("Width (px)", "The streak's thickness across, in pixels, over the particle's own life.",
                    s.streakWidth, 0f, 16f),
                Z.MicroSlider("Back", s.streakBackFrac, 0f, 1f,
                    "How far the streak spills BEHIND its root, as a fraction of its length (0 = starts exactly at "
                    + "the root; 1 = a full length behind it).",
                    v => Dirty(() => s.streakBackFrac = v), 150f, showValue: true),
                Z.MicroSlider("Tip", s.streakSoftTip, 0f, 1f,
                    "Softness of the FORWARD tip — how much of the front end feathers out to transparent (0 = a "
                    + "hard flat tip; 1 = the whole streak fades toward the front).",
                    v => Dirty(() => s.streakSoftTip = v), 150f, showValue: true)));

            shapeBody.Add(EdgeRow(s,
                "Soft sides (1) vs hard pixel edges (0) — feathers the streak's two long SIDES (the forward tip is "
                + "the Tip control above; the back end is a hard cut)."));
        }

        // Star form rows — a filled star polygon. Arms (point count) packed with Skew (the arm swirl); then Length
        // (arm reach) packed with Base width (valley position); then the shared Edge row (the star's rim softness).
        // The shared Size row above stays visible — it's the tip radius the arms reach to.
        void BuildStarRows(PyrePlusLayer s)
        {
            s.starLength ??= new ZUIValue(0.62f);       // defensive; the real defaults come from the spec factories
            s.starBaseWidth ??= new ZUIValue(1f);
            s.starSkew ??= new ZUIValue(0f);

            shapeBody.Add(WrapRow(
                Z.MicroSlider("Arms", s.starArms, 2f, 20f,
                    "How many points the star has (2–20). 5 = the classic five-pointed star; 6 = a Star of David.",
                    v => Dirty(() => s.starArms = Mathf.Clamp(Mathf.RoundToInt(v), 2, 20)), 150f,
                    showValue: true, decimals: 0),
                Val("Skew °",
                    "Swirls the arms by rotating the inner (valley) vertices, in degrees, over the particle's own "
                    + "life — 0 = straight symmetric arms, ± twists them into a pinwheel. (Clamped so a valley "
                    + "never crosses a tip.)",
                    s.starSkew, -60f, 60f)));

            shapeBody.Add(WrapRow(
                Val("Length",
                    "How far the arm tips reach out, 0..1, over the particle's own life — the inner (valley) radius "
                    + "is R·(1−length), so higher = longer, sharper arms (0.62 ≈ the classical pentagram).",
                    s.starLength, 0f, 1f),
                Val("Base width",
                    "Angular width of each arm's base, 0.1..1, over the particle's own life — 1 = the classical "
                    + "midpoint valleys; smaller pulls the valleys toward the tips for thinner arm bases and wider "
                    + "notches between them.",
                    s.starBaseWidth, 0.1f, 1f)));

            shapeBody.Add(EdgeRow(s,
                "Soft rim (1) vs a hard pixel edge (0) — feathers the star's whole outline inward along each ray."));
        }

        // Text form box — the string, the SDF font, spacing, the fill (mode + angle + gradient), the border (width
        // + gradient), and the 3D extrusion (Solid + Depth). Depth shows only when Solid, so the Solid toggle
        // rebuilds the Shape body. Text takes its colour from the Fill / Border gradients here — the shared Colour
        // row above is hidden for it. Size (above) is the character height; Tilt is the shared gemTilt (default);
        // per-letter Spin lives in the Advanced section.
        void BuildTextBox(PyrePlusLayer s)
        {
            s.textFill ??= new ZuiFill();
            s.textBorder ??= new ZuiFill();

            var box = Z.BoxKeyed("Text",
                "Every character of the string is one particle, rendered from a TMP SDF font atlas: a spatial "
                + "gradient fill, an optional border, and optional 3D extrusion. Needs a font with a READABLE "
                + "atlas — leave the Font empty to auto-pick one. When the Swarm is off the letters lay out as one "
                + "centred line; when it's on, each letter rides a swarm position.", "pyreplus.text");

            box.Add(Z.Field("Text",
                "The characters to render — one particle per character. The particle count follows the string "
                + "length (the Swarm's Count is hidden for Text).",
                Z.TextInput(s.textString ?? "", "The characters to render (one particle per character).",
                    v => Dirty(() => s.textString = v), 200f)));

            box.Add(Z.Field("Font",
                "The TMP SDF font asset. Its atlas must be Read/Write-enabled (a Dynamic SDF font works). Leave "
                + "empty to auto-pick the first readable font in the project; a missing/non-readable font falls back "
                + "to a plain disc per character.",
                Z.Object<TMP_FontAsset>(s.textFont,
                    "SDF font — needs a readable atlas; leave empty to auto-pick.",
                    v => Dirty(() => s.textFont = v), 200f)));

            box.Add(Z.MicroSlider("Spacing", s.textSpacing, 0.6f, 1.6f,
                "Letter advance multiplier for the centred line layout — below 1 tightens the letters, above 1 "
                + "spreads them apart. (Only affects the swarm-off line; a swarm places letters by its own shape.)",
                v => Dirty(() => s.textSpacing = v), 150f, showValue: true));

            box.Add(WrapRow(
                Z.Field("Sweep",
                    "How the Fill's gradient is swept across the letters. Per-char gradient = each letter contains "
                    + "the whole gradient. Per-char step = each letter one flat colour along the gradient, by index. "
                    + "Text gradient = one gradient swept across the whole line (degrades to per-char step when the "
                    + "Swarm is on — there's no line to sweep). Text takes its colour from the Fill below, NOT the "
                    + "Colour gradient above. (A Solid or textured Fill ignores this — see the Fill.)",
                    Z.Dropdown((int)s.textFillMode, TextFillModeChoices,
                        "How the Fill's gradient is swept: per-char gradient / per-char step / one gradient across the whole line.",
                        v => Dirty(() => s.textFillMode = (TextFillMode)v), 130f)),
                Z.MicroSlider("Angle", s.textGradientAngle, -180f, 180f,
                    "Rotates the fill axis. 0 = vertical bottom→top for per-char gradient; 0 = left→right across "
                    + "the line for text gradient. (Per-char step is index-based and ignores it.)",
                    v => Dirty(() => s.textGradientAngle = v), 150f, showValue: true)));

            box.Add(FillRow("Fill",
                "The letter fill — a solid colour, a gradient (swept per the Sweep mode above), or a texture "
                + "(sprite / noise / grid / dots) stamped across each letter. This is where Text's colour comes "
                + "from; the Shape Fill above is hidden for Text.",
                s.textFill, new ZuiFillControl.Options().WithWidth(190f).WithGrow(2.2f)));

            box.Add(Z.MicroSlider("Border px", s.textBorderWidth, 0f, 4f,
                "Letter outline width in screen pixels (0 = no border). Drawn as an SDF band just inside each "
                + "glyph edge, coloured from the Border fill below.",
                v => Dirty(() => s.textBorderWidth = v), 150f, showValue: true));
            box.Add(FillRow("Border",
                "The letter outline fill, sampled the same way as the Fill (solid / gradient / texture). A single "
                + "colour reads as a solid outline.",
                s.textBorder, new ZuiFillControl.Options().WithWidth(190f).WithGrow(2.2f)));

            box.Add(Z.Toggle("Solid",
                "Extrude each letter into a 3D box (a lit front face + darker extrusion sides). Off = a flat 2D "
                + "letter plane.",
                s.textSolid, v => { Dirty(() => s.textSolid = v); RebuildShape(); }));
            if (s.textSolid)
                box.Add(Z.MicroSlider("Depth", s.textDepth, 0.05f, 1f,
                    "Extrusion depth as a fraction of the character size — how deep the 3D letter boxes are.",
                    v => Dirty(() => s.textDepth = v), 150f, showValue: true));

            shapeBody.Add(box);
        }

        // ── Swarm ──────────────────────────────────────────────────────────────────
        // The section is a stable header + a body container we clear/refill on every toggle/mode/kind
        // change, so the conditional controls appear/disappear without rebuilding the whole window.
        VisualElement swarmBody;
        static readonly string[] SwarmModeLabels = { "Area", "Path" };
        static readonly string[] SwarmOrientLabels = { "None", "Outward", "Tangent" };
        static readonly string[] SwarmTimingLabels = { "Window", "Frames" };

        void BuildSwarm(VisualElement root, PyrePlusSpec s)
        {
            var swarmSection = Z.Section("Swarm", "Place many particles in a shape instead of one centred particle.");
            swarmBody = new VisualElement();
            swarmSection.Add(swarmBody);
            root.Add(swarmSection);
            RebuildSwarm();
        }

        void RebuildSwarm()
        {
            var s = SelLayer;   // every Swarm control now edits the SELECTED layer's fields
            if (s == null || swarmBody == null) { swarmBody?.Clear(); return; }
            swarmBody.Clear();

            // The single gate: off ⇒ exactly one centred particle (Shape alone); nothing else shown. For the Text
            // form the count is the STRING LENGTH, so its Count field is hidden below.
            swarmBody.Add(Z.Toggle("Swarm",
                "Off = one centred particle (the Shape section alone; Text = one centred line). On = Count particles "
                + "placed in a shape. For the Text form the particle count is the number of characters, so Count is "
                + "hidden — each letter rides one swarm position.",
                s.swarmEnabled, v => { Dirty(() => s.swarmEnabled = v); RebuildSwarm(); }));
            if (!s.swarmEnabled) return;

            // Dual spawn-path visualisation (P4): two independent overlay toggles — the authored Shape (outline +
            // drag handle + numbered spawn dots) and the objective spawner Trace (the canonical spine). Both are
            // cosmetic preview aids on the SPEC (never baked; they never re-render the frames → DirtyRepaintOnly),
            // and both may be on at once. Neither on = no overlay at all. (Spec-level, not per-layer: the overlay
            // is a single global aid that follows whichever layer is selected.)
            swarmBody.Add(WrapRow(
                Z.Toggle("Show shape",
                    "Draw the authored spawn shape — its outline, the drag handle and a numbered dot at every "
                    + "particle's actual spawn point.",
                    spec.previewShowShape, v => DirtyRepaintOnly(() => spec.previewShowShape = v)),
                Z.Toggle("Show trace",
                    "Draw the objective spawner trace — the canonical amber path the spawn point sweeps through "
                    + "space over the whole timeline, under the spawn dots.",
                    spec.previewShowTrace, v => DirtyRepaintOnly(() => spec.previewShowTrace = v))));

            float half = Mathf.Max(1f, spec.canvasSize * 0.5f);

            // Count + Particle life — both apply whichever Timing mode is chosen — packed to reflow. Text hides
            // Count (the string length rules the particle count) but keeps Particle life.
            var baseRow = new List<VisualElement>();
            if (s.shapeForm != ShapeForm.Text)
                baseRow.Add(Z.Field("Count", "How many particles the swarm places (at least 2).",
                    Z.Int(s.swarmCount, "How many particles the swarm places (at least 2).",
                        v => Dirty(() => s.swarmCount = Mathf.Max(2, v)), 60f)));
            baseRow.Add(Z.MicroSlider("Particle life", s.swarmParticleLife, 0.05f, 1f,
                "How long each particle lives, as a fraction of the timeline. Its colour/alpha/size envelopes "
                + "always play over ITS OWN life, not the timeline.",
                v => Dirty(() => s.swarmParticleLife = v), 150f, showValue: true));
            swarmBody.Add(WrapRow(baseRow.ToArray()));

            // Timing mode (G3): Window spreads the spawns across a fraction of the timeline; Frames spawns the first
            // particle on a chosen frame, then one more every N frames. Rebuild on change so the mode's own rows
            // swap in; the composed tooltip re-reads the current mode (house rule: no if-lists in a tooltip).
            swarmBody.Add(Z.Field("Timing", SwarmTimingTooltip(s),
                Z.Segmented((int)s.swarmTiming, SwarmTimingLabels, SwarmTimingTooltip(s),
                    v => { Dirty(() => s.swarmTiming = (SwarmTiming)v); RebuildSwarm(); })));

            if (s.swarmTiming == SwarmTiming.Window)
            {
                swarmBody.Add(Z.MicroSlider("Spawn window", s.swarmSpawnWindow, 0f, 1f,
                    "The slice of the timeline during which new particles appear (0 = all at frame 1, "
                    + "1 = spawning continues to the last frame). WHEN each one appears inside the window is set "
                    + "by Spawn timing.",
                    v => Dirty(() => s.swarmSpawnWindow = v), 150f, showValue: true));

                // WHEN each particle spawns inside the window — the particle-number → spawn-moment remap.
                swarmBody.Add(Val("Spawn timing",
                    "Remaps WHEN each particle spawns inside the Spawn window. The X axis is WHICH particle "
                    + "(0 = the first spawned, 1 = the last); the value is WHEN it spawns (0 = the window's start, "
                    + "1 = its end). Linear = evenly spread (the default); ease it for a burst then a trickle; a flat "
                    + "Static value spawns them all together at that moment; MinMax gives every particle a random moment.",
                    s.swarmSpawnTiming, 0f, 1f));
            }
            else
            {
                // FrameStep: first frame + step, both 0-based frame indexes (the transport readout shows frames
                // 1-based). Packed to reflow; each clamps at 0 (its [Min(0)] spec attribute).
                string firstTip = "The frame the FIRST particle spawns on. 0-based — frame 0 is the very first frame "
                    + "(the transport readout shows it as \"frame 1\").";
                string stepTip = "Frames between each following spawn (0-based step). 0 = every particle spawns "
                    + "together on the First frame; 2 = one new particle every 2 frames until the Count is filled.";
                swarmBody.Add(WrapRow(
                    Z.Field("First frame", firstTip,
                        Z.Int(s.swarmFirstFrame, firstTip, v => Dirty(() => s.swarmFirstFrame = Mathf.Max(0, v)), 60f)),
                    Z.Field("Every N frames", stepTip,
                        Z.Int(s.swarmFrameStep, stepTip, v => Dirty(() => s.swarmFrameStep = Mathf.Max(0, v)), 60f))));
            }

            // Placement geometry: mode (Area vs Path) + the shape kind, packed together.
            string modeTip = "Area = particles fill the shape's interior; Path = particles ride along its outline.";
            var kindChoices = new List<string> { "Circle", "Triangle", "Square", "Pentagon", "Hexagon" };
            if (s.swarmSpawnMode == SwarmSpawnMode.Path) kindChoices.Add("Custom");   // Custom is Path-only
            string kindTip = "The swarm's outline — a regular polygon by side count (Circle = ∞ sides)"
                + (s.swarmSpawnMode == SwarmSpawnMode.Path ? ", or a hand-drawn Custom path." : ".");

            swarmBody.Add(WrapRow(
                Z.Field("Mode", modeTip,
                    Z.Segmented((int)s.swarmSpawnMode, SwarmModeLabels, modeTip, v =>
                    {
                        Dirty(() =>
                        {
                            s.swarmSpawnMode = (SwarmSpawnMode)v;
                            // Custom has no meaning in Area — snap it back to Circle so data + renderer agree.
                            if (s.swarmSpawnMode == SwarmSpawnMode.Area && s.swarmShapeKind == SwarmShapeKind.Custom)
                                s.swarmShapeKind = SwarmShapeKind.Circle;
                        });
                        RebuildSwarm();
                    })),
                Z.Field("Shape", kindTip,
                    Z.Dropdown((int)s.swarmShapeKind, kindChoices, kindTip,
                        v => { Dirty(() => s.swarmShapeKind = (SwarmShapeKind)v); RebuildSwarm(); }, 120f))));

            // Per-particle FACING as each is placed (S1). Rebuild on change so the composed tooltip re-reads the
            // current mode (Tangent means something different in Area vs Path).
            swarmBody.Add(Z.Field("Orient", SwarmOrientTooltip(s),
                Z.Segmented((int)s.swarmOrient, SwarmOrientLabels, SwarmOrientTooltip(s),
                    v => { Dirty(() => s.swarmOrient = (SwarmOrient)v); RebuildSwarm(); })));

            // Path-only: the progress envelope, sampled per-particle at its OWN spawn frame → a trail.
            if (s.swarmSpawnMode == SwarmSpawnMode.Path)
            {
                swarmBody.Add(Val("Spawn travel",
                    "Where the spawn point sits along the outline at each moment of the timeline (0 = start, "
                    + "1 = once around; values above 1 = more laps on closed shapes). Each particle LOCKS its spot "
                    + "at the moment it spawns — ease/hold/rewind this curve to cluster, stall or retrace the trail.",
                    s.swarmProgress, 0f, 8f));

                // Custom-only: the hand-drawn path — paired X/Y envelopes over progress, canvas-pixel offsets.
                if (s.swarmShapeKind == SwarmShapeKind.Custom)
                    swarmBody.Add(Val2D("Path",
                        "The hand-drawn path: numbered points in XY canvas-pixel offsets from the shape centre. "
                        + "Point order is progress around the path; each particle reads its spot by its own "
                        + "spawn-frame Progress.",
                        s.swarmCustomX, s.swarmCustomY,
                        new ZuiValue2DControl.Options()
                            .WithRange(-half, half, -half, half)
                            .WithDefault(Vector2.zero)
                            .Expanded()));

                // Even-path spacing (B1): spread the string EVENLY along the outline by index; Spawn travel then
                // rides the whole evenly-spaced string along the path together. Spread only matters when even, so
                // it's hidden until then (toggling rebuilds this section).
                var evenRow = new List<VisualElement>
                {
                    Z.Toggle("Even spacing",
                        "Spread the particles EVENLY along the outline (by index) instead of each sampling Spawn "
                        + "travel on its own. Spawn travel then rides the whole evenly-spaced string along the "
                        + "path together — ideal for readable text or a comet chain on a path.",
                        s.swarmEvenPath, v => { Dirty(() => s.swarmEvenPath = v); RebuildSwarm(); }),
                };
                if (s.swarmEvenPath)
                    evenRow.Add(Z.MicroSlider("Spread", s.swarmPathSpread, 0f, 1f,
                        "How much of the outline the evenly-spaced string covers (1 = the whole path start-to-end; "
                        + "smaller packs the particles into a shorter arc).",
                        v => Dirty(() => s.swarmPathSpread = v), 150f, showValue: true));
                swarmBody.Add(WrapRow(evenRow.ToArray()));
            }

            // Per-particle SIZE by index (S1) + shared DEATH point (S1).
            swarmBody.Add(Val("Scale by index",
                "Multiplies each particle's size by a factor read from its index (0 = first, 1 = last). Static 1 = "
                + "every particle full size; a Curve tapers the swarm (ends-vs-middle, centre-vs-edge — author it "
                + "freely, Bars-style); MinMax gives each particle a random size.",
                s.swarmScaleByIndex, 0f, 3f));
            swarmBody.Add(Z.Toggle("Die together",
                "All particles fade out at the SAME timeline moment (spawn-window end + particle life) instead of "
                + "each dying one particle-life after its own spawn — a burst that vanishes as one.",
                s.swarmDieTogether, v => Dirty(() => s.swarmDieTogether = v)));

            // Shared shape transform — every field a per-spawn snapshot (see the box tooltip).
            var xform = Z.BoxKeyed("Transform",
                "Offset, size, rotation and pseudo-3D tilt of the whole shape. Every field is a per-spawn "
                + "SNAPSHOT: each particle reads it at its OWN spawn moment and keeps that value for life. "
                + "Animating a field therefore does NOT move particles already placed — it spreads a TRAIL of new "
                + "spawns along the curve (rotate past 360, or travel past once-around, for several laps).",
                "pyreplus.transform");
            xform.Add(Val2D("Offset",
                "Shape-centre offset in canvas pixels — drag to move the whole shape off the origin. Animating it "
                + "does NOT slide placed particles; each takes the offset at its own spawn moment, leaving a trail.",
                s.shapeOffsetX, s.shapeOffsetY,
                new ZuiValue2DControl.Options().WithRange(-half, half, -half, half).WithDefault(Vector2.zero)));
            xform.Add(WrapRow(
                Val("Scale (px)", "The shape's radius in canvas pixels. Animating this does NOT resize placed "
                    + "particles — each takes the radius at its own spawn moment, so a growing curve leaves a "
                    + "trail of expanding rings.", s.shapeScale, 0f, 64f),
                Z.Field("Snap", "Round the evaluated scale to the nearest multiple of this, so placements land on "
                    + "fixed radii. 0 = off.",
                    Z.Float(s.shapeScaleSnap,
                        "Round the evaluated scale to the nearest multiple of this (0 = off).",
                        v => Dirty(() => s.shapeScaleSnap = Mathf.Max(0f, v)), 50f))));
            xform.Add(Val("Rotation °",
                "Spin the whole shape in the canvas plane, in degrees. Animating this does NOT spin placed "
                + "particles — each particle takes the value at its own spawn moment, so a rising curve spreads "
                + "spawns around the shape (several laps if the curve goes past 360).",
                s.shapeRotation, -1440f, 1440f));
            xform.Add(Val2D("Tilt °",
                "Pseudo-3D tilt of the whole shape, in degrees: drag X to yaw (turn left/right), Y to pitch "
                + "(tip up/down). Nearer parts of the tilted shape render bigger and brighter. Animating it does "
                + "NOT re-tilt placed particles; each takes the tilt at its own spawn moment.",
                s.shapeYaw, s.shapePitch,
                new ZuiValue2DControl.Options().WithRange(-1440f, 1440f, -1440f, 1440f)
                    .WithDefault(Vector2.zero).WithAxisLabels("Yaw", "Pitch")));
            swarmBody.Add(xform);
        }

        // ── helpers ──────────────────────────────────────────────────────────────
        static VisualElement WrapRow(params VisualElement[] kids)
        {
            var r = Z.Row(kids); r.style.flexWrap = Wrap.Wrap; return r;
        }

        VisualElement Val(string label, string tooltip, ZUIValue v, float lo, float hi)
        {
            var o = new ZuiValueControl.Options
            {
                absMin = lo, absMax = hi,
                hideCurveTiming = true, hideCurveRange = true, hideLiveReadout = true,
                controlWidth = 170f, grow = true,
            };
            return Z.Value(label, v, o, tooltip, () => MarkDirty(), () => Undo.RecordObject(spec, "Edit Pyre Plus"));
        }

        // 2D analog of Val — an animatable XY pair, same Undo-record + preview-dirty wiring.
        VisualElement Val2D(string label, string tooltip, ZUIValue x, ZUIValue y, ZuiValue2DControl.Options o)
            => Z.Value2D(label, x, y, o, tooltip, () => MarkDirty(), () => Undo.RecordObject(spec, "Edit Pyre Plus"));

        // A Z.Fill row (ZuiFill editor) wired to the same Undo/dirty/preview contract as Val: record the asset once
        // per gesture (onBeforeMutate), then dirty + repaint (onChanged). The control mutates the ZuiFill instance
        // directly, so there's no explicit setter.
        ZuiFillControl FillRow(string label, string tooltip, ZuiFill fill, ZuiFillControl.Options opt = null)
            => Z.Fill(label, fill, tooltip,
                () => { if (spec != null) EditorUtility.SetDirty(spec); MarkDirty(); },
                () => { if (spec != null) Undo.RecordObject(spec, "Edit Pyre Plus"); },
                opt);

        // Compact Z.Fill for the Solid box's slot fills (spec / line / edge / inner), packed beside their sliders.
        ZuiFillControl SlotFill(string label, string tooltip, ZuiFill fill)
            => FillRow(label, tooltip, fill, new ZuiFillControl.Options().WithWidth(96f));

        // The Solid box tooltip, naming the CURRENT solid form (the box rebuilds on form change).
        static string SolidBoxTooltip(ShapeForm f)
        {
            const string shared = "A true-3D form lit per-pixel by one key light, with light-catching hard edge lines "
                + "and two glows (a rim halo + an interior glow). Its material colour is the shared Fill above (a blue "
                + "gradient = a sapphire); its base size is the shared Size. ";
            switch (f)
            {
                case ShapeForm.Gem:     return shared + "This form: an octahedral crystal.";
                case ShapeForm.Box:     return shared + "This form: a cuboid.";
                case ShapeForm.Pyramid: return shared + "This form: a square pyramid.";
                case ShapeForm.Can:     return shared + "This form: a cylinder.";
                case ShapeForm.Orb:     return shared + "This form: a sphere (no geometry rows — a ball needs none).";
                case ShapeForm.Ring:    return shared + "This form: a flat two-sided tilted annulus (a Saturn ring).";
                default:                return shared;
            }
        }

        // The 3D-solid forms — they share BuildSolidBox and its Turn/Tilt/Roll rotation trio (and hide the Advanced
        // Spin row, since Turn IS that field). Disc / Crescent / Sparkle / Sprite / Text are NOT solid forms.
        static bool IsSolidForm(ShapeForm f) =>
            f == ShapeForm.Gem || f == ShapeForm.Box || f == ShapeForm.Pyramid ||
            f == ShapeForm.Can || f == ShapeForm.Orb || f == ShapeForm.Ring;

        // The Swarm Timing tooltip, composed for the CURRENT mode (the swarm rebuilds on change), so it names only
        // the mode the user is in (house rule: no if-lists in a tooltip).
        static string SwarmTimingTooltip(PyrePlusLayer s)
        {
            const string common = "How the swarm's spawns are spread across the timeline. ";
            if (s.swarmTiming == SwarmTiming.FrameStep)
                return common + "Frames: the first particle spawns on First frame, then one more every N frames until "
                     + "the Count is filled (N = 0 spawns them all on that one frame). Frame indexes are 0-based; the "
                     + "transport readout shows frames 1-based.";
            return common + "Window: spawns spread across a fraction of the timeline (Spawn window), with Spawn timing "
                 + "remapping which particle lands when inside it.";
        }

        // The Swarm Orient tooltip, composed for the CURRENT orient + mode (the swarm rebuilds on either change),
        // so it never lists branches the user isn't in.
        static string SwarmOrientTooltip(PyrePlusLayer s)
        {
            const string common = "Turns each particle to face a direction as it's placed — the renderer folds it "
                + "into the form's own rotation (Streak forward, Sprite/Disc spin, Text/solid roll). ";
            switch (s.swarmOrient)
            {
                case SwarmOrient.Outward:
                    return common + "Outward: each particle faces away from the shape centre (a Streak points "
                         + "outward; letters/solids roll to match).";
                case SwarmOrient.PathTangent:
                    return common + (s.swarmSpawnMode == SwarmSpawnMode.Path
                        ? "Tangent: each particle faces ALONG the outline it rides — readable text follows the "
                          + "path and Streaks trail along it. Pair with Even spacing for a clean string."
                        : "Tangent: only meaningful in Path mode; in Area it falls back to Outward.");
                default:
                    return common + "None: particles keep their own orientation (no turning). Outward faces them "
                         + "away from the centre; Tangent (Path mode) faces them along the outline.";
            }
        }

        // ── per-form tooltip composers (rebuilt on every form change, so each branches to the CURRENT form) ──
        static string FillTooltip(ShapeForm f)
        {
            const string modes = " Solid = one flat colour; Over life = a gradient across the particle's life; "
                + "Linear / Radial / Noise = a spatial fill across the shape. Every mode is alpha-capable (⋯ to switch).";
            if (IsSolidForm(f))
                return "The solid's material fill — a blue gradient reads as a sapphire." + modes;
            if (f == ShapeForm.Sprite)
                return "Tints the stamped sprite (multiplied over its colours), sampled at the particle centre only — "
                     + "a spatial fill has no effect on a sprite tint. Turn Tint off for the sprite's raw colours." + modes;
            return "The particle's colour (0 = birth, 1 = death)." + modes;
        }

        static string SizeTooltip(ShapeForm f)
        {
            if (IsSolidForm(f))
                return "Radius in pixels over the particle's own life — the base size R that Aspect / Depth (or the "
                     + "Gem's Crown / Pavilion) scale from.";
            if (f == ShapeForm.Text)
                return "The character HEIGHT in pixels over the particle's own life.";
            if (f == ShapeForm.Star)
                return "The star's TIP radius in pixels over the particle's own life — the arms reach out to it "
                     + "(the valleys sit at Length inside).";
            return "Radius in pixels over the particle's own life.";
        }

        // Spin only shows for the 2D forms + Text (solids edit it as Turn), so this branches only those cases.
        static string SpinTooltip(ShapeForm f)
        {
            switch (f)
            {
                case ShapeForm.Crescent:
                    return "Degrees the crescent rotates over its own life — turns the whole crescent, on top of its "
                         + "own bite Angle.";
                case ShapeForm.Sparkle:
                    return "Degrees the sparkle field rotates in place over its own life — an even field is radially "
                         + "symmetric, so add a geometry/texture Modifier for the spin to read.";
                case ShapeForm.Sprite:
                    return "Degrees the stamped sprite rotates over its own life.";
                case ShapeForm.Streak:
                    return "Degrees the streak turns about its root over its own life — rotates its forward "
                         + "direction, ADDED on top of any Swarm Orient facing.";
                case ShapeForm.Star:
                    return "Degrees the star spins in place over its own life — unlike a disc, a star isn't "
                         + "radially symmetric, so its arms visibly turn.";
                case ShapeForm.Text:
                    return "Degrees each letter yaws about its OWN centre over its life (its 3D letter-box turns to "
                         + "face the light); paired with the letters' tilt for the extruded look.";
                default:   // Disc
                    return "Degrees the disc's pixels spin in place over its own life — a plain disc is radially "
                         + "symmetric so it shows little (add a geometry/texture Modifier for the spin to read).";
            }
        }

        static string TurnTooltip(ShapeForm f)
        {
            const string common = "Rotate around the VERTICAL axis, like a turntable (yaw), over the particle's own life. ";
            if (f == ShapeForm.Orb)
                return common + "For the Orb it rolls the lit hotspot left/right around the ball (the silhouette never changes).";
            if (f == ShapeForm.Ring)
                return common + "For the Ring it swings the ellipse — turning the ring edge-on along the horizontal axis.";
            return common + "Sweeps the solid's facets past the light.";
        }

        static string TiltTooltip(ShapeForm f)
        {
            const string common = "Lean the top toward or away from you (about the HORIZONTAL axis), over the particle's own life. ";
            if (f == ShapeForm.Orb)
                return common + "For the Orb it rolls the lit hotspot up/down across the ball (the silhouette never changes).";
            if (f == ShapeForm.Ring)
                return common + "For the Ring it opens/closes the ellipse — 0° face-on (a full circle), ±90° edge-on (a sliver).";
            return common + "Tips the solid so different facets catch the light.";
        }

        // Roll is hidden for the Ring (geometrically inert there), so this only ever branches Orb vs the facet solids.
        static string RollTooltip(ShapeForm f)
        {
            const string common = "Rotate the solid flat against the screen (about the axis pointing at you), over the particle's own life. ";
            if (f == ShapeForm.Orb)
                return common + "For the Orb it rolls the lit hotspot around the centre of the ball.";
            return common + "Spins the whole silhouette in the screen plane.";
        }

        void Dirty(System.Action apply)
        {
            if (spec == null) return;
            Undo.RecordObject(spec, "Edit Pyre Plus");
            apply();
            EditorUtility.SetDirty(spec);
            MarkDirty();
        }

        // A cosmetic edit that only changes how the preview is LAID OUT / drawn (not the rendered frames): record
        // Undo + SetDirty + repaint, but do NOT set previewDirty. Used by the filmstrip's Tile-px slider so dragging
        // it re-lays-out the existing tile textures instead of re-rendering every frame (the strip cache is keyed
        // to previewDirty + frameCount/canvas only — tile size is neither).
        void DirtyRepaintOnly(System.Action apply)
        {
            if (spec == null) return;
            Undo.RecordObject(spec, "Edit Pyre Plus");
            apply();
            EditorUtility.SetDirty(spec);
            preview?.MarkDirtyRepaint();
        }

        // DrawPreview + the Swarm authoring overlay live in PyrePlusWindow.Preview.cs.
    }
}
