# T-0109 — the height stage: extrusion, bevel, Z offset

**Clause prefix `HS-`.** This document is the specification the implementation is built from and verified against. It is written after `REF-HEIGHT-MATHS.md` (the reference maths, transcribed and re-derived) and `CONTRACT-EXTRACT.md` (what the five signed-off contracts bind), both in this folder, and it does not repeat either — it decides.

It carries the three decisions the task asked to be made rather than discovered: the treatment of the STEPPED profile (HS-5), the parameterisation of a SIDE WALL (HS-6), and the shape of the Z OFFSET (HS-7). Each is stated as a ruling with its reasoning and its consequences, not as an option list.

---

## Part 0 — the three rulings, and the finding that reframes the first one

**The finding, first, because it changes what the first ruling has to be.** The task's requirement reads "each extrusion profile and each bevel declares how steeply it can change, and that declaration COMPOSES with the shape's own bound", with STEPPED named as the one exception that has no such constant. Measurement (`REF-HEIGHT-MATHS.md` §C) says the exception is not one technique, it is most of them: `round` and `dome` are formally unbounded at their DEFAULT parameter `curve = 1`, and four of the five non-identity bevels (`rounded`, `cove`, `ogee`, `stepped`) are unbounded too — `rounded` at the silhouette, `cove` at the inner edge of the band, `ogee` at its mid-band inflection. Of the twelve techniques, five have a finite slope bound (`flat` 0, `linear` 0 in `t`, `pyramid` ≤ 1, `taper` ≤ 20, bevel `linear` = 1/a) and seven do not. The two figures the task quotes as maxima, `round 16.4` and `dome 23.1`, are one measurement grid's samples of a divergent function: their ratio is √2 to within 0.4%, which is exactly the small-`t` ratio of the two derivatives, so they rise without limit under refinement. A build agent hard-coding them ships a wrong constant.

So "declare a finite slope and march by it" is not a treatment that stepped alone fails. It is a treatment that fails for the majority of the authored set, and would have quietly excluded `dome` and `round` at their own defaults — the two profiles most likely to be authored.

