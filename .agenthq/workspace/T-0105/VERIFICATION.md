# T-0105 Wave 2 — independent verification of the Shaper shape engine

Verifier: independent agent, no part in writing the code. Subject: `D:\UNITY\Laubrary Dev - Shaper`, branch `feat/shaper`, `Assets\Packages\Laubrary\Runtime\Shaper\` (12 C# files + asmdef) and `Assets\Packages\Laubrary\Editor\Shaper\ShaperFieldAudit.cs`. All measurement run against the Shaper editor, PID 90644, bridge port 7801, confirmed by `Application.dataPath == D:/UNITY/Laubrary Dev - Shaper/Assets`. Nothing under `D:\UNITY\Laubrary Dev\Assets\` was touched and no tree-mutating git command was run in either copy.

Raw probe sources and outputs are kept at `D:\UNITY\_verify_T0105\` (`probe0.cs`, `audit.cs`, `kernel.cs`, `prims.cs`, `ellipse.cs`, `defects.cs`, `star.cs`, `ops.cs` and their `*-out.txt`). One supplementary probe, `ellipse2.cs`, was a finer re-census of the ellipse spike locus; it was still executing on the editor's main thread when this report was written and none of the conclusions below depend on it — if `ellipse2-out.txt` later appears it only refines the hit-rate figures in §4.1, not the defect itself. Note that a long `eval_file` blocks the Shaper editor's main thread, so that editor may appear unresponsive for a while after this run.

---

## 1. The bound maths, re-derived from scratch

I derived both bounds before reading any comment in the repo, then confirmed each numerically against the shipped code rather than against my own re-implementation.

### 1.1 `smoothMinShaped` — soft Add and soft Intersect

`f(a,b,k,n) = min(a,b) − h^n · k/(2n)` with `h = max(k − |a−b|, 0)/k`.

Take `a < b` and `b − a < k` (inside the band), so `min(a,b) = a` and `h = (k − b + a)/k`, giving `∂h/∂a = +1/k` and `∂h/∂b = −1/k`. Then

- `∂f/∂a = 1 − (k/(2n))·n·h^(n−1)·(1/k) = 1 − h^(n−1)/2`
- `∂f/∂b = 0 − (k/(2n))·n·h^(n−1)·(−1/k) = + h^(n−1)/2`

`h ∈ [0,1]` by construction and `n = pow(8, clamp01(sharpness)) ∈ [1,8]`, so `h^(n−1) ∈ [0,1]`; hence `∂f/∂a ∈ [½,1]`, `∂f/∂b ∈ [0,½]`, both non-negative, and they sum to exactly 1. The gradient is therefore a genuine convex combination, `∇f = w_a∇a + w_b∇b` with `w_a, w_b ≥ 0` and `w_a + w_b = 1`, so `|∇f| ≤ max(|∇a|, |∇b|)` for **any** relative orientation of the two gradients, including the anti-aligned worst case. Outside the band `h = 0` and the weights are `(1,0)`; at the band edge with `n = 1` the weights jump from `(½,½)` to `(1,0)`, which is a derivative discontinuity but still a convex pair. Soft Intersect is `−f(−a,−b)`, which reuses the same weights unchanged. **`max(B_a, B_b)` is sound. The just-applied hand edit is correct and there is no factor of 2.** For the record, the earlier draft's `[−½, +3/2]` range is exactly what you get if the penalty term is added rather than subtracted — a sign error, as the current spec says.

### 1.2 The soft-subtract composite

`result = a + s·bite`, `bite = −F(0, −carve, band, n)`, `carve = −d − a`, so the second argument of `F` is `v = −carve = a + d`, and `band = sin(sπ)·reach·0.6` is a constant in space. Writing `F_v` for `∂F/∂(second argument)`:

- `∂result/∂a = 1 − s·F_v`
- `∂result/∂d = − s·F_v`

From §1.1, `F_v` is either `1 − h^(n−1)/2` or `h^(n−1)/2` depending on which argument is the smaller, and in **both** cases `F_v ∈ [0,1]`. With `s ∈ [0,1]` put `sw = s·F_v ∈ [0,1]`; then `∇result = (1 − sw)·∇a + sw·(−∇d)`, again a convex combination with non-negative weights summing to 1. So `|∇result| ≤ max(|∇a|, |∇d|)` at **every** strength, not only at the endpoints, and **`max(B_a, B_d)` is sound**. The one thing that would break this is `n < 1`, where `h^(n−1) → ∞` as `h → 0⁺` and `∂f/∂a → −∞`; `ShaperOps.BlendExponent` clamps `sharpness` to `[0,1]` before the `pow`, so `n ≥ 1` always, and `ShaperCompiler.cs:247` is the only site that fills the exponent slot. No under-declaration.

### 1.3 Numerical confirmation of both, against the shipped code

The decisive test is not a field sample but the kernel's own weights: `|w_a| + |w_b| ≤ 1` is exactly the condition for `|∇f| ≤ max(|∇a|,|∇b|)` under arbitrary gradient directions. Measured on `ShaperOps.Combine` directly, by central differences in each argument:

| kernel | sweep | sup(&#124;w_a&#124; + &#124;w_b&#124;) |
|---|---|---|
| soft Add | k ∈ {0.01, 0.1, 1, 10, 100, 1000} × sharpness ∈ {0, 0.001, 0.1, 0.25, 0.5, 0.75, 0.9, 0.999, 1} × 40 001 points across the band | **1.000070** |
| soft Intersect | same | **1.000070** |
| soft Subtract | reach ∈ {0.5, 5, 50, 500} × sharpness ∈ {0, 0.5, 1} × strength ∈ {1e-5 … 0.99999, 16 values} × 401×401 (a,b) grid | **1.000404** |

The residual 7e-5…4e-4 is float finite-difference noise (the quotient is a difference of order-`k` floats divided by `2·k·10⁻³`). Nothing anywhere approaches 1.5, let alone 2 or 4.

---

## 2. Compile, after the hand edit to `ShaperBound.cs`

`unity command recompile` then `recompile_status` → `{"status":"completed","failed":false,"errors":[]}`. Then, via `eval_file` on port 7801 (not by grepping the cumulative console):

```
dataPath=D:/UNITY/Laubrary Dev - Shaper/Assets
isCompiling=False
scriptCompilationFailed=False
  Laubrary.Shaper.ShaperSdf              => com.Lautaro-Arino.Laubrary.Shaper
  Laubrary.Shaper.ShaperCompiler         => com.Lautaro-Arino.Laubrary.Shaper
  Laubrary.Shaper.ShaperEvaluator        => com.Lautaro-Arino.Laubrary.Shaper
  Laubrary.Shaper.ShaperBound            => com.Lautaro-Arino.Laubrary.Shaper
  Laubrary.Shaper.ShaperOps              => com.Lautaro-Arino.Laubrary.Shaper
  Laubrary.Shaper.ShaperPrimitives       => com.Lautaro-Arino.Laubrary.Shaper
  Laubrary.Shaper.ShaperNode             => com.Lautaro-Arino.Laubrary.Shaper
  Laubrary.Shaper.ShaperProgram          => com.Lautaro-Arino.Laubrary.Shaper
  Laubrary.Shaper.ShaperField            => com.Lautaro-Arino.Laubrary.Shaper
  Laubrary.Shaper.ShaperMatrix           => com.Lautaro-Arino.Laubrary.Shaper
  Laubrary.Shaper.ShaperValue            => com.Lautaro-Arino.Laubrary.Shaper
  Laubrary.Shaper.ShaperTransformBlock   => com.Lautaro-Arino.Laubrary.Shaper
  Laubrary.Shaper.Editor.ShaperFieldAudit=> com.Lautaro-Arino.Laubrary.Shaper.Editor
