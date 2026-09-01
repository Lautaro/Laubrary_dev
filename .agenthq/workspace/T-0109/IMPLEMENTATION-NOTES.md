# T-0109 — implementation notes: the height stage (extrusion, bevel, Z offset)

Written by the implementer. Everything below is either measured or flagged as unmeasured. Clause IDs are `HEIGHT-SPEC.md`'s (`HS-`), the contracts' (`BC-`, `LR-`, `FC-`, `BD-`, `R`), and `REF-HEIGHT-MATHS.md`'s section letters.

**All code is in the Shaper working copy `D:\UNITY\Laubrary Dev - Shaper` (branch `feat/shaper`), uncommitted and untracked, exactly as T-0105/T-0106/T-0107/T-0108 left theirs. The main copy's `Assets/` is untouched — verified by `git status -- Assets` in `D:\UNITY\Laubrary Dev`, which shows only the pre-existing stray `Assets/Temp.meta`.** Compiled and every check run in the Shaper editor (bridge port 7801), with `Application.dataPath` confirmed as `D:/UNITY/Laubrary Dev - Shaper/Assets` before every run and after every domain reload.

---

## 1. What was built

| File | Lines | What |
|---|---|---|
| `Runtime/Shaper/ShaperHeight.cs` | 1222 | NEW. The catalogue (`ShaperExtrusionTechnique`, `ShaperBevelTechnique`, `ShaperHeightDef`, `ShaperHeightOp`) and all of its maths: `Profile`/`Bevel`/`Composed`, the analytic `ProfileDerivative`/`BevelDerivative`/`ComposedDerivative`, `Inverse`/`ProfileInverse`/`BevelInverseU`/`Bisect`/`InverseLowerBound`, the declared bounds (`ExtrusionSlopeBound`, `BevelSlopeBound`, `ComposedSlopeBound`, `SlopeBound`, `IsLipschitz`, `LinearGradient`), `Breakpoints`, `WallHeight`/`HasWall`, `Publishes`, and the `FillTile` block entry. |
| `Runtime/Shaper/ShaperHeightCompiler.cs` | 218 | NEW. `ShaperHeightDef` → `ShaperHeightOp`, every `ZUIValue` sampled once through `ShaperValue.Sample`; plus `LayerBase` (HS-7.2). |
| `Runtime/Shaper/ShaperResolve.cs` | 644 | NEW. BC-2.2's general query: `ShaperSurfaceKind`, `ShaperResolveBranch`, `ShaperCrossing`, `ShaperResolveLayer`, `ShaperResolveScene`, `ShaperResolveResult`, `Query` (two implementations chosen by the ray), `Depth` (HS-6.6). |
| `Runtime/Shaper/ShaperNormals.cs` | 393 (was 164) | EDITED. `ShaperNormalKind.Profile = 1` implemented as a case in `FillTile`'s body; `ShaperNormalOp` gained `height` and `reflection`; `FillTile` gained two optional trailing parameters; the new `FillProfile` private body. |
| `Runtime/Shaper/ShaperLightRig.cs` | 405 (was 369) | EDITED. `ShaperLayer.zOffset` and `ShaperDocument.layerSpacing` (HS-7.1). |
| `Runtime/Shaper/ShaperFillContract.cs` | 482 (was 451) | EDITED. `ShaperQuantitySet.ShapeEngineWithHeight` and a `FromShapeStage(coverage, edgeDistance, height)` overload (HS-1.4). |
| `Editor/Shaper/ShaperHeightAudit.cs` | 1727 | NEW. H1–H10, the contact sheet and the tilted conformance render. Plain statics, **no `[MenuItem]`, no `EditorWindow`**. |

