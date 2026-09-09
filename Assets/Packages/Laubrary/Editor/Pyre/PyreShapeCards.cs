// PyreShapeCards — Pyre's own Shape-section cards, extracted so a second host can draw them (T-0183).
//
// WHY THIS FILE EXISTS. Every per-form card below was written for, and only for, PyreWindow's Shape
// section: the Solid / Light / Lines / Glow boxes, the Crescent bite pad, Sparkle, Sprite, Streak, Star,
// Polygon, Fire, Fireball, Text, the flat-2D Border box, the Position box (Turn/Tilt/Roll/Spin + Offset)
// and the layer's own Life-window row. Shaper now hosts a real PyreLayer as one of its shape sources
// (Runtime/PyreShaper/PyreLayerCompositeSource.cs), and the owner's requirement is that such a generator
// "could look quite like it did in Pyre's Shape section". Two hand-kept copies of ~800 lines of dial code
// would drift apart within a release, so there is exactly ONE copy and two hosts.
//
// WHAT WAS ALLOWED TO CHANGE. Nothing about Pyre's behaviour. The bodies are verbatim; only the four
// things that were reaching into PyreWindow became host calls: where a card is added (Body), the two
// numbers a card reads off the spec (CanvasSize / FrameCount), the Undo+dirty wrappers (Dirty / MarkDirty
// / RecordUndo) and the rebuild hooks. PyreWindow implements IPyreShapeCardHost with exactly the methods
// it used to call inline, so its rendered UI and its Undo behaviour are unchanged.
//
// WHAT STAYED IN PyreWindow. BuildPlaybackBox: Playback3D is an editor-preview-only proof of concept
// wired to PyreWindow's own PreviewRenderUtility island, so it is a host hook rather than a shared card
// (and Shaper does not offer that form at all). The plug-in PyreForm branch likewise stays in PyreWindow
// — Shaper reaches hosted forms through PyreFormCompositeSource, which has its own reflection card.
using System.Collections.Generic;
using Laubrary.Zui;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Pyre.Editor
{
    /// <summary>
    /// What a window must supply for <see cref="PyreShapeCards"/> to draw a layer's shape cards into it.
    ///
    /// Deliberately tiny and window-agnostic: it names only what the cards genuinely reached for when they
    /// lived inside PyreWindow. A host that is not a Pyre window (Shaper's Shape section) supplies its own
    /// document's Undo target and frame count and gets byte-identical cards.
    /// </summary>
    public interface IPyreShapeCardHost
    {
        /// Where a card is added. The cards call <c>Body.Add(...)</c> exactly where they used to call
        /// <c>shapeBody.Add(...)</c>.
        VisualElement Body { get; }

        /// The canvas edge length in pixels. Bounds several dial ranges (Size, Streak length, the Fire
        /// emitter width, the Border width), so a big canvas gets big maxima.
        int CanvasSize { get; }

        /// How many frames the animation has. Drives the Life-window row's bounds and the frame markers a
        /// Curve envelope draws, so an authored curve shows where each baked frame lands.
        int FrameCount { get; }

        /// One authored edit: record Undo, apply, mark the asset dirty, invalidate the preview.
        void Dirty(System.Action apply);

        /// The picture changed — invalidate whatever the host caches and repaint.
        void MarkDirty();

        /// Record the host's own asset for Undo before a control mutates a value in place (the ZuiFill and
        /// ZuiValue controls edit their object directly, so they record before rather than around).
        void RecordUndo();

        /// Rebuild the shape body (a card that changes WHICH cards apply calls this).
        void RebuildShape();

        /// Rebuild the swarm body (Fire's "emitters from the swarm" toggle changes what the swarm section
        /// shows). A host with no swarm section does nothing.
        void RebuildSwarm();

        /// An animatable scalar dial over the layer's own life.
        VisualElement Val(string label, string tooltip, ZUIValue v, float lo, float hi, bool cyclic = false);

        /// An animatable XY pair.
        VisualElement Val2D(string label, string tooltip, ZUIValue x, ZUIValue y, ZuiValue2DControl.Options o);

        /// A ZuiFill row (Solid / over-life gradient / spatial fill).
        ZuiFillControl FillRow(string label, string tooltip, ZuiFill fill, ZuiFillControl.Options opt = null);

        /// The compact ZuiFill used for the Solid box's packed slot fills.
        ZuiFillControl SlotFill(string label, string tooltip, ZuiFill fill);

        /// Playback3D's card. Editor-preview-only and wired to the host's own preview island, so it is a
        /// hook rather than a shared card; a host that does not offer Playback3D draws nothing.
        void BuildPlaybackBox(PyreLayer s);

        /// <summary>
        /// Whether the HOST already owns where this layer sits (T-0265). Pyre's own window does not — the
        /// Position box below is the only placement a Pyre layer has, so PyreWindow answers false. A window
        /// that hosts this layer inside a shape tree of its own does: Shaper's node has a Position box with a
        /// Translate that moves this very picture, and drawing Pyre's Offset pad beside it would be a second
        /// control for one move. When true the Position box keeps only what the host CANNOT express — the
        /// solids' Turn and Tilt, which swing the body in depth — and drops the in-plane Spin and the Offset.
        /// </summary>
        bool HostOwnsPlacement { get; }
    }

    /// <summary>
    /// Pyre's Shape-section cards for one <see cref="PyreLayer"/>, drawn into any
    /// <see cref="IPyreShapeCardHost"/>. Pure UI assembly — it reads and writes the layer's fields through
    /// the host's Undo wrappers and owns no state of its own.
    /// </summary>
    public static class PyreShapeCards
    {
        // Fireball's wedge mode — Mirror (alternate wedges reflected, a seam) / Repeat (each wedge the same, rotated).
        // Index 0 = Mirror (fireballMirror true), 1 = Repeat (false).
        public static readonly string[] FireballMirrorChoices = { "Mirror", "Repeat" };
        // Fire's two arm modes — Mirror (symmetric) / Vary (each arm its own seed). Order matches FireArmMode.
        public static readonly string[] FireArmModeChoices = { "Mirror", "Vary" };
        public static readonly List<string> TextFillModeChoices = new List<string> { "Per-char gradient", "Per-char step", "Text gradient" };

        /// The layer's Life-window row. Drawn by both hosts above the form's own cards.
        public static void BuildLifeWindow(IPyreShapeCardHost host, PyreLayer s)
        {
            // Lifetime window (#55) — the frame range this LAYER is alive, ported 1:1 from Pyre1's per-layer
            // "Life (frames)" row. A bounded int min/max pair over [0, frameCount-1] ⇒ ONE Z.MinMax(isInt) range
            // (never two separate fields), per the layout rules. The layer's life sweeps 0→1 across [start, end];
            // outside the window the layer draws nothing. endFrame's -1 sentinel ("the last frame") DISPLAYS as
            // frameCount-1; dragging the high handle back to the far right restores -1 so a full-range window keeps
            // auto-tracking the frame count (and stays byte-identical), while any inset stores a concrete end.
            // fcMax is captured at rebuild time — the render-time clamp keeps it correct if frameCount changes since.
            int fcMax = Mathf.Max(0, host.FrameCount - 1);
            int winLo = Mathf.Clamp(s.startFrame, 0, fcMax);
            int winHi = s.endFrame < 0 ? fcMax : Mathf.Clamp(s.endFrame, 0, fcMax);
            const string framesTip = "The frame window this layer is alive. Its life is lerped 0→1 across [start, end]; before Start / after End the layer contributes nothing (blank). Full range = the whole timeline.";
            // T-0320 — one embedded range control, not the flanked Z.MinMax this used to draw. The layout
            // rules prefer Z.MicroMinMax ("label + low – high both drawn INSIDE the track") and keep
            // Z.MinMax for pairs whose typed precision matters; a whole-frame index over 0..frameCount-1
            // has none to lose, and every other range in this window (Root spread, Tongue climb, Ember
            // rise, Shaper's own Lifetime) is already a MicroMinMax — this row was the only native-looking
            // slider left on the card. `decimals: 0` also gives it the whole-number arrow nudge T-0318 fixed.
            host.Body.Add(Z.MicroMinMax("Life (frames)", winLo, winHi, 0f, fcMax, framesTip,
                (lo, hi) => host.Dirty(() =>
                {
                    int a = Mathf.Clamp(Mathf.RoundToInt(lo), 0, fcMax);
                    int b = Mathf.Clamp(Mathf.RoundToInt(hi), a, fcMax);
                    s.startFrame = a;
                    s.endFrame = b >= fcMax ? -1 : b;   // far-right restores the "last frame" sentinel (auto-tracks frameCount)
                }), 200f, decimals: 0));
        }

        /// Every card a built-in <see cref="ShapeForm"/> layer shows: the shared Fill / Alpha / Size rows,
        /// the per-form box, the flat-2D Border, and the Position box. The plug-in-PyreForm branch is NOT
        /// here — a hosted form draws its own reflection card in whichever window hosts it.
        public static void BuildLegacyForm(IPyreShapeCardHost host, PyreLayer s)
        {
            s.alpha ??= new ZUIValue(1f);
            s.shapeFill ??= new ZuiFill();
            // Shared rows. For the Gem, Colour is its material tint and Size is its girdle radius; for the Sprite,
            // Colour is the optional tint. TEXT takes its colour from its own Fill / Border gradients instead, so
            // the Colour row is hidden for it (the Text box's Fill row tooltip says so). Size is the scale driver
            // for every form — Text reads it as the character HEIGHT.
            // Colour → a ZuiFill: Solid, Over life (a gradient, the default), or a spatial fill (linear/radial/
            // noise); every mode is alpha-capable, switched via the ⋯ menu. Hidden for Text (its own per-char fill
            // rules its colour). Z.Fill draws its own "Fill" label + the ⋯, so it isn't wrapped in a Z.Field; its
            // tooltip is composed for the CURRENT form.
            if (s.shapeForm != ShapeForm.Text)
                host.Body.Add(host.FillRow("Fill", FillTooltip(s.shapeForm), s.shapeFill,
                    new ZuiFillControl.Options().WithWidth(190f).WithGrow(2.2f)));
            host.Body.Add(host.Val("Alpha", "Opacity over the particle's own life (multiplies the final output alpha).", s.alpha, 0f, 1f));
            // Size drives every form's scale — except the Streak, which has its OWN Length/Width envelopes, so the
            // shared Size row is hidden for it (a dead control would be clutter per the layout rules). Its max is
            // the CANVAS size (not a fixed 32) so a big canvas can hold a big particle — the Canvas Size slider
            // rebuilds the pane to refresh this.
            // Streak has its own Length/Width; Fire and Fireball are whole-layer sims bounded by their Reach radius,
            // not a particle radius — so all three hide the shared Size row (a dead control is clutter per the rules).
            if (s.shapeForm != ShapeForm.Streak && s.shapeForm != ShapeForm.Fire && s.shapeForm != ShapeForm.Fireball
                && s.shapeForm != ShapeForm.Playback3D)
                host.Body.Add(host.Val("Size (px)", SizeTooltip(s.shapeForm), s.size, 0f, host.CanvasSize));

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
                    BuildSolidBox(host, s);
                    break;
                case ShapeForm.Disc:
                    host.Body.Add(EdgeRow(host, s, "Soft rim (1) vs a hard pixel edge (0)."));
                    break;
                case ShapeForm.Crescent:
                    BuildCrescentRows(host, s);
                    break;
                case ShapeForm.Sparkle:
                    BuildSparkleRows(host, s);
                    break;
                case ShapeForm.Sprite:
                    BuildSpriteRows(host, s);
                    break;
                case ShapeForm.Text:
                    BuildTextBox(host, s);
                    break;
                case ShapeForm.Streak:
                    BuildStreakRows(host, s);
                    break;
                case ShapeForm.Star:
                    BuildStarRows(host, s);
                    break;
                case ShapeForm.Polygon:
                    BuildPolygonRows(host, s);
                    break;
                case ShapeForm.Fire:
                    BuildFireBox(host, s);
                    break;
                case ShapeForm.Fireball:
                    BuildFireballBox(host, s);
                    break;
                case ShapeForm.Playback3D:
                    host.BuildPlaybackBox(s);
                    break;
            }

            // First-class Border (task #60/#65) — the six flat 2D forms get an optional coloured rim. Shown ONLY for
            // them (the 3D solids have their own edge lines, Text its own border, and Sprite/Fire/Fireball/Sparkle get
            // none). Added AFTER the form-specific rows so it reads as part of the shape's look. Off = a single compact
            // toggle; on = a Border box (Width / Fill / Draw-over-matte).
            if (IsFlat2DBorderForm(s.shapeForm)) BuildBorderBox(host, s);

            // Fire and Fireball are whole-layer forms with no particles at all, so a Position section there would be
            // dead — skip it. Their Fill (the ramp) and Alpha (overall opacity) rows still apply.
            if (s.shapeForm == ShapeForm.Fire || s.shapeForm == ShapeForm.Fireball || s.shapeForm == ShapeForm.Playback3D) return;

            // Position (task #11) — one collapsible box grouping everything positional: the particle's rotation and
            // its Offset from spawn. Present for every particle form. A collapsible box already provides show/hide,
            // so there is no longer an "Advanced" toggle gating it (the old toggle only gated the UI — the renderer
            // always keyed travel off particlePathX/Y being non-static-zero, never off the shapeAdvanced flag).
            BuildPositionBox(host, s);
        }

        public static VisualElement WrapRow(params VisualElement[] kids)
        {
            var r = Z.Row(kids); r.style.flexWrap = Wrap.Wrap; return r;
        }

        // The Position box (task #11) — the particle's orientation AND its Offset from the spawn position, grouped
        // into ONE collapsible box shown for every particle form (only the whole-layer Fire/Fireball sims are
        // excluded, above). Orientation is the 3D solids' Turn / Tilt / Roll trio (moved here out of the old Solid
        // box), or a single in-plane Spin for the flat forms and Text — all the SAME particleSpin field, just
        // labelled per form. Offset (formerly "Travel") is the per-particle path added to the spawn position; a
        // static value is a constant offset that causes no motion, which is why "Travel" was the wrong name. There
        // is deliberately no enable-toggle: a collapsible box already gives show/hide (ui-layout-rules — no extra
        // toggle when a section can collapse), and the renderer applies the offset whenever particlePathX/Y are not
        // static-zero, so always showing this is byte-identical to the old "Advanced" gate.
        public static void BuildPositionBox(IPyreShapeCardHost host, PyreLayer s)
        {
            s.particleSpin ??= new ZUIValue(0f);
            s.particlePathX ??= new ZUIValue(0f);
            s.particlePathY ??= new ZUIValue(0f);

            // T-0265 — hosted inside another window's shape tree, this box says only what that window cannot:
            // the depth rotations of a 3D solid. The in-plane Spin and the Offset pad are the host's own
            // Rotation and Translate said twice, and a flat form has nothing left once they are gone.
            bool hosted = host.HostOwnsPlacement;
            if (hosted && !IsSolidForm(s.shapeForm)) return;

            var box = Z.BoxKeyed(hosted ? "Orientation" : "Position",
                hosted
                ? "How the solid is turned in DEPTH over its own life. Where it sits and how it turns on the "
                  + "canvas are the node's own Position box, above."
                : "Where the particle sits and how it is turned, over its own life: its rotation (Turn / Tilt / Roll "
                + "for a 3D solid, Spin for a flat form) and its Offset from the position the swarm spawned it at. "
                + "Every field defaults to no rotation and no offset, so a fresh shape sits exactly where it was "
                + "placed.",
                "pyreplus.position");

            if (hosted)
            {
                s.gemTilt ??= new ZUIValue(18f);
                box.Add(host.Val("Turn °", TurnTooltip(s.shapeForm), s.particleSpin, -1440f, 1440f, cyclic: true));
                box.Add(host.Val("Tilt °", TiltTooltip(s.shapeForm), s.gemTilt, -1440f, 1440f, cyclic: true));
                host.Body.Add(box);
                return;
            }

            if (IsSolidForm(s.shapeForm))
            {
                s.gemTilt ??= new ZUIValue(18f);
                s.gemRoll ??= new ZUIValue(0f);
                // Turn (yaw = the shared particleSpin), Tilt (gemTilt) and Roll (gemRoll) — the three rotation axes
                // in plain words, each animatable over the particle's own life. Roll is geometrically inert for the
                // symmetric Ring (Turn + Tilt already shape its ellipse), so it's hidden there. Tooltips per form.
                box.Add(host.Val("Turn °", TurnTooltip(s.shapeForm), s.particleSpin, -1440f, 1440f, cyclic: true));
                box.Add(host.Val("Tilt °", TiltTooltip(s.shapeForm), s.gemTilt, -1440f, 1440f, cyclic: true));
                if (s.shapeForm != ShapeForm.Ring)
                    box.Add(host.Val("Roll °", RollTooltip(s.shapeForm), s.gemRoll, -1440f, 1440f, cyclic: true));
            }
            else
            {
                // The flat 2D forms + Text edit particleSpin as a single in-plane Spin. (For a 3D solid the same
                // field is shown once above as "Turn °", so it isn't repeated here.) Tooltip composed per form.
                box.Add(host.Val("Spin °", SpinTooltip(s.shapeForm), s.particleSpin, -720f, 720f, cyclic: true));
            }

            float half = Mathf.Max(1f, host.CanvasSize * 0.5f);
            box.Add(host.Val2D("Offset",
                "A per-particle positional OFFSET from the spawn position, in canvas pixels, sampled on the "
                + "particle's OWN life (0 = birth, 1 = death). Static = a constant offset (Static 0 = it stays where "
                + "it spawned); author a Curve to make the particle drift or arc as it lives.",
                s.particlePathX, s.particlePathY,
                new ZuiValue2DControl.Options().WithRange(-half, half, -half, half).WithDefault(Vector2.zero)));

            host.Body.Add(box);
        }

        // The 3D-solid controls (task #13 restructure). What used to be ONE "Solid" box holding geometry + the
        // rotation trio + Light/Lines/Glow sub-sections is now split: the "Solid" box keeps only the per-form
        // GEOMETRY, while Light / Lines / Glow are their OWN sibling collapsible boxes (added straight into the Shape
        // body beside Solid, not nested under it). The rotation trio (Turn/Tilt/Roll) moved out to the Position box
        // (task #11). Shown for Gem / Box / Pyramid / Can / Orb / Ring. Colour, Alpha and Size stay above (shared).
        // All plain sliders are label-inside MicroSliders; the two glows are animatable ZUIValues (Val), each packed
        // with its own colour; Specular packs with its colour.
        public static void BuildSolidBox(IPyreShapeCardHost host, PyreLayer s)
        {
            s.gemEdgeGlow ??= new ZUIValue(0.5f);     // defensive; the real steady defaults come from the spec factories
            s.gemInnerGlow ??= new ZUIValue(0.35f);

            // ── Solid box: the per-form GEOMETRY only. BoxKeyed: view presets persist under this stable key —
            // retitling the box or rewording its tooltip must never orphan saved views. The Orb (a bare sphere) has
            // no geometry, so its Solid box would be empty — skip it there rather than show an empty box.
            var solid = Z.BoxKeyed("Solid", SolidBoxTooltip(s.shapeForm), "pyreplus.solid");

            // ── per-form geometry (Orb has none, so its Solid box is never added) ──
            if (s.shapeForm == ShapeForm.Gem)
            {
                solid.Add(Z.HGroup(
                    Z.MicroSlider("Sides", s.gemSides, 3f, 8f,
                        "Girdle vertex count — 4 is the classic octahedral gem; more sides make a rounder crystal.",
                        v => host.Dirty(() => s.gemSides = Mathf.Clamp(Mathf.RoundToInt(v), 3, 8)), 150f, showValue: true, decimals: 0),
                    Z.MicroSlider("Crown", s.gemCrown, 0.2f, 2.5f,
                        "Crown height (the top point) as a fraction of the gem's radius.",
                        v => host.Dirty(() => s.gemCrown = v), 150f, showValue: true),
                    Z.MicroSlider("Pavilion", s.gemPavilion, 0.2f, 2.5f,
                        "Pavilion depth (the bottom point) as a fraction of the gem's radius.",
                        v => host.Dirty(() => s.gemPavilion = v), 150f, showValue: true)));
                host.Body.Add(solid);
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
                        v => host.Dirty(() => s.solidAspect = v), 150f, showValue: true),
                };
                if (s.shapeForm != ShapeForm.Can)
                    geo.Add(Z.MicroSlider("Depth", s.solidDepth, 0.2f, 2f,
                        "Depth (front-to-back) as a fraction of width. Box: its third dimension; Pyramid: its base "
                        + "front-to-back (1 = the square base).",
                        v => host.Dirty(() => s.solidDepth = v), 150f, showValue: true));
                solid.Add(WrapRow(geo.ToArray()));
                host.Body.Add(solid);
            }
            else if (s.shapeForm == ShapeForm.Ring)
            {
                // Ring — one geometry row: the hole radius. (Orb has NO geometry rows — a sphere needs none — so its
                // Solid box is skipped; it falls straight through to the Light / Lines / Glow boxes below.)
                solid.Add(Z.MicroSlider("Inner", s.ringInner, 0.1f, 0.92f,
                    "Inner radius as a fraction of the outer radius — the size of the ring's hole (0.1 = a nearly "
                    + "solid disc, 0.92 = a thin hoop).",
                    v => host.Dirty(() => s.ringInner = v), 150f, showValue: true));
                host.Body.Add(solid);
            }

            // ── Light — its own sibling collapsible box (task #13), no longer a divider-subsection of Solid. The
            // rotation trio (Turn/Tilt/Roll) that used to sit here has moved to the Position box (task #11), and the
            // old ⚙ gear + per-control ToggleGroups are gone: Light / Lines / Glow are now three separate collapsible
            // boxes, so each box's own fold is its show/hide (a collapsible section already does that job).
            var light = Z.BoxKeyed("Light",
                "The single KEY light and how the surfaces respond to it. The key light is DIRECTIONAL — aim it on "
                + "the sphere; Ambient is a separate non-directional base light. The edge Lines and the Glows have "
                + "their OWN strengths and do NOT obey this light.",
                "pyreplus.solid.light");
            // Light DIRECTION + distance as ONE reusable Z.Direction3D control (ZUI #67): a draggable LIT SPHERE
            // gizmo (yaw × pitch) whose lit hotspot IS the readout, with numeric fallback fields, the distance as
            // its 3rd axis, and a larger 3D preview on hover / pin. It edits the SAME gemLightYaw/Pitch/Distance
            // fields (yaw −180..180, pitch −85..85, distance 1.5..8 — the control's option defaults), so every spec
            // renders byte-identical. Wrapped in a Z.Frame so it reads as one titled unit.
            const string lightDirTip = "The key light is DIRECTIONAL — aim it on the sphere. Yaw = which side it "
                + "comes FROM (left/right, −180..180°); Pitch = its height (−85..85°, negative brings it from "
                + "below/behind); Distance = how far off, in radii (closer = a tighter, brighter hotspot). Only the "
                + "lit faces and the specular hotspot follow it — the Lines and Glows have their own strengths and "
                + "do NOT obey the light. Hover (or pin) for a larger 3D preview.";
            light.Add(Z.Frame("Direction", lightDirTip,
                Z.Direction3D(s.gemLightYaw, s.gemLightPitch, s.gemLightDistance, lightDirTip,
                    (yaw, pitch, dist) => host.Dirty(() =>
                    {
                        s.gemLightYaw = yaw;
                        s.gemLightPitch = pitch;
                        s.gemLightDistance = Mathf.Clamp(dist, 1.5f, 8f);
                    }),
                    new ZuiDirection3D.Options { showDistance = true })));
            light.Add(WrapRow(
                Z.MicroSlider("Ambient", s.gemAmbient, 0f, 1f,
                    "Non-directional BASE light on every face (it doesn't come from a direction). Near zero keeps the "
                    + "solid contrasty; raise it to flatten the shading.",
                    v => host.Dirty(() => s.gemAmbient = v), 150f, showValue: true),
                Z.MicroSlider("Diffuse", s.gemDiffuse, 0f, 3f,
                    "The key light's DIFFUSE strength on the faces it hits (the Lambert term). 0 = only Ambient + "
                    + "Specular light the faces; 2.1 is the default look. This is the dial that was missing — with it "
                    + "at 0 and Ambient/Specular at 0 the faces finally go dark instead of staying diffuse-lit.",
                    v => host.Dirty(() => s.gemDiffuse = v), 150f, showValue: true),
                Z.MicroSlider("Specular", s.gemSpecular, 0f, 2f,
                    "Strength of the tight highlight (the bright hot spot) where the key light reflects — pair it with "
                    + "Spec power for the hotspot's tightness.",
                    v => host.Dirty(() => s.gemSpecular = v), 150f, showValue: true),
                Z.MicroSlider("Spec power", s.gemSpecPower, 2f, 128f,
                    "TIGHTNESS of the specular hotspot — higher = a smaller, sharper glint; lower spreads it into a "
                    + "broad sheen. (The old fixed 48 was so tight the highlight rarely showed — lower it to see it.)",
                    v => host.Dirty(() => s.gemSpecPower = v), 150f, showValue: true),
                host.SlotFill("Spec fill", "Fill for the specular highlight — Solid, or a gradient/spatial fill (alpha-capable).",
                    s.gemSpecularFill)));
            host.Body.Add(light);

            // ── Lines — its own sibling collapsible box (task #13). The hard facet edge lines.
            var lines = Z.BoxKeyed("Lines", "The hard facet edge lines that catch the light.", "pyreplus.solid.lines");
            lines.Add(WrapRow(
                Z.MicroSlider("Line width", s.gemLineWidth, 0f, 3f,
                    "Width of the hard facet edge lines in pixels (0 = no lines). The lines catch the key light.",
                    v => host.Dirty(() => s.gemLineWidth = v), 150f, showValue: true),
                host.SlotFill("Line fill", "Fill for the facet edge lines — Solid, or a gradient/spatial fill (alpha-capable).",
                    s.gemLineFill)));
            host.Body.Add(lines);

            // ── Glow — its own sibling collapsible box (task #13). A rim halo + an interior glow.
            var glow = Z.BoxKeyed("Glow",
                "A rim halo and an interior glow — steady by default (author a Curve to pulse), each with its own fill.",
                "pyreplus.solid.glow");
            glow.Add(WrapRow(
                host.Val("Edge glow",
                    "Strength (0-1) of the halo around the edge lines, over the particle's OWN life; it spills "
                    + "OUTSIDE the solid's silhouette. Static = a steady glow (the default); author a Curve to make "
                    + "it pulse over the particle's life.",
                    s.gemEdgeGlow, 0f, 1f),
                host.SlotFill("Edge fill", "Fill for the edge-line halo glow — Solid, or a gradient/spatial fill (alpha-capable).",
                    s.gemEdgeGlowFill)));
            glow.Add(WrapRow(
                host.Val("Inner glow",
                    "Strength (0-1) of the emissive glow rising from the facet interiors, over the particle's OWN "
                    + "life; interior only. Static = a steady glow (the default); author a Curve to make it pulse "
                    + "over the particle's life.",
                    s.gemInnerGlow, 0f, 1f),
                host.SlotFill("Inner fill", "Fill for the facet inner glow — Solid, or a gradient/spatial fill (alpha-capable).",
                    s.gemInnerGlowFill)));
            host.Body.Add(glow);
        }

        // The Edge-softness control — now a MultiCont (Static / Min-Max / Curve over the particle's own life, #12),
        // seeded Static(edgeSoftness) the first time it's built so an un-migrated asset stays byte-identical. Shared
        // by Disc (its single rim), Crescent (both rims) and the other flat 2D forms, each passing its own tooltip.
        public static VisualElement EdgeRow(IPyreShapeCardHost host, PyreLayer s, string tooltip)
        {
            s.edgeSoftnessAnim ??= new ZUIValue(s.edgeSoftness);
            return host.Val("Edge", tooltip, s.edgeSoftnessAnim, 0f, 1f);
        }

        // Crescent form rows — the shared Edge row (drives BOTH rims), the bite size, then the mask-disc CENTRE as a
        // 2D pad (#12 part 2, replacing the old polar Offset + Angle).
        public static void BuildCrescentRows(IPyreShapeCardHost host, PyreLayer s)
        {
            s.crescentBite ??= new ZUIValue(0.55f);
            s.crescentAngle ??= new ZUIValue(0f);

            host.Body.Add(EdgeRow(host, s,
                "Soft rim (1) vs a hard pixel edge (0). For the Crescent it feathers BOTH rims — the outer disc "
                + "edge and the bite edge."));
            host.Body.Add(host.Val("Bite",
                "Size of the disc bitten out of the main disc, as a fraction of its radius (0 = no bite, a "
                + "full disc; 1 = a bite as wide as the disc), over the particle's own life.",
                s.crescentBite, 0f, 1f));
            host.Body.Add(CrescentCenterPad(host, s));
        }

        // The mask-disc CENTRE as a 2D pad (#12 part 2), replacing the polar Offset + Angle. The pad is SEEDED for
        // display from the legacy polar values, but only CONVERTS — writing crescentCenterX/YAnim — on an explicit
        // drag, so an untouched crescent keeps rendering through the byte-identical polar path (a silent
        // polar→cartesian seed is not byte-identical: (off·radius)·cos vs (off·cos)·radius differ by multiply order).
        public static VisualElement CrescentCenterPad(IPyreShapeCardHost host, PyreLayer s)
        {
            bool live = s.crescentCenterXAnim != null && s.crescentCenterYAnim != null;
            float angRad = (s.crescentAngle != null ? s.crescentAngle.staticValue : 0f) * Mathf.Deg2Rad;
            float off = Mathf.Clamp01(s.crescentOffset);
            var cx = live ? s.crescentCenterXAnim : new ZUIValue(off * Mathf.Cos(angRad));
            var cy = live ? s.crescentCenterYAnim : new ZUIValue(off * Mathf.Sin(angRad));
            var o = new ZuiValue2DControl.Options().WithRange(-1f, 1f, -1f, 1f).WithDefault(Vector2.zero);
            return Z.Value2D("Mask", cx, cy, o,
                "Where the bitten-out mask disc sits, as (x,y) in radius units from the drawn disc's centre (-1..1), "
                + "over the particle's own life. (0,0) = the bite dead-centre (a hole/ring); push it out for a "
                + "thinner sliver of a crescent. Replaces the old Offset + Angle.",
                () => { if (s.crescentCenterXAnim == null) { s.crescentCenterXAnim = cx; s.crescentCenterYAnim = cy; } host.MarkDirty(); },
                () => host.RecordUndo());
        }

        // Sparkle form rows — no Edge row (sparkles are hard pixels). Density (animatable) + the pixel block size.
        public static void BuildSparkleRows(IPyreShapeCardHost host, PyreLayer s)
        {
            s.sparkleDensity ??= new ZUIValue(0.35f);
            host.Body.Add(WrapRow(
                host.Val("Density",
                    "Fraction of the disc's cells that sparkle, 0..1, over the particle's own life — a rising "
                    + "curve makes the sparkles ignite as it lives. Each lit cell also twinkles on/off per frame.",
                    s.sparkleDensity, 0f, 1f),
                Z.MicroSlider("Size px", s.sparkleSize, 1f, 4f,
                    "Size of each lit sparkle block in pixels (1 = single pixels, up to 4).",
                    v => host.Dirty(() => s.sparkleSize = Mathf.Clamp(Mathf.RoundToInt(v), 1, 4)), 150f,
                    showValue: true, decimals: 0)));
        }

        // Sprite form rows — no Edge row. The stamped image picker + the tint toggle, packed.
        public static void BuildSpriteRows(IPyreShapeCardHost host, PyreLayer s)
        {
            host.Body.Add(WrapRow(
                Z.Field("Sprite",
                    "The image stamped at each particle. Its texture MUST have Read/Write enabled in its import "
                    + "settings, or it can't be sampled and the particle falls back to a plain disc.",
                    Z.Object<Sprite>(s.spriteImage,
                        "The stamped image — its texture needs Read/Write enabled (import settings), else the "
                        + "particle renders a disc fallback.",
                        v => host.Dirty(() => s.spriteImage = v), 160f)),
                Z.Toggle("Tint",
                    "Multiply the sprite by the Colour gradient at the particle's own life. Off = the sprite's own "
                    + "raw colours.",
                    s.spriteTint, v => host.Dirty(() => s.spriteTint = v))));
        }

        // Streak form rows — the streak's own Length (its scale driver, replacing the hidden shared Size), then
        // Width + Anchor + Tip packed, then the shared Edge row (which feathers the streak's two long SIDES). The
        // particle is an anchor point ON the streak (Anchor 0 = tail, 0.5 = centred, 1 = tip); default orientation
        // up, steered by the Swarm's Orient (and its own Advanced Spin).
        public static void BuildStreakRows(IPyreShapeCardHost host, PyreLayer s)
        {
            s.streakLength ??= new ZUIValue(20f);   // defensive; the real shoot-out arc comes from the spec factory
            s.streakWidth ??= new ZUIValue(3f);

            host.Body.Add(host.Val("Length (px)",
                "The streak's length forward (from its root) in pixels, over the particle's own life. Author a "
                + "Curve to make it shoot out then ease shorter — the default comet-tail arc.",
                s.streakLength, 0f, host.CanvasSize));   // max = the canvas, so a big canvas gets a long streak

            host.Body.Add(WrapRow(
                host.Val("Width (px)", "The streak's thickness across, in pixels, over the particle's own life.",
                    s.streakWidth, 0f, host.CanvasSize / 4f),   // width max = a quarter-canvas (keeps the old 64→16 feel)
                Z.MicroSlider("Anchor", s.streakAnchor, 0f, 1f,
                    "Where the particle sits ALONG the streak, from tail to tip: 0 = at the TAIL (the streak grows "
                    + "forward), 0.5 = CENTRED (Length grows both ways, so it never slides off the particle), 1 = at "
                    + "the TIP (grows backward).",
                    v => host.Dirty(() => s.streakAnchor = v), 150f, showValue: true),
                Z.MicroSlider("Tip", s.streakSoftTip, 0f, 1f,
                    "End softness — how much of EACH end (forward and back) feathers out to transparent (0 = hard "
                    + "flat ends; 1 = the streak fades from its centre to both tips).",
                    v => host.Dirty(() => s.streakSoftTip = v), 150f, showValue: true)));

            host.Body.Add(EdgeRow(host, s,
                "Soft sides (1) vs hard pixel edges (0) — feathers the streak's two long SIDES (both ends are "
                + "feathered by the Tip control above)."));

            // Bars taper (slice 3): make the Swarm's per-index Scale drive LENGTH only, leaving width uniform — a row
            // of equal-width bars of graduated length, Pyre's barTaper flame silhouette. Only meaningful with a Swarm
            // whose Scale-by-index is a Curve/MinMax (Advanced Swarm); a no-op at the default (Scale-by-index = 1).
            host.Body.Add(Z.Toggle("Taper length only",
                "With a Swarm, make the per-index Scale change the streak's LENGTH only, not its width — equal-width "
                + "bars of graduated length (a flame/asterisk silhouette). Off = the index Scale changes both length "
                + "and width together. Set the Swarm's Scale-by-index (Advanced) to a Curve or Min/Max to see it.",
                s.streakScaleLengthOnly, v => host.Dirty(() => s.streakScaleLengthOnly = v)));
        }

        // Star form rows — a filled star polygon. Arms (point count) packed with Skew (the arm swirl); then Length
        // (arm reach) packed with Base width (valley position); then the shared Edge row (the star's rim softness).
        // The shared Size row above stays visible — it's the tip radius the arms reach to.
        public static void BuildStarRows(IPyreShapeCardHost host, PyreLayer s)
        {
            s.starLength ??= new ZUIValue(0.62f);       // defensive; the real defaults come from the spec factories
            s.starBaseWidth ??= new ZUIValue(1f);
            s.starSkew ??= new ZUIValue(0f);

            host.Body.Add(Z.HGroup(
                Z.MicroSlider("Arms", s.starArms, 2f, 20f,
                    "How many points the star has (2–20). 5 = the classic five-pointed star; 6 = a Star of David.",
                    v => host.Dirty(() => s.starArms = Mathf.Clamp(Mathf.RoundToInt(v), 2, 20)), 150f,
                    showValue: true, decimals: 0),
                host.Val("Skew °",
                    "Swirls the arms by rotating the inner (valley) vertices, in degrees, over the particle's own "
                    + "life — 0 = straight symmetric arms, ± twists them into a pinwheel. (Clamped so a valley "
                    + "never crosses a tip.)",
                    s.starSkew, -60f, 60f)));

            host.Body.Add(Z.HGroup(
                host.Val("Length",
                    "How far the arm tips reach out, 0..1, over the particle's own life — the inner (valley) radius "
                    + "is R·(1−length), so higher = longer, sharper arms (0.62 ≈ the classical pentagram).",
                    s.starLength, 0f, 1f),
                host.Val("Base width",
                    "Angular width of each arm's base, 0.1..1, over the particle's own life — 1 = the classical "
                    + "midpoint valleys; smaller pulls the valleys toward the tips for thinner arm bases and wider "
                    + "notches between them.",
                    s.starBaseWidth, 0.1f, 1f)));

            host.Body.Add(EdgeRow(host, s,
                "Soft rim (1) vs a hard pixel edge (0) — feathers the star's whole outline inward along each ray."));
        }

        // Polygon form rows — a flat filled regular convex N-gon (#59 Part A). Just the Sides count (3..12) + the
        // shared Edge (rim softness) row; the shared Size row above stays visible (it's the circumradius the vertices
        // reach to). Sides is a bounded int scalar ⇒ a MicroSlider (matching the Star's Arms row), never a bare field.
        public static void BuildPolygonRows(IPyreShapeCardHost host, PyreLayer s)
        {
            host.Body.Add(Z.MicroSlider("Sides", s.polygonSides, 3f, 12f,
                "How many sides the polygon has (3–12): 3 = a triangle, 4 = a square, 5 = a pentagon, 6 = a hexagon,… "
                + "An even count rests on a flat edge (a square sits flat, not a diamond); an odd count points a vertex "
                + "up (an upright triangle/pentagon). Radius is set by Size (px), above.",
                v => host.Dirty(() => s.polygonSides = Mathf.Clamp(Mathf.RoundToInt(v), 3, 12)), 150f,
                showValue: true, decimals: 0));

            host.Body.Add(EdgeRow(host, s,
                "Soft rim (1) vs a hard pixel edge (0) — feathers the polygon's whole outline inward along each ray."));
        }

        // Fire form box — the stateful flame SIMULATION (slice 6a). Every rate is an envelope over the LAYER's life
        // (Fire has no particles — the whole layer IS the sim), grouped Emitter → Heat & fuel → Motion → Flame shape →
        // Confinement → Output. Its colour ramp is the shared Shape Fill above (smoke→fire) and its overall opacity is
        // the shared Shape Alpha; `size` and the Swarm don't apply (both hidden/noted). Reuses Pyre's own FireSim via
        // the renderer's replay harness — the dials here map 1:1 onto Pyre's Fire fields.

        public static void BuildFireBox(IPyreShapeCardHost host, PyreLayer s)
        {
            // Defensive nulls (the real defaults come from the spec factories).
            s.fireIntensity ??= new ZUIValue(1f);
            s.fireDirection ??= new ZUIValue(90f);
            s.fireEmitterWidth ??= new ZUIValue(9f);
            s.fireEmitterInset ??= new ZUIValue(0f);
            s.fireHeat ??= new ZUIValue(0.95f);
            s.fireFuel ??= new ZUIValue(0.75f);
            s.firePulse ??= new ZUIValue(0.18f);
            s.fireFlow ??= new ZUIValue(1f);
            s.fireBuoyancy ??= new ZUIValue(4f);
            s.fireCurl ??= new ZUIValue(1.5f);
            s.fireCurlScale ??= new ZUIValue(7f);
            s.fireFlicker ??= new ZUIValue(0.6f);
            s.fireStretch ??= new ZUIValue(3f);
            s.firePinch ??= new ZUIValue(0.6f);
            s.fireBreakup ??= new ZUIValue(0.4f);
            s.fireDissipation ??= new ZUIValue(0.35f);
            s.fireBurn ??= new ZUIValue(1.5f);
            s.fireReach ??= new ZUIValue(0.8f);
            s.fireEdgeCooling ??= new ZUIValue(0.9f);

            var box = Z.BoxKeyed("Fire",
                "A stateful flame SIMULATION: heat is carried by a velocity field, so it's reached by REPLAYING the sim "
                + "from frame 0 (scrubbing and baking stay exact). Every rate is an envelope over the layer's life, so "
                + "you author the SHAPE of the burn — ignite, roar, die back — not a speed. Built-in emitters (arms "
                + "around the centre); the Swarm doesn't apply unless 'Swarm emitters' is on. Colour is the Shape "
                + "Fill above (smoke→fire ramp); overall opacity is the Shape Alpha.", "pyreplus.fire");

            // Swarm emitters (slice 8) — source the emitters from the Swarm instead of the built-in arms. Only acts
            // when the Swarm is enabled; rebuild the Swarm section on change so it un-gates (or re-gates) accordingly.
            box.Add(Z.Toggle("Swarm emitters",
                "Source the flame's emitters from the SWARM instead of the built-in arms: each alive swarm particle "
                + "becomes ONE heat/fuel injection at its own position (radius/heat/fuel = this box's Emitter width / "
                + "Heat / Fuel envelopes at that particle's OWN life), all advecting and merging into ONE shared fire "
                + "field. Only takes effect with the Swarm ENABLED (turn it on in the Swarm section below) — with the "
                + "Swarm off, the built-in Arms emitters are used. Arms / Direction still shape the fluid field's "
                + "buoyancy and confinement.",
                s.fireSwarmEmitters,
                v => { host.Dirty(() => s.fireSwarmEmitters = v); host.RebuildSwarm(); }));

            // Progress — the master burn envelope (scales the injected heat + fuel).
            box.Add(host.Val("Progress",
                "The burn's PROGRESS over the layer's life as ONE envelope: 0 = the emitter is off, 1 = full. This is "
                + "how the fire ignites, holds and dies — shape this curve instead of setting a speed. It scales the "
                + "injected heat and fuel, so the flame physically grows and shrinks with it.",
                s.fireIntensity, 0f, 1f));

            // ── Emitter ──
            box.Add(Z.HGroup(
                Z.MicroSlider("Arms", s.fireArms, 1f, 8f,
                    "How many flame arms radiate from the centre. 1 = a single directional flame; Pinch opens the "
                    + "cold gaps between arms, so a 3-arm fire is a 3-point star, not a filled triangle.",
                    v => host.Dirty(() => s.fireArms = Mathf.Clamp(Mathf.RoundToInt(v), 1, 8)), 150f, showValue: true, decimals: 0),
                Z.Field("Mode",
                    "Mirror = every arm emits identically (kaleidoscope symmetry). Vary = each arm gets its own seed, "
                    + "so the flames genuinely differ while sharing these dials.",
                    Z.Segmented((int)s.fireArmMode, FireArmModeChoices,
                        "Mirror = arms identical; Vary = each arm its own seed.",
                        v => host.Dirty(() => s.fireArmMode = (Laubrary.SpriteFx.FireArmMode)v)))));
            box.Add(host.Val("Direction °",
                "Which way arm 0 points, in degrees — 90 = up. (Arms are spaced evenly around the circle from here.)",
                s.fireDirection, 0f, 360f));
            box.Add(Z.HGroup(
                host.Val("Emitter width (px)", "Width of each arm's emitter, in pixels — the base of the flame.",
                    s.fireEmitterWidth, 1f, host.CanvasSize),
                host.Val("Inset (px)", "How far each emitter sits out from the centre, in pixels.",
                    s.fireEmitterInset, 0f, Mathf.Max(1f, host.CanvasSize * 0.5f))));
            // Off-centre emitter (task #61): shift the whole built-in flame off the canvas centre. A plain Vector2 in px
            // (not animatable) → a Z.Pad, mirroring the Gem light-dir pad; the renderer translates the FINISHED flame,
            // so the sim stays centred and byte-faithful (0,0 = the pre-change centred behaviour). Undo-safe via Dirty.
            {
                float offHalf = Mathf.Max(1f, host.CanvasSize * 0.5f);
                const string offTip = "Shift the whole flame off the canvas centre, in pixels — X right, Y up. The flame "
                    + "is simulated exactly as if centred (buoyancy, arms and confinement all move with it), then "
                    + "translated to here. 0,0 = centred (the built-in behaviour). Places a flame that doesn't sit in the "
                    + "middle. (Built-in arms path only — with 'Swarm emitters' on, place the sources with the Swarm.)";
                box.Add(Z.Field("Emitter offset (px)", offTip,
                    Z.Pad(s.fireEmitterOffset, new Rect(-offHalf, -offHalf, offHalf * 2f, offHalf * 2f), offTip,
                        v => host.Dirty(() => s.fireEmitterOffset = v), 56f)));
            }

            // ── Heat & fuel ──
            box.Add(Z.HGroup(
                host.Val("Heat", "How hot the emitter injects. Animate it to ignite, roar and die back (scaled by Progress).",
                    s.fireHeat, 0f, 1f),
                host.Val("Fuel", "How much unburnt fuel the emitter injects — fuel turns into heat as it burns, which is "
                    + "what gives a flame a body rather than a glow (scaled by Progress).",
                    s.fireFuel, 0f, 1f)));
            box.Add(Z.HGroup(
                host.Val("Burn", "How fast fuel converts into heat.", s.fireBurn, 0f, 4f),
                host.Val("Dissipation", "How fast heat fades. High = a short sharp flame; low = long lingering tongues.",
                    s.fireDissipation, 0f, 2f)));
            box.Add(host.Val("Pulse", "How much the emitter's output breathes in and out — a seeded wobble on the base.",
                s.firePulse, 0f, 2f));

            // ── Motion ──
            box.Add(Z.HGroup(
                host.Val("Flow", "Steady outward push away from the centre — a jet.", s.fireFlow, 0f, 6f),
                host.Val("Buoyancy", "How strongly HEAT carries itself outward — what makes a flame CLIMB rather than just "
                    + "spread.", s.fireBuoyancy, 0f, 10f)));
            box.Add(Z.HGroup(
                host.Val("Curl", "Swirl strength — curls the tongues instead of merely stretching them.", s.fireCurl, 0f, 6f),
                host.Val("Curl scale", "Size of the swirls — small = fine turbulence, large = slow broad rolls.",
                    s.fireCurlScale, 2f, 24f)));
            box.Add(host.Val("Flicker", "Sideways wobble of the tongues — how much they lick and wave.", s.fireFlicker, 0f, 4f));

            // ── Flame shape ── (what makes it read as a FLAME, not an expanding blob)
            box.Add(Z.HGroup(
                host.Val("Stretch", "Elongate the flame along its direction — high = long licking tongues, 0 = squat.",
                    s.fireStretch, 0f, 8f),
                host.Val("Pinch", "Taper the sides into a pointed tongue — most of what makes it read as a flame, and (with "
                    + "several arms) what opens the cold gaps between them.", s.firePinch, 0f, 3f)));
            box.Add(host.Val("Breakup", "Eat the edges into wisps instead of a smooth silhouette.", s.fireBreakup, 0f, 3f));

            // ── Confinement ── (the reason a hot setting stays usable — never touches the frame edge)
            box.Add(Z.HGroup(
                host.Val("Reach", "How far the flame may reach, as a fraction of the canvas half-size. Past this it's cooled "
                    + "to nothing, so it can NEVER touch the frame edge however hard it's driven — raise it for more "
                    + "room, not to make the fire bigger.", s.fireReach, 0f, 1f),
                host.Val("Edge cooling", "How hard the flame is cooled once past the Reach radius.",
                    s.fireEdgeCooling, 0f, 1f)));

            // ── Output / sim ──
            box.Add(Z.HGroup(
                Z.MicroSlider("Steps", s.fireSteps, 1f, 8f,
                    "Simulation steps per frame — more = smoother, faster-evolving motion for the same frame count "
                    + "(it does not change the flame's shape, only how far it gets each frame).",
                    v => host.Dirty(() => s.fireSteps = Mathf.Clamp(Mathf.RoundToInt(v), 1, 8)), 150f, showValue: true, decimals: 0),
                Z.MicroSlider("Threshold", s.fireThreshold, 0f, 0.9f,
                    "Heat below this reads as empty — raise it to carve a crisper silhouette.",
                    v => host.Dirty(() => s.fireThreshold = v), 150f, showValue: true)));
            box.Add(Z.MicroSlider("Contrast", s.fireContrast, 0.05f, 2f,
                "Contrast on the gradient lookup — below 1 pushes more of the flame toward the hot end of the ramp.",
                v => host.Dirty(() => s.fireContrast = v), 150f, showValue: true));

            host.Body.Add(box);
        }

        // Fireball form box — the stateful CELLULAR SIMULATION (slice 6b). Heat blooms OUTWARD from one central point,
        // folded into `Arms` kaleidoscope wedges, so it reads as a radial/star explosion cooling at the rim. SINGLE-
        // SOURCE (no swarm, no particles — the whole layer IS the sim). Every rate is an envelope over the LAYER's life,
        // grouped Progress → Kaleidoscope → Core & reach → Arm shape → Output. Its colour ramp is the shared Shape Fill
        // above (smoke→fire) and its overall opacity the shared Shape Alpha; `size` and the Swarm don't apply. Reuses
        // Pyre's own FireballSim via the renderer's replay harness — the dials here map 1:1 onto Pyre's Fireball fields.
        public static void BuildFireballBox(IPyreShapeCardHost host, PyreLayer s)
        {
            // Defensive nulls (the real defaults come from the spec factories).
            s.fireballSource ??= new ZUIValue(1f);
            s.fireballSourceRadius ??= new ZUIValue(4f);
            s.fireballCooling ??= new ZUIValue(0.03f);
            s.fireballSharpness ??= new ZUIValue(0.2f);
            s.fireballSpread ??= new ZUIValue(0.5f);
            s.fireballReach ??= new ZUIValue(0.95f);

            var box = Z.BoxKeyed("Fireball",
                "A stateful CELLULAR flame SIMULATION (the cheap \"doom-fire\" family): heat blooms OUTWARD from one "
                + "central point and is folded into Arms kaleidoscope wedges, so it reads as a radial / star explosion "
                + "cooling at the rim. Reached by REPLAYING the sim from frame 0 (scrubbing and baking stay exact). "
                + "Every rate is an envelope over the layer's life — you author the SHAPE of the burn. SINGLE-SOURCE: "
                + "the Swarm doesn't apply. Colour is the Shape Fill above (smoke→fire ramp); overall opacity is the "
                + "Shape Alpha.", "pyreplus.fireball");

            // Progress — the master burn envelope (how hot the centre injects over life).
            box.Add(host.Val("Progress",
                "The burn's PROGRESS over the layer's life as ONE envelope — how hot the centre injects: 0 = off, "
                + "1 = full. Shape this to ignite, hold and die back, instead of setting a speed.",
                s.fireballSource, 0f, 1f));

            // ── Kaleidoscope ──
            box.Add(Z.HGroup(
                Z.MicroSlider("Arms", s.fireballArms, 1f, 12f,
                    "Radial wedges the flame is mirrored into. 1 = a plain outward burst; more give a kaleidoscope "
                    + "explosion — a 5-arm fireball is a 5-point star. Sharpness opens the cold gaps between arms.",
                    v => host.Dirty(() => s.fireballArms = Mathf.Clamp(Mathf.RoundToInt(v), 1, 12)), 150f, showValue: true, decimals: 0),
                Z.Field("Mode",
                    "Mirror = alternate wedges are reflected, so neighbours meet at a seam (a true kaleidoscope). "
                    + "Repeat = each wedge is the same, just rotated. Only matters with more than one arm.",
                    Z.Segmented(s.fireballMirror ? 0 : 1, FireballMirrorChoices,
                        "Mirror = alternate wedges reflected; Repeat = rotated copies.",
                        v => host.Dirty(() => s.fireballMirror = (v == 0))))));

            // ── Core & reach ──
            box.Add(Z.HGroup(
                host.Val("Core radius (px)", "Radius of the hot core at the centre, in pixels — the source the arms grow from.",
                    s.fireballSourceRadius, 1f, Mathf.Max(2f, host.CanvasSize * 0.5f)),
                host.Val("Reach", "How far the flame may reach, as a fraction of the canvas half-size. Past this it is cooled "
                    + "to nothing, so it can NEVER touch the frame edge — raise it to give long arms room.",
                    s.fireballReach, 0f, 1f)));

            // ── Arm shape ── (LENGTH vs THINNESS, set independently — the whole point of the fireball's look)
            box.Add(Z.HGroup(
                host.Val("Cooling", "How fast the flame cools travelling outward — this sets arm LENGTH. Low = long reaching "
                    + "tongues; high = a tight core. Pair a LOW value here with high Sharpness for long thin arms.",
                    s.fireballCooling, 0f, 0.3f),
                host.Val("Sharpness", "How hard the arms taper — their THINNESS, set independently of length. High = narrow "
                    + "pointed spokes; 0 = a round burst. Only matters with more than one arm.",
                    s.fireballSharpness, 0f, 2f)));
            box.Add(host.Val("Spread", "Sideways waver of the tongues — how much they lick and slip instead of being straight "
                + "radial spokes.", s.fireballSpread, 0f, 2f));

            // ── Output / sim ──
            box.Add(Z.HGroup(
                Z.MicroSlider("Threshold", s.fireballThreshold, 0f, 0.9f,
                    "Heat below this reads as empty — raise it to carve a crisper silhouette.",
                    v => host.Dirty(() => s.fireballThreshold = v), 150f, showValue: true),
                Z.MicroSlider("Contrast", s.fireballContrast, 0.05f, 2f,
                    "Contrast on the gradient lookup — below 1 pushes more of the flame toward the hot end of the ramp.",
                    v => host.Dirty(() => s.fireballContrast = v), 150f, showValue: true)));

            host.Body.Add(box);
        }

        // Text form box — the string, the SDF font, spacing, the fill (mode + angle + gradient), the border (width
        // + gradient), and the 3D extrusion (Solid + Depth). Depth shows only when Solid, so the Solid toggle
        // rebuilds the Shape body. Text takes its colour from the Fill / Border gradients here — the shared Colour
        // row above is hidden for it. Size (above) is the character height; Tilt is the shared gemTilt (default);
        // per-letter Spin lives in the Advanced section.
        public static void BuildTextBox(IPyreShapeCardHost host, PyreLayer s)
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
                    v => host.Dirty(() => s.textString = v), 200f)));

            box.Add(Z.Field("Font",
                "The TMP SDF font asset. Its atlas must be Read/Write-enabled (a Dynamic SDF font works). Leave "
                + "empty to auto-pick the first readable font in the project; a missing/non-readable font falls back "
                + "to a plain disc per character.",
                Z.Object<TMP_FontAsset>(s.textFont,
                    "SDF font — needs a readable atlas; leave empty to auto-pick.",
                    v => host.Dirty(() => s.textFont = v), 200f)));

            box.Add(Z.MicroSlider("Spacing", s.textSpacing, 0.6f, 1.6f,
                "Letter advance multiplier for the centred line layout — below 1 tightens the letters, above 1 "
                + "spreads them apart. (Only affects the swarm-off line; a swarm places letters by its own shape.)",
                v => host.Dirty(() => s.textSpacing = v), 150f, showValue: true));

            // Sweep is a wrapped MiniRadio (was a Dropdown/context-menu): three modes read as radio buttons that
            // fold onto a second line in a narrow column. On its own row (the labels are long), with Angle below.
            box.Add(Z.Field("Sweep",
                "How the Fill's gradient is swept across the letters. Per-char gradient = each letter contains "
                + "the whole gradient. Per-char step = each letter one flat colour along the gradient, by index. "
                + "Text gradient = one gradient swept across the whole line (degrades to per-char step when the "
                + "Swarm is on — there's no line to sweep). Text takes its colour from the Fill below, NOT the "
                + "Colour gradient above. (A Solid or textured Fill ignores this — see the Fill.)",
                Z.MiniRadio((int)s.textFillMode, TextFillModeChoices.ToArray(),
                    "How the Fill's gradient is swept: per-char gradient / per-char step / one gradient across the whole line.",
                    v => host.Dirty(() => s.textFillMode = (TextFillMode)v), wrap: true)));
            box.Add(Z.MicroSlider("Angle", s.textGradientAngle, -180f, 180f,
                "Rotates the fill axis. 0 = vertical bottom→top for per-char gradient; 0 = left→right across "
                + "the line for text gradient. (Per-char step is index-based and ignores it.)",
                v => host.Dirty(() => s.textGradientAngle = v), 150f, showValue: true));

            box.Add(host.FillRow("Fill",
                "The letter fill — a solid colour, a gradient (swept per the Sweep mode above), or a texture "
                + "(sprite / noise / grid / dots) stamped across each letter. This is where Text's colour comes "
                + "from; the Shape Fill above is hidden for Text.",
                s.textFill, new ZuiFillControl.Options().WithWidth(190f).WithGrow(2.2f)));

            box.Add(Z.MicroSlider("Border px", s.textBorderWidth, 0f, 4f,
                "Letter outline width in screen pixels (0 = no border). Drawn as an SDF band just inside each "
                + "glyph edge, coloured from the Border fill below.",
                v => host.Dirty(() => s.textBorderWidth = v), 150f, showValue: true));
            box.Add(host.FillRow("Border",
                "The letter outline fill, sampled the same way as the Fill (solid / gradient / texture). A single "
                + "colour reads as a solid outline.",
                s.textBorder, new ZuiFillControl.Options().WithWidth(190f).WithGrow(2.2f)));

            box.Add(Z.Toggle("Solid",
                "Extrude each letter into a 3D box (a lit front face + darker extrusion sides). Off = a flat 2D "
                + "letter plane.",
                s.textSolid, v => { host.Dirty(() => s.textSolid = v); host.RebuildShape(); }));
            if (s.textSolid)
                box.Add(Z.MicroSlider("Depth", s.textDepth, 0.05f, 1f,
                    "Extrusion depth as a fraction of the character size — how deep the 3D letter boxes are.",
                    v => host.Dirty(() => s.textDepth = v), 150f, showValue: true));

            host.Body.Add(box);
        }


        // The Solid box tooltip, naming the CURRENT solid form (the box rebuilds on form change).
        public static string SolidBoxTooltip(ShapeForm f)
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

        // FIX 1 — when a layer shows a 3D solid form (Gem/Box/Pyramid/Can/Orb/Ring) and its Fill is still the EXACT
        // pristine OverLife fire default, swap that fill to a STEADY Solid material (the gradient's mid colour) so
        // the lit solid reads as light-driven, not "pulsing": the OverLife gold→dark-red life ramp darkens the whole
        // gem over its life, and the light dials can't counter it because it IS the material colour. Only the
        // untouched factory default is converted (IsPristineDefaultShapeFill is strict); a user-customised fill is
        // left exactly as-is, and Disc/Crescent/Sparkle/Sprite/Text/Streak/Star keep the OverLife fire default
        // (right for soft particles). MUST be called inside a host.Dirty() block (records Undo). One-way — never converts
        // a Solid back to OverLife. Returns true when it changed the fill.
        public static bool SteadyDefaultFillForSolid(PyreLayer layer)
        {
            if (layer == null || !IsSolidForm(layer.shapeForm)) return false;
            var f = layer.shapeFill;
            if (!PyreLayer.IsPristineDefaultShapeFill(f)) return false;
            f.color = f.gradient.Evaluate(0.5f);   // a sensible mid material (the fire gradient's midpoint)
            f.mode = ZuiFill.Mode.Solid;
            return true;
        }

        // The 3D-solid forms — they share BuildSolidBox (Solid geometry + the Light / Lines / Glow sibling boxes) and
        // edit particleSpin in the Position box as "Turn °" (Tilt/Roll join it there), so the flat forms' "Spin °"
        // row is not shown for them. Disc / Crescent / Sparkle / Sprite / Text are NOT solid forms.
        public static bool IsSolidForm(ShapeForm f) =>
            f == ShapeForm.Gem || f == ShapeForm.Box || f == ShapeForm.Pyramid ||
            f == ShapeForm.Can || f == ShapeForm.Orb || f == ShapeForm.Ring;

        // The flat 2D forms that get a first-class Border (task #60/#65) — mirrors PyreRenderer.IsFlat2DBorderForm
        // exactly (Ring counts as a flat 2D form here, unlike the 3D solids). The 3D solids, Text, and Sprite/Fire/
        // Fireball/Sparkle are excluded, so the Border box never shows for them.
        public static bool IsFlat2DBorderForm(ShapeForm f) =>
            f == ShapeForm.Disc || f == ShapeForm.Crescent || f == ShapeForm.Ring ||
            f == ShapeForm.Streak || f == ShapeForm.Star || f == ShapeForm.Polygon;

        // The Border sub-box (task #60/#65), shown only for the flat 2D forms. Off = a single compact toggle row (the
        // common default, minimal footprint); on = a titled box with Width / Fill / Draw-over-matte. Toggling Enable
        // rebuilds the Shape body so the box expands/collapses. Every edit is Undo-safe (Dirty / the Fill/Val contract).
        public static void BuildBorderBox(IPyreShapeCardHost host, PyreLayer s)
        {
            s.borderWidth ??= new ZUIValue(2f);            // defensive; the real defaults come from the spec factories
            s.borderFill ??= new ZuiFill(new Color(1f, 1f, 1f, 1f));

            if (!s.borderEnabled)
            {
                host.Body.Add(Z.Toggle("Border",
                    "Add a coloured rim around this shape's silhouette (the outermost few px of the drawn alpha) — the "
                    + "2D counterpart to the 3D solids' edge lines, and what a disc used as a ball wants for a rim. "
                    + "Turn on to set its width, fill and draw-over-matte.",
                    s.borderEnabled, v => { host.Dirty(() => s.borderEnabled = v); host.RebuildShape(); }));
                return;
            }

            var box = Z.BoxKeyed("Border",
                "A coloured rim around this shape's silhouette — the outermost Width px of the drawn alpha, in the "
                + "Border fill (the 2D counterpart to the 3D solids' edge lines).",
                "pyreplus.border", "bounding-box");
            // Collapsed, the box hides an active rim — mark it so folding away the border doesn't hide that it's on.
            box.SetHeaderSuffix(() => s.borderEnabled ? " (on)" : "");
            // The rim's switch is the box header's own checkbox, so the header names the rim and turns it on.
            box.SetHeaderToggle(s.borderEnabled,
                "Draw the rim. Off = no border (the shape is unchanged, byte-identical to no border).",
                v => { host.Dirty(() => s.borderEnabled = v); host.RebuildShape(); });
            box.Add(host.Val("Width (px)",
                "Rim thickness in pixels, over the layer's life — the outermost N px of the shape's silhouette are "
                + "recoloured to the Border fill (the rim keeps the shape's anti-aliased edge).",
                s.borderWidth, 0f, Mathf.Max(8f, host.CanvasSize / 4f)));
            box.Add(host.FillRow("Fill",
                "The rim's colour/fill — Solid, a gradient, or a spatial fill (alpha-capable), like the shape's own "
                + "Fill. A spatial fill is mapped across the shape's bounding box.",
                s.borderFill, new ZuiFillControl.Options().WithWidth(190f).WithGrow(2.2f)));
            box.Add(Z.Toggle("Draw over matte",
                "When this layer feeds a matte (Write / Luma) or is clipped: send only the FILL into the mask and draw "
                + "the BORDER on top of the finished frame instead — so a shape's fill can BE the matte while its "
                + "border still shows, with no separate outline-only layer. Off = the border is part of the layer.",
                s.borderOverMatte, v => host.Dirty(() => s.borderOverMatte = v)));
            host.Body.Add(box);
        }


        // ── per-form tooltip composers (rebuilt on every form change, so each branches to the CURRENT form) ──
        public static string FillTooltip(ShapeForm f)
        {
            const string modes = " Solid = one flat colour; Over life = a gradient across the particle's life; "
                + "Linear / Radial / Noise = a spatial fill across the shape. Every mode is alpha-capable (⋯ to switch).";
            if (IsSolidForm(f))
                return "The solid's material fill — a blue gradient reads as a sapphire." + modes;
            if (f == ShapeForm.Sprite)
                return "Tints the stamped sprite (multiplied over its colours), sampled at the particle centre only — "
                     + "a spatial fill has no effect on a sprite tint. Turn Tint off for the sprite's raw colours." + modes;
            if (f == ShapeForm.Fire)
                return "The flame's colour RAMP: the sim reads this fill's GRADIENT as one smoke→fire ramp (low end = "
                     + "smoke, high end = fire), mapping each pixel's heat onto it — like Height balls. Prefer an Over "
                     + "life gradient; a Solid fill leaves the flame white." + modes;
            if (f == ShapeForm.Fireball)
                return "The fireball's colour RAMP: the cellular sim reads this fill's GRADIENT as one smoke→fire ramp "
                     + "(low end = cool rim, high end = hot core), mapping each pixel's heat onto it. Prefer an Over "
                     + "life gradient; a Solid fill leaves the flame white." + modes;
            return "The particle's colour (0 = birth, 1 = death)." + modes;
        }

        public static string SizeTooltip(ShapeForm f)
        {
            if (IsSolidForm(f))
                return "Radius in pixels over the particle's own life — the base size R that Aspect / Depth (or the "
                     + "Gem's Crown / Pavilion) scale from.";
            if (f == ShapeForm.Text)
                return "The character HEIGHT in pixels over the particle's own life.";
            if (f == ShapeForm.Star)
                return "The star's TIP radius in absolute pixels over the particle's own life — the arms reach out to "
                     + "it (the valleys sit at Length inside). It only reads as 'big' because the canvas is small.";
            if (f == ShapeForm.Polygon)
                return "The polygon's circumradius in absolute pixels over the particle's own life — every vertex "
                     + "reaches out to it (the same pixel convention as every form).";
            return "Radius in pixels over the particle's own life.";
        }

        // Spin only shows for the 2D forms + Text (solids edit it as Turn), so this branches only those cases.
        public static string SpinTooltip(ShapeForm f)
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
                case ShapeForm.Polygon:
                    return "Degrees the polygon spins in place over its own life — unlike a disc, a regular polygon "
                         + "isn't radially symmetric, so its corners visibly turn.";
                case ShapeForm.Text:
                    return "Degrees each letter yaws about its OWN centre over its life (its 3D letter-box turns to "
                         + "face the light); paired with the letters' tilt for the extruded look.";
                default:   // Disc
                    return "Degrees the disc's pixels spin in place over its own life — a plain disc is radially "
                         + "symmetric so it shows little (add a geometry/texture Modifier for the spin to read).";
            }
        }

        public static string TurnTooltip(ShapeForm f)
        {
            const string common = "Rotate around the VERTICAL axis, like a turntable (yaw), over the particle's own life. ";
            if (f == ShapeForm.Orb)
                return common + "For the Orb it rolls the lit hotspot left/right around the ball (the silhouette never changes).";
            if (f == ShapeForm.Ring)
                return common + "For the Ring it swings the ellipse — turning the ring edge-on along the horizontal axis.";
            return common + "Sweeps the solid's facets past the light.";
        }

        public static string TiltTooltip(ShapeForm f)
        {
            const string common = "Lean the top toward or away from you (about the HORIZONTAL axis), over the particle's own life. ";
            if (f == ShapeForm.Orb)
                return common + "For the Orb it rolls the lit hotspot up/down across the ball (the silhouette never changes).";
            if (f == ShapeForm.Ring)
                return common + "For the Ring it opens/closes the ellipse — 0° face-on (a full circle), ±90° edge-on (a sliver).";
            return common + "Tips the solid so different facets catch the light.";
        }

        // Roll is hidden for the Ring (geometrically inert there), so this only ever branches Orb vs the facet solids.
        public static string RollTooltip(ShapeForm f)
        {
            const string common = "Rotate the solid flat against the screen (about the axis pointing at you), over the particle's own life. ";
            if (f == ShapeForm.Orb)
                return common + "For the Orb it rolls the lit hotspot around the centre of the ball.";
            return common + "Spins the whole silhouette in the screen plane.";
        }

    }
}
