# T-0109 — independent verification of the height stage (extrusion, bevel, Z offset)

**Verifier:** independent (general-purpose), `claude-opus-5`. **Subject:** `D:\UNITY\Laubrary Dev - Shaper\Assets\Packages\Laubrary\{Runtime,Editor}\Shaper\`, branch `feat/shaper`, uncommitted. **Nothing under verification was edited.** No git command that mutates a tree was run. No scene or asset was saved from any probe.

## How this was measured — the instrument, stated first

I did not run the implementer's audit and read its verdicts. I built an independent harness that compiles **the real `Runtime/Shaper` source, unmodified**, into a standalone .NET 8 console executable using Unity's own Roslyn (`Editor\Data\DotNetSdkRoslyn\csc.dll`) against a hand-written `UnityEngine` stub (`Mathf`, `Vector2/3`, `Color`, `Gradient`, `Texture2D`) and a minimal `ZUIValue`. Every number below is produced by the shipped code, in `float`, exercised by my own test code. The harness is at `D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0109\verify\` (`build.sh`, `Stubs.cs`, `ZUIValueStub.cs`, `Main1.cs`, `March.cs`, `March2.cs`, `Tests.cs`, `Tests2.cs`, `W2.cs`, `ILB.cs`).

The references it is measured against are mine, not the implementation's: a **double-precision transcription of HS-2.2's and HS-3.2's formula tables**, and an exact infimum obtained by 200-iteration bisection on that double reference. The march is measured against a **dense scan of the membership predicate written independently in my own file**, not by calling `ShaperResolve`'s private `Member`. That separation is what lets a defect shared by subject and reference show up — and it is exactly what the audit's own H4 lacks (defect 3).

---

## Verdict table

| # | Claim / check | Source | Verdict | Measured |
|---|---|---|---|---|
| 1 | HS-5.4 "an over-large prism can only make the marcher stop early, never late"; HS-5.5 "correctness holds for any count ≥ 1"; HS-5.4 "the slab count is a SPEED dial, not a correctness one" | spec | **FALSE** | 16 of 32 configurations lose a crossing pair; a 46.2 px feature lost against a 57.74 px step |
| 2 | IMPL §2.4 "a feature thinner than `slabLength / 8` can be missed… I believe it can be made to miss one; I did not make it" | impl notes | **CONFIRMED, and understated** | the floor applies everywhere, not only in the shell; `slabLength` is the ray's parameter length, and Flat/None has exactly 1 slab |
| 3 | HS-6.6 `depth` = sum of entry-to-exit spans, never the envelope | spec | **FALSE in the presence of #1** | `Depth` returns 400.000 where the true solid is 380.000 — 20 canvas px of air reported as solid |
| 4 | HS-5.7 "every closed form is verified against the bisection by round-trip in the audit — that is the check that catches an algebra slip" | spec / H4 | **HOLLOW** | H4's ζ floor is 5e-3; the true worst reach shortfall is at ζ = 1.26e-4 and is **142× larger** than H4 reports |
| 5 | D2's cancellation class was found and fixed | impl notes | **INCOMPLETE** | the same cancellation is live in `Bevel(Cove)` (277% rel. err.), `Bevel(Ogee)` (198%), `BevelInverseU(Rounded)` (returns 0 for ζ ≤ 2.4e-4), `BevelInverseU(Cove)` (+9.2%, unsafe direction) |
| 6 | H1 "monotone for all 42 combinations, 4 860 000 samples, violations 0" | H1 | **OVERSTATED** | 2 520 violations in 66 717 000 ulp-adjacent ordered pairs; worst drop 5.96e-8 (1 ulp). Theorem itself re-derived and **CONFIRMED** |
| 7 | HS-5.1 monotonicity as a theorem, per technique | spec | **CONFIRMED** | re-derived analytically for all 7 profiles × 6 bevels incl. `Linear` |
| 8 | HS-4.1 finite slope bounds (Dome c≤½, Round, Taper, Pyramid, bevel Linear) | spec / H2 | **CONFIRMED** | my own difference quotient at h = 1e-3/1e-5/1e-7; every finite declaration is an upper bound, all tight to ≤0.1% |
| 9 | HS-4.2 composed product rule `L_E + supE·L_B/a` is an upper bound | spec | **CONFIRMED** | 384 finite composed declarations, 0 under-declared |
| 10 | HS-4.1 infinite declarations demonstrated by divergence | H3 | **CONFIRMED with a blind region** | diverges cleanly for c ≥ 0.7; at c = 0.5001 the quotient moves only 2.0015 → 2.0062 over four decades of h — true, but undemonstrable |
| 11 | HS-8.2 analytic `G′` matches a fine central difference; correctly refused where `G` is not differentiable | spec / H9 | **CONFIRMED** | 179 262 checks, 0 outside max(2e-3, 5%), 0 spurious `+inf` |
| 12 | H9's gate can detect its own failure mode | H9 | **CONFIRMED by injection** | ×1.02 → 0.12% trips (baseline); ×1.06 → 56.7%; ×1.20 → 57.3% |
| 13 | HS-8.4 normal is unit, finite, non-zero, never unwritten; no magic ceiling on `G′` | spec | **CONFIRMED** | 4 032 000 samples over 126 configurations: 0 NaN, 0 Inf, 0 unwritten, 0 zero, 0 with \|len−1\| > 1e-4. No clamp exists |
| 14 | H6 the two branches agree at zero tilt, on a bevelled shape, on a non-empty sample set | H6 | **CONFIRMED independently** | my own comparison: 4 914 comparisons over all 42 combinations, 0 count disagreements, worst \|ΔrayT\| 9.445e-3 px |
| 15 | H8 zero allocation on the hot path | H8 | **CONFIRMED, and H8's own instrument was inert** | my measurement: 0 bytes/call and 0 gen-0 collections on all four hot paths. H8 states its thread probe is "INERT on this Mono runtime" |
| 16 | HS-6.4's five mechanical wall statements | spec / H7 | **CONFIRMED** | all five reproduce; Stepped-bevel wall = 3.3333 = `body·E(0)/m` exactly |
| 17 | H7 statement 2 "full-height wall ONLY on Flat, Linear" | H7 | **OVERSTATED** | measured at nx = ny = 0 only; over the support box `Linear`'s wall reaches `supE·body` = 73.941 for body 40 |
| 18 | HS-6.1/6.2 a wall publishes `edgeDistance` exactly 0 | spec | **CONFIRMED** | horizontal and 1.1° rays on Flat and Linear solids: every crossing classified `Wall`, `edgeDistance` = 0.000 |
| 19 | HS-2.3 `body` is not a height budget for `Linear`; `supE` = 1.84853 | spec | **CONFIRMED** | max height over the support box = 73.941124, exactly `supG·body`; declared `supE` = 1.8485281 |
| 20 | HS-7.2 `base(i) = i × layerSpacing + zOffset(i)`; HS-7.3 no sentinel, no clamp | spec | **CONFIRMED** | exact for a 4-layer document with negative and fractional offsets; `zOffset` = −100000 passes through unclamped |
| 21 | IMPL §2.5 `InverseLowerBound` is a genuine lower bound over the whole canvas, incl. `Linear`+`Ogee` | impl notes | **CONFIRMED** | 829 316 800 checks over 42 combinations × 9 angles × 4 amounts × 169 (nx,ny) × 400 ζ: **0 violations** |
| 22 | FC-2.5 `height_final = height_shape + heightDelta·coverageEff` is "a real sum here" | spec Part 10 | **NOT DONE** | `ShaperHeight.FillTile` has no caller outside the audit; the resolver still computes `0 + heightDelta·ce` |
| 23 | IMPL §4 "`ShaperLightCompiler.CompileNormal` still always emits `Constant`" | impl notes | **FALSE as stated; the substance is worse** | `CompileNormal` copies `resp.normalKind`, so `Profile` **is** authorable — and silently degrades to `(0,0,1)` because `op.height` is never set and `PaintTile` passes `field`/`stack` as absent |
| 24 | LR-3.3 no screen-space neighbour of a buffer is read | contract | **CONFIRMED** | `FillProfile` central-differences `ShaperEvaluator.Distance`, a pure function of a canvas point. No buffer differencing anywhere in the new code |
| 25 | No `[MenuItem]`, no `EditorWindow`, no new menu entry | project rule | **CONFIRMED** | only doc-comment mentions of the words in `Runtime/Shaper` and `Editor/Shaper` |
| 26 | HS-9.5 tilted conformance artefact exists and shows a visible side wall | spec | **CONFIRMED by eye** | `tilted-conformance.png` shows a tilted, extruded, stepped-bevelled slab with visible walls and a star inset |
| 27 | The main copy `D:\UNITY\Laubrary Dev\Assets` is untouched | brief | **CONFIRMED** | `git status --porcelain -- Assets` returns only the pre-existing `?? Assets/Temp.meta` |
| 28 | IMPL §5.4 parity on a truncated slice | impl notes | **UNTESTABLE with the fixtures I could build** | see "What I could not test" |

---

## Defect 1 — the general march is a fixed-resolution sampler, and it loses whole features

**Severity: high.** This is the one that makes the general branch unfit for the purpose HS-9.1 states.

`ShaperResolve.QueryGeneral` computes a per-slab sampling resolution and then uses it as a **floor**, not a fallback:

```
float step = resStep;                      // resStep = (a1 - a0) / SlabSamples,  SlabSamples = 8
...
float safe = gapOut / (bound * dxy);
if (safe > step) step = safe;              // the provably safe step is only ever adopted when it is LARGER
```

The exact, provably-safe empty-space skip and its mirrored solid-space skip can therefore only ever make a step **bigger**. They can never make one smaller. When the marcher is near a surface — where `safe → 0` — it takes `resStep` regardless, and any feature thinner than `resStep` along the ray is jumped clean over: `Member` is false (or true) at both ends of the step, no flip is seen, no bisection is triggered, and the crossing pair is never emitted.

Two things make this materially worse than the implementer's own §2.4 statement ("a feature thinner than `slabLength / 8` can be missed"):

- `a1 - a0` is the ray's **parameter length through the slab**, not the slab's Z extent. For a shallow ray it is the full width of the XY support box. For the 400 px plate below it is 400 px, so `resStep` is **50 canvas pixels**, not `body/128`.
- For `Flat` + `None` there is exactly **one** slab. I measured `ShaperHeight.Breakpoints` returning 2 entries → 1 slab for `Flat+None` (17 for `Flat+Cove`, 47 for `Stepped+Stepped`). So the case HS-5.5 singles out as "the containing prism is exact and the march is exact with zero conservatism — the stepped case is the BEST case" is in fact the case with the **coarsest** sampling in the system, because exactness removed the subdivision that was the only thing keeping `resStep` small.

### Reproduction

Fixture: a solid rectangle 400 × 400 canvas px (`rectHalfW = rectHalfH = 200`), minus a full-height slot of width `slot` centred at `x = +40` (`ShaperCombineMode.Subtract`). One layer, `technique = Flat`, `bevel = None`, `depth = 40`, `baseZ = 0`. Ray: origin `(−400, 0, base + 20)`, direction `(1, 0, 0)` (exactly horizontal, so the general branch is taken — asserted, not assumed: `result.branch == General` in every row). Ground truth: 1 200 000-sample scan of the exact membership predicate, each flip bisected 40 times.

| slot (px) | tilt | true crossings | reported | `resStep` (px) | branch |
|---|---|---|---|---|---|
| 60 | 0° | 4 | 4 | 50.00 | General |
| 40 | 0° | 4 | 4 | 50.00 | General |
| **40** | **30°** | **4** | **2** | **57.74** | General |
| **30** | **30°** | **4** | **2** | **57.74** | General |
| **20** | **0°** | **4** | **2** | **50.00** | General |
| **20** | **10°** | **4** | **2** | **50.77** | General |
| **12** | **0°** | **4** | **2** | **50.00** | General |
| **4** | **0°** | **4** | **2** | **50.00** | General |

**16 of 32 configurations lost a crossing pair.** For `slot = 20, tilt = 0` the truth is `rayT = 200.000, 430.000, 450.000, 600.000` and the reported list is `200.000, 600.000` — the entire 20 px slot is absent.

The largest feature lost is `slot = 40` at 30° tilt: **46.19 px along the ray**, against a `resStep` of 57.74 px. That is a 10 %-of-shape feature, not a pathological sliver.

### The consequence, in the resolve's own published quantity

`ShaperResolve.Depth` on the same fixture (HS-6.6 says `depth` is the sum of the spans, and rejects the envelope answer explicitly because "reporting the outer envelope would report solid where there is none"):

| slot | crossings reported | `Depth` reported | true solid along the ray | error |
|---|---|---|---|---|
| 60 | 4 | 340.000 | 340.000 | 0.000 |
| 40 | 4 | 360.000 | 360.000 | 0.000 |
| **20** | **2** | **400.000** | **380.000** | **+20.000 canvas px** |
| **8** | **2** | **400.000** | **392.000** | **+8.000 canvas px** |

So the defect makes HS-6.6's ruling produce exactly the answer the ruling was written to forbid.

### What is actually false in the spec

- HS-5.4: *"an over-large prism can only make the marcher stop early, never late… Accuracy is therefore independent of the slab count; the slab count is a SPEED dial, not a correctness one."* — the prism is fine; the **step floor** is what steps late. Accuracy is a direct function of `SlabSamples` and of the slab count, and for `Flat`/`None` the slab count is pinned at 1.
- HS-5.5: *"correctness holds for any count ≥ 1"* — it does not.

### What would have to change

Either `step = min(resStep, safe)` when `safe` is a genuine distance bound (turning the marcher into a real sphere-tracer with `resStep` as a *progress guarantee* only when both gaps are non-positive), or an in-shell step derived from something the shape publishes rather than from the ray's own clipped length. Either way the `Flat`/`None` one-slab case needs its own subdivision, since exactness is currently what removes it.

---

## Defect 2 — the D2 cancellation class is live in four more closed forms

**Severity: medium.** The implementer found catastrophic cancellation in `1 − √(1 − ζ^(2c))` and rewrote Dome's inverse as `w/(1+√(1−w))`. The identical cancellation is present, unfixed, in the **forward** `Bevel` for `Cove` and `Ogee`, and in the **inverse** `BevelInverseU` for `Rounded` and `Cove`. The rewrite is algebraically identical and numerically better across the whole curve range — I verified Dome's rewrite itself is exact — but the search for siblings stopped at Dome.

Measured, forward evaluation of `ShaperHeight.Bevel` against the double reference (`a = 1`, so `t == u`):

| bevel | worst absolute error | worst relative error | where |
|---|---|---|---|
| `None` | 0 | 0 | — |
| `Linear` | 2.85e-8 | 5.38e-8 | — |
| `Rounded` | 6.92e-8 | 6.04e-8 | — |
| **`Cove`** | **9.380e-5** | **2.77 (277 %)** | got 5.9605e-8, want 1.5811e-8, at u = 1.78e-4 |
| **`Ogee`** | 4.14e-8 | **1.98 (198 %)** | got 2.9802e-8, want 1.0000e-8, at u = 1.00e-4 |
| `Stepped` | 1.99e-8 | 2.98e-8 | — |

`Cove` computes `1 − √(1 − u²)`; below u ≈ 1.7e-4 the `1 − u²` is exactly 1.0 in `float` and the whole expression collapses to 0. `Ogee` computes `½(1 − √(1 − 4u²))` and collapses the same way below u ≈ 1.2e-4. Correct forms: `u²/(1 + √(1−u²))` and `2u²/(1 + √(1−4u²))`.

Measured, the inverses:

- **`BevelInverseU(Rounded, ζ) = 1 − √(1−ζ²)` returns exactly 0** for every ζ below ≈2.4e-4. Round-trip: Flat+Rounded, amount 0.05, ζ = 1e-4 → `Ginv = 0`, `G(Ginv) = 0`, **reach shortfall 1.000e-4**. This is D2's failure mode verbatim, in a different function. Correct form: `ζ²/(1 + √(1−ζ²))`.
- **`BevelInverseU(Cove, ζ) = √(1 − (1−ζ)²)` over-reports** — the one direction HS-5.4 does not cover. Flat+Cove, amount 1, ζ = 1e-7: returns **4.88281e-4** against a true infimum of **4.47214e-4**, i.e. **+4.107e-5 t-units, +9.2 %**. Correct form: `√(ζ(2−ζ))`.
- The bisected products inherit it. Worst over-reports of `ShaperHeight.Inverse` against the double-precision infimum, over 2 850 configurations and 6 190 200 checks spanning the awkward values the brief names (ζ at and just below each breakpoint, ζ → 0⁺, ζ → supG⁻, a → 0⁺ and 1, curve 0.2 and 4 and out-of-range 0/−1/8, taper 0 and 1, steps at both ends):

| combination | max over-report (t-units) | at |
|---|---|---|
| Dome/Round/Taper/Pyramid + `Cove` | 1.726e-4 | ζ = 1e-30, a = 1 |
| any + `Ogee` | 8.631e-5 | ζ = 1e-20/1e-30, a = 1 |
| Pyramid + `Rounded` | 7.874e-5 | ζ = 0.9999999, a = 0.9999, τ = 1e-4 |
| Flat/Linear + `Cove` | 4.107e-5 | ζ = 1e-7, a = 1 |
| Stepped + `Rounded` | 3.576e-5 | ζ = 0.48387, steps 32, a = 0.5 |
| everything else | ≤ 3.2e-7 | — |

**Bounding the practical impact honestly.** `ShaperResolve.Member` evaluates the same `float` `Composed`, so the marcher and the prism are wrong the *same* way, and my containment test (`InverseLowerBound` vs `Composed`, 829 316 800 checks) found **zero** violations. The real consequence is therefore not that the marcher punches through solid: it is that the rendered solid differs from the mathematically-defined solid by up to **1.726e-4 × span** near the silhouette, and that the whole `Cove`/`Ogee` band below u ≈ 1.7e-4 is quantised to zero. On a shape with `span = 200 px` that is 0.035 px — sub-pixel. I rank it medium, not high, on that basis, but it is a real unfixed instance of the exact class the implementer went hunting for, and it is the class H4 was built to catch.

---

## Defect 3 — `ShaperNormalKind.Profile` is authorable and silently does nothing

**Severity: medium.** The implementer's note says *"`ShaperLightCompiler.CompileNormal` still always emits `Constant`"*. That is **false**: `CompileNormal` (`ShaperLightCompiler.cs:491`) does `op.kind = resp.normalKind;`, and `ShaperLightResponse.normalKind` (`ShaperLightRig.cs:288`) is a plain authored public field. So an author *can* select `Profile`.

What actually happens is worse than the note describes. `CompileNormal` never populates `op.height`, and `ShaperFillResolver.PaintTile` calls

```
ShaperNormals.FillTile(scene.normalOp[o], grid, x0, y0, width, height,
                       null, null, scene.normal, slab, width, slab, width);
