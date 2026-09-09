// LatheWindow — the authoring window for Lathe: R&D tool for solid 3D-ish procgen shapes, laid out and
// operated the way Pyre is (a Solids "layer" stack, per-solid plug-in Module + Modifiers, a live
// preview with a transport/scrub), but generating swept/primitive 3D geometry instead of a 2D raster.
// Deliberately a separate tool from Pyre — see the CLAUDE.md conversation this was scoped from: the
// generation model (a solid stack sharing one 3D scene) is different enough from Pyre's shape/swarm
// raster stack to earn its own window rather than co-opting Pyre's preview pane.
using System;
using System.Collections.Generic;
using System.IO;
using Laubrary.AssetKit.Editor;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Lathe.Editor
{
    public partial class LatheWindow : ZuiAssetWindow<LatheSpec>
    {
        [MenuItem("Laubrary/Lathe")]
        public static void Open() => GetWindow<LatheWindow>("Lathe");

        LatheSpec spec => Current;
        protected override string TypeLabel => "Lathe";
        protected override string NewAssetName => "New Lathe";
        protected override string DefaultFolder => "Assets/Lathe";

        // ── preview / transport state ────────────────────────────────────────────────
        IMGUIContainer preview;
        LathePreview previewRenderer;
        double lastTime;
        float acc;
        bool playing = true;
        int frame;
        Button playButton;
        SliderInt scrubSlider;
        Label frameReadout;
        VisualElement transportHost;

        // Orbit camera — independent of the turntable spin, mouse-controlled in DrawPreview.
        float orbitYaw = 35f, orbitPitch = -20f, orbitDist = 5f;

        // Skeleton editor state — see LatheWindow.Skeleton.cs. Session-only (not persisted): re-enable
        // "Edit Skeleton" on revisiting an asset, same as the orbit camera resets.
        bool editingSkeleton;

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
            previewRenderer?.Dispose();
            previewRenderer = null;
        }

        protected override void OnAssetChanged()
        {
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
                frame = (frame + 1) % Mathf.Max(1, spec.turntableFrames);
                acc -= 1f;
                advanced = true;
            }
            if (advanced) { preview?.MarkDirtyRepaint(); RefreshTransportReadout(); }
        }

        // ── mutation helpers (Laubrary Undo rule: every dial edit is undoable) ──────────
        void Dirty(Action edit)
        {
            if (spec == null) return;
            Undo.RecordObject(spec, "Edit Lathe");
            edit();
            EditorUtility.SetDirty(spec);
            preview?.MarkDirtyRepaint();
        }

        // Cosmetic preview-only state (the backdrop colour) — no Undo, still dirtied so it persists.
        void DirtyRepaintOnly(Action edit)
        {
            if (spec == null) return;
            edit();
            EditorUtility.SetDirty(spec);
            preview?.MarkDirtyRepaint();
        }

        // ── selected solid ──────────────────────────────────────────────────────────
        int solidSel
        {
            get => spec != null ? spec.previewSolidSel : 0;
            set
            {
                if (spec == null || spec.previewSolidSel == value) return;
                spec.previewSolidSel = value;
                EditorUtility.SetDirty(spec);
            }
        }

        LatheSolid SelSolid
        {
            get
            {
                if (spec == null || spec.solids == null || spec.solids.Count == 0) return null;
                int clamped = Mathf.Clamp(spec.previewSolidSel, 0, spec.solids.Count - 1);
                if (clamped != spec.previewSolidSel) spec.previewSolidSel = clamped;
                return spec.solids[clamped];
            }
        }

        // ── layout ───────────────────────────────────────────────────────────────────
        protected override void BuildAsset(VisualElement root, LatheSpec s)
        {
            root.style.flexGrow = 1f;
            root.style.minHeight = 0f;

            var left = new ScrollView(ScrollViewMode.Vertical);
            left.style.minWidth = 320f;
            var col = left.contentContainer;
            col.style.flexGrow = 1f;

            BuildCanvasBox(col, s);
            BuildSolidsList(col, s);

            var sel = SelSolid;
            if (sel != null)
            {
                col.Add(BuildTransformBox(sel));
                col.Add(BuildFillBox(sel));
                BuildModuleBox(col, sel);
                BuildModifiersBox(col, sel);
            }

            var rightPane = new VisualElement();
            rightPane.style.flexGrow = 1f;
            rightPane.style.minWidth = 260f;
            rightPane.style.minHeight = 0f;

            preview = new IMGUIContainer(() => DrawPreview(s));
            preview.style.flexGrow = 1f;
            preview.style.minHeight = 220f;
            preview.AddToClassList("zui-stage");
            preview.tooltip = "Drag to orbit, scroll to zoom. The turntable spin (below) is independent of this camera.";
            rightPane.Add(preview);

            var chrome = new VisualElement();
            chrome.style.flexShrink = 0f;
            BuildTransport(chrome, s);
            chrome.Add(Z.Field("Background", "The preview's clear colour — cosmetic, never baked.",
                Z.Color(s.previewBackground, "The preview's clear colour.",
                    v => DirtyRepaintOnly(() => s.previewBackground = v), 110f)));
            rightPane.Add(chrome);

            root.Add(Z.Split("lathe.split", 340f, left, rightPane));
        }

        void BuildCanvasBox(VisualElement root, LatheSpec s)
        {
            var box = Z.Section("Canvas", "Output settings — canvasSize/PPU are a placeholder for an eventual "
                + "sprite bake and aren't read by anything yet; Seed is available to any module/modifier that "
                + "wants deterministic randomness.", "lathe.canvas", icon: "frame-corners");
            box.Add(Z.HGroup(
                Z.MicroSlider("Size", s.canvasSize, 16f, 256f,
                    "Square output resolution in pixels — reserved for a future sprite bake, not read yet.",
                    v => Dirty(() => s.canvasSize = Mathf.Clamp(Mathf.RoundToInt(v), 16, 256)), 150f, showValue: true, decimals: 0),
                Z.MicroSlider("PPU", s.pixelsPerUnit, 1f, 64f, "Pixels per unit — reserved for a future sprite bake.",
                    v => Dirty(() => s.pixelsPerUnit = Mathf.Clamp(v, 1f, 64f)), 150f, showValue: true)));
            box.Add(Z.HGroup(
                Z.MicroSlider("Turntable frames", s.turntableFrames, 1f, 120f,
                    "How many steps the turntable spin is divided into — drives the transport's scrub range below.",
                    v => { Dirty(() => s.turntableFrames = Mathf.Clamp(Mathf.RoundToInt(v), 1, 120)); RefreshTransportReadout(); },
                    170f, showValue: true, decimals: 0),
                Z.Field("Seed", "Reserved for any module/modifier that wants deterministic randomness.",
                    Z.Int(s.seed, "Random seed.", v => Dirty(() => s.seed = v), 70f))));
            root.Add(box);
        }

        VisualElement BuildTransformBox(LatheSolid solid)
        {
            var box = Z.Section("Transform", "This solid's position, rotation, scale and tint within the shared scene.",
                "lathe.transform", icon: "move");
            box.Add(Z.Toggle(showGizmo ? "Move Gizmo — drag an axis in the preview" : "Show Move Gizmo",
                "Draw a draggable red/green/blue axis handle over this solid in the preview — drag an axis "
                + "line instead of typing X/Y/Z. Freezes the turntable at 0 while showing (and the camera "
                + "stops responding to the left mouse button, same as Edit Skeleton) so drags land correctly.",
                showGizmo, v => { showGizmo = v; preview?.MarkDirtyRepaint(); }));
            box.Add(Vector3Row("Position", solid.position,
                "World position of this solid's pivot (also orbits with the turntable spin).",
                v => Dirty(() => solid.position = v)));
            box.Add(Vector3Row("Rotation", solid.rotationEuler, "Euler rotation, in degrees.",
                v => Dirty(() => solid.rotationEuler = v)));
            box.Add(Vector3Row("Scale", solid.scale, "Non-uniform scale.",
                v => Dirty(() => solid.scale = v)));
            box.Add(Z.Field("Tint", "This solid's colour — multiplies the texture below, or shows alone when no texture is set.",
                Z.Color(solid.tint, "This solid's colour.", v => Dirty(() => solid.tint = v), 110f)));
            box.Add(Z.HGroup(
                Z.MicroSlider("Metallic", solid.metallic, 0f, 1f,
                    "0 = a plain dielectric surface (plastic, wood, stone). 1 = a bare metal — its colour comes "
                    + "entirely from reflections, not the Tint above.",
                    v => Dirty(() => solid.metallic = v), 150f, showValue: true),
                Z.MicroSlider("Smoothness", solid.smoothness, 0f, 1f,
                    "How mirror-like the surface is — low is a rough, diffuse scatter; high is a sharp, "
                    + "clear reflection. Most visible when Metallic is also high.",
                    v => Dirty(() => solid.smoothness = v), 150f, showValue: true)));
            box.Add(Z.Field("Texture", "Optional — a box/triplanar-projected UV set is generated automatically, "
                + "so any texture drops straight on with no per-shape unwrap work.",
                ZuiReflect.ObjectByType(typeof(Texture2D), solid.texture,
                    "This solid's surface texture.", v => Dirty(() => solid.texture = v as Texture2D), 160f)));

            // Both below are OFF by default and draw NOTHING beyond their own toggle until enabled — the
            // avoid-bloat rule: an optional/modular feature's controls don't exist in the UI unless opted into.
            box.Add(Z.Toggle("Animate Texture", "Scrolls the surface texture/fill's UV offset over the "
                + "turntable's own frame — a tiled texture visibly scrolls across the baked sprite strip, not "
                + "just in the live preview.", solid.animateTexture, v => { Dirty(() => solid.animateTexture = v); Rebuild(); }));
            if (solid.animateTexture)
                box.Add(Z.HGroup(
                    Z.Field("Scroll Speed", "UV units scrolled per full turntable loop.", Z.Row(
                        Z.Float(solid.scrollSpeed.x, "Scroll speed (U).", v => Dirty(() => solid.scrollSpeed = new Vector2(v, solid.scrollSpeed.y)), 60f),
                        Z.Float(solid.scrollSpeed.y, "Scroll speed (V).", v => Dirty(() => solid.scrollSpeed = new Vector2(solid.scrollSpeed.x, v)), 60f))),
                    Z.MicroSlider("Tile", solid.tileScale, 0.2f, 10f, "Texture tiling repeat count.",
                        v => Dirty(() => solid.tileScale = v), 130f, showValue: true)));

            box.Add(Z.Toggle("Second Texture Layer", "Blends a second texture over the one above, each "
                + "independently tiled and scrolled — a scrolling light strip decal over a slower conveyor "
                + "tile, for example. Composited into one baked texture at render time, no shader needed.",
                solid.secondTextureLayer, v => { Dirty(() => solid.secondTextureLayer = v); Rebuild(); }));
            if (solid.secondTextureLayer)
            {
                box.Add(Z.Field("Layer 2 Texture", "The second texture, blended over the surface's base texture.",
                    ZuiReflect.ObjectByType(typeof(Texture2D), solid.texture2,
                        "This layer's own texture.", v => Dirty(() => solid.texture2 = v as Texture2D), 160f)));
                box.Add(Z.HGroup(
                    Z.Field("Blend", "How Layer 2 combines with the base texture.",
                        Z.MiniRadio((int)solid.blendMode, Enum.GetNames(typeof(LatheBlendMode)),
                            "Blend mode.", i => Dirty(() => solid.blendMode = (LatheBlendMode)i))),
                    Z.MicroSlider("Amount", solid.blendAmount, 0f, 1f, "How strongly Layer 2 shows through.",
                        v => Dirty(() => solid.blendAmount = v), 130f, showValue: true)));
                box.Add(Z.Toggle("Animate Layer 2", "Scrolls Layer 2's own UV offset over the turntable's "
                    + "frame, independently of Layer 1's own scroll above.",
                    solid.animateTexture2, v => { Dirty(() => solid.animateTexture2 = v); Rebuild(); }));
                if (solid.animateTexture2)
                    box.Add(Z.HGroup(
                        Z.Field("Scroll Speed", "UV units scrolled per full turntable loop.", Z.Row(
                            Z.Float(solid.scrollSpeed2.x, "Scroll speed (U).", v => Dirty(() => solid.scrollSpeed2 = new Vector2(v, solid.scrollSpeed2.y)), 60f),
                            Z.Float(solid.scrollSpeed2.y, "Scroll speed (V).", v => Dirty(() => solid.scrollSpeed2 = new Vector2(solid.scrollSpeed2.x, v)), 60f))),
                        Z.MicroSlider("Tile", solid.tileScale2, 0.2f, 10f, "Layer 2's own tiling repeat count.",
                            v => Dirty(() => solid.tileScale2 = v), 130f, showValue: true)));
            }

            box.Add(Z.Toggle("Emit Light", "Makes this solid glow AND spawns a real point light at its position "
                + "that illuminates neighbouring solids too — a light bulb that actually lights up the hull "
                + "around it, not just a bright mesh.", solid.emitLight, v => { Dirty(() => solid.emitLight = v); Rebuild(); }));
            if (solid.emitLight)
                box.Add(Z.HGroup(
                    Z.Field("Colour", "The light's colour, and the surface glow's tint.",
                        Z.Color(solid.lightColor, "Light colour.", v => Dirty(() => solid.lightColor = v), 100f)),
                    Z.MicroSlider("Glow", solid.emissiveBoost, 0f, 5f, "How bright the surface itself glows (colour × this).",
                        v => Dirty(() => solid.emissiveBoost = v), 120f, showValue: true),
                    Z.MicroSlider("Intensity", solid.lightIntensity, 0f, 10f, "How brightly it lights up neighbouring solids.",
                        v => Dirty(() => solid.lightIntensity = v), 130f, showValue: true),
                    Z.MicroSlider("Range", solid.lightRange, 0.1f, 20f, "How far the light reaches.",
                        v => Dirty(() => solid.lightRange = v), 120f, showValue: true)));
            return box;
        }

        VisualElement BuildFillBox(LatheSolid solid)
        {
            var box = Z.Section("Fill", "A procedural gradient — generated instead of imported, and (when its "
                + "Kind isn't None) takes over the surface from the Texture field above.", "lathe.fill", icon: "palette");
            solid.fill ??= new LatheSurfaceFill();
            var opt = new ZuiReflect.Options
            {
                OnBeforeChange = () => { if (spec != null) Undo.RecordObject(spec, "Edit Lathe"); },
                OnChanged = () => { if (spec != null) EditorUtility.SetDirty(spec); preview?.MarkDirtyRepaint(); },
                OnStructureChanged = Rebuild,
                TooltipFor = f => $"{ObjectNames.NicifyVariableName(f.Name)} — a Fill parameter.",
                ControlWidth = 150f,
            };
            ZuiReflect.FlowFields(box, solid.fill, opt);
            return box;
        }

        static VisualElement Vector3Row(string label, Vector3 v, string tooltip, Action<Vector3> onChanged)
        {
            var x = Z.Float(v.x, tooltip + " (X)", nv => onChanged(new Vector3(nv, v.y, v.z)), 60f);
            var y = Z.Float(v.y, tooltip + " (Y)", nv => onChanged(new Vector3(v.x, nv, v.z)), 60f);
            var z = Z.Float(v.z, tooltip + " (Z)", nv => onChanged(new Vector3(v.x, v.y, nv)), 60f);
            return Z.Field(label, tooltip, Z.Row(
                Z.Field("X", tooltip + " (X)", x), Z.Field("Y", tooltip + " (Y)", y), Z.Field("Z", tooltip + " (Z)", z)));
        }

        // ── transport ────────────────────────────────────────────────────────────────
        void BuildTransport(VisualElement root, LatheSpec s)
        {
            transportHost = new VisualElement();
            root.Add(transportHost);
            RebuildTransport(s);
        }

        void RebuildTransport(LatheSpec s)
        {
            if (transportHost == null) return;
            transportHost.Clear();
            playButton = Z.Button(playing ? "❚❚ Pause" : "▶ Play", "Play or pause the turntable spin.", () =>
            {
                playing = !playing;
                playButton.text = playing ? "❚❚ Pause" : "▶ Play";
            });
            var speedMs = Z.MicroSlider("Speed", s.previewFps, 1f, 30f,
                "Turntable spin speed, in frames per second.",
                v => Dirty(() => s.previewFps = Mathf.Clamp(Mathf.Round(v), 1f, 30f)), 150f, showValue: true, decimals: 0);
            var bakeBtn = Z.Button("Bake Sprite Strip…",
                "Render every turntable frame at Canvas Size, from the camera angle you're currently looking "
                + "from (only the subject spins — the camera stays fixed, the correct sprite-sheet convention), "
                + "and save it as one PNG strip (one tile per frame) ready for Unity's Sprite Editor grid slicer.",
                () => BakeSpriteStrip(s));
            transportHost.Add(Z.HGroup(playButton, speedMs, bakeBtn));

            int fcHigh = Mathf.Max(1, s.turntableFrames);
            scrubSlider = Z.SliderInt(Mathf.Clamp(frame, 0, fcHigh - 1) + 1, 1, fcHigh,
                "Scrub the turntable to an exact frame — dragging pauses playback.",
                v =>
                {
                    frame = Mathf.Clamp(v - 1, 0, Mathf.Max(0, s.turntableFrames - 1));
                    playing = false;
                    if (playButton != null) playButton.text = "▶ Play";
                    preview?.MarkDirtyRepaint();
                    RefreshTransportReadout();
                }, 200f);
            // The readout goes IN the scrubber's row, last (variable-width content trails), not on a row of its
            // own: it is the same scalar the slider is showing, and a whole line for "frame 14/24" is a line the
            // transport does not get back. Shaper packs its scrubber and readout into one row for the same
            // reason (ShaperWindow.cs, "the 'frame N/M' readout goes LAST in the row").
            frameReadout = Z.Text("", ZuiText.Subtle, "The turntable frame currently shown / the total frame count.");
            transportHost.Add(Z.Field("Frame", "Scrub the turntable to an exact frame.",
                Z.Row(scrubSlider, frameReadout)));
            RefreshTransportReadout();
        }

        void BakeSpriteStrip(LatheSpec s)
        {
            if (s == null) return;
            var strip = LatheBaker.BakeStrip(s, orbitYaw, orbitPitch);
            if (strip == null) return;
            string path = EditorUtility.SaveFilePanel("Export Lathe Sprite Strip", "",
                (s.name ?? "Lathe") + "_strip.png", "png");
            if (!string.IsNullOrEmpty(path))
            {
                File.WriteAllBytes(path, strip.EncodeToPNG());
                EditorUtility.RevealInFinder(path);
            }
            UnityEngine.Object.DestroyImmediate(strip);
        }

        void RefreshTransportReadout()
        {
            if (spec == null) return;
            int fc = Mathf.Max(1, spec.turntableFrames);
            int cur = Mathf.Clamp(frame, 0, fc - 1);
            if (scrubSlider != null)
            {
                scrubSlider.highValue = fc;
                scrubSlider.SetValueWithoutNotify(cur + 1);
            }
            if (frameReadout != null) frameReadout.text = $"frame {cur + 1}/{fc}";
        }

        // ── preview draw + orbit input (plain IMGUI — the preview island is an IMGUIContainer) ──────
        void DrawPreview(LatheSpec s)
        {
            var rect = GUILayoutUtility.GetRect(10, 10, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            if (rect.width < 2f || rect.height < 2f) return;

            var skelSolid = editingSkeleton ? SelSolid : null;
            var skel = skelSolid?.module as SkeletonSweepModule;
            var gizmoSolid = showGizmo && skel == null ? SelSolid : null;
            HandleOrbitInput(rect, allowLeftButton: skel == null && gizmoSolid == null);

            previewRenderer ??= new LathePreview();
            // Both the skeleton editor and the move gizmo measure clicks against the turntable frozen at 0
            // — spinning it while editing would mean the world-space ray no longer matches what's on screen.
            float turntableDeg = skel != null || gizmoSolid != null ? 0f
                : s.turntableFrames > 0 ? frame / (float)s.turntableFrames * 360f : 0f;
            float animT = s.turntableFrames > 0 ? frame / (float)s.turntableFrames : 0f;
            var tex = previewRenderer.Render(s, rect, turntableDeg, orbitYaw, orbitPitch, orbitDist, animT);
            if (tex != null) GUI.DrawTexture(rect, tex, ScaleMode.StretchToFill, true);

            if (skel != null) HandleSkeletonEditorInput(rect, skelSolid, skel);
            else if (gizmoSolid != null) DrawAndHandleGizmo(rect, gizmoSolid);
        }

        void HandleOrbitInput(Rect rect, bool allowLeftButton = true)
        {
            var e = Event.current;
            int id = GUIUtility.GetControlID(FocusType.Passive, rect);
            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (rect.Contains(e.mousePosition) && ((e.button == 0 && allowLeftButton) || e.button == 2))
                    {
                        GUIUtility.hotControl = id;
                        e.Use();
                    }
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == id)
                    {
                        orbitYaw += e.delta.x * 0.5f;
                        orbitPitch = Mathf.Clamp(orbitPitch - e.delta.y * 0.5f, -89f, 89f);
                        preview?.MarkDirtyRepaint();
                        e.Use();
                    }
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == id) { GUIUtility.hotControl = 0; e.Use(); }
                    break;
                case EventType.ScrollWheel:
                    if (rect.Contains(e.mousePosition))
                    {
                        orbitDist = Mathf.Clamp(orbitDist + e.delta.y * 0.3f, 0.5f, 40f);
                        preview?.MarkDirtyRepaint();
                        e.Use();
                    }
                    break;
            }
        }
    }
}
