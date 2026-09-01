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
//   • THERE IS NO AUTHORED EFFECT STACK. ShaperEffectCatalog (PyreShaper/ShaperEffectContract.cs:118) is a
//     static classification of 41 effects; no node or layer holds a list of them. So the Effects card here
//     is a read-only browser of what the catalog says and whether each entry is usable, NOT an editor.
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
        ZuiSection fillSection, borderSection, modifiersSection, swarmSection,
                   compositeSection, childrenSection, responseSection, heightSection, effectsSection;

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

            // Absence rule: a card that cannot apply to this node kind is not drawn at all.
            if (node.kind == ShaperNodeKind.Composite) BuildCompositeSection(root, node);
            if (node.kind == ShaperNodeKind.Bag) BuildChildrenSection(root, node);

            BuildFillSection(root, node);
            BuildBorderSection(root, node);
            BuildModifiersSection(root, node);
            BuildSwarmSection(root, node);

            // LAYER-level cards. These bind to ShaperLayer, not to the node — the mock had them on the node
            // and that was corrected. Shown only at the layer root, because a bag member has no layer of its
            // own to author and drawing them while drilled in would be a lie about what is being edited.
            if (drillPath.Count == 0)
            {
                BuildResponseSection(root, layer);
                BuildHeightSection(root, layer);
            }

            BuildEffectsSection(root, layer);
        }

        /// The sections this file adds, for the shell's toggle bar. Null entries are skipped by the bar.
        internal (string, ZuiSection)[] SectionBarEntries() => new[]
        {
            ("Fill", fillSection), ("Border", borderSection), ("Modifiers", modifiersSection),
            ("Swarm", swarmSection), ("Generator", compositeSection), ("Children", childrenSection),
            ("Lighting", responseSection), ("Height", heightSection), ("Effects", effectsSection),
        };

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
                rows.Add(Val("Centre X", "Where the gradient radiates from, in canvas pixels.",
                    f.gradientCentreX, -128f, 128f));
                rows.Add(Val("Centre Y", "Where the gradient radiates from, in canvas pixels.",
                    f.gradientCentreY, -128f, 128f));
                rows.Add(Val("Size", "How far the gradient reaches before it repeats or clamps.",
                    f.gradientSize, 0f, 256f));
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
                if (document != null) Undo.RecordObject(document, "Edit Shaper Document");
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
                        () => { Change(() => node.border = new ShaperBorderDef()); Rebuild(); })));
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
                        () => { Change(() => b.fill = new ShaperFillDef()); Rebuild(); })));
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

        // ── Shape modifiers: Blend / Sweep / Shell ───────────────────────────────────────────────────────

        // All plain floats in the engine (ShaperNode.cs:24-69), so all Dial, with the [Range] bounds taken
        // from the declarations rather than invented.
        void BuildModifiersSection(VisualElement root, ShaperNode node)
        {
            var box = modifiersSection = Z.Section("Modifiers",
                "How this node folds into its parent, and the sweep/shell applied to its own shape.",
                "shaper.window.modifiers", icon: "sliders-horizontal");

            // Blend governs how this node combines with its siblings, so the combine mode belongs with it.
            box.Add(Z.HGroup(
                Z.Field("Combine", "How this node combines with what is already there.",
                    Z.Segmented((int)node.mode, Enum.GetNames(typeof(ShaperCombineMode)),
                        "Add unions; Subtract carves; Intersect keeps only the overlap.",
                        v => Change(() => node.mode = (ShaperCombineMode)v))),
                Dial("Blend width", "How far the join between this node and its neighbours is softened, "
                    + "in canvas pixels. 0 is a hard edge.",
                    node.blend.width, 0f, 32f, v => node.blend.width = v),
                Dial("Sharpness", "How abruptly the softened join falls off.",
                    node.blend.sharpness, 0f, 1f, v => node.blend.sharpness = v),
                Dial("Carve strength", "How strongly a Subtract carves. 1 removes fully.",
                    node.blend.carveStrength, 0f, 1f, v => node.blend.carveStrength = v)));

            var sweep = Z.BoxKeyed("Sweep", "Keep only an angular or fractional slice of the shape.",
                "shaper.window.sweep");
            sweep.Add(Z.HGroup(
                Z.Toggle("Enabled", "Apply the sweep.", node.sweep.enabled,
                    v => Change(() => node.sweep.enabled = v)),
                Dial("Start", "Where the kept slice begins, in degrees.",
                    node.sweep.startDegrees, 0f, 360f, v => node.sweep.startDegrees = v, decimals: 0),
                Dial("Extent", "How much of the shape is kept, in degrees.",
                    node.sweep.extentDegrees, 0f, 360f, v => node.sweep.extentDegrees = v, decimals: 0),
                Dial("Start ƒ", "Where the kept slice begins as a fraction of the shape.",
                    node.sweep.startFraction, 0f, 1f, v => node.sweep.startFraction = v),
                Dial("Extent ƒ", "How much is kept as a fraction of the shape.",
                    node.sweep.extentFraction, 0f, 1f, v => node.sweep.extentFraction = v)));
            box.Add(sweep);

            var shell = Z.BoxKeyed("Shell", "Hollow the shape into a shell of a given thickness.",
                "shaper.window.shell");
            shell.Add(Z.HGroup(
                Z.Toggle("Enabled", "Hollow this shape.", node.shell.enabled,
                    v => Change(() => node.shell.enabled = v)),
                Dial("Thickness", "How thick the remaining shell is, in canvas pixels.",
                    node.shell.thickness, 0f, 32f, v => node.shell.thickness = v),
                Z.Field("Alignment", "Which side of the surface the shell is taken from.",
                    Z.Segmented((int)node.shell.alignment, Enum.GetNames(typeof(ShaperShellAlignment)),
                        "Centred straddles the surface; Inward keeps material inside it; Outward outside.",
                        v => Change(() => node.shell.alignment = (ShaperShellAlignment)v)))));
            box.Add(shell);

            root.Add(box);
        }

        // ── Swarm ────────────────────────────────────────────────────────────────────────────────────────

        void BuildSwarmSection(VisualElement root, ShaperNode node)
        {
            var s = node.swarm;
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
                Dial("Rotation jitter", "How much each instance's rotation varies, in degrees.",
                    s.rotationJitterDegrees, 0f, 180f, v => s.rotationJitterDegrees = v, decimals: 0),
                Dial("Scale jitter", "How much each instance's scale varies.",
                    s.scaleJitter, 0f, 1f, v => s.scaleJitter = v),
                Dial("Lifetime stagger", "How much each instance's clock is offset from the others.",
                    s.lifetimeStagger, 0f, 1f, v => s.lifetimeStagger = v)));

            // A spatial pair is a pad, never two packed 1D fields — aiming a 2D value with two sliders is the
            // ergonomics problem the rule exists for.
            box.Add(Z.Field("Position jitter", "How far each instance can be displaced, in canvas pixels.",
                Z.Pad(s.positionJitter, new Rect(0f, 0f, 128f, 128f),
                    "How far each instance can be displaced, in canvas pixels.",
                    v => Change(() => s.positionJitter = v))));

            box.Add(Z.HGroup(
                Z.Toggle("Interact", "Let instances affect one another rather than being independent.",
                    s.interact, v => Change(() => s.interact = v)),
                Dial("Merge width", "How far neighbouring instances blend into each other.",
                    s.merge.width, 0f, 32f, v => s.merge.width = v),
                Dial("Merge sharpness", "How abruptly that blend falls off.",
                    s.merge.sharpness, 0f, 1f, v => s.merge.sharpness = v)));

            root.Add(box);
        }

        // ── Composite generator ──────────────────────────────────────────────────────────────────────────

        // The structural one. ShaperCompositeDef.source is [SerializeReference] IShaperCompositeSource holding
        // a PyreForm (ShaperCompositeDef.cs:95, PyreFormCompositeSource.cs:31-33) — an OBJECT, not an index.
        // The mock's `int compositeGeneratorIndex` does not transfer at all: picking a generator here means
        // INSTANTIATING a form and assigning it, and the per-generator dials are whatever that form declares.
        // Those declarations are enormous (ArcBurstForm alone declares 187 authored fields), so they are
        // reflected, never hand-listed — a hand-written list could not be kept correct and would silently
        // expose a fraction of the engine.
        void BuildCompositeSection(VisualElement root, ShaperNode node)
        {
            var c = node.composite;
            var box = compositeSection = Z.Section("Generator",
                "The imported effect this node hosts, and its own dials.",
                "shaper.window.composite", icon: "sparkle");

            var src = c.source as PyreFormCompositeSource;
            var form = src?.form;
            box.SetHeaderSuffix(() => " — " + (c.source?.SourceLabel ?? "(none)"));
            box.SetHeaderMenu("caret-down", "Choose the generator (or right-click the title).",
                anchor => ShowGeneratorMenu(anchor, node));

            if (form == null)
            {
                box.Add(Z.Field("Generator", "No generator assigned yet.",
                    Z.Button("Choose generator…", "Pick which imported effect this node hosts.",
                        () => ShowGeneratorMenu(box, node))));
                root.Add(box);
                return;
            }

            box.Add(Z.HGroup(
                Z.Field("Reason", "Why this is still a composite rather than split into primitives — a "
                    + "structural fact the audit checks, not an authored dial.",
                    Z.Text(c.reason.ToString(), ZuiText.Body, c.reason.ToString())),
                Dial("Half extent X", "Half the width of the box this generator bakes into, in canvas units.",
                    c.halfExtentX, 8f, 256f, v => c.halfExtentX = v, decimals: 0),
                Dial("Half extent Y", "Half the height of the box this generator bakes into, in canvas units.",
                    c.halfExtentY, 8f, 256f, v => c.halfExtentY = v, decimals: 0),
                Dial("Bake W", "Bake resolution in texels, independent of the canvas resolution.",
                    c.bakeWidth, 16f, 512f, v => c.bakeWidth = Mathf.RoundToInt(v), decimals: 0),
                Dial("Bake H", "Bake resolution in texels, independent of the canvas resolution.",
                    c.bakeHeight, 16f, 512f, v => c.bakeHeight = Mathf.RoundToInt(v), decimals: 0)));

            // The generator's own dials. Folded into a keyed box because the biggest forms declare well over
            // a hundred fields and an unfolded dump would bury every other card on the page. FlowFields packs
            // them into shared rows rather than one control per row, which is the same row-packing rule the
            // hand-built cards follow.
            var dials = Z.BoxKeyed(form.DisplayName + " dials",
                "Every dial this generator declares, read straight off the form — so it cannot drift out of "
                + "date as the generator changes. A hosted form's dials resolve to their STATIC value when "
                + "rendered through this path, so animating one has no effect here today.",
                "shaper.window.composite.dials");
            var host = new VisualElement();
            ZuiReflect.FlowFields(host, form, new ZuiReflect.Options
            {
                OnBeforeChange = () =>
                {
                    if (document != null) Undo.RecordObject(document, "Edit Shaper Document");
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

            root.Add(box);
        }

        /// Every concrete PyreForm in the domain, grouped by the Group its own PyreFormInfoAttribute
        /// declares. Read from the attribute rather than a hardcoded catalog so a newly added form appears
        /// here automatically instead of silently missing — the same reason the shell reads enum names from
        /// the enum. Searchable, because there are enough of them that scanning is worse than typing.
        void ShowGeneratorMenu(VisualElement anchor, ShaperNode node)
        {
            var menu = Z.Menu(anchor).Width(300f).Search("Search generators…");

            var types = TypeCache.GetTypesDerivedFrom<PyreForm>()
                .Where(t => !t.IsAbstract && t.GetConstructor(Type.EmptyTypes) != null)
                .Select(t => (type: t, info: t.GetCustomAttribute<PyreFormInfoAttribute>()))
                .Select(x => (x.type, name: x.info?.DisplayName ?? x.type.Name,
                              group: x.info?.Group ?? "Forms"))
                .OrderBy(x => x.group).ThenBy(x => x.name)
                .ToArray();

            string lastGroup = null;
            var current = (node.composite.source as PyreFormCompositeSource)?.form?.GetType();
            foreach (var t in types)
            {
                if (t.group != lastGroup) { menu.Section(t.group); lastGroup = t.group; }
                var type = t.type;
                menu.Item(t.name, null, () => AssignGenerator(node, type), current == type);
            }
            menu.Show();
        }

        /// Assign a generator by instantiating the form and wrapping it in the source the engine expects.
        /// When the form matches a PyreCompositeCatalog entry by display name, its declared reason/note come
        /// across too — that is authored classification the catalog already owns, and re-deriving it here
        /// would be a second source of truth.
        void AssignGenerator(ShaperNode node, Type formType)
        {
            Change(() =>
            {
                var form = (PyreForm)Activator.CreateInstance(formType);
                var entry = PyreCompositeCatalog.All.FirstOrDefault(e => e.displayName == form.DisplayName);
                var built = entry.displayName != null
                    ? PyreCompositeCatalog.Build(form, entry,
                        node.composite.halfExtentX, node.composite.halfExtentY,
                        node.composite.bakeWidth, node.composite.bakeHeight)
                    : new ShaperCompositeDef
                    {
                        source = new PyreFormCompositeSource { form = form },
                        reason = ShaperCompositeReason.NotYetSplit,
                        halfExtentX = node.composite.halfExtentX,
                        halfExtentY = node.composite.halfExtentY,
                        bakeWidth = node.composite.bakeWidth,
                        bakeHeight = node.composite.bakeHeight,
                    };
                node.composite = built;
            });
            Rebuild();
        }

        // ── Bag children ─────────────────────────────────────────────────────────────────────────────────

        void BuildChildrenSection(VisualElement root, ShaperNode node)
        {
            var box = childrenSection = Z.Section("Children",
                "The members this bag combines. Open one to author it.",
                "shaper.window.children", icon: "layers");

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
            root.Add(box);
        }

        // ── Light response (LAYER) ───────────────────────────────────────────────────────────────────────

        void BuildResponseSection(VisualElement root, ShaperLayer layer)
        {
            var r = layer.response;
            if (r == null) return;

            var box = responseSection = Z.Section("Lighting",
                "How this LAYER responds to the document's light rig.",
                "shaper.window.response", icon: "sun");

            box.Add(Z.HGroup(
                Z.Toggle("Receive lighting", "Let the rig light this layer at all.",
                    r.receiveLighting, v => { Change(() => r.receiveLighting = v); Rebuild(); }),
                Z.Toggle("Cast shadows", "Let this layer cast shadows.",
                    r.castShadows, v => Change(() => r.castShadows = v)),
                Z.Toggle("Receive shadows", "Let this layer be shadowed.",
                    r.receiveShadows, v => Change(() => r.receiveShadows = v))));

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
        void BuildHeightSection(VisualElement root, ShaperLayer layer)
        {
            var box = heightSection = Z.Section("Height",
                "Extrude this LAYER's silhouette into relief.", "shaper.window.height", icon: "mountains");

            if (layer.height == null)
            {
                box.Add(Z.Field("Height", "This layer has no height stage, so it stays flat.",
                    Z.Button("Add height", "Give this layer an extrusion stage.",
                        () => { Change(() => layer.height = new ShaperHeightDef()); Rebuild(); })));
                root.Add(box);
                return;
            }

            var h = layer.height;
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

        // ── Effects catalog (READ-ONLY) ──────────────────────────────────────────────────────────────────

        // Deliberately NOT an authoring card: nothing in the engine holds a list of effects to author. What
        // exists is ShaperEffectCatalog — 41 entries classified by stage, portability and the sheets they
        // need. Surfacing availability here is the point: an effect that cannot run says WHY, which is the
        // engine's own "declare, don't hide" posture (the same rule ShaperSolids.InertReason follows).
        void BuildEffectsSection(VisualElement root, ShaperLayer layer)
        {
            var box = effectsSection = Z.Section("Effects",
                "Which imported effects can run against what this document publishes. Read-only: the engine "
                + "has no authored effect stack yet.", "shaper.window.effects", icon: "sparkles");
            box.IsOpen = false;   // informational, so it does not push the authoring cards down by default

            // Which sheets are published decides which effects are usable. A layer with a height stage
            // publishes height as well — that is the difference between the two shipped sets, so it is
            // derived from the layer rather than assumed.
            var published = layer.height != null
                ? ShaperQuantitySet.ShapeEngineWithHeight
                : ShaperQuantitySet.ShippedShapeEngine;

            box.Add(Z.Field("Published sheets",
                "What this layer publishes for effects to read. A height stage publishes height too.",
                Z.Text(published.ToString(), ZuiText.Body, published.ToString())));

            var listHost = new VisualElement();
            foreach (var e in ShaperEffectCatalog.All)
            {
                bool ok = ShaperEffectCatalog.IsAvailable(in e, published, out string reason);
                var row = new VisualElement();
                row.AddToClassList("zui-row");
                row.Add(Z.Text(e.typeName, ZuiText.Body,
                    ok ? $"{e.stageKind} · runs at {e.defaultStage}"
                       : "Unavailable: " + reason));
                row.Add(Z.Flexible());
                row.Add(Z.Text(e.stageKind, ZuiText.Subtle, "Which stage this effect belongs to."));
                row.Add(Z.Text(e.bothStagesPossible ? "either stage" : e.defaultStage.ToString(),
                    ZuiText.Subtle,
                    e.bothStagesPossible
                        ? "Running this at the other stage is structurally legal too."
                        : "This effect only runs at " + e.defaultStage + "."));
                if (!ok)
                {
                    // Greyed WITH its reason, never hidden — an absent row would read as "this effect does
                    // not exist" when the truth is "it exists and cannot run here, for this reason".
                    row.SetEnabled(false);
                    row.tooltip = "Unavailable: " + reason;
                }
                listHost.Add(row);
            }
            box.Add(listHost);
            root.Add(box);
        }
    }
}