```

with the two optional trailing `ShaperProgram field` / `float[] stack` parameters omitted. `ShaperNormals.FillTile` therefore takes the `case ShaperNormalKind.Profile:` branch that writes `(0, 0, 1)` on every sample — the LR-3.5 fallback — with no warning and no diagnostic. Selecting `Profile` is indistinguishable from selecting `Constant(0,0,1)`.

Judged against the design doc's B7 failure mode this is the second variant, not the first: not "a rule invoked from nowhere", but **a rule invoked from somewhere and answered with a fallback**. That is harder to notice than a dead code path, because the pixel still gets written.

To make it real, two lines: `CompileNormal` must receive the layer's compiled `ShaperHeightOp` and assign `op.height`; `PaintTile` must pass `ow.shape` and `ow.stack` as the trailing arguments. The `Profile` path itself is correct — I exercised it directly over 4 032 000 samples across 126 configurations and it produced no NaN, no Inf, no unwritten and no zero sample, all unit to 1e-4.

---

## Defect 4 — the height sheet is genuinely unwired, and the light rig still shades at z = 0

**Severity: medium.** Confirmed. `ShaperHeight.FillTile` has **no caller** outside `ShaperHeightAudit`. In `ShaperFillResolver`:

- `buf.height` is allocated (`:263`) and `Array.Clear`ed (`:275`);
- the only writes are `buf.height[i] += buf.heightDelta[i] * ce;` (`:1164`, `:1297`).

So FC-2.5 is still `height_final = 0 + heightDelta · coverageEff`. HEIGHT-SPEC Part 10 says the sum "becomes a real sum here because `height_shape` finally exists" — it has not.

A second consequence the implementer's note does not mention: `Array.Clear(scene.pointZ, slab, n);` (`ShaperFillResolver.cs:937`) with the comment *"The surface point's Z is 0 for a Silhouette layer"*. `ShaperLightLaw.Shade` takes `pz`. So even with a compiled height stage present, every point lamp in the document still shades the layer as a flat sheet on the base plane. The stage computes a correct height that nothing in the rendering path reads.

**Was it in scope?** HEIGHT-SPEC Part 10 is genuinely ambiguous — it says "No fill reads `height` yet" and then says the sum "becomes a real sum here". My judgement: the *sum* belonged here, because Part 10 names it as a consequence of this task, and because `ShaperHeight.FillTile` was deliberately shaped to mirror `ShaperFillOps.FillTile` so the wiring would be "a few lines". Concretely what has to change: add an `ownHeight` sheet alongside `ownDistance`/`ownCoverage` in `ShaperFillResolver`; call `ShaperHeight.FillTile(heightOp, grid, x0, y0, w, h, buf.ownDistance, buf.height, …)` immediately after `ShaperEvaluator.FillTile`, before the delta accumulation; and write `base + height` into `scene.pointZ` instead of clearing it. The `heightOp` has to reach the resolver, which today it does not — that is the only non-trivial part.

---

## Defect 5 — H1's monotonicity result is a property of its sample spacing

**Severity: low (magnitude), medium (as a claim).** HS-5.1 states monotonicity as a **theorem** and the audit proves it by **sampling**. I re-derived the theorem independently and agree with it: every `E` is non-decreasing in `t` (`Flat` and `Linear` constant, `Stepped` a non-decreasing floor, `Dome` and `Round` non-decreasing powers of non-decreasing arguments on [0,1], `Taper` a clamped ramp, `Pyramid` affine with slope τ ≥ 0), every `B` is non-decreasing in `u`, `u` is non-decreasing in `t`, and a product of non-negative non-decreasing functions is non-decreasing. In exact arithmetic the claim holds.

In `float` it does not, and the audit's grid cannot see it. My probe places sample pairs at **±1 float ulp** around every stepped tread `k/n`, every bevel step `a·k/m`, the band edge `t = a`, `taperT`, and `t` ∈ {0, 1}, plus a 600-point uniform grid, over 42 combinations × steps {2,3,4,5,32} × bevelSteps {2,3,16} × curve {0, 0.2, 0.5, 1, 4, 9} × taper {0, 0.05, 1/12, 0.5, 1} × amount {0, 1e-6, 0.05, 0.25, 1}:

- **66 717 000 ordered pairs, 2 520 violations, worst drop 5.960e-8** (exactly one ulp at 1.0).
- Worst case: `Flat + Rounded`, amount 0.05, `t` 0.049999002 → 0.049999997, `G` 1 → 0.99999994.
- `Linear` specifically: 1 458 violations of the same size across 9 angles × 6 bevels × 3 amounts × 81 (nx,ny) points; declared `supE` = 1.8485281 matches HS-2.3's 1.84853 exactly.

The magnitude is benign — one ulp is far below every tolerance in the marcher — so this is not a functional defect. But the audit's line *"violations 0"* is true only of its grid, and HS-5.1 explicitly says the claim is load-bearing and that a failure makes HS-5.2 unsound. The honest statement is "monotone as a theorem; monotone to within 1 ulp in `float`".

---

## Defect 6 — H3's divergence method has a blind region it does not declare

**Severity: low.** HS-4.1 says infinite declarations are verified "by demonstrating divergence under grid refinement rather than reporting a number". Measured with my own difference quotient at h = 1e-3, 1e-5, 1e-7:

| technique | curve | declared | h=1e-3 | h=1e-5 | h=1e-7 |
|---|---|---|---|---|---|
| Dome | 0.5 | 2.00000 | 1.9990 | 2.0000 | 2.0000 |
| **Dome** | **0.5001** | **+inf** | **2.0015** | **2.0043** | **2.0062** |
| **Round** | **0.5001** | **+inf** | **1.0014** | **1.0023** | **1.0032** |
| Dome | 0.7 | +inf | 11.80 | 44.01 | 164.07 |
| Dome | 1 | +inf | 44.71 | 447.21 | 4472.14 |
| Dome | 4 | +inf | 459.83 | 25 860 | 1 454 215 |

The declaration at c = 0.5001 is mathematically correct (the exponent is `0.5/c − 1 = −2.0e-4`, so `E′ ~ t^(-2e-4)`, which diverges but needs `t ~ 1e-15000` to reach 10). It is simply **not demonstrable by refinement**, so "verified by divergence" is a method that works only for the parameters H3 samples, not for the declared range. Worth writing down so a future refactor does not "fix" the declaration to a finite number on the strength of a flat measurement.

---

## Defect 7 — H7's "full-height wall" is measured at one unrepresentative point

**Severity: low.** H7 statement 2 reports *"full-height wall ONLY on: Flat=10.000, Linear=10.000"*. `ShaperHeight.WallHeight(op, nx, ny)` is evaluated with `nx = ny = 0`, which is exactly the point where `Linear`'s tilt term `0.6·(cosθ·nx − sinθ·ny)` vanishes. Measured over the actual support box of a 300 × 200 px plate with `body = 40`, `angle = 45°`: the maximum height is **73.941124**, exactly `supG · body` = 1.8485281 × 40. So the `Linear` wall is *not* body-high; it is up to 1.85 × body, which is the very property HS-2.3 warns about ("`body` is not a height budget for `Linear`"). Also cosmetic: that H7 line prints no `ok` token while the other four statements do.

---

## Defect 8 — the height sheet's hard cut at the antialiased edge

**Severity: low, latent.** `ShaperHeight.FillTile` writes 0 whenever `d > 0`, so a sample with `d ∈ (0, halfBand)` — which has partial `coverage` — gets height exactly 0 with a step discontinuity at `d = 0`, while coverage ramps. The implementer flags this (§5.6). Nothing reads the sheet yet (defect 4), so nothing is wrong today; it becomes visible the moment a fill ramps by height, and it will disagree with `coverage` on the same sample.

---

## How the audit is hollow

Per-check, the question "would this fail if the thing it tests were broken?".

| check | detects its own failure mode? | evidence |
|---|---|---|
| **H1** monotonicity | **Partially.** It would catch a gross non-monotonicity but not a fine one, because it never places two samples close together. | my ulp-adjacent probe finds 2 520 violations where H1 finds 0 |
| **H2** finite bounds | **Yes.** | my independent quotient reproduces every declared figure to ≤0.1 % |
| **H3** infinite bounds | **Yes over most of the range, blind near c = 0.5⁺.** | measured 2.0015 → 2.0062 at c = 0.5001, a ratio of 1.002 against a 20× gate |
| **H4** closed-form round-trip | **No — hollow for the class it exists to catch.** Two independent reasons. (a) Its ζ grid is `(i/200)·supG` (`ShaperHeightAudit.cs:549`), so the smallest non-zero ζ it ever tests is 5e-3, and every cancellation defect in this family lives below that. (b) Its reference is `ShaperHeight.Bisect`, which evaluates the **same `float` `Composed`** as the subject, so a defect shared by forward and inverse is invisible by construction. | **injection**: on `Flat+Rounded`, H4's own grid reports a worst reach shortfall of **8.866e-7**; a log-spaced grid over the identical code reports **1.259e-4 at ζ = 1.26e-4 — 142× larger**. D2 was caught only because it happened to surface at ζ = 0.005, H4's *first* grid point |
| **H5** bit-identical early-outs | **Yes**, structurally — it compares bit patterns | — |
| **H6** branch agreement | **Yes for what it compares — but it compares the wrong thing for defect 1.** It checks that reported crossings are correct and that the two branches agree at *zero tilt*, where the general branch degenerates to the same geometry. Its tilted half asserts "1160/1160 cap crossings lie on the surface equation" — i.e. that everything **emitted** is real. **A check that validates only what was emitted cannot detect an omission.** Its fixture is a convex solid with no thin feature, so there is nothing to omit. | my March2 fixture loses 2 of 4 crossings on the general branch with `branch == General` asserted; H6's design would report `ok` on it |
| **H7** wall statements | **Yes for 4 of 5**; statement 2 is measured at nx = ny = 0 only | `Linear` wall reaches 73.941 not 40.000 |
| **H8** allocation | **Its primary instrument is inert and it says so.** The audit prints "thread probe 0 bytes - INERT on this Mono runtime, reported for completeness only", leaving `GC.GetTotalMemory` deltas as the actual evidence — a coarse instrument that a small steady allocation can hide inside. | my CoreCLR run gives a real per-call figure: **0 bytes/call, 0 gen-0 collections** on all four paths. Conclusion upheld, on better evidence |
| **H9** analytic `G′` | **Yes, at its declared resolution.** Injection: scaling `G′` by 1.02 trips 0.12 % of points (the unperturbed baseline), 1.06 trips 56.7 %, 1.20 trips 57.3 %. So the gate genuinely reads a perturbation back — and equally genuinely cannot see one below 5 %. | measured |
| **H10** normal dials read | **Not independently re-tested.** The `reflection` field the check depends on was added by the implementer *specifically so H10 could fail honestly* (§2.2) — a check and its input authored together is weaker evidence than it reads, though I found nothing wrong with it | — |
| **Multi-span HS-6.6** (inside H6) | **Thin, as declared — and the thinness is not the main problem.** 7 two-span rays out of 697. All 7 behave correctly. But the fixture is a hollow shell whose walls sit on the support-box boundary, which is precisely the geometry where the missed-crossing defect *cannot* fire (the `s0`/`s1` clip clauses catch both walls). I rebuilt that fixture first and it passed 42/42; only moving the feature strictly **inside** the box exposed defect 1. | my March.cs hollow-ring run: 42 configurations, 0 misses; March2.cs slotted plate: 16 of 32 miss |

**The single structural weakness across the audit:** every check that touches the resolve validates the crossings that were emitted. None of them compares the emitted list against an independently computed ground-truth list. That is the gap defect 1 lives in, and no amount of tightening the existing checks closes it — it needs a different check.

---

## What I could not test, and why

- **Truncation parity (implementer's §5.4).** I built three fixtures aiming to make one layer emit two flips in *different* slabs and therefore out of `rayT` order (Dome+Cove on a hollow ring at 15° tilt; Stepped+Stepped with 32/16 steps on a tilted ray; a 3-layer stack). Every one of them produced at most one cap flip plus the step-4b base exit, which is already in `rayT` order, so truncation always retained a correct prefix (`cap=1` kept the entry and stamped it `entering=true`, correctly). The structural risk stands — slabs are iterated by ζ and the layer's crossings are sorted only *after* emission, so a fixture that produces two cap flips in different slabs would retain an arbitrary subset and stamp parity from `insideAtStart` across a gap — but I did not construct one, and `result.truncated` does at least warn the caller.
- **Speed.** Nothing timed, as the implementer records. The march's cost is now *interesting* rather than merely unmeasured, because fixing defect 1 by lowering the step floor will raise it.
- **The `zTol = max(1e-3, 1e-3·body)` Wall/Cap tolerance's derivation.** I confirmed the classification is correct on every fixture I built (horizontal and 1.1° rays on Flat and Linear solids classify all crossings `Wall` with `edgeDistance` exactly 0; near-vertical rays classify `Cap`), but I could not derive the tolerance from anything either, and a shape whose cap sits within `zTol` of its own base would be misclassified.
- **The `Linear` y-frame question (§2.7).** Correctly identified as unmeasurable. The implementation is self-consistent with the spec; only the owner can settle whether the spec matches the intended look.
- **H10 independently.** I verified `reflectionFlatten` is read on every sample by inspection (`nzBase = op.normalZBase + op.reflectionFlatten * clamp01(op.reflection)` in `FillProfile`), but did not build a separate behavioural probe.
- **Anything under Unity itself.** The entire verification ran outside the editor, on the real source compiled standalone. That is a strength for the maths (no domain reloads, 829M-check sweeps) and a limitation for anything that depends on Unity's own `Mathf` differing from my stub — I matched `RoundToInt` to banker's rounding and every other function to its documented `(float)Math.X` form, and the figures I could cross-check against `HEIGHT-AUDIT.txt` (H4's per-combination `|Δt|`, H6's worst `|ΔrayT|`, `supE`, the wall heights) agree to the digits printed.
