# T-0109 — SECOND-PASS independent verification of the height stage, after the fix pass

**Verifier:** independent, second pass (general-purpose), `claude-opus-5`. **Subject:** `D:\UNITY\Laubrary Dev - Shaper\Assets\Packages\Laubrary\{Runtime,Editor}\Shaper\`, branch `feat/shaper`, uncommitted. **Nothing under verification was edited.** No git command that mutates a tree was run. No scene or asset was saved from any probe. The main project `D:\UNITY\Laubrary Dev\Assets` was not written to; only `\.agenthq\workspace\T-0109\{verify,v2}\` was.

## The instruments

Two, both mine, and every number below came out of one of them.

1. **The first verifier's standalone harness, reused and extended.** `…\T-0109\verify\` compiles the real `Runtime/Shaper` source unmodified against a `UnityEngine` stub into a .NET 8 (CoreCLR) console exe. I added `V2.cs` (the bound/epsilon/duplicate/truncation probes), `V2d.cs` (the broad truncation sweep), `V2e.cs` (seven adversarial march attacks), `V2f.cs`, `V2g.cs` (the whole-file cancellation sweep against a double reference), `V2h.cs` (F4's wiring driven through the real `ShaperFillResolver.PaintTile`), `V2i.cs` (H6's omission fixture replicated so its "verified real" extras can be inspected), `V2j/k/l/m/n.cs`. Built by `build3.sh` to `probe3.dll`; the fixer's `probe.dll` was left intact and re-run unchanged for its own claims.
2. **The Shaper editor**, bridge verified as `Application.dataPath = D:/UNITY/Laubrary Dev - Shaper/Assets` before every batch. Used only for the five audits (`ShaperFieldAudit`, `ShaperFillAudit`, `ShaperBorderAudit`, `ShaperLightAudit`, `ShaperHeightAudit`). Port 7800 was never targeted.

**Gotcha for the next agent:** Coplay's `execute_script` reports a 60 s timeout but the code *keeps running* in the editor and its side effects land — the four earlier-wave audits all completed and wrote their files ~90 s after the tool call had already "failed". Poll the output files; do not re-issue. Conversely `EditorApplication.update` did **not** tick for me while the editor was unfocused, so a deferred-work pattern silently never ran; a straight synchronous call is what works.

---

## Verdict table

| # | Item | Verdict | My measurement |
|---|---|---|---|
| F1 | the general march's floor is gone; the repro is fixed | **CONFIRMED** (but see N1/N3 — the branch is still not sound) | slotted-plate repro **0 of 32 configurations lose a crossing**; `slot=20, tilt=0` gives 4 crossings; `Depth` **380.000** against a true 380.000, error 0.000 |
| F1-bonus | `InverseUpperBound` replaces `InverseLowerBound` for the contained prism | **CONFIRMED at the slab level, OVERSTATED as a description of the march** (N4) | reproduced exactly: pre-fix 997 776 000 checks / **278 140 788 violations** / worst 1.697; shipped 502 712 808 / **6 760** / **1.1921e-7**. The residual IS one ulp — `ulp(1.0f)` is 1.1920929e-7 to the digit |
| F2 | cancellation fixed in six closed forms; no eighth | **CONFIRMED** | every rewritten form ≤ **1.3e-7 relative** against a double reference over a 563-point grid (uniform + log to 1e-44 + every breakpoint ±4 ulps); three cases where my *double* reference collapses and the shipped `float` is right; denormal sweep 1e0→1e-45: **0 non-finite, 0 negative** |
| F3 | H4's hollowness closed, and the `Stepped` inverse fixed | **OVERSTATED** (N6) | H4's own injection is real (1962 injected drops detected). But `ProfileInverse(Stepped)` still returns a **whole tread short** at ζ one ulp above a tread: worst \|Δt\| **3.333e-1**; `BevelInverseU(Stepped)` **5.000e-1**. "\|Δt\| 0.500 → 0.000" is a property of H4's grid, not of the code |
| F4 | the height sheet is wired; FC-2.5 is a real sum; `pointZ`; `Profile` normals; null is compatible | **CONFIRMED, on stronger evidence than H11's** | `ownHeight` 656/1920 non-zero max 23.975; **both FC-2.5 terms non-zero in 656/1920 samples** and `max height_final` rises by exactly **3.0000** when a delta of 3 is authored; `pointZ` 1920/1920 = base+height, baseZ 5.000; a **point lamp shades domed vs flat differently in 1968/7680 channels, max \|Δ\| 0.87808**; `Profile` normals 0 degenerate, all unit, 1376/5760 components differ from `Constant`; fallback LOUD at 1920/1920; `height = null` → `ownHeight` 0/1920, `pointZ` 0/1920, `height ≡ 0 + delta·ce` with **0/1920 violations** |
| F5 | H6 can now detect an omission | **PARTIALLY CONFIRMED** (N3) | the omission check is genuine and reaches 504 rays with 0 omissions and worst gap 1.5259e-5 px. But its "extra emitted pairs VERIFIED real" arm **launders a real defect**: replicating the same fixture, **42 of 42 extras are exactly zero-width and sit exactly on the base plane**, not "slivers finer than the scan's spacing" |
| F6 | H1 states its tolerance, H3 declares its blind band, H7 stmt 2 fixed | **CONFIRMED** | H1's injection detects 1962 injected 1-ulp drops; my CoreCLR harness independently reproduces the same one-ulp class. H3's band table is deliberately ungated. H7 stmt 2 asserts `supG·body` = 18.485, a strengthening not a weakening |
| F7 | the `Linear` angle flip, all four sites | **CONFIRMED** | `LinearGradient` = (0.28284, **+0.42426**); highest canvas point (+60, **+40**); corner (1,1) 73.9411 vs (1,−1) 40.0000; `supE` 1.8485281, `infE` 0.15147185 unchanged |
| 1 | the general march, attacked | **NOT SOUND** — two new failures | see N1, N3. Smallest slot the march reliably resolves: **0.019 px**; reliably missed: **0.01 px** (also 0.005, 0.002). 42 combos × 3 tilts on a two-slot plate: 6 wrong (all N3). Nested subtractions to 0.5 px: 42/42 ok. Rays parallel to a wall: 30/30 ok. Tangent to an ellipse: 24/24 ok. Support box 800×800 with a 2 px sliver: 14/14 ok |
| 2 | the bound/inverse class, swept | **A SECOND INSTANCE FOUND** | N4. `InverseLowerBound` containment re-verified myself: **829 316 800 checks, 0 violations, worst overshoot 0.000e+0** |
| 3 | F2's rewrites re-derived, whole file swept | **CONFIRMED, no eighth** | as above. The two declined residuals are correctly declined (one nit: the `Round` inverse underflows too, and only `Dome` was named) |
| 4 | absolute epsilons swept | **A LIVE ONE REMAINS** | N6. Two absolute `−1e-6` epsilons, both in scale-free quantities |
| 5 | F4's wiring | **CONFIRMED** | as above |
| 6 | H1–H11 hollowness re-asked | **ONE CHECK STILL LAUNDERS ITS SUBJECT** | N3. No check was made to pass by weakening an assertion; H1's restatement is the correct one and carries an injection |
| 7 | the fixer's "left unfixed" list | **ONE IS A REAL DEFECT, NOT A DEFERRAL** | truncation parity is N2, measured. `zTol` still underived and I could not test it either. The `FillTile` rim cut is live and is a **full-`body` step**: at `edgeSoftness 1.5`, d=+0.5 has coverage 0.25926 and height 0.00000 while d=−0.5 has coverage 0.74074 and height 24.00000 |
| 8 | regression on the earlier waves | **PASS, all four** | `ShaperFieldAudit` 13 PASS, `ShaperFillAudit` 85, `ShaperBorderAudit` 79, `ShaperLightAudit` 150. **0 lines containing FAIL in any of the four.** Border audit BT-14 IL-decodes the transitive call graph from `PaintTile` — now 50 methods including `ShaperHeight` — and finds **0 heap allocations** |
| 9 | housekeeping | **CONFIRMED**; `ChunksMock` is NOT ours | main `Assets/` shows only `?? Assets/Temp.meta` (pre-existing) plus `?? Assets/ChunksMock/` — see below. No `[MenuItem]`, no `EditorWindow` anywhere in `Runtime/Shaper` or `Editor/Shaper` (only doc-comment mentions). My own allocation measurement: **0 bytes/call and 0 gen-0 collections** on `ShaperHeight.FillTile`, `ShaperNormals.FillTile(Profile)`, `Query` straight-down and `Query` general |

**Full height audit H1–H11 re-run in the editor by me: 21 727 ms, all eleven VERDICT `ok`.** The fixer's audit numbers reproduce.

---

# New defects

## N1 — HIGH. A horizontal ray exactly on an interior slab breakpoint is marched TWICE; crossings double and `Depth` collapses to zero

`ShaperResolve.ClipSlab`'s parallel branch (`ShaperResolve.cs:791`) is

```csharp
if (Mathf.Abs(d) < 1e-9f) return o >= lo && o <= hi;
```

— **inclusive at both ends**. Adjacent slabs share their boundary exactly (`zb` of slab *k* and `za` of slab *k+1* are both `op.baseZ + op.body * breakpoints[k+1]`, the same expression). So a ray with `|dz| < 1e-9` sitting at exactly that z is accepted by **both** slabs, both march the whole clipped range, and both emit the same flips. `SortByRayT` then interleaves the duplicates and step 5's parity stamp toggles on every one of them, so the list reads I,O,I,O over two coincident rayT values and `ShaperResolve.Depth` sums two zero-length spans.

**Reproduction** (`probe3.dll B`): solid plate 400×400 (`rectHalfW = rectHalfH = 200`); one layer `technique = Flat`, `bevel = Stepped`, `bevelAmount = 0.5`, `bevelSteps = 4`, `depth = 40`, `baseZ = 0` — interior breakpoints in z at 10, 20, 30. Ray origin `(−400, 0, 20)`, direction `(1, 0, 0)`.

| ray z | true crossings | reported | `Depth` | true solid | parity flags |
|---|---|---|---|---|---|
| 0.0000 | 2 | 2 | 400.000 | 400.000 | I O |
| 10.0000 | 2 | 2 | 400.000 | 400.000 | I O |
| **20.0000** | **2** | **4** | **0.000** | **350.000** | I O I O |
| **30.0000** | **2** | **4** | **0.000** | **300.000** | I O I O |
| 40.0000 | 2 | 2 | 250.000 | 250.000 | I O |
| 20.3700 (control, off the breakpoint) | 2 | 2 | 300.000 | 300.000 | I O |

**Breadth** (`probe3.dll LAST`), the same plate, all 42 profile × bevel combinations, one horizontal ray at every interior breakpoint:

```
  combinations = 42,  interior-breakpoint rays fired = 552
  combinations with at least one wrong ray = 40,  wrong rays = 530
  worst |Depth error| = 399.609 canvas px
  Flat+Rounded bp[1] zeta=0.06250 z=2.5  got 4 true 2  Depth 0.000 vs 399.609
