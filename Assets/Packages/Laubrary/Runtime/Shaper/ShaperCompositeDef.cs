using System;
using UnityEngine;

namespace Laubrary.Shaper
{
    /// <summary>
    /// T-0112 — the composite generator's declared reason for being monolithic (SHAPER_THE_DESIGN.md B1,
    /// N-entanglement-split.md §6.2). APPEND-ONLY: serialized as an int.
    ///
    /// §6.2 names exactly two legitimate reasons a generator bypasses the shape/fill split. A third reason is a
    /// design bug, not a third enum value — this type is deliberately closed, the same "closed vocabulary" stance
    /// <see cref="ShaperQuantity"/> already takes for the nine sample quantities.
    /// </summary>
    public enum ShaperCompositeReason
    {
        /// <summary>
        /// PERMANENT. §6.2 #1: "its default fill is authored data, not a rule" — a hand-painted sprite, a baked
        /// sheet, a future Shaper import. There is no procedural rule to decompose, so there is nothing to split
        /// and nothing to schedule.
        /// </summary>
        AuthoredData = 0,

        /// <summary>
        /// TEMPORARY, and §6.2 requires the declaration to "say so" — this reads as technical debt, never as
        /// architecture. §6.2 #2: the generator's picture IS a computed rule, but nobody has done the work of
        /// splitting its edge rule from its paint recipe into the shape/fill contract yet.
        /// </summary>
        NotYetSplit = 1,
    }

    /// <summary>
    /// T-0112 — the minimal surface a composite generator's picture source must offer Shaper.
    ///
    /// Deliberately NOT a reference to <c>Laubrary.Pyre.PyreForm</c>. Runtime/Shaper's asmdef references only
    /// <c>com.Lautaro-Arino.Laubrary.ZuiRuntime</c> — Shaper has no dependency on Pyre today, and this task does
    /// not add one to the core engine. The adapter that hosts a <c>PyreForm</c> behind this interface lives in
    /// the bridge asmdef <c>Runtime/PyreShaper/</c>, named for the two systems it connects — the same convention
    /// already used for ZoetropePyre and ZoetropeLaunimator (Laubrary_Dev CLAUDE.md, "Naming"). A future host
    /// (a baked sprite sheet, a different tool's generator) implements this same interface with no Pyre
    /// dependency at all.
    /// </summary>
    public interface IShaperCompositeSource
    {
        /// <summary>A short author-facing label for the picker's single, locked entry ("Fill: <i>label</i>") —
        /// the fill picker a composite node shows never offers a second one (§6.2).</summary>
        string SourceLabel { get; }

        /// <summary>
        /// Render one whole frame into <paramref name="target"/> (row-major, row 0 = bottom — the same convention
        /// <c>PyreForm.Render</c> already uses, so hosting a Pyre form is close to a direct pass-through and
        /// nothing about the form's own code changes). A pure function of its arguments: same determinism
        /// contract a <c>PyreForm</c> already carries (seed / phase in, target filled, nothing read from Unity
        /// time or global state).
        /// </summary>
        void Render(int width, int height, float phase01, uint seed, Color32[] target);
    }

    /// <summary>
    /// T-0112 — one composite generator's baked picture, held on the <see cref="ShaperProgram"/> that compiled
    /// it. A composite node cannot be evaluated by the closed-form per-point walk every other op kind uses
    /// (<see cref="ShaperEvaluator.Distance"/>) because it has no analytic distance function — only a rendered
    /// raster. So it is baked ONCE at compile time (<see cref="ShaperCompiler"/>'s <c>EmitComposite</c>) into
    /// this flat, host-owned record, and the per-sample walk sngle-fetches from it by index
    /// (<see cref="ShaperOp.count"/>) — the same "bulk data referenced by index, never a managed object in the
    /// op" rule <c>ShaperFillDef.texture</c> already follows (FC-5.5).
    /// </summary>
    public sealed class ShaperCompiledComposite
    {
        public int width, height;

        /// <summary>Alpha channel of the render, decoded to 0..1 — this IS the node's published coverage.</summary>
        public float[] coverage;

