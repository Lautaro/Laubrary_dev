# Shaper shape engine — implementation spec (T-0105)

This is the binding spec for the Wave-2 silhouette generator. It is written to be implemented from directly, without re-reading the three Wave-1 contracts or the two research reports. Where it departs from a source, it says so and why.

Sources it stands on, all in `D:\UNITY\Laubrary Dev\.agenthq\workspace\`: `T-0105\REF-MATHS.md` (the reference maths, transcribed with file:line), `T-0105\CONTRACT-EXTRACT.md` (what the Wave-1 contracts bind), `T-0102\BUFFER_CONTRACT.md`, `T-0103\SHAPE-TREE-RULES.md`, `T-0098\SHAPER_THE_DESIGN.md`.

---

## 0. Where the code goes

All Shaper code lives in the **Shaper working copy**, `D:\UNITY\Laubrary Dev - Shaper`, on branch `feat/shaper`. Nothing in this task touches `D:\UNITY\Laubrary Dev\Assets\`. The AgentHQ board and all workspace documents stay in the main copy.

- Runtime: `D:\UNITY\Laubrary Dev - Shaper\Assets\Packages\Laubrary\Runtime\Shaper\`, asmdef `com.Lautaro-Arino.Laubrary.Shaper`, rootNamespace `Laubrary.Shaper`, referencing `com.Lautaro-Arino.Laubrary.ZuiRuntime` only.
- Editor: `D:\UNITY\Laubrary Dev - Shaper\Assets\Packages\Laubrary\Editor\Shaper\`, asmdef `com.Lautaro-Arino.Laubrary.Shaper.Editor`, `includePlatforms:["Editor"]`, referencing the runtime asmdef. It holds the field audit and nothing else.

**No `[MenuItem]`, no `EditorWindow`, no inspector drawer.** This task is the engine, not the UI. The audit is a plain static class invoked by the verification harness.

Nothing here may reference `Laubrary.Pyre` or `Laubrary.SpriteFx`. Pyre's star *specification* is ported; Pyre's *code* is not.

---

## 1. What a shape node is asked and what it returns

**The call is a block, never a per-sample callback** (BC-1.1). The public entry point fills a rectangular tile of the sample grid in one call, writing into host-supplied flat arrays. The evaluator loops inside; no virtual method, delegate or interface call happens per sample. Tile independence is a conformance requirement: the same tile must produce identical values whether requested alone or as part of the whole grid (BC-1.6).

**The value is a signed distance-like field**: negative inside, positive outside, zero on the boundary (BC-3.3 #3). Units are canvas pixels at every level of the tree — this is what R2's rescale exists to guarantee. Frame is the layer's own local space, +Y up, origin at the layer origin.

**Coverage is derived from the finished distance, once, at the top.** Combining happens in the distance domain (R6); nothing folds coverage.

### 1.1 The coverage kernel — a decision made here, not found

The three Wave-1 contracts specify that every node publishes coverage and that coverage is derived from the combined distance, but **none of them specifies the kernel or its width**. That gap is closed here, and it is flagged as a decision awaiting the owner's nod rather than a rule inherited from a signed-off document:

```
halfBand = max(edgeSoftness, 0.5 * pixelSize)          // pixelSize = canvas units per sample
coverage = 1 - smoothstep(-halfBand, +halfBand, distance)
```

`edgeSoftness` defaults to 0, so the default is a half-pixel antialiased edge and nothing else. Coverage is soft and unbounded in the sense of C3 — a fog, not a stencil — but it is clamped to [0,1] at this stage because a distance field carries no information above 1.

---

## 2. The compiled program — the shape of the evaluator

The authored tree is **compiled once per frame-time `t`** into a flat, immutable program, and the per-sample evaluator walks that program with no allocation, no virtual dispatch, no managed dereference and no `System.Random` (BC-1.3).

The compiler does three things the reference app does not, and each fixes a named defect:

1. **It accumulates the full root→leaf inverse transform per leaf.** A leaf is evaluated by mapping the canvas point straight into that leaf's own local space with one precomputed 2×3 inverse. No point stack, no per-node re-transform.
2. **It stores, per leaf, `distanceScale = σ_min(M)`** — the smallest singular value of the accumulated forward 2×2 linear part. The leaf's local distance is multiplied by it on the way out. This is the rescale that `evalShape` at `index.html:1118` omits, and it is the whole of the measured 5.00× over-report at `scale.x = 0.2`.
3. **It emits post-order RPN**, so evaluation is a flat loop over ops against a small value stack. Fold order is fixed at compile time and no stage may reorder it (R1).

Op kinds: `Leaf` (primitive kind + params + inverse matrix + distanceScale + bound), `Combine` (mode + width + sharpness, pops two, pushes one), `Sweep` (unary, carries its own local-frame inverse), `Shell` (unary).

The evaluator signature is deliberately the general one — a point, not a pixel index — because B7 requires the caller to be unable to tell that v1 resolves straight-down only.

**Sentinel.** The reference app injects a literal `1e6` for an empty tree and for a leading subtract (`index.html:1116`, `:1119`). `1e6` is not a distance and breaks any consumer that reads magnitude. Use a named constant `ShaperField.Empty = 1e9f`, document it as outside the metric contract, and note that it is a *constant* field — gradient zero — so it trivially satisfies any declared bound. A leading non-Add member is **flagged by the compiler, not seeded** (R1): the compiled program records `hasLeadingNonAdd` for the UI to surface later.

---

## 3. The transform block

Exactly R2, which is 3D Shaper's block: `translate`, `rotate`, `scale`, `skew`, `origin`. Composition order, forward:

```
M_forward = T · P · R · S · K · P⁻¹
```

where `P` is the translation to `origin` and `P⁻¹` its inverse. Points travel **down** by the inverse only; nothing is resampled into a buffer. Pyre's `pitch`/`yaw` are explicitly excluded — they bake a perspective scalar and belong to B7.

`origin` being an authored field is the structural fix for the class of bug where a warp pivots at the top-right corner.

**Distances come back up rescaled by `σ_min(M)`.** For a 2×2 matrix `[[a,b],[c,d]]` the singular values are obtained in closed form from `E=(a+d)/2, F=(a-d)/2, G=(c+b)/2, H=(c-b)/2`: `σ_max = hypot(E,H)+hypot(F,G)`, `σ_min = |hypot(E,H)-hypot(F,G)|`. Use that, not an iterative SVD.

**If `σ_min` is zero (singular linear part) the node publishes the empty field and sets a flag.** It must not divide.

**A transform composes its child's bound unchanged.** Proof, one line, and worth keeping in the code comment because it is the reason the rescale is not optional: with `f_parent(p) = σ_min · f_child(M⁻¹(p−t))`, `|∇f_parent| = σ_min · |∇f_child · M⁻¹| ≤ σ_min · L · σ_max(M⁻¹) = σ_min · L / σ_min = L`.

---

## 4. The bound

**The declared bound is a per-node number `B ≥ 1` promising `reportedDistance ≤ B × trueDistance` everywhere in the field, inside and outside.** The safe march step for anything that later steps along a ray is `reportedDistance / B`. Nothing in this task marches; the bound is one float per compiled node and one divide, and it is nearly free here and expensive to retrofit.

**How it is established.** A bound is derived analytically wherever the maths allows, and *verified by dense measurement over the whole field*. It is never read off a landmark point, however natural that landmark looks — the star's lobe tip is exactly right (1.0000×) and is precisely where the star is not at its worst, which is the trap this rule exists to avoid.

The measurement is a **gradient measurement, not a ratio measurement**, and that is a deliberate strengthening. If `sup |∇f| ≤ L` over the plane and `f` is zero on the boundary, then `|f(x)| ≤ L · d(x)` everywhere follows immediately. Measuring the gradient therefore certifies the bound over the *whole* field from a finite sample of it, which sampling the ratio at points cannot do. The audit also runs a direct ratio check against true Euclidean distance by radial bisection for the primitives that have a closed-form boundary, as a cross-check on the gradient method itself.

### 4.1 Composition

| node | bound |
|---|---|
| primitive | its own declared constant (§5) |
| transform | `B_child`, unchanged (§3) |
| combine — hard Add (`min`), Subtract (`max(a,−b)`), Intersect (`max`) | `max(B_a, B_b)` |
| combine — soft Add / soft Intersect | `max(B_a, B_b)` — proven in §4.2 |
| combine — soft Subtract | **explicitly declared** `max(B_a, B_b)`, at every strength (§4.3) |
| Sweep | `max(B_child, 1)` — a half-plane and a wedge are exactly Lipschitz-1 and the combine is a hard `max` |
| Shell | `B_child` — `abs` and hard `max` both preserve the gradient magnitude |

### 4.2 The soft blend is free after all — a correction to an earlier draft of this spec

`smoothMinShaped(a,b,k,n) = min(a,b) − pow(max(k−|a−b|,0)/k, n) · k/(2n)`. Write `h` for `max(k−|a−b|,0)/k`. Differentiating with `a < b`:

```
∂f/∂a = 1 − h^(n−1)/2        ∂f/∂b = h^(n−1)/2
```

`h ∈ [0,1]` and `n = pow(8, sharpness) ∈ [1,8]`, so `h^(n−1) ∈ [0,1]`, so **both weights lie in `[0,1]` and sum to 1**. The result is a genuine convex combination of the two gradients, `|∇f| ≤ max(|∇a|, |∇b|)`, and the soft blend preserves the bound exactly. Declared bound: `max(B_a, B_b)`.

**An earlier draft of this spec claimed the opposite** — that the weights range over `[−1/2, 3/2]` and that soft combining therefore costs a factor of 2, and it recorded that as a correction to `SHAPE-TREE-RULES.md` R6. That was a sign error on the penalty term, caught by the implementer and re-derived twice since. R6 was right; **there is no correction to R6, and nothing downstream should carry one.** The inflated factor is recorded here only so that nobody re-introduces it from a stale copy of this document.

Soft Intersect is the exact negation dual, `−smoothMinShaped(−a, −b, k, n)`, and carries the same bound by the same derivation.

### 4.3 Soft Subtract — an explicitly declared bound that happens to equal the derived one

The task brief requires soft Subtract to carry an **explicit** bound rather than a derived one, on the grounds that at partial strength it is not a distance field at all — it adds an authored bite term:

```
carve = −d − a
band  = sin(strength·π) · reach · 0.6
bite  = −smoothMinShaped(0, −carve, band, n)
result = a + strength · bite
```

The premise is correct: `result` is not the distance to anything for `0 < strength < 1`. But *not being a distance field* and *not being Lipschitz* are different properties, and it is the second one the bound is about. Differentiating, with `w` the smooth-min weight from §4.2 and so in `[0,1]`, and writing `sw = strength · w ∈ [0,1]`:

```
∇result = (1 − sw)·∇a − sw·∇d
```

— a convex combination of `∇a` and `−∇d`. So `|∇result| ≤ max(|∇a|, |∇d|)` and the bound is `max(B_a, B_d)` at every strength, not only at the exact endpoints.

**The declaration therefore stays explicit — its own code path, its own constant, this derivation in the comment — while its value is the proven one.** Declaring the brief's conservative larger number instead would be safe but not free: the bound divides the safe march step, so an unnecessary 4× is a 4× slower rotation feature, which is the exact cost this whole requirement exists to avoid paying. If the owner would rather have the belt-and-braces number, it is a one-line change at one site.

Two properties of the reference formulation must be preserved verbatim, because both are genuine correctness arguments and not preferences (`index.html:1123-1134`):

- Scaling the **carved amount** by `strength`, rather than shrinking the cutter's own field by an offset, is what makes `strength = 0` an exact no-op *everywhere in the field*. The offset alternative silently clips a deep point's magnitude and corrupts downstream extrusion/bevel shading with no visible hole.
- The band `sin(strength·π)·reach·0.6` is zero at both `strength = 0` and `strength = 1`, so at strength 1 the expression degenerates exactly to the hard `max(a, −d)`.

Both give free bit-identity tests, and both are in §8.

### 4.4 How the bound is measured — the instrument matters

Do **not** estimate `|∇f|` as `hypot` of the two axis-aligned central differences. At a kink each axis quotient can independently reach `L`, so that estimator has `√2` of headroom above the true Lipschitz constant and reports ~1.02–1.03 on fields that are provably exactly Lipschitz-1. Use the largest **directional** difference quotient `|f(p+hu) − f(p−hu)| / 2h` over a spread of directions `u`: that is the Lipschitz constant's own definition restricted to a finite sample, so it is bounded by `L` by construction and converges from below.

---

## 5. The primitive registry

Every primitive is defined in its own natural extents with an **exact** signed distance function wherever one exists, which is the point of departure from the reference app. Shaper's registry over-reports by 1.414× on the diamond at a corner, 2.236× on the triangle's slanted side, up to 3.13× inside the star and 2.39× outside it. None of those is inherited.

Every primitive declares three things: its **bound**, its **sweep axis**, and its **support extent** (a local-space bounding half-width/half-height, needed later by the compiler for band widths and by tile culling).

| kind | parameters | SDF | bound | sweep axis |
|---|---|---|---|---|
| `Rect` | `halfW`, `halfH`, `cornerRadius` | exact rounded box | 1 | Radial |
| `Ellipse` | `rx`, `ry` | closed-form approximate ellipse (`k0`,`k1` form), exact when `rx == ry` | measured, declared with margin | Radial |
| `Diamond` | `rx`, `ry` | exact rhombus | 1 | Radial |
| `Triangle` | `base`, `height` | exact isosceles triangle | 1 | Radial |
| `Capsule` | `halfLength`, `radius` | exact capsule | 1 | **Longitudinal** |
| `NGon` | `sides` (3..64), `radius`, `rotation`, `cornerRadius` | exact regular polygon | 1 | Radial |
| `Star` | `arms`, `length`, `baseWidth`, `skew`, `radius` | exact star polygon (§5.2) | 1 | Radial |

`NGon` at `sides = 6` and `sides = 8` replaces the reference app's hexagon and octagon outright. Those were hand-fitted axis-aligned approximations — the hexagon's `a + b·0.55 − 1` needs coefficient `0.5` for a true flat-top regular hexagon, and the octagon's `1.42` is a loose stand-in for `√2`. Both are also regular only in normalised `(|x|/rx, |y|/ry)` space, so a non-square box silently stretches them out of regularity. A true N-gon is the fix and it is one primitive instead of two.

The reference app's shared `pulge.top/middle/bottom` profile dials are **deliberately not in this task**. They are a deformer, not a primitive, and a position-dependent anisotropic scale is precisely the kind of thing whose bound has to be derived carefully — which puts them in the same family as the extrusion and bevel height profiles that T-0109 owns. Implementing them here without their bound would reintroduce the defect this task exists to remove. Say so in the handover rather than leaving it looking like an oversight.

The reference app's per-primitive `skew` is also not reproduced, for a better reason: skew is already in the transform block (§3), where it goes through the `σ_min` rescale and stays bounded. Having it in two places is what leaves it unbounded in one of them.

### 5.1 The N-gon

Standard exact regular-polygon SDF: fold the point into one sector by angle, then take the distance to that sector's single edge, signed by which side of it the point lies on. `cornerRadius` is applied by the usual offset (`d − r`) with the polygon's apothem reduced to match, so the outer extent is preserved. Exactly Lipschitz-1 in both the rounded and unrounded cases.

### 5.2 The star — Pyre's specification, exactly

The parameters are Pyre's, ported by value:

| dial | type | default | range | animatable |
|---|---|---|---|---|
| `arms` | int | 5 | 2 … 20 | no |
| `length` | `ZUIValue` | 0.62 | 0 … 1 | yes |
| `baseWidth` | `ZUIValue` | 1 | 0.1 … 1 | yes |
| `skew` | `ZUIValue` | 0 | −60 … +60 degrees | yes |

Tip radius is `R`. Valley radius is `rIn = max(0.5, R·(1 − length))`. The geometry is a **3N-gon**: N tips at `R`, and **two** valleys per sector at `rIn` joined by a flat base chord — two, not one, which is what lets `baseWidth` thin the arms symmetrically and lets `skew` act as a true pinwheel rather than a rotation.

**The clamp that stops valleys crossing tips, ported verbatim** (`PyreRenderer.cs:3272-3274`), with `sector = 2π/N` and `halfSector = π/N`:

```
lo = 0.02 · sector;  hi = 0.98 · sector
offA = clamp(halfSector · baseWidth + skew, lo, hi)   // valley A, angle past tip k
offB = clamp(halfSector · baseWidth − skew, lo, hi)   // valley B, angle before tip k+1
vA = offA;  vB = sector − offB
```

The symmetry argument is in Pyre's own source at `:3231-3237` and must be carried across as a comment, because it is what makes the clamp provably sufficient rather than merely defensive: unclamped, `offA + offB = sector · baseWidth ≤ sector` (the skew cancels), and raising either offset to `0.98·sector` drives the other to `0.02·sector`, so `offA + offB ≤ sector` always, so `vA ≤ vB` always. The valleys can coincide — which is what `baseWidth = 1`, the default, does, collapsing bit-identically to the classical two-segment star — but they can never cross.

**What is not ported is Pyre's inside test.** Pyre solves a per-ray boundary intersection, `t = cross(E,P1)/cross(E,D)`; that is a hit test, not a field, and it carries no usable distance. Instead: the star is star-shaped about its centre with a radially monotone boundary, so the sign comes from comparing `|p|` against the boundary radius along `p`'s own direction, and the magnitude comes from the exact minimum distance to the boundary segments. Fold the point into its sector, then evaluate the three segments (tip → valley A, valley A → valley B, valley B → next tip) of that sector **and of its two neighbours** — nine segments, `O(1)`, and provably sufficient because the nearest boundary point of a star-shaped polygon lies within one sector of the point's own. Exactly Lipschitz-1.

**Do not cross-port the reference app's star.** It is `hypot(x/rx, y/ry) − (0.42 + 0.58·pow(lobe,0.8))` with `lobe = 0.5 + 0.5·cos(5·angle − π/2)`: a fixed five-lobe cosine flower with the arm count, the sharpness exponent and the phase all hard-coded and no parameters at all, with rounded valleys and rounded tips. It is a flower, not a star polygon, and shipping it would be the day-one regression the task names.

### 5.3 A trap that will otherwise cost a day

**Pyre is y-up and the reference app is y-down.** Pyre writes through `SetPixels32` with a bottom-left origin; the reference app draws to a canvas. So the reference app's star tip at `atan2 = +90°` points *down* on screen, while Pyre's `TIP = π/2` points *up*. BC-3.3 settles it: the frame is +Y up. Port Pyre's angles directly and flip nothing; port any reference-app angle with a sign change.

---

## 6. Sweep

**An operator on the finished shape, not a change to any primitive's formula** (B12). All seven primitives gain it at once.

```
Sweep { axis, start, extent }
```

- **Radial axis.** Keep only a wedge from `start` across `extent`, measured in the node's own local frame. Implemented as a hard intersection with an exact wedge field: for `extent ≤ 180°` the wedge is the intersection of two half-planes through the origin; for `extent > 180°` it is the union of the two. Both are exactly Lipschitz-1, so the composed bound is `max(B_child, 1)`.
- **Longitudinal axis.** For long thin primitives — the capsule today, the line and streak shapes later — a wedge around the centre produces a meaningless bowtie. Instead keep a slab along the primitive's length: `start` and `extent` are fractions of the length, and the operator is a hard intersection with a slab, again exactly Lipschitz-1.

**The axis is declared by the primitive, not chosen by the user.** The compiler reads the child's declared `sweepAxis`; for a bag it is the axis its members agree on, and `Radial` when they disagree.

**Identity must be bit-identical, and is guaranteed structurally, not numerically.** At `extent ≥ 360°` (radial) or full length (longitudinal) the operator returns the child's value by an exact early-out, before any arithmetic touches it. Never rely on "the wedge test happens to pass" — a `max` against a computed value can perturb the last bit.

---

## 7. Shell

```
Shell { enabled, thickness, alignment ∈ { Centred, Inward, Outward } }
```

- Centred: `abs(d) − thickness/2`
- Inward: `max(d, −d − thickness)` — the band from the surface inward
- Outward: `max(−d, d − thickness)` — the band from the surface outward

All three are `abs` and hard `max` of Lipschitz-`B_child` quantities, so the bound is `B_child` unchanged.

**Shell is not Ring's inner radius under a new name.** Subtracting a scaled-down copy of a shape from itself gives a wall whose thickness varies with the shape — thin where the shape is thin. Shell keeps a constant wall thickness everywhere, which is the point, and on a non-radially-symmetric shape the two visibly differ. Treat it as new behaviour.

**Identity is `enabled == false`, which returns the child's value untouched** — again a structural early-out, not a numerically-neutral computation.

**One consequence to test rather than discover**: shelling changes what "inside" means downstream. A shelled disc is an annulus, and everything that later reads inside-ness — a fill, a border, a bevel — sees the new interior. That falls out of the maths rather than being chosen, so §8 tests it deliberately.

---

## 8. Verification — what must actually be run

The audit lives at `Assets\Packages\Laubrary\Editor\Shaper\ShaperFieldAudit.cs` in the Shaper working copy, as plain static methods returning a report string. **No `[MenuItem]`.** It is invoked through the Unity CLI against the Shaper editor on port 7801.

**V1 — sweep identity is bit-identical.** For every primitive, over a dense grid and a spread of parameters, the field with sweep at full extent must be bit-for-bit equal to the field with no sweep node at all. Compare raw `float` bits, not with a tolerance.

**V2 — shell identity is bit-identical.** Same, with shell disabled.

**V3 — soft-combine identities.** Soft Add at `viscosity = 0` equals hard Add bitwise. Soft Subtract at `strength = 0` is an exact no-op *everywhere in the field*, not merely where the cutter is — sample points deep inside both operands. Soft Subtract at `strength = 1` equals hard Subtract bitwise. These three are the reference app's own correctness arguments turned into tests.

**V4 — every declared bound holds, measured.** For every primitive, every transform case and every combine mode: sample `sup |∇f|` by central differences over a dense grid covering the shape and a generous margin outside it, at several resolutions to confirm the supremum has converged rather than been missed between samples. Report measured versus declared for each. A measured value above the declared bound is a failure; a measured value far below it is reported, not silently adopted.

**V5 — the cross-check on V4's method.** For `Rect`, `NGon` and `Star`, additionally measure the direct ratio `reported / trueEuclidean` by radial bisection against the analytic boundary, over tens of thousands of directions, **inside and outside**. This is the same instrument that produced the numbers in the task brief, and it exists to confirm that the gradient method is not itself missing something.

**V6 — the anisotropy case that the reference app fails.** A child under `scale.x = 0.2` must not over-report. The reference app measures 5.00× here; with the `σ_min` rescale the measured ratio must come back to the child's own bound.

**V7 — the landmark trap, kept as a regression.** Measure the star's over-report at its lobe tip specifically and assert it is 1.0000×, and assert that the *maximum* over the field is materially higher. This keeps a permanent, executable record of why a bound is never read off a landmark — the one point that looks most natural to measure is the one point that tells you nothing.

**V8 — tile independence.** A tile requested alone equals the same region of the whole grid, bitwise.

**V9 — shelled inside-ness.** Confirm a shelled disc reports positive at the centre and negative in the wall, so downstream consumers see the annulus rather than the disc.

Report every result as measured-number versus declared-number. Do not report a bound as verified on the strength of the code compiling.
