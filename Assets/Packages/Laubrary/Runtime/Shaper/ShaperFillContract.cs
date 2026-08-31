using System;
using UnityEngine;

namespace Laubrary.Shaper
{
    /// <summary>
    /// The closed vocabulary of nine per-sample quantities a fill may read (FC-1.2, BC-3.3).
    ///
    /// APPEND-ONLY: serialized as an int on an authored Ramp fill, so an existing fill's pick must never
    /// shift index. The set is <b>closed</b>: BC-3.3 is explicit that "extending the vocabulary is a design
    /// decision requiring a new task, not a new string literal", naming the free-form-string mechanism at
    /// <c>PyreForm.cs:156-159</c> as precisely how a vocabulary stops being one. So this is an enum and the
    /// declared sets below are a bit mask — never strings, at either end (FC-4.1).
    ///
    /// Units and frame, from BC-3.3, restated because a quantity with no unit is a string with extra steps:
    /// <list type="number">
    /// <item><b>Coverage</b> — float, ≥ 0, nominally 0..1 but <i>unbounded above</i>; the consumer clamps at
    /// use (FC-1.6). Every node publishes it (FC-4.1b).</item>
    /// <item><b>Height</b> — float, layer-local units along the layer's up axis, 0 = the base plane.</item>
    /// <item><b>EdgeDistance</b> — float, canvas pixels, <b>signed, negative inside</b>
    /// (<c>ShaperField.cs:9-10</c>). See the polarity trap at FC-1.3.</item>
    /// <item><b>Heat</b> — float 0..1. A distinct quantity from Density; never the same array under two names
    /// (BC-3.4).</item>
    /// <item><b>Density</b> — float 0..1.</item>
    /// <item><b>Soot</b> — float 0..1.</item>
    /// <item><b>Depth</b> — float, layer-local units, entry-to-exit along the ray (BC-3.5). Identically zero
    /// until extrusion exists (T-0109).</item>
    /// <item><b>Age</b> — float 0..1, normalised lifetime of the material at this sample. This is the ONLY
    /// route by which per-instance variation reaches a fill (FC-1.3b).</item>
    /// <item><b>SurfaceDirection</b> — float×3, unit vector, layer-local. Refused by the Ramp picker <i>on
    /// type</i> (FC-6.3c), not on availability.</item>
    /// </list>
    /// </summary>
    public enum ShaperQuantity
    {
        Coverage = 0,
        Height = 1,
        EdgeDistance = 2,
        Heat = 3,
        Density = 4,
        Soot = 5,
        Depth = 6,
        Age = 7,
        SurfaceDirection = 8,
    }

    /// <summary>
    /// A declared set of quantities, as a bit mask over <see cref="ShaperQuantity"/>.
    ///
    /// BC-3.7a: <b>declared, not discovered</b>. The buffer contract records that today's set "is discovered
    /// from whatever string literals a form happens to pass" and names that as the defect. Both ends declare
    /// here: a shape node declares what it PUBLISHES, and a fill declares what it REQUIRES and what it
    /// OPTIONALLY reads (FC-4.1). Nothing is inferred from what a loop happened to touch.
    /// </summary>
    [Flags]
    public enum ShaperQuantitySet
    {
        None = 0,
        Coverage = 1 << 0,
        Height = 1 << 1,
        EdgeDistance = 1 << 2,
        Heat = 1 << 3,
        Density = 1 << 4,
        Soot = 1 << 5,
        Depth = 1 << 6,
        Age = 1 << 7,
        SurfaceDirection = 1 << 8,

        /// <summary>
        /// What the SHIPPED shape engine actually publishes today. <c>ShaperEvaluator.FillTile</c> writes into
        /// precisely two host arrays, <c>distance</c> and <c>coverage</c>
        /// (<c>ShaperEvaluator.cs:148-149</c>) — there is no height, heat, density, soot, depth, age or
        /// surface-direction sheet anywhere in <c>Runtime/Shaper/</c>.
        ///
        /// So the availability gate of Part F4 is exercised HARD from the first build: six of Ramp's eight
        /// offerable entries are unavailable on the day it ships (FC-4.5). That is the right way round — it
        /// makes the gate a real path rather than a facility waiting for a future generator.
        /// </summary>
        ShippedShapeEngine = Coverage | EdgeDistance,