```

This is a **larger** error in HS-6.6's own published quantity than the defect F1 was written to fix (that one over-reported by 20 px; this one loses 399.609 px of 399.609). It is the same clause failing — `Depth` is not the sum of the spans — by a different mechanism.

**Reachability, stated honestly.** The trigger needs `|dz| < 1e-9` *and* `oz` exactly equal in `float` to `baseZ + body·ζ_k` for an interior breakpoint. Breakpoint z values are frequently round (the fixture above puts them at 10/20/30 for `body = 40`), and "sample at mid-height" is exactly the kind of origin a caller writes — the first verifier's own repro used `oz = base + 0.5·body`, which is z = 20 for `body = 40`, and only escaped because `Flat`/`None` has a single slab with no interior breakpoint. A tilted ray cannot trigger it: with `dz ≠ 0` the two slabs share a single ray parameter and `mPrev` carries across, so nothing is emitted twice. Neither half of H6 can reach it — H6's tilted half uses tilts of 2°–44°, and its zero-tilt half compares the two branches rather than the count against truth.

## N2 — MEDIUM-HIGH. Truncation on an out-of-order crossing slice mis-stamps parity and makes `Depth` OVER-report solid

This is the item both previous passes explicitly left untested. It is now tested, and it fails.

`QueryGeneral` iterates slabs in **ζ order**. A downward ray meets the lowest-ζ slab at the **largest** ray parameter, so crossings are emitted in roughly *decreasing* `rayT`. `Emit` drops on overflow and sets `truncated`, so the list retained is a **suffix** of the ray, while step 5 stamps parity onto it as if it were a prefix, starting from `insideAtStart`.

**Reproduction** (`probe3.dll TRUNC`). Fixture: a 400×80 plate (`rectHalfW 200`, `rectHalfH 40`, so `span = 40`) minus two 12 px slots at x = ±100; layer `Stepped` with 4/8/16 steps, `depth = 300`; rays at 20°–80° from `x = −196`, six heights; buffer capped at 1 … count−1.

```
  truncated cases exercised                                                     242
  cases whose retained set is NOT a ray-order prefix (emission WAS out of order) 200
  cases with at least one MIS-STAMPED parity flag                                144   (worst: 6 flags wrong in one case)
  cases whose Depth OVER-reports the true solid length                            11   worst over-report: 83.138 px
      worst: steps=4 tilt=30 z0=290 cap=4  Depth=355.648 true=272.509
