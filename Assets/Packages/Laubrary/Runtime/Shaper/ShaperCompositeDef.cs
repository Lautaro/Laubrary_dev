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

        /// <summary>The full render, kept for a composite ROOT layer's own albedo (§6.2: "its fill picker shows
        /// exactly one entry — itself"). A composite nested in a Bag never reads this; the bag's own fill paints
        /// the fused silhouette instead, and this generator contributes only its (pseudo-)distance to the fold.</summary>
        public Color32[] pixels;
    }

    /// <summary>
    /// T-0112 — the composite generator's authored declaration. Used when a <see cref="ShaperNode"/>'s
    /// <see cref="ShaperNode.kind"/> is <see cref="ShaperNodeKind.Composite"/>.
    ///
    /// The whole contract is one line (SHAPER_THE_DESIGN.md B1): "it must publish coverage, and it may publish
    /// nothing else." <see cref="reason"/>/<see cref="reasonNote"/> are the second half of §6.2's policy, which
    /// matters as much as the first: "monolithic must be a DECLARED REASON, never a DECLARED EXEMPTION."
    /// </summary>
    [Serializable]
    public class ShaperCompositeDef
    {
        /// <summary>The hosted generator. Null renders as empty — no coverage anywhere — the same "a
        /// half-configured fill never renders empty [wrongly]" posture FC-6.5 already takes for a Gradient with
        /// no gradient asset or a Texture with none assigned; a composite with no source assigned yet is a
        /// legal, if useless, authoring state, never a null-reference crash.</summary>
        [SerializeReference] public IShaperCompositeSource source;

        /// <summary>WHY this generator bypasses the shape/fill split. Never omitted, never inferred — a
        /// monolithic generator with no stated reason is exactly the "fatal hole" §6.1 warns the escape hatch
        /// becomes without this field: "a second, undocumented model growing inside the first."</summary>
        public ShaperCompositeReason reason = ShaperCompositeReason.NotYetSplit;

        /// <summary>
        /// Free-text justification. REQUIRED for both reasons, verified by <see cref="HasDeclaration"/>:
        /// <see cref="ShaperCompositeReason.AuthoredData"/> still has to say WHICH asset/bake it is;
        /// <see cref="ShaperCompositeReason.NotYetSplit"/> "must read as technical debt, not architecture"
        /// (§6.2), which needs a sentence, not a bare enum value.
        /// </summary>
        [TextArea(2, 5)] public string reasonNote = "";

        /// <summary>
        /// Half-extent, in the node's own local canvas units, of the box the generator's picture is baked into —
        /// a composite generator's equivalent of a primitive's own declared half-extents
        /// (<see cref="ShaperBakedPrimitive.halfExtentX"/>/<c>Y</c>). The source's render is assumed to have
        /// faded to (near) zero alpha before this box's edge; the sampler CLAMPS rather than hard-clips at the
        /// boundary (see <see cref="ShaperEvaluator"/>'s CompositeSample case), so authoring too small a box
        /// smears the generator's own edge flat instead of cutting it — a visible authoring mistake, not a
        /// silent one.
        /// </summary>
        public float halfExtentX = 64f;
        public float halfExtentY = 64f;

        /// <summary>
        /// Bake resolution in texels, independent of the canvas's own sampling resolution — a composite
        /// generator renders its own whole picture ONCE per compile, at this fixed resolution, and the shape
        /// stage then samples that raster like a texture (bilinear). Raising this sharpens the generator's own
        /// silhouette; it does not sharpen with camera zoom the way an analytic primitive's edge does — a real,
        /// named limitation of the escape hatch, not an oversight.
        /// </summary>
        public int bakeWidth = 128;
        public int bakeHeight = 128;

        /// <summary>True when §6.2's declaration is actually present — a compliance pass (<c>ShaperCompositeAudit</c>)
        /// counts this the same way <c>ShaperCompositeDef.HasDeclaration</c>'s doc promises.</summary>
        public bool HasDeclaration => !string.IsNullOrWhiteSpace(reasonNote);
    }
}
