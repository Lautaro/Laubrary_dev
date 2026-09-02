using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace Laubrary.Shaper
{
    /// <summary>
    /// APPEND-ONLY: serialized as an int. T-0112 adds <see cref="Composite"/> — a leaf like
    /// <see cref="Primitive"/>, but hosting a rendered picture instead of an analytic SDF (see
    /// <see cref="ShaperCompositeDef"/>).
    ///
    /// T-0155 adds <see cref="Solid"/>. Until it existed, <see cref="ShaperSolidDef"/> was a fully built
    /// generator — six forms, ~19 authored dials, a real <c>InertReason</c> table across 6 forms × 14 dials,
    /// and T-0127's HeightField-into-surface-normal relief shading — that NO DOCUMENT COULD REFERENCE. A
    /// repo-wide search for a <c>ShaperSolidDef</c> field in <c>Runtime/</c> returned nothing; the only thing
    /// that ever compiled one was an editor audit. Finished engine work no user could reach.
    /// </summary>
    public enum ShaperNodeKind { Primitive = 0, Bag = 1, Composite = 2, Solid = 3 }

    /// <summary>
    /// The softness dials a member carries. Deliberately two separate fields rather than the reference app's
    /// single overloaded <c>viscosity</c>, which is a band half-width in canvas units for a soft add and a
    /// dimensionless fraction of the cut for a soft subtract — the same slider showing two different
    /// quantities.
    /// </summary>
    [Serializable]
    public class ShaperBlend : ISerializationCallbackReceiver
    {
        const uint FldWidth = 0x7B00_0001u, FldSharpness = 0x7B00_0002u, FldCarve = 0x7B00_0003u;

        /// <summary>Blend band half-width in <b>canvas pixels</b>, for Add and Intersect. 0 = a hard combine.</summary>
        public ZUIValue widthDial = new ZUIValue(0f);
        /// <summary>0..1, mapped to the profile exponent <c>n = pow(8, sharpness)</c>.</summary>
        public ZUIValue sharpnessDial = new ZUIValue(0.5f);
        /// <summary>0..1 for Subtract: 1 = a full hard cut, 0 = an exact no-op everywhere in the field.</summary>
        public ZUIValue carveStrengthDial = new ZUIValue(1f);

        [SerializeField, FormerlySerializedAs("width")]         float legacyWidth = 0f;
        [SerializeField, FormerlySerializedAs("sharpness")]     float legacySharpness = 0.5f;
        [SerializeField, FormerlySerializedAs("carveStrength")] float legacyCarve = 1f;
        [SerializeField] bool dialsPromoted;

        public void OnBeforeSerialize() => dialsPromoted = true;

        public void OnAfterDeserialize()
        {
            if (dialsPromoted) { EnsureDials(); return; }
            widthDial = new ZUIValue(legacyWidth);
            sharpnessDial = new ZUIValue(legacySharpness);
            carveStrengthDial = new ZUIValue(legacyCarve);
            dialsPromoted = true;
        }

        public void EnsureDials()
        {
            if (widthDial == null) widthDial = new ZUIValue(0f);
            if (sharpnessDial == null) sharpnessDial = new ZUIValue(0.5f);
            if (carveStrengthDial == null) carveStrengthDial = new ZUIValue(1f);
        }

        // Plain-number views of the three dials, for code that authors one outright rather than as an envelope.
        // They read and write the STATIC value only, which is what "set this blend to 4 pixels" means.
        public float width { get => ShaperDial.Get(widthDial); set => ShaperDial.Set(ref widthDial, value); }
        public float sharpness { get => ShaperDial.Get(sharpnessDial, 0.5f); set => ShaperDial.Set(ref sharpnessDial, value); }
        public float carveStrength { get => ShaperDial.Get(carveStrengthDial, 1f); set => ShaperDial.Set(ref carveStrengthDial, value); }

        /// <summary>The three dials sampled once at <paramref name="phase01"/>, already clamped to the ranges
        /// the fold maths requires, so no caller has to remember which of them is bounded.</summary>
        public void Sample(float phase01, uint seed, out float bandWidth, out float sharp, out float carve)
        {
            EnsureDials();
            bandWidth = Mathf.Max(0f, ShaperValue.Sample(widthDial, phase01, seed ^ FldWidth, 0f));
            sharp = Mathf.Clamp01(ShaperValue.Sample(sharpnessDial, phase01, seed ^ FldSharpness, 0.5f));
            carve = Mathf.Clamp01(ShaperValue.Sample(carveStrengthDial, phase01, seed ^ FldCarve, 1f));
        }
    }

    /// <summary>
    /// Sweep — an operator on the finished shape, not a change to any primitive's formula (design B12), so
    /// every primitive gains it at once. The axis is declared by the child, never chosen here.
    ///
    /// The radial and longitudinal quantities are <b>four separate authored fields</b>, not two shared ones.
    /// A single <c>start</c>/<c>extent</c> pair would mean degrees on a radial child and a dimensionless 0..1
    /// length fraction on a longitudinal one, decided by an axis the user never sets — the identical "one
    /// slider showing two different quantities" fault that <see cref="ShaperBlend"/> above was split to
    /// avoid, and one that would have made a default <c>extent = 360</c> silently read "full length" for a
    /// capsule while any UI showing a degree suffix was wrong half the time. Each field carries its own
    /// identity default, so an enabled sweep with nothing authored is a no-op on either axis.
    /// </summary>
    [Serializable]
    public class ShaperSweep : ISerializationCallbackReceiver
    {
        const uint FldStartDeg = 0x7C00_0001u, FldExtentDeg = 0x7C00_0002u;
        const uint FldStartFrac = 0x7C00_0003u, FldExtentFrac = 0x7C00_0004u;

        public bool enabled = false;

        /// <summary>Radial only: where the kept arc starts, in degrees from +X, counter-clockwise.</summary>
        public ZUIValue startDegreesDial = new ZUIValue(0f);
        /// <summary>Radial only: degrees of arc kept. 360 (the default) is the identity.</summary>
        public ZUIValue extentDegreesDial = new ZUIValue(360f);

        /// <summary>Longitudinal only: where the kept slab starts, as a 0..1 fraction of the length.</summary>
        public ZUIValue startFractionDial = new ZUIValue(0f);
        /// <summary>Longitudinal only: the 0..1 fraction of the length kept. 1 (the default) is the identity.</summary>
        public ZUIValue extentFractionDial = new ZUIValue(1f);

        [SerializeField, FormerlySerializedAs("startDegrees")]   float legacyStartDegrees = 0f;
        [SerializeField, FormerlySerializedAs("extentDegrees")]  float legacyExtentDegrees = 360f;
        [SerializeField, FormerlySerializedAs("startFraction")]  float legacyStartFraction = 0f;
        [SerializeField, FormerlySerializedAs("extentFraction")] float legacyExtentFraction = 1f;
        [SerializeField] bool dialsPromoted;

        public void OnBeforeSerialize() => dialsPromoted = true;

        public void OnAfterDeserialize()
        {
            if (dialsPromoted) { EnsureDials(); return; }
            startDegreesDial = new ZUIValue(legacyStartDegrees);
            extentDegreesDial = new ZUIValue(legacyExtentDegrees);
            startFractionDial = new ZUIValue(legacyStartFraction);
            extentFractionDial = new ZUIValue(legacyExtentFraction);
            dialsPromoted = true;
        }

        public void EnsureDials()
        {
            if (startDegreesDial == null) startDegreesDial = new ZUIValue(0f);
            if (extentDegreesDial == null) extentDegreesDial = new ZUIValue(360f);
            if (startFractionDial == null) startFractionDial = new ZUIValue(0f);
            if (extentFractionDial == null) extentFractionDial = new ZUIValue(1f);
        }

        public float startDegrees { get => ShaperDial.Get(startDegreesDial); set => ShaperDial.Set(ref startDegreesDial, value); }
        public float extentDegrees { get => ShaperDial.Get(extentDegreesDial, 360f); set => ShaperDial.Set(ref extentDegreesDial, value); }
        public float startFraction { get => ShaperDial.Get(startFractionDial); set => ShaperDial.Set(ref startFractionDial, value); }
        public float extentFraction { get => ShaperDial.Get(extentFractionDial, 1f); set => ShaperDial.Set(ref extentFractionDial, value); }

        /// <summary>The four dials at <paramref name="phase01"/>, each clamped to its own declared range so an
        /// animated sweep can never hand the compiler an arc it has no maths for.</summary>
        public void Sample(float phase01, uint seed, out float startDeg, out float extentDeg,
                           out float startFrac, out float extentFrac)
        {
            EnsureDials();
            startDeg = ShaperValue.Sample(startDegreesDial, phase01, seed ^ FldStartDeg, 0f);
            extentDeg = Mathf.Clamp(ShaperValue.Sample(extentDegreesDial, phase01, seed ^ FldExtentDeg, 360f), 0f, 360f);
            startFrac = Mathf.Clamp01(ShaperValue.Sample(startFractionDial, phase01, seed ^ FldStartFrac, 0f));
            extentFrac = Mathf.Clamp01(ShaperValue.Sample(extentFractionDial, phase01, seed ^ FldExtentFrac, 1f));
        }
    }

    /// <summary>
    /// Shell — keep only a band at a given distance from the surface, discarding the interior.
    /// Its identity setting is <c>enabled == false</c>, which returns the child's value untouched.
    /// </summary>
    [Serializable]
    public class ShaperShell : ISerializationCallbackReceiver
    {
        const uint FldThickness = 0x7D00_0001u;

        public bool enabled = false;
        /// <summary>Wall thickness in canvas pixels — constant everywhere, which is the whole point.</summary>
        public ZUIValue thicknessDial = new ZUIValue(4f);
        public ShaperShellAlignment alignment = ShaperShellAlignment.Centred;

        [SerializeField, FormerlySerializedAs("thickness")] float legacyThickness = 4f;
        [SerializeField] bool dialsPromoted;

        public void OnBeforeSerialize() => dialsPromoted = true;

        public void OnAfterDeserialize()
        {
            if (dialsPromoted) { EnsureDials(); return; }
            thicknessDial = new ZUIValue(legacyThickness);
            dialsPromoted = true;
        }

        public void EnsureDials()
        {
            if (thicknessDial == null) thicknessDial = new ZUIValue(4f);
        }

        public float thickness { get => ShaperDial.Get(thicknessDial, 4f); set => ShaperDial.Set(ref thicknessDial, value); }

        public float SampleThickness(float phase01, uint seed)
        {
            EnsureDials();
            return Mathf.Max(0f, ShaperValue.Sample(thicknessDial, phase01, seed ^ FldThickness, 4f));
        }
    }

    /// <summary>
    /// A node of the authored shape tree: either a primitive or a bag of ordered members.
    ///
    /// The combine mode lives <b>on the member</b>, not on the bag (R1) — a bag holds an ordered list of
    /// children and each child carries its own mode. The bottom of the list (index 0) is evaluated first and
    /// is the base of the silhouette; each member above it applies onto the accumulated result. No stage may
    /// reorder a bag's members: not to batch the additive ones, not to group by generator, not to improve
    /// cache locality, not to skip ahead.
    /// </summary>
    [Serializable]
    public class ShaperNode
    {
        public string name = "Shape";
        public bool enabled = true;
        public ShaperNodeKind kind = ShaperNodeKind.Primitive;

        /// <summary>How this node folds into its parent bag. Ignored on the root.</summary>
        public ShaperCombineMode mode = ShaperCombineMode.Add;
        public ShaperBlend blend = new ShaperBlend();

        /// <summary>Applied to this node's whole content — for a bag, to the whole assembly before its members.</summary>
        public ShaperTransformBlock transform = new ShaperTransformBlock();

        public ShaperSweep sweep = new ShaperSweep();
        public ShaperShell shell = new ShaperShell();

        /// <summary>T-0113 — the swarm modifier. Available on EVERY node kind (Primitive, Bag, Composite), null
        /// default is not the identity here (a class default, not a nullable field) — <see cref="ShaperSwarmDef.enabled"/>
        /// is. See <see cref="ShaperSwarmDef"/> for the two implementations behind it.</summary>
        public ShaperSwarmDef swarm = new ShaperSwarmDef();

        /// <summary>Used when <see cref="kind"/> is <see cref="ShaperNodeKind.Primitive"/>.</summary>
        public ShaperPrimitiveDef primitive = new ShaperPrimitiveDef();

        /// <summary>Used when <see cref="kind"/> is <see cref="ShaperNodeKind.Bag"/>. Order is authored data.</summary>
        [SerializeReference] public List<ShaperNode> children = new List<ShaperNode>();

        /// <summary>
        /// Used when <see cref="kind"/> is <see cref="ShaperNodeKind.Composite"/> (T-0112). A composite node's
        /// own <see cref="fill"/> and <see cref="border"/> fields are structurally ignored — never read by the
        /// compiler for a Composite node, so an old authored value left in either slot after a kind change is
        /// inert rather than silently reappearing (<see cref="ShaperCompiler"/>'s <c>EmitBorderJoin</c> refuses a
        /// Composite node's border by construction, and no code path ever resolves a fill for one).
        /// </summary>
        public ShaperCompositeDef composite = new ShaperCompositeDef();

        /// <summary>
        /// Used when <see cref="kind"/> is <see cref="ShaperNodeKind.Solid"/> (T-0155).
        ///
        /// A Solids node keeps its <see cref="fill"/> — unlike a Composite, whose fill is structurally ignored.
        /// That is not an inconsistency, it is LR-6.3: a Solid's MATERIAL COLOUR *is* an ordinary Shaper fill,
        /// which is what buys Solids Gradient, Ramp-by-quantity, Texture and the availability gate for free.
        /// Pyre's own <c>shapeFill</c> has no equivalent here on purpose.
        ///
        /// What the generator replaces is the SHAPE stage for this owner, and only that (LR-6.1): its
        /// coverage, edge distance and surface normal are its own closed-form geometry, written by
        /// <see cref="ShaperSolids.FillTile"/> straight over this owner's slab in
        /// <see cref="ShaperFillResolver.PaintTile"/>. Everything downstream — the claim, the exclusivity
        /// partition, the fill, the border, the composite — is byte-identical to any other owner's, which is
        /// what "goes through the ordinary pipeline like every other generator" has to mean to mean anything.
        /// </summary>
        public ShaperSolidDef solid = new ShaperSolidDef();

        /// <summary>
        /// The fill this node owns, or null (FILL-CONTRACT Part F3). <b>Null is the default and it is the
        /// point:</b> C4 says "the default for a new bag is that the bag owns the fill and the children own
        /// none — which is the 'fuse several shapes, then texture as one' case, made the default rather than a
        /// mode", so a newly created MEMBER node's fill slot is empty and drilling into a bag never creates one
        /// (FC-3.7). The clock then falls out with no second rule: R3 says a fill runs on the clock of the node
        /// it is attached to, so the default answer is the bag's clock.
        ///
        /// Two rules bind this field and neither is enforced here, because a node does not know where it sits:
        /// <list type="bullet">
        /// <item><b>FC-3.2</b> — a LAYER ROOT always owns a fill and it cannot be removed. Making the
        /// nearest-ancestor search TOTAL is worth more than the aesthetic default: it can never fail, so there
        /// is no "no fill found" branch anywhere, no null owner and no undefined pixel inside a covered
        /// silhouette. <see cref="ShaperFillResolver"/> substitutes
        /// <see cref="ShaperFillDef.DefaultRootFill"/> when a root's slot is empty.</item>
        /// <item><b>FC-3.3</b> — a member whose <see cref="mode"/> is
        /// <see cref="ShaperCombineMode.Subtract"/> MAY NOT own a fill; the resolver refuses it with a reason.
        /// A plain field cannot express that, and validating it here would put the rule in two places.</item>
        /// </list>
        /// </summary>
        public ShaperFillDef fill;

        /// <summary>
        /// The border this node owns, or null (BORDER-CONTRACT Part B1). <b>Null is the default and it stays
        /// the default at every level, including the root.</b>
        ///
        /// <b>BD-1.1</b> — a border attaches to a shape node, ANY shape node, and derives a strip from that
        /// node's own field. That single rule answers every placement question by construction: a border on each
        /// member outlines each blob separately, a border on the bag outlines the fused silhouette, and borders
        /// on both give an outer outline plus interior division lines without fighting, because they are
        /// different nodes tracing different fields. A node owns AT MOST ONE border; two strips on one node is a
        /// bag with one child, which keeps B2's one nesting mechanism intact instead of adding a per-node list
        /// that duplicates it.
        ///
        /// <b>BD-1.5</b> — a null, disabled or zero-width border is an EXACT no-op: no strip, no owner, no
        /// dilation, and output bitwise identical to the same tree with this field left null.
        ///
        /// Like <see cref="fill"/>, the rules that depend on WHERE the node sits are not enforced here, because
        /// a node does not know: <b>BD-3.7</b> refuses a border on a Subtract member (the way to outline a hole
        /// is a border on the BAG, which already works — the bag's finished field is zero on the hole's boundary
        /// just as it is on the outer one), and <b>BD-3.8</b> falls a refusing border fill back to the default
        /// Solid rather than letting the outline vanish. Both live in <see cref="ShaperFillResolver"/>.
        /// </summary>
        public ShaperBorderDef border;

        /// <summary>A primitive member.</summary>
        public static ShaperNode Primitive(ShaperPrimitiveDef def, string name = "Shape",
                                           ShaperCombineMode mode = ShaperCombineMode.Add)
        {
            return new ShaperNode
            {
                name = name,
                kind = ShaperNodeKind.Primitive,
                mode = mode,
                primitive = def ?? new ShaperPrimitiveDef(),
            };
        }

        /// <summary>A bag. Members fold bottom-up in the order given.</summary>
        public static ShaperNode Bag(string name = "Bag", ShaperCombineMode mode = ShaperCombineMode.Add,
                                     params ShaperNode[] members)
        {
            var node = new ShaperNode
            {
                name = name,
                kind = ShaperNodeKind.Bag,
                mode = mode,
                children = new List<ShaperNode>(),
            };
            if (members != null) node.children.AddRange(members);
            return node;
        }

        /// <summary>A composite member (T-0112) — the monolithic escape hatch. <paramref name="def"/> must carry
        /// a <see cref="ShaperCompositeDef.reason"/> and a non-empty <see cref="ShaperCompositeDef.reasonNote"/>;
        /// <see cref="ShaperCompositeDef.HasDeclaration"/> is what a compliance pass checks.</summary>
        public static ShaperNode Composite(ShaperCompositeDef def, string name = "Composite",
                                           ShaperCombineMode mode = ShaperCombineMode.Add)
        {
            return new ShaperNode
            {
                name = name,
                kind = ShaperNodeKind.Composite,
                mode = mode,
                composite = def ?? new ShaperCompositeDef(),
            };
        }
    }
}