        /// <summary>
        /// The full render — a composite ROOT layer's own albedo (§6.2: "its fill picker shows exactly one
        /// entry — itself"). A composite nested in a Bag never reads this; the bag's own fill paints the fused
        /// silhouette instead, and this generator contributes only its (pseudo-)distance to the fold.
        ///
        /// <b>T-0191 built the consumer this field was always written for.</b> It is STRAIGHT-alpha sRGB, not
        /// premultiplied — <c>PyreRenderer.Over</c> un-premultiplies back out after every blend
        /// (<c>Runtime/PyreShaper/ShaperEffectStageRunner.cs:108-109</c> states the same law) and
        /// <c>PyreSupersample.Downsample</c> un-premultiplies the block average
        /// (<c>Runtime/Pyre/PyreSupersample.cs:58</c>) — so it decodes exactly the way a Texture fill's pixels
        /// do (<c>ShaperFillCompiler.cs:626-635</c>): RGB through <see cref="ShaperSrgb.DecodeChannel"/>, alpha
        /// NOT decoded because alpha is a coverage and not a colour. See <see cref="ShaperCompositeAlbedo"/>.
        /// </summary>
        public Color32[] pixels;
    }

    /// <summary>
    /// T-0191 — the binding that makes a composite root's own picture its albedo: one baked
    /// <see cref="ShaperCompiledComposite"/> plus the canvas→node-local transform and half-extents its
    /// <see cref="ShaperOpKind.CompositeSample"/> op carries, lifted out of the compiled program ONCE at bind
    /// time so the paint pass never walks ops to find them.
    ///
    /// <b>The rule, stated once and in full.</b> A composite generator "produces a finished picture directly"
    /// (SHAPER_THE_DESIGN.md C1/§6.2) and "may not be re-filled". Until T-0191 only its ALPHA reached the
    /// document — <see cref="ShaperCompiledComposite.pixels"/> was written by
    /// <c>ShaperCompiler.EmitComposite</c> and read nowhere — so every composite painted as a one-colour
    /// silhouette in the default root fill's grey. Measured on Pyre's Gem at 96×96: 608 distinct RGB values in
    /// Pyre's own window, exactly 1 inside a Shaper document. The finished picture must therefore BE the
    /// layer's albedo, and these four clauses say precisely where:
    ///
    /// <list type="number">
    /// <item><b>A composite has no Shaper fill to lose to.</b> The window offers neither a Fill nor a Border
    /// card on a composite node (the owner's ruling, T-0191: "absent, not greyed"), because §6.2 already says
    /// a composite "may not be re-filled" — its colour is authored on the GENERATOR, in the hosted card's own
    /// fill ramp, and a second Shaper fill beside it would be two authorities over one pixel. So the FC-3.2
    /// default root fill this owner holds is a structural placeholder, and the binding is unconditional.</item>
    /// <item><b>Only where the composite is itself a fill owner.</b> Which, given FC-3.2 ("a shape layer's root
    /// node always owns a fill, and nothing else is given one by default") and clause 1, can only be the LAYER
    /// ROOT. A composite nested in a Bag never becomes an owner, so it still publishes coverage and nothing
    /// else, exactly as B1 requires, and its colour never leaks up into the bag that fused it. This is the
    /// nearest-ancestor fill-ownership rule applied unchanged, not a second rule beside it.</item>
    /// <item><b>No height.</b> <see cref="IShaperCompositeSource"/> has one output — a picture — so there is no
    /// height channel to read and the height DELTA is zero. The layer's own height stage
    /// (<see cref="ShaperHeightOp"/>, driven by the composite's edge distance) is untouched.</item>
    /// <item><b>Unlit.</b> The picture is already finished: Pyre's Gem arrives with its facets lit, its edge
    /// lines drawn and its glows applied. Running Shaper's rig over it a second time is double-lighting, which
    /// is the same objection LR-5.3 already makes of lighting an ADDITIVE fill ("a lamp does not get dimmer
    /// because you put it in a dark room"). So a composite-albedo owner takes the unlit branch regardless of
    /// its layer's <c>receiveLighting</c>. Shading a composite is the GENERATOR's job — Pyre's own Light card
    /// is right there on the hosted layer — which is the same place clause 1 sends its colour.</item>
    /// </list>
    /// </summary>
    public sealed class ShaperCompositeAlbedo
    {
        /// <summary>The baked picture this owner paints with.</summary>
        public ShaperCompiledComposite raster;

