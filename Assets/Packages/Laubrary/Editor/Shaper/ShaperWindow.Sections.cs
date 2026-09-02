// ShaperWindow.Sections — the AUTHORING CARDS (Phase C2).
//
// ShaperWindow.cs is the shell (window, document binding, Canvas, Layers, Shape/Primitive, Transform,
// preview, transport, Bake) and owns the shared helpers. This file adds the cards it deferred:
// Fill, Border, Shape modifiers (Blend/Sweep/Shell), Swarm, the Composite generator, Bag children +
// drill-down, and the two LAYER-level cards (Light response, Height/extrusion), plus a read-only
// Effects catalog.
//
// ── Val vs Dial was decided PER FIELD, from source, never from a category ────────────────────────────────
// The mock regressed a whole class of fill dials to plain floats on a doc citation that turned out to be
// wrong, so every choice here was made by reading the declaration. What the engine actually says:
//   • ShaperFillDef (ShaperFillDef.cs) declares 26 of its dials as ZUIValue -> Val:
//     veil:60, heightDelta:68, gradientAngleDegrees:113, gradientCentreX:116, gradientCentreY:117,
//     gradientSize:126, gradientDepthPixels:129, rampInputLow:145, rampInputHigh:151,
//     textureTilesX:165, textureTilesY:166, textureOffsetU:169, textureOffsetV:170,
//     textureAngleDegrees:173, stripRepeats:199, stripOrientationDegrees:202, stripOffset:205,
//     stripReach:216, heightFieldScale:248, steelCells:260, steelOctaves:263, steelSeed:266,
//     steelRustAmount:278, steelRustReachPixels:281, steelGrain:284, quantiseLevels:292.
//   • ShaperBorderDef.width:69 is ZUIValue -> Val. Its alignment/enabled/joinsCoverage are not.
//   • ShaperHeightDef (ShaperHeight.cs:95-126) is ZUIValue throughout -> Val.
//   • ShaperLightResponse (ShaperLightRig.cs:225-304) is ZUIValue for its dials -> Val, but
//     normalConstant:295 is a Vector3 and specularTint:283 a Color.
//   • ShaperBlend/ShaperSweep/ShaperShell (ShaperNode.cs:24-69) and ShaperSwarmDef's jitter dials are
//     PLAIN floats -> Dial. Their [Range] bounds are copied from the declaration, not invented.
//
// ── Three things the engine does not currently support, reported rather than faked ──────────────────────
//   • SOLIDS HAVE NO ATTACHMENT POINT. ShaperNodeKind is exactly {Primitive, Bag, Composite}
//     (ShaperNode.cs:12) and NOTHING in Runtime/ declares a ShaperSolidDef field — a repo-wide search for
//     one returns zero hits. ShaperSolidDef is fully authored (ShaperSolids.cs:32) with a real per-form
//     inertness table, but a document cannot reference one, so there is nothing for a Solids card to bind
//     to. Building one would mean an engine change (a 4th node kind + ShaperNode.solid + compiler
//     routing), which is an open owner decision and outside this file. No card is drawn.
//   • (SUPERSEDED by T-0163.) This used to say there was no authored effect stack to edit. There are now two:
//     ShaperLayer.effects (pre-composite) and ShaperDocument.effects (post-composite), each holding real
//     serialized modifier instances, so the Effects cards below are editors and not a read-only catalog view.
//   • A hosted composite form's dials resolve to their STATIC value only when rendered
//     (PyreFormCompositeSource.cs:22,44), so animating one has no effect through this path today. The
//     generator card says so on the control rather than letting a user animate into a no-op.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Laubrary.Pyre;
using Laubrary.PyreShaper;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Shaper.Editor
{
    public partial class ShaperWindow
    {
        // Sections owned by THIS file. Declared here rather than in the shell so the split stays clean:
        // each file declares the chrome it builds.
        // T-0182 — Generator, Children, Solid and Modifiers are no longer sections of their own: what a node
        // draws is ONE question answered by the Shape card's picker, so those bodies are now drawn inside
        // that card and appear only for the shape that has them.
        ZuiSection fillSection, borderSection, swarmSection,
                   responseSection, effectsSection,
                   layerEffectsSection;

        // T-0187 — Height and Mask stopped being toggle-bar ZuiSections (the owner: "I get the feeling you
        // shouldn't make them into sections... perhaps part of the layer item in the layer list") and became
        // foldable Z.BoxKeyed cards under the SELECTED layer's row instead — built by BuildLayersSection
        // (ShaperWindow.cs), not here. This set makes each card start FOLDED the first time it is ever built
        // in this editor session; after that, ZuiBox's own static fold dictionary remembers the user's choice
        // like every other box, exactly as ShaperWindow.Lights.cs's per-light cards already do.
        static readonly HashSet<string> s_layerCardDefaultedClosed = new HashSet<string>();

        /// Which bag member is being edited, as a path of child indices from the layer root. Empty = the
        /// root itself. This is VIEW state, not authored data — it is deliberately not [SerializeField]'d
        /// as document content, and EnsureDrillValid repairs it after an Undo that restructures the tree
        /// out from under it (deleting a child the path pointed into would otherwise throw on the next
        /// rebuild).
        [SerializeField] List<int> drillPath = new List<int>();

        /// Resolve the node the authoring cards edit by walking `drillPath` from the layer root. The shell's
        /// CurrentNode forwards to this — its own comment anticipated exactly this swap, so every card that
        /// already reads CurrentNode keeps working with no change.
        internal ShaperNode ResolveCurrentNode()
        {
            var node = CurrentLayer?.root;
            if (node == null) { drillPath.Clear(); return null; }
            for (int i = 0; i < drillPath.Count; i++)
            {
                int idx = drillPath[i];
                if (node.children == null || idx < 0 || idx >= node.children.Count)
                {
                    // The path outran the tree (an Undo or a delete removed the member). Truncate to the
                    // deepest still-valid node rather than throwing or silently showing the root.
                    drillPath.RemoveRange(i, drillPath.Count - i);
                    return node;
                }
                node = node.children[idx];
            }
            return node;
        }

        /// Every card this file owns, for the selected layer's current node. Called by the shell.
        internal void BuildAuthoringSections(VisualElement root, ShaperNode node)
        {
            var layer = CurrentLayer;
            if (layer == null || node == null) return;

            BuildBreadcrumb(root, layer);

            // The per-shape bodies (generator dials, bag members, solid dials) and the combine/sweep/shell
            // ops moved INTO the Shape card in T-0182 — see ShaperWindow.BuildShapeSection. They are drawn
            // by the same absence rule, one card closer to the choice that summons them.
            // T-0191 — A COMPOSITE NODE GETS NEITHER CARD, AND THEY ARE ABSENT RATHER THAN DISABLED.
            // SHAPER_THE_DESIGN §6.2: a composite "produces a finished picture directly" and "may not be
            // re-filled"; its colour is authored on the GENERATOR (the hosted Pyre card's own fill ramp) and
            // its edge is a raster, so a Shaper fill would be a second authority over the same pixel and a
            // border has no analytic edge to trace. A greyed card would still be a promise — it says "this
            // exists, you just cannot reach it today" — where the truth is that it does not apply at all.
            if (node.kind != ShaperNodeKind.Composite)
            {
                BuildFillSection(root, node);
                BuildBorderSection(root, node);
            }
            BuildSwarmSection(root, node);

            // LAYER-level cards. These bind to ShaperLayer, not to the node — the mock had them on the node
            // and that was corrected. Shown only at the layer root, because a bag member has no layer of its
            // own to author and drawing them while drilled in would be a lie about what is being edited.
            // T-0187 — Height and Mask no longer build here: they moved into BuildLayersSection
            // (ShaperWindow.cs), which draws them for the SELECTED layer regardless of drillPath, since they
            // are layer-level, not node-level, and the Layers section isn't about node drilling at all.
            if (drillPath.Count == 0) BuildResponseSection(root, layer);

            BuildEffectsSection(root, layer);
        }

        /// The sections this file adds, for the shell's toggle bar. Null entries are skipped by the bar.
        internal (string, ZuiSection)[] SectionBarEntries() => new[]
        {
            ("Fill", fillSection), ("Border", borderSection),
            ("Swarm", swarmSection),
            ("Lighting", responseSection),
            ("SpriteFX", layerEffectsSection), ("Global SpriteFX", effectsSection),
        };

        // ── Solids (T-0155) ──────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// A Solids generator's dials, with the engine's own inertness table driving the greying.
        ///
        /// <see cref="ShaperSolids.InertReason"/> is a real, shipped 6-form × 14-dial table stating which dial
        /// does nothing on which form AND why — Aspect on an Orb, Depth on a Can, Roll on a Ring, the Gem-only
        /// trio everywhere else. The engine's rule for it is "declare inert with a reason, never hide", so an
        /// inert dial is drawn, disabled, and carries the reason as its tooltip rather than vanishing and
        /// leaving the user to wonder whether they imagined it.
        /// </summary>
        VisualElement SolidVal(string label, string tip, ZUIValue v, float lo, float hi,
                               ShaperSolidForm form, ShaperSolidDial dial, int decimals = -1)
        {
            string reason = ShaperSolids.InertReason(form, dial);
            var el = Val(label, reason ?? tip, v, lo, hi, decimals: decimals);
            if (reason != null)
            {
                el.SetEnabled(false);
                el.tooltip = "Does nothing on a " + form + ": " + reason;
            }
            return el;
        }

        /// The solid dials, drawn inside the Shape card for a node whose picked shape is a solid (T-0182).
        /// The form itself is no longer chosen here — it is one of the Solids column's entries in the shape
        /// picker, because "which solid" is the same question as "what does this node draw".
        internal void BuildSolidBody(VisualElement box, ShaperNode node)
        {
            var s = node.solid ?? (node.solid = new ShaperSolidDef());

            box.Add(Z.HGroup(
                SolidVal("Size", "The solid's radius, in canvas pixels.", s.size, 1f, 128f,
                    s.form, ShaperSolidDial.Size),
                SolidVal("Centre X", "Canvas position of the solid's centre.", s.centreX, -128f, 128f,
                    s.form, ShaperSolidDial.Centre),
                SolidVal("Centre Y", "Canvas position of the solid's centre.", s.centreY, -128f, 128f,
                    s.form, ShaperSolidDial.Centre),
                SolidVal("Aspect", "The Y half-extent multiplier.", s.aspect, 0.1f, 4f,
                    s.form, ShaperSolidDial.Aspect),
                SolidVal("Depth", "The Z half-extent multiplier.", s.depth, 0.1f, 4f,
                    s.form, ShaperSolidDial.Depth)));

            box.Add(Z.HGroup(
                SolidVal("Gem sides", "Girdle sides, 3..8.", s.gemSides, 3f, 8f,
                    s.form, ShaperSolidDial.GemSides, decimals: 0),
                SolidVal("Gem crown", "Crown height as a fraction of the radius.", s.gemCrown, 0f, 2f,
                    s.form, ShaperSolidDial.GemCrown),
                SolidVal("Gem pavilion", "Pavilion depth as a fraction of the radius.", s.gemPavilion, 0f, 2f,
                    s.form, ShaperSolidDial.GemPavilion),
                SolidVal("Ring inner", "Hole radius as a fraction of the radius.", s.ringInner, 0.1f, 0.92f,
                    s.form, ShaperSolidDial.RingInner)));

            box.Add(Z.HGroup(
                SolidVal("Yaw", "Rotation about Y, in degrees.", s.yaw, -180f, 180f,
                    s.form, ShaperSolidDial.Yaw, decimals: 0),
                SolidVal("Tilt", "Rotation about X, in degrees.", s.tilt, -180f, 180f,
                    s.form, ShaperSolidDial.Tilt, decimals: 0),
                SolidVal("Roll", "Rotation about Z in model space, applied first, in degrees.", s.roll,
                    -180f, 180f, s.form, ShaperSolidDial.Roll, decimals: 0)));

            // "A facet edge line and never a border" (BD-4.1): these trace INTERIOR facet seams, which are
            // nowhere in the zero set of any 2D field, so no border stage could produce them. The label says
            // line, not border, for that reason.
            box.Add(Z.HGroup(
                SolidVal("Line width", "Facet edge line half-width, in canvas pixels.", s.lineWidth, 0f, 8f,
                    s.form, ShaperSolidDial.LineWidth),
                Z.Field("Line colour", "The facet edge line's colour.",
                    Z.Color(s.lineColour, "The facet edge line's colour.",
                        c => Change(() => s.lineColour = c), 90f)),
                SolidVal("Edge glow", "Halo strength.", s.edgeGlow, 0f, 1f,
                    s.form, ShaperSolidDial.EdgeGlow),
                Z.Field("Edge glow colour", "The halo's colour.",
                    Z.Color(s.edgeGlowColour, "The halo's colour.",
                        c => Change(() => s.edgeGlowColour = c), 90f)),
                SolidVal("Inner glow", "Inner-glow strength.", s.innerGlow, 0f, 1f,
                    s.form, ShaperSolidDial.InnerGlow),
                Z.Field("Inner glow colour", "The inner glow's colour.",
                    Z.Color(s.innerGlowColour, "The inner glow's colour.",
                        c => Change(() => s.innerGlowColour = c), 90f))));
        }

        // ── breadcrumb ───────────────────────────────────────────────────────────────────────────────────

        void BuildBreadcrumb(VisualElement root, ShaperLayer layer)
        {
            if (drillPath.Count == 0) return;   // nothing to go back to; no chrome for the common case

            var row = new VisualElement();
            row.AddToClassList("zui-row");
            row.Add(Z.Button(layer.name ?? "Layer", "Back to this layer's root node.",
                () => { drillPath.Clear(); Rebuild(); }));

            var node = layer.root;
            for (int i = 0; i < drillPath.Count; i++)
            {
                int depth = i + 1;
                node = node.children[drillPath[i]];
                row.Add(Z.Text("›", ZuiText.Subtle));
                string label = string.IsNullOrEmpty(node.name) ? "(unnamed)" : node.name;
                row.Add(Z.Button(label, "Back to this member.",
                    () => { drillPath.RemoveRange(depth, drillPath.Count - depth); Rebuild(); }));
            }
            root.Add(row);
        }

        // ── Fill ─────────────────────────────────────────────────────────────────────────────────────────

        // ShaperNode.fill (:138) has NO initializer, so it is genuinely nullable and null is a meaningful
        // authored state: FC-3.2 makes the resolver substitute a default fill for a layer ROOT with an empty
        // slot, which is why a fresh layer renders at all without anyone touching this card. So the card
        // offers add/remove rather than pretending a fill always exists.
        void BuildFillSection(VisualElement root, ShaperNode node)
        {
            var box = fillSection = Z.Section("Fill", "How this node's coverage is coloured.",
                "shaper.window.fill", icon: "paint-bucket");

            if (node.fill == null)
            {
                box.Add(Z.Field("Fill",
                    "This node has no fill of its own. A layer root without one is painted with the "
                    + "engine's default fill; a child without one inherits from its owner.",
                    Z.Button("Add fill", "Give this node its own fill.",
                        () => { Change(() => node.fill = new ShaperFillDef()); Rebuild(); })));
                root.Add(box);
                return;
            }

            BuildFillBody(box, node.fill, "shaper.window.fill");
            box.Add(Z.Button("Remove fill", "Drop this node's own fill and fall back to the default/inherited one.",
                () => { Change(() => node.fill = null); Rebuild(); }));
            root.Add(box);
        }

        /// The fill editor, reused verbatim for a node's own fill and for a border's fill — they are the same
        /// ShaperFillDef type (ShaperBorderDef.fill:107), so one builder is the only honest way to keep them
        /// from drifting apart.
        void BuildFillBody(VisualElement box, ShaperFillDef f, string keyPrefix)
        {
            box.Add(Z.Field("Kind", "Which fill this is. Switching it changes the dials below.",
                Z.MiniRadio((int)f.kind, Enum.GetNames(typeof(ShaperFillKind)),
                    "Which fill this is.", v => { Change(() => f.kind = (ShaperFillKind)v); Rebuild(); },
                    wrap: true)));

            // Cross-kind fields, packed as ONE continuous group so an overflowing control lands beside the
            // next one instead of orphaned on a line of its own.
            box.Add(Z.HGroup(
                Z.Field("Composite", "Whether this fill paints OVER what is underneath or ADDS to it.",
                    Z.Segmented((int)f.composite, Enum.GetNames(typeof(ShaperFillComposite)),
                        "Over paints on top; Add sums, which is what makes glow read as glow.",
                        v => Change(() => f.composite = (ShaperFillComposite)v))),
                Z.Field("Space", "Whether the fill is stamped onto the shape or fixed to the canvas.",
                    Z.Segmented((int)f.space, Enum.GetNames(typeof(ShaperFillSpace)),
                        "Stamped moves with the shape; Fixed stays put while the shape moves through it.",
                        v => Change(() => f.space = (ShaperFillSpace)v))),
                Z.Field("Fit", "How the fill is fitted to the shape's bounds.",
                    Z.Segmented((int)f.fit, Enum.GetNames(typeof(ShaperFillFit)),
                        "Uniform preserves aspect; Stretch fills the bounds exactly.",
                        v => Change(() => f.fit = (ShaperFillFit)v))),
                // INVENTORY GAP CLOSED — veil (:60) and heightDelta (:68) apply to every fill kind and had
                // no UI anywhere. heightDelta is the input the relief shading of T-0110/T-0127 reads, so
                // without it relief could not be authored at all.
                Val("Veil", "Multiplies this fill's own transparency — the veil the palette applies on top "
                    + "of whatever edge rule the fill computed.", f.veil, 0f, 1f),
                Val("Height Δ", "How much this fill raises or lowers the surface it paints. This is the "
                    + "input the relief shading reads, so a non-zero value is what makes a fill sculpt "
                    + "rather than merely colour.", f.heightDelta, -32f, 32f)));

            switch (f.kind)
            {
                case ShaperFillKind.Solid:
                    box.Add(Z.Field("Colour", "The flat colour this fill paints.",
                        Z.Color(f.solidColor, "The flat colour this fill paints.",
                            c => Change(() => f.solidColor = c), 90f)));
                    break;

                case ShaperFillKind.Gradient: BuildGradientFill(box, f); break;
                case ShaperFillKind.RampByQuantity: BuildRampFill(box, f); break;
                case ShaperFillKind.Texture: BuildTextureFill(box, f); break;
                case ShaperFillKind.IndexedStrip: BuildStripFill(box, f, keyPrefix); break;
                case ShaperFillKind.HeightField: BuildHeightFieldFill(box, f); break;
                case ShaperFillKind.TapestrySteel: BuildSteelFill(box, f); break;
                case ShaperFillKind.OverPhase: BuildOverPhaseFill(box, f); break;
                case ShaperFillKind.Procedural: BuildProceduralFill(box, f); break;
            }

            // quantiseLevels (:292) is ZUIValue, and applies whatever the kind is.
            box.Add(Val("Quantise", "Snap the result to this many discrete colour bands. 0 leaves it smooth.",
                f.quantiseLevels, 0f, 32f, decimals: 0));
        }

        void BuildGradientFill(VisualElement box, ShaperFillDef f)
        {
            box.Add(Z.Field("Mode", "How the gradient is projected across the shape.",
                Z.MiniRadio((int)f.gradientMode, Enum.GetNames(typeof(ShaperGradientMode)),
                    "Linear sweeps along an angle; Radial runs out from a centre; Angular sweeps around it; "
                    + "By Edge Distance follows how far each sample is from the shape's edge.",
                    v => { Change(() => f.gradientMode = (ShaperGradientMode)v); Rebuild(); })));

            box.Add(Z.Field("Ramp", "The colour ramp this gradient samples.", Gradient(f.gradient,
                "The colour ramp this gradient samples.")));

            // INVENTORY GAP CLOSED — centre/size/depth (:116-129) had no UI, which made Radial and Angular
            // unusable: both are defined by a centre the user could not move. Only drawn for the modes that
            // actually read them, per the absence rule.
            var rows = new List<VisualElement>
            {
                Val("Angle", "The direction the gradient sweeps, in degrees.",
                    f.gradientAngleDegrees, 0f, 360f, cyclic: true, decimals: 0),
                Z.Field("Tint", "Multiplies the sampled ramp colour.",
                    Z.Color(f.gradientTint, "Multiplies the sampled ramp colour.",
                        c => Change(() => f.gradientTint = c), 90f)),
            };
            if (f.gradientMode == ShaperGradientMode.Radial || f.gradientMode == ShaperGradientMode.Angular)
            {
                // W6.3 — these three are normalised to the shape's own half-extent (1 = the node's own edge
                // along this axis, FC-1.5), never canvas pixels. The old "in canvas pixels" tooltip and the
                // -128..128 / 0..256 ranges were sized for a pixel-scale dial on a unit whose entire
                // meaningful range is roughly -2..2 / 0..3 — dragging "Size" even a little on that old scale
                // overshot the shape by 50-100x, which is what made a freshly authored gradient read as
                // "barely showing": the ramp's whole variation got compressed into a sliver near t=0.5.
                rows.Add(Val("Centre X", "Where the gradient radiates from, as a fraction of the shape's own "
                    + "half-width — 0 is the shape's centre, 1 is its edge.",
                    f.gradientCentreX, -2f, 2f));
                rows.Add(Val("Centre Y", "Where the gradient radiates from, as a fraction of the shape's own "
                    + "half-height — 0 is the shape's centre, 1 is its edge.",
                    f.gradientCentreY, -2f, 2f));
                rows.Add(Val("Size", "How far the gradient reaches before it clamps or repeats, as a fraction "
                    + "of the shape's own half-extent — 1 reaches exactly to the shape's own edge.",
                    f.gradientSize, 0f, 4f));
            }
            if (f.gradientMode == ShaperGradientMode.ByEdgeDistance)
                rows.Add(Val("Depth", "How far in from the edge the ramp is spread, in pixels.",
                    f.gradientDepthPixels, 0f, 128f));
            box.Add(Z.HGroup(rows.ToArray()));
        }

        void BuildRampFill(VisualElement box, ShaperFillDef f)
        {
            box.Add(Z.Field("Quantity", "Which published quantity drives the ramp.",
                Z.MiniRadio((int)f.rampQuantity, Enum.GetNames(typeof(ShaperQuantity)),
                    "The sheet this fill reads to decide where in the ramp each sample lands.",
                    v => Change(() => f.rampQuantity = (ShaperQuantity)v), wrap: true)));

            // INVENTORY GAP CLOSED — rampGradient (:141) had no UI, so RampByQuantity had no authorable ramp
            // at all: the user could choose what drove the ramp but not what the ramp looked like.
            box.Add(Z.Field("Ramp", "The colour ramp the chosen quantity is mapped through.",
                Gradient(f.rampGradient, "The colour ramp the chosen quantity is mapped through.")));

            // T-0172 — a preset REPLACES rampGradient wholesale (an authoring convenience, not a persistent
            // dial), so a button opening a picker is the right control, not a MiniRadio (which implies a
            // saved "current selection" this fill does not have — picking a preset does not remember which
            // one, only its effect).
            VisualElement presetButton = null;
            presetButton = Z.Button("Choose preset…", "Overwrites the ramp above with a Pyre preset, converted to a plain gradient you can then tune freely.",
                () => ShowRampPresetMenu(presetButton, f));
            box.Add(Z.Field("Presets", "Start the ramp above from one of Pyre's shipped ramps, then keep editing it.",
                presetButton));

            box.Add(Z.HGroup(
                Val("Input low", "The quantity value that maps to the START of the ramp.",
                    f.rampInputLow, -1f, 2f),
                Val("Input high", "The quantity value that maps to the END of the ramp.",
                    f.rampInputHigh, -1f, 2f),
                Z.Field("Tint", "Multiplies the sampled ramp colour.",
                    Z.Color(f.rampTint, "Multiplies the sampled ramp colour.",
                        c => Change(() => f.rampTint = c), 90f))));
        }

        void BuildTextureFill(VisualElement box, ShaperFillDef f)
        {
            box.Add(Z.Field("Texture", "The image this fill samples.",
                Z.Object<Texture2D>(f.texture, "The image this fill samples.",
                    t => Change(() => f.texture = t), 200f)));
            box.Add(Z.Field("Mapping", "How the texture is mapped onto the shape.",
                Z.Segmented((int)f.textureMapping, Enum.GetNames(typeof(ShaperTextureMapping)),
                    "Fitted stretches one copy to the bounds; Tiled repeats it.",
                    v => Change(() => f.textureMapping = (ShaperTextureMapping)v))));
            box.Add(Z.HGroup(
                Val("Tiles X", "How many times the texture repeats horizontally.", f.textureTilesX, 0.1f, 16f),
                Val("Tiles Y", "How many times the texture repeats vertically.", f.textureTilesY, 0.1f, 16f),
                Val("Offset U", "Slides the texture horizontally, in UV.", f.textureOffsetU, -1f, 1f),
                Val("Offset V", "Slides the texture vertically, in UV.", f.textureOffsetV, -1f, 1f),
                Val("Angle", "Rotates the texture, in degrees.",
                    f.textureAngleDegrees, 0f, 360f, cyclic: true, decimals: 0),
                Z.Field("Tint", "Multiplies the sampled texel.",
                    Z.Color(f.textureTint, "Multiplies the sampled texel.",
                        c => Change(() => f.textureTint = c), 90f))));

            // T-0172 — a sprite-sheet stepped by phase, distinct from the continuous scroll Offset U/V above
            // already give (FC-6.4e).
            box.Add(Z.Toggle("Animated", "Treat the texture as a sprite-sheet grid and step to one cell per "
                + "the node's own phase, instead of sampling the whole image.",
                f.textureAnimated, v => { Change(() => f.textureAnimated = v); Rebuild(); }));
            if (f.textureAnimated)
                box.Add(Z.HGroup(
                    Val("Columns", "How many frames the sheet is divided into horizontally.",
                        f.textureFrameColumns, 1f, 32f, decimals: 0),
                    Val("Rows", "How many frames the sheet is divided into vertically.",
                        f.textureFrameRows, 1f, 32f, decimals: 0),
                    Val("Frames", "How many of the grid's cells are used frames, in order from the bottom-left.",
                        f.textureFrameCount, 1f, 1024f, decimals: 0)));
        }

        void BuildStripFill(VisualElement box, ShaperFillDef f, string keyPrefix)
        {
            box.Add(Z.Field("Parameterisation", "How a sample is turned into a position along the strip.",
                Z.Segmented((int)f.stripParameterisation,
                    Enum.GetNames(typeof(ShaperStripParameterisation)),
                    "Angular walks around the shape; Projection walks along an axis.",
                    v => Change(() => f.stripParameterisation = (ShaperStripParameterisation)v))));
            box.Add(Z.HGroup(
                Val("Repeats", "How many times the strip repeats around/along the shape.",
                    f.stripRepeats, 0.1f, 16f),
                Val("Orientation", "Rotates where the strip starts, in degrees.",
                    f.stripOrientationDegrees, 0f, 360f, cyclic: true, decimals: 0),
                Val("Offset", "Slides the strip along itself.", f.stripOffset, -1f, 1f),
                Val("Reach", "How far in from the edge the strip is applied.", f.stripReach, 0f, 1f),
                Z.Field("Plain colour", "Painted where no slot applies.",
                    Z.Color(f.stripPlainColor, "Painted where no slot applies.",
                        c => Change(() => f.stripPlainColor = c), 90f))));

            // The strip's per-slot colour + height IS the authored content of this fill kind, and each slot's
            // height is what T-0110 restored protrusion with — so the list is editable, not a summary.
            var slots = Z.BoxKeyed("Slots", "Each slot paints one band of the strip, with its own height.",
                keyPrefix + ".slots");
            var listHost = new VisualElement();
            slots.Add(listHost);

            void RebuildSlots()
            {
                listHost.Clear();
                if (f.stripSlots == null) f.stripSlots = new List<ShaperStripSlot>();
                for (int i = 0; i < f.stripSlots.Count; i++)
                {
                    var slot = f.stripSlots[i];
                    int si = i;
                    var row = new VisualElement();
                    row.AddToClassList("zui-row");
                    var grip = Z.Text("≡", ZuiText.Body, "Drag to reorder — slot order is strip order.");
                    grip.style.width = 16f;
                    ZuiReorder.MakeGrip(grip, row, listHost, (from, to) =>
                    {
                        Change(() =>
                        {
                            var s = f.stripSlots[from];
                            f.stripSlots.RemoveAt(from);
                            f.stripSlots.Insert(to, s);
                        });
                        RebuildSlots();
                    });
                    row.Add(grip);
                    row.Add(Z.Color(slot.color, "This slot's colour.",
                        c => Change(() => slot.color = c), 90f));
                    row.Add(Dial("Height", "How far this slot's band protrudes.", slot.height, -32f, 32f,
                        v => slot.height = v));
                    row.Add(Z.Flexible());
                    row.Add(Z.Button("×", "Remove this slot.",
                        () => { Change(() => f.stripSlots.RemoveAt(si)); RebuildSlots(); }));
                    listHost.Add(row);
                }
            }
            RebuildSlots();
            slots.Add(Z.Button("+ Add slot", "Add another band to the strip.", () =>
            {
                Change(() =>
                {
                    if (f.stripSlots == null) f.stripSlots = new List<ShaperStripSlot>();
                    f.stripSlots.Add(new ShaperStripSlot());
                });
                RebuildSlots();
            }));
            box.Add(slots);
        }

        void BuildHeightFieldFill(VisualElement box, ShaperFillDef f)
        {
            box.Add(Z.Field("Height field", "The imported height field this fill reads.",
                Z.Object<Texture2D>(f.heightField, "The imported height field this fill reads.",
                    t => Change(() => f.heightField = t), 200f)));
            box.Add(Z.HGroup(
                Val("Scale", "Multiplies the sampled height.", f.heightFieldScale, 0f, 8f),
                Z.Field("Tint", "Multiplies the resulting colour.",
                    Z.Color(f.heightFieldTint, "Multiplies the resulting colour.",
                        c => Change(() => f.heightFieldTint = c), 90f))));
        }

        void BuildSteelFill(VisualElement box, ShaperFillDef f)
        {
            box.Add(Z.HGroup(
                Val("Cells", "How many noise cells across the surface — the grain size.", f.steelCells, 1f, 64f),
                Val("Octaves", "How many layers of noise are summed.", f.steelOctaves, 1f, 8f, decimals: 0),
                Val("Seed", "Varies the pattern without changing its character.", f.steelSeed, 0f, 9999f, decimals: 0),
                Val("Grain", "How strongly the grain shows.", f.steelGrain, 0f, 1f)));
            box.Add(Z.HGroup(
                Val("Rust amount", "How much rust covers the surface.", f.steelRustAmount, 0f, 1f),
                Val("Rust reach", "How far rust creeps from its seeds, in pixels.",
                    f.steelRustReachPixels, 0f, 32f)));
            box.Add(Z.HGroup(
                Z.Field("Base low", "The darker of the two base metal tones.",
                    Z.Color(f.steelBaseLow, "The darker of the two base metal tones.",
                        c => Change(() => f.steelBaseLow = c), 90f)),
                Z.Field("Base high", "The lighter of the two base metal tones.",
                    Z.Color(f.steelBaseHigh, "The lighter of the two base metal tones.",
                        c => Change(() => f.steelBaseHigh = c), 90f)),
                Z.Field("Rust", "The rust colour.",
                    Z.Color(f.steelRustColor, "The rust colour.",
                        c => Change(() => f.steelRustColor = c), 90f))));
        }

        void BuildOverPhaseFill(VisualElement box, ShaperFillDef f)
        {
            box.Add(Z.Field("Ramp", "The whole shape paints this ramp's colour at the node's own phase — a "
                + "flash of red at phase 0 sliding to blue at phase 1, for instance, never a spatial pattern.",
                Gradient(f.overPhaseGradient, "The ramp this fill's flat colour is drawn from, over the node's phase.")));
            box.Add(Z.Field("Tint", "The flat colour painted while no ramp is authored.",
                Z.Color(f.overPhaseTint, "The flat colour painted while no ramp is authored.",
                    c => Change(() => f.overPhaseTint = c), 90f)));
        }

        void BuildProceduralFill(VisualElement box, ShaperFillDef f)
        {
            box.Add(Z.Field("Pattern", "Which procedural pattern this fill draws.",
                Z.MiniRadio((int)f.proceduralKind, Enum.GetNames(typeof(ShaperProceduralKind)),
                    "Noise paints a ramp through hashed noise; Grid and Dots ink a repeating line/disc pattern "
                    + "and leave the rest transparent.",
                    v => { Change(() => f.proceduralKind = (ShaperProceduralKind)v); Rebuild(); })));

            box.Add(Z.HGroup(
                Val("Scale", "How far the pattern spreads before it repeats.", f.proceduralScale, 0.05f, 16f),
                Val("Offset U", "Slides the pattern horizontally — animate this to make it drift.",
                    f.proceduralOffsetU, -8f, 8f),
                Val("Offset V", "Slides the pattern vertically — animate this to make it drift.",
                    f.proceduralOffsetV, -8f, 8f),
                Val("Angle", "Rotates the pattern, in degrees.",
                    f.proceduralAngleDegrees, 0f, 360f, cyclic: true, decimals: 0)));

            switch (f.proceduralKind)
            {
                case ShaperProceduralKind.Noise:
                    box.Add(Z.Field("Noise shape", "How the raw noise value is reshaped before it drives the ramp.",
                        Z.Segmented((int)f.noiseKind, Enum.GetNames(typeof(ShaperNoiseKind)),
                            "Value is plain noise; Ridged creases it into ridges; Steps posterises it into four bands.",
                            v => Change(() => f.noiseKind = (ShaperNoiseKind)v))));
                    box.Add(Z.Field("Ramp", "The colour ramp the noise value is mapped through.",
                        Gradient(f.proceduralGradient, "The colour ramp the noise value is mapped through.")));
                    box.Add(Z.Field("Tint", "The flat colour painted while no ramp is authored.",
                        Z.Color(f.proceduralTint, "The flat colour painted while no ramp is authored.",
                            c => Change(() => f.proceduralTint = c), 90f)));
                    break;

                case ShaperProceduralKind.Grid:
                    box.Add(Z.HGroup(
                        Val("Line width", "Line thickness, as a fraction of one cell.", f.gridLineWidth, 0f, 1f),
                        Z.Toggle("Vertical", "Draw the vertical lines.", f.gridVertical,
                            v => Change(() => f.gridVertical = v)),
                        Z.Toggle("Horizontal", "Draw the horizontal lines.", f.gridHorizontal,
                            v => Change(() => f.gridHorizontal = v)),
                        Z.Field("Ink", "The line colour; everywhere else stays fully transparent (veiled to zero).",
                            Z.Color(f.proceduralTint, "The line colour.",
                                c => Change(() => f.proceduralTint = c), 90f))));
                    break;

                case ShaperProceduralKind.Dots:
                    box.Add(Z.HGroup(
                        Val("Dot size", "Disc diameter, as a fraction of one cell.", f.dotSize, 0f, 1f),
                        Z.Toggle("Stagger", "Offset alternating rows by half a cell.", f.dotStagger,
                            v => Change(() => f.dotStagger = v)),
                        Z.Field("Ink", "The dot colour; everywhere else stays fully transparent (veiled to zero).",
                            Z.Color(f.proceduralTint, "The dot colour.",
                                c => Change(() => f.proceduralTint = c), 90f))));
                    break;
            }
        }

        /// T-0172 — the ramp-preset picker. Overwrites <paramref name="f"/>'s rampGradient with a Pyre preset,
        /// converted through <see cref="PyreShaperRampPresets.ToZuiGradient"/> so the applied gradient is a
        /// fresh, detached copy — never a live reference back into Pyre.
        void ShowRampPresetMenu(VisualElement anchor, ShaperFillDef f)
        {
            var menu = Z.Menu(anchor).Width(280f).Search("Search ramps…");
            foreach (var preset in PyreShaperRampPresets.All)
            {
                var captured = preset;
                menu.Item(captured.name, "Overwrite the ramp with this Pyre preset.", () =>
                {
                    var converted = PyreShaperRampPresets.ToZuiGradient(captured.factory());
                    if (converted == null) return;
                    Change(() => f.rampGradient = converted);
                    Rebuild();
                });
            }
            menu.Show();
        }

        /// A ZuiGradient control wired for Undo. Z.Gradient's own callback fires AFTER the edit, so the
        /// pre-edit snapshot has to come from OnBeforeMutate — the same reason Change() records before it
        /// mutates.
        VisualElement Gradient(ZuiGradient g, string tooltip)
        {
            var ctrl = Z.Gradient(g, tooltip, () =>
            {
                if (document != null) EditorUtility.SetDirty(document);
                RefreshPreview();
            });
            ctrl.OnBeforeMutate = () =>
            {
                if (document != null) Undo.RegisterCompleteObjectUndo(document, "Edit Shaper Document");
            };
            return ctrl;
        }

        // ── Border ───────────────────────────────────────────────────────────────────────────────────────

        // ShaperNode.border (:161) is nullable like fill, so the card offers add/remove. Its `enabled` flag is
        // separate from existing at all, and both are meaningful: a border that exists but is off keeps its
        // authored width and fill for when it is switched back on.
        void BuildBorderSection(VisualElement root, ShaperNode node)
        {
            var box = borderSection = Z.Section("Border", "A derived strip around this node's edge, with its "
                + "own fill.", "shaper.window.border", icon: "square");

            if (node.border == null)
            {
                box.Add(Z.Field("Border", "This node has no border.",
                    Z.Button("Add border", "Give this node a border strip.",
                        () => { Change(() => node.border = new ShaperBorderDef { fill = SeededBorderFill(node) }); Rebuild(); })));
                root.Add(box);
                return;
            }

            var b = node.border;
            box.SetHeaderToggle(b.enabled, "Draw this border.", v => Change(() => b.enabled = v));

            box.Add(Z.HGroup(
                Val("Width", "How thick the border strip is, in canvas pixels.", b.width, 0f, 32f),
                Z.Field("Alignment", "Which side of the edge the strip sits on.",
                    Z.Segmented((int)b.alignment, Enum.GetNames(typeof(ShaperShellAlignment)),
                        "Centred straddles the edge; Inward grows into the shape; Outward grows out of it.",
                        v => Change(() => b.alignment = (ShaperShellAlignment)v))),
                Z.Toggle("Joins coverage",
                    "Whether the border adds itself to the shape's coverage, or only paints over it.",
                    b.joinsCoverage, v => Change(() => b.joinsCoverage = v))));

            // The border carries a full ShaperFillDef of its own, so it gets the same editor rather than a
            // reduced copy that would drift.
            if (b.fill == null)
            {
                box.Add(Z.Field("Border fill", "The border has no fill of its own yet.",
                    Z.Button("Add border fill", "Give the border its own fill.",
                        () => { Change(() => b.fill = SeededBorderFill(node)); Rebuild(); })));
            }
            else
            {
                var fillBox = Z.BoxKeyed("Border fill", "How the border strip itself is coloured.",
                    "shaper.window.border.fill");
                BuildFillBody(fillBox, b.fill, "shaper.window.border.fill");
                box.Add(fillBox);
            }

            box.Add(Z.Button("Remove border", "Remove this node's border entirely.",
                () => { Change(() => node.border = null); Rebuild(); }));
            root.Add(box);
        }

        /// <summary>
        /// W6.3 — a border added to a node whose OWN fill is currently fading (T-0190's seeded envelope,
        /// <see cref="SeededVeil"/>) must fade WITH it. Before this fix a fresh document's seeded root-fill
        /// fade left an opaque, unfaded grey outline behind it as the fill vanished — exactly the picture the
        /// owner's screenshot showed at frame 14/16: a grey silhouette with only a sliver of the (authored,
        /// dark-red-tinted) fill still visible where the fade hadn't yet fully bottomed out.
        ///
        /// <b>Why this, and not a layer-level alpha veil.</b> A genuine layer-level veil — one dial the
        /// compositor applies uniformly to every contributor (fill, border, and any future per-node kind) —
        /// is the more architecturally correct destination, but it needs new surface in
        /// <see cref="ShaperFillResolver"/>'s / <see cref="ShaperDocumentRenderer"/>'s compositing path,
        /// which T-0194 is actively editing this same wave (PROGRAMME_RULES.md's "one driver at a time"
        /// makes that path off-limits here). Re-seeding the border's OWN veil costs nothing there: a border
        /// fill is already <see cref="ShaperFillDef"/>, already carries its own <c>veil</c> dial, and this
        /// window is already the ONE place seeding happens (<see cref="NewLayer"/>'s "seeded here, and only
        /// here"). A border added later, well after the layer's own fade was seeded, is exactly the "any
        /// other route" case that stays untouched by design — so the match is made at the moment a border's
        /// fill is CREATED, not read live off the node's current fill.
        ///
        /// A fresh independent <see cref="SeededVeil"/> rather than a shared reference or a deep clone of
        /// the node's own curve: both envelopes hit the same normalised life fractions (0 / 0.15 / 0.7 / 1),
        /// so fill and border move together, while staying two authored curves an author can later detune
        /// independently without one edit silently dragging the other. Only fires when the node's own fill
        /// is actually animated (Curve mode) — a node with a Static veil gets a Static border, unchanged from
        /// before this fix.
        /// </summary>
        internal static ShaperFillDef SeededBorderFill(ShaperNode node)
        {
            var fill = ShaperFillDef.DefaultRootFill();
            if (node?.fill != null && node.fill.veil != null && node.fill.veil.mode == ZUIValue.Mode.Curve)
                fill.veil = SeededVeil();
            return fill;
        }

        // ── Combine / Sweep / Shell — part of the SHAPE, not modifiers of it (T-0182) ────────────────────

        // These used to sit in a card called "Modifiers", which they never were: a modifier is an effect
        // applied to the finished picture, while combine/blend decide how this node folds into its parent
        // and sweep/shell carve the node's own geometry. They now live in the Shape card beside the picker
        // that produced that geometry.
        //
        // Every dial here is a ZUIValue on the engine side (ShaperNode.cs), so all Val — right-click one to
        // author a Curve and the join, the slice or the wall thickness moves over the document's frames.
        internal void BuildShapeOpsBody(VisualElement box, ShaperNode node)
        {
            node.blend.EnsureDials();
            node.sweep.EnsureDials();
            node.shell.EnsureDials();

            // Absence rule: a node with no siblings has nothing to combine WITH, so the combine op and the
            // join dials are drawn only for a bag member (drillPath non-empty = editing a member).
            if (drillPath.Count > 0)
            {
                var combine = Z.BoxKeyed("Combine",
                    "How this member folds into the bag it belongs to.", "shaper.window.combine");
                combine.Add(Z.HGroup(
                    Z.Field("Combine", "How this node combines with what is already there.",
                        Z.Segmented((int)node.mode, Enum.GetNames(typeof(ShaperCombineMode)),
                            "Add unions; Subtract carves; Intersect keeps only the overlap.",
                            v => Change(() => node.mode = (ShaperCombineMode)v))),
                    Val("Blend width", "How far the join between this node and its neighbours is softened, "
                        + "in canvas pixels. 0 is a hard edge.",
                        node.blend.widthDial, 0f, 32f),
                    Val("Sharpness", "How abruptly the softened join falls off.",
                        node.blend.sharpnessDial, 0f, 1f),
                    Val("Carve strength", "How strongly a Subtract carves. 1 removes fully.",
                        node.blend.carveStrengthDial, 0f, 1f)));
                box.Add(combine);
            }

            var sweep = Z.BoxKeyed("Sweep", "Keep only an angular or fractional slice of the shape.",
                "shaper.window.sweep");
            sweep.Add(Z.HGroup(
                Z.Toggle("Enabled", "Apply the sweep.", node.sweep.enabled,
                    v => Change(() => node.sweep.enabled = v)),
                Val("Start", "Where the kept slice begins, in degrees.",
                    node.sweep.startDegreesDial, 0f, 360f, cyclic: true, decimals: 0),
                Val("Extent", "How much of the shape is kept, in degrees. Animate it to wipe the shape on or "
                    + "off over the document's frames.",
                    node.sweep.extentDegreesDial, 0f, 360f, decimals: 0),
                Val("Start ƒ", "Where the kept slice begins as a fraction of the shape.",
                    node.sweep.startFractionDial, 0f, 1f),
                Val("Extent ƒ", "How much is kept as a fraction of the shape.",
                    node.sweep.extentFractionDial, 0f, 1f)));
            box.Add(sweep);

            var shell = Z.BoxKeyed("Shell", "Hollow the shape into a shell of a given thickness.",
                "shaper.window.shell");
            shell.Add(Z.HGroup(
                Z.Toggle("Enabled", "Hollow this shape.", node.shell.enabled,
                    v => Change(() => node.shell.enabled = v)),
                Val("Thickness", "How thick the remaining shell is, in canvas pixels.",
                    node.shell.thicknessDial, 0f, 32f),
                Z.Field("Alignment", "Which side of the surface the shell is taken from.",
                    Z.Segmented((int)node.shell.alignment, Enum.GetNames(typeof(ShaperShellAlignment)),
                        "Centred straddles the surface; Inward keeps material inside it; Outward outside.",
                        v => Change(() => node.shell.alignment = (ShaperShellAlignment)v)))));
            box.Add(shell);
        }

        // ── Swarm ────────────────────────────────────────────────────────────────────────────────────────

        void BuildSwarmSection(VisualElement root, ShaperNode node)
        {
            var s = node.swarm;
            s.EnsureDials();
            var box = swarmSection = Z.Section("Swarm",
                "Repeat this node's own content many times with per-instance jitter.",
                "shaper.window.swarm", icon: "copy");
            box.SetHeaderToggle(s.enabled, "Repeat this node as a swarm.",
                v => { Change(() => s.enabled = v); Rebuild(); });

            if (!s.enabled) { root.Add(box); return; }

            // The hard cap is a real engine rule (ShaperSwarmDef.cs:163) and the reason a large count can
            // silently do less than it says. It goes in the CONTROL'S OWN TOOLTIP, composed for the current
            // value, rather than an on-screen note: explanation is tooltip content, and a conditional
            // tooltip has to read correctly for the state it is actually in.
            string countTip = s.count > ShaperSwarmDef.SimulationHardCap
                ? $"How many instances. A stateful-simulation source is clamped to "
                  + $"{ShaperSwarmDef.SimulationHardCap}, so {s.count} runs in full only for sources that "
                  + "are not a stateful simulation, or that provide a native swarm path."
                : "How many instances. 1 is a legal identity — one instance, itself.";

            box.Add(Z.HGroup(
                Dial("Count", countTip,
                    s.count, 1f, 64f, v => { s.count = Mathf.RoundToInt(v); Rebuild(); }, decimals: 0),
                // uint seed, same clamp reasoning the shell used for the document seed: an int control cannot
                // express the top half of a uint, and no workflow needs it.
                Z.Field("Seed", "Varies the jitter without changing its character. Clamped to a positive "
                    + "int — the engine's field is a uint, whose upper half no int control can express.",
                    Z.Int((int)Math.Min(s.seed, int.MaxValue), "Swarm seed.",
                        v => Change(() => s.seed = (uint)Mathf.Max(0, v)), 90f)),
                Val("Rotation jitter", "How much each instance's rotation varies, in degrees.",
                    s.rotationJitterDegreesDial, 0f, 180f, decimals: 0),
                Val("Scale jitter", "How much each instance's scale varies.",
                    s.scaleJitterDial, 0f, 1f)));

            // A spatial pair is one 2D control, never two packed 1D fields — and each axis is its own dial, so
            // animating the pair makes the cloud spread out or draw in without re-rolling which instance is where.
            box.Add(Val2D("Position jitter",
                "How far each instance can be displaced, in canvas pixels. Animate it to make the swarm "
                + "spread out or gather over the document's frames.",
                s.positionJitterX, s.positionJitterY,
                // T-0186 — dropped the 110px WithPlotSize override; matches Pyre's default 140px plot.
                new ZuiValue2DControl.Options().WithRange(0f, 128f, 0f, 128f)
                    .WithDefault(new Vector2(24f, 24f)).WithPrefKey("shaper.swarm.positionJitter")));

            BuildSwarmShapeBox(box, s);
            BuildSwarmTimingBox(box, s);

            // ShaperBlend carries THREE dials and the swarm's merge had a control for only two, which left the
            // third editable from the Inspector and invisible here. It is drawn now — but disabled, with the
            // reason as its tooltip, because it genuinely does nothing on a swarm: the swarm unions its
            // instances with a hard-coded ShaperCombineMode.Add, and carve strength is read only under
            // Subtract, by the field (ShaperOps.cs:70-79) and by the Lipschitz bound (ShaperBound.cs:84-87)
            // alike. Presenting it as live would be the lie; hiding it would leave the author wondering where
            // the node blend's third dial went. Same "declare inert with a reason, never hide" posture
            // ShaperSolids.InertReason takes for a dial that does nothing on a given solid form.
            var mergeCarve = Val("Merge carve", "How strongly a Subtract carves.",
                s.merge.carveStrengthDial, 0f, 1f);
            mergeCarve.SetEnabled(false);
            mergeCarve.tooltip = "Does nothing on a swarm: instances are unioned, and carve strength is read "
                + "only where a node subtracts. The merge keeps the value so a blend that does carve still "
                + "has it.";

            box.Add(Z.HGroup(
                Z.Toggle("Interact", "Let instances affect one another rather than being independent.",
                    s.interact, v => Change(() => s.interact = v)),
                Val("Merge width", "How far neighbouring instances blend into each other.",
                    s.merge.widthDial, 0f, 32f),
                Val("Merge sharpness", "How abruptly that blend falls off.",
                    s.merge.sharpnessDial, 0f, 1f),
                mergeCarve));

            root.Add(box);
        }

        // T-0169 — WHERE the instances land. Every control here is inert while the shape is None, so the box
        // shows only the shape picker until a shape is chosen: an authored figure is what gives radius,
        // distribution, progress and orient anything to mean.
        void BuildSwarmShapeBox(VisualElement parent, ShaperSwarmDef s)
        {
            var box = Z.BoxKeyed("Spawn shape", "The figure the instances arrange themselves on, instead of "
                + "sitting on the node's own position.", "shaper.window.swarm.shape");

            box.Add(Z.Field("Shape", "The figure the instances arrange themselves on.",
                Z.MiniRadio((int)s.shape, Enum.GetNames(typeof(ShaperSwarmShape)),
                    "None leaves every instance on the node's own position, moved only by the jitter above.",
                    v => { Change(() => s.shape = (ShaperSwarmShape)v); Rebuild(); }, wrap: true)));

            if (s.shape == ShaperSwarmShape.None) { parent.Add(box); return; }

            bool line = s.shape == ShaperSwarmShape.Line;
            bool path = !line && s.spawnMode == ShaperSwarmSpawnMode.Path;

            // Built as one argument list rather than by Add-ing afterwards: ZuiHGroup spaces its children in
            // its constructor, so a child added later would sit flush against its neighbour.
            box.Add(Z.HGroup(
                line ? null : Z.Field("Spawn", "Whether instances fill the figure or ride its outline.",
                    Z.Segmented((int)s.spawnMode, Enum.GetNames(typeof(ShaperSwarmSpawnMode)),
                        "Area fills the interior; Path places them along the edge.",
                        v => { Change(() => s.spawnMode = (ShaperSwarmSpawnMode)v); Rebuild(); })),
                Val(line ? "Half length" : "Radius",
                    line ? "Half the length of the row, in canvas pixels."
                         : "How big the figure is, in canvas pixels. Animate it to make the arrangement grow "
                           + "or close in over the document's frames.",
                    s.spawnerRadius, 0f, 256f, decimals: 0),
                Val("Turn", "Turns the whole arrangement in the canvas plane, in degrees.",
                    s.spawnerRotationDegrees, 0f, 360f, cyclic: true, decimals: 0),
                Val("Tilt", "Leans the arrangement away from the viewer about the horizontal axis, in "
                    + "degrees. At 90° it collapses to a line, so a ring reads as seen edge-on.",
                    s.spawnerPitchDegrees, -90f, 90f, decimals: 0),
                Val("Yaw", "Leans the arrangement about the vertical axis, in degrees.",
                    s.spawnerYawDegrees, -90f, 90f, decimals: 0)));

            box.Add(Val2D("Centre offset",
                "Where the figure's centre sits, relative to the node's own position, in canvas pixels.",
                s.spawnerOffsetX, s.spawnerOffsetY,
                // T-0186 — dropped the 110px WithPlotSize override; matches Pyre's default 140px plot.
                new ZuiValue2DControl.Options().WithRange(-128f, 128f, -128f, 128f)
                    .WithDefault(Vector2.zero).WithPrefKey("shaper.swarm.spawnerOffset")));

            if (!line && !path)
            {
                box.Add(Z.HGroup(
                    Dial("Distribution", "0 spaces the instances evenly through the figure; 1 scatters them "
                        + "at random inside it.", s.distribution, 0f, 1f, v => s.distribution = v),
                    Z.Toggle("Fill from the edge", "Build the even layout from the outermost ring inward "
                        + "instead of from the centre out.", s.gridReverse,
                        v => Change(() => s.gridReverse = v))));
            }
            else if (path)
            {
                box.Add(Z.HGroup(
                    Val("Progress", "Where along the outline the arrangement sits, as a fraction of one lap. "
                        + "Animate it past 1 to ride the instances around further laps.",
                        s.pathProgress, 0f, 2f),
                    Z.Toggle("Even spacing", "Spread the instances evenly along the outline, so Progress "
                        + "moves the whole string rather than each instance's own place on it.",
                        s.evenSpacing, v => { Change(() => s.evenSpacing = v); Rebuild(); }),
                    s.evenSpacing
                        ? Dial("Spread", "How much of the outline the evenly-spaced string covers. 1 is the "
                            + "whole shape; 0.5 is a half-shape arc.", s.pathSpread, 0f, 1f,
                            v => s.pathSpread = v)
                        : null));
            }

            box.Add(Z.HGroup(
                Z.Field("Face", "Which way each instance is turned once it is placed.",
                    Z.MiniRadio((int)s.orient, Enum.GetNames(typeof(ShaperSwarmOrient)),
                        path ? "Outward turns each instance away from the centre; PathTangent turns it along "
                               + "the outline it sits on."
                             : "Outward turns each instance away from the centre. PathTangent needs a Path "
                               + "spawn, and falls back to Outward here.",
                        v => Change(() => s.orient = (ShaperSwarmOrient)v))),
                Val("Size by index", "A size multiplier read across the instances rather than over time — "
                    + "author it as a curve to taper the swarm from one end to the other, or as Min-Max to "
                    + "give each instance its own size.", s.scaleByIndex, 0f, 2f)));

            parent.Add(box);
        }

        // T-0169 — WHEN the instances exist. Stagger is the model the swarm shipped with (every instance alive
        // the whole time, its own clock offset); the other two give each instance a real birth and death, so
        // the swarm builds up and clears instead of merely being present.
        void BuildSwarmTimingBox(VisualElement parent, ShaperSwarmDef s)
        {
            var box = Z.BoxKeyed("Spawn timing", "When each instance appears and how long it lasts.",
                "shaper.window.swarm.timing");

            // Collected first and constructed once: ZuiHGroup spaces its children in its constructor, so a
            // child added afterwards would sit flush against its neighbour.
            var kids = new List<VisualElement>
            {
                Z.Field("Timing", "How the instances are distributed over the document's frames.",
                    Z.MiniRadio((int)s.timing, Enum.GetNames(typeof(ShaperSwarmTiming)),
                        "Stagger keeps every instance alive throughout; Window and FrameStep give each one a "
                        + "birth and a death, outside which it is not drawn at all.",
                        v => { Change(() => s.timing = (ShaperSwarmTiming)v); Rebuild(); }))
            };

            if (s.timing == ShaperSwarmTiming.Stagger)
            {
                // The stagger decides how the per-instance clocks are DRAWN, so animating it over those same
                // clocks would be circular — it stays a plain dial deliberately.
                kids.Add(Dial("Lifetime stagger", "How much each instance's clock is offset from the others.",
                    s.lifetimeStagger, 0f, 1f, v => s.lifetimeStagger = v));
                box.Add(Z.HGroup(kids.ToArray()));
                parent.Add(box);
                return;
            }

            if (s.timing == ShaperSwarmTiming.Window)
            {
                kids.Add(Val("Births", "Maps an instance's number — 0 for the first, 1 for the last — to the "
                    + "moment it appears. A rising curve spreads the births out; a flat value brings the "
                    + "whole swarm in at once.", s.spawnTiming, 0f, 1f));
            }
            else
            {
                // Authored in FRAMES, stored in phase. ShaperClock is the one home for that conversion, so the
                // control converts through it in both directions rather than dividing by frameCount here.
                int frames = Mathf.Max(1, document != null ? document.frameCount : 1);
                int last = Mathf.Max(1, frames - 1);
                kids.Add(Dial("First frame", "Which frame the first instance appears on.",
                    Mathf.Round(s.firstSpawnPhase * last), 0f, last,
                    v => s.firstSpawnPhase = ShaperClock.PhaseOfFrame(Mathf.RoundToInt(v), frames), decimals: 0));
                kids.Add(Dial("Frame step", "How many frames pass between one instance appearing and the next. "
                    + "0 brings the whole swarm in on the first frame.",
                    Mathf.Round(s.spawnPhaseStep * last), 0f, last,
                    v => s.spawnPhaseStep = ShaperClock.PhaseOfFrame(Mathf.RoundToInt(v), frames), decimals: 0));
            }

            kids.Add(Dial("Lifetime", "How much of the document each instance lasts, measured from its own "
                + "birth.", s.instanceLife, 0.01f, 1f, v => s.instanceLife = v));
            kids.Add(Z.Toggle("Die together", "Clear the whole swarm at one moment rather than letting each "
                + "instance expire on its own schedule.", s.dieTogether,
                v => Change(() => s.dieTogether = v)));
            kids.Add(Dial("Appearance order", "0 brings in neighbouring positions one after another; 1 "
                + "reveals them in a scrambled order.", s.spawnOrderChaos, 0f, 1f,
                v => s.spawnOrderChaos = v));

            box.Add(Z.HGroup(kids.ToArray()));
            parent.Add(box);
        }

        // ── Composite generator ──────────────────────────────────────────────────────────────────────────

        // The structural one. ShaperCompositeDef.source is [SerializeReference] IShaperCompositeSource holding
        // a PyreForm (ShaperCompositeDef.cs:95, PyreFormCompositeSource.cs:31-33) — an OBJECT, not an index.
        // The mock's `int compositeGeneratorIndex` does not transfer at all: picking a generator here means
        // INSTANTIATING a form and assigning it, and the per-generator dials are whatever that form declares.
        // Those declarations are enormous (ArcBurstForm alone declares 187 authored fields), so they are
        // reflected, never hand-listed — a hand-written list could not be kept correct and would silently
        // expose a fraction of the engine.
        /// The generator's dials, drawn inside the Shape card under the picker that chose it (T-0182).
        /// WHICH generator is no longer asked here — it is one of the picker's columns, because a generator
        /// is one more answer to "what does this node draw", not a separate kind of question.
        internal void BuildCompositeBody(VisualElement box, ShaperNode node)
        {
            var c = node.composite;

            // What the dials are read off. A hosted Pyre form declares them on the FORM, so the wrapper's own two
            // fields must not be what gets reflected; a source that is not a form (a stateful simulation) declares
            // them on itself. Resolving it to one object here is what lets the whole card below stay generic —
            // the card never learns which kind it is showing.
            var src = c.source as PyreFormCompositeSource;
            object dialOwner = src != null ? (object)src.form : c.source;

            // A Composite node with no source assigned is a state a document can genuinely hold. The picker
            // above is the way out of it, so this says so rather than offering a second, competing chooser.
            if (dialOwner == null)
            {
                box.Add(Z.Text("No generator picked yet — choose one from the Shape picker above.",
                    ZuiText.Subtle,
                    "This node is set to host a generator but none is assigned, so it draws nothing."));
                return;
            }

            // The reasonNote sentence behind Reason's one-word classification is written by
            // PyreCompositeCatalog (PyreCompositeCatalog.cs:168) and read by the audit
            // (PyreShaperCompositeAudit.cs:276) — it is READ-ONLY here on purpose: SHAPER_THE_DESIGN §6.2's
            // "monolithic must be a declared reason" makes the declaration the GENERATOR's to own, so a text
            // field on the node would let one document quietly disagree with the catalog every other document
            // reads. `HasDeclaration` is the engine's own non-blank test (ShaperCompositeDef.cs:134).
            //
            // T-0192 (PM addendum) — this sentence used to be its own on-screen "Note" paragraph body text
            // below the row. The labeling rule is that an explanation lives in a tooltip, never as body text
            // (ui-layout-rules), so the sentence now lives on the Reason chip's hover instead and there is no
            // second row for it — a short chip, not a paragraph.
            string reasonTooltip = "Why this is still a composite rather than split into primitives — a "
                + "structural fact the audit checks, not an authored dial."
                + (c.HasDeclaration ? "  ·  " + c.reasonNote : "");
            // T-0191 — the Half extent X/Y and Bake W/H dials are GONE, on the owner's report that on
            // Pyre › Disc they "scale the disc and make no sense to a human next to Pyre's Size". They were a
            // second size authority sitting beside the generator's own, and an author turning one had no way
            // to tell which of the two he was turning. The box is now fitted to the canvas by the renderer
            // (ShaperCompositeDef.FitTo), so there is nothing left here to author — only the declaration.
            box.Add(Z.HGroup(
                Z.Field("Reason", reasonTooltip,
                    Z.Text(c.reason.ToString(), ZuiText.Body, reasonTooltip))));

            // A generator family with a DESIGNED card draws it instead of the reflected dump below (T-0183).
            // The window does not know which families those are — it asks the registry, so a hosted Pyre layer
            // showing Pyre's own Solid / Light / Lines / Glow boxes needs no branch here. See
            // ShaperCompositeSourceUI.cs for why this is a registry and not an `is` test.
            var sourceUI = ShaperCompositeSourceUICatalog.For(c.source);
            if (sourceUI != null)
            {
                sourceUI.Build(new ShaperSourceUIContext
                {
                    Body = box,
                    UndoTarget = document,
                    FrameCount = document != null ? document.frameCount : 0,
                    CanvasExtent = document != null
                        ? Mathf.Max(document.canvasWidth, document.canvasHeight) : 128f,
                    Change = Change,
                    Touch = () =>
                    {
                        if (document != null) EditorUtility.SetDirty(document);
                        RefreshPreview();
                    },
                    Rebuild = Rebuild,
                }, c.source);
                return;
            }

            // The generator's own dials. Folded into a keyed box because the biggest forms declare well over
            // a hundred fields and an unfolded dump would bury every other card on the page. FlowFields packs
            // them into shared rows rather than one control per row, which is the same row-packing rule the
            // hand-built cards follow.
            var dials = Z.BoxKeyed(c.source.SourceLabel + " dials",
                "Every dial this generator declares, read straight off the generator itself — so it cannot drift "
                + "out of date as the generator changes. A dial set to a curve is read at each frame's own phase, "
                + "so animating one animates the picture.",
                "shaper.window.composite.dials");
            var host = new VisualElement();
            ZuiReflect.FlowFields(host, dialOwner, new ZuiReflect.Options
            {
                OnBeforeChange = () =>
                {
                    if (document != null) Undo.RegisterCompleteObjectUndo(document, "Edit Shaper Document");
                },
                OnChanged = () =>
                {
                    if (document != null) EditorUtility.SetDirty(document);
                    RefreshPreview();
                },
                OnStructureChanged = Rebuild,
                ControlWidth = 140f,
            });
            dials.Add(host);
            box.Add(dials);
        }

        // ── Bag children ─────────────────────────────────────────────────────────────────────────────────

        /// The bag's members, drawn inside the Shape card for a node whose picked shape is "Combine
        /// children" (T-0182). It was its own section; a bag's members ARE its shape, so they belong under
        /// the picker that said so rather than in a card that existed for one node kind out of four.
        internal void BuildChildrenBody(VisualElement outer, ShaperNode node)
        {
            var box = Z.BoxKeyed("Members", "The members this bag combines. Open one to author it.",
                "shaper.window.children");

            var listHost = new VisualElement();
            box.Add(listHost);

            void RebuildChildren()
            {
                listHost.Clear();
                if (node.children == null) node.children = new List<ShaperNode>();
                for (int i = 0; i < node.children.Count; i++)
                {
                    var child = node.children[i];
                    int ci = i;
                    var row = new VisualElement();
                    row.AddToClassList("zui-row");

                    var grip = Z.Text("≡", ZuiText.Body,
                        "Drag to reorder — order is the order members combine in.");
                    grip.style.width = 16f;
                    ZuiReorder.MakeGrip(grip, row, listHost, (from, to) =>
                    {
                        Change(() =>
                        {
                            var m = node.children[from];
                            node.children.RemoveAt(from);
                            node.children.Insert(to, m);
                        });
                        RebuildChildren();
                    });
                    row.Add(grip);
                    row.Add(Z.Toggle("", "Include this member.", child.enabled,
                        v => Change(() => child.enabled = v)));
                    row.Add(Z.TextInput(child.name ?? "", "This member's name.",
                        v => Change(() => child.name = v), 140f));
                    row.Add(Z.Text(child.mode.ToString(), ZuiText.Subtle,
                        "How this member combines: " + child.mode));
                    row.Add(Z.Flexible());
                    // Label = action: this opens the member for editing, which is what "Open" says it does.
                    row.Add(Z.Button("Open", "Author this member.",
                        () => { drillPath.Add(ci); Rebuild(); }));
                    row.Add(Z.Button("×", "Remove this member.",
                        () => { Change(() => node.children.RemoveAt(ci)); RebuildChildren(); }));
                    listHost.Add(row);
                }
            }
            RebuildChildren();

            box.Add(Z.Button("+ Add member", "Add another member to this bag.", () =>
            {
                Change(() =>
                {
                    if (node.children == null) node.children = new List<ShaperNode>();
                    node.children.Add(new ShaperNode { name = "Member " + (node.children.Count + 1) });
                });
                RebuildChildren();
            }));
            outer.Add(box);
        }

        // ── Light response (LAYER) ───────────────────────────────────────────────────────────────────────

        void BuildResponseSection(VisualElement root, ShaperLayer layer)
        {
            var r = layer.response;
            if (r == null) return;

            var box = responseSection = Z.Section("Lighting",
                "How this LAYER responds to the document's light rig.",
                "shaper.window.response", icon: "sun");

            // LR-4.5 declares ShaperLightRig.ShadowsNotComputed (ShaperLightRig.cs:194) as the sentence to show
            // on the Cast/Receive Shadows controls "whenever either is ticked", and the compiler raises it on
            // the program under exactly that condition (ShaperLightCompiler.cs:422). Neither control was
            // quoting it, so ticking one read as "this now casts a shadow" when nothing is computed. The
            // string is composed in per state rather than pinned on permanently, because a conditional tooltip
            // has to read true for the state it is actually in: with both off, no shadow is being claimed. The
            // toggles Rebuild so the tooltip recomposes the moment the state it describes changes.
            bool shadowClaimed = r.castShadows || r.receiveShadows;
            string shadowLimit = shadowClaimed ? " " + ShaperLightRig.ShadowsNotComputed : "";

            box.Add(Z.HGroup(
                Z.Toggle("Receive lighting", "Let the rig light this layer at all.",
                    r.receiveLighting, v => { Change(() => r.receiveLighting = v); Rebuild(); }),
                Z.Toggle("Cast shadows", "Let this layer cast shadows." + shadowLimit,
                    r.castShadows, v => { Change(() => r.castShadows = v); Rebuild(); }),
                Z.Toggle("Receive shadows", "Let this layer be shadowed." + shadowLimit,
                    r.receiveShadows, v => { Change(() => r.receiveShadows = v); Rebuild(); })));

            if (!r.receiveLighting) { root.Add(box); return; }

            box.Add(Z.HGroup(
                Val("Intensity ×", "Scales the rig's effect on this layer.", r.intensityScale, 0f, 3f),
                Val("Rim strength", "How strong the rim light is.", r.rimStrength, 0f, 2f),
                Val("Rim power", "How tightly the rim light hugs the silhouette.", r.rimPower, 0.5f, 8f),
                Val("Specular", "How strong the specular highlight is.", r.specular, 0f, 1f),
                Val("Spec power", "How tight the specular highlight is.", r.specularPower, 1f, 128f),
                Z.Field("Spec tint", "Tints the specular highlight.",
                    Z.Color(r.specularTint, "Tints the specular highlight.",
                        c => Change(() => r.specularTint = c), 90f))));

            box.Add(Z.Field("Normals", "Where this layer's surface directions come from.",
                Z.Segmented((int)r.normalKind, Enum.GetNames(typeof(ShaperNormalKind)),
                    "Constant uses one authored direction for the whole layer; Profile uses the "
                    + "extrusion/bevel profile's own analytic normal, which needs a height stage to produce "
                    + "any relief.",
                    v => { Change(() => r.normalKind = (ShaperNormalKind)v); Rebuild(); })));

            // INVENTORY GAP CLOSED — normalConstant (ShaperLightRig.cs:295) is the ONLY parameter of the
            // DEFAULT normal path and had no UI at all, so the default lighting mode was unauthorable. Drawn
            // only for Constant, since Profile ignores it. X/Y is a pad (a direction is a spatial value); Z is
            // its own dial because a pad cannot express three axes.
            if (r.normalKind == ShaperNormalKind.Constant)
            {
                box.Add(Z.HGroup(
                    Z.Field("Direction XY", "The surface direction this layer reports, X and Y.",
                        Z.Pad(new Vector2(r.normalConstant.x, r.normalConstant.y),
                            new Rect(-1f, -1f, 2f, 2f),
                            "The surface direction this layer reports, X and Y.",
                            v => Change(() =>
                                r.normalConstant = new Vector3(v.x, v.y, r.normalConstant.z)))),
                    Dial("Direction Z", "The surface direction's Z. 1 faces the viewer.",
                        r.normalConstant.z, -1f, 1f,
                        v => r.normalConstant = new Vector3(r.normalConstant.x, r.normalConstant.y, v))));
            }

            root.Add(box);
        }

        // ── Height / extrusion (LAYER) ───────────────────────────────────────────────────────────────────

        // ShaperLayer.height is NULLABLE, and null is not the same as Flat: a layer with no height stage has
        // no height pass at all. So this card adds/removes the stage rather than only offering dials.
        //
        // T-0187 — moved from a toggle-bar ZuiSection into a foldable Z.BoxKeyed card under the Layers
        // section's SELECTED-layer row (BuildLayersSection, ShaperWindow.cs), next to the Lifetime card:
        // "Height, Mask? ... perhaps part of the layer item in the layer list" (owner). Keyed by ordinal —
        // same fold-drift-on-reorder trade-off ShaperWindow.Lights.cs's per-light cards already accept, low
        // cost here since it only affects which card starts folded, never any authored value.
        void BuildHeightSection(VisualElement root, ShaperLayer layer)
        {
            string key = "shaper.window.height." + document.layers.IndexOf(layer);

            if (layer.height == null)
            {
                var empty = Z.BoxKeyed("Height", "Extrude this LAYER's silhouette into relief.", key, "mountains");
                if (s_layerCardDefaultedClosed.Add(key)) empty.IsOpen = false;
                empty.SetHeaderSuffix(() => ": none");
                empty.Add(Z.Field("Height", "This layer has no height stage, so it stays flat.",
                    Z.Button("Add height", "Give this layer an extrusion stage.",
                        () => { Change(() => layer.height = new ShaperHeightDef()); Rebuild(); })));
                root.Add(empty);
                return;
            }

            var h = layer.height;
            var box = Z.BoxKeyed("Height", "Extrude this LAYER's silhouette into relief.", key, "mountains");
            if (s_layerCardDefaultedClosed.Add(key)) box.IsOpen = false;
            box.SetHeaderSuffix(() => ": " + ObjectNames.NicifyVariableName(h.technique.ToString()));
            box.Add(Z.Field("Technique", "How the silhouette is raised.",
                Z.MiniRadio((int)h.technique, Enum.GetNames(typeof(ShaperExtrusionTechnique)),
                    "Flat leaves it unraised; the others differ in how the surface climbs from edge to centre.",
                    v => { Change(() => h.technique = (ShaperExtrusionTechnique)v); Rebuild(); }, wrap: true)));

            box.Add(Z.HGroup(
                Val("Depth", "How far the surface is raised, in canvas pixels.", h.depth, 0f, 64f),
                Val("Angle", "The wall angle of the extrusion, in degrees.", h.angle, 0f, 90f, decimals: 0),
                Val("Steps", "How many discrete steps a stepped technique uses.", h.steps, 1f, 32f, decimals: 0),
                Val("Curve", "How the climb is shaped between edge and centre.", h.curve, 0f, 4f),
                Val("Taper", "How much the surface narrows as it rises.", h.taper, 0f, 2f)));

            box.Add(Z.Field("Bevel", "An additional bevel applied at the edge.",
                Z.MiniRadio((int)h.bevel, Enum.GetNames(typeof(ShaperBevelTechnique)),
                    "None leaves a hard edge; the others differ in the bevel's profile.",
                    v => { Change(() => h.bevel = (ShaperBevelTechnique)v); Rebuild(); }, wrap: true)));

            if (h.bevel != ShaperBevelTechnique.None)
                box.Add(Z.HGroup(
                    Val("Bevel amount", "How far the bevel reaches in from the edge.", h.bevelAmount, 0f, 16f),
                    Val("Bevel steps", "How many discrete steps a stepped bevel uses.",
                        h.bevelSteps, 1f, 16f, decimals: 0)));

            box.Add(Z.Button("Remove height", "Drop this layer's height stage and leave it flat.",
                () => { Change(() => layer.height = null); Rebuild(); }));
            root.Add(box);
        }

        // ── Mask (LAYER, T-0170) ─────────────────────────────────────────────────────────────────────────
        //
        // The source is PICKED, never typed. The list is always derivable here because the owner is always
        // known — a layer's siblings are the document's other layers — which is the test the project's
        // "never type a reference string" rule sets for whether a picker is buildable at all. With no other
        // layer the control says so and offers nothing else, rather than degrading to a text field.

        // T-0187 — moved from a toggle-bar ZuiSection into a foldable Z.BoxKeyed card under the Layers
        // section's SELECTED-layer row, next to Height and the Lifetime card — see BuildHeightSection's
        // comment above for the reasoning and the ordinal-key trade-off.
        void BuildMaskSection(VisualElement root, ShaperLayer layer)
        {
            // A document serialized before this field existed deserializes with the field initializer, so a
            // null here is only reachable through hand-edited YAML — repaired silently rather than throwing,
            // and NOT through Change(), because restoring a default is not an authored edit.
            if (layer.mask == null) layer.mask = new ShaperLayerMask();
            var m = layer.mask;
            string key = "shaper.window.mask." + document.layers.IndexOf(layer);

            var source = document.LayerById(m.sourceLayerId);
            bool missing = m.IsSet && source == null;

            bool hasOther = false;
            for (int i = 0; i < document.layers.Count && !hasOther; i++)
                if (document.layers[i] != null && document.layers[i] != layer) hasOther = true;

            string pickLabel = missing ? "Missing layer"
                             : source != null ? (string.IsNullOrEmpty(source.name) ? "(unnamed layer)" : source.name)
                             : hasOther ? "None" : "No other layers";
            string pickTip = missing ? ShaperLayerMask.MissingSource
                           : hasOther ? "Which other layer of this document cuts this one. Pick “None” to "
                                        + "remove the mask."
                                      : "This document has no other layer to mask with. Add a second layer "
                                        + "first — a layer cannot mask itself.";

            var box = Z.BoxKeyed("Mask",
                "Cut this LAYER with another layer of the same document. The mask is held here, on the layer "
                + "being cut, so one shape can mask several layers and dragging the list never re-points it.",
                key, "stack");
            if (s_layerCardDefaultedClosed.Add(key)) box.IsOpen = false;
            box.SetHeaderSuffix(() => ": " + (m.IsSet ? pickLabel : "none"));

            Button pick = null;
            pick = Z.Button(pickLabel, pickTip, () =>
            {
                var menu = Z.Menu(pick).Width(240f);
                menu.Item("None", "Remove this layer's mask.",
                    () => { Change(() => m.sourceLayerId = 0); Rebuild(); }, @checked: !m.IsSet);
                for (int i = 0; i < document.layers.Count; i++)
                {
                    var cand = document.layers[i];
                    if (cand == null || cand == layer) continue;
                    var captured = cand;
                    string nm = string.IsNullOrEmpty(cand.name) ? "Layer " + (i + 1) : cand.name;
                    menu.Item(nm, "Cut this layer with “" + nm + "”. " + ShaperLayerMask.SourceIsReadUnmasked,
                        // IdOf allocates the source's stable id on first reference, so it happens INSIDE the
                        // Undo scope — a Ctrl+Z takes the id back with the reference that caused it.
                        () => { Change(() => m.sourceLayerId = document.IdOf(captured)); Rebuild(); },
                        @checked: captured.id != 0 && captured.id == m.sourceLayerId);
                }
                menu.Show();
            });
            if (!hasOther && !m.IsSet) pick.SetEnabled(false);

            var pickRow = Z.HGroup(Z.Field("Mask by", pickTip, pick));
            if (missing)
                pickRow.Add(Z.Button("Clear", "Drop the reference to the deleted layer.",
                    () => { Change(() => m.sourceLayerId = 0); Rebuild(); }));
            box.Add(pickRow);

            if (m.IsSet)
            {
                box.Add(Z.HGroup(
                    Z.Field("Mode", "Clip keeps what the source covers, Subtract cuts it away, and Intersect "
                        + "keeps the lesser of the two — which differ only where the mask is soft.",
                        Z.Segmented((int)m.mode, Enum.GetNames(typeof(ShaperMaskMode)),
                            "Clip keeps what the source covers, Subtract cuts it away, Intersect keeps the "
                            + "lesser of the two.",
                            v => { Change(() => m.mode = (ShaperMaskMode)v); Rebuild(); })),
                    Z.Toggle("Invert", "Read the source backwards, so it cuts where it is empty instead of "
                        + "where it is solid.", m.invert, v => Change(() => m.invert = v))));

                // Availability is the SOURCE's own answer: a layer with no height stage publishes only
                // Coverage and Edge Distance (HS-1.4), and the renderer falls back to Coverage rather than
                // reading an empty sheet and cutting the whole layer away. Declared with its reason rather
                // than hidden, the same posture ShaperSolids.InertReason takes for an inert dial.
                bool heightUnavailable = source != null && source.height == null;
                bool quantityUnavailable = heightUnavailable && m.quantity == ShaperMaskQuantity.Height;
                string qTip = "Which of the source's quantities is read as the mask. Coverage is its "
                    + "silhouette, Edge Distance ramps inward from its outline, Height needs it to be "
                    + "extruded, and Luma reads how bright it is."
                    + (heightUnavailable ? "\n\n" + ShaperLayerMask.QuantityNotPublished : "");

                var qRow = Z.HGroup(
                    Z.Field("Quantity", qTip,
                        Z.MiniRadio((int)m.quantity, Enum.GetNames(typeof(ShaperMaskQuantity)), qTip,
                            v => { Change(() => m.quantity = (ShaperMaskQuantity)v); Rebuild(); }, wrap: true)));

                // Coverage is already 0..1, so it has no scale to set and the dial is not drawn for it —
                // a second amplitude dial on a normalized quantity is the "two dials for one quantity"
                // defect ShaperLight.range refuses by name.
                if (m.quantity != ShaperMaskQuantity.Coverage)
                    qRow.Add(Val("Full at", "The source value that reads as a fully solid mask — canvas pixels "
                        + "for Height and Edge Distance, linear brightness for Luma. At 0 it becomes a hard "
                        + "test with no ramp.", m.fullAt, 0f, 64f));
                box.Add(qRow);

                if (quantityUnavailable)
                    box.Add(Z.Text("Falling back to Coverage.", ZuiText.Subtle,
                        ShaperLayerMask.QuantityNotPublished));
            }

            // Lives here rather than on the layer row because it is only ever set for a layer that is being
            // used as a mask, and this is the card that explains what that means.
            box.Add(Z.Toggle("Draws into the picture",
                "Turn this off to make THIS layer a pure mask: it still resolves, and other layers may still "
                + "be cut by it, but it never paints into the picture itself. Disabling the layer instead "
                + "turns it off as a mask source too.",
                layer.contributesToPicture, v => Change(() => layer.contributesToPicture = v)));

            root.Add(box);
        }

        // ── Effects, i.e. "SpriteFX" (T-0163, renamed + given a real add-menu T-0184) ────────────────────────
        //
        // Two lists, and an entry's STAGE is which list it is in — never a per-row dropdown, because the two
        // lists are applied at genuinely different points and nothing authored may disagree with where the
        // renderer actually runs them (ShaperDocumentRenderer.RenderPhaseInto for the layer half, RenderPhase
        // for the document half). Each row says its stage as a plain sentence rather than an enum name.
        //
        // Availability is the engine's own answer, not this file's: ShaperEffectRuntime.CanRun asks the
        // catalog what sheets an effect needs, then whether it may run at this stage, then whether the shared
        // SpriteFxStack kernel can dispatch its family at all. Anything that fails is shown GREYED with the
        // reason — the same "declare, don't hide" posture ShaperSolids.InertReason takes.
        //
        // T-0184 — owner: "Effects is what is called Modifiers in Pyre — that's the same thing as the SpriteFX
        // stack. So let's call them SpriteFX." The per-layer list is titled "SpriteFX" (Pyre's per-layer
        // "Modifiers"), the document-wide list "Global SpriteFX" (Pyre's spec-wide "Global Modifiers") — labels
        // and tooltips only; type names, C# member names (effectsSection/layerEffectsSection/BuildEffectsSection/
        // etc.) and view-state keys are unchanged. The add picker (ShowAddEffectMenu) was also rebuilt to match
        // Pyre's own "+ Add modifier" menu (PyreWindow.Modifiers.cs:344-378) content-for-content — a section
        // per family, an item per effect, unavailable entries greyed with their reason — but laid out as one
        // COLUMN per family instead of Pyre's flat vertical list, per the owner's separate standing feedback
        // this wave ("we have a lot of width but less height... at least 4 columns is totally acceptable").

        void BuildEffectsSection(VisualElement root, ShaperLayer layer)
        {
            // LAYER effects are a property of the layer, so — like Lighting and Height — they are only drawn
            // at the layer root. Drilled into a bag member there is no layer being edited to attach them to.
            //
            // T-0184 — owner: "Effects is what is called Modifiers in Pyre — that's the same thing as the
            // SpriteFX stack. So let's call them SpriteFX." Renamed labels/tooltips only (type names, view
            // keys and the effectsSection/layerEffectsSection field names are unchanged): per-layer → "SpriteFX"
            // (mirrors Pyre's per-layer "Modifiers"), document-wide → "Global SpriteFX" (mirrors Pyre's
            // spec-wide "Global Modifiers").
            layerEffectsSection = null;   // so a stale section from the layer root never lingers in the bar
            if (drillPath.Count == 0)
                BuildEffectListSection(root, ref layerEffectsSection, "SpriteFX",
                    "Effects applied to THIS layer's own picture before it composites into the document.",
                    "shaper.window.layereffects", layer.effects, ShaperEffectStage.PreComposite);

            BuildEffectListSection(root, ref effectsSection, "Global SpriteFX",
                "Effects applied to the document's finished picture, after every layer has composited.",
                "shaper.window.effects", document.effects, ShaperEffectStage.PostComposite);
        }

        /// <summary>One effect list as a section: the cards, the enabled count on the collapsed header, and the
        /// add picker. Both stages share it verbatim — the only difference between them is the list and the
        /// stage, which is exactly the claim the two-list design makes.</summary>
        void BuildEffectListSection(VisualElement root, ref ZuiSection slot, string title, string tooltip,
                                    string key, List<ShaperEffectRef> list, ShaperEffectStage stage)
        {
            MigrateLegacyEntries(list);

            var box = Z.Section(title, tooltip, key, icon: "sparkles");
            slot = box;
            box.SetHeaderSuffix(() =>
            {
                int n = 0;
                for (int i = 0; i < list.Count; i++) if (list[i] != null && list[i].enabled) n++;
                return n > 0 ? $" ({n})" : "";
            });

            var listHost = new VisualElement();
            for (int i = 0; i < list.Count; i++)
                listHost.Add(BuildEffectCard(listHost, list, i, stage));

            if (list.Count == 0)
                listHost.Add(Z.Text("No effects.", ZuiText.Subtle, tooltip));
            box.Add(listHost);

            Button add = null;
            add = Z.Button("+ Add SpriteFX", tooltip, () => ShowAddEffectMenu(add, list, stage));
            box.Add(add);
            root.Add(box);
        }

        /// <summary>
        /// One effect card — the same shape as Pyre's modifier card (grip / enable / name / stage / ×, with the
        /// dials reflection-drawn in a foldable body), because a Shaper effect IS one of Pyre's modifiers and
        /// authoring it should not feel like a different thing in a different window.
        /// </summary>
        VisualElement BuildEffectCard(VisualElement listHost, List<ShaperEffectRef> list, int index,
                                      ShaperEffectStage stage)
        {
            var entry = list[index];
            var box = Z.Box(null, null);

            var header = new VisualElement();
            header.AddToClassList("zui-row");

            var grip = Z.Text("≡", ZuiText.Body, "Drag to reorder — an effect's position IS its apply order.");
            grip.style.unityFontStyleAndWeight = FontStyle.Bold;
            grip.style.width = 16f;
            ZuiReorder.MakeGrip(grip, box, listHost, (from, to) =>
            {
                Change(() =>
                {
                    var moved = list[from];
                    list.RemoveAt(from);
                    list.Insert(to, moved);
                });
                Rebuild();
            });
            header.Add(grip);

            var enableToggle = Z.Toggle("", "Run this effect.", entry.enabled, v =>
            {
                Change(() => entry.enabled = v);
                Rebuild();   // the dial body appears / disappears with the toggle, as Pyre's card does
            });
            header.Add(enableToggle);

            var inst = entry.instance as ShaperModifierEffect;
            bool ok = ShaperEffectRuntime.CanRun(entry.typeName, stage, out string reason);
            if (inst == null || inst.Settings == null)
            {
                ok = false;
                reason = "Its effect class could not be loaded — it may have been removed or renamed.";
            }

            string stageSentence = stage == ShaperEffectStage.PreComposite
                ? "Runs on this layer's own picture, before it composites into the document."
                : "Runs on the finished picture, after every layer has composited.";

            header.Add(Z.Text(inst != null ? inst.DisplayName : (entry.typeName ?? "(unnamed)"), ZuiText.Body,
                ok ? stageSentence : "Will not run — " + reason));
            header.Add(Z.Flexible());
            header.Add(Z.Text(ok ? (stage == ShaperEffectStage.PreComposite ? "pre-composite" : "post-composite")
                                 : "inert",
                              ZuiText.Subtle, ok ? stageSentence : reason));

            var removeBtn = Z.Button("×", "Remove this effect (undoable).", () =>
            {
                int at = list.IndexOf(entry);
                if (at >= 0) { Change(() => list.RemoveAt(at)); Rebuild(); }
            }).W(22f);
            header.Add(removeBtn);
            box.Add(header);

            // The dials. Reflection-drawn over the modifier itself, so every parameter the effect declares is
            // authorable without this window listing any of them by hand — the same drawer, with the same
            // Undo contract, that Pyre's own stack uses.
            VisualElement body = null;
            if (entry.enabled && ok)
            {
                body = new VisualElement();
                ZuiReflect.BuildFields(body, inst.Settings, EffectDrawerOptions(inst.DisplayName));
                box.Add(body);
            }

            if (!ok)
            {
                box.SetEnabled(false);
                box.tooltip = "Will not run — " + reason;
            }

            // Fold state is kept per ENTRY instance so it survives a rebuild (undo, reorder, layer change).
            ZuiFoldCard.Wire(entry, header, body, enableToggle, removeBtn);
            return box;
        }

        /// <summary>
        /// The reflection drawer's Undo / dirty / rebuild contract for an effect's dials — the same wiring
        /// <see cref="Val"/> gives every other control in this window (record the document BEFORE the mutation,
        /// dirty it and refresh the preview after).
        /// </summary>
        ZuiReflect.Options EffectDrawerOptions(string displayName) => new ZuiReflect.Options
        {
            OnBeforeChange = () => { if (document != null) Undo.RegisterCompleteObjectUndo(document, "Edit Shaper effect"); },
            OnChanged = () => { if (document != null) EditorUtility.SetDirty(document); RefreshPreview(); },
            OnStructureChanged = Rebuild,
            // The enable toggle lives in the header row; drawing the modifier's own `enabled` field too would
            // put two controls for one idea on one card.
            Skip = f => f.Name == "enabled",
            // A Min-Max dial hashes without the frame, so it resolves to ONE constant for the whole animation —
            // offering the mode would offer a control that cannot animate. Curve and Steps DO read the Shaper
            // phase (SpriteFxStack.LifeEval, which RunStack Prepares every effect with), so they stay. The
            // Duration/Warmup row is the runtime-seconds API and means nothing on a frame-baked timeline.
            ConfigureValue = (f, vopt) => { vopt.allowMinMax = false; vopt.hideCurveTiming = true; },
            TooltipFor = f => $"{ObjectNames.NicifyVariableName(f.Name)} — a {displayName} parameter.",
        };

        /// <summary>
        /// Upgrade entries authored before T-0163, which named a type and carried no settings. Constructing the
        /// modifier at defaults loses nothing: until this task no effect had authorable parameters and none of
        /// them ran, so class defaults ARE what the document meant. Done without <c>Undo.RecordObject</c>
        /// because it is an asset-format upgrade rather than an edit — putting it on the undo stack would offer
        /// Ctrl+Z on "open the window", which would silently downgrade the asset again.
        /// </summary>
        void MigrateLegacyEntries(List<ShaperEffectRef> list)
        {
            bool changed = false;
            for (int i = 0; i < list.Count; i++)
            {
                var e = list[i];
                if (e == null || e.instance != null || string.IsNullOrEmpty(e.typeName)) continue;
                var made = ShaperEffectRuntime.Create(e.typeName);
                if (made == null) continue;
                e.instance = made;
                changed = true;
            }
            if (changed && document != null) EditorUtility.SetDirty(document);
        }

        /// <summary>
        /// T-0184 — the add-SpriteFX picker. Owner: "I don't see a way to choose what modifier to add. Look at
        /// how it's done in Pyre and copy it." Pyre's own "+ Add modifier" (<c>PyreWindow.Modifiers.cs:344-378</c>)
        /// opens a flat vertical <c>Z.Menu</c> with a bold section heading per family and items stacked beneath
        /// each — fine at Pyre's window width, but the owner's separate standing feedback for this wave is "we
        /// have a lot of width but less height... at least 4 columns is totally acceptable" rather than a tall
        /// scrolling list. So the CONTENT is Pyre's (one section per family, an item per effect, every entry
        /// that cannot run AT THIS STAGE shown DISABLED with its own reason rather than filtered out — hiding
        /// Voronoi crack would answer "where is it?" with silence; showing it greyed answers "it needs an
        /// edge-distance sheet, which a folded picture does not publish"), but the LAYOUT is a wide grid: one
        /// column per family (Geometry / Pixel / Post / Simulation / Edge — 5 here, still "≥4 columns"), each
        /// its own vertical list, so the whole 41-entry catalog reads at a glance with no scroll for the common
        /// case. Built with <c>menu.Custom(...)</c> rather than repeated <c>menu.Item(...)</c> calls because
        /// <c>ZuiMenu</c>'s row list is one flat vertical stack (<c>ZuiMenu.cs:32</c>) with no per-row column
        /// placement — Custom hands us a real element to lay out a Row of columns into, reusing the SAME
        /// "zui-menu__item"/"zui-menu__label" chrome every other menu row uses (BuildEffectMenuItem below) so a
        /// column entry looks identical to a Pyre menu item, just placed in a grid instead of one long list.
        /// </summary>
        void ShowAddEffectMenu(VisualElement anchor, List<ShaperEffectRef> list, ShaperEffectStage stage)
        {
            string stageSentence = stage == ShaperEffectStage.PreComposite
                ? "Will run on this layer's own picture, before it composites."
                : "Will run on the finished picture, after every layer has composited.";

            // Canonical family order — Pyre's own Geometry/Pixel/Post + Shaper's Simulation slot, plus Edge
            // (the one genuinely-stuck effect, T-0114) shown rather than hidden, matching the catalog's own
            // "declared, greyed-out-with-a-reason" posture.
            string[] groupOrder = { "Geometry", "Pixel", "Post", "Simulation", "Edge" };
            var byGroup = new Dictionary<string, List<int>>();
            foreach (var g in groupOrder) byGroup[g] = new List<int>();
            for (int i = 0; i < ShaperEffectCatalog.All.Length; i++)
            {
                var kind = ShaperEffectCatalog.All[i].stageKind;
                if (!byGroup.TryGetValue(kind, out var bucket)) byGroup[kind] = bucket = new List<int>();
                bucket.Add(i);
            }
            foreach (var bucket in byGroup.Values)
                bucket.Sort((a, b) => string.CompareOrdinal(ShaperEffectCatalog.All[a].typeName, ShaperEffectCatalog.All[b].typeName));

            int columnCount = groupOrder.Count(g => byGroup.TryGetValue(g, out var b) && b.Count > 0);
            var menu = Z.Menu(anchor).Width(Mathf.Max(220f, columnCount * 190f));
            menu.Custom((body, close) =>
            {
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.flexWrap = Wrap.Wrap;

                foreach (var g in groupOrder)
                {
                    if (!byGroup.TryGetValue(g, out var bucket) || bucket.Count == 0) continue;

                    var col = new VisualElement();
                    col.style.flexGrow = 1f;
                    col.style.flexShrink = 1f;
                    col.style.flexBasis = 0f;
                    col.style.minWidth = 170f;
                    col.style.marginRight = 8f;

                    var header = new Label(g) { tooltip = g + " effects." };
                    header.AddToClassList("zui-menu__section");
                    col.Add(header);

                    foreach (var i in bucket)
                    {
                        var e = ShaperEffectCatalog.All[i];
                        bool ok = ShaperEffectRuntime.CanRun(in ShaperEffectCatalog.All[i], stage, out string reason);
                        string typeName = e.typeName;
                        col.Add(BuildEffectMenuItem(typeName, ok ? stageSentence : "Unavailable — " + reason, ok,
                            () =>
                            {
                                var made = ShaperEffectRuntime.Create(typeName);
                                if (made == null) return;
                                Change(() => list.Add(new ShaperEffectRef(typeName, made)));
                                Rebuild();
                            }, close));
                    }

                    row.Add(col);
                }

                body.Add(row);
            });
            menu.Show();
        }

        /// <summary>One catalog row inside the grouped add-SpriteFX grid — the same look and click/dismiss
        /// contract as a plain <c>ZuiMenu.Item</c> row (<c>ZuiMenu.cs</c>'s private <c>BuildItem</c>, which a
        /// <c>Custom</c> row cannot call directly), reused here so a column entry is visually identical to a
        /// one-column Pyre menu item.</summary>
        static VisualElement BuildEffectMenuItem(string label, string tooltip, bool enabled, Action onClick, Action close)
        {
            var itemRow = new VisualElement { tooltip = tooltip };
            itemRow.AddToClassList("zui-menu__item");

            var lbl = new Label(label) { pickingMode = PickingMode.Ignore };
            lbl.AddToClassList("zui-menu__label");
            itemRow.Add(lbl);

            if (enabled)
                itemRow.AddManipulator(new Clickable(() => { onClick?.Invoke(); close?.Invoke(); }));
            else
            {
                itemRow.AddToClassList("zui-menu__item--disabled");
                itemRow.SetEnabled(false);
            }
            return itemRow;
        }
    }
}