        /// <summary>
        /// T-0109, HS-1.4 — what a node carrying a HEIGHT STAGE publishes:
        /// <c>Coverage | EdgeDistance | Height</c>.
        ///
        /// A node WITHOUT one still publishes exactly <see cref="ShippedShapeEngine"/>, and the difference is
        /// the point: the set follows from whether a height stage is present, DECLARED rather than discovered
        /// (BC-3.7a). <see cref="ShaperHeight.Publishes"/> is the one place that choice is made.
        ///
        /// <c>Depth</c> is deliberately NOT in this set. BC-3.5's <c>depth</c> is the thickness of solid a
        /// sampling ray traverses; it becomes real only through <see cref="ShaperResolve"/>'s crossing list
        /// and is not published by the height stage directly.
        /// </summary>
        ShapeEngineWithHeight = Coverage | EdgeDistance | Height,
    }

    /// <summary>Set algebra and naming for <see cref="ShaperQuantitySet"/>. No allocation, no LINQ.</summary>
    public static class ShaperQuantities
    {
        /// <summary>How many quantities the closed vocabulary holds. Nine, and it does not grow by a literal.</summary>
        public const int Count = 9;

        public static ShaperQuantitySet Of(ShaperQuantity q) => (ShaperQuantitySet)(1 << (int)q);

        public static bool Contains(ShaperQuantitySet set, ShaperQuantity q) => (set & Of(q)) != 0;

        /// <summary>True when every bit of <paramref name="required"/> is present in <paramref name="published"/>.</summary>
        public static bool Satisfies(ShaperQuantitySet published, ShaperQuantitySet required)
            => (required & ~published) == 0;

        /// <summary>
        /// The first missing quantity in vocabulary order, or -1 when nothing is missing. Vocabulary order
        /// rather than bit-scan order so the reason string is stable across builds — a diagnostic that names a
        /// different quantity on two runs of the same document is not a diagnostic.
        /// </summary>
        public static int FirstMissing(ShaperQuantitySet published, ShaperQuantitySet required)
        {
            for (int i = 0; i < Count; i++)
            {
                var q = (ShaperQuantity)i;
                if (Contains(required, q) && !Contains(published, q)) return i;
            }
            return -1;
        }

        /// <summary>The author-facing name, used verbatim inside a reason string (FC-4.3).</summary>
        public static string Name(ShaperQuantity q)
        {
            switch (q)
            {
                case ShaperQuantity.Coverage: return "coverage";
                case ShaperQuantity.Height: return "height";
                case ShaperQuantity.EdgeDistance: return "edge distance";
                case ShaperQuantity.Heat: return "heat";
                case ShaperQuantity.Density: return "density";
                case ShaperQuantity.Soot: return "soot";
                case ShaperQuantity.Depth: return "depth";
                case ShaperQuantity.Age: return "age";
                case ShaperQuantity.SurfaceDirection: return "surface direction";
                default: return "unknown";
            }
        }

        /// <summary>
        /// True when the quantity can drive a scalar ramp at all (FC-6.3c). <see cref="ShaperQuantity.SurfaceDirection"/>
        /// is a <c>float×3</c>, and a direction cannot drive a scalar ramp without first choosing a projection
        /// axis — which is a different control with its own dials, not a value in this picker. It is refused on
        /// TYPE and its refusal message is deliberately different from the availability one, because the two
        /// failures have different remedies: one is fixed by adding a member that publishes the quantity, the
        /// other cannot be fixed at all from this fill.
        /// </summary>
        public static bool IsRampable(ShaperQuantity q) => q != ShaperQuantity.SurfaceDirection;

        /// <summary>The FC-6.3c type refusal. Deliberately NOT the FC-4.3 availability sentence.</summary>
        public const string SurfaceDirectionRefusal = "surface direction is a direction, not a number";

        /// <summary>
        /// The FC-3.3 refusal for a Subtract member's fill slot. R3 is explicit that "a Subtracted member
        /// contributes no values at all. It removes coverage; it does not deposit heat"
        /// (<c>SHAPE-TREE-RULES.md:106</c>), and paint is a deposit.
        ///
        /// It must be REFUSED rather than left to the arithmetic: where a later member re-adds over the hole
        /// the bag's coverage is non-zero, so <c>min(coverage_N, coverage_A)</c> is not identically zero and a
        /// fill on a Subtract member would paint exactly the parts of the subtractor something else put back —
        /// a region nobody authored and which changes shape when an unrelated member above it is edited.
        /// </summary>
        public const string SubtractRefusal = "a subtracted member removes coverage; it deposits nothing";
    }