**HS-0.1 — RULING ONE. Every technique declares its slope bound honestly, including `+∞` where that is the truth, and the general resolve does not consume it.** The declaration is built and composed exactly as the task requires (HS-4), because it is the mechanical gate that lets any future consumer needing Lipschitz continuity refuse a technique by asking rather than by knowing. But the ray march is re-founded on a property every technique does have and the task's framing did not consider: **the composed normalised height `G(t)` is MONOTONE NON-DECREASING in the inside-distance for all forty-two profile×bevel combinations** (HS-5.1). Monotonicity makes every horizontal cross-section of the solid a LEVEL SET OF THE SHAPE'S OWN FIELD, offset by a constant — and offsetting a signed distance field by a constant preserves its declared bound exactly, a result this codebase already relies on twice (`ShaperBound.Shell`, and BD's join at `BORDER-CONTRACT.md:108`). The marcher therefore steps on the shape's own finite, already-proven bound and never needs a profile's slope at all. **The constant that does not exist is not merely documented as absent; it is designed out of the load-bearing path.** Stepped needs no exclusion, no new dial, and no change in appearance, and `dome`/`round`/`cove`/`ogee`/`rounded` are rescued from an exclusion nobody had noticed they needed.

**HS-0.2 — RULING TWO. A side wall inherits the rim value it descends from: `edgeDistance ≡ 0` on every wall point, and its outward normal is the silhouette gradient with a zero Z component.** Reasoning and consequences in HS-6. The vertical position on the wall is not smuggled into `edgeDistance`; it is already published, exactly, as `height`.

**HS-0.3 — RULING THREE. A per-layer `zOffset`, in canvas pixels, added to an ordering base `layerIndex × layerSpacing`, both new authored fields, with no new buffer.** HS-7.

---

## Part 1 — the layer stated as an implicit solid

**HS-1.1 — the statement.** A Silhouette layer is the implicit solid

> `S = { (x, y, z) : d(x,y) ≤ 0 and base ≤ z ≤ base + body · G(t(x,y), nx, ny) }`

where `d` is the shape tree's published signed distance in canvas pixels (negative inside, `ShaperField.cs:9-10`), `t` is the normalised inside-distance of HS-1.2, `G` is the composed height profile of HS-2/HS-3, `body` is the layer's authored extrusion thickness in canvas pixels, and `base` is the layer's Z of HS-7. Every one of the seven profiles and six bevels is a function of that one number `t` — with the single, named exception of `linear`, which is a function of the normalised local coordinates instead (HS-2.3). **This is why the whole authoring model survives into a 3D field unchanged and why no primitive has to be re-expressed:** the solid is defined entirely in terms of quantities the shape engine already publishes, and turning it is a change to the ray, not to the shape.

**HS-1.2 — `t`, the normalised inside-distance.** `t = clamp01(−d / span)`, with

> `span = max(pixelSize, rootSigmaMin × min(localSupportHalfW, localSupportHalfH))` when `hasLocalSupport`, else `max(pixelSize, min(supportHalfW, supportHalfH))`.

Three deliberate departures from the reference (`REF-HEIGHT-MATHS.md` §0, `SHAPER:1339`), each for a stated reason. The reference's floor is `max(1, ·)` in grid cells; ours is `max(pixelSize, ·)` because our unit is the canvas pixel (LR-1.5) and `pixelSize` is the sample spacing, so the floor means "never smaller than one sample" in both. The reference measures `span` from the screen-space bounding box; ours uses the ROOT-LOCAL support box scaled by `rootSigmaMin`, which is the same choice and the same reason FC-1.5a made for the fill's normalisation — the canvas box grows and shrinks as the node rotates, so a `span` taken from it would make a dome breathe once per quarter turn with nothing authored changing. And `span` is a compile-time scalar, read off `ShaperProgram` once, never recomputed per sample.

**HS-1.3 — units and frame.** `height` is in canvas pixels, canvas frame, +X right, +Y up, +Z out of the screen toward the viewer, at every nesting depth, no per-layer rescale — LR-1.5 verbatim, and the reason `dh/dd` is dimensionless. `body = max(0, depth)` in canvas pixels; `depth` is THICKNESS and is never negative (HS-7.4 is where the naming trap is recorded).

**HS-1.4 — what publishing height changes about the quantity set.** `ShaperQuantitySet.ShippedShapeEngine` becomes `Coverage | EdgeDistance | Height` for a node carrying a height stage, and stays `Coverage | EdgeDistance` for one that does not. `Depth` (BC-3.3 #7, ray thickness) becomes real only through the resolve of HS-9 and is not published by the height stage directly. `Heat`, `Density`, `Soot`, `Age` stay unpublished and are out of scope.

---

## Part 2 — the extrusion profiles

**HS-2.1 — the set is the reference's seven, ported by value, with the enum append-only and serialized as an int:** `Flat = 0, Linear = 1, Stepped = 2, Dome = 3, Round = 4, Taper = 5, Pyramid = 6`. Order follows `EXTRUSION_TECHNIQUES` at `project_document.py:28` so a future importer maps by index.

**HS-2.2 — the formulas.** `E(t)` is the height as a multiple of `body`. Transcribed from `index.html:1143-1155`; parameter ranges from `project_document.py:251-254`.

| technique | `E` | params (range, default) |
|---|---|---|
| `Flat` | `1` | — |
| `Linear` | `max(0, 1 + 0.6·(cos θ · nx − sin θ · ny))`, `θ = angle·π/180` | `angle` [−180,180], 45 |
| `Stepped` | `min(1, ⌊t·n⌋ / (n−1))`, `n = max(2, round(steps))` | `steps` [2,32] int, 4 |
| `Dome` | `(t(2−t))^(1/(2c))`, `c = max(0.2, curve)` | `curve` [0.2,4], 1 |
| `Round` | `t^(0.5/c)`, `c = max(0.2, curve)` | `curve` [0.2,4], 1 |
| `Taper` | `min(1, t / max(0.05, 0.6τ))`, `τ = clamp01(taper)` | `taper` [0,1], 1 |
| `Pyramid` | `1 − τ + τ·t` | `taper` [0,1], 1 |

`Dome` is written as `(t(2−t))^(1/(2c))` rather than the reference's `pow(sqrt(max(0, 1−(1−t)²)), 1/c)`: algebraically identical, one `sqrt` and one `pow` collapsed into one `pow`, and the `max(0,·)` guard becomes unnecessary because `t ∈ [0,1]` makes `t(2−t) ≥ 0` by construction. The `body ≤ 0` early-out at `index.html:1144` is kept — a zero-thickness layer publishes height 0 and no wall, and HS-9 skips it entirely.

**HS-2.3 — `Linear` is the named exception and it is not a height profile in `t` at all.** It reads `nx = clamp(lx/halfW, −1, 1)`, `ny = clamp(ly/halfH, −1, 1)` — the sample's position in the node's own local frame normalised by the LOCAL support half-extents (`ShaperProgram.localSupportHalfW/HalfH`), not by `span` — and does not read `t`. It tilts a constant-thickness slab: a plane in `(nx, ny)` of gradient magnitude exactly 0.6 for every angle. Three consequences, all of which the implementation must carry rather than discover. Its slope in `t` is exactly 0, so it composes with the shape's bound trivially and contributes nothing to it. Its `sup E = 1 + 0.6(|cos θ| + |sin θ|) ≤ 1 + 0.6√2 = 1.84853`, which is greater than 1 — so **`body` is not a height budget for `Linear`** and any code that assumes `height ≤ body` is wrong on exactly this one technique. And it is monotone in `t` only trivially (constant), which HS-5.1 relies on.

---

## Part 3 — the bevels

**HS-3.1 — six authored entries, five functions plus the identity:** `None = 0, Linear = 1, Rounded = 2, Cove = 3, Ogee = 4, Stepped = 5`, matching `BEVEL_TECHNIQUES` at `project_document.py:29`. `None` is a real serialized value and the layer default; it is not dropped to make the count five.

**HS-3.2 — the band.** `a = clamp01(amount)`; the band is `t ∈ [0, a)`, `a·span` canvas pixels wide measured inward from the silhouette; `u = clamp01(t/a)`. Outside the band the factor is exactly 1 by early-out. `amount` [0,1] default 0.25. Note and keep: because `a` scales `span`, the band is a constant fraction of the SHORTER half-extent, so on an elongated silhouette it is not a constant-width rim — that is the reference's behaviour and is ported deliberately, not fixed.

| technique | `B(u)` | params |
|---|---|---|
| `None` | `1` | — |
| `Linear` | `u` | `amount` |
| `Rounded` | `√(u(2−u))` | `amount` |
| `Cove` | `1 − √(1−u²)` | `amount` |
| `Ogee` | `½(1−√(1−4u²))` for `u<½`; `½+½√(1−4(1−u)²)` for `u≥½` | `amount` |
| `Stepped` | `min(1, (⌊u·m⌋+1)/m)`, `m = max(2, round(steps))` | `amount`, `steps` [2,16] int, 3 |

**HS-3.3 — the composed height is `G(t) = E · B(clamp01(t/a))`**, exactly the reference's `index.html:1424`. All five non-identity bevels reach 1 at the band's inner edge, so there is no seam at `t = a`. `Stepped` is the only bevel that does not reach 0 at the silhouette: `B(0) = 1/m`, a hard step of `1/m` at the outline itself, which is what leaves it the only bevel that does not remove the side wall (HS-6.4).

**HS-3.4 — the two `Stepped` are different functions and must not share code.** Different formula, different default step count (4 vs 3), different authored range ([2,32] vs [2,16]), and `E_stepped(0) = 0` while `B_stepped(0) = 1/m`. Sharing one implementation is the obvious and wrong economy.

---

## Part 4 — the declared slope bound, and how it composes

**HS-4.1 — what is declared.** Each technique declares `SlopeBound`, the supremum of `|dE/dt|` (or `|dB/du|`) over the authored parameter range, as a `float` which MAY be `float.PositiveInfinity`. Declaring infinity is a first-class answer, not a failure to measure. The audit measures each finite declaration and, for each infinite one, demonstrates divergence under grid refinement rather than reporting a number.

| technique | declared `sup|dE/dt|` | where |
|---|---|---|
| `Flat` | 0 (exact) | — |
| `Linear` | 0 in `t`; `|∇_(nx,ny)E| = 0.6` exactly | see HS-4.4 |
| `Stepped` | `+∞` — discontinuous | at `t = k/n` |
| `Dome` | `2m/√(2m−1)·(1−1/(2m−1))^(m−1)` with `m = 1/(2c)` if `c ≤ ½`; `+∞` if `c > ½` | `t → 0⁺` |
| `Round` | `0.5/c` if `c ≤ ½`; `+∞` if `c > ½` | `t → 0⁺` |
| `Taper` | `1 / max(0.05, 0.6τ)`, range `[1.667, 20]` | everywhere on the ramp |
| `Pyramid` | `τ ≤ 1` (tight) | everywhere |

| bevel | declared `sup|dB/du|` | where |
|---|---|---|
| `None` | 0 | — |
| `Linear` | 1 (tight) | everywhere |
| `Rounded` | `+∞` | `u → 0⁺` |
| `Cove` | `+∞` | `u → 1⁻` |
| `Ogee` | `+∞` | `u → ½` |
| `Stepped` | `+∞` — discontinuous | at `u = k/m` |

Both defaults (`curve = 1`) put `Dome` and `Round` in the infinite branch. That is the correct answer and the audit asserts it.

**HS-4.2 — the composition rule is a PRODUCT rule, not a `max` rule.** A height profile is not a primitive, not a transform and not a combine node, so none of `ShaperBound.Transform/Combine/Sweep/Shell` applies (they all compose bounds on the same quantity; this composes two different functions multiplied together). From `G = E·B(t/a)`, on the band `t < a`:

> `sup|dG/dt| ≤ L_E · sup|B| + sup|E| · L_B / a = L_E + sup|E| · L_B / a`

and off the band simply `L_E`. `sup|B| = 1` for every bevel; `sup|E| = 1` for every profile except `Linear` (HS-2.3). The extruded solid's declared slope in CANVAS units is then

> **`sup|dh/d(canvas pixel)| ≤ (body / span) · sup|dG/dt|`**

and it is this composed figure, never a primitive's, that any consumer requiring a Lipschitz height must read. Infinity absorbs: any infinite term makes the composition infinite.

**HS-4.3 — what consumes it, honestly.** Nothing on the rendering path. Its purpose is to make refusal mechanical: a consumer that genuinely requires a finite Lipschitz height asks `IsLipschitz` and is refused by name for seven of the twelve techniques, instead of silently producing holes. The general resolve of HS-9 is explicitly NOT such a consumer — HS-5 is why. Recording this plainly is the point: the task's instruction "do not leave a build agent hunting for a constant that does not exist" is discharged by making the absent constant unnecessary AND by making its absence a value a caller can test.

**HS-4.4 — `Linear`'s bound is in a different space and must not be summed with the others.** Its slope lives in the local coordinates: `∂h/∂lx = body·0.6·cos θ / localSupportHalfW`, `∂h/∂ly = −body·0.6·sin θ / localSupportHalfH`. It is declared separately as a 2-vector, not folded into the `t`-space figure, and the two are never added.

---

## Part 5 — RULING ONE, in full: the monotone inverse replaces the slope

**HS-5.1 — the property, and it is a claim the audit must prove.** `G(t) = E(t)·B(clamp01(t/a))` is **monotone non-decreasing in `t` on `[0,1]` for all forty-two profile×bevel combinations.** Proof: every `E` is non-decreasing in `t` (`Flat` constant; `Linear` constant in `t`; `Stepped`, `Dome`, `Round`, `Taper`, `Pyramid` each non-decreasing by inspection of HS-2.2), every `B` is non-decreasing in `u` and `u` is non-decreasing in `t`, and both are non-negative — a product of two non-negative non-decreasing functions is non-decreasing. The audit verifies it numerically over all 42 combinations across the full authored parameter ranges, and the claim is load-bearing: if it ever fails, HS-5.2 is unsound and the marcher must fall back to HS-5.5.

**HS-5.2 — the consequence: every horizontal cross-section of the solid is an offset level set of the shape's own field.** At normalised height `ζ = (z − base)/body`, the solid's cross-section is `{ p : G(t(p)) ≥ ζ } = { p : t(p) ≥ τ } = { p : d(p) ≤ −τ·span }` where `τ = Ginv(ζ)` and

> `Ginv(ζ) = inf{ t ∈ [0,1] : G(t) ≥ ζ }`, with `Ginv(ζ) = 0` for `ζ ≤ G(0)` and "no cross-section" for `ζ > sup G`.

`d(p) + τ·span` is therefore a conservative signed distance to that cross-section carrying **the shape's own already-proven declared bound, unchanged** — because adding a constant to a signed distance field does not change its gradient, the same result `ShaperBound.Shell` and `BORDER-CONTRACT.md:108` already rest on. No profile slope appears anywhere in that expression.

**HS-5.3 — the march, stated once.** Partition the ray's traversal of the layer's Z extent `[base, base + body·sup G]` into SLABS. Within a slab, let `τ_min` be `Ginv` of the slab's LOWEST `ζ`. Because `G` is monotone the cross-sections are nested and shrink upward, so the whole solid within that slab is contained in the single prism `{ d ≤ −τ_min·span }` — a fixed set for the whole slab, independent of how `z` varies inside it. A safe step is then

> `step = max( (d(p) + τ_min·span) / B_shape, (base_slab − z)·s, (z − top_slab)·s )`

with `s` the appropriate ray-direction factor for the two clipping planes (exact, bound 1). This is conservative over the entire slab by construction, and it uses only `B_shape`.

**HS-5.4 — the surface is located by bisection on an EXACT predicate, not by the march.** Membership is exact and O(1): evaluate `d`, form `t`, evaluate `G(t)`, compare with `ζ`. The march's only job is to skip empty space without overshooting; once a step lands inside, bisect between the last outside point and the first inside point to the configured tolerance. **This is what makes the whole scheme robust to a conservative `τ_min`:** an over-large prism can only make the marcher stop early, never late, and the bisection then finds the true surface. Accuracy is therefore independent of the slab count; the slab count is a SPEED dial, not a correctness one.

**HS-5.5 — the slab boundaries, per technique.** A technique declares a breakpoint set in `ζ` alongside its inverse. For `Stepped` extrusion the breakpoints are the tread heights `{ k/(n−1) }`, and inside a tread `G` is EXACTLY constant, so the containing prism is exact and the march is exact with zero conservatism — the stepped case is the BEST case, not the excluded one. For `Stepped` bevel likewise at `{ (k+1)/m }`. For `Flat` there is one slab and the solid is a plain prism. For the smooth profiles the breakpoints are a uniform subdivision of `[0, sup G]` into `slabQuality` slabs (default 16, authored nowhere — a constant on the resolve, not a dial on a layer), and correctness holds for any count ≥ 1. When both profile and bevel are stepped the breakpoint sets are merged, up to `31 + 15 = 46` slabs, which is the case to size for.

**HS-5.6 — what this ruling costs and what it gives up.** It gives up nothing in appearance: no new dial, no changed default, no asset drift, `Stepped` renders exactly as authored. It costs one extra declared function per technique (`Inverse`) plus a breakpoint list, and an `O(slabs)` outer loop in the marcher. Against that it removes an exclusion that would otherwise have had to cover `Stepped`, `Round`, `Dome`, `Rounded`, `Cove` and `Ogee` — six of the twelve techniques, including both `Round` and `Dome` at their own default parameter. **The alternative treatments named in the task are therefore both declined, with reasons**: the slope-clamped variant (`REF-HEIGHT-MATHS.md` §D.1) adds an authored dial and changes how existing stepped content looks, and would still leave `Round`/`Dome`/`Rounded`/`Cove`/`Ogee` unbounded and unaddressed; the written-down exclusion (§D.3) would have to be six times wider than the task anticipated and would exclude the default look of two profiles. Slab marching (§D.2) is adopted and generalised from "the stepped special case" to "the mechanism for every technique".

**HS-5.7 — `Ginv` per technique.** Closed form wherever one exists; a monotone bisection on `G` (≤ 40 iterations, no allocation) is the declared fallback and is the implementation for `Ogee`, for any profile×bevel product inside the band, and for `Linear` in combination. Closed forms: `Flat` → 0 for `ζ ≤ 1`; `Stepped` → `⌈ζ(n−1)⌉/n`; `Dome` → `1 − √(1 − ζ^(2c))`; `Round` → `ζ^(2c)`; `Taper` → `ζ·max(0.05, 0.6τ)`; `Pyramid` → `(ζ − 1 + τ)/τ`. Bevel inverses on the band: `Linear` → `a·ζ`; `Rounded` → `a(1 − √(1−ζ²))`; `Cove` → `a√(1 − (1−ζ)²)`; `Stepped` → `a·⌈ζm − 1⌉/m`. The product `E·B` generally has no closed inverse, so the shipped path bisects whenever `bevel ≠ None` and `ζ < E(a)·1`; the closed forms are used above the band, where `B ≡ 1` and `Ginv = E⁻¹`. **Every closed form is verified against the bisection by round-trip in the audit** — that is the check that catches an algebra slip, and it is cheap.

---

## Part 6 — RULING TWO, in full: the side wall

**HS-6.1 — the ruling.** A side wall inherits the rim value it descends from. Concretely: **on every wall point, `edgeDistance = 0` and `t = 0`.** The wall is the set of surface points at the silhouette boundary between `z = base` and `z = base + body·G(0)`; every one of them descends vertically from the rim point directly above it, whose inside-distance is exactly zero. The value is not invented, it is read from the point it descends from.

**HS-6.2 — the wall's normal, which LR-9.3 requires be answered at the same time and which no contract had connected to the question.** The wall's outward normal is the silhouette's own outward gradient with a zero Z component:

> `n_wall = normalize( (∂d/∂x, ∂d/∂y, 0) )`

taken from the shape's analytic field by central difference of `ShaperEvaluator.Distance`, which is a pure function of a canvas point and therefore a legal (non-neighbour-reading) derivative under LR-3.2 — the same licence the light rig already takes. `nz = 0` exactly, and the two consequences LR-9.3 predicts are correct behaviour rather than defects and are stated in the tool, not hidden: `rim` saturates to `rimStrength` on a wall, and the diffuse term differs in sign from the top face for a light above the plane. A vertical wall genuinely is lit differently from a horizontal cap; the alternative would be to lie about the geometry.

**HS-6.3 — why this rule and not the other two.** The competing rule "the wall carries its own top-to-bottom parameterisation" makes `edgeDistance` mean two different things on two surfaces of one solid — a near-duplicate of exactly the kind the whole rebuild exists to remove — and the information it wants is already published, exactly and in the right units, as `height`. So it is this rule plus a redundant channel. The third rule, "the wall is a separately paintable surface", is an authoring feature (a second fill slot on a node) rather than a parameterisation: it does not answer what `t` is on a wall, it only says who chooses the colour. It can be added later ON TOP of this ruling without undoing anything, whereas the second rule could not be undone. This ruling is also the only one of the three under which `edgeDistance` is CONTINUOUS across the silhouette edge — the top surface's `t → 0` as the rim is approached, and the wall's is identically 0 — which matters because a discontinuity there would show as a hard seam in every `ByEdgeDistance` gradient at precisely the most visible line in the picture.

**HS-6.4 — the consequence that makes this cheap, and it is checkable.** A wall exists only where `G(0) > 0`. At their default parameters `Dome`, `Round`, `Taper` and `Stepped` all have `E(0) = 0`, and `Pyramid` has `E(0) = 1 − τ = 0` at `τ = 1` — so **five of the seven profiles have no side wall at all by default**, the solid closing smoothly to the base plane at the rim. Only `Flat` and `Linear` have full-height walls. Further: any bevel other than `None` and `Stepped` forces `B(0) = 0` and therefore removes the wall entirely from every profile. `Stepped` bevel leaves a wall of exactly `body·E(0)/m`. These are five mechanical statements the audit asserts directly, and together they are why this ruling can be made now and its rendering deferred: for most authored content there is nothing to render.

**HS-6.5 — what is ruled and what is deferred.** The RULE is decided now and is binding on T-0110 and on every later fill and border. Rendering a wall is not in this task: v1 resolves straight down (HS-9.2), and a wall is exactly parallel to that ray, so it has zero screen area and cannot appear. The tilted conformance render of HS-9.5 is the one place a wall is visible, and it is rendered there under this rule — which is a second reason that render is worth its cost: it is the only thing in Wave 2 that exercises the ruling at all.

**HS-6.6 — the sibling question BC-3.5 bundles with this one is answered too.** For a ray crossing several disjoint spans of one layer (which a subtractive combine or B12's hollow-out makes ordinary), `depth` is the **sum of the spans**, and the resolve reports every span separately so a consumer wanting "first span" or "first-entry-to-last-exit" can compute either from the crossing list without a second query. Sum, because `depth` is defined as "the thickness of solid the sampling ray traverses" (BC-3.5) and a ray through a hollow shell traverses two walls' worth of solid and no air; reporting the outer envelope would report solid where there is none.

---

## Part 7 — RULING THREE, in full: the Z offset

**HS-7.1 — the fields.** `ShaperLayer` gains `public ZUIValue zOffset = new ZUIValue(0f)` — canvas pixels, signed, sampled once per compile through `ShaperValue.Sample` on the DOCUMENT's phase like every other layer-level dial. `ShaperDocument` gains `public float layerSpacing = 0.75f` — canvas pixels between consecutive layers' base planes, the reference's `order*0.75` ported by value (`index.html:1362`) with its unit reinterpreted from grid cells to canvas pixels per LR-1.5.

**HS-7.2 — the ordering base.** `base(i) = i × layerSpacing + zOffset(i)`, computed once per layer per compile, `i` being the layer's index in `ShaperDocument.layers` (bottom-most = 0, and no stage may reorder that list). This is the layer's `base` in HS-1.1 and it is the ONLY Z contributor — exactly the property that makes the change small.

**HS-7.3 — no new buffer, and why that is literally true here.** The height field is the depth buffer: a layer's surface Z at a sample IS `base + body·G`, so ordering, occlusion and the `depth` quantity all read from the one number. Adding a constant to `base` moves the whole layer in Z with nothing else to update. **Two traps from the reference are deliberately NOT inherited.** The reference's composite height buffer uses `−9999` as its empty-cell sentinel, tested against `−900`; with `base = order·0.75 ≥ 0` that threshold was unreachable, but a signed `zOffset` reaches it, and a layer pushed far enough back would vanish rather than go behind. Shaper carries occupancy in `coverage`, which already exists, so no sentinel is introduced and no range restriction on `zOffset` is needed. The reference also fuses layers within `FUSION_CELLS = 1.7` cells of each other, which silently welds any offset under ~1.7 cells into the neighbour instead of separating it; Shaper has no fusion band, so a small `zOffset` does what it says. Both are recorded because a build agent testing a new Z dial against the reference's behaviour would otherwise conclude the dial "doesn't do anything".

**HS-7.4 — the naming trap, recorded once.** In the direct reference (3D Shaper) the dial labelled "Depth" is extrusion THICKNESS, non-negative, `[0, 2048]`. In its ANCESTOR the slider labelled "Depth" is the opposite — Z position, `L.z`, range −8..32. Shaper names them `depth` (thickness, never negative) and `zOffset` (position, signed) and never uses the word "depth" for position. A porting agent reading the ancestor will get this backwards.

---

## Part 8 — the normal provider's `Profile` case

**HS-8.1 — `ShaperNormalKind.Profile = 1`** is implemented as a case in `ShaperNormals.FillTile`, not as a second entry point — the exact shape LR-3.1 reserved it in for. It reads the `distance` sheet and the compiled height op; it does NOT read the `heightSheet`, and it does not read any screen-space neighbour, so LR-3.3's prohibition is satisfied structurally rather than by promise.

**HS-8.2 — analytically, as Part 10 requires.** The surface gradient is the chain rule, never a difference of neighbouring samples:

> `∇h = body · G′(t) · ∇t = body · G′(t) · (−∇d / span)`

with `∇d` from a central difference of `ShaperEvaluator.Distance` at the sample point (legal: a pure function of a point, LR-3.2's own licence) and `G′` supplied ANALYTICALLY by each technique. `G′ = E′·B + E·B′/a` on the band, `E′` off it.

**HS-8.3 — the three dials, wired not invented.** `slopeGain = 0.65`, `normalZBase = 1.4`, `reflectionFlatten = 1.5` already exist named on `ShaperNormalOp` with their provenance (`ShaperNormals.cs:53-60`), unused. The normal is `normalize( (−slopeGain·∂h/∂x, −slopeGain·∂h/∂y, normalZBase) )`, matching `index.html:1491-1492`. `reflectionFlatten` enters as the reference's `normalZBase + 1.5·reflection` where a reflection term exists; with no reflection dial in Wave 2 it stays declared and at its default, and the audit asserts that it is read, so it cannot rot into a dead field.

**HS-8.4 — where `G′` is infinite.** Seven of the twelve techniques have unbounded `G′` somewhere (HS-4.1). At such a point the analytic normal is exactly vertical-walled and `normalize` handles it: the tangent components dominate, `nz → 0`, and the result is unit and finite. The implementation must NOT clamp `G′` to a magic ceiling; it must let `normalize` do the work, and must guard only the exactly-degenerate case (`∇d = 0`, at a local extremum of the field) by falling back to `(0,0,1)` per LR-3.5's "never leaves a sample unwritten and never writes zero". At a `Stepped` discontinuity `G′` is 0 almost everywhere and the riser is a wall of zero screen width straight down — correct, and consistent with HS-6.2's wall normal in the limit.

---

## Part 9 — the general resolve and the tilted conformance frame

**HS-9.1 — the general query, BC-2.2, and the API is general from day one.** `ShaperResolve.Query(scene, rayOrigin, rayDirection, crossings)` reports **every layer the ray passes through** and, for each surface it crosses, the depth at the crossing and the local coordinates of the crossing point on that layer. Every layer, not the front-most — that is what makes an occluded-outline pass fall out for free later instead of needing a second sweep. BC-2.3 binds the SHAPE of this call: a caller must not be able to tell from what it calls that only one direction is fast.

**HS-9.2 — two implementations behind that one API, chosen by the ray, not by the caller.** When the direction is the canvas `−Z` axis to within tolerance, the closed form runs: `t` is fixed for the sample, `G(t)` is evaluated once, the crossing is `base + body·G(t)` directly — algebraically the same point sample the tool performs today, at the same cost, no march. Otherwise the slab march of HS-5.3 runs. The production path in Wave 2 only ever takes the first branch; the second exists, is exercised, and is the thing HS-9.5 proves.

**HS-9.3 — what a crossing carries.** Layer index; the ray parameter at the crossing; whether it is an entry or an exit; the surface kind (`Cap` — the top surface — or `Wall`, HS-6); the crossing point's local coordinates on that layer; and its `edgeDistance` and `height` at that point, so a downstream consumer never has to re-evaluate the field to find out what it just hit. `depth` per layer is the sum of its entry-to-exit spans (HS-6.6).

**HS-9.4 — what is deferred, explicitly.** No acceleration structure, no camera object, no perspective, no shadow rays (LR-4.5 already deferred those on exactly this ground), and no exposure of tilt in any UI. The march exists to be correct, not to be fast, and its cost is not this task's subject.

**HS-9.5 — the tilted conformance render, discharging BC-2.7 / BC-4.4.** One frame of an EXTRUDED, BEVELLED shape, deliberately tilted, rendered through `ShaperResolve.Query` on the general branch, kept as a committed artefact in this workspace, and re-rendered whenever the resolve changes. It is not a feature, no menu item is added, and it is exposed nowhere. Tilting the solid and tilting the ray are the same operation composed with a rotation of the whole scene, so the disambiguation the contracts left open does not change the code: the render tilts the layer and leaves the ray at `−Z`, which puts the ray OFF the layer's local `−Z` axis and therefore takes the general branch — that is the property being proved, and the conformance check asserts the general branch was taken rather than assuming it. The frame must show a visible side wall (HS-6.5) and must be compared against the straight-down closed form at zero tilt, which the two branches must agree on to tolerance — that agreement is the real conformance test and the picture is the human-readable half of it.

---

## Part 10 — what is deliberately not built

The three "bulge" dials T-0105 left for this task are **not** built here. They squash a shape's width along its height; they are a WARP of the silhouette, not a height profile, and folding them in would reintroduce the over-reporting defect T-0105 removed, since a warp changes the shape's distance field and must declare its own bound as a transform. They belong with the transform block. Named, not built, and not silently dropped.

No UI, no `EditorWindow`, no `[MenuItem]` — the standing Wave-2 rule.

No fill reads `height` yet; FC-2.5's `height_final = height_shape + heightDelta · coverageEff` becomes a real sum here because `height_shape` finally exists, and nothing else about the fill stage changes. LR-3.4's ruling that a fill's height delta does not reach the normal in Wave 2 is NOT reopened.

The indexed-strip fill's per-slot protrusion is T-0110's and is not built here; this task's job is to make the height channel it writes into real.

---

## Part 11 — T-0109 fix-pass amendments (2026-08-31)

**Read this before trusting any clause above.** An independent verification (`VERIFICATION.md`) found eight defects in the built stage; the fix pass that followed (`FIX-REPORT.md`, with per-defect before/after measurements) changed the code in ways that make specific clauses of this document wrong, and one that makes a withdrawn claim true again. They are recorded here rather than edited in place, so that the original wording and the correction are both visible.

**HS-2.2, the `Linear` row — WRONG AS WRITTEN.** The shipped formula is `E = 1 + 0.6(cos θ·nx + sin θ·ny)`, with a **plus** on the sine term. The spec states the reference app's `− sin θ·ny`, which is written in a Y-DOWN frame; Shaper is +Y up, so ported verbatim it made a given authored angle lean the vertical mirror of the reference's. T-0105's precedent — "port Pyre angles unchanged, flip reference-app ones" — was applied. Measured at angle 45: the peak moved from canvas `(+60, −40)` to `(+60, +40)`. See FIX-REPORT §F7. Do not port the sign back.

**HS-4.4 — WRONG AS WRITTEN, for the same reason.** The shipped declaration is `∂h/∂ly = +body·0.6·sin θ / localSupportHalfH`. `LinearGradient` measured `(0.28284, +0.42426)` at angle 45.

**HS-5.3 — AMENDED.** The clause's own step expression has every term negative once the point is inside the containing prism AND inside the slab, so it does not define a step there. The implementation used a fixed-resolution floor instead, and that floor lost whole features (16 of 32 measured configurations lost a crossing pair; `Depth` reported 400.000 where the true solid was 380.000, i.e. exactly the envelope answer HS-6.6 forbids). The floor is gone. The shipped step is a TWO-SIDED BRACKET — the containing prism `Ginv(ζ_lo)` at the most permissive `E` and the CONTAINED prism `Ginv(ζ_hi)` at the least permissive `E`, both taken over the ζ (and, for `Linear`, `E`) window the ray can reach within the candidate step — with ADAPTIVE SUBDIVISION of that window when the point falls in the ambiguous shell between them, and an absolute floor of `ShaperResolve.SurfaceResolution` = 0.02 canvas px. Depth cap and step-guard exhaustion are reported on `ShaperResolveResult.bracketCapped` / `.stepsExhausted`, never swallowed.

**HS-5.4 "an over-large prism can only make the marcher stop early, never late" — HOLDS.** Re-verified over 829 316 800 checks, 0 violations. The prism was never the defect; the step floor was.

**HS-5.4 "accuracy is independent of the slab count; the slab count is a SPEED dial, not a correctness one" — TRUE OF THE CODE AGAIN.** No expression in the march reads the slab count, `SlabQuality`, or the ray's clipped length to size a step.

**HS-5.5 "correctness holds for any count ≥ 1" — TRUE ONLY IN THIS QUALIFIED FORM, and it must be written this way.** ⚠ **OVERSTATED AS WRITTEN — see Part 12.** The guarantee the implementation supports is: *every step is either a proof — the point is provably air, or provably solid, for the whole of the step — or is at most `SurfaceResolution` long; so no feature whose extent ALONG THE RAY is at least `SurfaceResolution` can be missed.* That holds for any slab count ≥ 1. It is EXACT, not merely tolerance-bounded, wherever the containing and contained prisms coincide: every `Flat` or `Linear` layer without a smooth bevel, every horizontal ray, and every `Stepped` tread. It is NOT an unconditional correctness claim — a feature thinner than 0.02 canvas px along the ray can still be missed on a smooth profile — and must not be restated as one.

**HS-5.7 "every closed form is verified against the bisection by round-trip in the audit" — RESTORED.** ⚠ **ITS CLOSING "|Δt| 0.500 → 0.000" WAS FALSE OF THE CODE — see Part 12.** H4's ζ grid floored at 5e-3, below which the whole catastrophic-cancellation family lives, so the check was hollow for the class it exists to catch. The grid is now log-spaced to 1e-9 and carries an injection sub-check. On its first run with the new grid it found a live defect the spec's own formula did not have: `ProfileInverse(Stepped)`'s absolute `−1e-6` epsilon drove the ceiling to 0 for `0 < ζ < 1e-6/(n−1)`, returning `t = 0` — a whole tread short (|Δt| 0.500 → 0.000).

**HS-4.1 "infinite declarations are verified by demonstrating divergence under grid refinement" — AMENDED with a DECLARED BLIND BAND.** For `curve ∈ (0.5, ~0.6]` the declaration of `+∞` is mathematically correct and NOT demonstrable by refinement: the exponent `0.5/c − 1` is so near zero that `t` must reach ~1e-15000 for the quotient to reach 10. H3 now measures and prints the band, ungated. Do not "fix" the declaration to a finite number on the strength of a flat measurement there.

**HS-5.1 monotonicity — RESTATED WITH ITS FLOAT TOLERANCE.** The theorem holds in exact arithmetic. In `float` it holds exactly in Unity's own runtime (0 drops in 24 715 935 adjacent-float pairs) and to within ONE ULP (5.960e-8) under CoreCLR, where an independent harness measured 2 520 drops on the same source. The two runtimes round `u·(2−u)` differently, so the declaration covers both: **monotone as a theorem; monotone to within one float ulp in float.** One ulp is six orders below `SurfaceResolution`, so HS-5.2's containment argument survives either way.

**HS-6.6 `depth` = the sum of spans, never the envelope — RESTORED.** ⚠ **"RESTORED" WAS TRUE OF ONE FIXTURE ONLY — see Part 12.** `Depth` on the verifier's fixture: 400.000 → 380.000 against a true 380.000, and H6 now checks the sum against an independently scanned ground truth rather than only against the envelope.

**Part 10's FC-2.5 "becomes a real sum here" — DISCHARGED.** `ShaperLayer.height` exists (null by default), `ShaperLightCompiler.BindLayer` compiles it, `ShaperFillResolver.PaintTile` calls `ShaperHeight.FillTile` into a new `ShaperFillBuffers.ownHeight` sheet, `buf.height` is seeded from the layer root's slab, and `ShaperLightScene.pointZ` is `base + height` instead of being cleared to 0. Audit H11 asserts all four.

**HS-8.1/LR-3.5, the `Profile` normal provider — NOW ACTUALLY RUNS.** `CompileNormal` populates `op.height` and `PaintTile` passes `ow.shape`/`ow.stack`; the degenerate `(0,0,1)` fallback is COUNTED on `ShaperLightScene.normalDegenerate` instead of being written in silence.

**HS-9.5 — DISCHARGED for this change.** ⚠ **Superseded by Part 12: the resolve changed again, so this was re-rendered again.** Both artefacts were re-rendered in the same `RunAll` pass that produced the current `HEIGHT-AUDIT.txt`. The tilted frame asserts general branch 126 000 rays / straight-down 0, and shows 10 049 wall pixels.

**One question deliberately left to the owner and NOT decided here.** ⚠ **Still the owner's; Part 12 adds a picture of what it looks like.** `ShaperHeight.FillTile` writes height exactly 0 whenever `d > 0`, so a sample with `d ∈ (0, halfBand)` has partial `coverage` but zero height — a step discontinuity at `d = 0` while coverage ramps. This was latent while nothing read the sheet; it is live now that the fill does. Making it ramp would change what `height_shape` MEANS at the rim (does height follow coverage, or does it follow the solid?), which is a spec decision, not a bug fix.

---

## Part 12 — T-0109 SECOND fix-pass amendments (2026-08-31)

**Read this before trusting Part 11.** A second independent verification (`VERIFICATION-2.md`) confirmed most of Part 11 but found that **three of its own amendments overstate**, and found four further defects. The second fix pass (`FIX-REPORT-2.md`, per-defect before/after measurements) fixed all of them. Part 11's original wording is left intact above and corrected here, same convention.

### The three Part 11 clauses that were wrong

**HS-5.7's closing claim — WAS FALSE OF THE CODE, NOW TRUE.** Part 11 wrote "`|Δt| 0.500 → 0.000`". That was a property of H4's ζ *grid*, not of the code: the `−1e-6` **absolute** epsilon inside `ProfileInverse(Stepped)`'s ceiling survived F3, and at ζ one float ulp above a tread — which neither H4 grid lands on — the inverse was still a whole tread out (`|Δt|` 3.333e-1 at n = 3, 2.5e-1 at n = 4, 3.125e-2 at n = 32; `BevelInverseU(Stepped)` 5.000e-1 at m = 2). Both epsilons are **gone**, replaced by a verify-and-nudge against the forward function: ceil the step index, walk it DOWN while the step below already reaches ζ, and nudge the returned `t` UP by ulps (never by a tread) if it falls short. Re-measured at the same ζ: **`|Δt|` 0.000e+0 for both, reach shortfall 0.000e+0**, and `InverseLowerBound` containment at tread-adjacent ζ still 0 violations in 46 200 checks. The rewrite also fixed a defect no ζ epsilon could ever have reached: `k/(float)n` is not always a float that `⌊t·n⌋` maps back to `k` — exhaustively over the authored range there are four such indices (`n=22,k=13`; `n=23,k=7`; `n=23,k=14`; `n=29,k=15`) where the returned `t` sat one ulp below its own riser and `E(t)` came back a whole tread short, in the UNSAFE direction for the contained prism.

**HS-5.5's qualified guarantee — THE STEP-SIZING HALF WAS ALWAYS TRUE; THE CORRECTNESS HALF WAS NOT, AND NOW IS.** "Every step is either a proof … or is at most `SurfaceResolution` long" described the step-sizing rule accurately and the branch's correctness inaccurately, for three separate reasons, all now fixed:

- the per-step contained prism called raw `ShaperHeight.InverseAtE`, which is **not** an upper bound (13 024 violations in 14 430 139 checks, worst shortfall 3.3379e-6 in ζ), so its "proof" was not a proof. It now calls `ShaperHeight.InverseAtEUpperBound`, the verify-and-nudge form generalised to a pinned `E`: **0 violations on the same sweep**;
- `ShaperResolve.ClipSlab`'s parallel branch was inclusive at BOTH ends, so a ray with `|dz| < 1e-9` sitting exactly on an interior slab breakpoint was marched by two slabs and every flip emitted twice. The interval is now half-open `[lo, hi)` for every slab except the last, whose top stays closed so the outer end of the Z extent is not lost instead;
- `Member` read HS-1.1's closed set literally, so where `G(t) = 0` — a whole BAND for a `Stepped` profile or bevel, not a point — the layer had a zero-thickness membrane and the march emitted crossing pairs of width exactly zero. See HS-1.1 below.

**HS-6.6 "RESTORED" — WAS TRUE OF ONE FIXTURE, NOW TRUE GENERALLY.** Two further ways `Depth` was wrong, both worse in `Depth`'s own units than the defect F1 removed:

- a breakpoint-parallel ray reported **`Depth` 0.000 against a true 399.609** (530 of 552 rays, 40 of 42 combinations) because of the `ClipSlab` duplication above. After: **1 of 552 rays differs, worst `|Depth| error` 0.001 canvas px**, and that one is a 0.001 px air gap at a `Stepped` riser — below the march's declared 0.019 px feature resolution, not a defect;
- a TRUNCATED crossing list **over-reported solid by up to 83.138 px** (11 cases of 242; 200 non-prefix; 144 mis-stamped parity flags), because `QueryGeneral` iterated slabs in ζ order while `Emit` drops on overflow — so a descending ray's retained list was a SUFFIX stamped as a prefix. The slab loop now runs in **ray order** (reversed when `dz < 0`), which makes a truncated list a genuine prefix and the parity stamped forward from `insideAtStart` correct for every element kept. After: **0 non-prefix, 0 mis-stamped, 0 over-reporting cases**, and `Depth` is again monotone non-increasing as the buffer cap shrinks.

### The new rulings

**HS-1.1 — AMENDED. Where the height is zero there is NO SOLID.** The set is `{ 0 ≤ z − base ≤ body·G(t), and G(t) > 0 }`. The `G > 0` conjunct is new and is not a tolerance: read without it, the set contains a two-dimensional zero-measure skirt on the base plane wherever `G` vanishes over a band, which `Stepped` does over the ring `t < 1/n` and a `Stepped` bevel does at the rim. This is the same sentence HS-2.2 already applies to a whole layer ("a zero-thickness layer publishes height 0 and no wall … there is no solid to cross"), applied pointwise, which is where it was always true. Both branches of the resolve carry it (`Member` and `QueryStraightDown`), so BC-2.3's two branches still agree. It changes no answer anywhere `G > 0`, i.e. nowhere any surface is rendered — measured: **0 omissions in H6's 504-ray fixture against a truth scan still carrying the OLD convention**, worst gap 1.5259e-5 px, and 0 count mismatches over a 126-ray 42-combination tilted sweep that previously showed six.

**HS-4.4 / `linearEGrad` — AMENDED. The baked figure is now the MAX over `LocalNormalised`'s clamp regimes, not the unclamped Jacobian.** Part 11 and the compiler both asserted that "the clamp inside `LocalNormalised` can only reduce the variation". That is false: `nx` and `ny` are clamped INDEPENDENTLY, so where one axis is pinned its term leaves the vector sum, and where the two terms partially cancel a surviving single term is LARGER than the sum. Measured over 1650 configurations: the true `sup|∇E|` exceeded the baked value in **385**, worst ratio **3.6235×**. The bake is now `max(|a+b|, |a|, |b|)` over the two axis terms, which is the true supremum over the whole plane. After: **0 of 1650**, worst ratio 1.0000.

**What the correct bound costs.** 1216 of the 1650 configurations pay nothing at all — the max IS the old value whenever the sum dominates. Mean widening **1.0858×**, worst **3.6235×**. A wider `E` window only makes the march take shorter, still-provable steps, so the cost is speed on `Linear` and never correctness. No march failure was ever produced from the old bound (5 184 targeted rays, 0 omissions), so this is a soundness repair with no observed behavioural change — which is exactly the case T-0105 exists to insist on anyway.

**H6's "extra emitted pairs VERIFIED real" arm — AMENDED, and this is the one to remember.** As written it certified the very defect it was built to catch: a pair of ZERO width has its midpoint equal to both endpoints, so `TruthInside(mid)` asked whether the boundary point was inside, and under the closed convention `TruthInside` shared, the answer was unconditionally yes. All 42 phantom base-plane pairs were counted as evidence of correctness, under a printed line calling them "slivers finer than the scan's spacing". They were exactly zero-width. A degenerate pair is now counted separately, **fails** the check, and a pair only reaches the predicate when its midpoint is strictly interior in float — which removes the tautology independently of the convention. After: `extra DEGENERATE 0`, `extra VERIFIED 0`, `extra SPURIOUS 0`.

**H11 statement 2 — SPLIT, because it proved the seed and not the sum.** Its document authors no fill, so `heightDelta ≡ 0` and `height == ownHeight` held for a reason unrelated to FC-2.5 being a sum — it would hold identically if the `+=` were an assignment. It is now 2a (the old, honest, seed-only statement) plus **2b**, on a document authoring `heightDelta = 3`: 656/1920 samples with BOTH terms non-zero, 1920/1920 residuals a legal `coverageEff` in [0,1], and `max height_final` rising by exactly **3.0000**.

**HS-9.5 — DISCHARGED AGAIN.** N1/N2/N3 change the resolve, so both artefacts were re-rendered (2026-08-31 15:47, same `RunAll` pass that produced the current `HEIGHT-AUDIT.txt`). The tilted frame still asserts **general 126 000 rays, straight-down 0**, and still shows **10 049 wall pixels** of 43 025 surface pixels. The counts are unchanged, which is the expected outcome and not a sign the render was skipped: N1 needs a Z-parallel ray, N2 needs a truncated buffer, and N3 removes pairs of zero screen area — this frame contains none of the three.

**The rim question is STILL THE OWNER'S, and now has a picture.** `rim-step.png` is a 10× crop of the same rim of three cells of the re-rendered contact sheet: `27 depth 0` (no height stage at all — the control), `01 Flat/None` (the live full-`body` cut), and `17 Flat+Rounded` (a ramped rim). The rig behind the sheet includes a POINT lamp at `(26,−20,30)` range 70, so `pointZ = base + height` is genuinely read by the shading and the cut IS exercised. Result: the last covered pixel reads **0.588 luminance in all three**, bit-identical, and the largest luminance difference between the control and the live cut across sixteen rim pixels is **0.0760** — a plateau-brightness difference, not a rim artefact. **The full-`body` cut is not visible in either committed artefact.** That does not make it right; it means the decision is not urgent, and the third panel shows what changing it would look like.

### What Part 12 does NOT fix

**`zTol = max(1e-3, 1e-3·body)` in `EmitAt` is still underived.** It is dimensionally sound (both operands are canvas pixels and it is already relative to `body`), so the epsilon sweep below does not condemn it, but nobody has derived the constant from the bisection residual it is presumably absorbing, and no fixture puts a cap within `zTol` of its own base. Third time this has been recorded as a gap rather than a pass.

**The other absolute epsilons were swept and are judged CORRECT AS ABSOLUTE.** `Insert`'s `1e-7` dedup tolerance is on ζ, whose range is pinned to `[0, supG ≤ 1.8485]`, so 1e-7 is at most ~1.7 ulps — a pinned scale, not a scale-free quantity. `StraightDownTolerance` 1e-5, `|dz| < 1e-9`, `dxy > 1e-9` and `len > 1e-12` are all on components of a NORMALISED direction, likewise pinned to 1. `span`'s `1e-4` floor and `invLocalHalfW/H`'s `1e-5` guard are in canvas/local units and are degenerate-geometry guards, not tolerances on a comparison. None of these is the class N6 was about.

**The march's SPEED was not re-measured against a pre-fix baseline.** Two changes push it: `InverseAtEUpperBound` per step (which early-outs without bisecting on 99.91% of calls — 13 024 of 14 430 139 needed the nudge) and the wider `linearEGrad` (`Linear` only). Absolute figure for whoever wants a baseline: the tilted conformance render, 126 000 general-branch rays, is **2 608 ms** on this machine as shipped.

---

## Part 13 — T-0109 THIRD fix-pass amendments (2026-08-31)

**Read this before trusting Part 12.** A third independent verification (`PASS3-FINDINGS.md`) confirmed the whole of the second fix round — N1–N7 all hold, and it WITHDREW its predecessor's N5 finding after re-measuring `linearEGrad` with the correct instrument (point pairs, worst needed/baked ratio 1.000456) — and found three further defects plus one failure of the audit itself. All four are fixed. `FIX-REPORT-3.md` carries the per-defect before/after measurements. Same convention as Parts 11 and 12: earlier wording is left intact and corrected here.

### The three new rulings

**HS-1.1's strict reading has a consequence in the MARCH that Part 12 did not draw, and it cost 24 px of solid.** The contained-prism SOLID skip proves solid with a non-strict `G(t) ≥ ζ`, while `Member` applies HS-1.1's amended strict reading. Those two are allowed to disagree at a slab boundary — the prism says SOLID at the very point `Member` calls AIR — and the pre-fix march took the skip there, jumping from an air sample to another air sample straight across a solid span: `mPrev` never flipped, nothing was emitted, and the span vanished. Measured: **26 of 7440 rays lost a true crossing, worst gap 26.207 px**, worst solid-length under-report 24.1747 px; a 400×400 plate at Stepped n = 8 lost `[552.3819, 576.5567]` entirely. **The ruling: a prism skip is entitled to jump, and is NOT entitled to jump while believing it is outside.** Both skips — the slab-wide `gIn` branch and the per-step `gapIn` branch — are now gated on `mPrev`, and where the prism and `Member` disagree the march declines the skip and falls through to the bounded step, so the next sample lands inside and the entry is emitted by the ordinary flip path. After: **0 lost in 3240 true transitions over the 13 affected step counts**, and the minimal repro returns all four crossings to within 0.00043 px.

**HS-5.2's containment held everywhere Part 12 measured and failed at four floats it did not.** `SteppedProfileInverseExact`'s DOWN walk asked `SteppedE` at the raw `(k−1)/n` — precisely the float that needed the UP nudge Part 12 introduced — so at the four indices where `⌊fl(k/n)·n⌋ < k` (`n=22 k=13`, `n=23 k=7`, `n=23 k=14`, `n=29 k=15`) the returned `t` sat one ulp below its own riser and the containing prism over-reported by **one whole tread**, `3.4481e-2` t-units: **12 violations in 520 800 checks**, costing 1 of 58 aimed rays a crossing at a 7.1227 px gap. Both walks now go through a new `SteppedRiser(op, j)` helper. After: **0 violations**. Note the grid dependence, because it is why two passes missed it — the failure appears only once the grid includes the ±ulp neighbourhood of a tread boundary, so that neighbourhood is now a permanent trap (H12) rather than a probe someone happened to write.

**HS-9.2's "to within tolerance" — AMENDED. There is no safe tolerance, only the exact axis.** `StraightDownTolerance = 1e-5` routed a nearly-vertical ray to the closed form, and the closed form is exact only ON the axis: measured, the **straight-down branch was wrong 101 times in 1008 branch-paired rays, worst 4.7018 px** (Flat+Ogee at x = −190: 150.0000 against a true 154.7018), while the general branch was wrong 0 times. This is not a tolerance that was set too loose. The induced z error shrinks as the CUBE ROOT of the lateral tolerance (eps 1e-1 gives 149.5 px, 1e-2 gives 83.0, 1e-3 gives 31.2, 1e-4 gives 10.4, 2e-5 gives 4.7), which is the signature of a surface whose slope in the inside-distance is unbounded — and Ogee, Round, Dome, Cove and Rounded all have unbounded `dG/dd` by HS-0.1's own ruling. **The branch test is now `dz < 0f && dx == 0f && dy == 0f`, exact equality**, and the clause must be read that way: *when the direction IS the canvas −Z axis*, not *to within tolerance*. It costs the production path nothing, because that path builds its direction as exactly `(0,0,-1)` — confirmed by measurement, not by argument: H6 still reports straight-down taken 825 times and general taken 825 times. `StraightDownTolerance` survives as a documented-deprecated constant only because H6 uses it to construct a deliberately off-axis ray.

### The audit's own failure, and what replaced it

**H6's omission arm reported 0 omissions while 4 existed, and it could not have done otherwise.** It fired at `steps = 4` — `OpFor`'s default and its only step count — and at ray heights that are fractions of the extent (0.08 / 0.30 / 0.52), never a tread. Neither defect above can arise on that fixture. Re-aimed with H6's own rays and the step count swept 4..31 the same check found **4 omissions, worst gap 5.2395 px**; aimed AT a tread with the same sweep, **8 omissions, worst 5.1270 px**. A clean row was therefore a true statement about a fixture that structurally could not fail. **The standing lesson, which generalises past this stage: a check's fixture must be shown capable of exercising the class it claims to cover, and "it passes" is evidence of nothing until it is.** H6 now sweeps the step count contiguously over 4..31 in both aims, and a new check **H12** pins V1's minimal repro, V2's four `(n,k)` risers and V3's branch selection as fixed, named regression traps with their pre-fix figures printed beside them.

**HS-5.5's resolution limit is now MEASURED rather than assumed, and it is live.** The re-aimed arm's first run failed on 2 unmatched truth crossings at 2.5660 px — which turned out not to be the size of anything. At `n = 27, tilt 30°, zf 0.1923` the truth scan finds a genuine **air sliver of 0.0154 px** between two Stepped treads that the march steps over; 2.5660 px was only the distance to the march's nearest answer. The check now measures the **feature extent** — the width of the interval between a consecutive pair of unmatched truth crossings, which is exactly the region mis-classified — and fails only at or above `SurfaceResolution` = 0.02 px, with the sub-resolution count and the raw unmatched count both printed so nothing can hide in the second bucket. An unmatched run of ODD length cannot be paired, meaning parity is wrong over an unbounded region; that fails unconditionally and is 0. **This is HS-5.5's qualified guarantee doing its job for the first time on real data, and the sliver is a real, reported disagreement with truth — not a clean result.**

### What Part 13 does NOT fix

**`ShaperResolveResult.bracketCapped` does not mean what it said, and only the COMMENT was corrected.** "Zero means every step the march took was a proof" is false: the bracket loop leaves by `if (sigma <= SurfaceResolution) break;` as well as by exhausting `MaxBracketDepth`, and only the second increments the counter — so a step that narrowed to `SurfaceResolution` without ever closing the ambiguous shell is a SAMPLED step, not a proved one, and increments nothing. The 0.0154 px sliver above is missed with `bracketCapped = 0`. The right repair is a separate resolution-floored counter on `ShaperResolveResult`, so a caller can ask "was this answer exact?"; it changes a public struct and was left for whoever owns that decision.

**The blindness question was asked of the height audit only.** V4 was a finding about one check's fixture, and the same question — can this fixture exercise the class it claims to cover — has not been put to the Field, Fill, Border or Light audits, all of which report clean (13 / 85 / 79 / 150 PASS, 0 FAIL).

**`zTol = max(1e-3, 1e-3·body)` in `EmitAt` is STILL underived.** Fourth pass running. Nothing new was learned about it here.

**HS-9.5 — DISCHARGED A THIRD TIME.** V1 and V3 change the resolve, so both artefacts were re-rendered (2026-08-31 19:04, the same `RunAll` pass that produced the current `HEIGHT-AUDIT.txt`). The tilted frame asserts **general 126 000 rays, straight-down 0**, and shows **10 049 wall pixels of 43 025 surface pixels** — counts unchanged, which is expected rather than a sign the render was skipped: V1 needs a Stepped riser inside a marched interval, V2 needs one of four step counts, and V3 needs a near-axis ray, and this frame contains none of the three. The straight-down branch is asserted non-zero where it actually runs, in H6's paired sweep (825 straight-down, 825 general), because the contact sheet goes through `ShaperHeight.FillTile` and never calls `ShaperResolve.Query` at all.