```

A single-ray illustration (`probe3.dll C`) shows the non-monotonicity that proves the retention is not a prefix: at buffer cap 6 `Depth` is 119.596, at cap **5** it is **202.840** — a smaller buffer reporting *more* solid.

`result.truncated` does warn the caller, but `Depth` is a published quantity that is silently wrong, and 355.648 > 272.509 is precisely the "reporting solid where there is none" that HS-6.6 forbids by name. The fix is one line in spirit — emit slabs in ray order, or sort-and-re-stamp before truncating rather than dropping on overflow — but I have not written it.

## N3 — MEDIUM. Spurious zero-thickness crossing pairs on the base plane, and H6's "verified real" arm launders them

`ShaperResolve.Member` reads HS-1.1's closed set literally: `above >= 0` and `above <= body·G(t)`. Where `G(t) = 0` that degenerates to the single plane `z = base`, and `Member` calls it inside. For a `Stepped` extrusion, `E(t) = 0` for the whole band `t < 1/n` — a real band of width `span/n` around the entire silhouette, not a measure-zero point. So every tilted ray whose exit at `s1` (the `z = baseZ` clip) lands inside that band emits an **entering flip from the slab loop and an exiting flip from step 4b at the same `rayT`**.

**Reproduction** (`probe3.dll DRILL`): plate 400×400 minus a 20 px slot at x = +40; `Stepped` extrusion (5 steps) with a `Stepped` bevel, `depth = 300`; ray from `(−400, 0, 100)` at 12° down.

```
tilt=12 z=100 dz=-0.20791169  got=4 true=2 branch=General capped=0 exhausted=False
   emitted: 245.3618I/Wall 398.7128O/Wall 480.9734I/Cap 480.9734O/Cap
   truth  : 245.3617 398.7128
      s=480.9734  x=70.4630  z=0.00000  d=-20.4630  t=0.10232  G=0.00000  body*G=0.00000  above=0.00000
                  inside(-0.05)=False  inside(+0.05)=False
