# T-0110 — VERIFICATION

Indexed-strip fill with per-slot height, for Shaper's fill stage. This document is the honest account of what
was built, what was measured, what was independently adversarially reviewed, and what is explicitly NOT yet
true — written so a reader never has to take a claim on faith.

Design document: `D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0110\STRIP-SPEC.md` (8 parts — reference-app maths,
3D Shaper's own maths, how it plugs into the fill contract, the shipped dial list, the Lipschitz decision, the
concrete code-change list, a reconciliation between two independent design passes, and a correction of one
verified-wrong claim).

Contact sheet: `D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0110\strip-contact-sheet.png` (6 cells, eyeballed
personally before this report was written — see Part 4).

---

## Part 1 — what was built

A fifth fill kind, `ShaperFillKind.IndexedStrip`, alongside Shaper's existing Solid/Gradient/RampByQuantity/
Texture. Its palette is a hand-authored list of slots (`ShaperStripSlot { Color color; float height; }`),
selected per pixel by one of two parameterisations (`ShaperStripParameterisation.Angular` — the angle around
the shape's own centre — or `.Projection` — a linear projection across it), with whole-number repeats,
orientation and offset dials, and a "reach" control expressed as a fraction of the shape's own local
half-extent (default 1.0, meaning "the shape's own shorter half-extent" — so the default already reads as
"the whole shape is patterned," matching the design brief's "turn it to maximum and it covers the WHOLE
shape").

Per pixel: the selected slot's colour is blended against a single authored "plain" colour by a continuous
`edgeCoverage` value (1 at the silhouette edge, ramping to 0 at the reach depth and beyond); the SAME
`edgeCoverage` blends the slot's height between a quarter weight (deep in the "plain" interior) and full
weight (right at the edge, "patterned"), so colour and height never disagree about where the pattern is. The
common `heightDelta` dial every fill kind already has still adds underneath, as a uniform nudge on the whole
fill.

Files touched (all in `D:\UNITY\Laubrary Dev - Shaper`, the second working copy / second Unity editor for this
whole Shaper rebuild, port 7801 — none of this touches the primary `D:\UNITY\Laubrary Dev` editor):

- `Assets/Packages/Laubrary/Runtime/Shaper/ShaperFillContract.cs` — the new enum value + `ShaperStripParameterisation`.
- `Assets/Packages/Laubrary/Runtime/Shaper/ShaperFillDef.cs` — `ShaperStripSlot` + the authored dials + `RequiredSet()`.
- `Assets/Packages/Laubrary/Runtime/Shaper/ShaperFillCompiler.cs` — the compiled `ShaperFillOp` fields + `BakeStrip`.
- `Assets/Packages/Laubrary/Runtime/Shaper/ShaperFillOps.cs` — the per-sample `Sample()` case + the `FillTile` edge-sheet gate.
- `Assets/Packages/Laubrary/Editor/Shaper/ShaperFillAudit.cs` — 7 new tests (`ST1`–`ST7`) + a contact-sheet renderer.
- `Assets/Packages/Laubrary/Editor/Shaper/ShaperBorderAudit.cs` — 9 mechanical call-site fixes for `Sample()`'s new signature.

Nothing else changed. No new `[MenuItem]`, no new editor window, no ZUI surface (this is a pure engine/fill-
kind addition; the picker UI for it is a later task, not part of T-0110's scope per the design doc's own
task list). Undo does not apply here — there is no interactive editor tool yet for this fill, only the
compiled data model and the tests.

---

## Part 2 — what was measured

Via the Unity CLI (`unity.exe command eval_file`) against the Shaper editor, never the Test Runner (per this
project's standing rule):

- **Compiles clean**, twice (once after the initial build, once after a fix — see Part 3).
- **All 22 pre-existing `FT-*` tests still PASS** — the new fill kind introduces zero regressions in the
  existing four kinds or the resolver's shared machinery.
- **All 7 new `ST-*` tests PASS**, after the fix in Part 3:
  - `ST-1` — an empty palette degenerates to `Solid(plainColour)`, with a diagnostic, exactly like the
    existing null-gradient/null-texture fallbacks.
  - `ST-2` — reach = 1000 (far beyond the shape's own half-extent) patterns even the dead centre of a disc
    (measured: albedo matches the slot's decode within 0.02, height reads 4.00 against an authored 4.0 at
    full weight).
  - `ST-3` — reach ≈ 0 leaves the interior plain (measured: albedo matches the plain colour's decode, height
    reads 1.00 = 4.0 × the quarter weight, exactly).
  - `ST-4` — the common `heightDelta` dial (authored 2.0) adds under the slot's height (4.0 at near-full
    weight): measured 6.00.
  - `ST-5` — `RequiredSet()` is exactly `EdgeDistance`, unconditionally.
  - `ST-6` — an authored `stripRepeats` of 3.7 compiles to 4 (rounded, never fractional).
  - `ST-7` — both parameterisations genuinely select different slots at different points around the shape
    (not a disguised constant fill) — measured 4 distinct colours (Angular) and 3 (Projection) around an
    8-point ring.
- **Contact sheet rendered and personally looked at** (not just measured) — `strip-contact-sheet.png`, 6
  cells: the reach control isolated (sunburst vs. rim-only), both parameterisations, orientation/offset, a
  greyscale render of the height buffer proving colour and height read the same slot index, and one composite
  cell honestly captioned as ordinary paint-order compositing rather than a real depth test (see Part 3).

---

## Part 3 — a real bug the audit caught, and the honest scope limits

**A real bug, caught by the tests themselves.** The first run of `ST-3` failed: the interior of a near-zero-
reach strip was still fully patterned instead of plain. Root cause: `ShaperFillOps.FillTile`'s edge-distance
sheet gate only recognised `Gradient.ByEdgeDistance`, so `IndexedStrip`'s `edge` value was silently always 0
regardless of the shape's real geometry — which happens to look identical to "reach = infinite" (also always
patterned), so `ST-2` passed by coincidence while `ST-3` caught it. Fixed by adding `IndexedStrip` to that
gate; recompiled and reran the whole battery — all 29 tests (22 + 7) now pass. This is recorded here rather
than smoothed over because it is exactly the kind of "declared a requirement but didn't wire the read" defect
this project's own audits exist to catch, and it is worth knowing the mechanism actually caught one on this
task.

**Honest, deliberate, pre-existing scope limits — not gaps this task introduced, and not fixed here:**

1. **Per-slot height does not reach shading, shadows, or a cross-layer depth test.** It reaches exactly one
   place at runtime: `ShaperFillBuffers.height`, the accumulated per-pixel height buffer — precisely the
   documented interface this task was scoped to populate ("T-0110's whole interface," in the fill contract's
   own words). It does NOT reach the surface normal — confirmed directly against `ShaperNormals.cs`'s own doc
   comments (`LR-3.3`/`LR-3.4`): a fill's height delta is explicitly, deliberately excluded from the normal
   provider in this wave, for cost and correctness reasons (screen-space differencing of paint-authored height
   would describe the paint's bumps, not the true surface, and has no apron mechanism to do cheaply). Since
   shading, specular and rim lighting all derive from that normal, none of them see the strip's relief either.
   And `grep`ing the whole shipped `Runtime/` and `Editor/` trees for `ShaperFillBuffers.height` finds it read
   by nothing except the editor audits — there is no cross-layer depth-test consumer anywhere yet, for any of
   the five fill kinds, not only this one. This was checked directly against the code, not assumed — an
   earlier draft of the design spec claimed the height DOES reach a point light's attenuation via
   `ShaperLightScene.pointZ`; that claim was traced to its one assignment site and found to be wrong (it reads
   the shape's own extrusion height only, before any fill has run), and corrected in `STRIP-SPEC.md` Part 8.
   The design brief's "the raised slots catch light and cast shadows, and a raised strip can win the depth
   test against a neighbouring layer" describes the REFERENCE app's behaviour and is the eventual goal; this
   task restores the data model that makes it possible (colour + height, composing correctly), not the
   lighting/depth-test wiring, which does not exist yet for any fill.
2. **No Lipschitz/slope bound is declared for the strip's height.** Checked directly: `ShaperBound.cs` bounds
   only the shape tree's own distance field, unrelated to a fill; the height stage's own slope fields
   (`ShaperHeightOp.extrusionSlope`/`bevelSlope`/`composedSlope`) belong to the per-LAYER extrusion/bevel
   profile, not a fill's `heightDelta`; and nothing in the resolve/march machinery reads a fill's height at
   all. A fill is architecturally outside the bound system for all five kinds, not only this one, so there is
   nothing for this task to declare that the other four don't already skip.

---

## Part 4 — adversarial review

Independent pass, in the Shaper editor (port 7801), via the Unity CLI (`unity.exe command eval_file`), never the
Test Runner, per this project's standing rule. First re-ran the whole existing FT+ST battery unchanged as a
baseline (all 29 PASS, byte-for-byte the same report as Part 2), then added five new adversarial tests — `ST-8`
through `ST-12` — directly to `ShaperFillAudit.cs` (same file, same style, wired into `RunAll()`), rather than a
disposable throwaway: they now stand as a permanent part of the conformance battery for whoever touches this fill
next. **All 34 tests (22 FT + 12 ST) PASS.** No bug was found that needed fixing — every adversarial input landed
on an existing defensive guard rather than falling through one, and that guard was measured, not assumed.

**What was tried, and what it measured:**

- **`ST-8` — extreme slot counts.** 200 hand-distinguishable slots: 0/16384 non-finite samples, 32 distinct
  colours over a 32-point ring (genuinely indexed, not degenerated to a constant). A single-slot strip with a
  **negative** height (-3) at 5 repeats: every one of 16 ring samples matched that one colour and stayed negative
  (0/16 wrongly positive) — confirms a slot can sink as well as rise, which the design doc states but no existing
  ST test had exercised.
- **`ST-9` — repeats extremes.** Negative (-7) and non-finite (NaN, +Infinity) authored `stripRepeats` all
  compile to the floor of 1, as `BakeStrip`'s `Mathf.Max(1, Mathf.RoundToInt(...))` promises. **A genuine, named
  gap: there is no upper clamp on `stripRepeats`** — the superseded SS-4.2 design wanted `[1,16]`, and the
  adopted Part 7 design (what actually shipped) never restates a ceiling. Authoring `1e7` bakes to a literal
  `10000000` and does not crash or produce a non-finite sample (0/16384) — but it does exactly what float32
  precision predicts: the running parameter's fractional part collapses at that magnitude, so the tile
  degenerates to essentially 2 colours instead of a fine subdivision. This is a real, measured limitation, not a
  crash — reported here rather than silently patched, because clamping it to some arbitrary ceiling is a design
  decision (what's a sane maximum "repeats" for a hand-authored strip?) this task was not asked to make, in the
  same spirit as SS-4.3's refusal to clamp per-slot height to the reference's legacy range without being asked.
- **`ST-10` — NaN/Infinity on the three UNGUARDED dials.** Read `BakeStrip` directly: only the per-slot heights
  and the shared `heightDelta` dial go through `ShaperFillCompiler.FiniteOrZero`. `stripReach`,
  `stripOrientationDegrees` and `stripOffset` do not — confirmed by reading the bake code, not inferred. Fed NaN,
  +Infinity (and a negative value) to each independently and scanned the WHOLE emitted tile (albedo, height,
  veil) for non-finite floats: 0/16384 in all seven cases. This is not a bake-time guard working — it is
  `ShaperFillOps.Sample`'s defensive slot-index clamp (`if (slot < 0) slot = 0`) catching a NaN/Infinity-derived
  index downstream, plus `Mathf.Max(reachPixels, MinPositive)` in `BakeStrip` happening to route around a NaN
  reach because of the specific ARGUMENT ORDER of Unity's `Mathf.Max(a, b)` (which returns `b` whenever the `a >
  b` comparison is false, and every comparison against NaN is false — so `Mathf.Max(NaN, 1e-6)` returns `1e-6`,
  not NaN, purely because the finite guard is the SECOND argument). That is a fragile way to be safe — it
  happens to hold today because of where the epsilon sits in the call, not because of an explicit `IsFinite`
  check — and it is exactly the class of accidental-not-designed safety this project's audits exist to surface
  rather than take on faith. It is not a live bug (measured: nothing breaks today) but a future edit to
  `BakeStrip` that reorders those arguments, or a JIT/runtime that resolves NaN comparisons differently, would
  reopen it silently. Worth a follow-up `FiniteOrZero` on these three dials for the same reason the height fields
  already have one, even though nothing failed here.
- **`ST-11` — a zero-radius (degenerate) disc.** 0/16384 non-finite samples; no exception, no divide-by-zero —
  the existing `DegenerateExtent`/`MinPositive` guards in `BakeStrip` (already used for the anchor and the reach
  span) cover this fill kind's own span computation too, without any strip-specific code needing to be added.
- **`ST-12` — measuring the continuous-vs-hard-cutoff choice directly (see the design opinion below), rather
  than trusting the code comment's derivation.** Walked a 40px radial line from a disc's centre to its edge at
  `reach = 0.5` and measured the largest SINGLE-SAMPLE jump in both height and albedo red. A hard cutoff (the
  reference app's own rule) would show one jump of `0.75 × slotHeight` (the full patterned/plain weight range) at
  exactly one sample. Measured: the worst single-sample jump was `0.30` against a hard-cutoff jump of `3.0` —
  **10% of what a seam would produce**, spread smoothly across the walk instead of concentrated at one boundary.
  This is the first direct measurement of that claim in this task's evidence trail; Parts 2/3 asserted it from
  reading the formula, this measures the pixels it actually produces.

**A documentation gap found, not a code bug.** `ShaperFillOps.cs`'s `IndexedStrip` case carries a substantial,
dated code comment ("T-0110 fix pass: the constant here was 1... Measured before this fix: a 40px-radius disc at
the default reach, sampled at its exact centre, returned the plain colour...") describing a SECOND real bug that
was found and fixed during this task's build — the `edgeCoverage` formula's constant was wrong by exactly the
amount that put the transition's start at the reach boundary instead of its end, which made the default
`reach = 1` fail to cover a disc's own centre (contradicting B6's headline claim) until it was changed from `1` to
`2`. This is a real, already-fixed, already-verified (by `ST-2`/`ST-3`, which both depend on the corrected
formula and both pass) defect. **It is not mentioned anywhere in this document's Part 3**, which currently
describes only ONE bug (the `FillTile` edge-sheet gate). Part 3's own framing — "a real bug the audit caught" —
undersells what actually happened: at least two independent defects were found and fixed before this document
was written, and only one made it into the write-up. Recorded here rather than silently amended into Part 3,
since fixing another section's account is outside this Part's own scope; whoever next edits Part 3 should add
this second bug so the "honest account" the top of this document promises stays honest about its own history.

**Opinions on the three design choices this document and `STRIP-SPEC.md` already flag as debatable, formed after
reading the code and the measurements above, not merely restating the existing rationale:**

1. **Continuous edge-coverage blend vs. the reference's hard patterned/plain cutoff — AGREE with the shipped
   choice.** `ST-12` gives this something the design doc's own justification didn't have: a direct pixel
   measurement, not just a read of the formula. A hard cutoff here would be the one place in this whole fill
   contract where a spatial parameter is discontinuous while every sibling (Gradient's four modes, Ramp, Texture)
   is continuous, and it would put a visible ring in the RELIEF — which, unlike a colour seam, cannot be
   softened by a light rig after the fact once a fill's height reaches shading in a later wave. The design's own
   observation that the reference's hard rule is recovered "to within one sample" as `stripInvReach` grows is
   also correct and was not re-derived here, only trusted, because it follows directly from the ramp's own
   algebra rather than needing a separate measurement.
2. **A single authored "plain" colour rather than a second full strip — AGREE, with a named reservation.** It is
   the right default for what B6 actually describes (paint a strip, sculpt a strip; the interior is a backdrop,
   not a second composition), and it avoids the duplicated-dial smell CLAUDE.md's own menu/dial guidance warns
   about. The reservation: an author who genuinely wants the reference app's two-material look (e.g. a metal rim
   over a rock body, both with their own relief) has no way to express that inside one `IndexedStrip` fill today
   — they would need two stacked fills, one `IndexedStrip` for the rim and a `Solid`/`Gradient` beneath it for
   the body, which is achievable with what already ships (multiple fills on sibling/nested nodes) and costs
   nothing new, so this is not a gap so much as a "the answer is compose two fills, not add a second palette to
   one" — worth a line in future authoring docs rather than a code change.
3. **The common `heightDelta` dial composing additively underneath — AGREE.** `ST-4` (existing) and `ST-8`'s
   negative-height case (new) together confirm the composition holds in both directions — a positive common
   nudge over a negative slot, and a negative slot alone — with no sign-flip or clamp surprise. This keeps
   `IndexedStrip` consistent with every other kind's own `heightDelta` semantics (FC-2.5's "ADDED to the shape's
   own height") rather than carving out a kind-specific exception, which is the right default: an exception here
   would be one more rule an author has to remember about ONE fill kind out of five.

**Net verdict:** the fill kind holds up under deliberately hostile input — huge and tiny slot counts, negative
and non-finite repeats, non-finite reach/orientation/offset, a degenerate shape, and a direct pixel-level test of
its own headline design claim. Nothing found here required a code change. The two items worth a follow-up task
rather than a blocking fix are named above: the missing `FiniteOrZero` guard on three dials (currently saved only
by an incidental `Mathf.Max` argument order) and the missing upper clamp on `stripRepeats` (currently saved only
by graceful float-precision degradation) — both are "works today for a reason nobody designed," which is exactly
the gap between compiling and being verified this project's audits exist to close.

---

## Part 5 — what this hands over as, and what is NOT yet true

**Done and verified:** the data model (a fill kind whose palette carries colour and height together), the
compile pipeline, the per-sample evaluation, and a conformance audit that includes a test the audit itself
proved is worth having (Part 3). This is, in the design document's own words, "the cheapest thing that proves
the whole colour-plus-height fill model" — and it does prove it, at the level the fill stage alone can prove
anything (paint-time colour and height, correctly composed).

**NOT yet true, and not claimed:** there is no authoring UI for this fill (no ZUI window can create/edit a
strip yet — out of scope per the design doc's own task breakdown), the strip's height does not yet visibly
sculpt anything (no consumer reads `ShaperFillBuffers.height` outside the audits, for any fill kind), and the
reference app's "divider" behaviour (a raised strip winning a depth test against a neighbouring layer) is not
built — it requires wiring the resolve/lighting stages to a fill's height at all, which is a separate,
later task, not this one.
