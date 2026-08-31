using Laubrary.Pyre;
using Laubrary.Shaper;

namespace Laubrary.PyreShaper
{
    /// <summary>
    /// T-0112 — the per-generator classification the task requires: "confirm which of the nine fall into reason
    /// 1 vs reason 2, and record that per-generator, not as a blanket assumption."
    ///
    /// <b>The finding, stated once here rather than nine separate times: 9 of 9 are
    /// <see cref="ShaperCompositeReason.NotYetSplit"/>; 0 of 9 are <see cref="ShaperCompositeReason.AuthoredData"/>.</b>
    /// Every one of the nine is a PROCEDURAL generator — an additive energy field, a turbulence sum, a LUT read —
    /// never a baked sprite or hand-painted asset, so none of them qualifies for §6.2 reason 1 ("its default fill
    /// is authored data, not a rule"). All nine are technical debt of the same kind: a computed edge rule that
    /// nobody has yet split from its paint recipe into Shaper's shape/fill contract (§6.2 reason 2).
    ///
    /// <b>The finer classification each entry's <see cref="PyreCompositeCatalogEntry.paletteIndifferent"/> carries</b>
    /// is the task's other explicit correction: "no generator's colour and shape are mathematically fused... in
    /// four, the outline is completely indifferent to the palette; in the rest, the palette's transparency is
    /// MULTIPLIED into an edge rule the generator computed on its own." Of these nine, that split is
    /// <b>Orb / Torch / Arc Burst / Plasma Bloom = indifferent (4)</b>,
    /// <b>Inferno / Fork Blast / Jet / Radial Jet / Explosive Jet = veil-multiplied (5)</b> — matching
    /// N-entanglement-split.md's "eleven generators examined... four... the rest" once Fire and Fireball (not
    /// among these nine) are set aside. It does not change which reason applies — a veil-multiplied generator is
    /// still fully procedural, not authored data — but it is exactly the fact a future split-out task needs: the
    /// four are candidates for splitting FIRST, because their edge rule already has zero dependency on the
    /// palette to begin with.
    /// </summary>
    public readonly struct PyreCompositeCatalogEntry
    {
        public readonly string displayName;
        public readonly bool paletteIndifferent;
        public readonly string reasonNote;

        public PyreCompositeCatalogEntry(string displayName, bool paletteIndifferent, string reasonNote)
        {
            this.displayName = displayName;
            this.paletteIndifferent = paletteIndifferent;
            this.reasonNote = reasonNote;
        }
    }

    public static class PyreCompositeCatalog
    {
        const string Debt = "Procedural generator (an energy field / turbulence sum / LUT read), not authored "
            + "data — reason 2, NotYetSplit. F-decomposability.md PART 4 names the split path (an opacity "
            + "transfer function separated from the palette lookup) but it has not been executed. Hosted "
            + "unmodified via the T-0112 composite escape hatch in the meantime.";

        /// <summary>All nine, in the order SHAPER_THE_DESIGN.md / the original Pyre report list them.</summary>
        public static readonly PyreCompositeCatalogEntry Inferno =
            new PyreCompositeCatalogEntry("Inferno", false, Debt + " Palette-DEPENDENT (veil-multiplied cohort).");

        public static readonly PyreCompositeCatalogEntry ForkBlast =
            new PyreCompositeCatalogEntry("Fork Blast", false, Debt + " Palette-DEPENDENT (veil-multiplied cohort).");

        public static readonly PyreCompositeCatalogEntry Orb =
            new PyreCompositeCatalogEntry("Orb", true, Debt + " Palette-INDIFFERENT (PyreOrb.cs:617-635 — the "
                + "256-entry LUT has no alpha column at all; the alpha window reads only the energy field). "
                + "Hosted as this task's one fully wired, fully verified example.");

        public static readonly PyreCompositeCatalogEntry Torch =
            new PyreCompositeCatalogEntry("Torch", true, Debt + " Palette-INDIFFERENT (TorchForm.cs:207-215 — "
                + "alpha from a separate _Fp plane, colour from a separately-cooled _C; two sheets, two rules, "
                + "zero contact).");

        public static readonly PyreCompositeCatalogEntry ArcBurst =
            new PyreCompositeCatalogEntry("Arc Burst", true, Debt + " Palette-INDIFFERENT "
                + "(ArcBurstForm.cs:496-504).");

        public static readonly PyreCompositeCatalogEntry PlasmaBloom =
            new PyreCompositeCatalogEntry("Plasma Bloom", true, Debt + " Palette-INDIFFERENT "
                + "(PyrePlasmaBloom.cs:587-594).");

        public static readonly PyreCompositeCatalogEntry Jet =
            new PyreCompositeCatalogEntry("Jet", false, Debt + " Palette-DEPENDENT (veil-multiplied cohort; "
                + "PyreJetEngine's alpha = smoothstep((H-lo)/soft)·ceiling, and the ramp's per-stop transparency "
                + "multiplies into that ceiling).");

        public static readonly PyreCompositeCatalogEntry RadialJet =
            new PyreCompositeCatalogEntry("Radial Jet", false, Debt + " Palette-DEPENDENT (veil-multiplied "
                + "cohort — same JetFormBase family as Jet).");

        public static readonly PyreCompositeCatalogEntry ExplosiveJet =
            new PyreCompositeCatalogEntry("Explosive Jet", false, Debt + " Palette-DEPENDENT (veil-multiplied "
                + "cohort — same JetFormBase family as Jet).");

        public static readonly PyreCompositeCatalogEntry[] All =
        {
            Inferno, ForkBlast, Orb, Torch, ArcBurst, PlasmaBloom, Jet, RadialJet, ExplosiveJet,
        };

        /// <summary>Build a fully-declared <see cref="ShaperCompositeDef"/> hosting <paramref name="form"/>,
        /// stamped with its catalog entry's reason. All nine currently resolve to
        /// <see cref="ShaperCompositeReason.NotYetSplit"/> — see this class's own doc for why.</summary>
        public static ShaperCompositeDef Build(PyreForm form, in PyreCompositeCatalogEntry entry,
                                               float halfExtentX = 64f, float halfExtentY = 64f,
                                               int bakeWidth = 128, int bakeHeight = 128)
        {
            return new ShaperCompositeDef
            {
                source = new PyreFormCompositeSource { form = form },
                reason = ShaperCompositeReason.NotYetSplit,
                reasonNote = entry.reasonNote,
                halfExtentX = halfExtentX,
                halfExtentY = halfExtentY,
                bakeWidth = bakeWidth,
                bakeHeight = bakeHeight,
            };
        }
    }
}
