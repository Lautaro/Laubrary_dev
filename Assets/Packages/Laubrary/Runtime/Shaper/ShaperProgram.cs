using UnityEngine;

namespace Laubrary.Shaper
{
    /// <summary>
    /// The op kinds the flat evaluator walks, plus the empty-accumulator push.
    /// <b>APPEND-ONLY: serialized as an int</b>, so a new kind goes on the END and no existing value moves.
    /// </summary>
    public enum ShaperOpKind
    {
        /// <summary>Pushes <see cref="ShaperField.Empty"/> — a bag's starting accumulator.</summary>
        Empty = 0,
        /// <summary>Pushes one primitive's distance, in canvas pixels.</summary>
        Leaf = 1,
        /// <summary>Pops two, pushes one.</summary>
        Combine = 2,
        /// <summary>Unary; carries its own local-frame inverse.</summary>
        Sweep = 3,
        /// <summary>Unary.</summary>
        Shell = 4,
        /// <summary>
        /// Unary. BORDER-CONTRACT BD-2.2: <c>d − reach</c>, the exact signed distance of the node dilated by a
        /// joining border's outward reach. Emitted by <see cref="ShaperCompiler"/> at the point a node's own
        /// content is finished, so every program containing the node publishes the same field. It is a CONSTANT
        /// SUBTRACTION and deliberately not a <c>min</c> against the strip's field — see
        /// <see cref="ShaperBorder.JoinOp"/> for the seam the <c>min</c> produces.
        /// </summary>
        Dilate = 5,
    }

    /// <summary>
    /// One instruction of the compiled program. Flat numbers only — the per-sample loop reads this struct and
    /// nothing else, so there is no virtual dispatch, no delegate, no managed dereference and no boxing.
    ///
    /// The <c>p0…p11</c> slots are reused per op kind:
    /// <list type="bullet">
    /// <item><b>Leaf</b> — the primitive's baked parameters (see <see cref="ShaperSdf"/> for the table).</item>
    /// <item><b>Sweep, radial</b> — p0/p1 and p2/p3 are the two half-plane normals; p4 is 1 when the wedge is
    /// a union (extent &gt; 180°).</item>
    /// <item><b>Sweep, longitudinal</b> — p0/p1 are the slab bounds along the local X axis.</item>
    /// <item><b>Shell</b> — p0 is the thickness, and it is the only slot Shell reads.</item>
    /// <item><b>Dilate</b> — p0 is the outward reach, and it is the only slot Dilate reads.</item>
    /// <item><b>Combine</b> — p0 blend width, p1 blend exponent, p2 carve strength, p3 reach.</item>
    /// </list>
    /// </summary>
    public struct ShaperOp
    {
        public ShaperOpKind kind;

        public ShaperPrimitiveKind primitive;
        public ShaperCombineMode mode;
        public ShaperSweepAxis sweepAxis;
        public ShaperShellAlignment shellAlignment;
        public int count;

        public float p0, p1, p2, p3, p4, p5, p6, p7, p8, p9, p10, p11;

        /// <summary>Canvas → this op's own local space. The full accumulated root→node inverse, one 2×3.</summary>
        public float m00, m01, m02, m10, m11, m12;

        /// <summary>
        /// <c>σ_min</c> of the accumulated forward linear part. The local distance is multiplied by it on the
        /// way out, so every node publishes in canvas pixels. Omitting this is the whole of the reference
        /// app's measured 5.00× over-report at <c>scale.x = 0.2</c>.
        /// </summary>
        public float distanceScale;

        /// <summary>The declared bound of the subtree rooted at this op.</summary>
        public float bound;

        /// <summary>Support extent of the subtree rooted at this op, in canvas space.</summary>
        public float boxCx, boxCy, boxHalfW, boxHalfH;
    }

    /// <summary>
    /// The authored tree compiled once, for one frame-time, into a flat immutable program in post-order RPN.
    /// Fold order is fixed at compile time and no stage may reorder it (R1).
    /// </summary>
    public sealed class ShaperProgram
    {
        /// <summary>Post-order RPN. Evaluated by a flat loop over a value stack.</summary>
        public ShaperOp[] ops = System.Array.Empty<ShaperOp>();

        /// <summary>The value stack this program needs. Hosts allocate it once, never per sample.</summary>
        public int stackDepth = 1;

        /// <summary>The root's declared bound: <c>reportedDistance ≤ bound × trueDistance</c>.</summary>
        public float bound = 1f;

        /// <summary>The support extent of the whole tree in canvas space — a node costs one pass over the
        /// pixels it can affect (R7), not one pass over the canvas.</summary>
        public float supportCx, supportCy, supportHalfW, supportHalfH;