Artefacts, all in `D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0109\`: `HEIGHT-AUDIT.txt` (full text output), `height-contact-sheet.png` (28 cells), `tilted-conformance.png`, and the re-runnable CLI scripts `_audit.cs`, `_checks.cs`, `_contact.cs`, `_tilted.cs`, `_probe.cs`, `_lin.cs`.

**Headline result: all ten checks pass, and H1's monotonicity claim — the one the whole march rests on — held for all 42 profile×bevel combinations, over 2 268 configurations spanning the full authored parameter ranges, 3 386 268 samples, zero violations.**

---

## 2. Departures from the spec, and completions of it

Each of these is a place where I did something the spec did not literally say. None is a silent change.

**2.1 `ShaperNormals.FillTile` gained two OPTIONAL trailing parameters (`ShaperProgram field = null, float[] stack = null`).** HS-8.2 requires `∇d` from a central difference of `ShaperEvaluator.Distance`, which needs the program and a stack, and LR-3.1 wanted the signature stable so that the `Profile` case would be "an addition to a body rather than a change to a signature". Optional trailing parameters satisfy both readings: every existing call site (including `ShaperFillResolver.PaintTile:934`) compiles untouched, and it is still ONE entry point with a switch inside it. The alternative — putting a `ShaperProgram` reference on `ShaperNormalOp` — was rejected because it would make that struct non-blittable, losing the FC-5.2 form it was built in. Null while `kind == Profile` falls back to `(0,0,1)` per LR-3.5, never to a differencing path.

**2.2 `ShaperNormalOp` gained `ShaperHeightOp height` and `float reflection`.** `ShaperHeightOp` holds no managed reference, so blittability is preserved. `reflection` was added because HS-8.3's Z term is `normalZBase + reflectionFlatten·clamp01(reflection)` and Wave 2 has no reflection dial — without an input, `reflectionFlatten` has nothing to multiply and H10 could not have been made to fail honestly. It defaults to 0, so the shipped look is unchanged.

**2.3 HS-8.2's `∇h` formula omits `Linear`'s term, and I supplied it.** The spec writes `∇h = body·G′(t)·∇t`. `Linear` is not a function of `t` at all (HS-2.3) and `E′(t) ≡ 0` for it, so that product is identically zero and a tilted slab would shade perfectly flat. The implemented gradient is `∂h/∂x = body·[G′(t)·∂t/∂x + B(t)·∂E/∂x|local]`, whose second term is zero for the six `t`-profiles (recovering HS-8.2 exactly) and is the whole of the tilt for `Linear`. HS-4.4 already declares that local gradient as a separate 2-vector; this uses it as a gradient, which is a different operation from summing two BOUNDS (which HS-4.4 forbids, and which is not done anywhere).

**2.4 HS-5.3's step formula does not define a step inside the prism, and this is the one place the scheme is a sampling rather than a proof.** `max((d + τ_min·span)/B, (base_slab − z)·s, (z − top_slab)·s)` has all three terms negative once the point is inside both the prism and the slab. What is implemented: the empty-space skip exactly as written (provably safe — the prism is a vertical extrusion of a level set of the shape's own field, so the 2D distance to it is a lower bound on the 3D distance to the solid, and dividing by `ShaperProgram.bound` turns a reported distance into a true one per BC-4.2); a MIRRORED solid-space skip using the *contained* prism at the slab's highest ζ (not in the spec — added because without it a march through the middle of a thick shape samples at slab resolution for its whole length for nothing); and, in the ambiguous shell between the two prisms, a fixed `SlabSamples = 8` resolution. **A feature thinner than `slabLength / 8` along the ray can therefore be missed.** With `SlabQuality = 16` that is 128 samples across a layer's Z extent. This is stated in the code, in the audit and here rather than hidden, and it is the first thing a verifier should attack (§5).

**2.5 `InverseLowerBound` exists because HS-5.2's containment argument is false for `Linear`.** HS-5.2 treats `G` as a function of `t` alone; for `Linear`, `G(t,nx,ny) = E(nx,ny)·B(t/a)` has a different level set at every canvas point, so a single `τ_min` does not exist. The marcher therefore inverts against `sup E` — the most permissive value — which makes `τ_min` the smallest over the whole canvas, so `{G ≥ ζ} ⊆ {t ≥ τ_min}` still holds and the containing prism is merely LARGER. HS-5.4 is explicit that an over-large prism can only make the marcher stop early, never late. `ShaperHeight.Inverse` remains the point-exact one and is what H4 round-trips.

**2.6 The reference's JS falsiness is deliberately NOT reproduced.** `index.html:1150-1151` reads `Math.max(0.2, Number(settings.curve)||1)`, so an authored `curve` of exactly 0 becomes 1 rather than 0.2, and `Math.round(steps)||4` turns a `steps` of 0 into 4 (REF §H6). Shaper's dials are floats with real defaults and no `undefined`, so 0 is a value an author typed and is clamped to the range floor like any other. Reproducing the falsiness would mean a `curve` slider that jumps from 0.2 to 1 as it passes zero.

**2.7 `Linear`'s y-frame — FLAGGED FOR THE OWNER, one token to change.** HS-2.2 states the formula verbatim as `max(0, 1 + 0.6·(cos θ·nx − sin θ·ny))`, and HS-4.4 states the matching derivative with the same sign, so the spec is internally consistent about it. But the reference rasterises y-DOWN (REF §H2) and Shaper is +Y UP, so for a given authored `angle` the tilt direction here is the vertical MIRROR of the reference app's. T-0105's own porting note ("port Pyre angles unchanged, flip reference-app ones") would flip it. I ported verbatim because the spec is authoritative and self-consistent; flipping is the single `-` on `sinAngle` in `ShaperHeight.Profile`, `ShaperNormals.FillProfile` and `ShaperHeight.LinearGradient`. **This is a look decision, not a correctness one, and nothing I measured can settle it.**

**2.8 `nx, ny` subtract the local support box CENTRE.** HS-2.3 says to normalise by `localSupportHalfW/HalfH` but does not mention the centre. `ShaperFillCompiler.BakeAnchor` (`ShaperFillCompiler.cs:342-345`) subtracts it, and not doing so would tilt about the node ORIGIN rather than about the shape. FC-1.5b's "never divide" discipline is followed: a degenerate box leaves the reciprocals at 0 and `LocalNormalised` returns `(0,0)`.

**2.9 The height sheet carries `body·G`, with `base` NOT folded in.** BC-3.3 #2 says "0 = the base plane", and folding `base` in would double-count it under FC-2.5's `height_final = height_shape + heightDelta·coverageEff`. The base is HS-7's and belongs to the resolve.

**2.10 `ShaperCrossing` carries no normal.** HS-6.2's ruling (a wall's normal is the silhouette gradient with `nz = 0`) is recorded on the type and implemented in the tilted render's consumer, because nothing in Wave 2 shades a crossing and putting a normal on every crossing would have been a field with one caller.

**2.11 `localX/localY` on a crossing are the canvas X and Y.** `ShaperLayer` has no transform of its own in Wave 2 (name, enabled, root, response, and now zOffset), so the layer's local frame IS the canvas frame in XY. Stated on the field; when a per-layer transform arrives that is the one place its inverse goes.

**2.12 The contact sheet's rig gained a close point lamp.** The first render was directional-only and three cells showed nothing: the analytic normal of a `Stepped` profile is exactly flat on every tread with a riser of zero screen width (HS-8.4), so a terraced solid shades as a featureless plateau. That is *correct* shading of a genuinely flat surface, but it makes the sheet useless for the one profile family whose whole point is its height. A point lamp reads surface POSITION too (`ShaperLightLaw.Shade` takes `pz`), so the treads separate by distance falloff with nothing faked.

**2.13 `depth` defaults to 0**, matching `index.html:3805`'s `default:0`. A default document therefore has no extrusion at all, which makes HS-1.4's "publishes `Height` only when a height stage is present" a live path rather than a facility.

---

## 3. Defects I found in my own first pass, by measurement

All three of the first two categories are real bugs in shipped-shaped code that passed compilation and looked right.

**D1 — `Inverse` over-reported the inverse by `a/m` on any Stepped bevel (caught by H4).** The band branch tested `zeta >= eAtA` and, above it, clamped the answer to `a`. But at `ζ == E(a)` the answer can still lie INSIDE the band: a Stepped bevel reaches `B = 1` at `u = (m−1)/m`, so with a Flat profile and `m = 3` the smallest `t` reaching `ζ = 1` is `2a/3`, not `a`. H4 measured `|Δt| = 0.3333` at `a = 1`. **This is the one error direction HS-5.4's "can only stop short, never late" does not cover** — an inverse that is too LARGE gives a containing prism SMALLER than the true cross-section, i.e. a prism that does not contain, which is exactly how a marcher steps through solid geometry. Fixed to `>`.

**D2 — Dome's closed inverse catastrophically cancelled (caught by H4's reach half).** `1 − √(1 − ζ^(2c))` at `c = 4`, `ζ = 0.005` gives `w = 3.9e-19`, and `√(1−w)` rounds to exactly 1.0 *even in double*, so the inverse returned `t = 0` for a height it does not reach. H4 measured the round-trip shortfall `G(Ginv(ζ)) − ζ = −5e-3` on every Dome row. Rewritten as the algebraically identical `w / (1 + √(1−w))`, which returns `1.95e-19` and is comfortably normal in float. Note the shape of the catch: the |Δt| half of H4 barely moved; it was the "does the inverse actually REACH the height it was asked for" half that found it.

**D3 — the general resolve dropped every downward ray's exit crossing (caught by H6).** `QueryGeneral` only reported FLIPS strictly inside the clipped parameter range, and `s1` — the far end of that range — IS the base plane for any downward ray, which is where every ordinary crossing pair ends. So the general branch returned 1 crossing where the closed form returned 2, on every sample. H6 caught it as "0 crossings compared" rather than as a wrong number, which is worth noting: the check failed by comparing NOTHING, not by disagreeing. Fixed by emitting the exit at `s1` when the ray is still inside there — the mirror of the entry the branch already emitted at `s0`.

**D4 — five defects in my own measuring instruments, each of which had made a check pass or fail dishonestly.**

- H3's uniform sweep could not demonstrate a *discontinuity's* divergence: at `h = 1e-6` a 4 000-point grid must land within `1e-6` of a riser to straddle it, which it never does, so the measured value collapsed to 0 and read as "converged" when it was the opposite. Replaced with a TARGETED measurement at each declaration's own named singular locus.
- H3's divergence gate was `last > first × 100`. A `1/√h` divergence — which is what `Rounded`, `Cove` and `Ogee` are — rises by exactly `√(10⁴) = 100` over that ladder, so the gate sat ON the true value and failed on the last float ulp: Cove measured 992.941 against a 994.983 threshold and was reported as "did not diverge" while its readings had risen by a clean `√10` per decade. Now 20×.
- **H6 and H7 placed their ray origins on the wrong Z plane, so the rays missed the shape entirely.** H7 reported "0/0 wall crossings" and H6 compared 0 samples. This is the worst class of instrument defect: a check that tests nothing does not look different from a check that passes. Both now aim at the solid's own mid-height and push back along the ray; H7 now finds 336 wall crossings and H6 compares 1 642.
- H9's gate was absolute-only, which fails on Dome (where `G′ → 0` near `t = 1`, so a 7e-4 absolute error is a 10% relative one and means nothing) and relative-only fails next to a Rounded or Cove bevel's singular end (where the CENTRAL DIFFERENCE is the inaccurate half — its truncation error is `O(h²|G‴|)` and `|G‴|` is enormous there). Now a per-point `err ≤ max(2e-3, 5% )`.
- The contact sheet's 3×5 digit table had several glyphs' columns in the opposite order, which is invisible on a symmetric digit and turns a `2` into a `5`. **The sheet was therefore labelled with plausible-looking WRONG numbers, which is worse than no labels at all.** Caught by reading the rendered PNG, not by re-reading the table.

---

## 4. What is NOT verified

- **Speed.** Nothing is timed. The 420×300 tilted frame renders in a few seconds; there is no comparison to anything and no profile of the march. T-0105 recorded the same gap and it is still open.
- **The height sheet is not wired into `ShaperFillResolver.PaintTile`.** FC-2.5's `height_final = height_shape + heightDelta·coverageEff` is still `0 + heightDelta·ce` in the resolver. HS-10 says "no fill reads `height` yet" and "nothing else about the fill stage changes", so this was read as out of scope — but a verifier should decide whether that wiring belonged here or belongs to T-0110. `ShaperHeight.FillTile` mirrors `ShaperFillOps.FillTile` exactly so the wiring is a few lines.
- **`ShaperNormalKind.Profile` is not selectable from anything authored.** `ShaperLightCompiler.CompileNormal` still always emits `Constant`; the `Profile` case is exercised only by the audit and the two renders. Nothing an author can set turns it on. That is consistent with "no UI in Wave 2" but it does mean the case has no production caller yet.
- **The multi-span HS-6.6 check is thin: 7 rays.** The hollow-shell fixture and ray direction happen to produce only 7 two-span rays out of 697. All 7 behave correctly, but 7 is not coverage. Widening that fixture is cheap and worth doing.
- **The in-prism sampling limit of §2.4 has not been attacked.** No test constructs a feature deliberately thinner than `slabLength/8` and checks whether the march misses it. I believe it can be made to miss one; I have not made it.
- **LR-3.1's LT-13 claim ("touches neither `ShaperLightLaw` nor one line of Solids") is true by inspection** — neither file was edited — **but no test asserts it.**
- **Undo / UI compliance: not applicable.** No UI, no window, no menu item was added, per the standing Wave-2 rule and the project's "never add a menu the user didn't ask for". Nothing to vet against the UI Guide.
- **The `Linear` y-frame question (§2.7)** cannot be settled by measurement and is waiting on the owner.
- **The `Wall`/`Cap` classification tolerance** is `zTol = max(1e-3, 1e-3·body)`. It is mechanical (the boundary has exactly three parts, two of which are horizontal faces) and it produced 336/336 correct wall classifications on the H7 fixture, but the tolerance itself is not derived from anything.

---

## 5. What a verifier should attack first, in order

1. **The in-prism sampling resolution (§2.4).** Build a shape whose solid has a feature thinner than `slabLength/8` along a tilted ray — a thin shelled ring, a narrow subtract — and see whether `ShaperResolve.Query` misses a crossing pair. If it does (I expect it can), the honest fix is either a higher `SlabSamples`, or a genuinely sound in-prism step derived from something the shape publishes.
2. **`InverseLowerBound` for `Linear` combined with each bevel.** `sup E` is the right conservative choice for `Linear` alone; check the product case has not been over-tightened anywhere, especially `Ogee` (which bisects on `supE·B` rather than on `G`).
3. **`Query` on subtractive and shelled trees generally**, widening the 7-ray multi-span fixture until it is real coverage. HS-6.6's ruling is mine and it is under-tested.
4. **The `entering` parity logic in `QueryGeneral`** when the ray origin is inside the solid AND the caller's crossing buffer truncates mid-layer. `truncated` is reported, but the parity stamped onto a truncated slice has not been checked.
5. **H4's `|Δt|` tolerance of 2e-3.** It is loose because a pure bisection on a step function converges from above at its own resolution. A tighter, per-technique tolerance would catch a smaller algebra slip than the one D1 was.
6. **Whether `ShaperHeight.FillTile` writing 0 outside the silhouette is right for the antialiased edge band.** It follows `index.html:1421`'s `if (distance>0) continue` exactly, but samples with `d ∈ (0, halfBand)` have partial coverage and now get height 0 with a hard cut at `d = 0`. Nothing downstream reads it yet, so nothing is currently wrong; it will matter the moment a fill ramps by height.
