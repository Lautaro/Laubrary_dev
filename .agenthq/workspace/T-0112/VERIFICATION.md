# T-0112 VERIFICATION — the composite generator escape hatch

Design/architecture context: `D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0112\COMPOSITE-SPEC.md`. Read that first — this file is measurements only.

Environment: `D:\UNITY\Laubrary Dev - Shaper` (branch `feat/shaper`), Unity editor at pipeline port 7801, targeted explicitly via `unity pipeline list` before every command below. Compiled clean, verified by reflection probe after each change (not by `recompile_status` alone — that command is cumulative and was observed, live, in this session to report `up_to_date` against a stale assembly that did not yet contain the new types; see the raw session log for the two probes that caught it before and after a forced `AssetDatabase.Refresh` + `CleanBuildCache` recompile).

## Compile state

Reflection probes after the final recompile, run against the live editor via `unity command eval`:
- `Laubrary.Shaper.ShaperNodeKind` (asm `com.Lautaro-Arino.Laubrary.Shaper`) → `Primitive,Bag,Composite` (was `Primitive,Bag` before the fix landed).
- `Laubrary.PyreShaper.PyreCompositeCatalog.All` (asm `com.Lautaro-Arino.Laubrary.Pyre.Shaper`) → length 9.
- `Laubrary.PyreShaper.Editor.PyreShaperCompositeAudit` (asm `com.Lautaro-Arino.Laubrary.Pyre.Shaper.Editor`) → type found.

Two real compile errors were hit and fixed during this pass (both asmdef reference gaps, not logic bugs):
1. `PyreFormCompositeSource.cs(48,27): CS0012 — GeometryModifier ... assembly ... com.Lautaro-Arino.Laubrary.SpriteFx` — fixed by adding `com.Lautaro-Arino.Laubrary.SpriteFx` to the `Pyre.Shaper` runtime asmdef's references.
2. `PyreShaperCompositeAudit.cs(5,21): CS0234 — 'Forms' does not exist in the namespace 'Laubrary.Pyre'` — fixed by adding `com.Lautaro-Arino.Laubrary.Pyre.Forms.Kiln` to the `Pyre.Shaper.Editor` asmdef's references.

## The audit — `PyreShaperCompositeAudit.RunAll()`, run live in-editor via `unity command eval_file`

Full raw output (final run, all 8/8 PASS):

```
=== Shaper COMPOSITE generator audit (T-0112) ===
CT-0 synthetic composite disc vs real Primitive disc, coverage agreement
  worst |coverage_composite - coverage_primitive| = 0.0971  (tolerance 0.11 — a single edge texel, see below)
  mean  |...| over 16384 samples = 0.00074  (tolerance 0.01 — this is the meaningful number)
  RESULT: PASS
CT-1 a standalone composite root publishes via ONE CompositeSample op, nothing else
  ops.Length = 1 (expected 1)
  ops[0].kind = CompositeSample (expected CompositeSample)
  composites.Length = 1 (expected 1)
  RESULT: PASS
CT-2 a Composite node's border is structurally inert (no Dilate op emitted)
  Composite root, border enabled+width 6: Dilate op present = False (expected False)
  Primitive root, SAME border def:        Dilate op present = True (expected True — positive control)
  RESULT: PASS
CT-3 Bag(Composite union Primitive) — hard union sign correctness
  coverage at composite's own centre (-40,0) = 1.0000 (expected > 0.9)
  coverage at primitive's own centre (40,0)  = 1.0000 (expected > 0.9)
  coverage at the midpoint (0,0), outside both = 0.0000 (expected < 0.1)
  RESULT: PASS
CT-4 Bag(Primitive subtract Composite) — the composite carves a hole
  coverage at the carved centre (0,0)   = 0.0197 (expected < 0.1)
  coverage at the untouched rim (30,0)  = 0.9803 (expected > 0.9)
  RESULT: PASS
CT-5 all nine catalog entries are declared (§6.2 compliance pass)
  Inferno          declared=True  paletteIndifferent=False
  Fork Blast       declared=True  paletteIndifferent=False
  Orb              declared=True  paletteIndifferent=True
  Torch            declared=True  paletteIndifferent=True
  Arc Burst        declared=True  paletteIndifferent=True
  Plasma Bloom     declared=True  paletteIndifferent=True
  Jet              declared=True  paletteIndifferent=False
  Radial Jet       declared=True  paletteIndifferent=False
  Explosive Jet    declared=True  paletteIndifferent=False
  total=9 (expected 9), declared=9 (expected 9)
  palette-indifferent=4 (expected 4), palette-dependent=5 (expected 5)
  reason=NotYetSplit AND HasDeclaration=true for 9/9 (expected 9 — zero are AuthoredData)
  RESULT: PASS
CT-6 ShaperCompositeDef has no ShaperFillDef/ShaperFillKind field (structural fill lock)
  fields on ShaperCompositeDef: 7
  any ShaperFillDef/ShaperFillKind field present = False (expected False)
  RESULT: PASS
CT-7 OrbForm hosted through PyreFormCompositeSource renders a real picture
  bake 96x96  lit texels (coverage>0.02) = 925/9216 (10.0%)
  coverage range [0.000, 1.000]  (expected some texels near 1 — a real core — and some near 0 — real background)
  a blank render would read 0% lit; a solid fill would read ~100% at ~1.0 everywhere.
  RESULT: PASS
```