```

It fires on **all six `Stepped` combinations** in my 42-combination sweep at 23° tilt (`v2_march3.txt`: `true=0 got=2 spurious=2`, six rows).

**The audit half of this is the more serious part.** H6's omission check counts an unexplained extra pair as `extraVerified` if `TruthInside` says the *midpoint* is solid — but `TruthInside` (`ShaperHeightAudit.cs:1834`) is a second implementation of the *same* closed-set convention, so for a zero-width pair the midpoint is the crossing itself and the answer is always `true`. Replicating H6's exact fixture (twin-slot 120×120 plate, 42 combinations, 12 rays each, same ray parameters, same predicate) in the standalone harness:

```
  rays=504  omissions=0  worst gap=1.5259E-005
  extra VERIFIED=42  extra SPURIOUS=0
  of the 'verified' extras: ZERO-WIDTH 42, sitting exactly ON the base plane 42
```

Every one of them prints `width=0.000E+000`, `z=0.00000`, `G=0.00000`. (The editor run reports 12 rather than 42 — Mono and CoreCLR round `Mathf.Tan`/`Cos` differently, so the ray origins differ slightly — but the character is identical.) The audit's own wording, *"slivers finer than the scan's 0.003 px spacing"*, is **false**: they are exactly zero-width. A check that reports the defect it was built to catch as evidence of correctness is the hollowness the fix pass was supposed to remove from H6, moved rather than removed.

## N4 — MEDIUM. The per-step contained prism uses raw `InverseAtE`; only the slab-wide one got the hardening

The "bonus defect" fix added verify-and-nudge to `ShaperHeight.InverseUpperBound`. `QueryGeneral` uses it **once per slab** (`ShaperResolve.cs:487`, `tauMaxSlab`). The **per-step** contained prism — the one inside the adaptive bracket, i.e. the machinery the whole F1 rewrite exists for — calls the unhardened function directly:

```csharp
float tauMax = ShaperHeight.InverseAtE(op, wHi, eLo);      // ShaperResolve.cs:576
if (!ShaperHeight.IsNoCrossSection(tauMax))
{
    float gapIn = -d - tauMax * op.span;
    if (gapIn > 0f) { ... skip, assuming SOLID ... }
}
```

Mirroring the fixer's own IUB sweep but calling exactly what the march calls (`probe3.dll A`, same 42 combinations × 9 angles × 4 amounts × 400 ζ × 41 t):

| function used for the contained prism | checks | violations | worst shortfall in ζ |
|---|---|---|---|
| **`InverseAtE` — what the per-step bracket calls** | 2 974 632 | **2 526** | **3.3379e-6** (`Flat+Cove`, a = 0.05, ζ = 0.995) |
| `InverseUpperBound` — what the slab bracket calls | 2 974 632 | 40 | 1.1921e-7 |

The worst τ under-report is **5.9605e-8 t-units**, and that self-limits the harm: `gapIn` can only exceed the true margin by `δτ·span`, so the bogus "provably solid" step is at most ~1.2e-5 px on a 200 px span and is then raised to `SurfaceResolution` anyway. I found no behavioural failure from it. The defect is that the FIX-REPORT's headline row ("shipped after hardening — 6 760 violations, worst 1.192e-7") and Part 11's "every step is either a proof …" do **not** describe the function the march actually calls, and this is the exact class the brief says the code is prone to.

## N5 — MEDIUM. `linearEGrad` is not an upper bound on `|∇E|` once `LocalNormalised`'s per-axis clamp is active

This is the fixer's own open question 1(b) — *"is it really an upper bound on `|∇E|` … for a sheared root transform? I derived it from the affine map and the clamp, but I did not test a shear."* The answer is **no**, and the reason is the clamp, not the shear.

`ShaperHeightCompiler.cs:190-192` bakes the exact Jacobian of the **unclamped** `E`:

```
ex = 0.6(cosθ·m00·invHalfW + sinθ·m10·invHalfH)
ey = 0.6(cosθ·m01·invHalfW + sinθ·m11·invHalfH)
linearEGrad = √(ex² + ey²)
```

I confirmed that is exact for the unclamped map — 18 rotated/skewed/scaled configurations, measured `|∇E|` / baked = **1.0000** to seven digits (`probe3.dll GRAD`). But `LocalNormalised` clamps `nx` and `ny` **independently**, and where one axis is clamped its term leaves the vector sum. When `cosθ·∇nx` and `sinθ·∇ny` partially cancel, the *surviving single term* is larger than the sum. The compiler's comment — *"the clamp inside LocalNormalised can only reduce the variation"* — is therefore false.

Measured (`probe3.dll CLAMP`, 11 angles × 5 rotations × 5 skews × 3 scales × 2 box aspects):

```
  configurations = 1650
  configurations where the TRUE sup|grad E| EXCEEDS the baked linearEGrad: 385
  worst ratio trueSup / baked = 3.6235
  angle=-135 rot=17 skew=60 sx=2.3 halfH=40
     unclamped=3.48485E-003  nx-clamped=1.06066E-002  ny-clamped=1.26274E-002  baked=3.48485E-003