SoftBlendFactor=1
SoftCarveFactor=1
```

The hand edit is live and compiled. `isCompiling` was re-read as `False` inside the same evaluation that resolved the types, so this is not a stale-assembly reading.

`ZUIValue.EvaluateCurveAtNorm`, `EvaluateStepsAtNorm`, `EvaluateOscillationAtNorm` and `Multiplier()` all exist on the shipped `ZUIValue` (`Assets/Packages/Laubrary/Zui/Scripts/Runtime/ZUIValue.cs:156, 184, 233, 101`), so `ShaperValue.Sample` is not calling into anything speculative.

---

## 3. Measured versus declared

Every number below is from my own run, not from a summary. Declared is `program.bound` read off the compiled program.

### 3.1 The audit's own V1–V9, re-run after the hand edit

| check | result | raw |
|---|---|---|
| V1 sweep identity | PASS | 0 mismatched bits / 248 897 samples, 17 primitive cases |
| V2 shell identity | PASS | 0 / 248 897 |
| V3 soft-combine identities | PASS | 0/14 641 soft-Add@w0; 0/14 641 + 64 deep probes soft-Sub@s0; 0/14 641 soft-Sub@s1 |
| V4 declared bounds | PASS | 48 cases, all ≤ 1.0002 measured vs 1.00 declared |
| V5 ratio cross-check | PASS | 7 subjects, inside 1.0000 / outside 1.0000 |
| V6 anisotropy | PASS | scale.x 0.2 / 0.1 / 5 → 1.0000, ratio 1.0000 in and out |
| V7 landmark trap | PASS | Shaper star tip 1.0000, field max 1.0000; reference flower tip 1.0000, field max 3.1377 |
| V8 tile independence | PASS | 0 / 8 633 (distance + coverage), 7×11 prime tiles over a 97×89 grid |
| V9 shelled inside-ness | PASS | centre +44.0000 cov 0.000, wall −6.0000 cov 1.000, rim −1.0000, outside +24.0000 |

### 3.2 Primitives — my own sweep, all declared 1.00

Instrument: max directional difference quotient, 16 directions, `h = 0.02` canvas units, 101² grid over ±90 plus four rounds of local refinement around the running worst.

| primitive | sweep | worst measured |
|---|---|---|
| Star | arms 2…20 × length {0, 0.2, 0.62, 0.9, 1} × baseWidth {0.1, 0.35, 0.7, 1} × skew {−60, −30, 0, 30, 60} — 1 900 configurations | **1.00098** (arms 2, len 0.2, bw 1, skew −60) |
| NGon | sides 3…64 × cornerRadius {0, 5, 20, 60} × rotation {0, 13} — 496 configurations | **1.00136** (sides 48, corner 60, rot 13) |
| Rect (r0 / r18) | 130 extent | 1.0000 / 1.0000 |
| Ellipse circle r50 | | 1.0000 |
| Ellipse 60×25 | audit instrument | 1.0002 — **but see §4.1: 4051.46 with a fine step** |
| Ellipse 20×70, 100×2 | | 1.0000 / 1.00000 |
| Diamond 50×30 | | 1.0000 |
| Triangle 90×70 | | 1.0000 |
| Capsule 40+15 | | 1.0000 |

The 1.0010–1.0014 readings are localised at the far corners of the sample window (|x|,|y| ≈ 90) where the field value is ~130 and one ulp is ~1e-5; divided by `2h = 0.04` that is ~3e-4 per ulp, so a few ulps of cancellation accounts for all of it. They are instrument noise, not signal.

### 3.3 Transforms — declared must pass through unchanged, all 1.00

| case | measured |
|---|---|
| identity / translate(30,−20) / rotate 30 | 1.0000 / 1.0000 / 1.0000 |
| scale(0.2, 1) | 1.0000 |
| scale(3, 0.5) | 1.0000 |
| skew(20, 10) | 0.9993 |
| origin(40,25) + rot45 | 1.0000 |
| full stack (T+R+S+K+origin) | 0.9994 |
| V6 scale.x 0.2 / 0.1 / 5, gradient **and** direct ratio | 1.0000 across the board |
| sweep + scale.x 0.15 / 0.5 / 4 + rot 27 + skew 15 (my own) | 0.99971 / 0.99678 / 0.99881 |

The `σ_min` rescale is real and it works: the reference app's 5.00× at `scale.x = 0.2` is gone in both instruments.

### 3.4 Combine modes — declared 1.00 everywhere

Field-level, anti-aligned bridge (two separated discs, gradients pointing at each other across the band), 24 directions, `h = 0.04`:

| mode | sweep | worst measured |
|---|---|---|
| soft Add | width {5, 20, 45, 90} × sharpness {0, 0.5, 1} | 1.00012 |
| soft Intersect | width {5, 20, 45, 90} × sharpness {0, 0.5, 1} | 1.00021 |
| soft Subtract | strength {0.001, 0.02, 0.1, 0.3, 0.5, 0.7, 0.9, 0.98, 0.999} × sharpness {0, 0.5, 1} | 1.00021 |
| hard Add / hard Intersect / Subtract@1 / Subtract@0 (audit) | | 1.0000 |

Kernel-level (§1.3) is the stronger statement and gives 1.000070 / 1.000070 / 1.000404.

### 3.5 Operators — declared `max(B_child, 1)` for Sweep, `B_child` for Shell

| case | measured | declared |
|---|---|---|
| sweep radial 30 / 90 / 179 / 180 / 181 / 270 / 359 ° on a 7-arm star | 1.00028 (all seven) | 1.00 |
| sweep radial 140 ° / 280 ° (union) on an NGon (audit) | 1.0000 / 1.0000 | 1.00 |
| sweep longitudinal 0.25…0.75 on a capsule | 1.0000 | 1.00 |
| shell Centred / Inward / Outward, t ∈ {0.5, 4, 30, 200}, on a 5-arm star | 1.00034 (all twelve) | 1.00 |

The 180°-boundary transition between the intersection form and the union form is clean — 179, 180 and 181 all read identically.

### 3.6 Star neighbour-sector window — the specific thing the brief asked about

I rebuilt the full 3N-gon vertex ring from `ShaperPrimitives.Bake`'s own output and took the exact minimum distance over **all** `3N` segments, then compared it against the shipped 5-sector (15-segment) window. Sweep: arms 2…20 × length {0, 0.15, 0.4, 0.62, 0.85, 0.98, 1} × baseWidth {0.1, 0.3, 0.55, 0.8, 1} × skew {−60, −25, 0, 25, 60} × 91² sample points ≈ **29 million comparisons**.

Worst over-report (shipped minus brute force) = **0.00003 canvas units**, worst ratio 1.00006. The window is sufficient, including at arms = 20 with `baseWidth = 0.1` and deep valleys, which is where it is thinnest. No configuration missed a nearer segment.

### 3.7 Hot path

`FillTile` on a 256×256 grid over a two-leaf bag (Star ∪ NGon), warmed, then 10 repeats between a forced `GC.Collect` and a `GetTotalMemory`:

```
10 x 65536 samples: managed delta = 0 bytes, gen0 collections = 0
```

Static confirmation over `Runtime/Shaper/`: zero matches for `System.Linq`, `System.Random`, `UnityEngine.Random`, `virtual `, `abstract `, `interface `, `Func<`, `Action<`, `delegate` outside of doc comments. Zero matches for `1e6` outside a doc comment. Zero matches for `[MenuItem]`, `EditorWindow`, `ScriptableObject`, `InitializeOnLoad` in **either** folder.

### 3.8 Degenerate inputs — no NaN, no Inf, no exception anywhere

81×81 scan per case.

| input | result |
|---|---|
| Rect halfW/halfH = 0, and = −30/−10 | clamped to 1e-4; a dot at the origin; no NaN |
| Rect cornerRadius = 999 | clamped to min(halfW,halfH); correct stadium |
| Ellipse rx/ry = 0, rx = −40 | clamped to 1e-4; no NaN |
| Diamond 0×0, Triangle 0×0 and −90×−70, Capsule 0+0 | clamped; no NaN |
| NGon sides = 0, = −5, = 1000 | clamped to 3 / 3 / 64; no NaN |
| Star arms = 0, = 1 | clamped to 2; no NaN |
| Star radius = 0 / 0.4 with length = 1 | no NaN, but `rIn` (0.5) **exceeds** `R` — see finding 4 |
| Shell thickness = 0 | no NaN, but returns the **unshelled solid** — see finding 2 |
| Sweep radial extent = 0 and = −90 | both clamp to a zero-width wedge; field ≥ 0 everywhere (nothing inside); no NaN |
| Sweep longitudinal extent = 0 | zero-width slab; nothing inside; no NaN |
| transform scale = (0,0) and (1,0) | Empty (1e9) everywhere, `hasSingularTransform = true`, no divide |
| transform skew = (89,89) | finite, correct (the shear stretches the rect past the sample window) |
| empty Bag, null root | Empty everywhere, no exception |
| bag whose first member is Subtract(0.5) | Empty, `hasLeadingNonAdd = true` |
| bag whose first member is soft Intersect | Empty, `hasLeadingNonAdd = true` |
| bag whose first member is soft Add (width 12) | correctly returns the member exactly — `SmoothMinShaped(1e9, d, k, n)` is bit-exactly `d` |

---

## 4. Defects, most severe first

### 4.1 The exact ellipse blows up on the cubic solver's branch seam — `ShaperSdf.cs:86`

`ShaperSdf.Ellipse` is Inigo Quilez's closed-form quartic solve. It branches on the cubic discriminant `d = c³ + m²n²` (`ShaperSdf.cs:83`, tested at `:86`). At the locus where `d ≈ 0` both branches lose all significance and `co` comes back as garbage, so the reported distance jumps.

Concrete failing input, `rx = 60, ry = 25`:

```
x=-23.27936   reported   6.51425   true   6.51424
x=-23.27736   reported  47.19825   true   6.51391      <-- 7.24x over-report
x=-23.27536   reported   6.51359   true   6.51359
```

(`true` is a 200 000-point brute-force minimum over the parametric boundary.) That is a **40.68-unit discontinuity across a 0.002-unit step**, at a point where the declared bound is 1.00. Evaluating the discriminant there in double precision gives `d ≈ −6.4e-6` — the seam, confirming the cause.

The bad locus is a 1-D curve (the ellipse's evolute continued outward), so a lattice hits it rarely but not never. Census over four aspect ratios, 0.01-unit x-spacing, using an "isolated relative to both x-neighbours by > 0.5 units" test:

| rx × ry | spiked samples |
|---|---|
| 100 × 2 | 16 / 40 994 562 |
| 80 × 10 | 2 / 26 238 975 |
| 40 × 20 | 2 / 6 566 913 — worst deviation 1.822 where the neighbour reads 0.565, i.e. **4.2×** |
| 60 × 25, 50 × 50, 20 × 70, 30 × 29, 50 × 49 | 0 on that particular lattice (60 × 25 does spike on a 0.002 lattice, above) |

Why it matters, in the brief's own terms: this is an **over**-report against a declared bound of 1, so a consumer stepping `reportedDistance / B` along a ray will overshoot at those points and punch a hole. It also shows up as single-pixel garbage for anything that reads the magnitude (extrusion depth, bevel factor, the edge band). On a 256×256 tile containing an ellipse the expected hit rate is roughly 1 pixel per 10–100 tiles, i.e. an intermittent flicker rather than a constant artefact, which is precisely the kind of thing that survives to shipping.

The audit does not find it: V4's step is `h = extent/(res−1)` ≈ 0.47–1.5 canvas units with 8 directions, far too coarse to straddle the seam, and the Ellipse is not among V5's subjects at all. Substituting `h = 0.005` and 64 directions raises Ellipse 60×25 from **1.0002 to 4051.46**; every other primitive stays at 1.00 under the same refinement.

Suggested fix: detect `|d| < ε·max(1, c³)` and either blend the two branches or fall back to two or three Newton iterations on the parametric angle. Whatever is chosen, add the Ellipse to V5 and add a fine-step pass to V4.

### 4.2 A zero-thickness Shell silently returns the unshelled solid — `ShaperCompiler.cs:354` + `ShaperEvaluator.cs:104`

`EmitShell` sets `p1 = thickness <= 0f ? 1f : 0f` and the evaluator treats a non-zero `p1` as a structural early-out, returning the child's value untouched. So a node with `shell.enabled = true, thickness = 0, alignment = Centred` is bit-identical to the same node with no shell at all — measured: both report inside = 1 053 / 6 561 samples, min −20, max +50 on a 60×40 rect.

The correct value is `abs(d) − 0 = |d| ≥ 0`, i.e. an empty interior — a wall that has thinned to nothing. What the user gets instead is the **solid shape reappearing**. Dragging the thickness slider down to zero therefore inverts the result rather than emptying it. Spec §7 declares exactly one identity, `enabled == false`; this is a second, undeclared one, and it is the wrong answer rather than a conservative one. (The same guard on `Inward`/`Outward` is likewise wrong: `max(d, −d − 0) = |d|`, not `d`.)

### 4.3 N-gon corner rounding shrinks the polygon; the spec requires the outer extent to be preserved — `ShaperPrimitives.cs:214`

`apothemInner = apothem − corner` with `halfEdge = apothemInner·tan(π/n)`, and the field is offset back out by `corner`. That preserves the **apothem** (the flat faces stay exactly where they were, as the code comment says) but shrinks the **circumradius**. Measured by bisection over 720 directions against an authored radius of 50:

| sides | corner 0 | corner 5 | corner 15 | corner 35 |
|---|---|---|---|---|
| 3 | 50.000 | 45.000 | **35.000** | **25.025** |
| 5 | 50.000 | 48.820 | 46.459 | 41.738 |
| 6 | 50.000 | 49.227 | 47.680 | 44.586 |
| 8 | 50.000 | 49.588 | 48.764 | 47.116 |
| 12 | 50.000 | 49.824 | 49.471 | 48.765 |
| 32 | 50.000 | 49.976 | 49.927 | 49.831 |

Spec §5.1: *"`cornerRadius` is applied by the usual offset (`d − r`) with the polygon's apothem reduced to match, **so the outer extent is preserved**."* A triangle authored at radius 50 with a corner radius of 35 draws at circumradius 25 — half the authored size. This was not among the six reported deviations. The field itself remains an exact SDF of the shape it actually draws (sup|∇f| ≤ 1.0005 across all 24 cases), so this is a spec-conformance and authoring-surprise defect, not a bound defect. Either the code should scale so the circumradius is held, or the spec sentence should be corrected and the behaviour documented on the dial.

### 4.4 The star's valley-radius floor is Pyre's pixel constant carried into canvas units, and can exceed the tip radius — `ShaperPrimitives.cs:235`

`rIn = Mathf.Max(0.5f, R * (1f - len))`. In Pyre, `0.5` is half a texture pixel and `R` is a particle radius in pixels, so the floor is a sub-pixel guard. In Shaper the units are canvas pixels and `R` is authored, so:

- at `starRadius = 0.4, length = 1` the floor gives `rIn = 0.5 > R = 0.4` — the valleys sit **outside** the tips and the polygon is inside-out, which invalidates the star-shapedness premise the sign test rests on. Measured: no NaN, no exception, and sup|∇f| still 1.00003 (the segment-distance magnitude stays exact and the sign happens to survive), so it is latent rather than live today. It is still a shape the compiler will happily emit.
- at `starRadius = 100, length = 1` the valleys sit at 0.5 canvas units regardless of `R`, so the star's proportions change with its size. That is a size-dependent shape, not a scale — surprising for an authored primitive.

Either scale the floor (`max(R·ε, …)`), or drop it and clamp `length` instead, or document that `starRadius` below ~1 is out of range.

### 4.5 `ShaperSweep.start` / `.extent` are one field meaning two different quantities — `ShaperNode.cs:35-38`

`start` is "degrees from +X" on a radial child and "a 0..1 fraction of the length" on a longitudinal one; `extent` is degrees (default 360) or a 0..1 fraction (clamped from that same 360). Which one applies is decided by the child's declared `sweepAxis`, which the user does not set. This is exactly the "one slider showing two different quantities" fault that the implementer correctly identified in the reference app's `viscosity` and split into two fields (`ShaperNode.cs:19-24`) — reintroduced here. Minor, but it will bite the Wave-3 UI: a default `extent = 360` silently means "full length" for a capsule and "full turn" for everything else, and any UI that shows a degree suffix will be wrong half the time.

### 4.6 `hasLeadingNonAdd` and `hasSingularTransform` record only the first occurrence in the whole program — `ShaperCompiler.cs:134-138`, `:211-215`

Both flags are guarded by `if (!st.program.hasX)`, so a tree with three offending bags surfaces one node name to the UI. Cosmetic, but the flag exists precisely so the UI can point at the problem, and it will point at only one of them.

### Categories where I found nothing

- **Sweep and shell identity.** Both are exact early-outs before any arithmetic (`ShaperEvaluator.cs:91` for sweep, `:104` for shell), and `EmitShell` emits no op at all when `enabled == false` (`ShaperCompiler.cs:343`). 0 bit mismatches over 248 897 samples each, 17 primitive cases. No defect.
- **The `σ_min` rescale and the singular case.** The rescale is applied at `ShaperEvaluator.cs:72` (leaf) and `:98` (sweep wedge), using the smallest singular value of the full accumulated forward map, in closed form exactly as §3 specifies (`ShaperMatrix.cs:72-82`). `TryInvert` refuses below `|det| < 1e-12` and `EmitNode` independently guards `sigmaMin <= 1e-9`, so there are two barriers before a divide. Both `scale = (0,0)` and `scale = (1,0)` publish Empty and set the flag. No defect.
- **The star's clamp.** `ShaperPrimitives.cs:251-253` is verbatim Pyre including `0.02`/`0.98`, the `+skew`/`−skew` split, and `vA = offA`, `vB = sector − offB`; the symmetry proof is carried in the comment; the two-valleys-per-sector geometry with the flat base chord is present and the coincide case degenerates correctly. No defect.
- **The star's neighbour-sector window.** Verified exact against brute force over 29 million comparisons (§3.6). No defect.
- **Y-flip.** `TipUp = +π/2` (`ShaperPrimitives.cs:110`), `ShaperMatrix.Rotate` is counter-clockwise, `ShaperSampleGrid.Y(iy)` increases with `iy`, and `FillTile`'s `y0` is documented as the bottom row. Nothing is flipped and no reference-app angle was ported. No defect.
- **`1e6` sentinel.** Absent; `ShaperField.Empty = 1e9f` with an `EmptyThreshold`/`IsEmpty` pair, and it composes exactly under all five combine paths (verified: `min`, `max`, `SmoothMinShaped`, `SmoothMaxShaped` and the soft-carve branch all pass Empty through unchanged or absorb it correctly). No defect.
- **Hot-path purity.** 0 bytes, 0 gen-0 collections over 655 360 samples; no LINQ, `System.Random`, virtual/interface/delegate dispatch anywhere in `Runtime/Shaper` (§3.7). No defect.
- **Menus and windows.** No `[MenuItem]`, `EditorWindow`, `ScriptableObject` or `InitializeOnLoad` in either new folder. No defect.
- **Asmdefs.** Runtime references `com.Lautaro-Arino.Laubrary.ZuiRuntime` and nothing else, exactly as §0 requires. The Editor asmdef references the runtime asmdef **plus** `ZuiRuntime`; §0 says "the runtime asmdef" only. Unity asmdef references are not transitive, and `ShaperFieldAudit` constructs `ZUIValue`s, so the extra reference is required for it to compile — the spec sentence is what is wrong here, not the code. Not counted as a defect.
- **Degenerate inputs.** No NaN, no infinity and no exception in any of the 30 degenerate cases I threw at it (§3.8). Two of them produce *wrong shapes* (findings 4.2 and 4.4) but nothing produces a bad float.

---

## 5. Verdict on the six reported deviations

1. **Ellipse as an exact cubic solve rather than the spec's `(k0, k1)` approximation.** The stated reason is correct — the approximate form really is discontinuous at the centre when `rx ≠ ry`, with limits `−rx` along X and `−ry` along Y, so no finite bound could be declared for it. But the replacement is not unconditionally better: it trades a bounded, everywhere-continuous over-report for an exact field with isolated ~7× blow-ups on the solver's branch seam (finding 4.1), which for a *bound* consumer is strictly worse. **Right decision, incomplete execution.** It needs a seam guard before it can honestly claim bound 1. Note also that the spec's own registry table says the Ellipse's bound is "measured, declared with margin", and the code declares a flat 1 with no margin (`ShaperPrimitives.cs:128`) — a third, unreported deviation that finding 4.1 turns from pedantic into material.
2. **Soft Intersect added beyond the spec's table.** **Improvement.** It is the exact negation dual of soft Add, it is one line (`ShaperOps.cs:52`), it carries the same proof with no new argument needed, and its absence would have left the mode set asymmetric — Add would have had a softness and Intersect would not, for no reason. Measured at 1.00021 worst.
3. **`origin` as a local-space point rather than a 0..1 fraction of the node's extent.** **Improvement**, and structurally so. The reference app's form requires a node to know its own width and height, which is exactly what a bag does not have; a local point needs no size at all. It also removes the class of bug the spec names in §3 (a warp pivoting at the top-right corner) rather than just moving it.
4. **Softness split into two authored fields rather than the reference app's overloaded `viscosity`.** **Improvement**, and directly supported by REF-MATHS trap #6: the same stored field really is a band half-width in canvas units for `softAdd` and a dimensionless fraction for `subtractSoft`. Two fields is the honest representation. See finding 4.5 for the one place the same lesson was not applied.
5. **Three combine modes with softness as a property, rather than the reference app's four modes with `subtractSoft` separate.** **Improvement**, and the natural consequence of #4. It also makes `ShaperBound.Combine`'s switch read directly against the spec's §4.1 table.
6. **`Empty = 1e9` named constant with an `IsEmpty` threshold, rather than the bare `1e6` literal.** **Improvement**, and the spec asked for it. Verified that it composes exactly under every combine path, so R1's leading-member rule falls out with no special case as claimed.

Three further deviations from the spec were **not** reported and are findings above: the N-gon's outer extent (4.3), the zero-thickness shell identity (4.2), and the Ellipse's bound declared flat rather than "measured, with margin".

---

## 6. What the audit claims to test but does not

This is the section I would act on first, because a hollow test is worse than a failing one.

1. **V4's instrument is too coarse to find a localised violation.** `MaxGradient` uses `h = step/2`, which is 0.47 to 1.72 canvas units at the extents it runs at, and only 8 directions. Re-running the same code path at `h = 0.005` with 64 directions takes Ellipse 60×25 from **1.0002 to 4051.46**. The docstring is right that the directional quotient can never *manufacture* a violation — but it can miss one entirely, and here it does. The resolution ladder (65/129/257) does not help because it moves the sample grid, not the probe step, and `h` is tied to the grid.
2. **`BoundTolerance = 1.02` decides the PASS/FAIL word.** Because the directional quotient is bounded by `L` by construction, any reading above ~1.001 is real signal, not slack. A genuine 1.019 violation would print "ok".
3. **V5 says "over tens of thousands of directions, inside and outside".** The 20 000 is the *boundary cloud's* direction count. The ratio itself is sampled on a **61 × 61 = 3 721-point grid**, and every point closer to the boundary than 3 % of the extent is skipped outright (`floorDist = extent * 0.03f`). So the headline number is computed from under four thousand points, none of them near the surface.
4. **V5's "true" distance is the nearest point in a finite cloud**, which always over-estimates the true distance and therefore systematically *under*-states every ratio it reports. Conservative in the wrong direction for a check whose job is to catch over-reporting.
5. **V5 omits the Ellipse** — the one primitive the spec itself flags as needing a measured bound, and the one that is actually broken.
6. **V1, V2 and V3 are tautologies by construction.** Every identity they check is a structural early-out in the code (`carveStrength <= 0f`, `carveStrength >= 1f`, `blendWidth > 0f`, `p5 != 0f`, `p1 != 0f`), so the tests exercise the `if`, not the maths. That is defensible — the spec asked for structural identity — but V3(c) is presented as verifying the reference app's own correctness argument ("at strength 1, `band = 0`, so the expression degenerates exactly to `max(a, −d)`"), and it does not: the code never evaluates the expression at strength 1, and its own comment at `ShaperOps.cs:75-78` explains that the algebra would *not* degenerate exactly because float `sin(π)` is −8.74e-8. The correct catch is undermined by a test that no longer tests it. The same applies to V3(a) and soft Add at width 0.
7. **V7's second assertion is not performed on the subject.** Spec §8 V7 asks to "assert that the *maximum* over the field is materially higher" than the landmark. On Shaper's own star both are 1.0000, so the assertion is demonstrated on the reference-flower witness instead. The audit says this openly in its own output, so it is honest — but the regression it installs guards the witness, not the star.
8. **V4 never composes anything.** No bag deeper than one level, no combine with more than two members, no Star or NGon under a transform, no combine under a transform, no sweep or shell over a bag. Every bound-composition rule in §4.1 is exercised at depth 1 only.
9. **Nothing in V1–V9 tests a degenerate input.** No zero or negative size, no `sides < 3`, no `arms < 2`, no zero-thickness shell, no zero-extent sweep, no empty tree, no leading Subtract, no `scale = 0`. All of those are in the spec's own contract (`hasLeadingNonAdd`, `hasSingularTransform`) and two of them found real defects here (4.2, 4.4).
10. **The coverage kernel is barely tested.** V9 reads coverage at two points. `edgeSoftness` is never exercised at any non-zero value, and the `halfBand = max(edgeSoftness, 0.5·pixelSize)` rule (spec §1.1) is never checked at all.
11. **V8 is sound but thin** — one tree, one grid size, one tile decomposition. Tile independence is structural (`FillTile` derives `x`/`y` from the absolute sample index) so the risk is low, but the check does not vary `dstOffset`/`dstStride` beyond the packed-row case.

---

## 7. Bottom line

The bound maths is right, and I say that from my own derivation and my own measurement, not from the code's comments: `max(B_a, B_b)` is correct for soft Add, soft Intersect and soft Subtract at every strength and every sharpness, and the hand edit to `ShaperBound.cs` is **not** an under-declaration. The engine compiles clean, allocates nothing per sample, has no menus or windows, has no `1e6`, is not y-flipped, and its star — including the neighbour-sector window that had already been widened once — is exact to 3e-5 against brute force over 29 million comparisons.

One primitive is not what it claims. `ShaperSdf.Ellipse` over-reports by a measured **7.24×** against a declared bound of 1 on a thin locus the audit's instrument is too coarse to see, and that is the exact failure mode the brief names: a consumer stepping `d/B` will overshoot there. Until it is guarded, the Ellipse's declared bound of 1 is not verified, and by extension neither is any tree containing one. Everything else measured at or below 1.0014, all of which is float noise in the instrument.

---
---

# SECOND PASS — independent re-verification after the six fixes

Same verifier, same instruments, no part in writing either the code or the fixes. Subject: `D:\UNITY\Laubrary Dev - Shaper`, branch `feat/shaper`, after another agent's fixes for F1–F6. Shaper editor PID 261208 (force-restarted after my pass-1 `ellipse2.cs` probe wedged its main thread — my fault; **every probe in this pass runs on a background thread writing to a file**, following the `runaudit.cs` pattern). Nothing under `D:\UNITY\Laubrary Dev\Assets\` touched, no tree-mutating git command, no commit.

Pass-2 probe sources and outputs: `D:\UNITY\_verify_T0105\` — `ell_attack.cs`/`ELL.txt`, `instrument.cs`/`INSTR.txt`, `fixes.cs`/`FIX.txt`, `final.cs`/`FIN.txt`, `runaudit2.cs`/`VAUDIT.txt`.

**Nothing below is taken from the fixer's numbers.** Every figure is from my own run, and the fixer's audit (`VAUDIT.txt`) was re-run by me rather than quoted.

## S1. The new ellipse, attacked hardest

### S1.1 Discontinuity sweep — nothing found

Rounding-corrected max directional quotient (16 directions, `h = size·1e-3`) over a 361² grid spanning ±1.8× the shape, for **21 aspect-ratio families deliberately chosen to be different from the fixer's list** — near-circular straddling the `1e-5·max(1,max(rx,ry))` fast-path threshold from both sides, extreme flat, tiny and huge absolute size, and minor-axis-first orderings. About 87 million evaluations per family.

| family | worst | family | worst | family | worst |
|---|---|---|---|---|---|
| 50 × 50 | 1.00002 | 60 × 25 | 1.00002 | 1000 × 0.5 | 1.00002 |
| 50 × 50.0004 | 1.00004 | 40 × 20 | 1.00003 | 500 × 0.01 | 1.00002 |
| 50 × 50.0006 | 1.00003 | 80 × 10 | 1.00002 | 2 × 900 | 1.00002 |
| 50 × 50.01 | 1.00001 | 100 × 2 | 1.00000 | 0.01 × 0.004 | 1.00003 |
| 50 × 50.5 | 1.00002 | 1 × 1.000012 | 1.00004 | 0.001 × 0.0009 | 1.00003 |
| 50 × 51 | 1.00004 | 1 × 1.001 | 1.00003 | 100000 × 40000 | 1.00001 |
| 25 × 60 | 1.00002 | 2 × 100 | 1.00000 | 1000000 × 100000 | 1.00001 |

**Worst over everything: 1.00004.** Re-measuring the worst point of each family at `h/100` gives 0.994–1.005, i.e. it moves *down* — the signature of float noise, not of a jump. For comparison, the pass-1 code read **4051** on this instrument. The old seam is gone and no new one appeared.

### S1.2 Against an independent double-precision oracle, at the structurally dangerous places

My own oracle (4096-angle coarse scan + 160 ternary refinements in `double`, independent of the audit's). 14,411 points per family — **302,631 in total** — deliberately placed where a seam would live: the centre; dense along all four half-axes out to 2× the size; a 601-point 1-D scan straight through the **major-axis evolute cusp** along it, across it, and at a `1e-7·e1` offset off it; a 401-point scan through the **minor-axis cusp**; 720 angles × 9 radial factors (0.99, 0.999, 0.99999, **1.0**, 1.00001, 1.001, 1.01, 1.5, 4.0) so the boundary itself is straddled; and a 61² interior grid.

| family | worst ratio | worst absolute error, as a fraction of the shape | NaN | sign errors |
|---|---|---|---|---|
| 60 × 25 | 1.0000380 | 2.2e-7 | 0 | 0 |
| 40 × 20 | 1.0000460 | 2.5e-7 | 0 | 0 |
| 80 × 10 | 1.0002140 | 3.8e-7 | 0 | 0 |
| 100 × 2 | 1.0000400 | 2.4e-6 | 0 | 0 |
| 50 × 51 | 1.0000390 | 2.7e-7 | 0 | 0 |
| 1 × 1.001 | 1.0000560 | 2.7e-7 | 0 | 0 |
| 0.001 × 0.0009 | 1.0000550 | 2.4e-7 | 0 | 0 |
| 1000000 × 100000 | 1.0000440 | 5.0e-7 | 0 | 0 |
| 1000 × 0.5 | 1.0001350 | 1.0e-4 | 0 | 0 |
| 500 × 0.01 | 1.0001580 | 3.1e-3 | 0 | 0 |
| *(all 21 families)* | ≤ 1.00021 except the fast-path case (S1.4) | | **0** | **0** |

The two 50000:1 and 2000:1 aspect ratios show the largest absolute errors, but at ratios of 1.00016 and 1.00014 — and at that aspect my own oracle's angular parameterisation is itself near its resolution limit, so I read those as at-instrument-precision rather than as a finding.

The fixer's specific claim checks out: at `rx=60, ry=25, x=−23.27736, y=−29.65` — the exact point that read **47.198 against a true 6.514** in pass 1 — the shipped code now returns **6.51391** against my oracle's **6.51391**.

### S1.3 The iteration cap is not silently truncating

I re-implemented the shipped solver exactly (same bracket, same safeguarded Newton, same `tn == t` exit) and ran it at cap 48 and cap 5000 over **760,000 general-branch points** across all 21 families:

```
cap(48) reached 0 times;  max iterations actually used = 43
results differing from cap-5000 by more than 1e-9 relative: 0
```

43 of 48 is a thinner margin than is comfortable, but the failure mode the question is about is structurally excluded rather than merely unobserved: every non-Newton step bisects a **live bracket** that is re-narrowed from the sign of `f` before the step, so at the cap the root is still bracketed to at worst `range/2^48`. The solver cannot return an unbracketed root, capped or not.

### S1.4 One residual, characterised and benign: the circle fast path

`|rx − ry| < 1e-5·max(1, max(rx,ry))` returns `|p| − (rx+ry)/2`, i.e. it draws a **circle of the mean radius** instead of the authored near-circular ellipse.

| rx × ry | in fast path | worst abs. error vs the true ellipse | as a fraction of rx | worst ratio near the boundary |
|---|---|---|---|---|
| 50 × 50.0002 | yes | 1.07e-4 | 2.1e-6 | **21.36** |
| 50 × 50.00049 | yes | 2.52e-4 | 5.0e-6 | **50.35** |
| 50 × 50.00051 | no | 6.23e-6 | 1.2e-7 | 1.52 |
| 1 × 1.000009 | yes | 4.60e-6 | 4.6e-6 | 45.90 |
| 1000 × 1000.009 | yes | 4.65e-3 | 4.6e-6 | 46.08 |
| 100000 × 100000.9 | yes | 0.469 | 4.7e-6 | 46.48 |

Read this correctly, because the big ratio is not what it looks like. The **absolute** silhouette error is bounded by half the axis difference, which the threshold caps at about 5e-6 of the shape's own size — invisible, and the guard is necessary, because that is exactly where `r0 = (e0/e1)²` approaches 1 and `c = r0 − 1` approaches zero in the general solver. And it is **not a bound violation**: the fast-path field is an exact circle SDF, so the gradient magnitude is exactly 1 and the declared bound holds against its own zero set (S1.1 measures 1.00002 there). The 50.35× is reported-over-distance-to-the-authored-ellipse evaluated where that distance is about 5e-6, i.e. the ratio metric dividing by almost nothing.

It is still worth two sentences of documentation, and one test change: **V11 does not cover the band at all** — its aspect list steps from 50×50 (exactly circular, zero error) straight to 50×51 (outside the threshold), so nothing exercises 50 × 50.0002. If V11 ever does sample there it will report a ratio in the tens and look like a regression. Add a case inside the band with an **absolute-error** assertion rather than a ratio one, and say on the primitive that near-circular ellipses are drawn as circles to within 5e-6 of their size.

## S2. The rounding-bound correction, audited sceptically

The correction subtracts `RoundingUlps · FloatEps · max(|a|,|b|)` from the difference-quotient numerator. That is mathematically legitimate — if the two evaluations carry total rounding at most `delta`, then the true difference is at least the computed difference minus `delta`, so the corrected quotient is a lower bound on the true one — **provided the ulp count really bounds the rounding.** Three tests.

### S2.1 The decisive test: inject a violation of known size through the audit's own instrument

I hand-built a `ShaperProgram` with a single `Leaf` op carrying `distanceScale = k` over a disc, so the field is exactly `k·(|p| − 40)`, its Lipschitz constant is exactly `k`, and its declared bound is 1. Then I called the audit's own private `MaxGradientDetailed` on it by reflection and applied the audit's own pass logic and constants.

| k | broad (h = ext·1e-3) | fine (h = ext·1e-5) | fineFloor | okBroad | okFine | audit verdict |
|---|---|---|---|---|---|---|
| 1.0000 | 1.00001 | 1.00202 | 0.05281 | yes | yes | pass |
| 1.0005 | 1.00051 | 1.00252 | 0.05284 | yes | yes | pass |
| **1.0010** | **1.00101** | 1.00302 | 0.05286 | no | yes | **FAIL — caught** |
| 1.0050 | 1.00501 | 1.00703 | 0.05307 | no | yes | FAIL — caught |
| 1.0500 | 1.05001 | 1.05213 | 0.05545 | no | yes | FAIL — caught |
| 1.1000 | 1.10001 | 1.10222 | 0.05809 | no | no | FAIL — caught |
| 2.0000 | 2.00002 | 2.00404 | 0.10562 | no | no | FAIL — caught |
| 7.2400 | 7.24005 | 7.25465 | 0.38235 | no | no | FAIL — caught |

**The correction does not mask.** The instrument reads `k` back to five decimal places and the composite verdict flips at exactly `BoundTolerance = 1.001`. A violation the size of the pass-1 ellipse bug (7.24×) is read as **7.24005**.

### S2.2 The old bug, re-measured with the correction ON

I re-implemented the pass-1 (broken) closed-form solve verbatim and measured at my named failing point:

| h | raw quotient | rounding-corrected | removed by the correction |
|---|---|---|---|
| 0.13 (`hBroad` at extent 130) | 1.000 | 1.000 | 2.4e-5 |
| 0.0013 (`hFine` at extent 130) | 26.100 | **26.098** | 0.0024 |
| 0.001 | 76.858 | **76.854** | 0.0032 |

The correction removes 0.004 % of the signal. **The old bug would still have been caught, by a factor of about 26,000 over the threshold.** Confirmed as requested.

### S2.3 Masking headroom, in general

The amount subtracted is `RoundingUlps·eps·|f| / 2h`. On the **broad** pass (`h = extent·1e-3`) that is 0.0004 at |f| = 100 and 0.0037 at |f| = 1000, both at extent 130 — so at large field magnitudes the broad subtraction slightly exceeds `BoundTolerance` itself, and a genuine ~1.004 violation on a big-magnitude field could pass. Small, but it is the honest number. On the **fine** pass it is 0.037–0.37 over the same range, which is why that pass carries a floor.

### S2.4 Three criticisms of the hardened instrument

1. **The comment overclaims, demonstrably.** The `RoundingUlps` block says the correction "still can never manufacture a violation, **at any h**". On a provably exact Rect the ULPS = 8 correction reads:

   | h | exact Rect, corrected | ULPS actually required to stay at or below 1.001 |
   |---|---|---|
   | 0.13 | 1.00001 | 0 |
   | 0.013 | 1.00003 | 0 |
   | 0.0013 (the configured `hFine` at extent 130) | 1.00058 | 1.7 |
   | 0.0005 | **1.00337** | 39.2 |
   | 0.0001 | **1.00696** | 23.7 |
   | 0.00003 | **1.03692** | 65.4 |

   So 8 ulps is a sound 4.7× margin **at the configured step** and insufficient below it. The claim should be scoped to the configured `RefineStepFraction`, or `RoundingUlps` should scale with `h`. (This also vindicates the fixer against my pass-1 recommendation: I suggested `h = 0.005`, and at that scale the *raw* quotient manufactures up to 1.106 on an exact Rect. My recommendation needed exactly the correction they added.)

2. **`fineFloor` double-counts, and the fine pass contributes nothing to detection.** `okFine = fine ≤ bound·1.001 + fineFloor`, where `fine` has *already* had the same `ULPS·eps·mag/(2h)` subtracted from its numerator and `fineFloor` is that expression again. Subtracting once already makes the reading a rigorous lower bound, so the added floor is not needed for soundness and roughly doubles the leniency. Measured consequence in S2.1: at k = 1.05 `okFine` is **true** while `okBroad` is false; the fine pass alone does not fail until k is about 1.1. The verdict is `okBroad && okFine`, so the composite is still sound — but the fine pass is a *localiser*, not a gate, and the code reads as though it were a second gate. Drop the floor or drop the subtraction, not both.

3. **The residual blind spot, stated precisely.** A violation confined to a spatial band narrower than `hBroad` (0.13 canvas units at extent 130) **and** smaller than about 1.1× will pass: too narrow for the broad probe to straddle reliably, too small for the fine threshold once its floor is added.

### S2.5 The thing I care about most: the regression protection is thinner than 12/12 PASS implies

I re-ran the fixer's audit myself (`runaudit2.cs` to `VAUDIT.txt`, 230,983 ms): **V1–V12 all PASS**, V11 worst ratio 1.000004, V12 circumradius exactly 50.000 in all 112 cells. That is a real result. But two sampling-density facts sit behind it:

- My pass-1 census measured the old spike's hit rate on a lattice at about **3e-7 per sample**. V4 evaluates roughly 6.4 M field points per case (3 resolutions × 257² × 16 directions × 2 probes), so the expected number of hits on the *old* code is of order **2** — detection, but marginal and probabilistic, not deterministic.
- **V11's dense sweep is 121² = 14,641 points per aspect ratio** (about 175 k total), a grid step of about 1.25 canvas units, and it skips every point closer than 1 % of the size to the boundary. At a 3e-7 hit rate that is about **0.05** expected hits. V11 catches the known bug **only** through its three hard-coded regression points at `x = −23.279…`, which guard that one seam and nothing else.

For comparison my S1.1 sweep is about 87 M evaluations per family over 21 families, and S1.2 is 302,631 oracle-checked points placed on the structures where seams actually live. So: **the fix is sound — my far denser probes found nothing — but if anyone edits `ShaperSdf.Ellipse` again, V11 as written will very likely not notice a new seam.** The cheap fix is to replace part of V11's coarse 2-D grid with fine 1-D scans (step about 1e-3 of the size) along a few chords crossing the evolute and both axes, which is where every seam this primitive has had was located.

## S3. F2–F6, each against a concrete input

**F2 — zero-thickness Shell. Fixed.** 60×40 rect, 81² samples over ±60:

| alignment | t = 0 | t = 1e-7 | t = 0.5 |
|---|---|---|---|
| Centred | inside **0**, min 0 | inside 54, min −5e-8 | inside 54, min −0.25 |
| Inward | inside 0, min 0 | inside 0, min 0 | inside 0, min 0 |
| Outward | inside 0, min 0 | inside 0, min 0 | inside 0, min 0 |

`Centred, t = 0` now empties the interior (the field becomes `|d| ≥ 0`) instead of returning the solid, and `t = 1e-7` produces a genuine hairline wall. `shell.enabled = false` vs no shell: **0 bit mismatches / 14,641**, so the structural identity survived. (The `inside = 0` readings for Inward/Outward at t = 0.5 are my 1.5-unit grid stepping over a 0.5-unit wall, not a defect — `Inward` correctly leaves the exterior bit-identical to the plain rect, 5,508 of 6,561 samples.)

**F3 — N-gon rounding preserves the circumradius. Fixed.** Measured by 1440-ray bisection, authored radius 50:

| sides | corner 0 | 5 | 15 | 35 | 50 |
|---|---|---|---|---|---|
| 3 | 50.0000 | 50.0000 | 50.0000 | 50.0000 | 50.0000 |
| 6 | 50.0000 | 50.0000 | 50.0000 | 50.0000 | 50.0000 |
| 64 | 50.0000 | 50.0000 | 50.0000 | 50.0000 | 50.0000 |

(all of 3, 4, 5, 6, 8, 12, 32, 64 identical). Compare pass 1, where n=3/corner=15 measured **35.000** and n=3/corner=35 measured **25.025**. The apothem now *grows* from `50·cos(π/n)` toward 50 as the corner radius rises, so full rounding gives the circumscribed circle — the right behaviour for a dial named "radius". Still an exact SDF: **sup|grad f| = 1.00006** over sides 3…64 × corner {0, 3, 12, 30, 49.9} at rotation 13°.

**F4 — the valley radius is now relative to R. Fixed.** Over `R` in {0.001 … 100000} × `len` in {0 … 1}: `rIn > R` **never** happens (pass 1: it did, at R = 0.4). `rIn/R` bottoms out at exactly the 0.01 floor. And the default star is untouched: shipped `rIn = 19` is **bit-identical** to Pyre's `max(0.5, 50·(1−0.62))`, with `vA == vB` at `baseWidth = 1` so the classical two-segment collapse still holds.

**F5 — sweep fields split. Fixed and genuinely independent.** With `startDegrees = 37, extentDegrees = 360, startFraction = 0, extentFraction = 1` across all seven primitives: **0 bit mismatches / 102,487**. A longitudinal child ignores `extentDegrees = 45` (0 mismatches / 6,561) and a radial child ignores `extentFraction = 0.2` (0 / 6,561) — the two pairs cannot be crossed. A real radial sweep still bounds at 1.00004.

**F6 — counts. Fixed.** Three bags each with a leading non-Add member gives `leadingNonAddCount = 3`; two singular transforms gives `singularTransformCount = 2`. The first offending node name is still reported alongside.

**Identities and hot path, re-verified independently of the audit:** V1 sweep identity **0 / 102,487** bit mismatches, V2 shell identity **0 / 102,487**, and `FillTile` at 10 × 65,536 samples over a Star-union-anisotropic-Ellipse bag: **0 bytes allocated, 0 gen-0 collections**.

**Degenerate battery, my own (13 cases, independent of V10):** zero NaN, zero infinity, zero exceptions. `Ellipse rx=1e-4 ry=1e6`, `NGon corner=1e9` (clamps to the circle), `Star arms=0 radius=0`, `Shell thickness=−5` (clamps to 0, so an empty interior rather than the solid), `Sweep extentDegrees=1e9` (at or above 360 so identity), empty bag and null root (both `1e9`, no exception).

## S4. New defects introduced by the fixes

**None found.** The two changes that alter geometry were re-swept from scratch rather than spot-checked:

- **F3's new rounding** — exact in every case measured (S3), circumradius exact to four decimals in all 56 combinations, and `corner = radius` degenerates cleanly to the circumscribed circle rather than to a degenerate polygon.
- **F4's new valley radius** — the star's 5-sector distance window is still exact under it. Brute force over **all 3N segments** versus the shipped 15-segment window, arms 2…20 × length {0, 0.3, 0.62, 0.9, 0.99, 1} × baseWidth {0.1, 0.4, 0.75, 1} × skew {−60, −25, 0, 25, 60} × 6,561 points, about **15 million comparisons**: worst over-report **3.3e-5 canvas units**, worst `sup|grad f|` **1.00012**, both pure float rounding. The window never missed a nearer segment at any arm count.

## S5. Second-pass bottom line

F1 is genuinely fixed, not moved. I attacked it with a wider parameter space than the fixer used, an independent oracle, and points placed on both evolute cusps, both axes, the centre and the boundary itself — about 87 M quotient evaluations per aspect-ratio family plus 302,631 oracle comparisons — and found no discontinuity, no NaN, no sign error, and no ratio above 1.00021 outside the deliberate circle fast path. The 48-iteration cap is never reached (max 43) and cannot silently return an unbracketed root by construction. F2–F6 each verified against a concrete input; V1/V2 bit-identity and the zero-allocation hot path both survived; no new defect.

Two things remain worth the owner's attention, neither a bound violation:

1. **The circle fast path is undocumented and untested.** Near-circular ellipses are drawn as circles of the mean radius, correct to about 5e-6 of the shape's size but with an unbounded *ratio* metric arbitrarily close to the boundary (measured 50.35× at 50 × 50.00049). V11's aspect list steps straight over the band.
2. **The instrument's comment overclaims, its fine pass double-counts its own correction, and V11's dense sweep is roughly 1/400 as dense as it needs to be to catch a seam regression on its own.** The 12/12 PASS is real, and I reproduced it — but for this primitive it currently rests on three hard-coded regression points rather than on the sweep.