    /// <summary>
    /// How a fill's colour composites against the accumulated destination (FC-2.6).
    ///
    /// It is a per-fill AUTHORED field with a per-kind default, baked into the compiled op at compile time —
    /// NOT a per-fill-kind static declaration and NOT a per-sample decision. B4's reason for having it at all:
    /// "Additive light is not expressible by coverage and colour alone, and leaving it out is how a glow ends
    /// up looking like paint". The reason it is authored rather than per-kind: the same Gradient is genuinely
    /// wanted both ways, and a static per-kind declaration would duplicate every fill kind into an additive
    /// twin — four kinds becoming eight before a single new capability exists.
    /// </summary>
    public enum ShaperFillComposite
    {
        /// <summary>Standard source-over. Raises the destination's alpha. FC-2.6a.</summary>
        Over = 0,
        /// <summary>
        /// Additive. Does NOT raise alpha — an additive glow over nothing stays transparent-but-bright, which
        /// is what makes it read as light rather than as paint. FC-2.6b.
        /// </summary>
        Add = 1,
    }

    /// <summary>
    /// Where a positional fill's pattern is anchored (FC-1.6). Taken BY VALUE from <c>ZuiFill.cs:40-47</c> —
    /// the concept, the two names and the default — never by reference: FC-0.1 forbids this stage referencing,
    /// subclassing, wrapping or extending <c>ZuiFill</c>.
    ///
    /// <b>Stamped</b> (the default) = the shape's OWN local coords, so the pattern rotates, spins and travels
    /// WITH the shape (stamped onto it). <b>Fixed</b> = canvas-anchored coords, so the shape moves THROUGH a
    /// stationary pattern that stays put on the canvas. That is design B5's "pattern anchored to the world
    /// versus anchored to the shape", which B5 calls "a control every texture fill needs and it is easy to
    /// forget until it looks wrong".
    ///
    /// A non-positional fill (Solid, Ramp-by-quantity) ignores this entirely.
    /// </summary>
    public enum ShaperFillSpace { Stamped = 0, Fixed = 1 }

    /// <summary>
    /// How a non-square anchor box normalises into ±1 (FC-1.5). Taken by value from <c>ZuiFill.cs:49-64</c>.
    ///
    /// <b>Uniform</b> divides both axes by <c>max(hx, hy)</c>, so a radial gradient on a wide rectangle stays a
    /// true CIRCLE; the cost is that the short axis never reaches ±1 — on a box 5.6× wider than it is tall, a
    /// vertical gradient shows the middle 18% of the ramp and nothing else. <b>Stretch</b> divides per axis, so
    /// the ramp always runs end to end and a radial becomes an ellipse fitted to the box.
    ///
    /// The dial exists because the codebase already had both conventions and neither was a choice anyone could
    /// see or make: "TextSplash divided by the larger half-extent, Pyre's background divided per axis"
    /// (<c>ZuiFill.cs:119-121</c>).
    /// </summary>
    public enum ShaperFillFit { Uniform = 0, Stretch = 1 }

    /// <summary>
    /// The fill kinds. APPEND-ONLY: serialized as an int. T-0106 shipped the first four (Part F6); T-0110 adds
    /// the fifth, <see cref="IndexedStrip"/> — B6's "the reference app's pixel-border thing", restored with its
    /// per-slot height.
    ///
    /// Deliberately NOT a class hierarchy. A fill does not nest — it is one object on one node with no children
    /// — so its compiled "program" is a single op struct and the flat form is a struct plus a switch, which is
    /// strictly SIMPLER than an abstract class with five subclasses rather than a concession to it (FC-5.2).
    /// </summary>
    public enum ShaperFillKind
    {
        Solid = 0,
        Gradient = 1,
        RampByQuantity = 2,
        Texture = 3,

        /// <summary>
        /// T-0110 — a hand-painted strip of palette slots, each carrying colour AND a height (protrusion),
        /// selected per pixel by a parameterisation of the shape (<see cref="ShaperStripParameterisation"/>),
        /// with a reach control gating a "patterned" (near-edge, indexed) region against a "plain" (interior,
        /// flat) one. See <see cref="ShaperFillDef"/>'s Indexed strip section and
        /// <c>D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0110\STRIP-SPEC.md</c> for the full model.
        /// </summary>
        IndexedStrip = 4,
    }

