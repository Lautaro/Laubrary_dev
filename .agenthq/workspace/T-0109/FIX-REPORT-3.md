# T-0109 — THIRD fix pass: V1, V2, V3 fixed; V4 (audit blindness) closed

Run 2026-08-31. Code changes are in the Shaper worktree only (`D:\UNITY\Laubrary Dev - Shaper`, branch `feat/shaper`); nothing in `D:\UNITY\Laubrary Dev\Assets\` was touched, no `[MenuItem]` was added, nothing was committed.

The four findings this pass answers are V1–V4 of `PASS3-FINDINGS.md`. V1/V2/V3 were fixed in code before this run and are re-verified here; V4 — "H6's omission fixture cannot see the class V1 and V2 belong to" — is what this pass actually built.

---

## V1 — HIGH — the contained-prism solid skip lost whole solid spans

**What it was.** Both contained-prism SOLID skips in `ShaperResolve.cs` could be taken while `mPrev` said AIR. The prism's *claim* was sound — every point in the skipped interval really is solid — but the prism proves solid with a non-strict `G(t) ≥ ζ` while `Member` applies HS-1.1's amended STRICT reading (`G > 0`), so at a slab boundary the prism could say SOLID at the very point `Member` calls AIR. The skip then jumped from an air sample to another air sample straight across a solid span: `mPrev` never flipped, no crossing was emitted, and the span vanished.

**The fix.** Both skips are gated on `mPrev`: the slab-wide `gIn` branch (`else if (hasInnerSlab && mPrev)`) and the per-step one (`float tauMax = mPrev ? ShaperHeight.InverseAtEUpperBound(op, wHi, eLo) : ShaperHeight.NoCrossSection;`). When the prism and `Member` disagree, that disagreement is itself the signal that a boundary is at the current point, so the march declines the skip and falls through to the bounded step. Declining costs speed on exactly one step, and only where a boundary is already known to be present.

**Measured, before → after.**

| Instrument | Before | After |
|---|---|---|
| Minimal repro (400×400 plate, Stepped n=8, bevel None, depth 290, ray origin `(-400, 0, 248.571732)` direction `(0.95394, 0, -0.3)`) | 2 crossings; the span `[552.3819, 576.5567]`, **24.1747 px wide, lost entirely** | 4 crossings `314.4854 / 550.3495 / 552.3823 / 576.5566`, worst residual against the quoted truth **0.00043 px** |
| Brute-force sweep over the 13 affected step counts | 26 of 7440 rays lost a true crossing, worst gap **26.207 px** | 2496 rays, 3240 true transitions, **0 lost**, worst gap 0.0000 px (`v5/V5-PROBE.txt`) |
| H12's own breadth arm (independent truth oracle, 1200 px, 120 000 samples) | — | 234 rays, 392 true crossings, **LOST 0** |

## V2 — MEDIUM — the Stepped containing prism over-reported at four float-latent step counts

**What it was.** `SteppedProfileInverseExact`'s DOWN walk asked `SteppedE` at the raw `(k−1)/n`, the very float that needed the UP nudge. Four indices exist over the authored range where `⌊fl(k/n)·n⌋ < k` — `n=22 k=13`, `n=23 k=7`, `n=23 k=14`, `n=29 k=15` — and at those the returned `t` sat one ulp below its own riser, so the containing prism stopped containing and HS-5.2's `{G ≥ ζ} ⊆ {t ≥ τ_min}` broke.

**The fix.** A new `SteppedRiser(op, j)` helper in `ShaperHeight.cs`; the DOWN walk asks `SteppedE` at `SteppedRiser(op, k−1)` instead of the raw quotient, and the UP walk starts from `SteppedRiser(op, k)`.

**Measured, before → after.** Before: **12 violations in 520 800 checks**, worst over-report `3.4481e-2` t-units — exactly one whole tread — costing 1 of 58 aimed rays a crossing at a **7.1227 px** gap. After: the four `(n,k)` pairs swept ±8 ulps around each tread's own ζ give **68 probes, 0 containing-prism violations** (both in `v5/V5-PROBE.txt` and now permanently in H12).

## V3 — MEDIUM — the straight-down branch tolerance admitted 4.70 px of error

**What it was.** `StraightDownTolerance = 1e-5f` routed a *nearly* vertical ray to the closed form. The closed form is exact only when the ray is exactly axis-aligned, and because Ogee, Round, Dome, Cove and Rounded all have unbounded `dG/dd` (HS-0.1), **no non-zero lateral tolerance bounds the induced z error** — the deficit shrinks only as the cube root of the tolerance.

**The fix.** `bool straightDown = dz < 0f && dx == 0f && dy == 0f;` — exact axis alignment. `StraightDownTolerance` is retained as a documented-deprecated const, because H6 uses it to construct a deliberately off-axis ray and that use must keep working.

**Measured, before → after.** Before: the straight-down branch was wrong **101 times of 1008 branch-paired rays, worst 4.7018 px** (Flat+Ogee at x = −190: got 150.0000, true 154.7018); the general branch was wrong 0 times. After, at that same worst configuration: `(0,0,-1)` exactly → **StraightDown**, first crossing 396.8736; `(2e-5,0,-1)` → **General**, 396.8686; `(1e-6,0,-1)` — two orders *inside* the retired tolerance — → **General**, 396.8734. The production straight-down path builds its direction as exactly `(0,0,-1)` and still takes the closed form: H6 reports **straight-down taken 825 times, general taken 825 times**, so neither branch was lost.

## V4 — the audit could not see the class V1 and V2 belong to. Closed.

### (a) H6's omission check now sweeps the step count and aims at treads

The old arm fired at `steps = 4` — `OpFor`'s default and its only step count — and at ray heights that are fractions of the extent (0.08 / 0.30 / 0.52), never a tread. On that fixture it reported **0 omissions while 4 existed**, and it did so structurally, not by luck: V1 needs a skip whose start point sits on a slab boundary the exact predicate reads as air (a riser landing inside the marched interval, which cannot arise at n = 4 on those rays), and V2 is latent at four step counts only.

A new arm sweeps the step count **4..31 contiguously** — contiguous rather than the pre-fix pass's hand-picked set, so the fixture does not depend on knowing which counts fail — over the W4E fixture (a 240×120 plate with two 16 px-wide slots subtracted at x = ±50, i.e. interior air the march itself has to find), in **both** aims: H6's own extent fractions, and ray heights that are a tread of the step count under test.

**What it measures now.**

| Row | rays | true crossings | unmatched | defects (feature ≥ SurfaceResolution) | resolution-limited (feature < 0.02 px) | odd runs |
|---|---|---|---|---|---|---|
| H6's own fractions, n swept 4..31 | 336 | 1362 | 0 | **0** | 0 | 0 |
| aimed AT a tread, n swept 4..31 | 1008 | 3796 | 2 | **0** | 1 (worst extent 0.01537 px) | 0 |

Pre-fix, the same two rows counted the old way: **4 unmatched, worst 5.2395 px** and **8 unmatched, worst 5.1270 px**.

**The two residual unmatched crossings are real and are reported, not smoothed over.** The first cut of this arm counted them as omissions and FAILED. Diagnosed (`v5/DIAG.txt`, a full re-scan of the aimed arm rather than the worst case alone): at `n = 27, tilt 30°, zf 0.1923` the truth scan finds an **air sliver of 0.0154 px** between two Stepped treads, at `t ∈ [60.0444, 60.0598]`, inside a solid span the march reports as continuous. That is *below* `ShaperResolve.SurfaceResolution` = 0.02 px, and HS-5.5's guarantee is explicitly scoped to features whose extent along the ray is at least `SurfaceResolution`. So the check now measures the **feature extent** — the width of the interval between a consecutive pair of unmatched truth crossings, which is exactly the region the march mis-classified — instead of the distance to the nearest emitted crossing. The 2.5660 px figure was never the size of anything; it is only how far away the march's nearest answer happened to be. A run of *odd* length cannot be paired, meaning the march's parity is wrong over an unbounded region; that is counted separately and fails, because nothing bounds it. It is 0 in both rows.

This is a change of *classification*, not of fixture: the same rays, the same predicate, the same 0.05 px matching threshold. Both the raw unmatched count and the sub-resolution count are printed, so a future regression cannot hide inside the "resolution-limited" bucket without the raw count moving too.

### (b) V1 and V2 as fixed, named traps, and (c) a V3 trap — the new `H12_RegressionTraps()`

A new check `H12_RegressionTraps()`, wired into `RunAll` after H11 and following the file's `Verdict(ok)`/`VERDICT` conventions. Each trap pins the EXACT configuration that exposed its defect — ray, dials and expected numbers all literals — rather than the family it belongs to, because a family test can drift off the one ray that mattered. Every trap also prints its own pre-fix figure, so the row is readable without this report.

- **V1 trap** — the minimal repro verbatim, asserting all four crossings within a stated **0.05 px** tolerance (`TrapTolerance`, the same threshold H6's omission arm uses to call a true crossing matched, and two orders above the 0.00043 px residual actually measured, so drift is visible long before it fails). Plus a **breadth arm** over `V1AffectedSteps` = the 13 step counts the pre-fix sweep lost a crossing at, against `TruthCrossings`.
- **V2 trap** — the four latent `(n,k)` pairs, each tread's own ζ swept ±8 ulps, asserting HS-5.2 containment `{G ≥ ζ} ⊆ {t ≥ τ_min}` by scanning 4096 points of `[0, τ_min)` for any `t` that already reaches ζ. The ulp neighbourhood is the whole point: two earlier passes measured 0 over-reports on grids that never landed there.
- **V3 trap** — Flat+Ogee at x = −190 (the configuration the tolerance was worst at), asserting `(0,0,-1)` → StraightDown, `(2e-5,0,-1)` → General, and `(1e-6,0,-1)` → General. The third ray is two orders *inside* the retired tolerance, so "no tolerance at all" is what is asserted, not "a smaller tolerance".

All four arms report **ok**; H12's VERDICT is **ok**.

---

## The full audit run

Single `RunAll` pass, 2026-08-31 18:58:56, `dataPath = D:/UNITY/Laubrary Dev - Shaper/Assets`, `compileFailed = False`. Full text in `v5/run/`, summary in `v5/run/SUMMARY.txt`; the height audit's output also refreshed the committed `HEIGHT-AUDIT.txt`.

| Audit | time | PASS | FAIL | VERDICT ok |
|---|---|---|---|---|
| `ShaperFieldAudit` | 234 762 ms | 13 | 0 | — (no VERDICT lines) |
| `ShaperFillAudit` | 10 083 ms | 85 | 0 | — |
| `ShaperBorderAudit` | 5 186 ms | 79 | 0 | — |
| `ShaperLightAudit` | 27 266 ms | 150 | 0 | — |
| `ShaperHeightAudit` | 52 956 ms | 0 | **0** | 14/15 by the counter, **15/15 in fact** |

Two notes on that last row, both counting artefacts rather than results. The height audit reports `ok`/`FAIL` and does not use the token `PASS` at all, which is why its PASS column is 0 — it is not a hollow run; it has 15 VERDICT lines and every one of them says `ok`. The counter scores 14/15 only because H1's VERDICT is a multi-line sentence whose `ok` lands on the *second* line ("…monotone TO WITHIN THE FLOAT TOLERANCE STATED ABOVE in float: ok"), which a same-line grep cannot see. An intermediate run of this pass genuinely reported `FAIL=2`, and that too was a counting artefact — a label containing the word "FAILING" — now reworded.

## Both artefacts re-rendered

Both were re-rendered in the same `RunAll` pass, overwriting the committed files (timestamps 19:04).

- `height-contact-sheet.png` — 28 cells, 6 × 5, 659×550. **Looks right**: a full grid of warmly lit bevelled plates in which the profile families are individually readable — the terracing of `03 Stepped n=4` and `21 Stepped n=12`, the sharp facets of `06/07 Taper/Pyramid`, the six bevel rims of `15–20`, the two stars, and `27 depth 0` correctly rendering as a flat unlit plateau.
- `tilted-conformance.png` — 420×300. **Looks right**: a tilted, terraced Flat+Stepped-bevel plate with its stepped **side walls plainly visible** down the left and bottom edges, and the blue Dome star floating above it at its zOffset-driven base — a lit 3D solid, not a silhouette and not garbage.

**Branch assertions fired, with the numbers quoted.** The tilted frame: `branch ASSERTED: general 126000 rays, straight-down 0 rays (must be 0) ok`, and `surface hits: 43025 pixels, of which WALL 10049 and CAP 32976 ok`. Unchanged from the second pass, which is the expected outcome: V1 needs a Stepped riser inside a marched interval, V2 needs one of four step counts, V3 needs a near-axis ray, and this frame contains none of the three.

The straight-down branch count does **not** come from the contact sheet — that render goes through `ShaperHeight.FillTile` and never calls `ShaperResolve.Query`, so it has no branch to assert. The place the straight-down branch is asserted non-zero is H6's paired sweep: `straight-down taken 825 times, general taken 825 times ok`. Both branches are therefore exercised in this run, on the same 825 ray pairs, agreeing to `worst |Δ rayT| 0.01213`.

---

## Still open, or not verified

1. **A 0.0154 px air sliver at Stepped n = 27 is genuinely missed by the march.** It is inside HS-5.5's declared guarantee and is now printed by name in the audit, but it is a real disagreement with truth and should not be read as "clean". Anything downstream that needs sub-`SurfaceResolution` fidelity on a Stepped riser does not have it.
2. **`ShaperResolveResult.bracketCapped` did not mean what its own documentation said, and this pass corrected the comment rather than the code.** The bracket loop leaves by `if (sigma <= SurfaceResolution) break;` as well as by exhausting `MaxBracketDepth`, and only the second increments the counter — so a step that narrowed to `SurfaceResolution` without ever closing the ambiguous shell is a *sampled* step, not a proved one, and increments nothing. The sliver above is measured live with `bracketCapped = 0`. The comment in `ShaperResolve.cs` now says so. **The proper fix is a separate resolution-floored counter on `ShaperResolveResult`**, which would let any caller ask "was this answer exact?"; it was not added here because it changes a public struct and is outside this pass's brief.
3. **Only the height audit was re-examined for blindness.** V4 was a finding about one check's fixture. The same question — "can this check's fixture exercise the defect class it claims to cover?" — has not been asked of the Field, Fill, Border or Light audits, all of which report clean.
4. **The march's speed was still not measured against a pre-fix baseline.** V1's `mPrev` gate makes the march decline some skips, which costs steps. `ShaperHeightAudit` went 24 768 ms → 52 956 ms across the two runs, but essentially all of that is the new swept arm's 1344 extra truth-scanned rays, not the resolve. Absolute figure unchanged as a baseline: the tilted render is 126 000 general-branch rays.
5. **`zTol = max(1e-3, 1e-3·body)` in `EmitAt` is still underived.** Fourth pass in a row this has been recorded as a gap rather than a pass. Nothing new was learned about it here.
6. **Nothing was committed.** All changes are unstaged in the `Laubrary Dev - Shaper` worktree: `ShaperHeightAudit.cs` (the V4 work) and `ShaperResolve.cs` (a corrected doc comment only, no behaviour change) on top of the V1/V2/V3 edits that arrived with this dispatch.