        /// <summary>Canvas → node-local, copied from the op so the paint pass maps points exactly the way
        /// <c>ShaperEvaluator</c>'s <see cref="ShaperOpKind.CompositeSample"/> case does. Any other mapping
        /// would put the colour a fraction of a texel off its own coverage.</summary>
        public float m00, m01, m02, m10, m11, m12;

        /// <summary>The raster's local-frame box, <c>[-halfExtentX, halfExtentX] × [-halfExtentY, halfExtentY]</c>.</summary>
        public float halfExtentX, halfExtentY;

        /// <summary>
        /// Lift the binding out of a fill owner's compiled program, or null when there is nothing unambiguous
        /// to lift.
        ///
        /// <b>Exactly one <see cref="ShaperOpKind.CompositeSample"/> op and exactly one baked picture</b> — the
        /// shape a lone Composite node compiles to, and equally the shape a NATIVE swarm compiles to
        /// (<c>ShaperCompiler.EmitCompositeSwarmNative</c> bakes the whole swarm into ONE raster, which is then
        /// genuinely the picture to paint with). A generic swarm fans out to N sampled instances with N
        /// rasters and no single picture, so it binds nothing and keeps the pre-T-0191 flat fill rather than
        /// silently picking one member's colours for all of them.
        /// </summary>
        public static ShaperCompositeAlbedo From(ShaperProgram program)
        {
            if (program == null || program.ops == null || program.composites == null) return null;
            if (program.composites.Length != 1) return null;

            int found = -1;
            for (int i = 0; i < program.ops.Length; i++)
            {
                if (program.ops[i].kind != ShaperOpKind.CompositeSample) continue;
                if (found >= 0) return null;
                found = i;
            }
            if (found < 0) return null;

            ShaperCompiledComposite raster = program.composites[program.ops[found].count];
            if (raster == null || raster.pixels == null || raster.width <= 0 || raster.height <= 0) return null;
            if (raster.pixels.Length < raster.width * raster.height) return null;

            return new ShaperCompositeAlbedo
            {
                raster = raster,
                m00 = program.ops[found].m00, m01 = program.ops[found].m01, m02 = program.ops[found].m02,
                m10 = program.ops[found].m10, m11 = program.ops[found].m11, m12 = program.ops[found].m12,
                halfExtentX = program.ops[found].p0,
                halfExtentY = program.ops[found].p1,
            };
        }
    }

    /// <summary>
    /// T-0112 — the composite generator's authored declaration. Used when a <see cref="ShaperNode"/>'s
    /// <see cref="ShaperNode.kind"/> is <see cref="ShaperNodeKind.Composite"/>.
    ///
    /// The whole contract is one line (SHAPER_THE_DESIGN.md B1): "it must publish coverage, and it may publish
    /// nothing else." §6.2's other half — "monolithic must be a DECLARED REASON, never a DECLARED EXEMPTION" —
    /// used to be authored here too (<see cref="reason"/>/<see cref="reasonNote"/>, both retired T-0254); the
    /// declaration now lives on the source TYPE (<see cref="ShaperCompositeSourceInfoAttribute"/> /
    /// <c>Runtime/PyreShaper/PyreCompositeCatalog</c>), which is where a fact true of every document hosting
    /// that source belongs.
    /// </summary>
    [Serializable]
    public class ShaperCompositeDef
    {
        /// <summary>The hosted generator. Null renders as empty — no coverage anywhere — the same "a
        /// half-configured fill never renders empty [wrongly]" posture FC-6.5 already takes for a Gradient with
        /// no gradient asset or a Texture with none assigned; a composite with no source assigned yet is a
        /// legal, if useless, authoring state, never a null-reference crash.</summary>
        [SerializeReference] public IShaperCompositeSource source;

        /// <summary>
        /// T-0254 — RETIRED from the authored surface. §6.2's classification is a fact about the SOURCE TYPE,
        /// not a per-document choice (every document hosting the same source gets the same answer), so it now
        /// lives on <see cref="ShaperCompositeSourceInfoAttribute.Reason"/> (the two stateful simulations) or
        /// in <c>Runtime/PyreShaper/PyreCompositeCatalog</c> (the nine hosted <c>PyreForm</c>s, all
        /// <see cref="ShaperCompositeReason.NotYetSplit"/>) — never read from here any more. Kept, serialized
        /// and defaulted exactly as before so a pre-T-0254 document still deserializes without complaint.
        /// </summary>
        [Obsolete("Retired T-0254 — the reason a generator is monolithic is now a fact about its TYPE, declared "
                + "on ShaperCompositeSourceInfoAttribute / PyreCompositeCatalog, not authored per document. Kept "
                + "serialized so existing documents still load; nothing reads this field any more.")]
        [HideInInspector] public ShaperCompositeReason reason = ShaperCompositeReason.NotYetSplit;

