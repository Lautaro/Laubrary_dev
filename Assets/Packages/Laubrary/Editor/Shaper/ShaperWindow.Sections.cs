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
        // T-0258 — Border, Swarm and Lighting stopped being sections (they are boxes inside the card whose
        // subject they belong to now: Fill, Shape and the selected layer's row), and the two effect lists
        // collapsed into one SpriteFX section, so only two of the six survive as bar entries.
        ZuiSection fillSection, swarmSection, effectsSection;

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

            // T-0257 — resolved ONCE per card build, here, because both the Fill card and the Border fill box
            // read it and a second Resolve() would walk the same tree twice for the same answer.
            fillDiagnostics = ResolveFillDiagnostics(layer);

            BuildBreadcrumb(root, layer);

            // T-0204 — reset every CONDITIONALLY-built section before deciding whether to rebuild it, so a
            // stale ZuiSection from a PREVIOUS selection never lingers in the toggle bar once its card is
            // genuinely absent for the current one (a composite node has no Fill). Without this the bar kept
            // offering a segment whose section no longer existed anywhere in the tree, so toggling it did
            // nothing — "Fill and Border are selectable but don't show up in the UI" (owner).
            // T-0258 — Border and Lighting are boxes now, and a box is owned by the card that draws it rather
            // than by the bar. Swarm is a section again (owner, 2026-09-08: Pyre keeps it in the bar, and a
            // section header is where its on/off checkbox reads as a section's).
            fillSection = null;
            swarmSection = null;

            // The per-shape bodies (generator dials, bag members, solid dials) and the combine/sweep/shell
            // ops moved INTO the Shape card in T-0182 — see ShaperWindow.BuildShapeSection. They are drawn
            // by the same absence rule, one card closer to the choice that summons them.
            // T-0191 — A COMPOSITE NODE GETS NEITHER CARD, AND THEY ARE ABSENT RATHER THAN DISABLED.
            // SHAPER_THE_DESIGN §6.2: a composite "produces a finished picture directly" and "may not be
            // re-filled"; its colour is authored on the GENERATOR (the hosted Pyre card's own fill ramp) and
            // its edge is a raster, so a Shaper fill would be a second authority over the same pixel and a
            // border has no analytic edge to trace. A greyed card would still be a promise — it says "this
            // exists, you just cannot reach it today" — where the truth is that it does not apply at all.
            // T-0258 — the Border card is now the Fill card's "Edge" box (BuildFillSection adds it), and the
            // Swarm card is the Shape card's "Swarm" box (BuildShapeSection adds it), so a composite node
            // skipping Fill now skips its edge with it — which is the same rule stated once instead of twice.
            if (node.kind != ShaperNodeKind.Composite) BuildFillSection(root, node);

            // T-0265 — A SWARM IS ABSENT WHERE EVERY SHAPE IS A SOLID, for the same reason Sweep and Shell
            // are. A swarm fans the node's SHAPE PROGRAM out into N placed instances; a Solids generator
            // replaces that program and writes one silhouette over its owner's whole slab (LR-6.1), so every
            // instance is overwritten by the same single draw. Measured: all thirty-three swarm controls move
            // 0 pixels on a Pyramid. Absent rather than greyed, because this is not "inert right now" — a
            // swarm does not apply to this kind of shape at all.
            if (!EveryLeafIs(node, false, true)) BuildSwarmSection(root, node);

            // T-0258 — Lighting moved out of here entirely: it is a LAYER property, so it is now a toggle plus
            // a folded box on the selected layer's own row (RefreshSelectedLayerCards, ShaperWindow.cs), where
            // Height and Mask already went in T-0187/T-0204, and it is drawn for the selected layer whether or
            // not the author has drilled into a bag member — the layer being edited does not change when they do.
            BuildEffectsSection(root, layer);
        }

        /// The sections this file adds, for the shell's toggle bar. Null entries are skipped by the bar.
        internal (string, ZuiSection)[] SectionBarEntries() => new[]
        {
            ("Fill", fillSection),
            ("Swarm", swarmSection),
            ("SpriteFX", effectsSection),
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
            return Inert(Val(label, reason ?? tip, v, lo, hi, decimals: decimals),
                         reason == null ? null : "Does nothing on a " + form + ": " + reason);
        }

        /// <summary>
        /// T-0257 — the ONE way this window declares a control inert. Lifted out of <see cref="SolidVal"/>
        /// (which was the only place doing it) so every other "the wire behind this dial is cut right now"
        /// case states itself the same way instead of inventing a second convention: draw the control,
        /// disable it, and put the REASON on it as its tooltip, because a dial that quietly vanishes teaches
        /// the author nothing and a dial that quietly does nothing teaches them something false.
        ///
        /// <paramref name="reason"/> null means "this dial can act" and the control is returned untouched, so
        /// a call site can compute a reason-or-null once and wrap unconditionally.
        /// </summary>
        static VisualElement Inert(VisualElement el, string reason)
        {
            if (el == null || reason == null) return el;
            el.SetEnabled(false);
            el.tooltip = reason;
            return el;
        }

        /// <summary>
        /// <see cref="Val"/> plus that declaration in one call — the exact shape <see cref="SolidVal"/> has
        /// always had, generalised so every other reason a dial can be dead uses it rather than a second
        /// idiom. The reason is passed INTO the control as its own tooltip as well as onto the wrapper, so
        /// the sentence is there whichever part of the control the pointer lands on.
        /// </summary>
        VisualElement InertVal(string label, string tip, string reason, ZUIValue v, float lo, float hi,
                               bool cyclic = false, int decimals = -1)
            => Inert(Val(label, reason ?? tip, v, lo, hi, cyclic, decimals), reason);

        /// <summary>
        /// The same declaration applied to INDIVIDUAL OPTIONS of a <c>Z.MiniRadio</c>/<c>Z.Segmented</c> row,
        /// for a choice control where some options are unreachable rather than the whole control being dead
        /// (the Ramp quantity radio: six of its nine values name a sheet the shipped shape stage never
        /// publishes). Both factories build exactly one child element per option, in option order, so the
        /// row's children ARE the options — no ZUI change is needed to say so.
        ///
        /// <paramref name="reasonAt"/> returns null for an option that is genuinely available.
        /// </summary>
        static VisualElement InertOptions(VisualElement row, Func<int, string> reasonAt)
        {
            if (row == null || reasonAt == null) return row;
            int i = 0;
            foreach (var option in row.Children())
            {
                string reason = reasonAt(i++);
                if (reason == null) continue;
                option.SetEnabled(false);
                option.tooltip = reason;
            }
            return row;
        }

        /// <summary>
        /// Whether a dial can ever be anything but zero. A Static dial at zero is a dial that does nothing,
        /// full stop; an ANIMATED one is non-zero somewhere on the timeline even when it reads zero at the
        /// frame on screen, so it is never declared inert — greying a curve because the playhead happens to
        /// sit on its zero would be a worse lie than the one this fixes.
        /// </summary>
        static bool DialAlwaysZero(ZUIValue v)
            => v == null
            || (v.mode == ZUIValue.Mode.Static && Mathf.Approximately(ShaperValue.Sample(v, 0f, 0u), 0f));

        /// <summary>
        /// T-0257 — does the document's rig hold a light the compiler will actually use? The same test
        /// <see cref="ShaperLightCompiler"/> compiles with (an entry that is null or disabled is not a light),
        /// shared with <see cref="BuiltInSolidKeyStandingIn"/> above rather than restated, so the Lighting
        /// card cannot grey a dial the renderer is still reading.
        /// </summary>
        bool DocumentHasEnabledLight()
        {
            var rig = document != null ? document.lightRig : null;
            if (rig == null || rig.lights == null) return false;
            for (int i = 0; i < rig.lights.Count; i++)
                if (rig.lights[i] != null && rig.lights[i].enabled) return true;
            return false;
        }

        /// <summary>
        /// Whether anything in this layer's tree is a Solid. Solids are EXEMPT from the empty-rig unlit gate
        /// (<c>ShaperLightCompiler</c>'s built-in key, T-0203) and that exemption keeps the layer's own
        /// response — its specular, rim and intensity scale are still read — so a layer holding one has live
        /// lighting dials even with an empty rig, and greying them there would be wrong.
        /// </summary>
        static bool LayerHasSolid(ShaperNode node)
        {
            if (node == null || !node.enabled) return false;
            if (node.kind == ShaperNodeKind.Solid) return true;
            if (node.children != null)
                for (int i = 0; i < node.children.Count; i++)
                    if (LayerHasSolid(node.children[i])) return true;
            return false;
        }

        /// <summary>
        /// T-0265 — every leaf under this node is a kind that REPLACES a shipped stage, so nothing in the layer
        /// reaches the stage named by <paramref name="composites"/>/<paramref name="solids"/>.
        ///
        /// Measured rather than assumed: on a layer whose only shape is a composite, all nine height dials,
        /// all eleven light-response dials and the mask's own source picker change 0 pixels; on a layer whose
        /// only shape is a solid, the nine height dials and the mask source change 0 pixels while the light
        /// response still acts (a Solid publishes its own normals into the same sheets and goes through the
        /// ordinary light law). A bag of primitives and one composite still has primitives to act on, which is
        /// why this asks about EVERY leaf and not about the root's own kind.
        /// </summary>
        static bool EveryLeafIs(ShaperNode node, bool composites, bool solids)
        {
            if (node == null || !node.enabled) return true;
            if (node.kind == ShaperNodeKind.Bag)
            {
                if (node.children == null || node.children.Count == 0) return true;
                for (int i = 0; i < node.children.Count; i++)
                    if (!EveryLeafIs(node.children[i], composites, solids)) return false;
                return true;
            }
            if (node.kind == ShaperNodeKind.Composite) return composites;
            if (node.kind == ShaperNodeKind.Solid) return solids;
            return false;
        }

        /// <summary>
        /// T-0265 — the height stage does not run for this layer at all, because every shape in it is a Solid.
        ///
        /// Structural, not circumstantial: <c>ShaperFillResolver.PaintTile</c> takes the Solids branch for such
        /// an owner and returns before <c>ShaperHeight.FillTile</c> is reached, so no height op is ever
        /// evaluated over its samples. Measured to agree — all nine dials move 0 pixels on a Pyramid and on an
        /// Orb. A bag holding one primitive and one solid still has a primitive to extrude, which is why this
        /// asks about EVERY leaf rather than about the root's own kind.
        /// </summary>
        static string NoHeightStageReason(ShaperNode root)
            => EveryLeafIs(root, false, true)
             ? "This layer's shape writes its own surface directly, so the height stage never runs for it and "
             + "this changes nothing (measured: 0 pixels on every dial here)."
             : null;

        /// <summary>
        /// T-0203 — is the built-in key light standing in for an empty rig right now? Answered from the
        /// AUTHORED rig using the same test <see cref="ShaperLightCompiler"/> compiles with (an entry that is
        /// null or disabled is not a light), so the card cannot claim one thing while the renderer does
        /// another.
        /// </summary>
        bool BuiltInSolidKeyStandingIn()
        {
            if (document == null) return false;

            // The layer's own switch wins, exactly as it does in the engine (ShaperLightCompiler.BindLayer):
            // an author who turned this layer's lighting off asked for an unlit layer and gets one, so
            // claiming a built-in key here would be the card lying about the picture.
            var layer = selectedLayer >= 0 && selectedLayer < document.layers.Count
                      ? document.layers[selectedLayer] : null;
            if (layer == null || layer.response == null || !layer.response.receiveLighting) return false;

            return !DocumentHasEnabledLight();
        }

        /// The solid dials, drawn inside the Shape card for a node whose picked shape is a solid (T-0182).
        /// The form itself is no longer chosen here — it is one of the Solids column's entries in the shape
        /// picker, because "which solid" is the same question as "what does this node draw".
        internal void BuildSolidBody(VisualElement box, ShaperNode node)
        {
            var s = node.solid ?? (node.solid = new ShaperSolidDef());

            // T-0203 — say which light is shading this solid, because the answer can be a light that does not
            // appear in the Lights section. A Solid is exempt from T-0200's "no lights ⇒ render unlit" rule
            // (ShaperLightCompiler.BuiltInSolidKey): unlit, a Solid does not read as a dim solid, it reads as
            // a flat one-colour silhouette with its bevel and facets gone, because the shading term was the
            // only thing expressing them. A light the author cannot see is exactly what LR-7.3 forbids leaving
            // silent, so it is stated here and the sentence is the engine's own const.
            if (BuiltInSolidKeyStandingIn())
                box.Add(Z.Text(ShaperLightRig.BuiltInSolidKeyActive, ZuiText.Subtle,
                               ShaperLightRig.BuiltInSolidKeyActive));

            // What the ordinary stages do to a Solid is said in the tooltips of the controls concerned (the Fill
            // card's kind row, the Line width dial), not as an on-screen paragraph: explanation is tooltip
            // content. A Border is refused on a Solid (ShaperFillResolver.BindBorder), so it is not mentioned.

            // T-0265 — CENTRE X / CENTRE Y AND ROLL ARE NO LONGER DRAWN. They were a second way to move and
            // turn a shape, sitting beside the Shape card's own Position box on every other node kind, and the
            // owner asked for exactly one. Position now reaches a solid for real (ShaperSolids.Place), so
            // Translate does what Centre did and Rotation does what Roll did. The fields stay serialized and
            // are still read by the engine, so an authored document keeps every value it had; nothing is lost,
            // there is simply no second control offering the same move.
            box.Add(Z.HGroup(
                SolidVal("Size", "How big the solid is, in canvas pixels. The Position box's Scale multiplies "
                    + "this rather than replacing it.", s.size, 1f, 128f,
                    s.form, ShaperSolidDial.Size),
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

            // Yaw and Tilt turn the BODY in depth — there is no 2D equivalent of them and the Position box
            // cannot express one, so they stay here. Roll is the axis that points at the viewer, which IS the
            // Position box's Rotation, so it is drawn there instead (T-0265).
            box.Add(Z.HGroup(
                SolidVal("Yaw", "Turns the solid about the vertical axis, in degrees — it swings away from "
                    + "the viewer rather than turning on the canvas.", s.yaw, -180f, 180f,
                    s.form, ShaperSolidDial.Yaw, decimals: 0),
                SolidVal("Tilt", "Leans the solid towards or away from the viewer, in degrees.", s.tilt,
                    -180f, 180f, s.form, ShaperSolidDial.Tilt, decimals: 0)));

            // "A facet edge line and never a border" (BD-4.1): these trace INTERIOR facet seams, which are
            // nowhere in the zero set of any 2D field, so no border stage could produce them. The label says
            // line, not border, for that reason.
            box.Add(Z.HGroup(
                SolidVal("Line width", "Facet seam line half-width, in canvas pixels. This is a solid's edge; the Fill "
                    + "card's Edge strip does not apply to a solid.", s.lineWidth, 0f, 8f,
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

        // ── Fill refusals, said out loud (T-0257) ────────────────────────────────────────────────────────
        //
        // The engine has always written a complete sentence when it refuses a fill and paints the default
        // Solid instead — "Ramp-by-value on 'Torso' needs Height, which this shape does not publish." — and
        // until now the ONLY reader of those five strings was ShaperFillAudit. So picking Ramp by value ▸
        // Height on a layer with no height stage painted flat white and said nothing anywhere on screen.
        // ShaperFillResolver.Resolve is the same tree walk the renderer already does per frame; it paints
        // nothing, so calling it once per card build to read its diagnostics is cheap and, more importantly,
        // it is the SAME answer the picture came from rather than a second opinion computed here.

        /// The layer's current fill diagnostics, resolved once per card build by BuildAuthoringSections.
        ShaperFillDocument fillDiagnostics;

        ShaperFillDocument ResolveFillDiagnostics(ShaperLayer layer)
        {
            if (document == null || layer == null || layer.root == null) return null;
            // The renderer's own half-extents (ShaperDocumentRenderer.cs:238-239) — a fill's anchor box is
            // measured against them, so a different pair here could refuse differently from the picture.
            float halfW = 0.5f * (document.canvasWidth - 1) * document.pixelSize;
            float halfH = 0.5f * (document.canvasHeight - 1) * document.pixelSize;
            // The phase of the frame ON SCREEN, through ShaperClock (the one home for that conversion) rather
            // than off document.phase01, which the renderer writes and restores mid-render and so is not a
            // stable reading of anything.
            float phase = ShaperClock.PhaseOfFrame(currentFrame, document.frameCount);
            try
            {
                return ShaperFillResolver.Resolve(layer.root, phase, document.seed, halfW, halfH,
                                                  ShaperQuantitySet.ShippedShapeEngine);
            }
            catch (Exception)
            {
                // A diagnostic read may never be the reason a card fails to draw.
                return null;
            }
        }

        /// <summary>
        /// The refusal sentence for one node's fill, or null when nothing was refused. Matched by the name the
        /// engine puts in its own sentence — the node's name for a fill, "<c>&lt;node&gt; border</c>" for a
        /// border (ShaperFillResolver.Refuse's `label` parameter), which is why this takes the whole label
        /// rather than the node.
        ///
        /// The engine records the FIRST offender of each kind plus a count, so a second refused node in the
        /// same layer has no sentence of its own to show. Its card therefore stays silent rather than
        /// borrowing somebody else's sentence, and the count is reported on the card that does own one.
        /// </summary>
        string FillRefusalFor(string who)
        {
            var d = fillDiagnostics;
            if (d == null || string.IsNullOrEmpty(who)) return null;

            if (d.hasUnavailableFill && d.unavailableFillNode == who)
                return Countable(d.unavailableFillReason, d.unavailableFillCount);
            if (d.hasTypeRefusedFill && d.typeRefusedFillNode == who)
                return Countable(d.typeRefusedFillReason, d.typeRefusedFillCount);
            if (d.hasSubtractFill && d.subtractFillNode == who)
                return Countable(d.subtractFillReason, d.subtractFillCount);
            if (d.hasFallbackFill && d.fallbackFillNode == who)
                return Countable(d.fallbackFillReason, d.fallbackFillCount);
            return null;
        }

        /// <summary>The border-strip refusal (BD-3.7), which has its own remedy and so its own sentence.</summary>
        string BorderRefusalFor(ShaperNode node)
        {
            var d = fillDiagnostics;
            if (d == null || node == null) return null;
            if (d.hasSubtractBorder && d.subtractBorderNode == node.name)
                return Countable(d.subtractBorderReason, d.subtractBorderCount);
            return FillRefusalFor(node.name + " border");
        }

        /// The engine keeps a count precisely so a UI can say "and two more like it" instead of pointing at
        /// one node and silently hiding the rest (ShaperProgram.cs:90-95).
        static string Countable(string reason, int count)
            => count > 1 ? reason + " (" + count + " fills in this layer were refused this way.)" : reason;

        /// <summary>
        /// The status line itself: the engine's OWN sentence, verbatim, in the same <c>Subtle</c> shape the
        /// Mask card's "Falling back to Coverage." line already uses — so a refused fill reads the same
        /// wherever it happens. Not a paraphrase, because the sentence names the fill kind, the node, the
        /// missing quantity AND where it went missing, and every one of those is the remedy.
        /// </summary>
        static VisualElement RefusalLine(string reason) => Z.Text(reason, ZuiText.Subtle, reason);

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
                // T-0258 — an edge is authorable whether or not the node owns a fill of its own: a layer root
                // with an empty slot is still painted (FC-3.2 substitutes a default), so the box goes in on
                // both paths rather than being stranded behind the Add-fill affordance. Not on a Solid (see below).
                if (node.kind != ShaperNodeKind.Solid) BuildEdgeBox(box, node);
                root.Add(box);
                return;
            }

            // T-0257 — said BEFORE the dials, not after them: the whole failure this closes is an author
            // tuning a ramp that is not the thing on screen.
            string refused = FillRefusalFor(node.name);
            if (refused != null) box.Add(RefusalLine(refused));

            BuildFillBody(box, node.fill, "shaper.window.fill");
            box.Add(Z.Button("Remove fill", "Drop this node's own fill and fall back to the default/inherited one.",
                () => { Change(() => node.fill = null); Rebuild(); }));
            // A Solid has no analytic edge for a strip to trace (its silhouette is written by ShaperSolids over
            // the node's carrier box), so the resolver refuses a border on it exactly as on a composite, and the
            // card offers none: the solid's own Line width IS its edge.
            if (node.kind != ShaperNodeKind.Solid) BuildEdgeBox(box, node);
            root.Add(box);
        }

        /// The fill editor, reused verbatim for a node's own fill and for a border's fill — they are the same
        /// ShaperFillDef type (ShaperBorderDef.fill:107), so one builder is the only honest way to keep them
        /// from drifting apart.
        void BuildFillBody(VisualElement box, ShaperFillDef f, string keyPrefix)
        {
            box.Add(Z.Field("Kind", "Which fill this is. Switching it changes the dials below.",
                Z.MiniRadio((int)f.kind, ShaperWords.Names(typeof(ShaperFillKind)),
                    "Which fill this is.", v => { Change(() => f.kind = (ShaperFillKind)v); Rebuild(); },
                    wrap: true)));

            // Cross-kind fields, packed as ONE continuous group so an overflowing control lands beside the
            // next one instead of orphaned on a line of its own.
            box.Add(Z.HGroup(
                // T-0257 — "Composite" was the pipeline's word for it; the author is choosing how this fill
                // blends with what is underneath, and the SpriteFX cards now say "blending" for the same idea.
                Z.Field("Blending", "Whether this fill paints OVER what is underneath or ADDS to it.",
                    Z.Segmented((int)f.composite, ShaperWords.Names(typeof(ShaperFillComposite)),
                        "Over paints on top; Add sums, which is what makes glow read as glow.",
                        v => Change(() => f.composite = (ShaperFillComposite)v))),
                Z.Field("Space", "Whether the fill travels with the shape or stays put on the canvas.",
                    Z.Segmented((int)f.space, ShaperWords.Names(typeof(ShaperFillSpace)),
                        "“Moves with the shape” stamps the fill onto it; “Stays put” holds the fill still "
                        + "while the shape moves through it.",
                        v => Change(() => f.space = (ShaperFillSpace)v))),
                Z.Field("Fit", "How the fill is fitted to the shape's bounds.",
                    Z.Segmented((int)f.fit, ShaperWords.Names(typeof(ShaperFillFit)),
                        "“Keep proportions” preserves aspect; “Stretch to fit” fills the bounds exactly.",
                        v => Change(() => f.fit = (ShaperFillFit)v))),
                // INVENTORY GAP CLOSED — veil (:60) and heightDelta (:68) apply to every fill kind and had
                // no UI anywhere. heightDelta is the input the relief shading of T-0110/T-0127 reads, so
                // without it relief could not be authored at all.
                Val("Fade", "Multiplies this fill's own transparency — the fade the palette applies on top "
                    + "of whatever edge rule the fill computed.", f.veil, 0f, 1f),
                Val("Height change", "How much this fill raises or lowers the surface it paints. This is the "
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
            box.Add(Val("Posterise", "Snap the result to this many discrete colour bands. 0 leaves it smooth.",
                f.quantiseLevels, 0f, 32f, decimals: 0));
        }

        void BuildGradientFill(VisualElement box, ShaperFillDef f)
        {
            box.Add(Z.Field("Mode", "How the gradient is projected across the shape.",
                Z.MiniRadio((int)f.gradientMode, ShaperWords.Names(typeof(ShaperGradientMode)),
                    "Linear sweeps along an angle; Radial runs out from a centre; Angular sweeps around it; "
                    + "“From the edge inwards” follows how far each sample is from the shape's edge.",
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
                // T-0257 — one of the three unrelated "Depth"s this window used to show. The solid's Z
                // half-extent keeps the bare word; this one and the height stage's are qualified.
                rows.Add(Val("Ramp depth", "How far in from the edge the ramp is spread, in pixels.",
                    f.gradientDepthPixels, 0f, 128f));
            box.Add(Z.HGroup(rows.ToArray()));
        }

        void BuildRampFill(VisualElement box, ShaperFillDef f)
        {
            // T-0257 — six of the nine values name a sheet the shipped shape stage NEVER publishes
            // (ShaperFillSheets.FromShapeStage publishes Coverage + Edge distance, and Height only when the
            // layer carries a height stage), so two thirds of this radio was a trap: picking one painted flat
            // white with no message anywhere. The values stay — the enum is append-only and an authored
            // document may already hold one — but they are declared inert with the reason, which is the same
            // posture ShaperSolids.InertReason takes for a dial that does nothing on a given solid form.
            bool layerHasHeight = CurrentLayer != null && CurrentLayer.height != null;
            box.Add(Z.Field("Quantity", "Which published quantity drives the ramp.",
                InertOptions(
                    Z.MiniRadio((int)f.rampQuantity, ShaperWords.Names(typeof(ShaperQuantity)),
                        "The sheet this fill reads to decide where in the ramp each sample lands.",
                        v => { Change(() => f.rampQuantity = (ShaperQuantity)v); Rebuild(); }, wrap: true),
                    i => RampQuantityReason((ShaperQuantity)i, layerHasHeight))));

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

        /// <summary>
        /// Why a ramp quantity cannot be used right now, or null when it can. The three the shape stage does
        /// publish are named from the engine's own sets (<see cref="ShaperQuantitySet.ShippedShapeEngine"/>
        /// plus Height when a height stage is attached, HS-1.4); the other six have no publisher at all in
        /// the shipped engine, and Surface direction is additionally refused on TYPE — the engine's own
        /// sentence for that is reused rather than a second one written here.
        /// </summary>
        static string RampQuantityReason(ShaperQuantity q, bool layerHasHeight)
        {
            switch (q)
            {
                case ShaperQuantity.Coverage:
                case ShaperQuantity.EdgeDistance:
                    return null;
                case ShaperQuantity.Height:
                    return layerHasHeight ? null
                        : "This layer has no height stage, so nothing publishes a height to ramp through. "
                          + "Turn Height on for the layer and this becomes available.";
                case ShaperQuantity.SurfaceDirection:
                    return "Not available: " + ShaperQuantities.SurfaceDirectionRefusal + ".";
                default:
                    return "Nothing in the shipped shape engine publishes " + ShaperWords.Of(q).ToLowerInvariant()
                         + ". It is kept because a simulation source could publish it, and because a document "
                         + "authored against one must not lose its setting.";
            }
        }

        void BuildTextureFill(VisualElement box, ShaperFillDef f)
        {
            box.Add(Z.Field("Texture", "The image this fill samples.",
                Z.Object<Texture2D>(f.texture, "The image this fill samples.",
                    t => Change(() => f.texture = t), 200f)));
            box.Add(Z.Field("Mapping", "How the texture is mapped onto the shape.",
                Z.Segmented((int)f.textureMapping, ShaperWords.Names(typeof(ShaperTextureMapping)),
                    "“Fit once” stretches a single copy to the bounds; “Repeat” tiles it.",
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
            box.Add(Z.Field("Band direction", "Which way the bands run across the shape.",
                Z.Segmented((int)f.stripParameterisation,
                    ShaperWords.Names(typeof(ShaperStripParameterisation)),
                    "“Around the shape” walks the bands round its outline; “Across the shape” walks them "
                    + "along one axis.",
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
                Val("Grain seed", "Varies the pattern without changing its character.", f.steelSeed, 0f, 9999f, decimals: 0),
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
                Z.MiniRadio((int)f.proceduralKind, ShaperWords.Names(typeof(ShaperProceduralKind)),
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
                        Z.Segmented((int)f.noiseKind, ShaperWords.Names(typeof(ShaperNoiseKind)),
                            "Smooth is plain noise; Ridged creases it into ridges; Banded posterises it into "
                            + "four bands.",
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
            // T-0201 — AfterEdit, not SetDirty+RefreshPreview: a gradient stop moved without invalidating the
            // frame cache re-served the frame rendered before the move, so the ramp changed and the picture
            // did not.
            var ctrl = Z.Gradient(g, tooltip, AfterEdit);
            ctrl.OnBeforeMutate = () =>
            {
                if (document != null) Undo.RegisterCompleteObjectUndo(document, "Edit Shaper Document");
            };
            return ctrl;
        }

        // ── Edge (was the "Border" section) ──────────────────────────────────────────────────────────────

        // ShaperNode.border (:161) is nullable like fill, so the box offers add/remove. Its `enabled` flag is
        // separate from existing at all, and both are meaningful: an edge that exists but is off keeps its
        // authored width and fill for when it is switched back on.
        //
        // T-0258 — this was a top-level section with its own toggle-bar segment, and it is a folded box inside
        // the FILL card now. A border is a second fill painted on a strip derived from the same coverage: it
        // carries a whole ShaperFillDef of its own (ShaperBorderDef.fill:107) and is drawn by the very same
        // BuildFillBody. Giving it a segment of its own doubled the height of the colour column for something
        // most nodes never author. Named "Edge" rather than "Border" because the strip is the shape's edge —
        // "border" reads as a frame around the picture. The state key stays "shaper.window.border" (a key names
        // the data, never the label) and the nested fill box keeps "shaper.window.border.fill".
        void BuildEdgeBox(VisualElement parent, ShaperNode node)
        {
            var box = Z.BoxKeyed("Edge", "A derived strip around this node's edge, with its own fill.",
                "shaper.window.border");

            if (node.border == null)
            {
                box.Add(Z.Field("Edge", "This node has no edge strip.",
                    Z.Button("Add edge", "Give this node an edge strip.",
                        () => { Change(() => node.border = new ShaperBorderDef { enabled = true, fill = SeededBorderFill(node) }); Rebuild(); })));
                parent.Add(box);
                return;
            }

            var b = node.border;
            // A folded box must still say whether the thing it holds is on — the same job the section header's
            // own toggle used to do from the bar.
            box.SetHeaderSuffix(() => b.enabled ? "" : " — off");

            // The switch lives on the header, like a section's: the header names the strip and turns it on.
            box.SetHeaderToggle(b.enabled, "Draw this edge strip.",
                v => { Change(() => b.enabled = v); Rebuild(); });
            box.Add(Z.HGroup(
                Val("Width", "How thick the border strip is, in canvas pixels.", b.width, 0f, 32f),
                // T-0257 — "Alignment" was the label on two unrelated cards (this and the Shell); this one
                // says where the strip SITS relative to the edge it traces.
                Z.Field("Sits", "Which side of the edge the strip sits on.",
                    Z.Segmented((int)b.alignment, ShaperWords.Names(typeof(ShaperShellAlignment)),
                        "Centred straddles the edge; Inward grows into the shape; Outward grows out of it.",
                        v => Change(() => b.alignment = (ShaperShellAlignment)v))),
                Z.Toggle("Joins coverage",
                    "Whether the border adds itself to the shape's coverage, or only paints over it.",
                    b.joinsCoverage, v => Change(() => b.joinsCoverage = v))));

            // T-0257 — a border strip has its own refusal (BD-3.7/BD-3.8) with its own remedy, so it gets its
            // own sentence rather than being folded into the node's.
            string borderRefused = BorderRefusalFor(node);
            if (borderRefused != null) box.Add(RefusalLine(borderRefused));

            // The border carries a full ShaperFillDef of its own, so it gets the same editor rather than a
            // reduced copy that would drift.
            if (b.fill == null)
            {
                box.Add(Z.Field("Edge fill", "The edge strip has no fill of its own yet.",
                    Z.Button("Add edge fill", "Give the edge strip its own fill.",
                        () => { Change(() => b.fill = SeededBorderFill(node)); Rebuild(); })));
            }
            else
            {
                var fillBox = Z.BoxKeyed("Edge fill", "How the edge strip itself is coloured.",
                    "shaper.window.border.fill");
                BuildFillBody(fillBox, b.fill, "shaper.window.border.fill");
                box.Add(fillBox);
            }

            box.Add(Z.Button("Remove edge", "Remove this node's edge strip entirely.",
                () => { Change(() => node.border = null); Rebuild(); }));
            parent.Add(box);
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
        /// <param name="carves">T-0265 — false for a Solid. Sweep and Shell carve the node's own SHAPE
        /// PROGRAM, and a Solids generator replaces that program outright (LR-6.1), so on a solid they change
        /// nothing at all (measured: 0 pixels on all six forms, for a 120° sweep and a 4 px shell). Combine is
        /// still drawn, because how a member folds into its bag is a property of the member, not of the shape
        /// stage it happens to use.</param>
        internal void BuildShapeOpsBody(VisualElement box, ShaperNode node, bool carves = true)
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
                        Z.Segmented((int)node.mode, ShaperWords.Names(typeof(ShaperCombineMode)),
                            "Add unions; “Cut out” carves this member away; “Keep overlap” keeps only the "
                            + "part both cover.",
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

            if (!carves) return;

            var sweep = Z.BoxKeyed("Sweep", "Keep only an angular or fractional slice of the shape.",
                "shaper.window.sweep");

            // T-0257 — A SWEEP HAS TWO PARAMETERISATIONS AND ONLY ONE OF THEM EVER ACTS. The axis is the
            // SHAPE's own answer, not an authored choice (ShaperPrimitives.SweepAxis: a Capsule is
            // Longitudinal, every other primitive is Radial — ShaperPrimitives.cs:329-330), and the compiler
            // reads the degree pair on a Radial axis and the fraction pair on a Longitudinal one
            // (ShaperCompiler.EmitSweep). So on a disc the two "ƒ" dials moved nothing and said nothing.
            // Answered ONLY for a Primitive, where the axis is exactly known; a bag folds its members' axes
            // and disagreement falls back to Radial, so claiming an answer there would be a guess.
            string degreeReason = null, fractionReason = null;
            if (node.kind == ShaperNodeKind.Primitive && node.primitive != null)
            {
                bool longitudinal =
                    ShaperPrimitives.SweepAxis(node.primitive.kind) == ShaperSweepAxis.Longitudinal;
                string inert = longitudinal
                    ? "This shape is swept along its length, so the angle dials do nothing here — use Start "
                      + "and Extent below."
                    : "This shape is swept around its centre, so the fraction dials do nothing here — use the "
                      + "degree dials above.";
                if (longitudinal) degreeReason = inert; else fractionReason = inert;
            }

            sweep.SetHeaderToggle(node.sweep.enabled, "Apply the sweep.",
                v => Change(() => node.sweep.enabled = v));
            sweep.Add(Z.HGroup(
                InertVal("Start", "Where the kept slice begins, in degrees.", degreeReason,
                    node.sweep.startDegreesDial, 0f, 360f, cyclic: true, decimals: 0),
                InertVal("Extent", "How much of the shape is kept, in degrees. Animate it to wipe the shape "
                    + "on or off over the document's frames.", degreeReason,
                    node.sweep.extentDegreesDial, 0f, 360f, decimals: 0),
                InertVal("Start ƒ", "Where the kept slice begins as a fraction of the shape's length.",
                    fractionReason, node.sweep.startFractionDial, 0f, 1f),
                InertVal("Extent ƒ", "How much is kept as a fraction of the shape's length.",
                    fractionReason, node.sweep.extentFractionDial, 0f, 1f)));
            box.Add(sweep);

            var shell = Z.BoxKeyed("Shell", "Hollow the shape into a shell of a given thickness.",
                "shaper.window.shell");
            shell.SetHeaderToggle(node.shell.enabled, "Hollow this shape.",
                v => Change(() => node.shell.enabled = v));
            shell.Add(Z.HGroup(
                Val("Thickness", "How thick the remaining shell is, in canvas pixels.",
                    node.shell.thicknessDial, 0f, 32f),
                Z.Field("Taken from", "Which side of the surface the shell is taken from.",
                    Z.Segmented((int)node.shell.alignment, ShaperWords.Names(typeof(ShaperShellAlignment)),
                        "Centred straddles the surface; Inward keeps material inside it; Outward outside.",
                        v => Change(() => node.shell.alignment = (ShaperShellAlignment)v)))));
            box.Add(shell);
        }

        // ── Swarm ────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Swarm is its own SECTION with a bar entry, as in Pyre (owner, 2026-09-08 — T-0258 had folded it into
        /// the Shape card as a box). Its on/off switch is the section header's checkbox; off, the section is
        /// just its header. Same controls, same ranges, same state key ("shaper.window.swarm"), so a saved view
        /// keeps folding it exactly as before.
        /// </summary>
        void BuildSwarmSection(VisualElement root, ShaperNode node)
        {
            var s = node.swarm;
            s.EnsureDials();
            var box = swarmSection = Z.Section("Swarm",
                "Repeat this node's own content many times with per-instance jitter.",
                "shaper.window.swarm", icon: "copy");
            box.SetHeaderSuffix(() => s.enabled ? " — " + s.count : "");
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
                Z.Field("Swarm seed", "Varies the jitter without changing its character. Clamped to a positive "
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
            // T-0257 — routed through the shared InertVal rather than its own SetEnabled/tooltip pair, so
            // there is exactly one way this window says "dead, and here is why".
            var mergeCarve = InertVal("Merge carve", null,
                "Does nothing on a swarm: instances are unioned, and carve strength is read only where a node "
                + "subtracts. The merge keeps the value so a blend that does carve still has it.",
                s.merge.carveStrengthDial, 0f, 1f);

            // T-0257 — "Interact" was a verb with no object. What the flag does is let one instance's values
            // BLEED INTO its neighbours' rather than each being evaluated alone and unioned, so a cluster
            // reads as one body (ShaperSwarmDef.cs:250-258, measured by ShaperSwarmAudit's CT-6/CT-7). It is
            // read ONLY on the native swarm path — a source that declares IShaperSwarmNativeSource and
            // currently supports it (ShaperCompiler.cs:372) — and CT-7 measures that a Primitive compiles
            // identically with it on and off, so on anything else it is declared inert rather than left to
            // look live. That is the greying the card's own "declare inert with a reason, never hide" comment
            // has been promising since T-0169 without doing it.
            var nativeSource = node.kind == ShaperNodeKind.Composite ? node.composite?.source : null;
            bool nativeSwarm = nativeSource is IShaperSwarmNativeSource nat && nat.SupportsNativeSwarm;
            var instancesMix = Inert(
                Z.Toggle("Instances mix",
                    "Let each instance's values bleed into its neighbours', so a close cluster reads as one "
                    + "body instead of many separate copies.",
                    s.interact, v => Change(() => s.interact = v)),
                nativeSwarm ? null
                    : "Does nothing on this shape: instances are evaluated independently and unioned. Only a "
                    + "generator that brings its own swarm path can let them mix.");

            box.Add(Z.HGroup(
                instancesMix,
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
                Z.MiniRadio((int)s.shape, ShaperWords.Names(typeof(ShaperSwarmShape)),
                    "None leaves every instance on the node's own position, moved only by the jitter above.",
                    v => { Change(() => s.shape = (ShaperSwarmShape)v); Rebuild(); }, wrap: true)));

            if (s.shape == ShaperSwarmShape.None) { parent.Add(box); return; }

            bool line = s.shape == ShaperSwarmShape.Line;
            bool path = !line && s.spawnMode == ShaperSwarmSpawnMode.Path;

            // Built as one argument list rather than by Add-ing afterwards: ZuiHGroup spaces its children in
            // its constructor, so a child added later would sit flush against its neighbour.
            box.Add(Z.HGroup(
                line ? null : Z.Field("Spawn", "Whether instances fill the figure or ride its outline.",
                    Z.Segmented((int)s.spawnMode, ShaperWords.Names(typeof(ShaperSwarmSpawnMode)),
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

            // T-0265 — "Spawn centre", not "Centre offset": this moves the ARRANGEMENT the instances sit on,
            // not the shape, and a pad labelled with the word "offset" beside the Shape card's own Translate
            // read as a second way to move the shape. There is one of those and it is Position > Translate.
            box.Add(Val2D("Spawn centre",
                "Where the figure the instances are arranged on sits, relative to the node's own position, in "
                + "canvas pixels. This moves the ARRANGEMENT, not the shape — to move the shape itself use "
                + "Translate in the Shape card's Position box.",
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
                    Z.MiniRadio((int)s.orient, ShaperWords.Names(typeof(ShaperSwarmOrient)),
                        path ? "“Away from centre” turns each instance outward; “Along the path” turns it "
                               + "along the outline it sits on."
                             : "“Away from centre” turns each instance outward. “Along the path” needs a Path "
                               + "spawn, and turns away from the centre here.",
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
                    Z.MiniRadio((int)s.timing, ShaperWords.Names(typeof(ShaperSwarmTiming)),
                        "“Spread out” keeps every instance alive throughout; the other two give each one a "
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
            var hostLayer = CurrentLayer;   // T-0204 — the source's LayerStartFrame/LayerEndFrame below

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

            // T-0254 — the Reason row is GONE. §6.2's classification (why this generator bypasses the shape/fill
            // split) is a fact about the SOURCE TYPE, not a per-document dial, and printing `NotYetSplit` — a
            // note-to-self about technical debt — as an authoring control read as "an engineering annotation,
            // not an authoring control" (analysis appendix 3 §4.2). It still exists for a compliance pass to
            // check (PyreCompositeCatalogEntry.reason / ShaperCompositeSourceInfoAttribute.Reason), just never
            // drawn here. T-0191's earlier removal of the Half extent X/Y and Bake W/H dials for the same "not
            // an authored fact" reason is the precedent this follows.

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
                    // T-0204 — the SELECTED layer's own Lifetime, in document-frame space, -1 sentinel kept as
                    // -1 rather than resolved here: a source that mirrors it (a hosted Pyre layer's Life
                    // window) is expected to treat -1 the same way ShaperLayer's own resolver does ("the last
                    // frame", tracking frameCount), not freeze today's frameCount into a concrete number.
                    LayerStartFrame = hostLayer != null ? hostLayer.startFrame : 0,
                    LayerEndFrame = hostLayer != null ? hostLayer.endFrame : -1,
                    Change = Change,
                    // T-0201 — every dial on a hosted Pyre layer (PyreShapeCards' Reach/Spread/Auto Exposure
                    // among them) reports its edit through Touch. It used to dirty the asset and re-read the
                    // frame cache, which still held the pre-edit frame, so the whole hosted-layer panel looked
                    // inert until an unrelated edit flushed the cache.
                    Touch = AfterEdit,
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
                // T-0201 — the generators' own dials (Orb, Plasma Bloom, ArcBurst, Fire, Fireball…) are drawn
                // straight off the generator object, so this hook is the ONLY thing standing between such an
                // edit and the preview. Dirty-and-refresh alone re-served the cached frame, which is why the
                // owner could take every Orb dial to its extreme and see nothing move.
                OnChanged = AfterEdit,
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
                    row.Add(Z.Text(ShaperWords.Of(child.mode), ZuiText.Subtle,
                        "How this member combines: " + ShaperWords.Of(child.mode).ToLowerInvariant() + "."));
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

        /// <summary>
        /// T-0258 — the ex-Lighting SECTION, now a folded box under the selected layer's own row, beside the
        /// Height and Mask boxes that moved there in T-0187/T-0204, and gone from the toggle bar. It edits
        /// <c>ShaperLayer.response</c> (ShaperLightRig.cs:352) — a LAYER property, so the layer row is where it
        /// belongs. Only called while the row's "Lighting" toggle is on, which is the response's own
        /// <c>receiveLighting</c> flag: the card already refused to draw a single dial while that was off, so
        /// the flag was always this card's own absence rule and now says so once instead of three times (a bar
        /// segment, a section header and an in-card toggle). State key unchanged.
        /// </summary>
        void BuildLightingBox(VisualElement parent, ShaperLayer layer)
        {
            var r = layer.response;
            if (r == null || !r.receiveLighting) return;

            var box = Z.BoxKeyed("Lighting",
                "How this LAYER responds to the document's light rig. Turn it off with the “Lighting” toggle "
                + "on the layer's own row above.",
                "shaper.window.response", "sun");

            // T-0257 — THE CAST/RECEIVE SHADOW TOGGLES ARE GONE. LR-4.5 is explicit that they are "authored,
            // serialized and persisted; changes no pixel", and BC-2.3 explains they are not merely unscheduled
            // but unimplementable on v1's one straight-down ray. A control that can never move a pixel is not
            // a limitation to declare, it is a promise to withdraw — the two greyed toggles it would otherwise
            // become would still say "shadows exist here, just not yet". The FIELDS stay on
            // ShaperLightResponse (castShadows/receiveShadows, ShaperLightRig.cs:274-277) exactly as LR-4.5
            // asks, so a document that authored them keeps the author's intent for the day shadows land; this
            // window simply no longer reads or offers them, and ShaperLightRig.ShadowsNotComputed stays as the
            // sentence for whoever brings the controls back.

            // T-0257 — with no enabled light in the rig, ShaperLightCompiler.CompileResponse forces this
            // layer's `receive` to 0 (ShaperLightCompiler.cs:399) and every dial below multiplies a term that
            // is not computed. The one exemption is a Solid, which keeps the built-in key light AND the
            // layer's own response (ShaperLightCompiler.cs:544-556) — so a layer holding one has live dials
            // and must not be greyed. Both halves of that are asked here rather than assumed.
            // T-0265 — a layer whose every shape is a COMPOSITE never reaches the light law at all: the
            // generator hands the pipeline a finished, already-coloured raster, and all eleven dials below
            // move 0 pixels on such a layer (measured on Arc Burst). That is a stronger reason than the empty
            // rig below it, so it is asked first.
            string ownPicture = EveryLeafIs(layer.root, true, false)
                ? "This layer's shape is a generator that paints its own finished picture, so the light rig "
                + "never touches it and this dial does nothing (measured: 0 pixels)."
                : null;

            string unlit = ownPicture ?? (DocumentHasEnabledLight() || LayerHasSolid(layer.root) ? null
                : "The rig has no enabled light, so this layer renders unlit and this dial does nothing. Add "
                + "a light in the Lights section.");

            // Rim power shapes a term rim STRENGTH scales to nothing, so at strength 0 it is inert on its own
            // account, whatever the rig holds. An animated strength is never declared inert — see
            // DialAlwaysZero.
            // T-0265 — "Follow the surface" with NO HEIGHT STAGE reports the same flat direction Constant
            // does, so the choice, the rim and the specular all move 0 pixels on such a layer (measured on a
            // Star). This is the shape of complaint the owner has raised twice — a dial that does nothing
            // until an unrelated switch elsewhere is on — so the switch is named on the control.
            string flatSurface = r.normalKind == ShaperNormalKind.Profile && layer.height == null
                ? "Normals are on “Follow the surface” but this layer has no Height stage, so the surface is "
                + "flat and reports one direction everywhere. Add Height on the layer's own row first."
                : null;

            string noRim = unlit ?? flatSurface ?? (DialAlwaysZero(r.rimStrength)
                ? "Rim strength is 0, so there is no rim light for this to shape. Raise Rim strength first."
                : null);

            box.Add(Z.HGroup(
                InertVal("Intensity ×", "Scales the rig's effect on this layer.", unlit,
                    r.intensityScale, 0f, 3f),
                InertVal("Rim strength", "How strong the rim light is.", unlit ?? flatSurface,
                    r.rimStrength, 0f, 2f),
                InertVal("Rim power", "How tightly the rim light hugs the silhouette.", noRim,
                    r.rimPower, 0.5f, 8f),
                InertVal("Specular", "How strong the specular highlight is.", unlit ?? flatSurface,
                    r.specular, 0f, 1f),
                InertVal("Spec power", "How tight the specular highlight is.", unlit ?? flatSurface,
                    r.specularPower, 1f, 128f),
                Inert(Z.Field("Spec tint", "Tints the specular highlight.",
                    Z.Color(r.specularTint, "Tints the specular highlight.",
                        c => Change(() => r.specularTint = c), 90f)), unlit ?? flatSurface)));

            // T-0265 — a Solid writes its own analytic surface normal over its slab (LR-6.1), so choosing where
            // the layer's normals come from decides nothing for it: the choice and the Direction row below it
            // both move 0 pixels on a solids-only layer (measured on Pyramid and Orb).
            // The CHOICE itself is dead whichever way it is set while there is no height stage: with nothing
            // extruded, "Follow the surface" resolves to the same flat direction "Flat" reports (measured on a
            // Star: switching between them moves 0 pixels).
            string noRelief = layer.height == null
                ? "This layer has no Height stage, so there is no relief to follow and both settings report "
                + "the same flat direction. Add Height on the layer's own row first."
                : null;

            string ownNormals = unlit ?? noRelief ?? (EveryLeafIs(layer.root, true, true)
                ? "This layer's shape publishes its own surface directions, so this choice changes nothing "
                + "for it (measured: 0 pixels)."
                : null);

            box.Add(Inert(Z.Field("Normals", "Where this layer's surface directions come from.",
                Z.Segmented((int)r.normalKind, ShaperWords.Names(typeof(ShaperNormalKind)),
                    "“Flat” uses one authored direction for the whole layer; “Follow the surface” uses the "
                    + "extrusion/bevel profile's own analytic normal, which needs a height stage to produce "
                    + "any relief.",
                    // T-0258 — refreshes the pane this box lives in rather than the whole window: the
                    // Direction row below appears/disappears with this choice, and a full Rebuild() here is
                    // what drops ZuiSectionToggleBar's solo/quick-view state (T-0197).
                    v => { Change(() => r.normalKind = (ShaperNormalKind)v); RefreshSelectedLayerCards(); })),
                ownNormals));

            // INVENTORY GAP CLOSED — normalConstant (ShaperLightRig.cs:295) is the ONLY parameter of the
            // DEFAULT normal path and had no UI at all, so the default lighting mode was unauthorable. Drawn
            // only for Constant, since Profile ignores it. X/Y is a pad (a direction is a spatial value); Z is
            // its own dial because a pad cannot express three axes.
            if (r.normalKind == ShaperNormalKind.Constant)
            {
                box.Add(Z.HGroup(
                    Inert(Z.Field("Direction XY", "The surface direction this layer reports, X and Y.",
                        Z.Pad(new Vector2(r.normalConstant.x, r.normalConstant.y),
                            new Rect(-1f, -1f, 2f, 2f),
                            "The surface direction this layer reports, X and Y.",
                            v => Change(() =>
                                r.normalConstant = new Vector3(v.x, v.y, r.normalConstant.z)))), ownNormals),
                    Inert(Dial("Direction Z", "The surface direction's Z. 1 faces the viewer.",
                        r.normalConstant.z, -1f, 1f,
                        v => r.normalConstant = new Vector3(r.normalConstant.x, r.normalConstant.y, v)),
                        ownNormals)));
            }

            parent.Add(box);
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
        //
        // T-0204 — the "no stage yet" placeholder card is GONE: BuildLayersSection now only calls this once
        // its own "Height" toggle has already put a stage on the layer, so a layer with none simply shows no
        // card at all rather than a whole box whose only content was an Add button — "Both mask and height
        // create a whole section each without being used" (owner). layer.height is asserted non-null on entry.
        void BuildHeightSection(VisualElement root, ShaperLayer layer)
        {
            string key = "shaper.window.height." + document.layers.IndexOf(layer);
            var h = layer.height ?? (layer.height = NewHeightStage());
            // T-0257 — the tooltip now states the OTHER switch this stage depends on. A height stage feeds the
            // light only through the Profile surface-direction provider, and the layer's Normals default to
            // Flat (ShaperLightRig.cs:320), so an extruded layer whose Normals are still Flat is shaded as if
            // it were not extruded at all — the relief is real (it reorders in depth and it publishes the
            // Height quantity for a ramp or a mask) but it catches no highlight.
            var box = Z.BoxKeyed("Height",
                "Extrude this LAYER's silhouette into relief. For the relief to catch light as well as reorder "
                + "in depth, set the Lighting card's Normals to “Follow the surface” — with Normals on “Flat” "
                + "the layer is shaded as though it were not extruded.", key, "mountains");
            if (s_layerCardDefaultedClosed.Add(key)) box.IsOpen = false;
            box.SetHeaderSuffix(() => ": " + ShaperWords.Of(h.technique));
            // T-0257 — "Technique" is the engineer's word for the shape of the extrusion.
            box.Add(Inert(Z.Field("Profile", "How the silhouette is raised.",
                Z.MiniRadio((int)h.technique, ShaperWords.Names(typeof(ShaperExtrusionTechnique)),
                    "Flat leaves it unraised; the others differ in how the surface climbs from edge to centre.",
                    v => { Change(() => h.technique = (ShaperExtrusionTechnique)v); Rebuild(); }, wrap: true)),
                NoHeightStageReason(layer.root)));

            // T-0257 — SIX DIALS BEHIND ONE. Every profile and bevel dial here is multiplied by the stage's
            // body, `max(0, depth)` (ShaperHeightOp.body, ShaperHeight.cs:145), so at depth 0 none of them can
            // move a pixel. That is the single largest "I turned it and nothing happened" in this window, and
            // it is now stated on each of them rather than left to be discovered.
            // T-0265 — the stage reads each owner's own EDGE DISTANCE, which a Solid and a Composite never
            // publish (they replace the shape stage outright), so on a layer holding only those every dial
            // here — the profile included — moves 0 pixels. Asked before the depth gate because it is the
            // stronger reason: raising Depth would not help.
            string noShape = NoHeightStageReason(layer.root);

            string noDepth = noShape ?? (DialAlwaysZero(h.depth)
                ? "Raise is 0, so this layer is not extruded at all and this dial has nothing to shape. Raise "
                + "it above 0 first."
                : null);

            // T-0265 — AND WHICH PROFILE IS CHOSEN. Measured, one technique at a time: Angle moves the picture
            // on Linear only, Curve on Dome and Round only, Taper on Taper and Pyramid only. A dial that
            // belongs to another profile is not "not yet"; it is not this profile's dial, and saying so is the
            // difference between an author reading the card and an author turning a slider that cannot answer.
            string forProfile(string mine, params ShaperExtrusionTechnique[] owners)
            {
                if (noDepth != null) return noDepth;
                foreach (var t in owners) if (h.technique == t) return null;
                return mine + " shapes the " + string.Join(" and ", System.Array.ConvertAll(owners, t => ShaperWords.Of(t)))
                     + " profile" + (owners.Length > 1 ? "s" : "") + ", and this layer is set to "
                     + ShaperWords.Of(h.technique) + " — so it changes nothing here (measured: 0 pixels).";
            }

            box.Add(Z.HGroup(
                // One of the three unrelated "Depth"s this window used to show, and the one the engine's own
                // field doc warns is thickness rather than a Z position (ShaperHeight.cs:85-95).
                InertVal("Raise", "How far the surface is raised, in canvas pixels.", noShape, h.depth, 0f, 64f),
                InertVal("Angle", "The wall angle of the extrusion, in degrees.",
                    forProfile("Angle", ShaperExtrusionTechnique.Linear),
                    h.angle, 0f, 90f, decimals: 0),
                // Steps moved NOTHING on any profile, Stepped included (measured across all seven): the
                // terrace count reaches the compiled op and the op's terraces are not read. Reported rather
                // than papered over — this is an engine gap, not a profile that ignores it.
                InertVal("Steps", "How many discrete steps a stepped profile uses.",
                    noDepth ?? "The step count does not reach the picture on any profile today (measured: 0 "
                             + "pixels on all seven, Stepped included). Reported to the engine.",
                    h.steps, 1f, 32f, decimals: 0),
                InertVal("Curve", "How the climb is shaped between edge and centre.",
                    forProfile("Curve", ShaperExtrusionTechnique.Dome, ShaperExtrusionTechnique.Round),
                    h.curve, 0f, 4f),
                InertVal("Taper", "How much the surface narrows as it rises.",
                    forProfile("Taper", ShaperExtrusionTechnique.Taper, ShaperExtrusionTechnique.Pyramid),
                    h.taper, 0f, 2f)));

            box.Add(Z.Field("Bevel", "An additional bevel applied at the edge.",
                Z.MiniRadio((int)h.bevel, ShaperWords.Names(typeof(ShaperBevelTechnique)),
                    "None leaves a hard edge; the others differ in the bevel's profile.",
                    v => { Change(() => h.bevel = (ShaperBevelTechnique)v); Rebuild(); }, wrap: true)));

            if (h.bevel != ShaperBevelTechnique.None)
                box.Add(Z.HGroup(
                    InertVal("Bevel amount", "How far the bevel reaches in from the edge.", noDepth,
                        h.bevelAmount, 0f, 16f),
                    // Same gap as Steps above, measured the same way: the bevel's terrace count changes nothing
                    // on a Stepped bevel or any other.
                    InertVal("Bevel steps", "How many discrete steps a stepped bevel uses.",
                        noDepth ?? "The bevel's step count does not reach the picture on any bevel today "
                                 + "(measured: 0 pixels, Stepped included). Reported to the engine.",
                        h.bevelSteps, 1f, 16f, decimals: 0)));

            // T-0204 — RefreshSelectedLayerCards(), not Rebuild(): this button turns the layer's own Height
            // toggle back off (layer.height == null), which is exactly the transition the toggle itself makes
            // — a full window Rebuild() here would be the same ZuiSectionToggleBar solo/quick-view-dropping
            // bug T-0197 already fixed for layer selection, reintroduced through the back door.
            box.Add(Z.Button("Remove height", "Drop this layer's height stage and leave it flat.",
                () => { Change(() => layer.height = null); RefreshSelectedLayerCards(); }));
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

            // T-0204 — every one of these mutates m.sourceLayerId, i.e. whether this card is even still on
            // screen (the Mask toggle's own absence rule reads m.IsSet), so they refresh the SAME pane the
            // toggle refreshes (RefreshSelectedLayerCards) rather than the whole window: a full Rebuild() here
            // is the ZuiSectionToggleBar solo/quick-view-dropping bug T-0197 already fixed for layer selection.
            Button pick = null;
            pick = Z.Button(pickLabel, pickTip, () =>
            {
                var menu = Z.Menu(pick).Width(240f);
                menu.Item("None", "Remove this layer's mask.",
                    () => { Change(() => m.sourceLayerId = 0); RefreshSelectedLayerCards(); }, @checked: !m.IsSet);
                for (int i = 0; i < document.layers.Count; i++)
                {
                    var cand = document.layers[i];
                    if (cand == null || cand == layer) continue;
                    var captured = cand;
                    string nm = string.IsNullOrEmpty(cand.name) ? "Layer " + (i + 1) : cand.name;
                    menu.Item(nm, "Cut this layer with “" + nm + "”. " + ShaperLayerMask.SourceIsReadUnmasked,
                        // IdOf allocates the source's stable id on first reference, so it happens INSIDE the
                        // Undo scope — a Ctrl+Z takes the id back with the reference that caused it.
                        () => { Change(() => m.sourceLayerId = document.IdOf(captured)); RefreshSelectedLayerCards(); },
                        @checked: captured.id != 0 && captured.id == m.sourceLayerId);
                }
                menu.Show();
            });
            if (!hasOther && !m.IsSet) pick.SetEnabled(false);

            var pickRow = Z.HGroup(Z.Field("Mask by", pickTip, pick));
            if (missing)
                pickRow.Add(Z.Button("Clear", "Drop the reference to the deleted layer.",
                    () => { Change(() => m.sourceLayerId = 0); RefreshSelectedLayerCards(); }));
            box.Add(pickRow);

            if (m.IsSet)
            {
                box.Add(Z.HGroup(
                    // T-0257 — these three used to print Clip/Subtract/Intersect, two of which are the same
                    // words the Combine control uses for entirely different operations.
                    Z.Field("Mode", "Keep what the source covers, cut it away, or keep the lesser of the two "
                        + "— which differ only where the mask is soft.",
                        Z.Segmented((int)m.mode, ShaperWords.Names(typeof(ShaperMaskMode)),
                            "“Keep inside” keeps what the source covers, “Cut away” removes it, “Keep "
                            + "overlap” keeps the lesser of the two.",
                            v => { Change(() => m.mode = (ShaperMaskMode)v); Rebuild(); })),
                    Z.Toggle("Invert", "Read the source backwards, so it cuts where it is empty instead of "
                        + "where it is solid.", m.invert, v => Change(() => m.invert = v))));

                // Availability is the SOURCE's own answer: a layer with no height stage publishes only
                // Coverage and Edge Distance (HS-1.4), and the renderer falls back to Coverage rather than
                // reading an empty sheet and cutting the whole layer away. Declared with its reason rather
                // than hidden, the same posture ShaperSolids.InertReason takes for an inert dial.
                bool heightUnavailable = source != null && source.height == null;
                bool quantityUnavailable = heightUnavailable && m.quantity == ShaperMaskQuantity.Height;
                string qTip = "Which of the source's quantities is read as the mask. Opacity is its "
                    + "silhouette, Distance from edge ramps inward from its outline, Height needs it to be "
                    + "extruded, and Brightness reads how bright it is."
                    + (heightUnavailable ? "\n\n" + ShaperLayerMask.QuantityNotPublished : "");

                var qRow = Z.HGroup(
                    Z.Field("Quantity", qTip,
                        InertOptions(
                            Z.MiniRadio((int)m.quantity, ShaperWords.Names(typeof(ShaperMaskQuantity)), qTip,
                                v => { Change(() => m.quantity = (ShaperMaskQuantity)v); Rebuild(); },
                                wrap: true),
                            // T-0257 — the source's own answer, said on the option itself rather than only in
                            // the shared tooltip: the mask source publishes Height only when IT is extruded.
                            i => heightUnavailable && (ShaperMaskQuantity)i == ShaperMaskQuantity.Height
                                   ? ShaperLayerMask.QuantityNotPublished : null)));

                // Coverage is already 0..1, so it has no scale to set and the dial is not drawn for it —
                // a second amplitude dial on a normalized quantity is the "two dials for one quantity"
                // defect ShaperLight.range refuses by name.
                if (m.quantity != ShaperMaskQuantity.Coverage)
                    qRow.Add(Val("Fully masked at", "The source value that reads as a fully solid mask — canvas pixels "
                        + "for Height and Edge Distance, linear brightness for Luma. At 0 it becomes a hard "
                        + "test with no ramp.", m.fullAt, 0f, 64f));
                box.Add(qRow);

                if (quantityUnavailable)
                    box.Add(Z.Text("Falling back to Opacity.", ZuiText.Subtle,
                        ShaperLayerMask.QuantityNotPublished));
            }

            // T-0204 — "Draws into the picture" moved OUT of this card onto the always-visible Lifetime/Z row
            // in BuildLayersSection: it is a property of the layer being USED as a mask SOURCE by some other
            // layer, which has nothing to do with whether THIS layer has a mask of its OWN — a layer with no
            // mask card at all (this whole card is now absent for it) can still be somebody else's mask
            // source, and hiding the toggle along with an unrelated card would have taken away a real setting.

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
        // stack. So let's call them SpriteFX." Labels and tooltips only; type names, C# member names and
        // view-state keys are unchanged. (T-0258 folded the two lists into ONE card called "SpriteFX", so the
        // separate "Global SpriteFX" title is gone — see BuildEffectsSection.)
        // The add picker (ShowAddEffectMenu) was also rebuilt to match
        // Pyre's own "+ Add modifier" menu (PyreWindow.Modifiers.cs:344-378) content-for-content — a section
        // per family, an item per effect, unavailable entries greyed with their reason — but laid out as one
        // COLUMN per family instead of Pyre's flat vertical list, per the owner's separate standing feedback
        // this wave ("we have a lot of width but less height... at least 4 columns is totally acceptable").

        /// <summary>
        /// T-0258 — ONE SpriteFX card holding BOTH effect lists, where there used to be two sections
        /// ("SpriteFX" and "Global SpriteFX") competing for two bar segments. They were never two different
        /// features: a Shaper effect is one modifier, and the only thing that differs is whether it runs on
        /// this layer's own picture or on the finished one. That is now a per-entry <b>Whole picture</b>
        /// toggle which MOVES the entry between the two lists, so the difference is authored on the thing it
        /// is a fact about instead of by choosing which card to add it to.
        ///
        /// The two lists keep their own hosts inside the card, because apply ORDER is per-list: a drag is only
        /// meaningful among the entries that run at the same point, and crossing the boundary is exactly what
        /// the toggle is for.
        ///
        /// The layer half is no longer gated on <c>drillPath.Count == 0</c>. Drilling into a bag member does
        /// not change which LAYER is being edited, and every other layer-level control (Height, Mask, Lighting
        /// since this task) is drawn regardless — hiding this one while drilled made a layer's own effects look
        /// deleted.
        ///
        /// T-0184 — owner: "Effects is what is called Modifiers in Pyre — that's the same thing as the SpriteFX
        /// stack. So let's call them SpriteFX." Type names and view keys are unchanged.
        /// </summary>
        void BuildEffectsSection(VisualElement root, ShaperLayer layer)
        {
            var layerList = layer.effects;
            var docList = document.effects;
            MigrateLegacyEntries(layerList);
            MigrateLegacyEntries(docList);

            const string tooltip =
                "Effects applied to this document's picture. An entry runs on THIS layer's own picture before "
                + "it composites, unless its “Whole picture” toggle is on — then it runs on the finished "
                + "picture after every layer has composited.";

            var box = effectsSection = Z.Section("SpriteFX", tooltip, "shaper.window.layereffects",
                icon: "sparkles");
            box.SetHeaderSuffix(() =>
            {
                int n = CountEnabled(layerList) + CountEnabled(docList);
                return n > 0 ? $" ({n})" : "";
            });

            // Two hosts, one per list, so ZuiReorder's (from, to) indices stay inside the list they belong to.
            var layerHost = new VisualElement();
            for (int i = 0; i < layerList.Count; i++)
                layerHost.Add(BuildEffectCard(layerHost, layerList, i, ShaperEffectStage.PreComposite,
                                              layerList, docList));
            box.Add(layerHost);

            var docHost = new VisualElement();
            for (int i = 0; i < docList.Count; i++)
                docHost.Add(BuildEffectCard(docHost, docList, i, ShaperEffectStage.PostComposite,
                                            layerList, docList));
            box.Add(docHost);

            if (layerList.Count + docList.Count == 0)
                box.Add(Z.Text("No effects.", ZuiText.Subtle, tooltip));

            Button add = null;
            add = Z.Button("+ Add SpriteFX", tooltip, () => ShowAddEffectMenu(add, layerList, docList));
            box.Add(add);
            root.Add(box);
        }

        static int CountEnabled(List<ShaperEffectRef> list)
        {
            int n = 0;
            for (int i = 0; i < list.Count; i++) if (list[i] != null && list[i].enabled) n++;
            return n;
        }

        /// <summary>
        /// One effect card — the same shape as Pyre's modifier card (grip / enable / name / stage / ×, with the
        /// dials reflection-drawn in a foldable body), because a Shaper effect IS one of Pyre's modifiers and
        /// authoring it should not feel like a different thing in a different window.
        /// </summary>
        VisualElement BuildEffectCard(VisualElement listHost, List<ShaperEffectRef> list, int index,
                                      ShaperEffectStage stage,
                                      List<ShaperEffectRef> layerList, List<ShaperEffectRef> docList)
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

            // T-0258 — the toggle that replaced the "Global SpriteFX" section. On = this entry lives in the
            // DOCUMENT's list and runs once on the finished picture; off = it lives in this LAYER's list and
            // runs on the layer alone. Flipping it MOVES the entry between the two lists, which is the whole
            // of what the two sections used to mean, authored on the entry itself.
            //
            // Declared inert (never hidden) when the effect cannot run at the other end: several catalog
            // entries are one-stage-only (ShaperEffectRuntime.CanRun, Runtime/PyreShaper/ShaperEffectRuntime
            // .cs:163-169), and a toggle that silently refuses is worse than one that says why.
            bool whole = stage == ShaperEffectStage.PostComposite;
            var otherStage = whole ? ShaperEffectStage.PreComposite : ShaperEffectStage.PostComposite;
            string moveReason;
            bool canMove;
            if (string.IsNullOrEmpty(entry.typeName))
            {
                canMove = false;
                moveReason = "This entry names no effect, so it cannot be moved.";
            }
            else canMove = ShaperEffectRuntime.CanRun(entry.typeName, otherStage, out moveReason);
            header.Add(Inert(
                Z.Toggle("Whole picture",
                    "On: run this once on the finished picture, after every layer has composited. Off: run it "
                    + "on this layer's own picture, before it composites. Switching moves the entry between "
                    + "the layer's list and the document's.",
                    whole,
                    v =>
                    {
                        var src = v ? layerList : docList;
                        var dst = v ? docList : layerList;
                        Change(() =>
                        {
                            int at = src.IndexOf(entry);
                            if (at >= 0) src.RemoveAt(at);
                            dst.Add(entry);
                        });
                        Rebuild();
                    }),
                canMove ? null
                    : "Cannot move: " + (moveReason ?? "this effect only runs where it is.")));

            // T-0257 — "pre-composite"/"post-composite" is the pipeline's vocabulary for a fact the author
            // reads as "does this run on my layer or on the finished picture".
            header.Add(Z.Text(ok ? (stage == ShaperEffectStage.PreComposite ? "Before blending" : "After blending")
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
            OnChanged = AfterEdit,
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
        ///
        /// T-0258 — ONE picker for the merged card, so every catalog entry is reachable from one button. An
        /// entry lands in the list where it can actually run: its own default stage when that is available,
        /// the other one when it is not, and greyed with the reason when NEITHER is. Before the merge, an
        /// effect that only runs post-composite was greyed in the layer card's picker and addable only from
        /// the other section — with one card, keeping that rule would have made it unreachable.
        /// </summary>
        void ShowAddEffectMenu(VisualElement anchor, List<ShaperEffectRef> layerList,
                               List<ShaperEffectRef> docList)
        {
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
                        // Its own default stage first — that is the stage the catalog says the effect is FOR,
                        // so an entry that can run at both lands where its author meant it to.
                        var wanted = e.defaultStage;
                        bool ok = ShaperEffectRuntime.CanRun(in ShaperEffectCatalog.All[i], wanted,
                                                             out string reason);
                        if (!ok)
                        {
                            wanted = wanted == ShaperEffectStage.PreComposite
                                ? ShaperEffectStage.PostComposite : ShaperEffectStage.PreComposite;
                            ok = ShaperEffectRuntime.CanRun(in ShaperEffectCatalog.All[i], wanted, out reason);
                        }
                        bool whole = wanted == ShaperEffectStage.PostComposite;
                        var target = whole ? docList : layerList;
                        string typeName = e.typeName;
                        string lands = whole
                            ? "Will run on the finished picture, after every layer has composited — it arrives "
                              + "with “Whole picture” on."
                            : "Will run on this layer's own picture, before it composites.";
                        col.Add(BuildEffectMenuItem(typeName, ok ? lands : "Unavailable — " + reason, ok,
                            () =>
                            {
                                var made = ShaperEffectRuntime.Create(typeName);
                                if (made == null) return;
                                Change(() => target.Add(new ShaperEffectRef(typeName, made)));
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
