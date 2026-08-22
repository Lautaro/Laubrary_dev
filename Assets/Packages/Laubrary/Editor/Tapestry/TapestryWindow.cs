// TapestryWindow — the authoring window for Tapestry: a stack of procedural tileable-texture generator
// layers, laid out the way Lathe/PyrePlus are (a layer list, per-layer plug-in Generator + modifier stack, a
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

        protected override void OnDisable()
        {
            base.OnDisable();
            if (bakedTex != null) UnityEngine.Object.DestroyImmediate(bakedTex);
            bakedTex = null;
        }

        protected override void OnAssetChanged() => bakeDirty = true;

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
                bakedTex = TapestryCompositor.Bake(s);
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
            chrome.Add(Z.HGroup(
                Z.Toggle("Tiled", "Show a 3x3 tiled repeat of the texture, to check it seams cleanly.",
                    tiledPreview, v => DirtyRepaintOnly(() => tiledPreview = v)),
                Z.Button("Export Texture…", "Save the current bake as a PNG on disk.", () => ExportTexture(s))));
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

        void BuildCanvasBox(VisualElement root, TapestrySpec s)
        {
            var box = Z.Section("Canvas", "Output settings for the whole texture.", "tapestry.canvas", icon: "frame-corners");
            box.Add(Z.HGroup(
                Z.MicroSlider("Resolution", s.resolution, 16f, 512f, "Square output resolution in pixels.",
                    v => Dirty(() => s.resolution = Mathf.Clamp(Mathf.RoundToInt(v), 16, 512)), 160f, showValue: true, decimals: 0),
                Z.Field("Seed", "Available to any generator/modifier that wants deterministic randomness.",
                    Z.Int(s.seed, "Random seed.", v => Dirty(() => s.seed = v), 70f))));
            root.Add(box);
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
    }
}