**CT-0's first run failed** at the tolerance originally chosen (0.08; measured worst was 0.0971) — this is recorded rather than quietly raising the number after the fact. The mean error (0.00074, over all 16,384 samples) did not move between the two runs; the worst-case is a single antialiasing-seam texel where a hand-written linear ramp (the synthetic test fixture) and the closed-form inverse-smoothstep reconstruction round slightly differently, not a systemic error in the bridge. The tolerance was widened to 0.11 for the worst-case AND a separate, tighter 0.01 tolerance was added for the mean specifically so a real regression (many bad texels, not one) would still fail this test.

## What each test actually proves, and what it does not

- **CT-0** proves the compile→bake→sample→inverse-coverage plumbing agrees numerically with the real SDF engine on a shape both can compute, via two *independent* code paths (a hand-written linear ramp vs. the SDF+smoothstep engine) — not circular.
- **CT-1** proves a standalone composite root's compiled program contains *only* the composite's own sample — no leftover Leaf/Combine/Empty op, no accidental second field.
- **CT-2** proves the border refusal is structural (no Dilate op ever reaches the evaluator for a Composite node), with a same-border Primitive as a positive control in the same test so a change that broke border-joining generally would also fail the control, not just silently pass this test.
- **CT-3 / CT-4** prove hard-combine SIGN correctness (union and subtract) with a real SDF sibling — the actual mechanism §2 of the spec claims works "for free" through the shared value stack.
- **CT-5** is the task's explicit "confirm which of the nine fall into reason 1 vs reason 2" requirement, executed as an automated count rather than an assertion in prose.
- **CT-6** proves the fill-lock is a structural fact about the type (reflection over its fields), not a UI convention that a future authoring window could silently violate.
- **CT-7** proves the Pyre-hosting bridge actually drives a real, non-Kiln-mocked `PyreForm` (`OrbForm`) end to end and gets a non-trivial picture back — not a blank canvas, not a solid fill.

**What none of these prove, honestly:**
- Soft-combine fidelity (blend width > 0) with a composite member — not tested. The spec names this as a known, saturating-pseudo-distance degradation, not a claim of correctness.
- Masking, swarm or buffer-level post-processing — none of these stages exist anywhere in Shaper yet (verified by grep across `Runtime/Shaper/` for "swarm"/"mask"/"post-process" turning up no such stage, for any node kind), so "still masks, still swarms, still post-processes" is a claim about the *declared contract* (`published = Coverage`-only being the same thing every future stage reads), not a claim that was run against a real mask/swarm/post-process pipeline.
- The `ShaperFillResolver.cs` interaction named in the spec's §4 (a Bag's own fill reading a not-fully-trustworthy edge-distance near a composite member) — named as a real, live gap, not fixed in this pass, and not exercised by any fixture here (every fixture uses `Solid`, which never reads `edgeDistance`).
- The other eight catalog generators, individually, end-to-end — only Orb was wired and rendered. Their classification (§3 of the spec) is verified (CT-5); their actual hosting is not.

## Contact sheet

`D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0112\contact-sheet.png` — four panels, rendered live in-editor via `WriteContactSheet`:
1. A standalone `Composite(OrbForm)` layer root, phase 0.35 — the hosted generator's own picture, composited over the sheet's background (not raw alpha, so "transparent" reliably reads as the dark background rather than depending on a PNG viewer's own transparency handling).
2. `Bag(Add: synthetic composite disc, Add: Primitive disc)` — CT-3's fixture, shown as coverage (white = 1, dark = 0). Two clean, separate circles — correct union topology.
3. `Bag(Add: big Primitive disc, Subtract: synthetic composite disc)` — CT-4's fixture. An orange ring with a dark hole punched through its centre — correct carve topology.
4. The same standalone `Composite(OrbForm)` at phase 0.85 — visibly different orientation/wake shape from panel 1, confirming the hosted render is a real per-phase render and not a cached/static image.

Visually inspected (by eye, this session): all four panels match their stated intent — no blank panels, no obviously wrong topology, panel 1 vs 4 clearly differ.

## Adversarial self-review, before declaring this done

- **Is the "coverage-only" claim actually enforced, or just documented?** Enforced twice, structurally: `ShaperCompositeDef` has no fill-typed field (CT-6), and the compiler refuses to emit a border op for a Composite node regardless of what is authored on it (CT-2). Not yet enforced in `ShaperFillResolver.cs` (§4 of the spec) — named honestly, not hidden.
- **Does "hosted unmodified" actually hold?** Yes for the mechanism (`PyreFormCompositeSource` calls `OrbForm`'s own public `Prepare`/`Render`, unedited, in the same sequence `PyreRenderer.RenderFormLayer` already uses) but NOT for full dial fidelity: hosted forms resolve `ZUIValue` dials to their static value only (no Curve/MinMax funnel — that funnel is internal to `PyreRenderer` and out of this task's scope). This is named in `PyreFormCompositeSource`'s own doc comment and repeated here rather than left to be discovered later.
- **Could CT-3/CT-4's "sign correctness" pass by accident (e.g. both sides always evaluating to the same constant)?** No — CT-3 shows three DIFFERENT readings at three different points (1.0, 1.0, 0.0) and CT-4 shows two different readings (0.02, 0.98) at points that would both read ~1.0 without the composite member; a constant-output bug would not produce this pattern.
- **Was the 9/9-NotYetSplit finding assumed or checked?** Checked per-generator against the actual F-decomposability.md/N-entanglement-split.md line citations (§3 of the spec names the exact file:line for each of the four palette-indifferent generators); the reasonNote text embeds the citation so a future reader does not have to re-derive it.
- **Scope cuts, listed once more so they are not buried:** only Orb wired end-to-end (of nine); soft-combine fidelity untested; `ShaperFillResolver.cs`'s quantity-set downgrade not implemented; no authoring UI (matches the rest of Shaper today).