    /// <summary>
    /// T-0110, B6 — how an <see cref="ShaperFillKind.IndexedStrip"/> fill's per-pixel parameter is derived from
    /// the shape: "the shape is parameterised — either by the angle around it, or by projection across it".
    /// APPEND-ONLY: serialized as an int.
    /// </summary>
    public enum ShaperStripParameterisation
    {
        /// <summary>The angle around the node's own local centre, like <see cref="ShaperGradientMode.Angular"/>.</summary>
        Angular = 0,
        /// <summary>A linear projection across the node's own local box, like <see cref="ShaperGradientMode.Linear"/>.</summary>
        Projection = 1,
    }

    /// <summary>Gradient's four spatial parameterisations (FC-6.2). APPEND-ONLY.</summary>
    public enum ShaperGradientMode
    {
        Linear = 0,
        Radial = 1,
        /// <summary>New here — <c>ZuiFill</c> has no angular mode (<c>ZuiFill.cs:29</c>).</summary>
        Angular = 2,
        /// <summary>The one mode that reads a QUANTITY, and therefore the one that can be gated (FC-4.1a).</summary>
        ByEdgeDistance = 3,
    }

    /// <summary>How a texture's UVs are derived (FC-6.4a). APPEND-ONLY.</summary>
    public enum ShaperTextureMapping
    {
        /// <summary>The anchor box maps to the texture's full 0..1 UV exactly once; the image never repeats.</summary>
        Fitted = 0,
        /// <summary>The image repeats at an authored density independent of the node's size.</summary>
        Tiled = 1,
    }

    /// <summary>
    /// Category two of FC-1.1: the per-invocation scalars, resolved BEFORE the tile call and never read from a
    /// managed object inside it (BC-1.2). These are not sheets and must not be given sheet-shaped storage
    /// (BC-3.2).
    /// </summary>
    public struct ShaperFillInputs
    {
        /// <summary>
        /// Which swarm instance this invocation is painting. Per BC-3.2 and R4 a swarmed node's fill runs
        /// ONCE, on the node's own clock, over the single resolved plane — so within one fill invocation this
        /// is CONSTANT, and per-instance variation reaches a fill only through the <c>age</c> sheet. Present so
        /// a future non-coalesced path has somewhere to put it; constant and 0 in T-0106.
        /// </summary>
        public int instanceIndex;

        /// <summary>The instance's own normalised life, 0..1. Same status as <see cref="instanceIndex"/>.</summary>
        public float instanceLife;

        /// <summary>
        /// The NODE's clock, per R3: "a fill runs on the clock of the node it is attached to", which is the
        /// parent's clock remapped through the node's own window. This MUST be the same value the shape of
        /// that node was compiled with in that frame — it is the same number
        /// <c>ShaperCompiler.Compile(root, phase01, seed)</c> already takes.
        ///
        /// There is deliberately no separate document-clock input (FC-1.4): handing a fill a raw document clock
        /// as well as its node clock would give it two answers to "when is it" and let a fill quietly ignore its
        /// node's window, which is the exact class of bug R3 exists to close.
        /// </summary>
        public float phase01;

        /// <summary>
        /// For the <c>MinMax</c> dial mode, drawn by hash and never by <c>System.Random</c> — BC-1.3 names
        /// <c>new System.Random(...)</c> at <c>PyreRenderer.cs:5034</c> as an offender by name. Routed through
        /// <c>ShaperValue.Sample</c>, which hashes (<c>ShaperValue.cs:43-49</c>).
        /// </summary>
        public uint seed;

        public static ShaperFillInputs At(float phase01, uint seed = 0u)
            => new ShaperFillInputs { phase01 = phase01, seed = seed };
    }

    /// <summary>
    /// Category one of FC-1.1: the per-sample sheet inputs, as host-allocated, host-owned flat arrays
    /// (BC-3.7f). Every array may be null — a consumer allocates only the quantities it declared, and a fill
    /// that did not declare a quantity never reads its array.
    ///
    /// A fill reads a sheet BY INDEX AT ITS OWN SAMPLE. It never reads <c>[i−1]</c> or <c>[i+width]</c>:
    /// BC-2.1 forbids screen-space neighbour reads before the resolve, and a neighbour read would seam under
    /// BC-1.6's tile-independence test. A fill also never WRITES to an input sheet (BC-3.7c) — coverage is
    /// readable by any number of consumers, and the border stage reads the same array.
    /// </summary>
    public struct ShaperFillSheets
    {
        public float[] coverage;
        public float[] height;
        public float[] edgeDistance;
        public float[] heat;
        public float[] density;
        public float[] soot;
        public float[] depth;
        public float[] age;
        /// <summary>The three components of quantity #9. Not readable by any fill T-0106 ships (FC-6.3c).</summary>
        public float[] surfaceDirX, surfaceDirY, surfaceDirZ;