```

The per-step `E` window `de = eGrad·σ·dxy` is therefore too narrow near the local support-box boundary, so neither `tauMin` (at `eHi`) nor `tauMax` (at `eLo`) is guaranteed to bracket the true `E` over the step, and both "proofs" lose their proof status there.

**I could not turn it into an observable march failure.** 5 184 targeted rays (`Linear` × 4 angles × 3 rotations × 3 skews × 6 bevels × 24 tilts, on a skewed/scaled slotted plate) gave **0 rays with a true crossing unmatched, worst gap 0.0000e+0**. So this is a proven-unsound bound with no demonstrated consequence — which is exactly how I rank it: real, and not urgent.

## N6 — LOW-MEDIUM. The `−1e-6` absolute epsilons remain; F3 removed a symptom, not the generator

`ProfileInverse(Stepped)` still computes `k = Mathf.CeilToInt(zeta * (op.n - 1) - 1e-6f)` and `BevelInverseU(Stepped)` still computes `k = Mathf.CeilToInt(zeta * op.bevelN - 1f - 1e-6f)`. An absolute epsilon on a scale-free quantity means the effective ζ tolerance is `1e-6/(n−1)` — about 17 ulps at n = 2 and about 1 ulp at n = 32. Probed at ζ one ulp above a tread, which neither H4's linear grid nor its log grid lands on (`probe3.dll D`):

| function | worst \|Δt\| vs `ShaperHeight.Bisect` | at | worst reach shortfall |
|---|---|---|---|
| `ProfileInverse(Stepped)` | **3.333e-1** | n = 3, ζ = 0.50000006 (one ulp above the tread 0.5) | 1.788e-7 |
| `BevelInverseU(Stepped)` | **5.000e-1** | m = 2, ζ = 0.50000006 | 1.788e-7 |

It fires at every tread of every step count: n = 4 gives 2.5e-1, n = 8 gives 1.25e-1, n = 32 gives 3.125e-2 — always exactly one tread.

FIX-REPORT §F3's table and HEIGHT-SPEC Part 11 both state `Stepped + *` went from `|Δt| 5.000e-1` to `0.000e+0`. That is true of H4's grid and false of the code.

**Direction and blast radius.** The error is toward a *smaller* `Ginv`, so the containing prism is over-large — safe. I re-verified containment at tread-adjacent ζ specifically: 46 200 checks, **0 violations**. The contained prism is protected by `InverseUpperBound`'s verify-and-nudge (this is that mechanism earning its keep). But `ShaperHeight.Inverse` is a public API and HS-5.7 publishes the closed form, so a consumer calling it gets an answer a whole tread out.

## N7 — informational. H11 statement 2 proves the seed, not the sum

H11's document authors no fill, so `heightDelta ≡ 0` and `buf.height[i] == buf.ownHeight[i]` is trivially true for all 1920 samples. The claim itself is nevertheless correct — I verified it with a `Solid` fill carrying `heightDelta = 3`:

```
      samples with height_shape != 0                 : 656/1920
      samples with heightDelta   != 0                : 1920/1920
      samples with BOTH terms non-zero               : 656/1920      <- the sum is genuinely a sum
      samples where (height - shape)/delta is a legal coverageEff in [0,1]: 1920/1920
      max height_final with delta=3: 26.9752   with delta=0: 23.9752   difference = 3.0000
