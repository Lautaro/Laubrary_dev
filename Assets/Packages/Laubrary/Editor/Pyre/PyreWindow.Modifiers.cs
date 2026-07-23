// PyreWindow.Modifiers — the modifier stack UI (global + per-layer + the single simulation slot) and
// every per-modifier body. Part of the UI Toolkit port; see PyreWindow.cs.
using System.Collections.Generic;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Pyre.Editor
{
    public partial class PyreWindow
    {
        // Single in-memory clipboard (last-copied wins) — static so it survives closing/reopening the
        // window within the session, like a real clipboard.
        static PyreModifier modifierClipboard;

        // Modifiers whose ENTIRE body is one animatable value — drawn inline in the header row.
        static bool TrySingleValueBody(PyreModifier m, out ZUIValue v, out float lo, out float hi, out float def)
        {
            v = null; lo = 0f; hi = 1f; def = 0f;
            switch (m)
            {
                case SkewModifier s: v = s.amount; lo = -2f; hi = 2f; def = 0f; return true;
                case ContrastModifier cm: v = cm.amount; lo = 0f; hi = 2f; def = 1f; return true;
                case BrightnessModifier bm: v = bm.amount; lo = 0f; hi = 2f; def = 1f; return true;
                case SaturationModifier sm: v = sm.amount; lo = 0f; hi = 2f; def = 1f; return true;
                case OrderedDitherModifier od: v = od.strength; lo = 0f; hi = 1f; def = 1f; return true;
                default: return false;
            }
        }

        void BuildModifiers(VisualElement root, List<PyreModifier> list, bool isGlobal)
        {
            if (list == null) return;
            float half = spec.canvasSize * 0.5f;

            var listHost = new VisualElement();
            root.Add(listHost);
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == null) { int nullAt = i; Dial("Remove modifier", () => list.RemoveAt(nullAt)); RebuildLeft(); return; }
                listHost.Add(BuildModifierBlock(listHost, list, i, half));
            }

            var pasteButton = Z.Button(
                $"Paste{(modifierClipboard != null ? " " + modifierClipboard.DisplayName : "")}",
                "Append a copy of the last-copied modifier.",
                () =>
                {
                    if (modifierClipboard == null) return;
                    Dial("Paste modifier", () => list.Add(modifierClipboard.Clone()));
                    RebuildLeft();
                });
            pasteButton.SetEnabled(modifierClipboard != null);
            root.Add(WrapRow(
                Z.Button("+ Add modifier", "Add a geometry/colour/alpha/post modifier to this stack.",
                    () => ShowAddModifierMenu(list, isGlobal)),
                pasteButton));
        }

        VisualElement BuildModifierBlock(VisualElement listHost, List<PyreModifier> list, int index, float half)
        {
            var m = list[index];
            bool single = TrySingleValueBody(m, out var singleVal, out float singleLo, out float singleHi, out float singleDef);

            var box = Z.Box(null, null);

            var header = new VisualElement();
            header.AddToClassList("zui-row");

            var grip = Z.Text("≡", ZuiText.Body, "Drag to reorder this modifier within its stack.");
            grip.style.unityFontStyleAndWeight = FontStyle.Bold;
            grip.style.width = 16f;
            ZuiReorder.MakeGrip(grip, box, listHost, (from, to) =>
            {
                Dial("Reorder modifier", () =>
                {
                    var mm = list[from];
                    list.RemoveAt(from);
                    list.Insert(to, mm);
                });
                RebuildLeft();
            });
            header.Add(grip);

            header.Add(Z.Toggle("", "Enable or disable this modifier.", m.enabled, v =>
            {
                Dial(v ? "Enable modifier" : "Disable modifier", () => m.enabled = v);
                RebuildLeft();
            }));
            header.Add(Z.Text(m.DisplayName, ZuiText.Body, m.DisplayName + " modifier."));
            if (single && m.enabled)
                header.Add(PackedVal("", m.DisplayName + " amount.", singleVal, singleLo, singleHi, singleDef));
            header.Add(Z.Flexible());
            header.Add(Z.Button("Copy", "Copy this modifier's settings to the modifier clipboard.", () =>
            {
                modifierClipboard = m.Clone();
                RebuildLeft();   // refresh the Paste button's label/enabled state
            }).W(40f));
            header.Add(Z.Button("X", "Delete this modifier (undoable).", () =>
            {
                if (m == paintSmudge) { paintSmudge = null; draggingSmudge = false; }
                if (m == editPin) { editPin = null; pinSel = -1; draggingPin = false; }
                if (ReferenceEquals(m, editCurl)) { editCurl = null; vortexSel = -1; draggingVortex = false; }
                int at = list.IndexOf(m);
                if (at >= 0) Dial("Remove modifier", () => list.RemoveAt(at));
                RebuildLeft();
            }).W(22f));
            box.Add(header);

            if (m.enabled && !single) BuildModBody(box, m, half);
            return box;
        }

        void BuildSimulationModifier(VisualElement root, string label,
            System.Func<PyreModifier> get, System.Action<PyreModifier> set)
        {
            root.Add(Z.Text(label, ZuiText.Section,
                "The one always-last simulation slot — a genuinely iterative, stateful modifier."));
            var current = get();
            if (current == null)
            {
                root.Add(Z.Button("+ Add simulation", "Add the pixel-fluid simulation modifier to this slot.", () =>
                {
                    Dial("Add simulation modifier", () => set(new PixelFluidModifier()));
                    RebuildLeft();
                }));
                return;
            }

            var box = Z.Box(null, null);
            var header = new VisualElement();
            header.AddToClassList("zui-row");
            header.Add(Z.Toggle("", "Enable or disable the simulation.", current.enabled, v =>
            {
                Dial(v ? "Enable simulation" : "Disable simulation", () => current.enabled = v);
                RebuildLeft();
            }));
            header.Add(Z.Text(current.DisplayName, ZuiText.Body, current.DisplayName + " simulation."));
            header.Add(Z.Flexible());
            header.Add(Z.Button("X", "Remove the simulation modifier (undoable).", () =>
            {
                Dial("Remove simulation modifier", () => set(null));
                RebuildLeft();
            }).W(22f));
            box.Add(header);
            if (current.enabled) BuildModBody(box, current, spec.canvasSize * 0.5f);
            root.Add(box);
        }

        // ── per-modifier bodies ─────────────────────────────────────────────────────────────
        void BuildModBody(VisualElement box, PyreModifier m, float half)
        {
            ZuiValue2DControl.Options Range2D(float lo, float hi) =>
                new ZuiValue2DControl.Options().WithRange(lo, hi, lo, hi).WithDefault(Vector2.zero);

            switch (m)
            {
                case ScaleModifier sc:
                    box.Add(Z.MiniRadio((int)sc.axis, ScaleAxisLabels, "Which axis the scale applies to.",
                        v => { Dial("Scale axis", () => sc.axis = (ScaleAxis)v); RebuildLeft(); }));
                    switch (sc.axis)
                    {
                        case ScaleAxis.Vertical: box.Add(ValRow("Vertical", "Vertical ×scale.", sc.vertical, 0f, 3f, 1f)); break;
                        case ScaleAxis.Horizontal: box.Add(ValRow("Horizontal", "Horizontal ×scale.", sc.horizontal, 0f, 3f, 1f)); break;
                        default: box.Add(ValRow("Both", "Uniform ×scale.", sc.both, 0f, 3f, 1f)); break;
                    }
                    break;
                case RotateModifier r:
                    box.Add(ValRow("Degrees", "Rotation in degrees (animatable).", r.degrees, -180f, 180f, 0f));
                    box.Add(Z.Field("Pivot", "The point the rotation turns around (plain value — not animatable).",
                        Z.Pad(new Vector2(r.pivotX, r.pivotY), new Rect(-1f, -1f, 2f, 2f),
                            "Drag to set the rotation pivot.",
                            v => Dial("Rotate pivot", () => { r.pivotX = v.x; r.pivotY = v.y; }))));
                    break;
                case WobbleModifier w:
                    box.Add(ValRow("Amplitude", "Wobble displacement in pixels.", w.amplitude, 0f, Mathf.Max(4f, half), 0f));
                    box.Add(ValRow("Frequency", "Wobble cycles per life.", w.frequency, 0f, 8f, 1f));
                    break;
                case SunburstWobbleModifier sw:
                    box.Add(ValRow("Amplitude", "Beam displacement in pixels.", sw.amplitude, 0f, Mathf.Max(4f, half), 3f));
                    box.Add(ValRow("Frequency (beams)", "How many beams around the circle.", sw.frequency, 1f, 24f, 6f));
                    box.Add(ValRow("Rotation", "Rotates the beam pattern.", sw.rotation, -180f, 180f, 0f));
                    break;
                case ProfileModifier pm:
                    pm.widthByHeight ??= ProfileModifier.DefaultProfile();
                    box.Add(Z.Field("Width by height", "The silhouette profile — width multiplier from bottom to top.",
                        Z.Envelope(pm.widthByHeight, new ZuiEnvelopeOptions { yMin = 0f, yMax = 2f },
                            "The silhouette profile — width multiplier from bottom to top.",
                            DirtySpec, RecordSpec, 200f, 70f)));
                    box.Add(ValRow("Strength", "How strongly the profile molds the shape.", pm.strength, 0f, 1f, 1f));
                    break;
                case GroundModifier g:
                    box.Add(ValRow("Grow angle", "Direction the shape grows from the surface.", g.angle, -180f, 180f, 0f));
                    box.Add(PackedSlider("Surface", "Where the ground surface sits (-1 bottom … 1 top).", g.surface, -1f, 1f, v => g.surface = v, 150f));
                    box.Add(ValRow("Stretch (height)", "Vertical stretch away from the surface.", g.stretch, 0f, 4f, 1f));
                    box.Add(PackedSlider("Bury base", "How much of the base hides below the surface.", g.bury, 0f, 1f, v => g.bury = v, 150f));
                    break;
                case TintModifier t:
                    box.Add(Z.Field("Tint", "Multiplies every pixel's colour.",
                        Z.Color(t.tint, "Multiplies every pixel's colour.",
                            v => Dial("Tint", () => t.tint = v))));
                    t.crossGradient ??= Layer.WhiteGradient();
                    box.Add(GradientRow("Cross grad", "A gradient crossed over the frame.",
                        () => t.crossGradient, v => t.crossGradient = v));
                    box.Add(ValRow("Cross amount", "How strongly the cross gradient applies.", t.crossAmount, 0f, 1f, 1f));
                    break;
                case DissolveModifier d:
                    box.Add(ValRow("Amount", "Fraction of pixels dissolved away.", d.amount, 0f, 1f, 0f));
                    box.Add(Z.MiniRadio((int)d.mode, DissolveModeLabels, "Erase removes pixels in place; Scatter flings them.",
                        v => Dial("Dissolve mode", () => d.mode = (DissolveMode)v)));
                    box.Add(ValRow("Smoothness", "Softens the dissolve pattern.", d.smoothness, 0f, 1f, 0f));
                    break;
                case LayerDissolveModifier ld:
                    box.Add(ValRow("Amount", "Fraction of pixels dissolved away.", ld.amount, 0f, 1f, 0f));
                    box.Add(Z.MiniRadio((int)ld.mode, DissolveModeLabels, "Erase removes pixels in place; Scatter flings them.",
                        v => Dial("Dissolve mode", () => ld.mode = (DissolveMode)v)));
                    box.Add(ValRow("Smoothness", "Softens the dissolve pattern.", ld.smoothness, 0f, 1f, 0f));
                    break;
                case AlphaMaskModifier am:
                    box.Add(Z.MiniRadio((int)am.shape, MaskShapeLabels, "The mask's shape/wipe pattern.",
                        v => { Dial("Mask shape", () => am.shape = (MaskShape)v); RebuildLeft(); }));
                    box.Add(ValRow("Progress", "How far the mask has progressed (0 hidden … 1 fully shown).", am.progress, 0f, 1f, 1f));
                    box.Add(PackedSlider("Sharpness", "Hardness of the mask's edge.", am.sharpness, 0f, 1f, v => am.sharpness = v, 150f));
                    box.Add(ValRow("Size", "×scale on the mask's footprint.", am.size, 0.1f, 4f, 1f));
                    box.Add(ValRow("Rotation", "Rotates the mask.", am.rotation, -180f, 180f, 0f));
                    box.Add(Z.Field("Offset", "Moves the mask off-centre (plain value — not animatable).",
                        Z.Pad(new Vector2(am.offsetX, am.offsetY), new Rect(-1f, -1f, 2f, 2f),
                            "Drag to offset the mask.",
                            v => Dial("Mask offset", () => { am.offsetX = v.x; am.offsetY = v.y; }))));
                    if (am.shape == MaskShape.Noise)
                    {
                        box.Add(PackedSlider("Noise warp", "Distorts the noise mask into itself.", am.noiseWarp, 0f, 2f, v => am.noiseWarp = v, 150f));
                        box.Add(Z.Value2D("Noise drift", am.noiseDriftX, am.noiseDriftY, Range2D(-64f, 64f),
                            "Scrolls the noise mask over time.", DirtySpec, RecordSpec));
                    }
                    break;
                case BloomModifier bm:
                    box.Add(PackedSlider("Threshold", "Brightness above which pixels bloom.", bm.threshold, 0f, 1f, v => bm.threshold = v, 150f));
                    box.Add(PackedSlider("Radius (px)", "Bloom spread radius.", bm.radius, 0f, 16f, v => bm.radius = Mathf.RoundToInt(v), 150f));
                    box.Add(ValRow("Intensity", "Bloom strength.", bm.intensity, 0f, 3f, 1.2f));
                    break;
                case OutlineModifier om:
                    box.Add(Z.MiniRadio((int)om.mode, OverLifeFillLabels, "How the outline's gradient reads: over life, or spatially in→out.",
                        v => { Dial("Outline mode", () => om.mode = (ColorMode)v); RebuildLeft(); }));
                    om.color ??= new Gradient();
                    box.Add(GradientRow(om.mode == ColorMode.OverLife ? "Colour (over life)" : "Colour (in→out)",
                        "The outline's colour ramp.", () => om.color, v => om.color = v));
                    box.Add(ValRow("Size (px)", "Outline thickness in pixels.", om.size, 0f, 12f, 1f));
                    box.Add(PackedSlider("Edge sensitivity", "Alpha threshold that counts as an edge.", om.alphaThreshold, 0.01f, 1f, v => om.alphaThreshold = v, 150f));
                    box.Add(PackedSlider("Inner softness (px)", "Fade distance inside the edge.", om.innerSoftness, 0f, 16f, v => om.innerSoftness = v, 150f));
                    box.Add(PackedSlider("Inner curve", "Falloff curve of the inner fade.", om.innerSoftnessCurve, 0.2f, 5f, v => om.innerSoftnessCurve = v, 150f));
                    box.Add(PackedSlider("Outer softness (px)", "Fade distance outside the edge.", om.outerSoftness, 0f, 16f, v => om.outerSoftness = v, 150f));
                    box.Add(PackedSlider("Outer curve", "Falloff curve of the outer fade.", om.outerSoftnessCurve, 0.2f, 5f, v => om.outerSoftnessCurve = v, 150f));
                    break;
                case JaggModifier jm:
                    box.Add(PackedSlider("Arms", "How many star arms.", jm.arms, 2f, 24f, v => jm.arms = Mathf.RoundToInt(v), 150f));
                    box.Add(ValRow("Strength", "How deep the jagging cuts.", jm.strength, 0f, 0.95f, 0.4f));
                    box.Add(ValRow("Twist", "Rotates the arms over life.", jm.twist, -180f, 180f, 0f));
                    break;
                case SmudgeModifier sm:
                    box.Add(ValRow("Brush size (px)", "Stroke brush radius.", sm.size, 1f, half, 12f));
                    box.Add(ValRow("Strength (px)", "How far pixels smear along the stroke.", sm.strength, 0f, half, 12f));
                    box.Add(ValRow("Grow", "0→1 front advancing along each stroke (all strokes in parallel).", sm.grow, 0f, 1f, 1f));
                    bool painting = paintSmudge == sm;
                    box.Add(WrapRow(
                        Z.Button(painting ? "● Painting — drag to add strokes" : "○ Paint stroke",
                            "Arm stroke painting, then drag in the preview — each drag records a new stroke.",
                            () => { paintSmudge = painting ? null : sm; draggingSmudge = false; RebuildLeft(); }),
                        Z.Button("⌫ Last", "Remove the most recent stroke.", () =>
                        {
                            if (sm.strokes.Count == 0) return;
                            Dial("Remove stroke", () => sm.strokes.RemoveAt(sm.strokes.Count - 1));
                            RebuildLeft();
                        }).W(56f),
                        Z.Button("Clear", "Remove every stroke.", () =>
                        {
                            Dial("Clear strokes", () => sm.strokes.Clear());
                            RebuildLeft();
                        }).W(48f)));
                    box.Add(Z.Text($"{sm.strokes.Count} stroke(s)", ZuiText.Small, "How many strokes are recorded."));
                    break;
                case DropShadowModifier ds:
                    box.Add(Z.Field("Offset", "Shadow offset in pixels (plain value — not animatable).",
                        Z.Pad(new Vector2(ds.offsetX, ds.offsetY), new Rect(-16f, -16f, 32f, 32f),
                            "Drag to offset the shadow.",
                            v => Dial("Shadow offset", () => { ds.offsetX = v.x; ds.offsetY = v.y; }))));
                    box.Add(Z.Field("Shadow colour", "The shadow's colour.",
                        Z.Color(ds.color, "The shadow's colour.", v => Dial("Shadow colour", () => ds.color = v))));
                    box.Add(PackedSlider("Edge alpha", "Alpha threshold that counts as the silhouette.", ds.alphaThreshold, 0.01f, 1f, v => ds.alphaThreshold = v, 150f));
                    break;
                case PosterizeModifier pz:
                    box.Add(PackedSlider("Levels", "How many colour levels remain.", pz.levels, 2f, 16f, v => pz.levels = Mathf.RoundToInt(v), 150f));
                    box.Add(Z.Toggle("Affect alpha", "Also posterize the alpha channel.", pz.affectAlpha,
                        v => Dial("Affect alpha", () => pz.affectAlpha = v)));
                    break;
                case TurbulenceModifier tb:
                    box.Add(ValRow("Amplitude", "Churn displacement in pixels.", tb.amplitude, 0f, Mathf.Max(4f, half), 4f));
                    box.Add(ValRow("Zoom", "Churn feature size.", tb.zoom, 1f, Mathf.Max(8f, half * 2f), 24f));
                    box.Add(ValRow("Rotation", "Rotates the churn field.", tb.rotation, -720f, 720f, 0f));
                    box.Add(Z.Value2D("Offset", tb.offsetX, tb.offsetY, Range2D(-half, half),
                        "Scrolls the churn field.", DirtySpec, RecordSpec));
                    box.Add(ValRow("Warp", "Distorts the churn field into itself.", tb.warp, 0f, 2f, 0.6f));
                    break;
                case PerlinTurbulenceModifier pt:
                    box.Add(ValRow("Amplitude", "Churn displacement in pixels.", pt.amplitude, 0f, Mathf.Max(4f, half), 4f));
                    box.Add(ValRow("Zoom", "Churn feature size.", pt.zoom, 1f, Mathf.Max(8f, half * 2f), 24f));
                    box.Add(ValRow("Rotation", "Rotates the churn field.", pt.rotation, -720f, 720f, 0f));
                    box.Add(Z.Value2D("Offset", pt.offsetX, pt.offsetY, Range2D(-half, half),
                        "Scrolls the churn field.", DirtySpec, RecordSpec));
                    box.Add(ValRow("Warp", "Distorts the churn field into itself.", pt.warp, 0f, 2f, 0.6f));
                    break;
                case EdgeSmoothModifier es:
                    box.Add(ValRow("Radius (px)", "Smoothing kernel radius.", es.radius, 0f, 16f, 2f, allowMinMax: false));
                    box.Add(ValRow("Strength", "How strongly edges smooth.", es.strength, 0f, 1f, 1f));
                    break;
                case RingWaveModifier rw:
                    box.Add(ValRow("Amplitude", "Ripple displacement in pixels.", rw.amplitude, 0f, Mathf.Max(4f, half), 3f));
                    box.Add(ValRow("Wavelength", "Distance between ripple crests.", rw.wavelength, 1f, Mathf.Max(8f, half), 10f));
                    box.Add(ValRow("Phase (travel)", "Moves the ripples outward/inward over life.", rw.phase, -6f, 6f, 0f));
                    break;
                case PointBlastModifier bl:
                    box.Add(WrapRow(
                        PackedVal2D("Origin", "Where the blast pushes from.", bl.originX, bl.originY, Range2D(-half, half)),
                        PackedVal("Angle", "Direction of a line/arc blast.", bl.angleDeg, -180f, 180f, 0f, allowMinMax: false)));
                    box.Add(PackedSlider("Arc (360=disc, 0=line)", "The blast's angular coverage.", bl.arcDegrees, 0f, 360f, v => bl.arcDegrees = v, 150f));
                    box.Add(WrapRow(
                        PackedVal("Arc softness", "Softens the arc's angular edges.", bl.arcSoftness, 0f, 1f, 0.2f, allowMinMax: false),
                        PackedVal("Band width (px)", "Thickness of the pushing band.", bl.bandWidth, 1f, Mathf.Max(8f, half * 0.5f), 12f, allowMinMax: false)));
                    box.Add(WrapRow(
                        PackedVal("Radius (px)", "How far the band has travelled.", bl.radius, 0f, Mathf.Max(4f, half), 0f, allowMinMax: false),
                        PackedVal("Strength", "Push strength (negative pulls).", bl.strength, -20f, 20f, 6f, allowMinMax: false)));
                    break;
                case CloudProjectileModifier cpj:
                    box.Add(WrapRow(
                        PackedVal("Angle", "The projectile's travel direction.", cpj.angleDeg, -180f, 180f, 0f, allowMinMax: false),
                        PackedVal("Offset (px)", "Sideways offset of the flight path.", cpj.offset, -half, half, 0f, allowMinMax: false)));
                    box.Add(ValRow("Depth", "How far through the cloud the projectile has flown.", cpj.depth, 0f, 1f, allowMinMax: false));
                    box.Add(WrapRow(
                        PackedVal("Radius (px)", "The projectile's push radius.", cpj.radius, 1f, Mathf.Max(4f, half), 10f, allowMinMax: false),
                        PackedVal("Strength", "Push strength along the tunnel.", cpj.strength, -20f, 20f, 6f, allowMinMax: false)));
                    box.Add(ValRow("Density", "How much cloud resists the push.", cpj.density, 0f, 5f, 1f, allowMinMax: false));
                    break;
                case BallisticShockwaveModifier bs:
                    box.Add(Z.Text("Projectile", ZuiText.Section, "The projectile that punches through."));
                    box.Add(WrapRow(
                        PackedVal("Angle", "Travel direction.", bs.angleDeg, -180f, 180f, 0f, allowMinMax: false),
                        PackedVal("Offset (px)", "Sideways offset of the flight path.", bs.offset, -half, half, 0f, allowMinMax: false)));
                    box.Add(ValRow("Depth", "How far through the projectile has flown.", bs.depth, 0f, 1f, allowMinMax: false));
                    box.Add(WrapRow(
                        PackedVal("Radius (px)", "The projectile's radius.", bs.projectileRadius, 0.5f, Mathf.Max(4f, half * 0.3f), 3f, allowMinMax: false),
                        PackedVal("Force", "Push force along the tunnel.", bs.projectileForce, -20f, 20f, 6f, allowMinMax: false)));
                    box.Add(ValRow("Erosion", "How much material the passage erodes.", bs.erosion, 0f, 1f, 0.75f, allowMinMax: false));
                    box.Add(Z.Text("Shockwaves", ZuiText.Section, "Expanding rings shed behind the projectile."));
                    box.Add(WrapRow(
                        PackedVal("Spacing", "Depth interval between shed rings.", bs.waveSpacing, 0.01f, 0.5f, 0.06f, allowMinMax: false),
                        PackedVal("Strength", "Ring push strength.", bs.waveStrength, -20f, 20f, 5f, allowMinMax: false)));
                    box.Add(WrapRow(
                        PackedVal("Expansion (px)", "How far each ring expands.", bs.waveExpansion, 0f, Mathf.Max(8f, half), 30f, allowMinMax: false),
                        PackedVal("Decay", "How quickly rings fade.", bs.waveDecay, 0f, 20f, 6f, allowMinMax: false)));
                    box.Add(ValRow("Thickness (px)", "Each ring's band thickness.", bs.waveThickness, 0.5f, Mathf.Max(4f, half * 0.2f), 2.5f, allowMinMax: false));
                    box.Add(Z.Text("Vortices", ZuiText.Section, "The trailing vortex street."));
                    box.Add(WrapRow(
                        PackedVal("Spacing", "Depth interval between shed vortices.", bs.vortexSpacing, 0.01f, 0.5f, 0.05f, allowMinMax: false),
                        PackedVal("Strength", "Vortex swirl strength.", bs.vortexStrength, -20f, 20f, 8f, allowMinMax: false)));
                    box.Add(WrapRow(
                        PackedVal("Radius (px)", "Each vortex's radius.", bs.vortexRadius, 0.5f, Mathf.Max(8f, half * 0.4f), 8f, allowMinMax: false),
                        PackedVal("Decay", "How quickly vortices fade.", bs.vortexDecay, 0f, 20f, 5f, allowMinMax: false)));
                    box.Add(ValRow("Pulse", "Pulsates vortex strength.", bs.vortexPulse, 0f, 5f, 1f, allowMinMax: false));
                    break;
                case PixelFluidModifier pf:
                    box.Add(Z.Text("Projectile", ZuiText.Section, "The projectile that punches through the fluid."));
                    box.Add(WrapRow(
                        PackedVal("Angle", "Travel direction.", pf.angleDeg, -180f, 180f, 0f, allowMinMax: false),
                        PackedVal("Offset (px)", "Sideways offset of the flight path.", pf.offset, -half, half, 0f, allowMinMax: false)));
                    box.Add(ValRow("Depth", "How far through the projectile has flown.", pf.depth, 0f, 1f, allowMinMax: false));
                    box.Add(WrapRow(
                        PackedVal("Radius (px)", "The projectile's radius.", pf.projectileRadius, 0.5f, Mathf.Max(4f, half * 0.3f), 3f, allowMinMax: false),
                        PackedVal("Force", "Push force along the tunnel.", pf.projectileForce, -20f, 20f, 10f, allowMinMax: false)));
                    box.Add(WrapRow(
                        PackedVal("Erosion rate", "How quickly the passage erodes material.", pf.erosionRate, 0f, 1f, 0.35f, allowMinMax: false),
                        PackedVal("Erosion healing", "How quickly eroded material recovers.", pf.erosionHealing, 0f, 1f, 0.85f, allowMinMax: false)));
                    box.Add(Z.Text("Shockwaves", ZuiText.Section, "Expanding rings shed behind the projectile."));
                    box.Add(WrapRow(
                        PackedVal("Spacing", "Depth interval between shed rings.", pf.waveSpacing, 0.01f, 0.5f, 0.06f, allowMinMax: false),
                        PackedVal("Strength", "Ring push strength.", pf.waveStrength, -20f, 20f, 6f, allowMinMax: false)));
                    box.Add(WrapRow(
                        PackedVal("Expansion (px/f)", "Ring expansion per frame.", pf.waveExpansion, 0f, Mathf.Max(2f, half * 0.1f), 1.5f, allowMinMax: false),
                        PackedVal("Persistence", "How much of each ring survives per frame.", pf.wavePersistence, 0f, 1f, 0.9f, allowMinMax: false)));
                    box.Add(ValRow("Thickness (px)", "Each ring's band thickness.", pf.waveThickness, 0.5f, Mathf.Max(4f, half * 0.2f), 2.5f, allowMinMax: false));
                    box.Add(Z.Text("Vortices", ZuiText.Section, "The trailing vortex street."));
                    box.Add(WrapRow(
                        PackedVal("Spacing", "Depth interval between shed vortices.", pf.vortexSpacing, 0.01f, 0.5f, 0.05f, allowMinMax: false),
                        PackedVal("Strength", "Vortex swirl strength.", pf.vortexStrength, -20f, 20f, 10f, allowMinMax: false)));
                    box.Add(WrapRow(
                        PackedVal("Radius (px)", "Each vortex's radius.", pf.vortexRadius, 0.5f, Mathf.Max(8f, half * 0.4f), 8f, allowMinMax: false),
                        PackedVal("Persistence", "How much of each vortex survives per frame.", pf.vortexPersistence, 0f, 1f, 0.94f, allowMinMax: false)));
                    box.Add(WrapRow(
                        PackedVal("Drift (px/f)", "Vortex downstream drift per frame.", pf.vortexDrift, -5f, 5f, 0.6f, allowMinMax: false),
                        PackedSlider("Child shed", "Chance a vortex sheds a child per frame.", pf.childShedChance, 0f, 0.5f, v => pf.childShedChance = v)));
                    box.Add(Z.Text("Fluid", ZuiText.Section, "The velocity field itself."));
                    box.Add(WrapRow(
                        PackedVal("Drag", "Velocity damping per frame.", pf.velocityDrag, 0f, 1f, 0.85f, allowMinMax: false),
                        PackedVal("Viscosity", "Velocity smoothing between neighbours.", pf.viscosity, 0f, 1f, 0.25f, allowMinMax: false)));
                    box.Add(ValRow("Display scale", "×scale on the displayed displacement.", pf.displayScale, 0f, 5f, 1f, allowMinMax: false));
                    break;
                case SunburstModifier sb:
                    box.Add(PackedSlider("Rays", "How many rays around the silhouette.", sb.rays, 2f, 32f, v => sb.rays = Mathf.RoundToInt(v), 150f));
                    box.Add(ValRow("Strength", "How deep the rays cut.", sb.strength, 0f, 0.95f, 0.6f));
                    box.Add(PackedSlider("Sharpness", "Hardness of the ray edges.", sb.sharpness, 0.5f, 8f, v => sb.sharpness = v, 150f));
                    box.Add(ValRow("Rotation", "Rotates the ray pattern.", sb.rotation, -180f, 180f, 0f));
                    break;
                case PulseRingsModifier pr:
                    box.Add(PackedSlider("Rings", "How many concentric pulse rings.", pr.rings, 1f, 12f, v => pr.rings = Mathf.RoundToInt(v), 150f));
                    box.Add(ValRow("Speed", "Ring travel speed (negative = inward).", pr.speed, -4f, 4f, 1f));
                    box.Add(ValRow("Strength (px)", "Ring displacement in pixels.", pr.strength, 0f, Mathf.Max(4f, half), 3f));
                    break;
                case VoronoiCrackModifier vc:
                    box.Add(ValRow("Zoom", "Crack cell size.", vc.zoom, 1f, Mathf.Max(8f, half), 10f));
                    box.Add(ValRow("Rotation", "Rotates the crack field.", vc.rotation, -720f, 720f, 0f));
                    box.Add(Z.Value2D("Drift", vc.driftX, vc.driftY, Range2D(-half, half),
                        "Scrolls the crack field.", DirtySpec, RecordSpec));
                    box.Add(ValRow("Seed offset", "Reseeds the cell pattern.", vc.seedOffset, -8f, 8f, 0f));
                    box.Add(WrapRow(
                        PackedVal("Crack width", "Seam thickness.", vc.crackWidth, 0.01f, 3f, 0.15f, allowMinMax: false),
                        PackedVal("Seam sharpness", "Hardness of the seam edges.", vc.seamSharpness, 0.1f, 8f, 1f, allowMinMax: false)));
                    box.Add(Z.MiniRadio((int)vc.mode, OverLifeFillLabels, "How the tint gradient reads: over life, or seam→away.",
                        v => { Dial("Crack mode", () => vc.mode = (ColorMode)v); RebuildLeft(); }));
                    vc.crackTint ??= new Gradient();
                    box.Add(GradientRow(vc.mode == ColorMode.Fill ? "Tint (seam→away)" : "Tint (over life)",
                        "The seams' tint ramp.", () => vc.crackTint, v => vc.crackTint = v));
                    box.Add(ValRow("Strength", "How strongly the cracks apply.", vc.strength, 0f, 1f, 1f));
                    box.Add(Z.Toggle("Tint cells", "Also shade each cell's interior.", vc.tintCells,
                        v => { Dial("Tint cells", () => vc.tintCells = v); RebuildLeft(); }));
                    if (vc.tintCells)
                        box.Add(ValRow("Cell shade", "How strongly cells shade.", vc.cellShadeStrength, 0f, 1f, 0.25f));
                    box.Add(Z.MiniRadio((int)vc.spreadMode, CrackSpreadModeLabels, "How the cracks spread across the shape over Spread.",
                        v => { Dial("Crack spread", () => vc.spreadMode = (CrackSpreadMode)v); RebuildLeft(); }));
                    if (vc.spreadMode != CrackSpreadMode.Uniform)
                    {
                        box.Add(ValRow("Spread", "How far the cracks have spread.", vc.spreadProgress, 0f, 1f, 1f));
                        box.Add(PackedSlider("Spread softness", "Softens the spreading front.", vc.spreadSoftness, 0f, 1f, v => vc.spreadSoftness = v, 150f));
                    }
                    break;
                case ChromaticAberrationModifier ca:
                    box.Add(ValRow("Amount (px)", "Channel separation in pixels.", ca.amount, 0f, 8f, 1.5f));
                    box.Add(ValRow("Alpha", "Opacity of the separated fringes.", ca.alpha, 0f, 1f, 1f));
                    box.Add(Z.Toggle("Radial (from centre)", "Separate channels radially instead of along one angle.", ca.radial,
                        v => { Dial("Radial", () => ca.radial = v); RebuildLeft(); }));
                    if (!ca.radial)
                        box.Add(ValRow("Angle", "Separation direction.", ca.angleDeg, -180f, 180f, 0f));
                    break;
                case PinWarpModifier pw:
                    BuildPinWarpBody(box, pw);
                    break;
                case CurlModifier cu:
                    BuildCurlBody(box, cu, half);
                    break;
                case CurlProgressModifier cp:
                    BuildCurlProgressBody(box, cp, half);
                    break;
                case SphereModifier sp:
                    box.Add(ValRow("Strength", "Bulge strength (negative dents).", sp.strength, -5f, 5f, 1f, allowMinMax: false));
                    box.Add(WrapRow(
                        PackedVal2D("Origin", "The bulge's centre.", sp.originX, sp.originY, Range2D(-half, half)),
                        PackedVal("Radius (0=auto)", "The bulge's radius; 0 auto-fits.", sp.radius, 0f, Mathf.Max(4f, half), 0f, allowMinMax: false)));
                    break;
                case FuseModifier fu:
                    box.Add(ValRow("Radius (px)", "Melt influence radius.", fu.radius, 0f, Mathf.Max(4f, half * 0.5f), 4f));
                    box.Add(ValRow("Threshold", "Field level at which the melted surface forms.", fu.threshold, 0f, 1f, 0.5f));
                    box.Add(ValRow("Softness", "Softness of the melted surface's edge.", fu.softness, 0.02f, 1f, 0.3f));
                    box.Add(ValRow("Colour bleed", "How much colours blend where shapes melt.", fu.colorBleed, 0f, 1f, 0.4f));
                    break;
                case EdgeWarpModifier re:
                    box.Add(ValRow("Amplitude", "Silhouette displacement in pixels.", re.amplitude, 0f, Mathf.Max(4f, half * 0.3f), 2f));
                    box.Add(ValRow("Frequency", "Waves around the silhouette.", re.frequency, 1f, 24f, 6f));
                    box.Add(ValRow("Jaggedness", "Blends smooth waves toward jagged noise.", re.jaggedness, 0f, 1f, 0.5f));
                    box.Add(ValRow("Warp", "Distorts the wave field into itself.", re.warp, 0f, 2f, 0.4f));
                    box.Add(ValRow("Softness (px)", "Softens the warped edge.", re.softness, 0f, Mathf.Max(4f, half * 0.2f), 0f));
                    break;
            }
        }

        void BuildPinWarpBody(VisualElement parent, PinWarpModifier pw)
        {
            var box = Z.Box("Pin warp — click the preview to add / drag pins",
                "Hand-animated drag points: each pin records keyframes at the current scrubbed frame as you drag it in the preview.");
            bool editing = editPin == pw;
            box.Add(Z.Button(editing ? "● Editing pins — click preview to add, drag to move" : "○ Edit pins (click preview)",
                "Arm pin editing, then click the preview to add pins / drag them to keyframe.",
                () => { editPin = editing ? null : pw; pinSel = -1; draggingPin = false; RebuildLeft(); }));

            int curFrame = CurrentFrame();
            for (int i = 0; i < pw.dots.Count; i++)
            {
                int idx = i;
                var d = pw.dots[idx];
                if (d == null) continue;
                var pinBox = Z.Box(null, null);
                pinBox.Add(WrapRow(
                    Z.Button(pinSel == idx ? "●" : "○", "Select this pin.", () => { pinSel = idx; RebuildLeft(); }).W(24f),
                    Z.Text($"Pin #{d.id}", ZuiText.Small, "Pin id."),
                    PackedSlider("Radius", "This pin's radius of influence in pixels.", d.radius, 2f, 128f, v => d.radius = v, 130f),
                    Z.Button("X", "Delete this pin.", () =>
                    {
                        Dial("Remove pin", () => pw.dots.RemoveAt(idx));
                        if (pinSel == idx) pinSel = -1;
                        RebuildLeft();
                    }).W(22f)));
                bool hasKeyAtFrame = d.keyframes.Exists(k => k.frame == curFrame);
                var keyRow = WrapRow(Z.Text($"{d.keyframes.Count} keyframe(s)", ZuiText.Small, "How many keyframes this pin has."));
                var removeKey = Z.Button("Remove keyframe @ this frame", "Delete this pin's keyframe at the currently scrubbed frame.", () =>
                {
                    Dial("Remove keyframe", () => d.keyframes.RemoveAll(k => k.frame == curFrame));
                    RebuildLeft();
                });
                removeKey.SetEnabled(hasKeyAtFrame && d.keyframes.Count > 1);
                keyRow.Add(removeKey);
                pinBox.Add(keyRow);
                box.Add(pinBox);
            }
            box.Add(WrapRow(
                Z.Button("Clear pins", "Delete every pin.", () =>
                {
                    Dial("Clear pins", () => pw.dots.Clear());
                    pinSel = -1;
                    RebuildLeft();
                }),
                Z.Text($"{pw.dots.Count} pin(s) — frame {curFrame}", ZuiText.Small, "Pin count and the frame keyframes record at.")));
            parent.Add(box);
        }

        void BuildCurlBody(VisualElement parent, CurlModifier cu, float half)
        {
            var ambient = Z.Box("Curl — ambient swirl", "The whole-frame ambient swirl field.");
            ambient.Add(WrapRow(
                PackedVal("Strength", "Swirl displacement strength.", cu.strength, 0f, 24f, 4f, allowMinMax: false),
                PackedVal("Zoom", "Swirl feature size.", cu.zoom, 1f, Mathf.Max(8f, half), 24f, allowMinMax: false),
                PackedVal("Speed", "Swirl animation speed.", cu.speed, -4f, 4f, 1f, allowMinMax: false),
                PackedSlider("Warp", "Distorts the swirl field into itself.", cu.warp, 0f, 2f, v => cu.warp = v)));
            parent.Add(ambient);
            parent.Add(BuildVortexList(cu, "Curl — vortices (click the preview to add / drag to move)", half, progressNotSpeed: false));
        }

        void BuildCurlProgressBody(VisualElement parent, CurlProgressModifier cp, float half)
        {
            parent.Add(BuildVortexList(cp, "Vortex field (progress) — click the preview to add / drag to move", half, progressNotSpeed: true));
        }

        VisualElement BuildVortexList(IVortexHost host, string title, float half, bool progressNotSpeed)
        {
            var box = Z.Box(title, "Authored vortices — place and drag them directly in the preview.");
            bool editing = ReferenceEquals(editCurl, host);
            box.Add(Z.Button(editing ? "● Editing vortices — click preview to add, drag to move" : "○ Edit vortices (click preview)",
                "Arm vortex editing, then click the preview to add / drag to move.",
                () => { editCurl = editing ? null : host; vortexSel = -1; draggingVortex = false; RebuildLeft(); }));

            for (int i = 0; i < host.Vortices.Count; i++)
            {
                int idx = i;
                var v = host.Vortices[idx];
                if (v == null) continue;
                var vBox = Z.Box(null, null);
                vBox.Add(WrapRow(
                    Z.Button(vortexSel == idx ? "●" : "○", "Select this vortex.", () => { vortexSel = idx; RebuildLeft(); }).W(24f),
                    Z.Text($"#{idx + 1}", ZuiText.Small, "Vortex number."),
                    Z.Toggle(v.clockwise ? "CW" : "CCW", "Spin direction — clockwise or counter-clockwise.", v.clockwise,
                        on => { Dial("Vortex direction", () => v.clockwise = on); RebuildLeft(); }),
                    Z.Button("X", "Delete this vortex.", () =>
                    {
                        Dial("Remove vortex", () => host.Vortices.RemoveAt(idx));
                        if (vortexSel == idx) vortexSel = -1;
                        RebuildLeft();
                    }).W(22f)));
                var valueRow = WrapRow(
                    PackedVal("Radius", "This vortex's influence radius in pixels.", v.radius, 2f, Mathf.Max(8f, half), 24f, allowMinMax: false),
                    PackedVal("Strength°", "Swirl strength in degrees of turn.", v.strength, 0f, 50f, 25f, allowMinMax: false));
                valueRow.Add(progressNotSpeed
                    ? PackedVal("Progress", "How far this vortex's swirl has progressed.", ((VortexPoint)v).progress, 0f, 1f, null, allowMinMax: false)
                    : PackedVal("Speed", "Swirl animation speed.", ((VortexPoint)v).speed, -4f, 4f, 1f, allowMinMax: false));
                vBox.Add(valueRow);
                box.Add(vBox);
            }
            box.Add(WrapRow(
                Z.Button("Clear vortices", "Delete every vortex.", () =>
                {
                    Dial("Clear vortices", () => host.Vortices.Clear());
                    vortexSel = -1;
                    RebuildLeft();
                }),
                Z.Text($"{host.Vortices.Count} vortex(es)", ZuiText.Small, "How many vortices are placed.")));
            return box;
        }

        void ShowAddModifierMenu(List<PyreModifier> list, bool isGlobal)
        {
            var menu = new GenericMenu();
            void Add(string label, System.Func<PyreModifier> make) =>
                menu.AddItem(new GUIContent(label), false, () =>
                {
                    Dial("Add modifier", () => list.Add(make()));
                    RebuildLeft();
                });
            Add("Geometry/Skew", () => new SkewModifier());
            Add("Geometry/Rotate", () => new RotateModifier());
            Add("Geometry/Scale", () => new ScaleModifier());
            Add("Geometry/Wobble", () => new WobbleModifier());
            Add("Geometry/Sunburst wobble", () => new SunburstWobbleModifier());
            Add("Geometry/Profile (mold shape)", () => new ProfileModifier());
            Add("Geometry/Ground (grow from surface)", () => new GroundModifier());
            Add("Geometry/Jagg (star)", () => new JaggModifier());
            Add("Geometry/Edge warp (jagged, wavy silhouette only)", () => new EdgeWarpModifier());
            Add("Geometry/Smudge", () => new SmudgeModifier());
            Add("Geometry/Turbulence (churn)", () => new TurbulenceModifier());
            Add("Geometry/Perlin turbulence (sharper churn)", () => new PerlinTurbulenceModifier());
            Add("Geometry/Curl (swirl)", () => new CurlModifier());
            Add("Geometry/Vortex field (progress)", () => new CurlProgressModifier());
            Add("Geometry/Sphere (fake depth)", () => new SphereModifier());
            Add("Geometry/Ring wave (shockwave ripple)", () => new RingWaveModifier());
            Add("Geometry/Pulse rings (radius-relative shockwave)", () => new PulseRingsModifier());
            Add("Geometry/Blast (disc, arc, or line)", () => new PointBlastModifier());
            Add("Geometry/Sunburst (star silhouette)", () => new SunburstModifier());
            Add("Geometry/Pin warp (hand-animated drag)", () => new PinWarpModifier());
            Add("Colour/Tint", () => new TintModifier());
            Add("Colour/Contrast", () => new ContrastModifier());
            Add("Colour/Brightness", () => new BrightnessModifier());
            Add("Colour/Saturation", () => new SaturationModifier());
            Add("Colour/Posterize", () => new PosterizeModifier());
            Add("Colour/Voronoi crack", () => new VoronoiCrackModifier());
            if (isGlobal) Add("Alpha/Dissolve", () => new DissolveModifier());
            else Add("Alpha/Layer dissolve (follows this layer's own geometry warps)", () => new LayerDissolveModifier());
            Add("Alpha/Ordered dither", () => new OrderedDitherModifier());
            Add("Alpha/Alpha mask", () => new AlphaMaskModifier());
            Add("Post/Bloom (glow)", () => new BloomModifier());
            Add("Post/Outline", () => new OutlineModifier());
            Add("Post/Edge smooth", () => new EdgeSmoothModifier());
            Add("Post/Drop shadow", () => new DropShadowModifier());
            Add("Post/Chromatic aberration", () => new ChromaticAberrationModifier());
            Add("Post/Fuse (blob melt)", () => new FuseModifier());
            Add("Post/Cloud projectile", () => new CloudProjectileModifier());
            Add("Post/Ballistic shockwave (rings + vortex street)", () => new BallisticShockwaveModifier());
            menu.ShowAsContext();
        }
    }
}
