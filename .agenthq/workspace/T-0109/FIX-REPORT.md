# T-0109 — fix pass over the height stage (extrusion, bevel, Z offset)

**Fixer:** general-purpose, `claude-opus-5`. **Subject:** `D:\UNITY\Laubrary Dev - Shaper\Assets\Packages\Laubrary\{Runtime,Editor}\Shaper\`, branch `feat/shaper`, uncommitted. Nothing was committed; no tree-mutating git command was run; no scene or asset was saved from a probe. The main project `D:\UNITY\Laubrary Dev\Assets` was not touched by this pass — no command was ever pointed at bridge port 7800.

> **Note for the PM, unrelated to this task but observed while checking.** `git status` on the MAIN project now shows `?? Assets/ChunksMock/` and `?? Assets/ChunksMock.meta`. Those were **not** present in the session-start snapshot (which listed only `?? Assets/Temp.meta` under `Assets`) and are not mine. Something else is writing to `D:\UNITY\Laubrary Dev\Assets` concurrently.

**Work order:** `VERIFICATION.md` (independent verifier, `claude-opus-5`). Every defect it raised is addressed below with before/after numbers measured on the same instrument it used.

## The instruments, stated first

Two, deliberately, because they disagree in one measurable way and that disagreement is itself a finding.

1. **The verifier's own standalone harness**, reused unmodified in structure: `…\T-0109\verify\` compiles the real `Runtime/Shaper` source against a `UnityEngine` stub into a .NET 8 (CoreCLR) console exe. Three files were added to it — `F1.cs` (the omission attack), `F2.cs` (cancellation before/after, with the pre-fix expressions re-implemented in place so the comparison is like-for-like without a second checkout), `F3.cs` (a targeted probe) and `IUB.cs` (the mirror of `ILB`, for the bound the new solid-space skip rests on). Every existing test in it was re-run.
2. **The audit in the editor**, `ShaperHeightAudit`, run in the Shaper editor (bridge port 7801, `Application.dataPath` verified as `D:/UNITY/Laubrary Dev - Shaper/Assets`). Compile was checked with `EditorUtility.scriptCompilationFailed` **and** by reflecting for eleven newly-added symbols, not by reading the cumulative console.

**A gotcha worth recording for the next agent:** the unity CLI's `eval_file` has a hard 30 s pipeline timeout that `--timeout` does **not** override (verified with a deliberate 40 s spin: `--timeout 300` still failed at 30000 ms). `RunAll` now takes ~26 s and fits, but only just. `_part.cs` + `_recompile.sh` in this workspace run one check at a time and wait for the domain reload, which is what to reach for if it ever stops fitting. Also: `eval_file` does **not** trigger a recompile — an edit followed straight by `eval_file` silently runs the OLD assembly. `_recompile.sh` refreshes and polls `EditorApplication.isCompiling` before returning.

---

## Summary

| Fix | Verdict | Headline before → after |
|---|---|---|
| F1 general march loses features | **FIXED** | verifier's slotted-plate repro **16 of 32 configurations lost a crossing pair → 0 of 32**; `slot=20, tilt=0` crossings **2 → 4**; `Depth` **400.000 → 380.000** (truth 380.000) |
| F2 cancellation in four more closed forms | **FIXED**, and the sweep found more | `Bevel(Cove)` 277 % → 4.9e-8 rel; `Bevel(Ogee)` 198 % → 5.4e-8; `BevelInverseU(Rounded)` returned exactly 0 below ζ=2.4e-4 → exact to 5e-8 down to ζ=1e-20; `BevelInverseU(Cove)` +9.18 % → 1.0e-8 |
| F3 H4 hollow | **FIXED** — and it immediately caught a live defect | log grid to 1e-9 + injection sub-check; found and fixed a **whole-tread** error in `ProfileInverse(Stepped)` (\|Δt\| **0.500 → 0.000**) |
| F4 two wiring gaps | **FIXED**, both, plus a new H11 that proves it | `buf.height` carries `height_shape`; `pointZ` = `base + height` (was `Array.Clear`); `Profile` normals really run; degenerate fallback is counted |
| F5 H6 hollow for omissions | **FIXED** | new omission check: 504 rays, 42 combinations, non-convex fixture inside the box, **0 omissions**; multi-span fixture replaced and its depth checked against ground truth, 600/600 |
| F6 three narrower | **FIXED** (all three) | H1 states its float tolerance and proves the probe can see a 1-ulp drop; H3 declares and measures its blind band; H7 statement 2 now asserts `supG·body` = 18.485, not 10.000 |
| F7 `Linear` angle flip | **DONE** | at angle 45 the peak moved from (+x, −y) to (+x, +y); `LinearGradient` (0.283, −0.424) → (0.283, +0.424) |
| — bonus, found by the F1 rewrite | **FIXED** | the solid-space skip used a *lower* bound on `Ginv` where it needs an *upper* one — unsound on `Linear`, could call air solid |

**Full audit H1–H11: 0 FAIL, every VERDICT `ok`.** Both PNGs re-rendered from the same `RunAll` pass at 2026-08-31 14:10; the tilted frame asserts **general 126 000 rays, straight-down 0** and shows **10 049 wall pixels**.

---

## F1 — `QueryGeneral` was a fixed-resolution sampler. It is now a two-sided bracket with adaptive subdivision, and there is no floor.

### What was wrong

`ShaperResolve.QueryGeneral` computed `resStep = (a1 − a0) / SlabSamples` and used it as a **floor**: `float safe = gapOut / (bound * dxy); if (safe > step) step = safe;`. The provably-safe empty-space skip could therefore only ever make a step *bigger*. Near a surface, where `safe → 0`, the march took `resStep` regardless. Two aggravations the verifier identified and I confirm: `a1 − a0` is the ray's **parameter length through the slab**, not the slab's Z extent (50 canvas px on a 400 px plate), and `Flat`/`None` has exactly **one** slab — so the case HS-5.5 calls "the BEST case" had the coarsest sampling in the system.

### What replaced it

The floor is gone. The hole it papered over — HS-5.3's step expression being undefined once the point is inside the containing prism *and* inside the slab — is closed by a **two-sided bracket over a window derived from the candidate step**, subdivided:

1. For a candidate step `σ`, form the ζ window the ray can reach within `σ` (`ζ ± |dz|·σ/body`, clipped to the slab) and — for `Linear` only — the `E` window it can reach, from a newly baked `ShaperHeightOp.linearEGrad` (a bound on `|∇E|` in canvas pixels).
2. `τ_min = Ginv(ζ_lo)` at the **most permissive** `E`: contains every solid point of the window. `τ_max = Ginv(ζ_hi)` at the **least permissive** `E`: contained in the solid throughout it. Monotonicity (HS-5.1) is what makes both true.
3. Outside prism(`τ_min`) → provably air for the whole of `σ`; step by `gapOut / (bound·dxy)`, capped at `σ`, **never floored**. Inside prism(`τ_max`) → provably solid; the mirrored step.
4. Between them is the ambiguous shell, of width exactly `(τ_max − τ_min)·span` in `d`. **Halve `σ` and re-bracket** — a smaller `σ` gives a narrower window and therefore a narrower shell. Stop at `SurfaceResolution`.
5. `MaxBracketDepth = 24`. If the cap is hit it is reported on `ShaperResolveResult.bracketCapped` — never truncated silently. `MaxStepsPerSlab` was raised 4096 → 131 072 and its exhaustion is reported on `ShaperResolveResult.stepsExhausted`.

Two efficiency measures, neither of which touches correctness: the **slab-wide** bracket (containing prism at `ζ_lo`/`supE`, contained prism at `ζ_hi`/`infE`) is computed once per slab and tried first, so the per-step subdivision runs only for points in the slab-wide shell; and `σ` starts at twice the last accepted step rather than at the whole remaining slab, which can only ever *shorten* a step and therefore only ever make it more conservative.

**For `Flat`/`None` the two prisms coincide** (`τ_min = τ_max = 0`), the shell is empty, and the march degenerates to the shape's own sphere-trace — exact. That is why the verifier's repro had to go to 4 crossings and `Depth` to exactly 380.000, and it does.

### Measured, on the verifier's own reproduction (`probe.dll M2`)

| slot (px) | tilt | true | **before** | **after** | `resStep` (px) | branch |
|---|---|---|---|---|---|---|
| 60 | 0° | 4 | 4 | 4 | 50.00 | General |
| 40 | 30° | 4 | **2** | **4** | 57.74 | General |
| 30 | 30° | 4 | **2** | **4** | 57.74 | General |
| **20** | **0°** | **4** | **2** | **4** | 50.00 | General |
| 20 | 10° | 4 | **2** | **4** | 50.77 | General |
| 12 | 0° | 4 | **2** | **4** | 50.00 | General |
| 4 | 0° | 4 | **2** | **4** | 50.00 | General |
| 1 | 0° | 4 | **2** | **4** | 50.00 | General |

**Total configurations that lost crossings: 16 of 32 → 0 of 32.** For `slot = 20, tilt = 0` the truth is `rayT = 200.000, 430.000, 450.000, 600.000` and the march now returns all four; before it returned `200.000, 600.000`.

### `Depth`, HS-6.6's own published quantity (`probe.dll DP`)

| slot | true solid | **before** | **after** | error after |
|---|---|---|---|---|
| 60 | 340.000 | 340.000 | 340.000 | 0.000 |
| 40 | 360.000 | 360.000 | 360.000 | 0.000 |
| **20** | **380.000** | **400.000** | **380.000** | **0.000** |
| 8 | 392.000 | 400.000 | 392.000 | 0.000 |

The envelope answer HS-6.6 forbids by name is gone.

### The broader attack (`probe.dll F1`)

A non-convex plate (400×400 minus two 24 px slots strictly inside the support box), **all 42 profile × bevel combinations × 25 rays = 1050 rays**, ground truth from a 600 000-sample scan of an independently written membership predicate:

- **omissions (a true crossing with no emitted crossing within 0.05 px): 0**
- worst gap, true crossing → nearest emitted: **2.14e-4 canvas px**
- 60 extra emitted pairs, **all 60 verified genuinely inside by the exact predicate, 0 spurious** — these are solid slivers at `Stepped` risers thinner than the truth scan's own 0.0023 px spacing, i.e. the march is finer than the oracle there
- `bracketCapped` **0**, `stepsExhausted` **0**

### Cost

`Dome+Cove`, 300 px body, tilted ray through a 400 px plate: **205 µs/ray**. Pre-fix the same ray took 8 samples per slab × 16 slabs, so this is roughly an order of magnitude more work — the honest price of removing the floor, and the reason `MaxStepsPerSlab` had to be raised. It is not on any production path: Wave 2 renders straight down. The 126 000-ray tilted conformance frame renders in **2.07 s**.

### HS-5.4 and HS-5.5, restated honestly — this is the part not to skim

The verifier is right that both clauses were false as written, and they are **not fully true now either**. What is true:

- **The claim that failed is not the one the clauses make.** HS-5.4's "an over-large prism can only make the marcher stop early, never late" was always true and still is — I re-verified the containment property over 829 316 800 checks (`probe.dll ILB`, 0 violations). The prism was never the problem. The **step floor** was, and it is gone.
- **HS-5.4 "accuracy is independent of the slab count": now TRUE.** No expression in the march reads the slab count, `SlabQuality`, or the ray's clipped length to size a step. Accuracy is governed by one absolute constant, `ShaperResolve.SurfaceResolution` = 0.02 canvas px.
- **HS-5.5 "correctness holds for any count ≥ 1": TRUE in a qualified sense that must be written down rather than assumed.** The guarantee the code now supports is mechanical: *every step is either a proof (the point is provably air, or provably solid, for the whole of the step) or is at most `SurfaceResolution` long — so no feature whose extent along the ray is at least `SurfaceResolution` can be missed.* That holds for any slab count ≥ 1.
- It is **exact, not merely tolerance-bounded**, wherever the ambiguous shell is empty: every `Flat` or `Linear` layer without a smooth bevel, every horizontal ray (the ζ window collapses to a point), and every `Stepped` tread. That covers the cases HS-5.5 singles out.
- It is **not** an unconditional correctness claim, and I will not write one. A feature thinner than 0.02 canvas px along the ray can still be missed on a smooth profile. That is a stated resolution, six orders finer than the pre-fix behaviour and two orders below one canvas pixel, but it is a resolution and not a proof.

**Spec status:** HS-5.4's and HS-5.5's sentences are now *correct for the wrong reason* if read literally, and should be rewritten to say "accuracy is governed by `SurfaceResolution`, an absolute canvas-pixel tolerance, and is exact wherever the containing and contained prisms coincide". Flagged in `HEIGHT-SPEC.md` (see "T-0109 fix-pass amendments") rather than silently left.

### A second, unreported defect found while rewriting this

The pre-fix solid-space skip used `ShaperHeight.InverseLowerBound` for **both** prisms:

```
float tauMax = ShaperHeight.InverseLowerBound(op, zetaRefHi);   // WRONG for Linear
...
float gapIn = -d - tauMax * op.span;
if (gapIn > 0f) { ... skip, assuming SOLID ... }
```

For every technique but `Linear` that is harmless, because `Ginv` does not depend on position and lower = upper. On `Linear` it is unsound: `InverseLowerBound` inverts against `supE`, giving a τ **below** the true `Ginv` at that point, so `t ≥ τ_max` does **not** imply `G(t) ≥ ζ` — the march could declare air to be solid and skip a whole crossing pair. Fixed by adding `ShaperHeight.InverseUpperBound` (inverts against `infE`, newly baked) and a shared `InverseAtE(op, ζ, e)`. The verifier did not catch this because its fixtures were `Flat`.

**Measured, by a new sweep written specifically for it** (`probe.dll IUB`, the mirror of `ILB`): *for every (nx,ny) and every t ≥ τ_max, `G(t,nx,ny) ≥ ζ`.*

| bound used for the contained prism | checks | violations | worst shortfall in ζ |
|---|---|---|---|
| **pre-fix** — `InverseLowerBound` | 997 776 000 | **278 140 788** | **1.697** (`Linear+None`, angle −135) |
| first shipped fix — `InverseUpperBound` | 502 712 808 | 227 706 | 3.338e-6 |
| **shipped after hardening** | 502 712 808 | **6 760** | **1.192e-7** (one ulp at 1.0) |

The middle row is worth keeping. `InverseUpperBound` built naively from the closed forms is exact in real arithmetic and short by a few float roundings in `float` — which for the *containing* prism is harmless (it only makes the prism larger) and for this one is precisely the difference between "every point of this prism is inside the solid" and "almost every point is". `InverseUpperBound` therefore **verifies its own answer and bisects upward if the check fails**: it returns the smallest `t` it can *prove* reaches ζ at the least permissive `E`. That can only move `τ_max` up, i.e. shrink the contained prism, which is the conservative direction. The residual 1.192e-7 is one float ulp at `G ≈ 1` and is six orders below `SurfaceResolution`.

### Files touched

`Runtime/Shaper/ShaperResolve.cs` (the march, the two new diagnostics on `ShaperResolveResult`, `SurfaceResolution`, `MaxBracketDepth`, `MaxStepsPerSlab`), `Runtime/Shaper/ShaperHeight.cs` (`InverseAtE`, `InverseUpperBound`, `infE`, `linearEGrad`), `Runtime/Shaper/ShaperHeightCompiler.cs` (bakes `infE`/`linearEGrad`).

---

## F2 — catastrophic cancellation, all four named cases plus three the sweep found

Every figure below is from `probe.dll F2`, which evaluates the **pre-fix expression** and the **shipped expression** side by side against a cancellation-free double reference, so "before" is not a recollection.

### The four named

| function | at | **before** | **after** | true | before rel. err | after rel. err |
|---|---|---|---|---|---|---|
| `Bevel(Cove)` | u = 1.78e-4 | 5.96046e-8 | **1.58420e-8** | 1.58420e-8 | **2.76 (276 %)** | 4.9e-8 |
| `Bevel(Cove)` | u = 1e-6 | **0** | 5.00000e-13 | 5.00000e-13 | 1.0 (total loss) | 1.1e-9 |
| `Bevel(Ogee)` | u = 1e-4 | 2.98023e-8 | **1.00000e-8** | 1.00000e-8 | **1.98 (198 %)** | 5.4e-8 |
| `Bevel(Ogee)` | u = 1e-5 | **0** | 1.00000e-10 | 1.00000e-10 | 1.0 | 5.6e-9 |
| `BevelInverseU(Rounded)` | ζ = 2.4e-4 | 5.96046e-8 | **2.88000e-8** | 2.88000e-8 | 1.07 | 4.5e-9 |
| `BevelInverseU(Rounded)` | ζ = 1e-4 | **exactly 0** | 5.00000e-9 | 5.00000e-9 | 1.0 | 4.7e-8 |
| `BevelInverseU(Cove)` | ζ = 1e-7 | 4.88281e-4 | **4.47214e-4** | 4.47214e-4 | **+9.18 % (unsafe direction)** | 1.0e-8 |
| `BevelInverseU(Cove)` | ζ = 1e-10 | **0** | 1.41421e-5 | 1.41421e-5 | 1.0 | 2.2e-8 |

Rewrites, each algebraically identical: `1 − √(1−u²) → u²/(1+√((1−u)(1+u)))`; `½(1 − √(1−4u²)) → 2u²/(1+√((1−2u)(1+2u)))` and the mirrored half as `1 − 2w²/(1+√((1−2w)(1+2w)))`; `1 − √(1−ζ²) → ζ²/(1+√((1−ζ)(1+ζ)))`; `√(1−(1−ζ)²) → √(ζ(2−ζ))`.

**Round trip** (`Flat + bevel`, amount 0.05, log-spaced ζ from 1e-9): worst reach shortfall

| bevel | **before** | **after** |
|---|---|---|
| Rounded | **1.000e-4** (at ζ = 1e-4; the inverse returned 0) | **8.94e-8** |
| Cove | — | 2.89e-7 |
| Ogee | — | 2.98e-8 |
| Linear | — | 2.98e-8 |
| Stepped | — | 0 |

**Denormal sweep**: 1e0 down to 1e-45 over both fixed forwards and both fixed inverses — **0 non-finite, 0 negative results**. The forms bottom out only where the true value underflows `float` (e.g. `Cove(1e-30)` is 5e-61, below `float.MinValue`), which is a range limit and not a cancellation.

### The sweep of the *remaining* closed forms — the step the original fix skipped

Three more instances of the same class, none named in the defect list, all fixed:

- **`BevelDerivative(Cove)`** computed `1 − u*u`, which cancels as `u → 1⁻` — **exactly where this derivative diverges** and therefore exactly where H3 reads it. Now `(1−u)(1+u)`.
- **`BevelDerivative(Ogee)`** computed `1 − 4u²` and `1 − 4w²`, cancelling at `u = ½` — again exactly at its own singularity. Now `(1∓2u)(1±2u)`.
- **`Bevel(Ogee)`, upper half** now uses the same factorisation inside the root, so it stays accurate at the `u = ½` inflection.

One near-1 regression was introduced by the naive `√(ζ(2−ζ))` form and then fixed: in `float`, ζ = 0.999999 gives a product one ulp below 1 purely from rounding the two factors, so the inverse came back one ulp short of `u = 1` and lost 3.4e-4 of reach at the band's inner edge. The product is now formed in `double` and only then rounded. Safe direction either way, but there is no reason to give away a breakpoint the `t >= a` early-out makes exact.

### What the F2 fix did NOT remove, measured and stated

`probe.dll A` (the over-report hunt, 2 850 configurations, 6 190 200 checks) — the verifier's table, re-measured:

| combination | verifier's before | after |
|---|---|---|
| Dome/Round/Taper/Pyramid + `Cove` | 1.726e-4 (at ζ = 1e-30) | **not in the top 15 — gone** |
| any + `Ogee` | 8.631e-5 | **not in the top 15 — gone** |
| Flat/Linear + `Cove` | 4.107e-5 (at ζ = 1e-7) | **not in the top 15 — gone** |
| Pyramid + `Rounded` | 7.874e-5 (ζ = 0.9999999, a = 0.9999) | **7.874e-5 — unchanged** |
| Stepped + `Rounded` | 3.576e-5 (ζ = 0.48387, a = 0.5) | **3.576e-5 — unchanged** |

The two that did not move are **not** cancellation. They are the `Rounded` inverse near `ζ → 1`, where `u = 1 − √(1−ζ²)` is genuinely ill-conditioned in the *input*: at ζ = 0.9999999 a one-ulp change in ζ moves the true `u` by ~4e-4, so no rewrite of the expression helps — the information is not in the `float`. Magnitude on a 200 px span: 0.016 px. Recorded as a residual, not fixed.

Two more residuals of the same "float range, not float algebra" kind: `Dome+None` at curve 4, ζ = 1e-6 gives `w = ζ^8 = 1e-48`, which underflows `float` to 0, so `Ginv` returns 0 and under-reaches by 1e-6 (safe direction); and `ProfileInverse(Pyramid)`'s `(ζ − 1 + τ)/τ` loses one ulp of 1.0 when `ζ ≈ 1 − τ`, giving an absolute error of ~6e-8/τ in `t` near `t = 0`. Both are inherent to single precision and both are in the conservative direction. Neither was changed.

---

## F3 — H4 could not fail for the class it exists to catch. It can now, and it immediately caught a live defect.

### Both hollownesses closed

- **The ζ grid.** Was `(i/200)·supG`, floor 5e-3. Now the original linear 0–200 grid is kept (so figures stay comparable) and a **log grid from `supG` down to 1e-9** is appended at 201–340. Round-trips per run went 192 000 → **327 360**.
- **The reference.** The `|Δt|` half still compares against `Bisect` on the same `float` `Composed` and is now *labelled* as the weaker half; the **reach** half — `G(Ginv(ζ)) ≥ ζ`, which does not share the defect because a forward/inverse pair that agree wrongly still fail to reach — carries the weight.
- **An INJECTION sub-check** was added: `Ginv` is perturbed by a known factor and the reach shortfall re-measured. It must read the perturbation back proportionately or H4 fails.

Measured injection, in the shipped audit:

| factor on `Ginv` | worst reach shortfall | reads it back? |
|---|---|---|
| 1.00 | 2.9802e-8 | (baseline) |
| 0.95 | **1.5401e-2** | ok — 5.2e5× baseline |
| 0.50 | **2.0467e-1** | ok |
| 0.00 | **1.0000e+0** | ok |

### The defect the fixed H4 found on its first run

`ProfileInverse(Stepped)` computes `k = ceil(ζ(n−1) − 1e-6)`. The `−1e-6` is an absolute epsilon so that a ζ landing one float short of a tread does not ceil up a whole step. But for `0 < ζ < 1e-6/(n−1)` it drives the ceiling to **0**, and `E(0) = 0` for `Stepped` — so the inverse returned `t = 0` for a height the profile does not reach there, **a whole tread short**.

| combination | worst \|Δt\| vs bisection, **before** | **after** |
|---|---|---|
| `Stepped + None` | **5.000e-1** | **0.000e+0** |
| `Stepped + Linear` | 5.000e-1 | 0.000e+0 |
| `Stepped + Rounded` | 5.000e-1 | 0.000e+0 |
| `Stepped + Cove` | 5.000e-1 | 0.000e+0 |
| `Stepped + Ogee` | 5.000e-1 | 0.000e+0 |
| `Stepped + Stepped` | 5.000e-1 | 0.000e+0 |

Fixed with `if (k < 1 && zeta > 0f) k = 1;` — `E` is 0 below `t = 1/n`, so any ζ > 0 needs `k ≥ 1` and there is nothing to weigh up. Direction of the old error: toward a *smaller* `Ginv`, i.e. an over-large containing prism — safe, and wrong. It is fixed rather than excused because "safe" is not the same as "right", and because H4 exists to catch exactly this.

This is the concrete answer to "would this check fail if the thing it tests were broken": it did, on a defect nobody had reported, the first time it ran with a grid that reaches the region where the defect lives.

**H4 now: 327 360 round-trips, 0 FAIL, VERDICT ok.**

---

## F4 — both wiring gaps closed, and a new H11 that would have caught them

### (a) `ShaperHeight.FillTile` had no caller. It has one.

The gap was larger than "add a call": **`ShaperLayer` had no height field at all**, so no authored object referred to the height stage anywhere. What was added:

- `ShaperLayer.height` — a `[SerializeReference] ShaperHeightDef`, **null by default**, so every existing document renders bit-identically to before. Null compiles to `present = false`, which is HS-1.4's "absent", not "a zero-depth stage".
- `ShaperLightCompiler.BindLayer` gained two optional trailing parameters (`layerProgram`, `pixelSize`) and now compiles the layer's height stage there — which is where HS-7.2's `base = i × layerSpacing + zOffset(i)` is already computable — and publishes it through a new `ShaperLightScene.heightOp[]` plus `SetAllHeight`, which writes `normalOp[i].height` in the same call so the two can never drift apart.
- `ShaperFillBuffers.ownHeight` — a new per-owner sheet alongside `ownCoverage`/`ownDistance`, filled by `ShaperHeight.FillTile` immediately after `ShaperEvaluator.FillTile` in `PaintTile`.
- `buf.height` is **seeded** from the root owner's slab before the delta accumulation, so FC-2.5's `height_final = height_shape + heightDelta·coverageEff` is a real sum. The root owner is the one with `ancestorOwner < 0`, because HS-1.1 defines the layer's solid from the *layer's* field; the per-owner slabs are still all filled because they feed `pointZ`, which is per owner (the surface point being shaded belongs to the owner being shaded).
- `scene.pointZ` is written as `hop.baseZ + ownHeight[i]` instead of `Array.Clear`ed. `ShaperLightLaw.Shade` takes `pz`, so before this every point lamp in the document shaded every layer as a flat sheet on the base plane no matter what the height stage computed.

### (b) `ShaperNormalKind.Profile` was authorable and silently did nothing.

The verifier is right and the implementer's note was wrong: `CompileNormal` does `op.kind = resp.normalKind`, so `Profile` *is* authorable. Three changes:

- `CompileNormal(resp, in ShaperHeightOp height)` — a new overload that populates `op.height`; the one-argument form is kept and delegates.
- `PaintTile` now passes `ow.shape` and `ow.stack` as the trailing arguments. `distance`/`heightSheet` stay null deliberately: the `Profile` case central-differences `ShaperEvaluator.Distance` at the sample *point*, a pure function of a canvas point, and reads no screen-space neighbour of any buffer (LR-3.3 holds structurally).
- **The degenerate path is LOUD.** `ShaperNormals.FillTile` changed return type `void → int` (source-compatible: every existing call site ignores it) and returns the number of samples that declared `Profile` and got LR-3.5's `(0,0,1)` fallback. `PaintTile` records it on `ShaperLightScene.normalDegenerate[o]`. The pixel is still written — that is the right pixel — but it is no longer written in silence.

### H11, the check that would have caught all of this

Added because nothing in H1–H10 could tell a correct stage from a correct stage nobody calls, and that is exactly what shipped. It drives a real authored document through `ShaperFillResolver.PaintTile` and asserts:

```
H11 the height stage is WIRED to the shipping paint pass (T-0109 FIX F4)
    1  ShaperHeight.FillTile called from PaintTile: 656/1920 samples carry a height, max 23.975 canvas px  ok
    2  FC-2.5 height_final carries height_shape: 1920/1920 samples  ok
    3  LR-1.5 pointZ = base + height (base 5.000 from HS-7.2, expected 5.000): 1920/1920 match, 0 still exactly 0  ok
    4a Profile normal provider RUNS when authored: degenerate count 0, 1264/1920 samples still exactly (0,0,1)  ok
    4b INJECTION - same authored Profile with NO height stage: degenerate count 1920/1920 (the fallback is LOUD, not silent)  ok
    VERDICT ok