        /// <summary>T-0254 — RETIRED alongside <see cref="reason"/>, same reasoning: a hosted source's own
        /// declaration (its catalog entry / attribute) is the one place this sentence is written now.</summary>
        [Obsolete("Retired T-0254 — see reason. Kept serialized so existing documents still load; nothing reads "
                + "this field any more.")]
        [HideInInspector, TextArea(2, 5)] public string reasonNote = "";

        /// <summary>
        /// Half-extent, in the node's own local canvas units, of the box the generator's picture is baked into —
        /// a composite generator's equivalent of a primitive's own declared half-extents
        /// (<see cref="ShaperBakedPrimitive.halfExtentX"/>/<c>Y</c>). The source's render is assumed to have
        /// faded to (near) zero alpha before this box's edge; the sampler CLAMPS rather than hard-clips at the
        /// boundary (see <see cref="ShaperEvaluator"/>'s CompositeSample case), so authoring too small a box
        /// smears the generator's own edge flat instead of cutting it — a visible authoring mistake, not a
        /// silent one.
        ///
        /// <b>T-0254 — never authored.</b> <see cref="FitTo"/> overwrites this on every render before
        /// <see cref="ShaperCompiler"/> reads it (see that method's own doc), so nothing a user sets here would
        /// ever survive to be drawn. <c>[NonSerialized]</c> rather than deleted outright: <c>ShaperCompiler</c>
        /// reads this field directly at a dozen call sites and <c>ShaperCompiler.Compile</c>'s signature is
        /// documented as frozen ("kept EXACTLY as shipped"), so there is no canvas-size channel into the
        /// compiler except this instance field. Excluding it from serialization is the whole of "remove it from
        /// the authored surface" that is safe to do without re-plumbing the compiler; see this task's handover
        /// for the follow-up that would be needed to delete the field outright.
        /// </summary>
        [NonSerialized] public float halfExtentX = 64f;
        [NonSerialized] public float halfExtentY = 64f;

        /// <summary>
        /// Bake resolution in texels, independent of the canvas's own sampling resolution — a composite
        /// generator renders its own whole picture ONCE per compile, at this fixed resolution, and the shape
        /// stage then samples that raster like a texture (bilinear). Raising this sharpens the generator's own
        /// silhouette; it does not sharpen with camera zoom the way an analytic primitive's edge does — a real,
        /// named limitation of the escape hatch, not an oversight.
        ///
        /// <b>T-0254 — never authored</b>, same reasoning as <see cref="halfExtentX"/>.
        /// </summary>
        [NonSerialized] public int bakeWidth = 128;
        [NonSerialized] public int bakeHeight = 128;

        /// <summary>
        /// T-0254 — the fitted box, returned rather than only stashed on the instance (see <see cref="FitTo"/>).
        /// A plain readonly value: nothing on it is authored, it is recomputed fresh on every render.
        /// </summary>
        public readonly struct FittedBox
        {
            public readonly float halfExtentX, halfExtentY;
            public readonly int bakeWidth, bakeHeight;
            public FittedBox(float hx, float hy, int bw, int bh)
            { halfExtentX = hx; halfExtentY = hy; bakeWidth = bw; bakeHeight = bh; }
        }