        /// <summary>
        /// The support extent of the whole tree in the ROOT NODE'S OWN LOCAL FRAME — the frame
        /// <see cref="rootInverse"/> maps a canvas point into. Added for the fill stage (FILL-CONTRACT
        /// FC-1.5a), which normalises its sampling point by this box rather than by the canvas one.
        ///
        /// <b>Why a second box rather than mapping the canvas one back down.</b> The canvas box is built by
        /// carrying local half-extents through the FORWARD matrix with an absolute-value corner sum
        /// (<c>ShaperCompiler.cs:178-179</c>), so it grows and shrinks as the node rotates. A fill normalising
        /// by it would make a gradient on a spinning shape breathe once per quarter turn with nothing authored
        /// changing. Carrying the box back down through the inverse — which the longitudinal-sweep path does at
        /// <c>ShaperCompiler.cs:326-328</c> — inherits exactly that inflation. It is correct there, because a
        /// sweep only needs a CONSERVATIVE span; it is wrong for a fill, which needs a STABLE one. So the local
        /// box is computed the other way: the same fold with this node's own transform omitted.
        ///
        /// Valid only when <see cref="hasLocalSupport"/>. An empty bag, an all-disabled subtree or a singular
        /// transform leaves it false, and the fill's coordinate becomes (0,0) without dividing (FC-1.5b).
        /// </summary>
        public float localSupportCx, localSupportCy, localSupportHalfW, localSupportHalfH;
        /// <summary>True when the local support box above is meaningful.</summary>
        public bool hasLocalSupport;

        /// <summary>
        /// BORDER-CONTRACT BD-2.4 — the factor converting a distance expressed in THIS FIELD's units into a
        /// conservative distance in CANVAS units: the worst <c>σ_max / σ_min</c> of the accumulated forward map
        /// anywhere in the tree. Exactly 1 for any isotropically transformed tree, which is the common case.
        ///
        /// <b>Why a distance is not already in canvas units.</b> It is, in the sense that matters for authoring
        /// — every node publishes <c>σ_min · d_local</c>, so a blend width dialled in pixels means pixels at any
        /// depth. But <c>σ_min</c> makes the field a conservative UNDER-estimate, so the set <c>{d ≤ r}</c>
        /// reaches as far as <c>r · σ_max/σ_min</c> canvas pixels along a stretched axis. Any consumer growing
        /// <see cref="supportHalfW"/> by a distance read off the field must multiply by this, or the box
        /// excludes real samples — measured at 808 samples and 23.5 px on a member scaled (2.0, 0.5) with an
        /// Outward border of reach 8.
        /// </summary>
        public float supportSpread = 1f;

        /// <summary>
        /// <c>σ_min</c> of the accumulated forward map at the root NODE of this program — the number that
        /// converts a canvas distance into that node's own local units, which is the frame
        /// <see cref="localSupportHalfW"/> is expressed in. Used by <see cref="ShaperBorder.CompileStrip"/> to
        /// grow a strip's own node-local box, the way <c>ShaperCompiler.EmitShell</c> already grows a shell's.
        /// Never zero: a singular node publishes the empty field and leaves this at 1.
        /// </summary>
        public float rootSigmaMin = 1f;

        /// <summary>
        /// Canvas → the root node's own local space: the accumulated inverse INCLUDING this node's own
        /// transform block, and including whatever <c>parentForward</c> the compile was seeded with. This is
        /// the 2×3 a fill uses for its Stamped anchor (FC-1.5); it is the same number
        /// <c>ShaperCompiler.EmitLeaf</c> already bakes per leaf, exposed at the program level.
        /// Identity when <see cref="rootInvertible"/> is false — it never divides.
        /// </summary>
        public ShaperMatrix rootInverse = ShaperMatrix.Identity;
        /// <summary>False when the root's accumulated linear part is singular.</summary>
        public bool rootInvertible = true;

        /// <summary>
        /// A bottom-most member set to Subtract or Intersect gives nothing, because the accumulator starts
        /// empty. That is not a change of behaviour, it is a UI flag (R1): the compiler <b>records</b> it
        /// rather than seeding a sentinel to make it do something else.
        /// </summary>
        public bool hasLeadingNonAdd;
        /// <summary>The FIRST offender's name, for a one-line message.</summary>
        public string leadingNonAddNode;
        /// <summary>
        /// How many offenders there are in the whole program. The name alone records only the first, so a
        /// tree with three offending bags would have surfaced one and silently hidden two — and the flag
        /// exists precisely so the UI can point at the problem.
        /// </summary>
        public int leadingNonAddCount;

        /// <summary>A member whose linear part is singular publishes the empty field and is flagged. It never divides.</summary>
        public bool hasSingularTransform;
        /// <summary>The FIRST offender's name, for a one-line message.</summary>
        public string singularNode;
        /// <summary>How many singular-transform nodes there are in the whole program, not just the first.</summary>
        public int singularTransformCount;

        /// <summary>The normalised frame time this program was compiled for.</summary>
        public float phase01;

        /// <summary>A value stack of the right size. Allocate once per thread and reuse; never per sample.</summary>
        public float[] NewStack() => new float[Mathf.Max(1, stackDepth)];
    }
}