```

(4a's 1264 flat samples are the ones *outside* the silhouette, where `(0,0,1)` is the correct answer. 4b is the injection: take the height stage away and the same authored `Profile` must now report 1920/1920 — a diagnostic that never fires is not a diagnostic.)

---

## F5 — H6 could not detect an omission, and its fixture had nothing to omit. Both fixed.

### The tilted half is now labelled for what it is

It asserts that everything **emitted** lies on the surface equation. That structurally cannot detect an **omission**, and its fixture is convex. It is kept (it is a genuinely independent oracle for what it does check) and now prints that limitation in the report, immediately above the check that can fail.

### The new OMISSION check

Fixture: a **non-convex** 120×120 plate minus two thin slots **strictly inside** the support box — so neither the `s0`/`s1` support-box clip nor the empty-space skip can carry the marcher across them. All 42 combinations, 12 rays each, ground truth from a dense scan of `TruthInside`, a **second implementation of HS-1.1's predicate written in the audit file** (deliberately not a call into `ShaperResolve.Member`, which is private precisely so a check cannot become a tautology by reaching for it).

```
      rays                                 504 (all asserted General)
      true crossings with NO emitted one within 0.05 px   0
      worst gap, true crossing -> nearest emitted         1.5259E-005 canvas px
      extra emitted pairs VERIFIED real by the predicate   12  (slivers finer than the scan's spacing)
      extra emitted pairs SPURIOUS                         0
      march diagnostics: bracketCapped 0, rays hitting the step guard 0
```

I ran this design against the **pre-fix** march in the standalone harness first: it loses crossings there. It is a check that can fail.

### The multi-span fixture was replaced, and its depth is now checked against truth

The old fixture was a hollow shell — a rect minus an inset rect — whose two walls sit **on** the support-box boundary, the one geometry where a missed interior feature cannot fire. It passed 42/42 against a march that was losing whole crossing pairs. It is now a solid plate with two slots strictly inside, 21×53 ray origins instead of 7 rays, and the assertion is no longer only "the sum is below the envelope" (defect F1's `Depth` = 400 vs 380 would have passed that too) but the sum against an independent scan of the true solid length:

```
      600 rays crossed 2+ disjoint spans, 600 report a summed depth strictly below the envelope,
      600 would have been misreported by an envelope answer, and 600/600
      match an INDEPENDENT scan of the true solid length to 0.05 px  ok
```

---

## F6 — the three narrower ones

### H1: the claim now carries its float tolerance, and the probe is proven capable

H1's uniform grid never placed two samples close together. It now also runs an **adjacent-float probe** — pairs at every breakpoint (±64 ulps), and spread over the whole domain and the bevel band, across **seven** bevel amounts including the 0.05 the verifier's worst case used. **24 715 935 ordered pairs.** The verdict text no longer says "monotone"; it says monotone as a theorem and monotone to within a stated float tolerance of 1.2e-7 (one ulp at 1.0).

**And a finding worth more than the fix.** The probe measures **0 drops in Unity's runtime**, while the verifier's CoreCLR harness measures **2 520 drops, worst 5.960e-8**, on the same source. I reproduced both: `Flat+Rounded, a = 0.05, t = 0.049999002 → 0.049999997` gives `G` `1 → 0.99999994` under CoreCLR and is monotone under Unity's Mono — 73 such drops over a 2001-point band sweep in CoreCLR, 0 in Unity. The two runtimes round `u * (2f − u)` differently. So:

- the verifier's number is real and was correctly reported;
- it does **not** reproduce in the runtime that ships;
- therefore the *declaration* must cover both, which is exactly why the claim is now stated with a tolerance rather than as "violations 0". H1 says all of this in its own output.

An **injection** sub-check confirms the probe can see a 1-ulp drop at all: 1 962 injected drops detected, worst 5.9605e-8. Without it, "0 drops" would be indistinguishable from a probe that cannot fail.

### H3: the blind band is declared and measured

A new, deliberately **ungated**, table. Failing the run because a *correct* declaration cannot be demonstrated would be the wrong answer; omitting it is how someone later "fixes" the declaration to a finite number on the strength of a flat measurement.

| curve | h=0.01 | h=0.001 | h=1e-4 | h=1e-5 | h=1e-6 | declared | |
|---|---|---|---|---|---|---|---|
| Dome 0.5 | 1.98 | 1.998 | 1.9998 | 1.99998 | 2 | 2 | (finite, H2's business) |
| **Dome 0.5001** | 1.98128 | 2.00021 | 2.00293 | 2.00403 | **2.00497** | +inf | **BLIND — true but not demonstrable** |
| **Round 0.5001** | 1.00078 | 1.00124 | 1.0017 | 1.00217 | **1.00263** | +inf | **BLIND** |
| Dome 0.55 | 2.65551 | 3.30088 | 4.07281 | 5.02156 | 6.19086 | +inf | BLIND (rises only 2.3×) |
| Dome 0.7 | 4.98108 | 9.67932 | 18.6999 | 36.1061 | 69.7104 | +inf | demonstrable (14×) |
| Dome 1 | 9.94986 | 31.6069 | 99.9949 | 316.226 | 999.999 | +inf | demonstrable (100×) |

The band is `curve ∈ (0.5, ~0.6]`: the exponent `0.5/c − 1` is so close to 0 that `t` must reach ~1e-15000 for the quotient to reach 10.

### H7 statement 2: the fixture was one unrepresentative point

`WallHeight(op, nx, ny)` was evaluated at `nx = ny = 0`, which is exactly where `Linear`'s tilt term vanishes — the one number that makes `Linear` look height-budgeted, which HS-2.3 says in as many words that it is not. Now measured over the support box, with the asserted figure being `supG·body`:

```
    2  full-height wall ONLY on: Flat=10.000, Linear=10.000  (expected Flat, Linear)  ok
       Linear wall, body=10.000: at (nx,ny)=(0,0) it is 10.0000 - the ONE point where its tilt
       vanishes - but over the support box it reaches 18.4853 = supG*body = 18.4853.
```

It also prints an `ok` token now, which it did not before while its four siblings did (the verifier's cosmetic note).

---

## F7 — the `Linear` angle is flipped, and the flip is recorded where someone would undo it

**Ruling implemented.** `Linear`'s formula was ported verbatim from the reference, which rasterises Y-DOWN; Shaper is +Y UP, so for a given authored angle the tilt was the vertical mirror of the reference's. T-0105's precedent is explicit — *"port Pyre angles unchanged, flip reference-app ones"* — and `Linear`'s angle is a reference-app angle. The sign on `sin θ` is flipped in **four** places, or the height and its gradient would disagree about which way the slab leans:

| file | what |
|---|---|
| `ShaperHeight.Profile` (Linear case) | `1 + 0.6(cos θ·nx − sin θ·ny)` → `+ sin θ·ny` |
| `ShaperHeight.LinearGradient` | `gy = −body·0.6·sin θ·invHalfH` → `+` |
| `ShaperHeightCompiler` (`linearEGrad`) | the two `− sinAngle` terms → `+` |
| `ShaperNormals.FillProfile` (chain rule) | `dhdx/dhdy`'s `− h.sinAngle` → `+` |

Measured at angle 45, body 40, on a 120×80 plate:

| | before | after |
|---|---|---|
| highest canvas point | (+60, **−40**) | (+60, **+40**) |
| `LinearGradient` | (0.28284, **−0.42426**) | (0.28284, **+0.42426**) |
| corner (nx,ny)=(1,1) | 6.0589 | **73.9411** |
| corner (nx,ny)=(1,−1) | 73.9411 | **40.0000** |

`supE` (1.8485281) and `infE` (0.1515) are unchanged — the extremes are symmetric in the sign — so nothing downstream of the magnitude moved.

The FRAME NOTE in `ShaperHeight.Profile` was rewritten from "flagged, not silently decided" to a settled ruling with its provenance, and says in as many words: *a future reader diffing this line against `index.html:1148` will see a sign that does not match; that is deliberate. Do not port it back.* The same note is repeated in short form at each of the other three sites.

**Spec status:** HS-2.2's `Linear` row and HS-4.4's matching derivative still state the reference's sign and are now **wrong**. Flagged in `HEIGHT-SPEC.md`.

---

## Spec clauses this pass invalidates or restores

| clause | status after the fix |
|---|---|
| **HS-2.2**, `Linear` row `1 + 0.6(cos θ·nx − sin θ·ny)` | **INVALIDATED** by F7 — the shipped sign is `+`. Rewrite the row. |
| **HS-4.4**, `∂h/∂ly = −body·0.6·sin θ / halfH` | **INVALIDATED** by F7 — the shipped sign is `+`. |
| **HS-5.3**, "a safe step is …" | **AMENDED**: the step expression is undefined inside the prism, which was always true and was the hole the floor covered. The shipped step is a two-sided bracket over a σ-derived window with adaptive subdivision, capped at `SurfaceResolution`. |
| **HS-5.4**, "an over-large prism can only make the marcher stop early, never late" | **RESTORED / never broken** — re-verified over 829 316 800 checks, 0 violations. |
| **HS-5.4**, "accuracy is independent of the slab count" | **RESTORED, and now true of the code** — no step reads the slab count or the ray length. |
| **HS-5.5**, "correctness holds for any count ≥ 1" | **RESTORED IN A QUALIFIED FORM.** True at the stated resolution (0.02 canvas px), exact wherever the shell is empty. Not an unconditional claim; the clause should say so. |
| **HS-5.7**, "every closed form is verified against the bisection by round-trip in the audit — that is the check that catches an algebra slip" | **RESTORED**: it was hollow below ζ = 5e-3; the grid now reaches 1e-9 and the check caught a live `Stepped` defect on its first run. |
| **HS-5.7**, `Stepped → ⌈ζ(n−1)⌉/n` | **UNCHANGED AS WRITTEN** — the spec's formula was right; the implementation's epsilon was not. |
| **HS-6.6**, `depth` = sum of spans, never the envelope | **RESTORED** — `Depth` 400.000 → 380.000 on the verifier's fixture, and now checked against ground truth by H6 rather than only against the envelope. |
| **HS-5.1** monotonicity | **RESTATED WITH ITS TOLERANCE.** Theorem holds. In `float` it holds exactly in Unity's runtime and to within one ulp in CoreCLR. |
| **HS-4.1**, infinite declarations "verified by demonstrating divergence" | **AMENDED** — the method has a declared blind band at `curve ∈ (0.5, ~0.6]`. |
| **HEIGHT-SPEC Part 10**, FC-2.5 "becomes a real sum here" | **DISCHARGED** — it is a real sum, asserted by H11. |
| **HS-9.5** tilted conformance re-rendered whenever the resolve changes | **DISCHARGED** — re-rendered in the same `RunAll` pass, general branch asserted 126 000 / 0. |

A "T-0109 fix-pass amendments" section has been appended to `HEIGHT-SPEC.md` recording these, so the spec is not silently left wrong.

---

## What I could NOT fix, and why

1. **The `Rounded`-inverse ill-conditioning near ζ → 1** (7.874e-5 t-units at ζ = 0.9999999). Not cancellation — the information is not present in the `float` input. A rewrite cannot recover it; only a `double` ζ could. 0.016 px on a 200 px span. Left, and stated.
2. **`Dome` inverse underflow** at curve 4, ζ = 1e-6: `ζ^(2c)` = 1e-48 underflows `float`, so `Ginv` returns 0 and under-reaches by 1e-6. Safe direction, inherent to single precision.
3. **`ProfileInverse(Pyramid)`'s `(ζ − 1 + τ)/τ`** loses one ulp of 1.0 when `ζ ≈ 1 − τ`. Absolute error ~6e-8/τ in `t`, either direction. No cancellation-free rewrite exists — the subtraction is the definition.
4. **Truncation parity on an out-of-order crossing slice** (the verifier's own untested item). I did not construct a fixture that makes one layer emit two cap flips in *different* slabs, and I did not change the truncation path. The structural risk stands exactly as the verifier stated it, and `result.truncated` still warns the caller. The F1 rewrite does not make it better or worse.
5. **The `zTol = max(1e-3, 1e-3·body)` Wall/Cap tolerance's derivation.** Still underived. A shape whose cap sits within `zTol` of its own base is still misclassifiable. Not touched.
6. **`ShaperHeight.FillTile`'s hard cut at the antialiased edge** (verifier's defect 8): a sample with `d ∈ (0, halfBand)` has partial coverage but height exactly 0. Now that the sheet IS read (F4), this is live rather than latent — but making it ramp would change what `height_shape` *means* at the rim, which is a spec decision (does the height follow coverage, or does it follow the solid?) and not a bug fix. **Flagged for the owner; deliberately not decided here.**
7. **Speed.** The march is ~10× more expensive than before (205 µs/ray on a hard case). Nothing on a production path is affected in Wave 2, but a future tilted/3D path will feel it. Not optimised beyond the two cost measures described in F1.
8. **`ShaperLayer.height` has no UI.** It is authorable through the API and null by default. Per the project's standing rule, no menu item, window or inspector was added.

---

## What a second verifier should attack

1. **The `SurfaceResolution` guarantee itself.** I claim "no feature of ray-extent ≥ 0.02 canvas px is missed". The argument is that any interval not *proven* empty-or-solid is sampled at ≤ 0.02 spacing. Attack the proof: construct a case where a step is accepted as "proven air" but the window it was proven over does not actually contain the whole step. The two places to press are (a) the ζ-window clipping to `[zetaLo, zetaHi]` when `zeta` itself has been clamped, and (b) `linearEGrad` — is it really an upper bound on `|∇E|` through `rootInverse` for a *sheared* root transform, not just a rotation? I derived it from the affine map and the clamp, but I did not test a shear.
2. **`InverseUpperBound`'s residual 6 760 violations.** I wrote the mirror sweep (`probe.dll IUB`, 502 712 808 checks) and hardened the function until the worst shortfall was one float ulp — but it is not ZERO, and the sweep samples `t` on a 41-point grid above `τ_max` rather than exhaustively. Press on whether a 1-ulp shortfall at `G ≈ 1` can ever compound into a skipped crossing on a very thick body, and on whether a finer `t` grid finds a worse case.
3. **The runtime split in H1.** I claim Unity's Mono and CoreCLR round `u*(2f−u)` differently. If that is wrong, the alternative explanation is that my Unity-side probe is not evaluating what I think it is, and H1's "0 drops" is hollow again.
4. **H6's omission check against a deliberately broken march.** I verified the design fails on the pre-fix code in the standalone harness. Re-verify it in the editor by injecting a floor back into `QueryGeneral` and confirming H6 goes red.
5. **F4's root-owner choice.** `buf.height` is seeded from the first owner with `ancestorOwner < 0`. If a document can produce more than one such owner, or none, the seed is wrong or absent. I did not construct either case.
6. **`pointZ` for a Solids owner.** `ShaperSolids.FillTile` writes `pointZ` itself and `PaintTile` `continue`s before the height fill, so a Solids owner never gets `base + height`. That is intended (the generator replaces the shape stage) but it is untested.
7. **The 30 s CLI ceiling.** `RunAll` takes ~26 s. Any further check added to it will push it over, and the failure mode is a timeout that looks like a crash. H6's truth-scan sample counts (120 000 and 100 000) are the dial to turn.

---

## Files changed

**Runtime** — `ShaperResolve.cs`, `ShaperHeight.cs`, `ShaperHeightCompiler.cs`, `ShaperNormals.cs`, `ShaperLightCompiler.cs`, `ShaperLightRig.cs`, `ShaperFillResolver.cs`.
**Editor** — `ShaperHeightAudit.cs` (H1, H3, H4, H6, H7 amended; H11 added; `TruthInside`/`TruthCrossings`/`SlotAt`/`NextAfter`/`UlpPair` helpers added).
**Workspace** — `HEIGHT-AUDIT.txt`, `height-contact-sheet.png`, `tilted-conformance.png` regenerated; `HEIGHT-SPEC.md` gained an amendments section; `verify/F1.cs`, `verify/F2.cs`, `verify/F3.cs`, `verify/IUB.cs` added to the harness; `_part.cs`, `_recompile.sh`, `_state.cs`, `_probe109.cs`, `_f7.cs` added as re-runnable tooling.

No `[MenuItem]`, no `EditorWindow`, no new menu entry was added anywhere.
