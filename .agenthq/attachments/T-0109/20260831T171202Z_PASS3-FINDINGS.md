# T-0109 — third verification pass, reconstructed

The third-pass verifier (opus-5) ran from 14:06Z to 15:08Z and was killed mid-run by the planned AgentHQ server restart. It never wrote its report, but every measurement it took survives as raw output in `D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0109\v3\`. This document is that pass's findings recovered from those files by the dispatching Plan agent — it is a transcription with attribution, not a re-measurement, and every number below is quoted from the named file.

Scope of the pass: re-verify the second fix round (N1–N7 from `VERIFICATION-2.md`) against the verifier's own instruments, and re-attack the general march.

---

## Confirmed — the second fix round holds

| Claim | Instrument | Result |
|---|---|---|
| N1 (ClipSlab half-open) fixed | `v3/w3_C.txt` | 645 z-parallel rays over 42 combinations, 0 duplicated `rayT`, 0 wrong at either outer plane; the `lastSlab` degenerate case is unreachable from any authored dial (0/42 duplicated breakpoints) |
| N2 (`descending = dz < 0f` at ±0) | `v3/w3_D.txt` | 645 cases, 0 differing results, worst rayT difference exactly 0 — negative-zero cannot matter because the ray never leaves one z |
| N3 (phantom zero-width pairs) fixed | `v3/w3_F.txt`, `v3/out_V3.txt` | 1008 rays, 0 pairs of width under 1e-5 px; and nothing was LOST by adopting the strict `G > 0` reading — 0 true crossings under the old closed convention left unmatched, worst gap 1.75e-4 px |
| N4 (`InverseAtEUpperBound` per-step) fixed | `v3/w3_G.txt`, `v3/out_V3.txt` | the pre-fix `InverseAtE` violates the upper-bound property in 3194 of 125736 checks; the shipped `InverseAtEUpperBound` violates it in **0** on the same grid, and 40 of 2974632 on the plus/minus-ulp grid |
| the 40 residual N4 violations are not a bound failure | `v3/w4_W4A.txt` | the invariant `LowerG(tau,e) >= zeta` holds in 167040/167040; the 40 failures are all in the *monotonicity* arm at exactly 1 ulp (1.19e-7) — documented float non-monotonicity of G, not a broken bound |
| N5 — **VERIFICATION-2's finding is WITHDRAWN** | `v3/w4_W4C.txt`, `v3/out_V3.txt` | `linearEGrad` IS a valid Lipschitz constant for the shipped E. Tested as point PAIRS (the correct instrument) over 1650 configurations by 4000 pairs: worst needed/baked ratio **1.000456**. VERIFICATION-2's 3.62x figure came from a central difference that takes its x-partial in one clamp regime and its y-partial in another, synthesising a gradient no straight segment can realise |
| N7 (truncation parity — `Depth` must never over-report) fixed | `v3/w3_E.txt` | single layer: 87 truncated cases, 0 non-prefix, 0 mis-stamped, **0 over-reporting**. Multi-layer (the fixer's own open question 2, layer 0 starving layer 1): 20 truncated cases, 0 mis-stamped, 0 over-reporting |
| H4 is real, not self-referential | `v3/w5.txt` (W5A) | the verifier re-implemented H4 independently (closed Ginv vs its own bisection): worst dt 1.726e-4 vs H4's reported 4.567e-4, worst reach shortfall 1.73e-6 vs H4's 3.40e-6 — H4 is the *more* conservative of the two. Injection control: perturbing Ginv by 0.95 moves the reach shortfall to 1.89e-2, so the probe can see a broken inverse |
| no hot-path allocation | `v3/w5.txt` (W5C) | 33600 `ShaperResolve.Query` calls across both branches, **0** managed bytes, 0 gen-0 collections |
| slab-boundary crossings | `v3/out_V3F.txt` | 5265 rays aimed so a crossing lands exactly on a slab boundary, across all 42 combinations: **0** lost |
| sub-resolution spans on the near-vertical techniques | `v3/w5.txt` (W5B), `v3/w3_F.txt` | Dome/Round/Taper/Pyramid at curve 0.2 and 1: 0 emitted pairs narrower than SurfaceResolution, 0 bracket caps, 0 exhausted rays |
| the four earlier-wave audits still pass | `v3/run5/SUMMARY.txt` | Field 13 PASS / 0 FAIL, Fill 85/0, Border 79/0, Light 150/0 |

---

## V1 — HIGH — the contained-prism solid-skip loses whole solid spans

**Evidence:** `v3/w3_A.txt` (breadth), `v3/w3_B.txt` (minimal repro), `v3/out_V3E.txt` (mechanism), `v3/out_V3C.txt` and `v3/out_V3D.txt` (attribution).

Breadth, judged against a truth predicate carrying the amended HS-1.1: **26 of 7440 rays lose a true crossing, worst gap 26.207 px**, worst solid-length under-report 24.1747 px (`v3/w3_A.txt`, `v3/out_V3D.txt`). Affected step counts: 8, 12, 13, 14, 15, 18, 20, 22, 23, 26, 27, 29, 31. Note that `v3/out_V3D.txt` correctly *withdrew* 2 of an initial 28 — those two were the verifier's own truth predicate still carrying the stale pre-N3 zero-height membrane, and it caught that itself.

**Minimal repro** (`v3/w3_B.txt`): a 400x400 solid plate, profile Stepped n=8, bevel None, depth 290 (body 290, span 200); ray origin (-400, 0, 248.571732), direction (0.95394, 0, -0.3). Emitted 314.4854 I / 550.3496 O. Truth 314.4854, 550.3496, 552.3819, 576.5567. The span [552.3819, 576.5567], 24.1747 px wide, is missing entirely.

**Mechanism.** At the entry of slab k=1 the contained-prism test computes `gapIn = -d - tauMaxSlab*span = 23.061 > 0` and skips 24.175 px forward. Two facts then combine: the prism's *claim* is correct — `v3/out_V3E.txt` walks the whole interval and every sample in it reads SOLID, and its own soundness table shows `G(tauMax) >= zetaHi` holds for all 7 slabs — but the prism proves solid with a non-strict `G(t) >= zeta` whereas `Member()` applies HS-1.1's strict reading, so at the slab-boundary start point `Member` returns **AIR**. `mPrev` is therefore false when the skip is taken, and the skip lands at 576.5567, which is exactly the span's true exit and so also reads AIR. Both sampled endpoints are air, the interior is never sampled, `mPrev` never flips, and the span is dropped. `v3/w3_B.txt` states the contradiction explicitly: *"prism says SOLID, exact predicate says AIR - at the SAME point."*

This is a bookkeeping defect, not a bound defect. The skip is entitled to jump; it is not entitled to jump while believing it is outside.

**Recommended fix:** gate BOTH contained-prism skips on already knowing we are inside — the slab-wide `gIn` branch (`ShaperResolve.cs` around line 557) and the per-step `gapIn` branch (around line 618). When `mPrev` is false and the prism says solid, that disagreement is itself the signal that a boundary is at the current point: do not take the skip, fall through to the bounded step so the next sample lands inside and the entry is emitted normally. The alternative — having the skip emit the entry itself — is also sound but changes more of the emission path. Whichever is chosen, verify by measurement, not by argument.

## V2 — MEDIUM — the Stepped containing prism over-reports at four float-latent step counts

**Evidence:** `v3/out_V3.txt` (V3-N6), `v3/out_V3B.txt`, `v3/w4_W4B.txt`.

HS-5.2 requires that `{G >= zeta}` is a subset of `{t >= tauMin}`; a violation means some `t` BELOW `tauMin` already reaches zeta, so the containing prism does not contain and the march can step clean through solid. `v3/out_V3.txt` finds **12 violations in 520800 checks**; `v3/w4_W4B.txt` isolates it to a single zeta in 2542 probes, over-reporting by 3.4481e-2 t-units — exactly one whole tread.

**Root cause, diagnosed in `v3/out_V3B.txt`:** the DOWN walk asks `SteppedE` at the un-nudged `(k-1)/n`, which is precisely the float that needed the UP nudge. Four latent indices exist where `floor(fl(k/n)*n) < k`: n=22 k=13, n=23 k=7, n=23 k=14, n=29 k=15. For n=29 k=15: `SteppedE(raw) = 0.5` but `SteppedE(nudged) = 0.5357143`.

**Cost in the march:** 1 of 58 rays aimed at the affected zeta loses a crossing, worst gap **7.1227 px** (`v3/w4_W4B.txt`, `v3/out_V3B.txt`).

Note the grid dependence: `v3/w3_H.txt` reports 0 over-reports in 4495 probes on its own grid. The failure only appears once the grid includes the plus/minus-ulp neighbourhood of a tread boundary, which is why two earlier passes missed it.

**Recommended fix:** apply the same nudge in the down walk that the up walk already applies, in `ShaperHeight.cs`, and add the four (n,k) pairs above as a named regression trap.

## V3 — MEDIUM — the straight-down branch tolerance admits 4.70 px of error

**Evidence:** `v3/w5.txt` (W5D), `v3/w6.txt` (W6).

`ShaperResolve.StraightDownTolerance = 1e-5f` routes a ray to the closed-form branch when the absolute values of dx and dy are both under 1e-5. Measured against the verifier's own scan over 1008 branch-paired rays: the **general branch is wrong 0 times**; the **straight-down branch is wrong 101 times, worst 4.7018 px** (`v3/w6.txt`), at Flat+Ogee x=-190 — got 150.0000, true 154.7018.

The mechanism is stated by W6's own eps sweep: the deficit shrinks roughly as the cube root of the marching tolerance (eps 1e-1 gives 149.5 px, 1e-2 gives 83.0, 1e-3 gives 31.2, 1e-4 gives 10.4, 2e-5 gives 4.7), which is the signature of a surface whose slope in the inside-distance is unbounded. Since Ogee, Round, Dome, Cove and Rounded all have unbounded `dG/dd` (HS-0.1's own ruling), **no non-zero lateral tolerance bounds the induced z error**. The closed form is exact only when the ray is exactly axis-aligned.

**Recommended fix:** require exact axis alignment (`dx == 0f && dy == 0f`) for the closed-form branch, and say in the comment why a tolerance cannot be made safe rather than merely small. The production straight-down renderer builds its direction as exactly (0,0,-1), so this costs it nothing — confirm that by measurement (the straight-down PNG must still render through the straight-down branch, with a non-zero branch count).

## V4 — LOW (audit rigour) — H6's omission fixture cannot see the class V1 and V2 belong to

**Evidence:** `v3/w4_W4E.txt`.

H6 fires its omission check at `depth 90, amount 0.25` with `steps = 4` — its default and only step count — and at ray heights that are fractions of the extent (0.08 / 0.30 / 0.52), never a tread. On that fixture it finds 0 omissions and reports clean. Re-aimed with H6's own ray heights but step counts swept 4 to 31, the same check finds **4 omissions, worst gap 5.2395 px** (n=13, tilt 44 degrees, zf 0.08); aimed at a tread with the same sweep, **8 omissions, worst 5.1270 px**.

So the audit's clean H6 row is a true statement about a fixture that structurally cannot exercise the defect. **Recommended fix:** sweep the step count in H6 (or add a sibling check that does), and add V1's and V2's exact repro rays as fixed regression traps so neither can come back silently.

---

## Convergence note

This is the third verification round, and each round has found genuine new defects in a narrowing band: round 1 found a fixed-resolution sampler losing 46 px features, round 2 found a 400 px `Depth` error, round 3 finds a 26 px lost span and a 7 px one. The trend is real convergence, not churn, but the plan agent's judgement is that after fixing V1–V4 the task should report honestly rather than open a fourth adversarial round — the remaining risk is better spent on the fills that build on top of this spine than on a fourth pass over the same 900 lines.
