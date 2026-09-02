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

        /// <summary>
        /// T-0173 — WHICH of §6.2's two reasons this entry declares. It defaults to
        /// <see cref="ShaperCompositeReason.NotYetSplit"/>, which is what all nine hosted forms are and what
        /// <see cref="PyreCompositeCatalog.Build"/> used to hardcode; the parameter exists because the stateful
        /// SIMULATIONS this catalog now also classifies are not debt of the same kind (see
        /// <see cref="PyreCompositeCatalog.Fire"/>). Carrying it on the entry rather than at the call site keeps
        /// the classification where the justification for it is written.
        /// </summary>
        public readonly ShaperCompositeReason reason;

        public PyreCompositeCatalogEntry(string displayName, bool paletteIndifferent, string reasonNote,
                                         ShaperCompositeReason reason = ShaperCompositeReason.NotYetSplit)
        {
            this.displayName = displayName;
            this.paletteIndifferent = paletteIndifferent;
            this.reasonNote = reasonNote;
            this.reason = reason;
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

        // ── T-0173: the two stateful simulations ─────────────────────────────────────────────────────────
        //
        // These are NOT hosted PyreForms and so are not part of `All` above — Fire and Fireball are ShapeForm
        // enum cases in Pyre with renderer-side harnesses, which is exactly why Shaper had no fire until they
        // were given their own IShaperCompositeSource implementations (FireCompositeSource,
        // FireballCompositeSource). They are classified here rather than in a second catalog because §6.2's
        // requirement is that a monolithic generator states its reason, and this is the file that states them.
        //
        // THEY DECLARE REASON 1, NOT REASON 2, AND THE DISTINCTION IS THE WHOLE POINT. Reason 2 (NotYetSplit)
        // "must read as technical debt, not architecture" — it promises that somebody could separate this
        // generator's edge rule from its paint recipe and nobody has yet. That promise is false for a
        // simulation. A flame's picture at frame N is not computed from a rule at all; it is the accumulated
        // state of N iterations, and heat carried by a velocity field has no analytic distance function to
        // extract even in principle (FireSim.cs:7-19 makes exactly this argument about why the sim is allowed
        // to be stateful when nothing else in the engine is). Filing them as debt would put an item on a
        // backlog that can never be closed. Reason 1 is the honest one: the picture IS data — produced by
        // iteration rather than by hand, but data, with no procedural rule underneath to decompose.

        const string SimData = "Stateful simulation: the picture is accumulated grid state replayed from frame "
            + "0, not a closed-form rule, so there is no edge rule to separate from a paint recipe — reason 1, "
            + "AuthoredData, and PERMANENTLY so rather than pending a split. ";

        public static readonly PyreCompositeCatalogEntry Fire =
            new PyreCompositeCatalogEntry("Fire", true, SimData + "Pyre's fluid fire sim (heat and fuel carried "
                + "by a velocity field, FireSim.cs:54), hosted unmodified. Palette-INDIFFERENT: the sim's "
                + "coverage comes from the heat field through a threshold (FireSim.cs:299-304) and the ramp "
                + "only colours what that field already decided.",
                ShaperCompositeReason.AuthoredData);

        public static readonly PyreCompositeCatalogEntry Fireball =
            new PyreCompositeCatalogEntry("Fireball", true, SimData + "Pyre's cellular doom-fire sim "
                + "(FireballSim.cs:32), hosted unmodified. Palette-INDIFFERENT for the same reason as Fire: "
                + "coverage is the thresholded heat field (FireballSim.cs:145-148), the ramp only tints it.",
                ShaperCompositeReason.AuthoredData);

        /// <summary>The stateful simulations, in the order the window offers them.</summary>
        public static readonly PyreCompositeCatalogEntry[] Sims = { Fire, Fireball };

        /// <summary>Build a fully-declared <see cref="ShaperCompositeDef"/> hosting <paramref name="form"/>,
        /// stamped with its catalog entry's reason. All nine forms resolve to
        /// <see cref="ShaperCompositeReason.NotYetSplit"/> — see this class's own doc for why.</summary>
        public static ShaperCompositeDef Build(PyreForm form, in PyreCompositeCatalogEntry entry,
                                               float halfExtentX = 64f, float halfExtentY = 64f,
                                               int bakeWidth = 128, int bakeHeight = 128)
        {
            return BuildSource(new PyreFormCompositeSource { form = form }, entry,
                               halfExtentX, halfExtentY, bakeWidth, bakeHeight);
        }

        /// <summary>
        /// T-0173 — the same build for a source that is NOT a hosted <see cref="PyreForm"/> (the two stateful
        /// simulations today, anything else that implements the interface tomorrow). <see cref="Build"/> now goes
        /// through this too, so a composite def is stamped in exactly one place regardless of what it hosts.
        /// </summary>
        public static ShaperCompositeDef BuildSource(IShaperCompositeSource source,
                                                     in PyreCompositeCatalogEntry entry,
                                                     float halfExtentX = 64f, float halfExtentY = 64f,
                                                     int bakeWidth = 128, int bakeHeight = 128)
        {
            return new ShaperCompositeDef
            {
                source = source,
                reason = entry.reason,
                reasonNote = entry.reasonNote,
                halfExtentX = halfExtentX,
                halfExtentY = halfExtentY,
                bakeWidth = bakeWidth,
                bakeHeight = bakeHeight,
            };
        }

        /// <summary>The catalog entry whose <c>displayName</c> matches, or a default (null-named) entry when
        /// nothing does — the same "match by display name, fall back to an undeclared def" shape the window's
        /// generator assignment already relies on.</summary>
        public static PyreCompositeCatalogEntry Find(string displayName)
        {
            foreach (var e in All) if (e.displayName == displayName) return e;
            foreach (var e in Sims) if (e.displayName == displayName) return e;
            return default;
        }
    }
}