        /// <summary>
        /// T-0191 — fit the bake box to the canvas. The four fields above stopped being AUTHORED here: the
        /// owner's report was that on Pyre › Disc "Half extent X/Y and Bake W/H scale the disc and make no
        /// sense to a human next to Pyre's Size", and he is right — the generator already owns a size dial, so
        /// a second one beside it is two authorities over one number and the author has no way to know which
        /// one he is turning. The generator keeps its size; the box it draws into is simply the canvas.
        ///
        /// Called from <see cref="ShaperDocumentRenderer.RenderPhaseInto"/>, the one place that knows the
        /// canvas, before the layer resolves. A plain field write on a serializable sub-object: it dirties
        /// nothing, and resizing the canvas is already folded into every layer's cache key directly (the
        /// document's own <c>canvasWidth</c>/<c>canvasHeight</c> are part of <c>ShaperLayerKey.DocumentPart</c>),
        /// so a resize correctly invalidates every cached frame without this box needing to be hashed too.
        ///
        /// <b>T-0254 — returns the box.</b> <see cref="ShaperCompiler"/> still reads it off the instance fields
        /// (see <see cref="halfExtentX"/>'s doc for why), so this still writes them; the return value is for a
        /// caller that wants the fitted box without reaching back into the (now <c>[NonSerialized]</c>) fields.
        /// </summary>
        /// <param name="pixelSize">T-0198 — the canvas's own sample spacing (<see cref="ShaperDocument.pixelSize"/>).
        /// The half-extents are in CANVAS UNITS, not samples, and the canvas spans
        /// <c>0.5·(w−1)·pixelSize</c> either side of the origin (<c>ShaperDocumentRenderer.cs:237-238</c>), so a
        /// box sized in samples alone is only right while pixelSize is 1. At the owner's 2.49 it made every
        /// composite cover four tenths of the canvas it was supposed to fill, which reads as "the generator
        /// shrank" rather than as a unit error.</param>
        public FittedBox FitTo(int canvasWidth, int canvasHeight, float pixelSize)
        {
            int w = Mathf.Max(1, canvasWidth), h = Mathf.Max(1, canvasHeight);
            float ps = pixelSize > 0f ? pixelSize : 1f;
            // Half a sample wider each side than the grid's own half-extent, because the box bounds the sample
            // AREAS while the grid bounds their CENTRES — the same half-texel convention halfBand already uses.
            halfExtentX = w * 0.5f * ps;
            halfExtentY = h * 0.5f * ps;
            // One bake texel per canvas pixel. Any other ratio would resample the generator's own picture on
            // the way in for no gain: the raster is sampled at canvas resolution, and a composite root now
            // paints with these very texels (T-0191), so 1:1 is the only ratio that carries the generator's
            // colours across without a filtering step nobody asked for.
            bakeWidth = w;
            bakeHeight = h;
            return new FittedBox(halfExtentX, halfExtentY, bakeWidth, bakeHeight);
        }

        /// <summary>Fit every composite in a subtree. A no-op on a tree with none.</summary>
        public static void FitTree(ShaperNode node, int canvasWidth, int canvasHeight, float pixelSize)
        {
            if (node == null || !node.enabled) return;
            if (node.kind == ShaperNodeKind.Composite && node.composite != null)
                node.composite.FitTo(canvasWidth, canvasHeight, pixelSize);
            if (node.children == null) return;
            for (int i = 0; i < node.children.Count; i++)
                FitTree(node.children[i], canvasWidth, canvasHeight, pixelSize);
        }

        /// <summary>
        /// T-0198 — why this node's last bake produced nothing, or null when it produced a picture. A generator
        /// is third-party code from the shape engine's point of view (that is the whole premise of the escape
        /// hatch), so it can throw; the compiler catches that and records it here rather than letting one
        /// generator take the whole preview down with it (<c>ShaperCompiler.EmitComposite</c>). Never serialized:
        /// it describes the last RENDER, not the authored document, and a stale one saved into an asset would
        /// report a failure that no longer happens.
        /// </summary>
        [NonSerialized] public string lastRenderError;

        /// <summary>
        /// The first composite failure anywhere in a subtree, formatted for a status line, or null when every
        /// generator in it rendered. Walks disabled nodes too: a node the author has just switched off should
        /// stop REPORTING as well as stop drawing, and it does — <see cref="lastRenderError"/> is only ever set
        /// by a bake that actually ran.
        /// </summary>
        public static string FirstError(ShaperNode node)
        {
            if (node == null) return null;
            if (node.kind == ShaperNodeKind.Composite && node.composite != null
                && !string.IsNullOrEmpty(node.composite.lastRenderError))
                return node.composite.lastRenderError;
            if (node.children == null) return null;
            for (int i = 0; i < node.children.Count; i++)
            {
                string e = FirstError(node.children[i]);
                if (e != null) return e;
            }
            return null;
        }
    }
}