        /// <summary>
        /// Which of the nine these arrays actually carry. Declared by the host, NOT discovered by testing which
        /// references are non-null — BC-3.7a. (The null test is used only as a defensive read guard inside the
        /// loop, never as the declaration.)
        /// </summary>
        public ShaperQuantitySet published;

        /// <summary>The two sheets the shipped shape engine produces (<c>ShaperEvaluator.cs:148-149</c>).</summary>
        public static ShaperFillSheets FromShapeStage(float[] coverage, float[] edgeDistance)
            => new ShaperFillSheets
            {
                coverage = coverage,
                edgeDistance = edgeDistance,
                published = ShaperQuantitySet.ShippedShapeEngine,
            };

        /// <summary>
        /// T-0109, HS-1.4 — the shape stage WITH a height stage attached: the same two sheets plus the height
        /// sheet <see cref="ShaperHeight.FillTile"/> writes.
        ///
        /// A separate overload rather than a nullable third argument on the one above, so that the declared
        /// set and the arrays it names can never drift apart: passing a height array here declares
        /// <c>Height</c>, and there is no way to pass one without declaring it.
        /// </summary>
        public static ShaperFillSheets FromShapeStage(float[] coverage, float[] edgeDistance, float[] height)
            => new ShaperFillSheets
            {
                coverage = coverage,
                edgeDistance = edgeDistance,
                height = height,
                published = ShaperQuantitySet.ShapeEngineWithHeight,
            };

        /// <summary>The scalar sheet for one quantity, or null. Called at compile/bind time, never per sample.</summary>
        public float[] Sheet(ShaperQuantity q)
        {
            switch (q)
            {
                case ShaperQuantity.Coverage: return coverage;
                case ShaperQuantity.Height: return height;
                case ShaperQuantity.EdgeDistance: return edgeDistance;
                case ShaperQuantity.Heat: return heat;
                case ShaperQuantity.Density: return density;
                case ShaperQuantity.Soot: return soot;
                case ShaperQuantity.Depth: return depth;
                case ShaperQuantity.Age: return age;
                default: return null;   // SurfaceDirection is a float×3 and has no single scalar sheet.
            }
        }
    }

    /// <summary>
    /// The three outputs of FC-2.1, as host-allocated, host-owned flat arrays — BC-3.7f extended to the fill's
    /// OUTPUTS, which is what keeps the fill from ever allocating, replacing, resizing or freeing anything.
    ///
    /// A fill emits albedo, always; a veil, always; and a height delta, only if it declared that it does. It
    /// emits nothing else — no shine, no specular, no rim, no lit colour. That is B4's load-bearing
    /// simplification: "A fill emits albedo — flat, unlit colour — and optionally a height delta. The
    /// document's light rig turns albedo plus height into the final pixel. Shine belongs to the lights, not to
    /// the paint." Lighting is T-0108 and the height delta is the entire interface it needs.
    /// </summary>
    public struct ShaperFillEmit
    {
        /// <summary>
        /// LINEAR, non-premultiplied albedo, THREE floats per sample at index <c>3·sampleIndex</c>.
        ///
        /// <b>There is no alpha channel</b> (FC-2.2) and that is deliberate. The shipped shape stage split
        /// <c>ShaperBlend</c> into <c>width</c> and <c>carveStrength</c>, and <c>ShaperSweep</c> into four
        /// fields, both times because one authored number was showing two different quantities
        /// (<c>ShaperNode.cs:11-15</c>, <c>:30-37</c>). A fill emitting both an albedo alpha and a veil, with no
        /// rule saying which wins, is the same defect a third time — and it is exactly the defect
        /// <c>ZuiFill</c> has today, where <c>color</c>'s alpha means translucency in Solid mode
        /// (<c>ZuiFill.cs:68</c>) and pattern-mask in Grid and Dots mode (<c>ZuiFill.cs:143</c>).
        /// The veil is the one and only transparency authority.
        /// </summary>
        public float[] albedo;

        /// <summary>Scalar in [0,1], one per sample. FC-2.4.</summary>
        public float[] veil;

