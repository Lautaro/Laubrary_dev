// PyreWindow.Layers — the layer list (reorder/enable/rename/dup/delete/library) and the selected
// layer's inspector, including every per-shape section. Part of the UI Toolkit port; see PyreWindow.cs.
using System.Collections.Generic;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Pyre.Editor
{
    public partial class PyreWindow
    {
        // ── layer list ─────────────────────────────────────────────────────────────────────
        void BuildLayerList(VisualElement root)
        {
            root.Add(Z.Text("Layers (back → front)", ZuiText.Section,
                "The blast's flat draw stack — earlier layers composite behind later ones."));

            var addRow = WrapRow(
                Z.EnumDropdown(addShape, "Which shape the next added layer starts as.", v => addShape = v, 110f),
                Z.Button("+ Add layer", "Append a new layer of the picked shape (undoable).", () =>
                {
                    Dial("Add layer", () =>
                    {
                        spec.layers.Add(Layer.Default(addShape));
                        layerSel = spec.layers.Count - 1;
                    });
                    RebuildLeft();
                }));
            var recallButton = Z.Button("Recall…", "Insert a layer saved in the layer library.", null);
            recallButton.clicked += () =>
            {
                var wb = recallButton.worldBound;
                UnityEditor.PopupWindow.Show(new Rect(wb.x, wb.y, wb.width, wb.height),
                    new PyreLayerLibraryPopup(PyreLayerLibrary.Load(), leftWidth - 24f, InsertLibraryLayer));
            };
            addRow.Add(recallButton);
            root.Add(addRow);

            var listHost = new VisualElement();
            root.Add(listHost);
            for (int li = 0; li < spec.layers.Count; li++)
                listHost.Add(BuildLayerRow(listHost, li));
        }

        VisualElement BuildLayerRow(VisualElement listHost, int li)
        {
            var layer = spec.layers[li];
            bool sel = li == layerSel;

            var row = new VisualElement();
            row.AddToClassList("zui-row");
            if (sel) row.style.backgroundColor = new Color(0.35f, 0.55f, 0.95f, 0.18f);

            var grip = Z.Text("≡", ZuiText.Body, "Drag to reorder this layer in the stack.");
            grip.style.unityFontStyleAndWeight = FontStyle.Bold;
            grip.style.width = 16f;
            ZuiReorder.MakeGrip(grip, row, listHost, (from, to) =>
            {
                Dial("Reorder layer", () =>
                {
                    var lay = spec.layers[from];
                    spec.layers.RemoveAt(from);
                    spec.layers.Insert(to, lay);
                    layerSel = to;
                });
                RebuildLeft();
            });
            row.Add(grip);

            var enabledToggle = Z.Toggle("", "Show or hide this layer in the render.", layer.enabled,
                v => Dial(v ? "Enable layer" : "Disable layer", () => layer.enabled = v));
            row.Add(enabledToggle);

            row.Add(Z.Button(sel ? "●" : "○", "Select this layer to edit it below.", () =>
            {
                layerSel = li;
                RebuildLeft();
            }).W(24f));

            var name = Z.TextInput(layer.name, "This layer's name — rename it right here.", v =>
            {
                Undo.RecordObject(spec, "Rename layer");
                layer.name = v;
                EditorUtility.SetDirty(spec);
            }, 0f);
            name.style.width = StyleKeyword.Auto;
            name.style.flexGrow = 1f;
            name.style.flexShrink = 1f;
            name.style.minWidth = 50f;
            name.AddToClassList("zui-audit-allow-stretch");   // the rulebook's name-field exception, made explicit
            name.RegisterCallback<PointerDownEvent>(_ => { if (layerSel != li) { layerSel = li; RebuildLeft(); } });
            row.Add(name);

            if (!layer.enabled) row.Add(Z.Text("off", ZuiText.Small, "This layer is currently hidden."));

            row.Add(Z.Button("★", "Save a copy of this layer to the layer library.", () =>
            {
                PyreLayerLibrary.Load().Add(layer, layer.name);
                ShowNotification(new GUIContent($"Saved “{layer.name}” to layer library"));
            }).W(24f));
            row.Add(Z.Button("Dup", "Duplicate this layer just after itself (undoable).", () =>
            {
                Dial("Duplicate layer", () =>
                {
                    var copy = layer.Clone();
                    copy.name += " copy";
                    spec.layers.Insert(li + 1, copy);
                    layerSel = li + 1;
                });
                RebuildLeft();
            }).W(40f));
            row.Add(Z.Button("X", "Delete this layer (undoable).", () =>
            {
                Dial("Remove layer", () =>
                {
                    spec.layers.RemoveAt(li);
                    layerSel = Mathf.Clamp(layerSel, 0, Mathf.Max(0, spec.layers.Count - 1));
                });
                RebuildLeft();
            }).W(22f));

            return row;
        }

        void InsertLibraryLayer(Layer layer)
        {
            if (spec == null || layer == null) return;
            Dial("Recall layer", () =>
            {
                int at = Mathf.Clamp(layerSel + 1, 0, spec.layers.Count);
                spec.layers.Insert(at, layer);
                layerSel = at;
            });
            RebuildLeft();
        }

        // ── selected layer inspector ────────────────────────────────────────────────────────
        void BuildSelectedLayer(VisualElement root)
        {
            if (layerSel < 0 || layerSel >= spec.layers.Count) return;
            var l = spec.layers[layerSel];
            float cs = spec.canvasSize;
            float half = cs * 0.5f;

            root.Add(Z.Text($"Layer — {l.name}", ZuiText.Section, "The selected layer's own dials."));
            if (!l.enabled)
                root.Add(Z.Help("This layer is disabled (its checkbox in the list above is unticked) — " +
                    "nothing you change down here will show up in the render until you re-enable it.",
                    HelpBoxMessageType.Warning));

            BuildShapePreview(root, l);

            root.Add(Z.MiniRadio((int)l.shape, ShapeLabels,
                "The layer's shape family — decides which controls appear below.",
                v => { Dial("Layer shape", () => l.shape = (LayerShape)v); RebuildLeft(); }));

            int fcMax = Mathf.Max(1, FrameCount - 1);
            root.Add(Z.Field("Life (frames)", "The frame window this layer exists in (start ↔ end frame of the bake).",
                Z.MinMax(Mathf.Clamp(l.startFrame, 0, fcMax), Mathf.Clamp(l.endFrame, 0, fcMax), 0f, fcMax,
                    "The frame window this layer exists in (start ↔ end frame of the bake).",
                    (lo, hi) =>
                    {
                        RecordSpec();
                        l.startFrame = Mathf.RoundToInt(lo);
                        l.endFrame = Mathf.Clamp(Mathf.RoundToInt(hi), l.startFrame, fcMax);
                        DirtySpec();
                    })));

            l.colorOverLife ??= Layer.DefaultColor(l.shape);
            l.alpha ??= Layer.DefaultAlpha();
            root.Add(GradientRow("Colour",
                l.shape == LayerShape.HeightBalls
                    ? "The cloud's ONE continuous ramp — its low end is a cold, un-energised ball (smoke), its high end a fully energised one (fire). Adding energy to a ball walks it up this ramp; withering walks it back down."
                    : "The layer's colour ramp (meaning depends on the colour mode below).",
                () => l.colorOverLife, g => l.colorOverLife = g));

            var shape = l.shape;
            if (shape == LayerShape.Disc || shape == LayerShape.Crescent)
            {
                root.Add(Z.MiniRadio((int)l.colorMode, ColorModeLabels,
                    "How the colour ramp maps onto each shape: over its lifetime, spatially (fill), scrolling (flow fill), or through a noise field.",
                    v => { Dial("Colour mode", () => l.colorMode = (ColorMode)v); RebuildLeft(); }));
                if (l.colorMode == ColorMode.NoiseFill)
                    BuildNoiseFillParams(root, l, half, perShape: !(l.scatterMode == ScatterMode.Rosing || l.scatterMode == ScatterMode.Ring) || !l.fuse);
                else if (l.colorMode != ColorMode.OverLife)
                {
                    root.Add(Z.Text("Gradient", ZuiText.Section, "How the spatial gradient sits and moves on each shape."));
                    root.Add(WrapRow(
                        PackedVal("Position", "Slides the gradient through each shape.", l.colorFlow, -2f, 2f, 0f),
                        PackedVal("Zoom", "Scales the gradient within each shape.", l.colorFlowZoom, 0.1f, 4f, 1f),
                        PackedVal2D("Offset", "Moves the gradient's core off-centre — an offset core + a bright→dark ramp reads as a 3D orb.",
                            l.gradientOffsetX, l.gradientOffsetY,
                            new ZuiValue2DControl.Options().WithRange(-1f, 1f, -1f, 1f).WithDefault(Vector2.zero))));
                }
            }

            if (shape == LayerShape.Bars || shape == LayerShape.MetaBlob || shape == LayerShape.HeightBalls)
                root.Add(ValRow("Alpha", "Opacity over this layer's life.", l.alpha, 0f, 1f,
                    allowMinMax: shape == LayerShape.Bars));

            if (shape == LayerShape.Bars) { BuildBarsSection(root, l, cs); BuildLayerModifierSections(root, l); return; }
            if (shape == LayerShape.MetaBlob) { BuildMetaBlobSection(root, l, half); BuildLayerModifierSections(root, l); return; }
            if (shape == LayerShape.HeightBalls) { BuildHeightBallsSection(root, l, half); BuildLayerModifierSections(root, l); return; }

            BuildScatterSection(root, l, half);

            switch (shape)
            {
                case LayerShape.Disc:
                    BuildDiscEdges(root, l);
                    if (l.scatterMode == ScatterMode.Ring || l.scatterMode == ScatterMode.Rosing)
                    {
                        root.Add(Z.Toggle("Fuse",
                            "Melts every shape in this layer into ONE gradient-shaded metaball field (like MetaBlob, but fed by this layer's own Ring/Rosing-placed discs) instead of compositing them independently.",
                            l.fuse, v => { Dial("Fuse", () => l.fuse = v); RebuildLeft(); }));
                        if (l.fuse)
                            root.Add(Z.Box("Fuse — metaball field", "The melted field's own shading parameters.",
                                WrapRow(
                                    PackedSlider("Threshold", "Field level at which the melted surface forms.", l.metaThreshold, 0.1f, 2f, v => l.metaThreshold = v),
                                    PackedSlider("Shade range", "How deep into the field the colour ramp reaches.", l.metaShadeRange, 0.1f, 3f, v => l.metaShadeRange = v),
                                    PackedSlider("Edge softness", "Softness of the melted surface's edge.", l.metaSoftness, 0.01f, 1f, v => l.metaSoftness = v))));
                    }
                    break;
                case LayerShape.SparkleField:
                    root.Add(ValRow("Sparkle density", "Fraction of the disc's pixels that sparkle.", l.sparkleDensity, 0f, 1f, 0.25f));
                    root.Add(Z.Toggle("Blobs",
                        "Off = the original single-pixel-per-frame twinkle (Sparkle seed). On = each sparkle becomes a small blob with its own grow/hold/fade lifetime and a soft radius.",
                        l.sparkleBlobs, v => { Dial("Sparkle blobs", () => l.sparkleBlobs = v); RebuildLeft(); }));
                    if (l.sparkleBlobs)
                        root.Add(Z.Box("Sparkle blobs", "Per-blob shape and lifetime.",
                            WrapRow(
                                PackedVal("Radius (px)", "Each blob's radius in pixels.", l.sparkleBlobRadius, 0.5f, Mathf.Max(4f, half * 0.3f), 2f),
                                PackedVal("Life (frames)", "Each blob's grow/hold/fade lifetime.", l.sparkleBlobLife, 1f, 30f, 8f),
                                PackedVal("Softness", "Softness of each blob's edge.", l.sparkleBlobSoftness, 0f, 1f, 0.6f))));
                    else
                        root.Add(ValRow("Sparkle seed", "Reseeds the twinkle pattern (Min-Max = a new roll per frame).", l.sparkleSeed, 0f, 1f));
                    BuildDiscEdges(root, l);
                    break;
                case LayerShape.Crescent:
                    root.Add(Z.Value2D("Crescent offset",
                        l.crescentOffsetX, l.crescentOffsetY,
                        new ZuiValue2DControl.Options().WithRange(-2f, 2f, -2f, 2f).WithDefault(new Vector2(0.45f, 0f)),
                        "Where the masking disc sits — offset it to bite a crescent out of the main disc. ±2 fully separates the discs (a clean disc, no bite).",
                        DirtySpec, RecordSpec));
                    root.Add(WrapRow(
                        PackedVal("Outer softness", "Softness of the crescent's outer edge.", l.outerSoftness, 0f, 1f, 0f),
                        PackedVal("Bite softness", "Softness of the bitten (masked) edge.", l.innerSoftness, 0f, 1f, 0f)));
                    break;
                case LayerShape.Sprite:
                    root.Add(Z.Field("Sprite", "The sprite each scattered particle draws.",
                        Z.Object<Sprite>(l.particleSprite, "The sprite each scattered particle draws.", v =>
                        {
                            Dial("Particle sprite", () => l.particleSprite = v);
                            BlastRenderer.ClearSpriteCache();
                        }, 180f)));
                    root.Add(ValRow("Spin", "Degrees each sprite rotates over its life.", l.spriteSpin, -360f, 360f, 0f));
                    root.Add(WrapRow(
                        Z.Button("New sprite (Aseprite)", "Create a tiny starter PNG next to this asset and open it in Aseprite.",
                            () => { CreateParticleSprite(l); RebuildLeft(); }),
                        Z.Button("Edit in Aseprite", "Open the assigned sprite's PNG in Aseprite.",
                            () => { if (l.particleSprite != null) OpenInAseprite(l.particleSprite); })));
                    break;
            }

            BuildLayerModifierSections(root, l);
        }

        void BuildLayerModifierSections(VisualElement root, Layer l)
        {
            root.Add(Z.VSpace(2f));
            root.Add(Z.Text("Modifiers", ZuiText.Section, "This layer's own modifier stack, applied top to bottom."));
            BuildModifiers(root, l.modifiers, isGlobal: false);
            root.Add(Z.VSpace());
            BuildSimulationModifier(root, "Simulation (genuinely iterative, always last IN THIS LAYER)",
                () => l.simulationModifier, m => l.simulationModifier = m);
        }

        // ── shape preview (isolated single shape) ───────────────────────────────────────────
        void BuildShapePreview(VisualElement root, Layer l)
        {
            root.Add(Z.Toggle("Shape preview",
                "An isolated preview of exactly ONE of this layer's shapes, centred and at max size — ignoring Count/Position/Spawn radius/Ring placement entirely, so a busy layer can still be dialed in cleanly.",
                spec.previewShapeOn, v => { Dial("Shape preview", () => spec.previewShapeOn = v); RebuildLeft(); }));
            if (!spec.previewShapeOn) return;

            const float boxSize = 128f;
            var shapeView = new IMGUIContainer(() =>
            {
                var r = new Rect(0f, 0f, boxSize, boxSize);
                if (Event.current.type != EventType.Repaint) return;
                EditorGUI.DrawRect(r, new Color(0f, 0f, 0f, 0.25f));
                int cur = CurrentFrame();
                float span = Mathf.Max(1, l.endFrame - l.startFrame);
                float t = Mathf.Clamp01((cur - l.startFrame) / span);
                if (shapePreviewTex != null) Object.DestroyImmediate(shapePreviewTex);
                shapePreviewTex = BlastRenderer.RenderShapePreviewTexture(l, spec, layerSel, t, cur,
                    (int)boxSize, (int)boxSize, spec.previewShapeGradientFill, spec.previewShapeCrescent,
                    spec.previewShapeHollow, spec.previewShapeSize, spec.previewShapeSpin, spec.previewShapeAlpha,
                    spec.previewShapeModifiers);
                GUI.DrawTexture(r, shapePreviewTex, ScaleMode.StretchToFill, true);
            });
            shapeView.style.width = boxSize;
            shapeView.style.height = boxSize;
            shapeView.style.flexShrink = 0f;
            shapeView.tooltip = "The isolated shape, animated on the main transport's own timeline.";
            shapeView.schedule.Execute(() => shapeView.MarkDirtyRepaint()).Every(66);

            Toggle Tg(string label, string tooltip, bool value, System.Action<bool> set) =>
                Z.Toggle(label, tooltip, value, v => Dial("Shape preview", () => set(v))).W(120f);

            var toggles = Z.Column(
                WrapRow(Tg("Gradient Fill", "Show the spatial gradient in the isolated preview.", spec.previewShapeGradientFill, v => spec.previewShapeGradientFill = v),
                        Tg("Crescent", "Apply the crescent bite in the isolated preview.", spec.previewShapeCrescent, v => spec.previewShapeCrescent = v)),
                WrapRow(Tg("Hollow", "Apply the hollow hole in the isolated preview.", spec.previewShapeHollow, v => spec.previewShapeHollow = v),
                        Tg("Size", "Animate Size in the isolated preview.", spec.previewShapeSize, v => spec.previewShapeSize = v)),
                WrapRow(Tg("Spin", "Animate Spin in the isolated preview.", spec.previewShapeSpin, v => spec.previewShapeSpin = v),
                        Tg("Alpha", "Animate Alpha in the isolated preview.", spec.previewShapeAlpha, v => spec.previewShapeAlpha = v)),
                WrapRow(Tg("Layer Modifiers", "Apply this layer's modifier stack in the isolated preview.", spec.previewShapeModifiers, v => spec.previewShapeModifiers = v)));

            root.Add(Z.Box(null, null, WrapRow(shapeView, toggles)));
        }

        // ── scatter section (all non-Bars/MetaBlob shapes) ─────────────────────────────────
        void BuildScatterSection(VisualElement root, Layer l, float half)
        {
            bool rosing = l.scatterMode == ScatterMode.Rosing;
            bool ring = l.scatterMode == ScatterMode.Ring;

            if (!rosing)
                root.Add(WrapRow(
                    PackedVal("Count", "How many shapes this layer scatters (sampled once for the whole layer).", l.count, 1f, 40f, allowMinMax: false),
                    PackedVal("Spawn radius", "How far from the origin each shape may spawn (0..1 of the canvas).", l.spawnRadius, 0f, 1f)));

            root.Add(Z.MiniRadio((int)l.scatterMode, ScatterModeLabels,
                "How shapes place themselves: scattered over an area, along a ring, or as authored blooming rings (Rosing).",
                v => { Dial("Scatter mode", () => l.scatterMode = (ScatterMode)v); RebuildLeft(); }));

            if (ring || rosing)
            {
                var box = Z.Box(rosing ? "Rosing — rings bloom outward over life" : "Ring — placed along the rim",
                    rosing ? "Each authored ring places its own Count shapes evenly around the arc, at its own Radius, appearing at Birth and living for Life."
                           : "360° = the full rim; less confines shapes to a wedge starting at Start angle. Start angle/Arc lock in per-shape at spawn — for a ring that visibly spins live, add a Rotate modifier instead.");
                box.Add(Z.MiniRadio((int)l.ringOrder, RingOrderLabels,
                    "Sequential = shapes place in order around the arc; Random = shuffled placement.",
                    v => Dial("Ring order", () => l.ringOrder = (RingOrder)v)));
                box.Add(WrapRow(
                    PackedVal("Start angle", "Where the arc begins, in degrees.", l.ringStartAngle, -180f, 180f, 0f, allowMinMax: false),
                    PackedVal("Arc degrees", "How much of the rim the shapes cover.", l.ringArcDegrees, 0f, 360f, 360f, allowMinMax: false)));
                box.Add(WrapRow(
                    PackedVal("Ring expand", "Live ×scale on the whole ring's radius — animate it and the ring grows/shrinks with every shape riding along.", l.ringExpand, 0f, 3f, 1f, allowMinMax: false),
                    Z.Toggle("Align rotation",
                        "Rotates each shape to face its own angle around the ring — matters for asymmetric shapes; a plain Disc looks the same either way.",
                        l.ringAlignRotation, v => Dial("Align rotation", () => l.ringAlignRotation = v))));
                if (rosing)
                {
                    box.Add(Z.Toggle("Reverse ring order",
                        "Off = later rings draw ON TOP of earlier ones. On = reversed. Only changes which RING composites over which.",
                        l.roseReverseDraw, v => Dial("Reverse ring order", () => l.roseReverseDraw = v)));
                    BuildRoseRings(box, l);
                }
                root.Add(box);
            }

            root.Add(WrapRow(
                PackedVal2D("Position", "Each shape's offset from the origin — drag to aim, or animate as a path.",
                    l.positionX, l.positionY,
                    new ZuiValue2DControl.Options().WithRange(-half, half, -half, half).WithDefault(Vector2.zero)),
                PackedVal("Spin", "Degrees each shape rotates over its life.", l.spinDegrees, -360f, 360f, 0f)));
            root.Add(WrapRow(
                PackedVal("Size", "Each shape's radius in pixels.", l.size, 0f, half),
                PackedVal("Alpha", "Each shape's opacity over its life.", l.alpha, 0f, 1f)));

            var lifeRow = WrapRow(
                PackedSlider("Life jitter", "Randomizes each shape's lifetime by up to this fraction.", l.perShapeLifeJitter, 0f, 1f, v => l.perShapeLifeJitter = v));
            if (!rosing)
                lifeRow.Add(PackedSlider("Spawn stagger", "Spreads shape spawn times across the layer's life window.", l.spawnStagger, 0f, 1f, v => l.spawnStagger = v));
            root.Add(lifeRow);

            if (!rosing)
                root.Add(Z.Toggle("Sync death",
                    "Every shape reaches the end of its life at the SAME frame (this layer's End frame) regardless of when it spawned — earlier shapes mature more slowly so the whole burst finishes together.",
                    l.syncDeath, v => Dial("Sync death", () => l.syncDeath = v)));
        }

        // ── Bars section ────────────────────────────────────────────────────────────────────
        void BuildBarsSection(VisualElement root, Layer l, float cs)
        {
            var box = Z.Box("Bars — forward-growing row",
                "A self-contained directional mode — scatter/emission/deform controls don't apply; everything here is sampled once for the whole row/arm-set.");
            box.Add(WrapRow(
                PackedVal("Bars per side", "How many bars on each side of the centre.", l.barCount, 0f, 40f, 7f, allowMinMax: false),
                PackedVal("Width (px)", "Each bar's width in pixels.", l.barWidth, 1f, 12f, 3f, allowMinMax: false)));
            box.Add(WrapRow(
                PackedVal("Spacing (×width)", "Gap between bars as a multiple of their width — 1 = touching.", l.barSpacing, 1f, 6f, 1.5f, allowMinMax: false),
                PackedVal("Edge softness", "Softens each bar's sides and tip.", l.barSoftness, 0f, 1f, 0f, allowMinMax: false)));
            box.Add(WrapRow(
                PackedVal("Forward reach", "How far a bar reaches forward (Min-Max IS per-bar variety here).", l.barForward, 0f, cs, 40f),
                PackedVal("Backward frac", "Fraction of the reach extending backward too.", l.barBackwardFrac, 0f, 1f, 0.18f, allowMinMax: false)));
            box.Add(WrapRow(
                PackedVal("Taper (centre↔edge)", "+1 = centre longest (triangle/flame), 0 = flat, -1 = concave.", l.barTaper, -1f, 1f, 0.85f, allowMinMax: false),
                PackedVal("Stagger (timing)", "Delays outer bars so the row unfurls centre-out — timing, not shape.", l.barStagger, 0f, 0.5f, 0.05f, allowMinMax: false)));
            box.Add(WrapRow(
                PackedVal("Layer angle", "Rotates the whole row.", l.barAngleDeg, -180f, 180f, 0f, allowMinMax: false),
                Z.Toggle("Mirror angle", "Mirror the row across the base angle.", l.barMirror,
                    v => Dial("Mirror angle", () => l.barMirror = v))));
            box.Add(ValRow("Origin inset", "Pushes the row's base away from the origin.", l.originInset, 0f, 40f, 4f, allowMinMax: false));
            box.Add(Z.MiniRadio((int)l.barDecay, BarDecayLabels,
                "How bars die: Contract pulls back to the base; Dissolve erodes in place.",
                v => { Dial("Bar decay", () => l.barDecay = (BarDecay)v); RebuildLeft(); }));
            if (l.barDecay == BarDecay.Dissolve)
                box.Add(PackedSlider("Dissolve start", "Life fraction at which dissolving begins.", l.dissolveStart, 0f, 1f, v => l.dissolveStart = v, 150f));
            box.Add(WrapRow(
                PackedVal("Base angle", "Base direction the row grows toward.", l.baseAngleDeg, -180f, 180f, 0f, allowMinMax: false),
                Z.Toggle("Star (arms radiate from centre)", "Turn the row into arms radiating from the centre; the canvas auto-fits.",
                    l.star, v => { Dial("Star", () => l.star = v); RebuildLeft(); })));
            if (l.star)
            {
                box.Add(WrapRow(
                    Z.Field("Arms", "How many arms radiate from the centre.",
                        Z.SliderInt(l.spreadCount, 1, 24, "How many arms radiate from the centre.",
                            v => Dial("Arms", () => l.spreadCount = Mathf.Max(1, v)), 110f)),
                    PackedVal("Spread degrees", "Total angle the arms fan across.", l.spreadDegrees, 0f, 360f, 360f, allowMinMax: false)));
                box.Add(Z.Text("Arms share the centre and radiate outward; canvas auto-fits.", ZuiText.Small,
                    "Star mode computes the canvas size from the arms."));
            }
            root.Add(box);
        }

        // ── MetaBlob section ────────────────────────────────────────────────────────────────
        void BuildMetaBlobSection(VisualElement root, Layer l, float half)
        {
            var box = Z.Box("MetaBlob — orbs fuse into one gradient-shaded shape",
                "Authored by clicking the preview to drop fusing orbs — no scatter/count controls.");
            box.Add(WrapRow(
                Z.Toggle(placeMetaMode ? "● Placing — click the preview" : "○ Place orbs (click preview)",
                    "Arm placing mode, then click the preview to drop orbs (birth follows placement order).",
                    placeMetaMode, v => { placeMetaMode = v; RebuildLeft(); }),
                Z.Toggle("Markers", "Draw the orb rings + numbers over the preview.", showMetaMarkers,
                    v => { showMetaMarkers = v; previewContainer?.MarkDirtyRepaint(); })));
            box.Add(WrapRow(
                PackedSlider("Threshold", "Field level at which the melted surface forms.", l.metaThreshold, 0.1f, 2f, v => l.metaThreshold = v),
                PackedSlider("Shade range", "How deep into the field the colour ramp reaches.", l.metaShadeRange, 0.1f, 3f, v => l.metaShadeRange = v),
                PackedSlider("Edge softness", "Softness of the melted surface's edge.", l.metaSoftness, 0.01f, 1f, v => l.metaSoftness = v)));
            box.Add(WrapRow(
                PackedSlider("Spawn interval", "Birth-time gap between successively placed orbs.", l.metaSpawnInterval, 0f, 0.5f, v => l.metaSpawnInterval = v, 150f),
                Z.Button("Renumber births", "Re-apply the spawn interval to every orb already placed (it only affects NEW orbs on its own).", () =>
                {
                    Dial("Renumber births", () =>
                    {
                        for (int oi = 0; oi < l.metaOrbs.Count; oi++)
                        {
                            var oo = l.metaOrbs[oi];
                            if (oo == null) continue;
                            oo.birth = Mathf.Clamp01(oi * l.metaSpawnInterval);
                            oo.life = Mathf.Clamp(1f - oo.birth, 0.25f, 1f);
                        }
                    });
                    RebuildLeft();
                })));
            box.Add(WrapRow(
                PackedVal("Radius pulse", "×radius of every orb over life — one shared animatable value.", l.metaRadiusScale, 0f, 3f, 1f, allowMinMax: false),
                PackedVal("Expand", "Contract/expand every orb's centre about the origin over life.", l.metaExpand, 0f, 3f, 1f, allowMinMax: false)));

            box.Add(Z.MiniRadio((int)l.colorMode, ColorModeLabels,
                "How the colour ramp shades the melted field.",
                v => { Dial("Colour mode", () => l.colorMode = (ColorMode)v); RebuildLeft(); }));
            if (l.colorMode == ColorMode.NoiseFill)
                BuildNoiseFillParams(box, l, half, perShape: false);
            else if (l.colorMode != ColorMode.OverLife)
            {
                box.Add(Z.Text("Gradient", ZuiText.Section, "How the spatial gradient sits and moves on the blob."));
                box.Add(WrapRow(
                    PackedVal("Position", "Slides the gradient through the blob.", l.colorFlow, -2f, 2f, 0f, allowMinMax: false),
                    PackedVal("Zoom", "Scales the gradient within the blob.", l.colorFlowZoom, 0.1f, 4f, 1f, allowMinMax: false)));
            }

            for (int i = 0; i < l.metaOrbs.Count; i++)
            {
                int idx = i;
                var o = l.metaOrbs[idx];
                var orbBox = Z.Box(null, null);

                // Header row: select · number · (spacer) · delete — delete sits at the row's END, not
                // wedged between fields (reported 2026-07-23).
                orbBox.Add(Z.Row(
                    Z.Button(metaSel == idx ? "●" : "○", "Select this orb (also draggable in the preview).",
                        () => { metaSel = idx; RebuildLeft(); }).W(24f),
                    Z.Text($"#{idx + 1}", ZuiText.Small, "Orb number (placement order)."),
                    Z.Flexible(),
                    Z.Button("X", "Delete this orb.", () =>
                    {
                        Dial("Remove orb", () => l.metaOrbs.RemoveAt(idx));
                        metaSel = -1;
                        RebuildLeft();
                    }).W(22f)));

                // Position as a real 2D pad (plain Vector2 — an orb's placement never animates),
                // beside its animatable radius.
                orbBox.Add(WrapRow(
                    Z.Vector2Field("Position", () => o.pos, v => o.pos = v, o,
                        new ZuiValue2DControl.Options()
                            .WithRange(-half, half, -half, half)
                            .WithDefault(Vector2.zero)
                            .WithPlotSize(72f)
                            .WithValueDisplay(false, false)   // the pad IS the display; 16 orbs stay compact
                            .WithoutSidePanel()
                            .Expanded(),
                        "Drag to place this orb on the canvas (pixels from the middle).",
                        DirtySpec, RecordSpec),
                    ValRow("Rad", "This orb's radius in pixels — animatable over its OWN life (birth→death).",
                        o.Radius, 2f, half, 12f, allowMinMax: false, width: 130f)));

                orbBox.Add(WrapRow(
                    PackedSlider("Birth", "Life fraction at which this orb appears.", o.birth, 0f, 1f, v => o.birth = v),
                    PackedSlider("Life", "How long this orb lives (fraction of the layer's life).", o.life, 0.02f, 1f, v => o.life = v)));
                box.Add(orbBox);
            }
            box.Add(WrapRow(
                Z.Button("Clear orbs", "Delete every orb.", () =>
                {
                    Dial("Clear orbs", () => l.metaOrbs.Clear());
                    metaSel = -1;
                    RebuildLeft();
                }),
                Z.Text($"{l.metaOrbs.Count} orb(s)", ZuiText.Small, "How many orbs the blob currently has.")));
            root.Add(box);
        }

        // ── Height balls section ────────────────────────────────────────────────────────────
        void BuildHeightBallsSection(VisualElement root, Layer l, float half)
        {
            var shading = Z.Box("Fused cloud — shared by every group",
                "Every group below feeds ONE set of density/heat/height fields, in one pass, before any shading happens — which is why a smoke base and a flame burst melt together instead of stacking as two silhouettes. These dials describe that single fused result, so they can't be per-group.");
            shading.Add(PackedVal2D("Position", "Moves the whole cloud off the blast's origin. Animatable — this is also what drifts the cloud upward or sideways over its life.",
                l.positionX, l.positionY,
                new ZuiValue2DControl.Options().WithRange(-half, half, -half, half).WithDefault(Vector2.zero)));
            shading.Add(WrapRow(
                PackedSlider("Fusion", "How eagerly neighbouring balls melt into each other. 0 = every ball keeps its own hard edge; higher blends them into one mass with soft necks.", l.hbFusion, 0f, 1f, v => l.hbFusion = v),
                PackedSlider("Coverage", "How fast the cloud turns opaque as it thickens. Low = wispy and translucent; high = a solid silhouette that only fades at the very edge.", l.hbCoverage, 0.5f, 12f, v => l.hbCoverage = v)));
            shading.Add(Z.Toggle("Relief lighting",
                "Shade the cloud by the local slope of its height field, as if lit from one side — this is what gives it the chunky 3D read. Off leaves flat ramp shading.",
                l.hbLighting, v => { Dial("Relief lighting", () => l.hbLighting = v); RebuildLeft(); }));
            if (l.hbLighting)
                shading.Add(WrapRow(
                    PackedSlider("Relief", "How steep the height field is treated as being — how pronounced the bumps and creases between fused balls read.", l.hbRelief, 0.2f, 6f, v => l.hbRelief = v),
                    PackedVal("Light angle", "Where the light comes from, in degrees (0 = from the right, 90 = from above). Animate it to roll the shading across the cloud.", l.hbLightAngle, 0f, 360f, 135f, allowMinMax: false)));
            shading.Add(WrapRow(
                PackedSlider("Confine", "The cloud's self-limiting radius, as a fraction of the canvas half-size. Nothing any group draws can reach past this — travel is squeezed smoothly toward the limit rather than clipped — so the animation never collides with the frame edge.", l.hbConfine, 0.05f, 1f, v => l.hbConfine = v),
                PackedSlider("Fold under", "How readily a squeezed ball folds under — one the Confine circle had to pull back in loses mass and heat, shrinks, sinks toward smoke and is tucked inward. A ball that fits inside Confine is under no pressure and never folds. 0 = no folding.", l.hbFold, 0f, 1f, v => l.hbFold = v)));
            root.Add(shading);

            var groups = l.HeightBallGroups;
            root.Add(Z.Text("Ball groups", ZuiText.Section,
                "Each group is its own series of waves of balls, with its own size, spread, height on the ramp, alpha and rotation — but all of them fuse into the single mass above."));

            var host = new VisualElement();
            root.Add(host);
            for (int gi = 0; gi < groups.Count; gi++)
                host.Add(BuildHeightBallGroupBox(host, l, groups, gi, half));

            root.Add(WrapRow(
                Z.Button("+ Add group", "Append another group of balls to this same fused cloud (undoable).", () =>
                {
                    Dial("Add ball group", () => groups.Add(
                        HeightBallGroup.New($"Group {groups.Count + 1}", HeightBallGroup.NextSeedSalt(groups))));
                    RebuildLeft();
                }),
                Z.Text($"{groups.Count} group(s)", ZuiText.Small, "How many ball groups feed this layer's fused cloud.")));
        }

        // A group's dials run on one of two clocks, and confusing them is the difference between "the ball
        // fades" and "the whole cloud fades." Every animatable tooltip in a group says which, in the same
        // words, so the answer is one hover away wherever the reader happens to be looking.
        const string BallAxis = " Read across THIS BALL's own life: 0 the moment its wave places it, 1 the moment that wave ends.";
        const string LayerAxis = " Read across the LAYER's life, like every other layer dial.";

        VisualElement BuildHeightBallGroupBox(VisualElement host, Layer l, List<HeightBallGroup> groups, int gi, float half)
        {
            var g = groups[gi];
            bool open = !hbGroupClosed.Contains(gi);
            var box = Z.Box(null, null);

            var grip = Z.Text("≡", ZuiText.Body,
                "Drag to reorder this group in the list. Purely cosmetic: every group fuses into the same mass, so none draws in front of another, and each keeps its own arrangement wherever it sits.");
            grip.style.unityFontStyleAndWeight = FontStyle.Bold;
            grip.style.width = 16f;
            ZuiReorder.MakeGrip(grip, box, host, (from, to) =>
            {
                Dial("Reorder ball group", () =>
                {
                    var moved = groups[from];
                    groups.RemoveAt(from);
                    groups.Insert(to, moved);
                });
                hbGroupClosed.Clear();
                RebuildLeft();
            });

            var nameField = Z.TextInput(g.name, "This group's name — rename it right here.", v =>
            {
                RecordSpec();
                g.name = v;
                DirtySpec();
            }, 0f);
            nameField.style.width = StyleKeyword.Auto;
            nameField.style.flexGrow = 1f;
            nameField.style.flexShrink = 1f;
            nameField.style.minWidth = 50f;
            nameField.AddToClassList("zui-audit-allow-stretch");

            box.Add(Z.Row(
                grip,
                Z.Button(open ? "-" : "+", open ? "Fold this group's dials away." : "Unfold this group's dials.", () =>
                {
                    if (open) hbGroupClosed.Add(gi); else hbGroupClosed.Remove(gi);
                    RebuildLeft();
                }).W(24f),
                Z.Toggle("", "Include this group in the fused cloud. Off hides its balls without deleting the group.",
                    g.enabled, v => Dial(v ? "Enable ball group" : "Disable ball group", () => g.enabled = v)),
                nameField,
                Z.Button("Dup", "Duplicate this group just after itself. The copy gets its own random arrangement, so it lands beside the original rather than exactly on top of it (undoable).", () =>
                {
                    Dial("Duplicate ball group", () =>
                    {
                        var copy = g.Clone();
                        copy.name += " copy";
                        copy.seedSalt = HeightBallGroup.NextSeedSalt(groups);
                        groups.Insert(gi + 1, copy);
                    });
                    hbGroupClosed.Clear();
                    RebuildLeft();
                }).W(40f),
                Z.Button("X", "Delete this group (undoable).", () =>
                {
                    Dial("Remove ball group", () => groups.RemoveAt(gi));
                    hbGroupClosed.Clear();
                    RebuildLeft();
                }).W(22f)));

            var body = new VisualElement();
            box.Add(body.Shown(open));
            if (!open) return box;

            body.Add(Z.Box("Every ball — over its own life",
                "Everything here describes a single ball, so it is authored on that ball's own clock: 0 is the moment its wave places it, 1 is the moment that wave ends. That is why Alpha is the fade — a curve rising from 0 and falling back to it IS the ball arriving and leaving, and nothing else fades one.",
                WrapRow(
                    PackedVal("Alpha", "A ball's own opacity, multiplied with the layer's Alpha above. The default rise-hold-fall curve is what fades each ball in and out." + BallAxis, g.Alpha, 0f, 1f, allowMinMax: false),
                    PackedVal("Height", "How far UP the smoke→fire ramp a ball sits — its energy. Low = cold smoke, and a flat cloud; high = fire, and a tall one that catches the relief light. A falling curve cools a ball as it ages." + BallAxis, g.height, 0f, 1f, 0.45f, allowMinMax: false),
                    PackedVal("Mass", "How much body a ball adds — raises the cloud's height (so it catches more light) and nudges it up the ramp even with no energy at all." + BallAxis, g.mass, 0f, 0.5f, 0.16f, allowMinMax: false),
                    PackedVal("Ball size", "A ball's radius in pixels — bigger balls melt together into a smoother, heavier mass." + BallAxis, g.ballSize, 1f, half, 7f, allowMinMax: false),
                    PackedVal("Spread", "Where inside the cloud a wave's balls sit. 0 = every ball clumped in the middle; 0.5 = a perfectly even spread; 1 = every ball out on the rim, a ring. A 0→1 curve blows a clump out into an expanding ring." + BallAxis, g.spread, 0f, 1f, 0.5f, allowMinMax: false))));

            body.Add(Z.Box("The whole group — over the layer's life",
                "These describe the group as a whole rather than any one ball in it, so they run on the layer's own clock alongside every other layer dial.",
                WrapRow(
                    PackedVal("Cloud size", "The group's radius (0-1 of the canvas half-size) — how far out a wave places its balls, and Spread decides where INSIDE that radius they sit. Confine above is the hard ceiling: raise it if you want the cloud to use more of the frame." + LayerAxis, g.cloudSize, 0f, 1f, 0.4f, allowMinMax: false),
                    PackedVal("Push", "How far outward a wave carries its balls beyond where Spread placed them, in pixels. It eases to a halt rather than launching, and the shared Confine caps it regardless." + LayerAxis, g.wavePush, 0f, half, 14f, allowMinMax: false),
                    PackedVal("Rotation", "Turns the whole group about the cloud's centre, in degrees. A rising curve visibly rotates it as the blast plays." + LayerAxis, g.rotation, -360f, 360f, 0f, allowMinMax: false),
                    PackedVal("Churn", "How far a ball wanders from its placed spot, in pixels — this group's idle boil." + LayerAxis, g.churn, 0f, 20f, 2.5f, allowMinMax: false),
                    PackedSlider("Churn speed", "How many full churn cycles a ball completes across the layer's life. Low = a slow roll; high = a busy boil.", g.churnSpeed, 0.1f, 8f, v => g.churnSpeed = v))));

            var waves = Z.Box("Waves — how many balls, and when they live",
                "A group is nothing but its waves: every ball belongs to one, is born with it and dies with it. Waves times Balls per wave is the whole population — one single ball is Waves 1, Balls per wave 1.");
            waves.Add(WrapRow(
                Z.Field("Waves", "How many waves this group fires across the layer's life, spaced so the last one still finishes. 1 = a single wave spanning the WHOLE layer life. Never 0 — a ball with no wave would have no life to animate over.",
                    Z.SliderInt(g.waves, 1, HeightBallGroup.MaxWaves, "How many waves this group fires across the layer's life, spaced so the last one still finishes. 1 = a single wave spanning the WHOLE layer life. Never 0 — a ball with no wave would have no life to animate over.",
                        v => Dial("Waves", () => g.waves = v), 110f)),
                Z.Field("Balls per wave", "How many balls one wave places, around the group's centre. This is the group's ONLY ball count.",
                    Z.SliderInt(g.waveBalls, 1, HeightBallGroup.MaxWaveBalls, "How many balls one wave places, around the group's centre. This is the group's ONLY ball count.",
                        v => Dial("Balls per wave", () => g.waveBalls = v), 110f)),
                PackedSlider("Wave life", "How long one wave lasts, as a fraction of the layer's life — which is also how long each of its balls lives. Ignored when Waves is 1.", g.waveLife, 0.05f, 1f, v => g.waveLife = v),
                PackedSlider("Symmetry", "1 = a wave's balls sit at perfectly even angles; lower scatters them for a lopsided, organic wave.", g.symmetry, 0f, 1f, v => g.symmetry = v)));
            if (g.waves == 1)
                waves.Add(Z.Text("One wave — it spans the layer's whole life.", ZuiText.Small,
                    "With a single wave there is nothing to leave room for, so Wave life is bypassed and the wave covers every frame of the layer."));
            body.Add(waves);

            body.Add(Z.Box("Shape — breaking up the roundness",
                "Perfect domes fused together still read as a bag of marbles. Squash makes each ball its own ellipse, and Surface noise stretches every ball of THIS group at a given pixel by the same amount, so its neighbours bulge and dent together and their rims interlock. Each group gets its own noise field, so a smoke base and a flame burst can be rough in different ways.",
                WrapRow(
                    PackedSlider("Squash", "How far each ball departs from a circle — its own seeded ellipse at its own angle (up to about 2:1), with a burst's balls leaning along their travel direction. 0 = perfectly round.", g.squash, 0f, 1f, v => g.squash = v),
                    PackedSlider("Surface noise", "Roughens this group's surface with noise shared by all of ITS balls, so neighbours deform together into one lumpy mass and the relief lighting picks the roughness up as texture. 0 = smooth domes.", g.surfaceNoise, 0f, 1f, v => g.surfaceNoise = v)),
                WrapRow(
                    PackedSlider("Noise size", "Feature size of this group's surface noise, in pixels. Small = a fine crumbly boil; large = a few big soft lobes.", g.surfaceZoom, 2f, 48f, v => g.surfaceZoom = v),
                    PackedSlider("Noise drift", "How fast this group's surface noise crawls over the layer's life, so the surface roils instead of holding one frozen pattern.", g.surfaceDrift, 0f, 4f, v => g.surfaceDrift = v))));

            return box;
        }

        // ── disc edges (Disc + SparkleField) ────────────────────────────────────────────────
        void BuildDiscEdges(VisualElement root, Layer l)
        {
            root.Add(WrapRow(
                PackedVal("Outer softness", "Alpha falloff at the disc's outer edge.", l.outerSoftness, 0f, 1f, 0f),
                Z.Toggle("Hollow", "Cut a hole in the disc (its own size + edge below).", l.hollow,
                    v => { Dial("Hollow", () => l.hollow = v); RebuildLeft(); })));
            if (l.hollow)
            {
                root.Add(WrapRow(
                    PackedVal("Hole size", "Hole radius as a fraction of the disc.", l.holeSize, 0f, 1f, 0.5f),
                    PackedVal("Inner softness", "Alpha falloff at the hole's edge.", l.innerSoftness, 0f, 1f, 0f)));
                root.Add(Z.Value2D("Hole offset", l.holeOffsetX, l.holeOffsetY,
                    new ZuiValue2DControl.Options().WithRange(-1f, 1f, -1f, 1f).WithDefault(Vector2.zero),
                    "Offset the hole from the disc's centre — an offset hole reads as a crescent.",
                    DirtySpec, RecordSpec));
            }
        }

        // ── rose rings ──────────────────────────────────────────────────────────────────────
        void BuildRoseRings(VisualElement box, Layer l)
        {
            l.roseRings ??= new List<RoseRing>();
            for (int i = 0; i < l.roseRings.Count; i++)
            {
                int idx = i;
                var r = l.roseRings[idx];
                if (r == null) continue;
                float fw = 84f;
                var row = WrapRow(
                    Z.Text($"R{idx + 1}", ZuiText.Small, "Ring number."),
                    Z.Stacked("Count", "How many shapes this ring places around the arc.", r.count, 1, 60,
                        v => Dial("Ring count", () => r.count = Mathf.Max(1, Mathf.RoundToInt(v))), fw, isInt: true),
                    Z.Stacked("Radius", "This ring's placement distance from the origin (0-1).", r.radius, 0f, 1f,
                        v => Dial("Ring radius", () => r.radius = v), fw),
                    Z.Stacked("Size", "×scale on the layer's Size for just this ring's discs.", r.sizeScale, 0.1f, 3f,
                        v => Dial("Ring size", () => r.sizeScale = v), fw),
                    Z.Stacked("Birth", "Life fraction at which this ring appears.", r.birth, 0f, 1f,
                        v => Dial("Ring birth", () => r.birth = v), fw),
                    Z.Stacked("Life", "How long this ring lives.", r.life, 0.02f, 1f,
                        v => Dial("Ring life", () => r.life = v), fw),
                    Z.Button("X", "Delete this ring.", () =>
                    {
                        Dial("Remove rose ring", () => l.roseRings.RemoveAt(idx));
                        RebuildLeft();
                    }).W(22f));
                box.Add(Z.Box(null, null, row));
            }
            box.Add(WrapRow(
                Z.Button("+ Add ring", "Append another blooming ring.", () =>
                {
                    Dial("Add rose ring", () =>
                    {
                        int n = l.roseRings.Count;
                        l.roseRings.Add(new RoseRing { count = 4 + n * 6, radius = Mathf.Min(0.9f, 0.15f + n * 0.2f), birth = Mathf.Min(0.6f, n * 0.15f), life = 1f });
                    });
                    RebuildLeft();
                }),
                Z.Text($"{l.roseRings.Count} ring(s)", ZuiText.Small, "How many rings this Rosing layer has.")));
        }

        // ── noise-fill params ───────────────────────────────────────────────────────────────
        void BuildNoiseFillParams(VisualElement root, Layer l, float half, bool perShape)
        {
            root.Add(Z.Text("Gradient", ZuiText.Section, "How the colour ramp maps onto the noise field."));
            root.Add(WrapRow(
                PackedVal("Position", "Slides the ramp through the noise field.", l.noiseGradientPosition, 0f, 1f, 0f, allowMinMax: perShape),
                PackedVal("Zoom", "Scales the ramp within the noise field.", l.noiseGradientZoom, 0.1f, 8f, 1f, allowMinMax: perShape)));
            root.Add(Z.Text("Noise", ZuiText.Section, "The sampled noise field's own shape."));
            root.Add(WrapRow(
                PackedVal("Zoom", "Noise feature size.", l.noiseZoom, 1f, Mathf.Max(8f, half), 20f, allowMinMax: perShape),
                PackedVal("Rotation", "Rotates the noise field.", l.noiseRotation, -720f, 720f, 0f, allowMinMax: perShape)));
            root.Add(WrapRow(
                PackedVal2D("Drift", "Scrolls the noise field over time.", l.noiseDriftX, l.noiseDriftY,
                    new ZuiValue2DControl.Options().WithRange(-half, half, -half, half).WithDefault(Vector2.zero)),
                PackedVal("Warp", "Distorts the noise field into itself.", l.noiseWarp, 0f, 2f, 0.6f, allowMinMax: perShape)));
            root.Add(PackedSlider("Shading bands", "Quantizes the ramp into this many bands.", l.noiseBands, 1f, 8f,
                v => l.noiseBands = Mathf.Max(1, Mathf.RoundToInt(v)), 150f));
            root.Add(PackedSlider("Band softness", "Softens the band boundaries.", l.noiseBandSoftness, 0f, 1f,
                v => l.noiseBandSoftness = v, 150f));
        }

        // ── gradient rows (GradientField has no Z wrapper yet — used via Z.Field pairing) ──
        VisualElement GradientRow(string label, string tooltip, System.Func<Gradient> get, System.Action<Gradient> set)
        {
            var gf = new UnityEditor.UIElements.GradientField { value = get(), tooltip = tooltip };
            gf.style.width = 180f;
            gf.RegisterValueChangedCallback(e =>
            {
                RecordSpec();
                set(e.newValue);
                DirtySpec();
            });
            return Z.Field(label, tooltip, gf);
        }
    }
}
