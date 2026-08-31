# T-0109 contract extract — what binds the height stage (extrusion, bevel, Z offset)

**Distilled for T-0109 from `BUFFER_CONTRACT.md` (T-0102, BC-*), `SHAPE-TREE-RULES.md` (T-0103, R*), `FILL-CONTRACT.md` (T-0106, FC-*), `BORDER-CONTRACT.md` (T-0107, BD-*), `LIGHT-RIG-CONTRACT.md` (T-0108, LR-*), plus `SHAPE-ENGINE-SPEC.md` and `CONTRACT-EXTRACT.md` from T-0105 as the format model, and the shipped code in `D:\UNITY\Laubrary Dev - Shaper\Assets\Packages\Laubrary\Runtime\Shaper\`, which is authoritative wherever a document and the code disagree.** Also mined: `T-0109\REF-HEIGHT-MATHS.md`, a read-only reference-maths transcription already sitting in this task's own workspace folder (not one of the five contracts, but directly load-bearing for §6 and §9 below — it is where the extrusion/bevel formulas, the Lipschitz-bound derivations and the Z-offset site are worked out against the reference app). 2026-08-31.

Format follows T-0105's `CONTRACT-EXTRACT.md`: dense, clause-ID-first, every claim carries `file:line`.

---

## 1. The height sheet

**There is no height sheet in the shipped code today, and no contract defines one as a per-layer or per-document object — only as a per-pixel quantity.**

| Clause | Says | file:line |
|---|---|---|
| BC-3.3 #2 | `height` — float, "layer-local units along the layer's own up axis; 0 = base plane". Status: **New as a published quantity**. Frame: "the layer's own local space, right-handed, +Y up, origin at the layer's own origin. Not the screen. Not flipped." | `T-0102\BUFFER_CONTRACT.md:189,184` |
| BC-3.7f | The generator that publishes a sheet "never allocates, replaces, resizes or frees" it — host-allocated, host-owned. Applies to height like every other sheet. | `BUFFER_CONTRACT.md:258` |
| LR-1.5 | "Height is in canvas pixels… a height of 4 is four pixels tall." Pinned specifically so `dh/dd` is dimensionless. Frame: canvas space, canvas-pixel units, origin at canvas centre, +X right, +Y up, +Z out of the screen toward the viewer, at **every nesting depth**, no per-layer/per-particle rescale. | `T-0108\LIGHT-RIG-CONTRACT.md:141,147` |
| FC-1.2 row 2 | `height` sheet: "Available in T-0106? **No — no producer exists**." | `T-0106\FILL-CONTRACT.md:82` |
| FC-2.5 | The **fill's height DELTA** (not the shape's own height) is "a scalar in layer-local height units (BC-3.3 #2), ADDED to the shape's own height, weighted by `coverageEff`." Composition: `height_final = height_shape + heightDelta · coverageEff`. "The shape's own height does not exist in T-0106… it arrives with T-0109's extrusion." | `FILL-CONTRACT.md:238-246` |
| F8.2 | "Not here: no extrusion thickness, no bevel profile, no Z offset, no side walls, no height *profile*. A fill emits a scalar height *delta*; the shape's own height is worked out by T-0109 'by a fixed recipe from its own published edge distance'." | `FILL-CONTRACT.md:684` |
| LR-3.2 | "There is no height profile, because a height profile is T-0109's… and taking it here would be scope theft." Confirms the shipped engine writes exactly two sheets, `distance` and `coverage` (`ShaperEvaluator.cs:160-161`); no `h` exists to difference. | `LIGHT-RIG-CONTRACT.md:403,405` |

**Ownership/write/read, as it will be once T-0109 exists (inferred from the above, not yet built):**
- **Owns:** the shape node that publishes it — same rule as `edgeDistance` and `coverage` (BC-3.7a, declared statically before render).
- **Who may write it:** the height-profile stage T-0109 builds (a new per-node mechanism, "worked out by a fixed recipe from its own published edge distance" — quoted at `FILL-CONTRACT.md:246`, sourced `SHAPER_THE_DESIGN.md:323`), PLUS the fill stage's height-delta accumulation which is **additive on top**, per FC-2.5. Both write into the same conceptual `height` quantity but through different mechanisms — the shape publishes the base height, fills add deltas to it.
- **Who may read it:** any downstream consumer per BC-3.1's general rule (fill via `ByHeight`/Ramp-by-height, greyed out today per FC-1.2; the normal provider, per LR-3.1's `Profile` case reading `heightSheet`; potentially border/bevel machinery). None of these reads exist yet in shipped code.
- **Per-layer or per-document:** **per-node** (a shape-tree quantity, like every other BC-3.3 sheet), not per-layer and not per-document. The Z **offset** (§6 below) is the separate, as-yet-nonexistent per-layer/per-document quantity — do not conflate the two. `height` is "how tall this point on this node's surface is above its own base plane"; Z offset is "where this whole layer's base plane sits relative to other layers."
- **Relationship to a depth/Z-buffer:** BC-3.3 #7 (`depth`) is explicitly **not** the same quantity — `depth` is "the thickness of solid the sampling ray traverses at that sample" (BC-3.5, `BUFFER_CONTRACT.md:222`), identically zero until T-0109, and is a **derived** quantity from BC-2.2's resolve-query crossing list, not something a shape authors directly. `height` is the authored/computed surface elevation that the resolve's crossings are computed *from*. REF-HEIGHT-MATHS.md §E.iii independently confirms the reference app's own architecture treats the height buffer as simultaneously "the depth buffer, the surface geometry, and the input to lighting" (`REF-HEIGHT-MATHS.md:330`) — i.e. one buffer serves all three roles, which is the precedent T-0109 is expected to reproduce structurally, not the buffer contract's own ruling (the buffer contract keeps `height` and `depth` as two named, distinct quantities in its closed vocabulary of nine).

---

## 2. What the shape node publishes — quantities, and drift vs the code

**The closed vocabulary is nine (BC-3.3, `BUFFER_CONTRACT.md:182-196`).** Cross-checked against the shipped `ShaperQuantity` enum at `ShaperFillContract.cs:34` (values confirmed via `Name()` switch, `ShaperFillContract.cs:112-128`):

| # | Contract name (BC-3.3) | Code enum member (`ShaperQuantity`, `ShaperFillContract.cs`) | Drift? |
|---|---|---|---|
| 1 | `coverage` | `Coverage` | None. **But** a live contradiction on range: `SHAPE-ENGINE-SPEC.md:39` says clamped `[0,1]`, shipped `ShaperField.Coverage` does clamp (`ShaperField.cs:54-61`); `BUFFER_CONTRACT.md:188` says "unbounded above, publisher never clamps." Resolved *for the fill stage only* by FC-1.6: treat as unbounded, clamp at use. Not resolved for the shape stage itself — flagged again in §10. |
| 2 | `height` | `Height` | Enum slot exists; **no producer** (§1). |
| 3 | `edgeDistance` | `EdgeDistance` | Shipped and published (`ShaperEvaluator.cs:148`), negative inside (`ShaperField.cs:9-10`). |
| 4 | `heat` | `Heat` | Enum slot exists; no producer in Shaper (Pyre-only concept, not ported). |
| 5 | `density` | `Density` | Enum slot exists; no producer. |
| 6 | `soot` | `Soot` | Enum slot exists; no producer. |
| 7 | `depth` | `Depth` | Enum slot exists; identically zero until T-0109 (BC-3.5). |
| 8 | `age` | `Age` | Enum slot exists; no producer. |
| 9 | `surfaceDirection` | `SurfaceDirection` | Enum slot exists; **refused as a rampable quantity by type** — `IsRampable(ShaperQuantity q) => q != ShaperQuantity.SurfaceDirection` (`ShaperFillContract.cs:137`, FC-6.3c). No drift from BC-3.3's naming; the vocabulary is exactly nine on both sides — **no drift found** between the contract's closed list and the code's enum membership. |

**`ShaperQuantitySet.ShippedShapeEngine` is the actually-published set today: `Coverage | EdgeDistance` only** (`ShaperFillContract.cs:79`, confirmed independently at `FILL-CONTRACT.md:21`: "the whole of `Runtime\Shaper\` contains no height, heat, density, soot, depth, age or surface-direction sheet"). Seven of nine are enum slots with zero producers. T-0109's job is to make `height` (and, derivatively, `depth`) real producers; `heat`/`density`/`soot`/`age` stay unpublished and are out of this task's scope; `surfaceDirection` gets a *new* producer path via the normal-provider mechanism (§4), not via `ShaperQuantitySet`.

**`edgeDistance` — is it declared/published, units, sign:**
- **Yes, declared and published**, by every shape node unconditionally: `ShaperQuantitySet.ShippedShapeEngine = Coverage | EdgeDistance` (`ShaperFillContract.cs:79`, confirmed at `BORDER-CONTRACT.md:199`: "It needs only the node's distance, which every shape node publishes unconditionally").
- **Units:** canvas pixels, at every nesting depth — guaranteed by the `σ_min` rescale (R2, `SHAPE-TREE-RULES.md:61`; `ShaperField.cs:10-12`).
- **Sign:** **negative inside, positive outside, zero on the boundary** — the standard SDF convention (BC-3.3 #3, `BUFFER_CONTRACT.md:190`; shipped at `ShaperField.cs:9-10`). Stated repeatedly as a trap: Pyre's old `BorderInsideDistance` ran the opposite direction (unsigned, grows inward) and "inverts if it is ported literally onto a negative-inside quantity" (`BUFFER_CONTRACT.md:190`; restated FC-1.3, `FILL-CONTRACT.md:91`; restated again BD context, `BORDER-CONTRACT.md:17,45`).
- **`inside`/`t` in REF-HEIGHT-MATHS.md is a DIFFERENT, derived quantity, not `edgeDistance` itself:** the reference app's height maths is driven by `t = clamp01(−distance/span)` (`REF-HEIGHT-MATHS.md:15-20`), i.e. `edgeDistance` negated, divided by `span = max(1, min(halfW,halfH))` (the shape's own shorter half-extent), and clamped to `[0,1]`. T-0109 will need to decide whether it derives an equivalent `t` from the shipped `edgeDistance` sheet the same way, or defines its own normalisation — REF-HEIGHT-MATHS.md §0 and §H1 are the worked precedent and gotcha list (the reference app's own primitives were inconsistently normalised before a later fix; `ShaperField.cs`'s shipped SDFs are already exact and in canvas units, so this specific historical bug does not recur, but the `t = −d/span` derivation itself is not yet a contract clause anywhere — it is reference-app precedent, not a ruling).

---

## 3. The bound machinery

**What a bound formally means (BC-4.2, quoted exactly):** "The multiplier belongs on the *true* side: `reportedDistance ≤ declaredBound × trueDistance`… and the march's safe step is `reportedDistance / declaredBound`." (`T-0105\CONTRACT-EXTRACT.md:63`, sourced `BUFFER_CONTRACT.md:287`). `B ≥ 1` per node; nearly free if declared at authoring time, "roughly forty times as expensive" if retrofitted (`ShaperBound.cs:8-12`).

**How a transform composes it:** **unchanged** — `Transform(childBound) => childBound` (`ShaperBound.cs:29`). The `σ_min` rescale already absorbs the transform's effect on distance, so the bound passes through as-is (R2, `SHAPE-TREE-RULES.md:65`).

**How each combine mode composes it:**
- Add / Subtract / Intersect, hard or soft: **`max(boundA, boundB)`**, proven via convexity of the smooth-min weights (`ShaperBound.cs:73-90`, `Combine(mode, boundA, boundB, blendWidth, carveStrength)`).
- Soft Add / soft Intersect: factor `SoftBlendFactor = 1f` on top of `max` — proven free (`ShaperBound.cs:64`).
- Soft Subtract: factor `SoftCarveFactor = 1f`, **explicitly declared as its own branch** even though the derived value equals 1, per an explicit requirement in the T-0105 task brief (`ShaperBound.cs:45-57,71`).
- Sweep: `max(childBound, 1)` (`ShaperBound.cs:96`).
- Shell: `childBound` unchanged (`ShaperBound.cs:99`).
- **Border/Dilate** (T-0107's addition): the strip inherits the node's declared bound unchanged, "because `abs` and a hard `max` both preserve gradient magnitude" (`BORDER-CONTRACT.md:65`); joining is a constant subtraction `d − reach` which "preserves the declared bound exactly because adding a constant does not change a gradient" (`BORDER-CONTRACT.md:108`).

**What consumes the bound:** nothing in the shipped shape/fill/border/light stages marches a ray yet — BC-4.2 states this is priced for "anything that later steps along a ray," and the only place that exists today is B7/BC-1.4's stated future tilted resolve. T-0109 is explicitly the task where a ray first steps through a solid (§7), so T-0109 is the **first real consumer** of this bound.

**The exact API surface an implementer of a new bounded stage calls or extends today:**

| Type/member | Signature | file |
|---|---|---|
| `ShaperBound.Transform` | `static float Transform(float childBound)` | `ShaperBound.cs:29` |
| `ShaperBound.Combine` | `static float Combine(ShaperCombineMode mode, float boundA, float boundB, float blendWidth, float carveStrength)` | `ShaperBound.cs:73` |
| `ShaperBound.Sweep` | `static float Sweep(float childBound)` | `ShaperBound.cs:96` |
| `ShaperBound.Shell` | `static float Shell(float childBound)` | `ShaperBound.cs:99` |
| `ShaperOp.bound` | `public float bound;` — per-op declared bound of the subtree rooted at this op | `ShaperProgram.cs:69` |
| `ShaperProgram.bound` | `public float bound = 1f;` — the root's declared bound | `ShaperProgram.cs:88` |
| `ShaperOpKind` | `enum { Empty=0, Leaf=1, Combine=2, Sweep=3, Shell=4, Dilate=5 }` — **APPEND-ONLY**, a new op kind (e.g. a height-profile op) goes on the END | `ShaperProgram.cs:9-29` |
| `ShaperOp` | flat struct, `p0..p11` float slots reused per op kind, `m00..m12` (2×3 inverse), `distanceScale`, `bound`, `boxCx/Cy/HalfW/HalfH` | `ShaperProgram.cs:46-73` |
| `ShaperCompiler.Compile` | `static ShaperProgram Compile(ShaperNode root, float phase01=0f, uint seed=0u)` and the `parentForward`-taking overload `static ShaperProgram Compile(ShaperNode root, in ShaperMatrix parentForward, float phase01=0f, uint seed=0u)` | `ShaperCompiler.cs:159,181` |
| `ShaperEvaluator.Distance` | `static float Distance(ShaperProgram program, float x, float y, float[] stack)` — a **pure function of a canvas point**, usable for a legal (non-neighbour-read) central difference (LR-3.2 exploits exactly this for `∇d`) | `ShaperEvaluator.cs:50` |

**Because a height profile is not a primitive, not a transform and not a combine node, it falls through all three composition rules above.** No existing `ShaperOpKind` case, no existing `ShaperBound` method, and no existing primitive/transform/combine slot is the right place to attach a profile's own Lipschitz behaviour. The natural extension point, by the append-only discipline already established for `ShaperOpKind` (`ShaperProgram.cs:9`) and for `ShaperNormalKind` (`ShaperNormals.cs:8-9,16`, which already reserves `Profile = 1` for T-0109 by name), is: **a new `ShaperOpKind` case for the height-profile stage, its own `ShaperBound`-style composition method (not one of the four existing ones), and per REF-HEIGHT-MATHS.md §C.8 the composed bound is a *product* rule, `body·(L_E·1 + sup|E|·L_B/a)`, not a `max` rule** — genuinely new maths, not a reuse of `ShaperBound.Combine`.

---

## 4. Who reads height today

**Nobody — no consumer of a height/normal channel exists in shipped code yet, because no producer exists.** But the consumer *interfaces* are built and waiting, per LR-3.1's explicit design-for-T-0109:

| Consumer | What it expects | Exact signature | Status |
|---|---|---|---|
| `ShaperNormals.FillTile` | `distance` and `heightSheet` params **MAY be null** — "a provider reads only what it declared," and `Constant` (Wave 2's only kind) declares neither and ignores both params. Writes `float[3n]`, 3 floats/sample, UNIT vectors, canvas frame, never NaN, never zero, never unwritten. | `static void FillTile(in ShaperNormalOp op, in ShaperSampleGrid grid, int x0, int y0, int width, int height, float[] distance, float[] heightSheet, float[] normal, int srcOffset, int srcStride, int dstOffset, int dstStride)` | `ShaperNormals.cs:121-126`. `ShaperNormalKind.Profile = 1` is **reserved and named but not implemented** (`ShaperNormals.cs:16`) — T-0109 adds this case. |
| `ShaperLightLaw.Shade` | Takes a surface normal **as an input** (`nx,ny,nz`, unit, canvas frame) — never derives one, never reads a sheet itself. Two output triples `(lr,lg,lb)` multiplicative and `(sr,sg,sb)` additive: `final = albedo·L + S`. | `static void Shade(in ShaperLightRigCompiled rig, in ShaperResponseCompiled resp, float px, float py, float pz, float nx, float ny, float nz, float vx, float vy, float vz, out float lr, out float lg, out float lb, out float sr, out float sg, out float sb)` | `ShaperLightLaw.cs:192-199`. Height does not reach this call directly; only the normal does. |
| `ShaperLightCompiler.CompileNormal` | Compiles a layer's `ShaperLightResponse` into a `ShaperNormalOp` — where a T-0109 profile's compiled parameters would be assembled. | `static ShaperNormalOp CompileNormal(ShaperLightResponse resp)` | `ShaperLightCompiler.cs:486` |
| `ShaperFillResolver.PaintTile` | Owns `ShaperFillBuffers.height` (`float[]`), accumulated **only from fill height-deltas today**: `buf.height[i] += buf.heightDelta[i] * ce` where `ce = coverageEff`. No shape-published height is added anywhere in this loop yet. | `static void PaintTile(ShaperFillDocument doc, in ShaperSampleGrid grid, int x0, int y0, int width, int height, ShaperFillBuffers buf, in ShaperFillSheets sheets, ShaperLightScene scene = null)` | `ShaperFillResolver.cs:878-881`; height accumulation at `ShaperFillResolver.cs:1046` (buffer declared `ShaperFillResolver.cs:227`). |
| `ShaperSolids.FillTile` | A **second, independent writer of the same `normal` array** — not a `ShaperNormalKind` case. Writes coverage/distance/normal from its own analytic geometry (facet/sphere/ring). Not affected by T-0109's height-profile work; Solids' normal is unrelated to Silhouette's height. | `static void FillTile(ShaperSolidGeometry sg, ShaperSolidOp op, in ShaperSampleGrid grid, int x0, int y0, int width, int height, ShaperSolidEmit emit, int slab, int stride)` (called from `ShaperFillResolver.cs:906`) | `ShaperSolids.cs`; call site `ShaperFillResolver.cs:906-916`. |

**LR-3.x on published-vs-derived surface direction — the clauses that matter most for T-0109, quoted:**

- **LR-3.1** (the interface): "the law's normal comes from a NORMAL PROVIDER: a named, declared, swappable stage that writes a `float[3n]` sheet of unit vectors in the canvas frame, one per sample, before the law runs. The provider interface IS that sheet plus a declaration. Anything that can write the sheet is a provider. The law reads the sheet and knows nothing about who wrote it." `LIGHT-RIG-CONTRACT.md:347`
- **LR-3.2** (why Wave 2 can't finish it): "Wave 2's only Silhouette provider is `Constant`, defaulting to `(0,0,1)`. There is no height profile, because a height profile is T-0109's… and taking it here would be scope theft that collides with a scheduled task." `LIGHT-RIG-CONTRACT.md:403`. Also: without a profile, `∇d` alone gives "a constant-magnitude tilt everywhere inside the silhouette… the analytic normal is not merely unavailable in Wave 2; it is *meaningless* until a profile exists." `LIGHT-RIG-CONTRACT.md:405`
- **LR-3.3** (the prohibition T-0109 must not reintroduce by accident): "no normal provider in Wave 2 may read the screen-space neighbours of any intermediate buffer. A differencing provider is permitted in a LATER wave only if it is registered as an image-space stage under BC-4.3, named in the tile-independence exclusion list, and excluded from the tilt conformance render." `LIGHT-RIG-CONTRACT.md:413`. T-0109's profile must publish `dh/dd` **analytically** so the differencing route is never needed at all (see the two explicit requirements in §7 below), not merely permitted-with-registration.
- **LR-3.4** (the fill height-delta fork, ruled for Wave 2 only): "a fill's height delta does NOT contribute to the surface normal in Wave 2. It continues to accumulate into `ShaperFillBuffers.height`… and is consumed by nothing." `LIGHT-RIG-CONTRACT.md:429`. T-0109 does not have to re-open this fork — it is about the *fill's* delta, not the shape's own profile height, and Part 10's requirements (§7) do not ask T-0109 to change it.

---

## 5. The fill's height delta

**FC-2.5, quoted:** "the height delta is a scalar in layer-local height units (BC-3.3 #2), ADDED to the shape's own height, weighted by `coverageEff`. It is optional in the strict BC-3.7a sense: a fill DECLARES at compile time whether it emits height, and the host allocates the height-delta sheet only if some fill in the layer declares it." `FILL-CONTRACT.md:240`

- **Already exists in built code:** yes. `ShaperFillBuffers.height` and `.heightDelta` are live; every shipped fill kind (Solid, Gradient, Ramp, Texture) carries a `heightDelta` (`ZUIValue`, default 0) dial common to all four (`FILL-CONTRACT.md:533`), and the accumulation `buf.height[i] += buf.heightDelta[i] * ce` runs today at `ShaperFillResolver.cs:1046`.
- **Compositing order relative to the shape's own extrusion height:** **additive, always, order-independent** — `height_final = height_shape + heightDelta · coverageEff` (`FILL-CONTRACT.md:244`). This is a flat sum, not a layered/sequenced composite: FC-2.6c is explicit that "Height is always added" regardless of the fill's `Over`/`Add` colour-composite mode — "There is no 'over' meaning for a height *delta* — a delta by definition adds." `FILL-CONTRACT.md:282`
- **Who adds them together:** the fill resolver, inside `ShaperFillResolver.PaintTile`, per-owner, at the point each owner's fill emits (`ShaperFillResolver.cs:1046`). Not the shape stage, not T-0109's height-profile stage — the shape publishes its own `height` sheet (once T-0109 builds it) and the fill resolver is the single place that sums the two.
- **Today, since the shape's own height does not exist**, the sum degenerates to `height_final = 0 + heightDelta · coverageEff`, i.e. the fill's delta is "produced, testable, and consumed by nothing" (`FILL-CONTRACT.md:246`) except being written to a buffer nothing reads — described as deliberate, "the hook that makes T-0110 nearly free" (`FILL-CONTRACT.md:246`, `F8.3`).
- **T-0109 must not add a fourth fill output or reopen FC-2.1's "albedo, always; a veil, always; and a height delta, only if it declares that it does. It emits nothing else"** (`FC-2.1`, referenced `LIGHT-RIG-CONTRACT.md:434`) — LR-3.4 already refused option (b), "fills additionally emit a height gradient," specifically to keep this contract closed. T-0109's job is to make the *shape's* `height_shape` term real; the fill-delta half of the sum is already complete and out of scope.

---

## 6. The Z offset

**No per-layer Z, no Z offset, and no cross-layer Z compositing exist anywhere in the shipped Shaper code today.** Confirmed directly: `ShaperLayer` (`ShaperLightRig.cs:315-321`) has exactly four fields — `name`, `enabled`, `root` (`ShaperNode`), `response` (`ShaperLightResponse`) — no Z, no order-derived base, no depth-buffer participation. `ShaperDocument` (`ShaperLightRig.cs:338-368`) holds `canvasWidth/Height`, `pixelSize`, an ordered `List<ShaperLayer> layers` ("Ordered, bottom-most first. Order is authored data and no stage may reorder it," `ShaperLightRig.cs:349`), the light rig, `phase01`, `seed` — again, no Z anywhere. This matches BUFFER_CONTRACT.md's own correction: "Pyre has no layer Z at all, no `layerOrder` symbol and no Z offset; its layers simply composite in order." `BUFFER_CONTRACT.md:194`

**Contract clauses on layer ordering / Z / the "ordering base":**

| Clause | Content | file:line |
|---|---|---|
| BC-3.5 | Defines `depth` as entry-to-exit ray thickness, "corrected in review: an earlier draft attributed `layerOrder × 0.75` to Pyre. It is the reference app's, not Pyre's." | `BUFFER_CONTRACT.md:194,222` |
| BC-4.4 / Part 5 §1 | The tilted-conformance requirement (full text in §7) is explicitly gated on extrusion existing, "the first thing that can tilt a solid rather than a zero-thickness sheet," landing in T-0109. | `BUFFER_CONTRACT.md:294,296` |

**What REF-HEIGHT-MATHS.md (reference-app precedent, not a ruling) establishes about how layer order maps to Z, and where a per-layer Z offset would attach:**

- E.i — confirmed: `layer.depth` is extrusion **thickness** (`body`), always non-negative; `SHAPER:1362`, `SCHEMA:57` (`REF-HEIGHT-MATHS.md:296-308`).
- E.ii — confirmed: a layer's Z **position** today is `base = order * 0.75`, purely a function of list index, in **grid cells**; "the only way to move a layer in Z is to reorder the list, in fixed 0.75-cell steps, and two layers can never share a Z" (`REF-HEIGHT-MATHS.md:320`, `SHAPER:1362`, `:1389`).
- E.iii — the composite height buffer (`heights`, one `Float32` per grid cell, sentinel `-9999`) is simultaneously the depth buffer, the surface geometry and the lighting input — "the height field already IS the depth buffer… literally true here and no new buffer is needed for a Z offset" (`REF-HEIGHT-MATHS.md:330`). The z-test is `if (z > previous)` with a `FUSION_CELLS = 1.7` smooth-max fusion band on both sides of the compare (`REF-HEIGHT-MATHS.md:332-348`).
- **Exact site for a Z offset, per REF-HEIGHT-MATHS.md:352:** `SHAPER:1362`, in `compileLayer`'s returned record — `base: order*0.75` becomes `base: order*0.75 + (Number(layer.zOffset)||0)`. Argued as correct because `base` is computed once per layer per frame, is already the sole Z contributor, is already inside the envelope-evaluated path so the offset animates for free, and nothing downstream needs to change. The alternative site (per-cell, at `SHAPER:1424`) is named only to be ruled out as strictly worse.
- **Gotcha (H3):** `FUSION_CELLS = 1.7` means any Z offset under ~1.7 cells silently welds into the neighbouring layer instead of separating it — "any offset under ~1.7 cells changes the fused shape rather than the stacking order… a build agent testing a new Z-offset dial with small values will conclude it 'doesn't do anything'" (`REF-HEIGHT-MATHS.md:420`).
- **Gotcha (H6, sentinel):** the height buffer's empty-cell sentinel is `-9999`, tested against `-900`; with `base = order*0.75 ≥ 0` today that threshold is unreachable, but "a signed Z offset makes it reachable… any new Z-offset range must be bounded well inside that, or the sentinel must become a separate occupancy mask" (`REF-HEIGHT-MATHS.md:438`).

**What T-0109 must conform to, concretely:** since no existing Shaper contract clause or shipped field constrains Z offset yet, T-0109 is greenfield here — but whatever it builds must (a) be an addition to `ShaperLayer`/`ShaperDocument`, analogous in shape to how `ShaperLightRig`/`ShaperDocument` were introduced as new document-level objects in T-0108 (LR-1.1, `LIGHT-RIG-CONTRACT.md:61`, "no light is owned by a node… no stage may add one at render time" — the equivalent per-layer-not-per-node discipline likely applies to Z), (b) respect LR-1.5's "canvas pixels… at every nesting depth, no per-layer rescale" frame for whatever Z units it authors in, and (c) feed into BC-2.2's general resolve query (§7) as the mechanism that finally gives cross-layer ordering real geometric meaning rather than pure list-order compositing.

---

## 7. The tilted conformance frame

**The clause, in full and verbatim, with its ID and exact line:**

> **BC-2.7, third bullet — "The tilted conformance render."** `T-0102\BUFFER_CONTRACT.md:162`
>
> "Before Wave 2 closes, one deliberately tilted frame is rendered through the general resolve path and kept, re-rendered whenever the resolve changes. It is not a feature and is exposed nowhere. **That requirement is stated here and is DONE in T-0109**, which is where extrusion lands and therefore the first task that can tilt an actual solid rather than a zero-thickness sheet. **It is not satisfiable in T-0102 and must not be treated as an outstanding item against this task.**"

Restated and cross-referenced twice more in the same document:

- BC-4.2's table row: "BC-2 tilt | **One tilted frame rendered through the general path, kept and re-rendered whenever the resolve changes** | **DONE in T-0109**, before Wave 2 closes — see BC-4.4" — `BUFFER_CONTRACT.md:281`
- BC-4.4, in full: "**What is explicitly NOT satisfiable in T-0102.** The tilted conformance render. It requires extrusion — the first thing that can tilt a solid rather than a zero-thickness sheet — which lands in **T-0109**. This task closes long before it can be satisfied. **Stating the requirement is this task's deliverable; discharging it is T-0109's.** It must not be logged as an outstanding item against T-0102." — `BUFFER_CONTRACT.md:294-296`

**Every constraint the clause places on the artefact:**

1. **What must be rendered:** "one deliberately tilted frame" — i.e. a document/scene authored with a non-straight-down view or a tilted solid, not an arbitrary test image.
2. **Through what path:** "the general resolve path" — BC-2.2's general ray-query resolve (below), not any special-cased or shortcut renderer. This is the load-bearing constraint: it is meant to exercise the *same* code path production rendering uses, per BC-2.3's rule that "a caller MUST NOT be able to tell that v1 only handles one direction by the shape of what it calls" — the general API, specialised only in body.
3. **Kept where:** "kept" — implies a persisted artefact (an asset, a golden image, or equivalent), analogous to T-0105's own precedent of shipping a contact sheet PNG kept in the workspace (`T-0105\shaper-contact-sheet.png`, referenced as precedent at `FILL-CONTRACT.md:664`, FT-20: "A passing table is not a picture… kept and **looked at by a human**").
4. **When it must be re-rendered:** "whenever the resolve changes" — i.e. it is a living conformance artefact, re-generated on resolve-affecting changes, not a one-time snapshot.
5. **Scope constraint — it is explicitly NOT a feature:** "not a feature and is exposed nowhere" — no UI, no menu item, matching the standing "no UI, no menu item, no drawer" rule every Wave-2 contract repeats (`SHAPE-ENGINE-SPEC.md:16`, `FILL-CONTRACT.md:718` F8.6, `LIGHT-RIG-CONTRACT.md:11`).

**BC-2.2, the general resolve query — stated signature and what Wave 2 (i.e. T-0109) must implement vs may defer:**

> **BC-2.2, quoted:** "Given a ray, report every layer the ray passes through and, for each surface it crosses, the depth at the crossing and the local coordinates of the crossing point on that layer." `BUFFER_CONTRACT.md:106`

Three contractual properties, none optional (`BUFFER_CONTRACT.md:108-112`):
- **Every layer, not the front-most** — the whole ordered set of crossings, which is what makes occluded-outline coverage and true multi-layer depth fall out "for free."
- **Depth at each crossing** — so ordering, occlusion and the `depth` quantity all read from one source.
- **Local coordinates at each crossing** — so every downstream consumer (fill, border, bevel band) is defined against the layer's own space.

**What v1 (and therefore T-0109, unless it explicitly extends v1) implements vs defers, per BC-2.3 (`BUFFER_CONTRACT.md:114-118`):**
- **Implemented now:** "exactly one ray direction: straight down, orthographic." For that direction the query "has a closed form — it is algebraically the same point sample the current tool performs, at the same cost. There is no march, no acceleration structure and no camera in v1."
- **Must still be true even though only one direction is implemented:** "The API is the general one from day one; only the implementation is special-cased. A caller MUST NOT be able to tell that v1 only handles one direction by the shape of what it calls." This is the binding requirement on T-0109's implementation shape, independent of whether it extends the ray directions: the general signature (arbitrary ray in, ordered crossing list out) must exist and be what every caller uses, even if only the straight-down case is filled in.
- **May be deferred:** actual marching / acceleration structures / a camera for non-straight-down rays. LR-4.5 (T-0108) already deferred shadows on exactly this ground ("v1's resolve is straight-down-only… a shadow ray is by definition a ray *toward a light*, which is not straight down for any light worth authoring," `LIGHT-RIG-CONTRACT.md:25`), and the tilted conformance render itself is explicitly about proving the *general path* handles a tilt correctly at the one ray direction that is implemented — a tilted **solid**, viewed straight down, not a tilted **camera**. (Confirm this reading against T-0109's own task brief before implementing — the contracts do not fully disambiguate "tilt the solid" from "tilt the ray" and REF-HEIGHT-MATHS.md's Q1-3d-rotation.md research, in the adjacent T-0098 workspace though not one of this task's five contracts, argues for the solid-tilts/ray-stays-straight-down reading on cost grounds.)

---

## 8. The side-wall question

**Every contract-level (not design-doc) statement that a side wall would violate:**

| # | Assumption a side wall violates | Clause | file:line |
|---|---|---|---|
| 1 | **`edgeDistance` is assumed to exist and be meaningful for every published surface point**, and three fills/stages are built directly on it. | FC-1.3 (fill), BD-3.3 (border), and the general BC-3.3 #3 definition itself | `FILL-CONTRACT.md:91`, `BORDER-CONTRACT.md:146-157` |
| 2 | **Part 5 §1 of the buffer contract, verbatim:** "An extruded solid has side walls, and a wall has no 'how far in from the outline' value — yet `edgeDistance` is what the bevel band, the border region and the indexed-strip fill are all defined against. Three sensible rules exist (the wall inherits the rim value it descends from; the wall carries its own top-to-bottom parameterisation; the wall is a separately paintable surface) and one must be picked before fills and borders are built on top. **Recorded against the extrusion task (T-0109), not decided here.**" | BC (Part 5, item 1) | `BUFFER_CONTRACT.md:304` |
| 3 | **F8.2 restates it as a live fill-stage dependency:** "Gradient's `ByEdgeDistance` mode (FC-6.2) is directly affected: once walls are visible, that mode has no defined `t` on a wall until one of the three candidate rules is picked." | F8.2 | `FILL-CONTRACT.md:686` |
| 4 | **BD-3.3's whole clause assumes a well-defined `edgeDistance` on the region being painted** — "the `edgeDistance` sheet handed to a border's fill is the STRIP's, not the node's… `t = clamp01(−s(d) / depthPixels)`." A side wall is not "the strip" or "the node" in this sense at all; BD-3.3's polarity/normalisation logic has no defined input on a wall surface. | BD-3.3 | `BORDER-CONTRACT.md:146-157` |
| 5 | **LR-3.x's normal-provider machinery assumes a well-defined `dh/dd` gradient at every published sample** (Part 10 requirement 1, quoted in full in §7 of this extract below) — a wall breaks this too, and the light-rig contract is explicit that nobody has recorded it yet: "**The side wall's normal is a NEW consequence of the side-wall question that nobody has recorded.**… a wall's normal is in-plane (`nz ≈ 0`), so `rim` on a wall saturates to `rimStrength` and the diffuse term inverts sign relative to the top face. Whichever of the three side-wall rules is picked must state what the wall's normal is, not only what its `edgeDistance` is." | LR-9.3 (Part 10, "To T-0109", item 3) | `LIGHT-RIG-CONTRACT.md:756` |
| 6 | **BC-3.5's own definition of `depth` assumes a single-valued ray crossing**, and separately flags multi-span rays (which subtractive combines and a hollowed-out solid — B12's "hollow it out from the middle" — make ordinary) as needing a decision among "first span, sum of spans, or first-entry-to-last-exit," explicitly filed "beside the side-wall question in Part 5." Not the same question, but the same clause bundles them as siblings T-0109 must resolve together. | BC-3.5 | `BUFFER_CONTRACT.md:222` |

**Net: any wall-parameterisation ruling T-0109 makes must (a) pick one of the three candidate rules named in BC Part 5 §1, (b) simultaneously answer what the wall's *normal* is (LR-9.3, not previously connected to the edgeDistance question by any contract until now), and (c) be compatible with BD-3.3's strip-vs-node dual-anchor pattern if a border/bevel ever needs to paint a wall the way it paints a strip.**

---

## 9. The stepped profile

**No contract (BC-*, R*, FC-*, BD-*, LR-*) mentions discontinuous fields, step functions, or unbounded fields directly** — the closest is BC-2.6's general bound requirement and BC Part 5 §3's forward-reference. Quoted:

> **BUFFER_CONTRACT.md, Part 5 §3:** "**The stepped extrusion profile's ray bound.** B7 records that this profile family cannot be bounded at all and needs its own treatment. BC-2.6 requires a bound from every primitive; this family is the known exception and its treatment belongs to the extrusion task." `BUFFER_CONTRACT.md:306`

> **T-0105\SHAPE-ENGINE-SPEC.md:71** (not a contract, but the shipped shape engine's own spec, restating the same forward-reference): "curved tops, bevels and the stepped-profile family cost more or, for stepped profiles, cannot be bounded at all — B7/BC-2.6 flags that family as a known exception requiring its own treatment in the extrusion task, T-0109, not this one."

**No contract does the actual maths — that work is already done, but only in `REF-HEIGHT-MATHS.md` (this task's own reference doc, not a contract):**
- §D proves formally that no finite Lipschitz bound exists for either `stepped` variant (extrusion or bevel): "for any candidate `L`, take `t₁ = k/n − ε` and `t₂ = k/n`; the difference quotient is `1/((n−1)ε) → ∞` as `ε → 0`… no refinement of a measurement grid will ever converge" (`REF-HEIGHT-MATHS.md:256`).
- §D also finds `stepped` is not *uniquely* unbounded — `rounded`, `cove`, `ogee` and `round`/`dome` at their default parameters are unbounded too, differing only in that `stepped` is the sole *discontinuous* case among them (`REF-HEIGHT-MATHS.md:513,524`).
- §D.1–D.3 name three concrete options (slope-clamped variant with a formula and exact bound `L = n/((n−1)w)`; slab-by-slab marching with a `Breakpoints()` interface addition; explicit authoring-time exclusion on any path that steps along a ray) and states plainly "**Not my call.** Stated as three options with their costs; the decision is T4's" (`REF-HEIGHT-MATHS.md:290`) — i.e. this remains an open decision for T-0109 itself, not something any signed-off contract has ruled on.

**Conclusion for T-0109: no contract clause constrains the stepped-profile decision beyond "it must be treated, T-0109 owns the treatment, and BC-2.6's general bound requirement does not excuse skipping it."** The actual three candidate treatments and their exact bound formulas live in `REF-HEIGHT-MATHS.md §D`, not in any of the five signed-off contracts.

---

## 10. Contradictions and drift

| # | Where | Contradiction | Status / resolution |
|---|---|---|---|
| 1 | `SHAPE-ENGINE-SPEC.md:39` vs `BUFFER_CONTRACT.md:188` | Coverage clamped to `[0,1]` at the shape stage (shipped, `ShaperField.cs:54-61`) vs coverage "unbounded above… the publisher never clamps." | **Live, unresolved at the shape-stage level.** FC-1.6 resolves it *for fill-stage consumers only* ("treat as unbounded, clamp at use"). Not fixed in either source document — `FILL-CONTRACT.md:737` explicitly notes "Neither document is edited by this one; the resolution is scoped to consumers." T-0109 inherits this ambiguity if any height-profile maths reads coverage directly. |
| 2 | T-0105's two "corrections folded in" (bound-composition direction inversion; R7's bounding-box order-blindness) — **were they applied to the documents, or only to the code?** | `SHAPE-TREE-RULES.md`'s own closing section ("How this document was checked," `SHAPE-TREE-RULES.md:291-301`) states both were corrected **in the document itself** before sign-off — "R2's bound composition was inverted… the correct treatment is to rescale the published distance by the *smallest* [singular value]" and "R7's bounding-box rule ignored member order" are both listed as already-fixed blocking findings from the document's own adversarial review, not as later patches applied only to code. **Verified: applied to the document.** The shipped `ShaperBound.cs` and `ShaperProgram.cs` code independently match the corrected (not the wrong) versions — `ShaperBound.Transform` is a no-op pass-through per the corrected R2, and `ShaperBound.cs:41-43` explicitly calls out that a stale/wrong version of the soft-blend penalty "should not be re-introduced from a stale copy of this document," confirming the fix lives in both places consistently. No drift found between document and code on these two specific corrections. |
| 3 | `BUFFER_CONTRACT.md:246` vs `LR-3.1`'s dial block | BC-3.6 names three normal-calibration dials (`slopeGain=0.65`, `normalZBase=1.4`, `reflectionFlatten=1.5`) as needing to become "named, defaulted dials with their provenance in a comment. None of them is a magic number in the new code." | **No drift — already satisfied ahead of need.** `ShaperNormalOp` (`ShaperNormals.cs:53-60`) already carries all three, named, defaulted, with provenance comments, "unused until T-0109." T-0109 must wire them, not invent them. |
| 4 | `BUFFER_CONTRACT.md:305` (Part 5 §2) | "Whether `MatteBlur`, `MatteDisplace` and the per-layer post stage stay image-space or get shape-space equivalents" is explicitly listed as **not decided** by T-0102, and no later contract (T-0105 through T-0108) appears to have picked it up — not referenced in any of FC-*, BD-*, LR-* by ID. | **Open, and appears to remain open for T-0109 or later** — not verified as resolved anywhere in the five contracts read. Flag if T-0109's height/normal work touches per-layer post effects. |
| 5 | REF-HEIGHT-MATHS.md's own findings vs the T-0109 task brief that commissioned it | Ten numbered corrections at `REF-HEIGHT-MATHS.md:517-528`, most materially: quoted bound figures "round 16.4"/"dome 23.1" are **not maxima** (both profiles are formally unbounded at their default parameter, `curve=1`) and the quoted "taper 16.7 worst case" is wrong — the true worst case over the authored range is **20**, not 16.7. | **Drift between the task brief's prior figures and the re-derived maths**, resolved *in T-0109's own reference doc* (not a signed-off contract) rather than in any of the five contracts. T-0109 must use REF-HEIGHT-MATHS.md's corrected figures (Table J, `REF-HEIGHT-MATHS.md:491-513`), not any bound numbers that may still circulate in the original task brief. |
| 6 | `BUFFER_CONTRACT.md:222` (BC-3.5) vs the reference app's own naming | The reference app itself has a **naming collision** the contract explicitly does not inherit: in the ancestor tool the UI-labelled "Depth" slider is the Z *position*, while in 3D Shaper (the direct reference) "Depth" is thickness. BC-3.5 picks the 3D-Shaper/thickness reading for the `depth` quantity. `REF-HEIGHT-MATHS.md:310,424` (H5) documents this collision explicitly as a porting trap, not a contract contradiction — but worth flagging since it's exactly the kind of drift a build agent skimming only the ancestor source could reintroduce. | Not a contract contradiction; a documented porting trap. No action needed beyond awareness. |

**No other contradictions found between BC-*, R*, FC-*, BD-*, LR-* touching height, depth, Z, normals or bounds** — the five contracts are otherwise internally consistent and each new one (FC, BD, LR) explicitly reconciles itself against the ones before it (see each contract's own "Part 0" / "what changed while checking" section, which is where every genuine tension the authors found was already surfaced and argued rather than left implicit).
