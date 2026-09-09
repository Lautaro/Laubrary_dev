// TapestryWindow — the authoring window for Tapestry: a stack of procedural tileable-texture generator
// layers, laid out the way Lathe/Pyre are (a layer list, per-layer plug-in Generator + modifier stack, a
// live preview), but producing a single flat 2D texture instead of 3D geometry or an animated raster. Own
// tool rather than a Lathe addition — see the CLAUDE.md conversation this was scoped from: this is a
// fundamentally 2D generation model, sharing real pieces (the bake-to-Texture2D pattern, the SDF math) but
// not Lathe's solid-stack/3D-module architecture.
using System;
using System.IO;
using Laubrary.AssetKit.Editor;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Tapestry.Editor
{
    public partial class TapestryWindow : ZuiAssetWindow<TapestrySpec>
    {
        [MenuItem("Laubrary/Tapestry")]
        public static void Open() => GetWindow<TapestryWindow>("Tapestry");

        TapestrySpec spec => Current;
        protected override string TypeLabel => "Tapestry";
        protected override string NewAssetName => "New Tapestry";
        protected override string DefaultFolder => "Assets/Tapestry";

        // ── preview state (all cosmetic view-only — never baked, never undo-tracked) ────────────────────
        IMGUIContainer preview;
        Texture2D bakedTex;
        bool bakeDirty = true;
        bool tiledPreview = true;
        float previewRotation = 0f;
        float previewZoom = 1f;

        // ── transport (only matters for layers with Animate Transform on — mirrors Lathe's turntable) ──
        double lastTime;
        float acc;
        bool playing = false;
        int frame;
        Button playButton;
        SliderInt scrubSlider;
        Label frameReadout;
        VisualElement transportHost;

        protected override void OnEnable()
        {
            base.OnEnable();
            lastTime = EditorApplication.timeSinceStartup;
            EditorApplication.update += Tick;
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            EditorApplication.update -= Tick;
            if (bakedTex != null) UnityEngine.Object.DestroyImmediate(bakedTex);
            bakedTex = null;
        }

        protected override void OnAssetChanged()
        {
            bakeDirty = true;
            frame = 0;
        }

        void Tick()
        {
            if (this == null || spec == null || !playing) return;
            double now = EditorApplication.timeSinceStartup;
            float dt = Mathf.Clamp((float)(now - lastTime), 0f, 0.1f);
            lastTime = now;
            acc += dt * Mathf.Max(1f, spec.previewFps);
            bool advanced = false;
            while (acc >= 1f)
            {
                frame = (frame + 1) % Mathf.Max(1, spec.frameCount);
                acc -= 1f;
                advanced = true;
            }
            if (advanced) { bakeDirty = true; preview?.MarkDirtyRepaint(); RefreshTransportReadout(); }
        }

        // ── mutation helpers (Laubrary Undo rule: every dial edit is undoable) ──────────────────────────
        void Dirty(Action edit)
        {
            if (spec == null) return;
            Undo.RecordObject(spec, "Edit Tapestry");
            edit();
            EditorUtility.SetDirty(spec);
            bakeDirty = true;
            preview?.MarkDirtyRepaint();
        }

        // Cosmetic preview-only state (rotation/zoom/tiled/backdrop) — no Undo, no rebake, just a repaint.
        void DirtyRepaintOnly(Action edit)
        {
            if (spec == null) return;
            edit();
            EditorUtility.SetDirty(spec);
            preview?.MarkDirtyRepaint();
        }

        Texture2D GetBaked(TapestrySpec s)
        {
            if (bakeDirty || bakedTex == null)
            {
                if (bakedTex != null) UnityEngine.Object.DestroyImmediate(bakedTex);
                float animT = s.frameCount > 0 ? frame / (float)s.frameCount : 0f;
                bakedTex = TapestryCompositor.Bake(s, animT);
                bakeDirty = false;
            }
            return bakedTex;
        }

        // ── selected layer ───────────────────────────────────────────────────────────────────────────
        int layerSel
        {
            get => spec != null ? spec.previewLayerSel : 0;
            set
            {
                if (spec == null || spec.previewLayerSel == value) return;
                spec.previewLayerSel = value;
                EditorUtility.SetDirty(spec);
            }
        }

        TapestryLayer SelLayer
        {
            get
            {
                if (spec == null || spec.layers == null || spec.layers.Count == 0) return null;
                int clamped = Mathf.Clamp(spec.previewLayerSel, 0, spec.layers.Count - 1);
                if (clamped != spec.previewLayerSel) spec.previewLayerSel = clamped;
                return spec.layers[clamped];
            }
        }

        // ── layout ───────────────────────────────────────────────────────────────────────────────────
        protected override void BuildAsset(VisualElement root, TapestrySpec s)
        {
            root.style.flexGrow = 1f;
            root.style.minHeight = 0f;

            var left = new ScrollView(ScrollViewMode.Vertical);
            left.style.minWidth = 320f;
            var col = left.contentContainer;
            col.style.flexGrow = 1f;

            BuildCanvasBox(col, s);
            BuildLayersList(col, s);

            var sel = SelLayer;
            if (sel != null)
            {
                col.Add(BuildLayerBlendBox(sel));
                col.Add(BuildLayerTransformBox(sel));
                BuildGeneratorBox(col, sel);
                BuildLayerModifiersBox(col, sel);
            }

            BuildGlobalModifiersBox(col, s);

            var rightPane = new VisualElement();
            rightPane.style.flexGrow = 1f;
            rightPane.style.minWidth = 260f;
            rightPane.style.minHeight = 0f;

            preview = new IMGUIContainer(() => DrawPreview(s));
            preview.style.flexGrow = 1f;
            preview.style.minHeight = 220f;
            preview.AddToClassList("zui-stage");
            preview.tooltip = "Scroll to zoom.";
            rightPane.Add(preview);

            var chrome = new VisualElement();
            chrome.style.flexShrink = 0f;
            BuildTransport(chrome, s);
            chrome.Add(Z.HGroup(
                Z.Toggle("Tiled", "Show a 3x3 tiled repeat of the texture, to check it seams cleanly.",
                    tiledPreview, v => DirtyRepaintOnly(() => tiledPreview = v)),
                Z.Button("Export Texture…", "Save the CURRENT frame's bake as a single PNG on disk.", () => ExportTexture(s)),
                Z.Button("Export Strip…", "Bake every frame of the animation loop and save them as one PNG "
                    + "strip (one tile per frame) — only useful if a layer has Animate Transform on; otherwise "
                    + "every tile is identical.", () => ExportStrip(s))));
            chrome.Add(Z.HGroup(
                Z.MicroSlider("Rotate", previewRotation, 0f, 360f, "Rotates the preview only, to assess the "
                    + "pattern from other angles — never baked into the texture itself.",
                    v => DirtyRepaintOnly(() => previewRotation = v), 150f, showValue: true),
                Z.MicroSlider("Zoom", previewZoom, 0.25f, 4f, "Preview-only zoom.",
                    v => DirtyRepaintOnly(() => previewZoom = v), 150f, showValue: true)));
            chrome.Add(Z.Field("Background", "The preview's clear colour — cosmetic, never baked.",
                Z.Color(s.previewBackground, "The preview's clear colour.",
                    v => DirtyRepaintOnly(() => s.previewBackground = v), 110f)));
            rightPane.Add(chrome);

            root.Add(Z.Split("tapestry.split", 340f, left, rightPane));
        }

        VisualElement BuildLayerBlendBox(TapestryLayer layer)
        {
            var box = Z.Section("Blend", "How this layer combines with everything below it in the stack.",
                "tapestry.blend", icon: "palette");
            box.Add(Z.Field("Mode", "Blend mode.",
                Z.MiniRadio((int)layer.blendMode, Enum.GetNames(typeof(TapestryBlendMode)),
                    "How this layer's colour combines with the layers below it.",
                    i => Dirty(() => layer.blendMode = (TapestryBlendMode)i))));
            box.Add(Z.MicroSlider("Opacity", layer.opacity, 0f, 1f, "How strongly this layer shows through.",
                v => Dirty(() => layer.opacity = v), 150f, showValue: true));
            return box;
        }

        VisualElement BuildLayerTransformBox(TapestryLayer layer)
        {
            var box = Z.Section("Transform", "This layer's own position/rotation/scale, applied to its "
                + "rendered content before it's blended into the stack.", "tapestry.transform", icon: "move");
            box.Add(Z.Field("Position", "UV offset of this layer's own content.", Z.Row(
                Z.Float(layer.position.x, "Position (U).", v => Dirty(() => layer.position = new Vector2(v, layer.position.y)), 60f),
                Z.Float(layer.position.y, "Position (V).", v => Dirty(() => layer.position = new Vector2(layer.position.x, v)), 60f))));
            box.Add(Z.HGroup(
                Z.MicroSlider("Rotation", layer.rotation, 0f, 360f, "This layer's own rotation, in degrees.",
                    v => Dirty(() => layer.rotation = v), 150f, showValue: true)));
            box.Add(Z.Field("Scale", "Non-uniform scale of this layer's own content (1 = unchanged).", Z.Row(
                Z.Float(layer.scale.x, "Scale (X).", v => Dirty(() => layer.scale = new Vector2(v, layer.scale.y)), 60f),
                Z.Float(layer.scale.y, "Scale (Y).", v => Dirty(() => layer.scale = new Vector2(layer.scale.x, v)), 60f))));

            box.Add(Z.Toggle("Animate Transform", "Drifts this layer's position/rotation/scale over the "
                + "animation loop below — an animated texture. This can break tileability (rotation and "
                + "scale especially) — that's an effect you're opting into, not a bug.",
                layer.animateTransform, v => { Dirty(() => layer.animateTransform = v); Rebuild(); }));
            if (layer.animateTransform)
            {
                box.Add(Z.Field("Position Speed", "UV units drifted per full loop.", Z.Row(
                    Z.Float(layer.positionSpeed.x, "Speed (U).", v => Dirty(() => layer.positionSpeed = new Vector2(v, layer.positionSpeed.y)), 60f),
                    Z.Float(layer.positionSpeed.y, "Speed (V).", v => Dirty(() => layer.positionSpeed = new Vector2(layer.positionSpeed.x, v)), 60f))));
                box.Add(Z.MicroSlider("Rotation Turns", layer.rotationTurns, -4f, 4f,
                    "Full 360° turns drifted per loop — an integer value (1, 2…) loops back to the same angle cleanly.",
                    v => Dirty(() => layer.rotationTurns = v), 170f, showValue: true));
                box.Add(Z.Field("Scale Speed", "Scale delta drifted per loop (added to the base Scale above).", Z.Row(
                    Z.Float(layer.scaleSpeed.x, "Speed (X).", v => Dirty(() => layer.scaleSpeed = new Vector2(v, layer.scaleSpeed.y)), 60f),
                    Z.Float(layer.scaleSpeed.y, "Speed (Y).", v => Dirty(() => layer.scaleSpeed = new Vector2(layer.scaleSpeed.x, v)), 60f))));
            }
            return box;
        }

        void BuildCanvasBox(VisualElement root, TapestrySpec s)
        {
            var box = Z.Section("Canvas", "Output settings for the whole texture.", "tapestry.canvas", icon: "frame-corners");
            box.Add(Z.HGroup(
                Z.MicroSlider("Resolution", s.resolution, 16f, 512f, "Square output resolution in pixels.",
                    v => Dirty(() => s.resolution = Mathf.Clamp(Mathf.RoundToInt(v), 16, 512)), 160f, showValue: true, decimals: 0),
                Z.Field("Seed", "Available to any generator/modifier that wants deterministic randomness.",
                    Z.Int(s.seed, "Random seed.", v => Dirty(() => s.seed = v), 70f))));
            box.Add(Z.HGroup(
                Z.MicroSlider("Loop Frames", s.frameCount, 1f, 120f,
                    "Only matters to a layer with Animate Transform on — how many steps its drift is divided into.",
                    v => { Dirty(() => s.frameCount = Mathf.Clamp(Mathf.RoundToInt(v), 1, 120)); RefreshTransportReadout(); },
                    170f, showValue: true, decimals: 0),
                Z.MicroSlider("FPS", s.previewFps, 1f, 60f, "Preview playback speed.",
                    v => Dirty(() => s.previewFps = v), 120f, showValue: true)));
            root.Add(box);
        }

        // ── transport ────────────────────────────────────────────────────────────────
        void BuildTransport(VisualElement root, TapestrySpec s)
        {
            transportHost = new VisualElement();
            root.Add(transportHost);
            RebuildTransport(s);
        }

        void RebuildTransport(TapestrySpec s)
        {
            if (transportHost == null) return;
            transportHost.Clear();
            playButton = Z.Button(playing ? "❚❚ Pause" : "▶ Play", "Play or pause the animation loop — only "
                + "visible effect if a layer has Animate Transform on.", () =>
            {
                playing = !playing;
                lastTime = EditorApplication.timeSinceStartup;
                acc = 0f;
                if (playButton != null) playButton.text = playing ? "❚❚ Pause" : "▶ Play";
            }).W(88f);
            transportHost.Add(playButton);

            int fcHigh = Mathf.Max(1, s.frameCount);
            scrubSlider = Z.SliderInt(Mathf.Clamp(frame, 0, fcHigh - 1) + 1, 1, fcHigh,
                "Scrub to an exact frame — dragging pauses playback.",
                v =>
                {
                    frame = Mathf.Clamp(v - 1, 0, Mathf.Max(0, s.frameCount - 1));
                    playing = false;
                    if (playButton != null) playButton.text = "▶ Play";
                    bakeDirty = true;
                    preview?.MarkDirtyRepaint();
                    RefreshTransportReadout();
                }, 200f);
            transportHost.Add(Z.Field("Frame", "Scrub to an exact frame.", scrubSlider));

            frameReadout = Z.Text("", ZuiText.Subtle, "The frame currently shown / the loop's total frame count.");
            transportHost.Add(frameReadout);
            RefreshTransportReadout();
        }

        void RefreshTransportReadout()
        {
            if (spec == null || frameReadout == null) return;
            frameReadout.text = $"Frame {frame + 1} / {Mathf.Max(1, spec.frameCount)}";
        }

        void ExportTexture(TapestrySpec s)
        {
            var tex = GetBaked(s);
            if (tex == null) return;
            string path = EditorUtility.SaveFilePanel("Export Tapestry Texture", "", (s.name ?? "Tapestry") + ".png", "png");
            if (!string.IsNullOrEmpty(path))
            {
                File.WriteAllBytes(path, tex.EncodeToPNG());
                EditorUtility.RevealInFinder(path);
            }
        }

        void ExportStrip(TapestrySpec s)
        {
            var strip = TapestryBaker.BakeStrip(s);
            if (strip == null) return;
            string path = EditorUtility.SaveFilePanel("Export Tapestry Strip", "", (s.name ?? "Tapestry") + "_strip.png", "png");
            if (!string.IsNullOrEmpty(path))
            {
                File.WriteAllBytes(path, strip.EncodeToPNG());
                EditorUtility.RevealInFinder(path);
            }
            UnityEngine.Object.DestroyImmediate(strip);
        }
    }
}