        /// <summary>
        /// Layer-local height units, one per sample, or null when no fill in the layer declared it. "Optional"
        /// means DECLARED-then-allocated, never discovered-then-allocated (BC-3.7a / FC-2.5).
        ///
        /// Nothing consumes it in T-0106 — the shape's own height arrives with T-0109's extrusion. That is not
        /// a reason to defer it: it is the hook that makes T-0110's indexed strip one new fill kind and zero
        /// contract changes, which B6 calls "the existence proof for B4's central claim".
        /// </summary>
        public float[] heightDelta;
    }

    /// <summary>
    /// The sRGB boundary. Exactly two conversions exist in the whole fill stage and both live here, one
    /// function each (FC-2.3).
    ///
    /// <b>Albedo is LINEAR, non-premultiplied, float per channel.</b> The project is in Gamma colour space
    /// (<c>m_ActiveColorSpace: 0</c> at <c>ProjectSettings/ProjectSettings.asset:50</c>), so Unity performs no
    /// conversion for us and this is a choice the fill stage makes for itself.
    ///
    /// The reason: everything the fill stage and the light stage do to albedo is a MULTIPLICATION — veil
    /// multiplies it, coverage multiplies it, the light rig will multiply it, and additive compositing sums it.
    /// Every one of those is wrong in an sRGB-encoded space (coverage-weighted compositing in sRGB is the
    /// classic dark-fringe artefact; albedo × light in sRGB is the classic muddy-shading artefact). Since B4's
    /// whole architecture is "a fill emits albedo, the lights multiply it", getting the space wrong makes the
    /// central ruling produce visibly worse pictures than the thing it replaces.
    ///
    /// The costs, stated: the decode is per AUTHORED COLOUR at compile time (256 LUT entries, or one colour for
    /// a Solid) and the encode is once per output pixel at the <c>Color32</c> write, which happens exactly once
    /// regardless — neither is per-sample-per-fill. And the same authored gradient will not produce the same
    /// bytes here as in Pyre, because Pyre composites in sRGB; that is a real, expected difference in Shaper's
    /// favour.
    ///
    /// <b>The escape hatch, named rather than built.</b> If byte-for-byte Pyre parity is ever wanted over
    /// correct compositing, making <see cref="DecodeChannel"/> and <see cref="EncodeChannel"/> the identity is
    /// a two-line change at two sites and no other code moves. Do NOT add a dial for it — a colour-space mode
    /// is a document-wide correctness property, not a per-fill preference, and a switch would mean every
    /// conformance test in Part F7 has to run twice.
    /// </summary>
    public static class ShaperSrgb
    {
        // The standard IEC 61966-2-1 transfer function's four constants. Named rather than inlined because
        // every one of them is a magic number whose provenance is the standard, not this project.
        const float DecodeKnee = 0.04045f;
        const float DecodeSlope = 12.92f;
        const float DecodeOffset = 0.055f;
        const float DecodeGamma = 2.4f;
        const float EncodeKnee = 0.0031308f;

        /// <summary>sRGB-encoded 0..1 → linear 0..1. Called once per authored colour at compile time.</summary>
        public static float DecodeChannel(float s)
        {
            if (s <= 0f) return 0f;
            if (s >= 1f) return 1f;
            return s <= DecodeKnee
                ? s / DecodeSlope
                : Mathf.Pow((s + DecodeOffset) / (1f + DecodeOffset), DecodeGamma);
        }

        /// <summary>linear 0..1 → sRGB-encoded 0..1. Called once per output pixel at the <c>Color32</c> write.</summary>
        public static float EncodeChannel(float l)
        {
            if (l <= 0f) return 0f;
            if (l >= 1f) return 1f;
            return l <= EncodeKnee
                ? l * DecodeSlope
                : (1f + DecodeOffset) * Mathf.Pow(l, 1f / DecodeGamma) - DecodeOffset;
        }

        /// <summary>Decode an authored <see cref="Color"/> to linear RGB. Its alpha is NOT read (FC-2.2).</summary>
        public static void Decode(Color c, out float r, out float g, out float b)
        {
            r = DecodeChannel(c.r);
            g = DecodeChannel(c.g);
            b = DecodeChannel(c.b);
        }

        /// <summary>The one encode site: linear float → the byte a <c>Color32</c> carries.</summary>
        public static byte EncodeToByte(float linear)
        {
            float s = EncodeChannel(linear);
            int b = Mathf.RoundToInt(s * 255f);
            return (byte)(b < 0 ? 0 : b > 255 ? 255 : b);
        }
    }
}
