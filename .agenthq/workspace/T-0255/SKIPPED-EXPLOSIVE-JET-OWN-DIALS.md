# Skipped: Explosive Jet's own dials (T-0255)

**Scope note, not a dial map.** Explosive Jet's SHARED dials (everything it inherits from `JetSettings`) already got the
naming pass — see `JET-DIAL-MAP-2-jetsettings.md`. Its variant type field got `[ZUILabel("Explosive jet type")]`. What
is still jargon is Explosive Jet's OWN ~120 dials, declared in
`Assets/Packages/Laubrary/Runtime/Pyre/Forms/Kiln/Jet/ExplosiveJetProgram.cs`:

- `ExplosiveJetSettings` itself (line 235): `bias, srcR, swirl, spin, lobes, lobeDepth, lobeKick, rootK, ringFlat,
  ringArc, warpSpin, blasts, blastSpan, blastSkew, blastFront, velSpread, swell, hold, shrink, shrinkAt, leadDie,
  opaq, shedSwell, fracture, fracture2` (~26 fields) — the radial-arc dials it shares in KIND with `RadialJetSettings`
  (already named there) plus the gen 4/5 detonation-clock and death dials.
- Seven further nested `[Serializable]` classes it owns, each its own reflected box: `ExplosiveBlast` (line 64, 5
  fields), `ExplosiveFracture` (line 80, 12), `ExplosiveFracture2` (line 110, 8), `ExplosiveFlash` (line 132, 5),
  `ExplosiveChunks` (line 148, 9), `ExplosiveGobs` (line 172, 11), `ExplosiveDust` (line 200, 13) — roughly 63 more
  fields.

That is on the order of 90 further dials with real gen-4/5/6/7 detonation mechanics (fracture geometry, chunks/gobs/
dust debris, the blast schedule) that deserve the same read-the-render-code-then-write-a-map treatment the rest of
this task got, not a rushed pass. Given the size of everything else in this card (Arc Burst's 198 fields, Fork
Blast, Inferno, Torch's ~87, and the rest of the Jet family), I ran out of the budget I could respons­ibly spend on
one card and stopped here rather than name ~90 fields from tooltips alone without checking the render code the way
the rest of this pass did. Recommend a follow-up card scoped to exactly this file.