```

No change is needed to the code; H11's statement 2 should say what it measures.

---

## The fixer's "left unfixed" list, re-judged

1. **`Rounded`-inverse conditioning near ζ→1 (7.874e-5).** **Correctly declined.** Not cancellation — the information is not in the `float` input. My whole-file sweep found no rewrite that helps, and `InverseLowerBound` containment holds at 0 violations over 829 316 800 checks, so the consequence is confined to the rendered-vs-ideal solid at sub-pixel scale.
2. **`Dome` inverse underflow.** **Correctly declined, and incompletely described.** `ProfileInverse(Round)` underflows the same way (`c = 4`, ζ = 1e-6 → `ζ^8` = 1e-48 → 0); only `Dome` was named. Safe direction, harmless.
3. **`ProfileInverse(Pyramid)`'s one-ulp subtraction.** **Correctly declined**, and slightly over-stated as a hazard: `ζ − 1` is exact by Sterbenz for ζ ≥ 0.5 and the following addition has relative error ≤ half an ulp *of its own result*, so the absolute error in `t` is bounded by ~1.2e-7 rather than being catastrophic.
4. **Truncation parity on an out-of-order slice.** **NOT a deferral — a live defect.** See N2: 144 mis-stamped cases, `Depth` over-reporting by up to 83.138 px.
5. **The underived `zTol = max(1e-3, 1e-3·body)`.** Still underived. **I could not test it either** and I record that as a gap, not a pass.
6. **The ~10× march cost.** Not re-measured. Nothing in Wave 2 renders through the general branch, so I agree with the deferral.
7. **`FillTile`'s hard height cut at the antialiased edge.** **Correctly identified as an owner decision, and its magnitude is a full `body`.** Measured on a `Flat`/`None` layer, `depth = 24`, `edgeSoftness = 1.5`:

   | d | coverage | height | coverage·body |
   |---|---|---|---|
   | +0.5 | 0.25926 | **0.00000** | 6.22222 |
   | −0.5 | 0.74074 | **24.00000** | 17.77778 |

   Height steps the entire body across one pixel while coverage ramps. This is now live in `scene.pointZ`, so a point lamp's surface Z jumps by `body` at the rim of every non-bevelled layer. I agree it is a spec question; I would raise its priority — it is a visible artefact now, not a latent one.
8. **`ShaperLayer.height` has no UI.** Correct, and correct per the project's standing rule.

---

## Are HEIGHT-SPEC Part 11's amendments honest?

**Mostly yes, and in one respect unusually so** — the section refuses to restate HS-5.5 as an unconditional guarantee, names the resolution as a resolution rather than a proof, and hands the `FillTile` rim question to the owner instead of deciding it. Nothing in it weakens a claim in order to hide a defect I could find, and the two amendments that *sound* like weakening (HS-5.1's float tolerance, HS-4.1's blind band) are the mathematically correct statements and each is backed by an injection or an ungated measurement.

Three amendments are nevertheless **wrong or incomplete as written**, and all three overstate rather than understate:

- **HS-5.7's amendment** claims the `Stepped` inverse went from `|Δt| 0.500` to `0.000`. Measured: still **3.333e-1** and **5.000e-1** at ζ one ulp above a tread (N6). The epsilon that generated the defect was not removed.
- **HS-5.5's qualified guarantee** — "every step is either a proof … or is at most `SurfaceResolution` long" — is not quite true (N4: the per-step contained prism's "proof" fails 2 526 times in 2.97 M checks) and, more importantly, is not the whole story: N1 shows a *correctness* failure of the general branch that has nothing to do with step length at all, and N3 shows the march emitting crossings that are not there. The clause describes the step-sizing rule accurately and describes the branch's correctness inaccurately.
- **HS-6.6 "RESTORED"** is true of the fixer's fixture and false in general: `Depth` reports **0.000 against a true 399.609** on a breakpoint-parallel ray (N1) and **over-reports by 83.138 px** on a truncated list (N2).

Two amendments I checked and can affirm without qualification: **HS-5.4's containment property** (829 316 800 checks, 0 violations, my own run) and **Part 10's FC-2.5 "DISCHARGED"** (verified with both terms non-zero, which H11 itself does not do).

---

## Housekeeping

- **The main copy is untouched by this work.** `git status --porcelain -- Assets` in `D:\UNITY\Laubrary Dev` returns `?? Assets/Temp.meta` (present in the session-start snapshot), `?? Assets/ChunksMock.meta` and `?? Assets/ChunksMock/`.
- **`Assets/ChunksMock/` is NOT attributable to any T-0109 work.** It contains exactly one source file, `Editor/ChunksMockWindow.cs`, in namespace `Laubrary.Chunks.Mock.Editor`, a `ZuiWindow` subclass carrying `[MenuItem("Laubrary/Chunks Mock (Prototype)")]` and described in its own header as "a disposable UI blueprint" for the Chunks authoring workflow. It references no Shaper type, no height/extrusion/bevel concept, and lives in the main project, which neither the fix pass nor this pass ever wrote to. It is somebody else's Chunks task. **Not deleted.**
- **No `[MenuItem]`, no `EditorWindow`, no new menu entry** in `Runtime/Shaper` or `Editor/Shaper` — the only occurrences of either token are doc comments in the five audit files saying that there are none.
- **Hot-path allocation, my own measurement (CoreCLR):** `ShaperHeight.FillTile` 0 bytes/call, 0 gen-0; `ShaperNormals.FillTile(Profile)` 0/0; `ShaperResolve.Query` straight-down 0/0; `ShaperResolve.Query` general 0/0. Independently corroborated by the *border* wave's own instrument: BT-14 IL-decodes 50 methods reachable transitively from `PaintTile`, now including `ShaperHeight`, and finds 0 heap allocations.

---

## What I could not test

- **A ray that demonstrably loses a crossing because of N5.** The bound is provably not an upper bound (385/1650 configurations, worst 3.62×), but 5 184 targeted `Linear` rays on rotated/skewed/scaled fixtures produced 0 omissions. The soundness hole is proven; the consequence is not.
- **`zTol = max(1e-3, 1e-3·body)`.** Like the first verifier, I could not derive it from anything, and I built no fixture that puts a cap within `zTol` of its own base.
- **H6's omission check against a deliberately broken march inside the editor.** That needs a floor injected back into `QueryGeneral`, which is an edit to the code under verification and outside my remit. I substituted the stronger evidence I could get without editing: I built three fixtures the shipped march fails (N1, N2, N3) and showed by construction which of them H6 structurally cannot see.
- **The `Linear` y-frame *look*.** F7's flip is internally consistent across all four sites and matches T-0105's stated precedent; whether the resulting lean is the one the owner wants remains an owner question, exactly as the first pass said.
- **Speed.** Not re-measured. My own runs are consistent with an order-of-magnitude cost rise but I did not instrument it.
- **The 0.001 px slot anomaly.** In the thin-feature sweep the march finds 0.019 px slots, misses 0.01 / 0.005 / 0.002 px slots, and then *finds* a 0.001 px slot. I believe that is sampling-phase luck rather than a property, but I did not prove it.
- **Anything about Unity's own runtime beyond the five audits.** All maths ran in CoreCLR. Where the two runtimes disagree (H1's monotonicity) the fixer's account of the split is consistent with what I measured on my side, but I did not re-derive the Mono half.

---

## Overall judgement

**Not yet sound enough to hand over, and the reason is narrow and specific.**

Everything the fix pass claimed about the *maths* holds up under an independent instrument. F2's six rewrites are exact to one ulp and no eighth cancellation exists in the forward or derivative forms. F4's wiring is real end to end and provably backward-compatible when `height` is null. F7 is consistent across all four sites. The containment property the whole scheme rests on survives 829 million checks. The four earlier waves are untouched — every one of their audits passes, and the border wave's own IL decoder confirms the new `PaintTile` → `ShaperHeight` edge allocates nothing. That is a lot of genuinely good work and it should be said plainly.

What is not sound is **the general resolve branch**, which is the one artefact this task exists to leave behind for 3D. Three separate ways of getting the wrong answer survive the fix pass, and two of them are worse in HS-6.6's own published quantity than the defect the fix pass was written to remove: a breakpoint-parallel ray reports double its crossings and `Depth` 0.000 against 399.609 (N1); a truncated list over-reports solid by 83.138 px (N2); and every `Stepped` layer emits phantom zero-thickness pairs at the base plane, which the rebuilt H6 then counts as evidence that it is working (N3). N1 and N2 are small, local, well-understood fixes. N3 is a one-line convention decision plus an honest correction to H6's text.

The deeper lesson for whoever picks this up is N3, not N1: **the fix pass built a genuinely better check and then let it absorb a live defect into a passing line.** Two passes have now each found what the previous one missed, and the thing that made this one findable was replicating the check's own fixture and looking at what it had labelled "ok". I would fix N1, N2 and N3, re-run H6 with the "verified real" arm required to prove a *non-zero width*, correct the three Part 11 overstatements, and then hand over.
