# Shaper — the buffer contract

**T-0102, 2026-08-30. Wave 1. Written before any Shaper code exists, which is the only moment at which it is cheap.**

This document is normative. Where it says MUST, a task that violates it is wrong even if it works. Where it says MAY, the choice is genuinely free. Every factual claim about existing code carries a `file:line`, because the three rules below are being justified by what today's tool does, and a rule justified by a wrong fact is a rule that gets argued away later.

**Authority.** The design is the "Shaper — the design" tab of the Shaper node's planning document, also at `D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0098\SHAPER_THE_DESIGN.md`. This document does not re-decide anything that document decided; it turns three of its rulings — B9, B7, and the quantity table in B4/C3 — into a contract with signatures, conventions and tests. Where this document goes further than the design, it says so and marks the addition as a decision taken here.

**Scope.** This contract binds the *shape stage* and the *sheets it publishes*, plus the resolve that consumes them. It does not bind fills, borders, lighting or effects beyond stating what they are handed. It says nothing about the UI.

**Rule IDs.** Every normative clause has an ID (`BC-1.3`, `BC-2.1`, …). Cite the ID in task text and in code comments. A rule nobody can name is a rule nobody can defend.

---

# Part 0 — the three rules, and what changed while checking them

## The three rules

**BC-1 — Block fill.** A generator is handed a rectangular tile of the sample grid and flat numeric arrays to fill for it. It is never asked "what is at 4,7?" through a managed callback. *(design B9)*

**BC-2 — Implicit field, sampled.** A shape's published output is defined as an implicit field evaluated at a point in the layer's own space, and no stage before the resolve may read screen-space neighbours. The resolve is specified as a ray query in its general form and implemented in v1 as the straight-down closed form. *(design B7, C3)*

**BC-3 — A closed quantity vocabulary.** Exactly nine named per-pixel quantities exist. Each is defined here with its units, its range, its coordinate frame, and an honest statement of whether it already exists or is new construction. Surface direction is mandatory and is published by the shape, never differenced out of a depth buffer by the shading stage. *(design B4, C3, C6)*

## Six corrections to the brief and the design, found while verifying them

These are stated up front rather than buried, because two of them are numbers the brief asks the contract to quote.

1. **"6 of 29 generators" is 6 of 26.** The border stage's allow-list is `Disc, Crescent, Ring, Streak, Star, Polygon` (`Assets\Packages\Laubrary\Runtime\Pyre\PyreRenderer.cs:950-952`), gated at `PyreRenderer.Layers.cs:92`. The generator population is 17 pickable `ShapeForm` cases (`Editor\Pyre\PyreWindow.cs:1148-1162`) plus 9 concrete `PyreForm` plug-ins = **26**. The `ShapeForm` enum has 19 members but two are `[Obsolete]` retired slots (`Runtime\Pyre\Pyre.cs:114-115`). No counting reaches 29. **Use 6 of 26; 20 excluded, not 23.** The defect itself is confirmed and is worse than stated in one respect: all 9 plug-in forms are excluded *categorically* by the `layer.form == null` clause, and the editor keeps a second, hand-synced copy of the same allow-list (`Editor\Pyre\PyreWindow.cs:2670-2672`, used at `:1371`) with a comment claiming it "mirrors PyreRenderer.IsFlat2DBorderForm exactly".

2. **The picture-rect call is not called from nowhere — it is called from exactly one place, and never from Pyre.** It is `PostModifier.SetPicture(int sw, int sh, int px, int py)` (`Runtime\SpriteFx\SpriteFxModifiers.cs:1778-1779`), invoked once at `Runtime\SpriteFx\SpriteFxBurst.cs:656` inside the runtime sprite-filter path, and read by one class (`Runtime\SpriteFx\SpriteFxRelight.cs:472-474`). Pyre reaches `PostModifier` by reflection and resolves only `SetLife`, `SetSeed`, `SetFrameIndex` (`Runtime\Pyre\PyreRenderer.cs:1385-1387`), so inside every Pyre bake `pictureW == 0` and every post pass silently falls back to raw buffer dimensions — which the call's own doc comment predicts in as many words at `SpriteFxModifiers.cs:1774-1775`. **The cautionary example still stands, restated accurately: a contract call that one caller honours and the main caller does not know exists.** A sibling with the same shape is `PyreModifier.OutwardReachPx()` (`SpriteFxModifiers.cs:156`), overridden by five modifiers and called only from `SpriteFxBurst.cs:500`, so a Pyre bake never grows a margin for an outward-reaching post pass and clips bloom and outline at the canvas edge.

3. **"A 1.4 flattening term" is the floor of an undocumented third dial.** The reference app's expression (`D:\CODEZ\AgentHQ\3D Shaper\public\index.html:1491-1492`) is `normalZ = 1.4 + clamp01(material.reflection) * 1.5`, so a reflective material reaches `normalZ = 2.9` and roughly halves the tilt sensitivity of a matte one. There are **three** constants to name, not two. Details and the physical reading in BC-3.6.

4. **The publishing mechanism that "eight of nine effects use" is a debug hook, not a channel.** `IPlusFieldPublisher.PublishFields(Action<string,float[]> sink)` (`Runtime\Pyre\PyreForm.cs:156-159`) is called only when a sink is installed (`PyreRenderer.cs:352-359`), and the only installer in the project is the parity dumper `Editor\Pyre\Parity\PyreParityDump.cs:79`. In normal operation the planes are **not allocated at all** (`OrbForm.cs:497`, `PlasmaBloomForm.cs:437`, `TorchForm.cs:148`), the publish call **nulls the fields as it hands them over** (`ArcBurstForm.cs:427`, `JetFormBase.cs:78`, `OrbForm.cs:470`, `PlasmaBloomForm.cs:403`, `TorchForm.cs:123`), the names are free-form strings with no enum, and the coordinate frame differs per form (`JetFormBase.cs:143-150` flips explicitly; `OrbForm.cs:526` uses a flipped copy; `PyreForkBlast.cs:338` publishes un-flipped). The **8-of-9 and 4-of-9 counts are confirmed** (table in BC-3.3) but "coverage is a promotion" is too generous: it is four mutually incompatible definitions of alpha, produced only under a debug flag, in three different coordinate frames, destroyed on read.

5. **The design's claim that "every call site in Pyre passes zero for the fill's spatial point" is refuted as stated, and true where it matters.** `ZuiFill.Evaluate(float life, float u, float v)` (`Assets\Packages\Laubrary\Zui\Scripts\Runtime\ZuiFill.cs:167`) is fed **real per-pixel coordinates** at 23 `EvalFill` call sites in the built-in rasterisers (`PyreRenderer.cs:4987-4992`, e.g. the Disc at `:3000`); the 20 zero-passing sites are a deliberate hoist for non-spatial fills, guarded by `IsSpatialFill` at `PyreRenderer.cs:4976-4978`. The collapse **is** real for the Kiln composite forms, which bake a 1-D LUT by sampling at `(0.5, 0.5)` — not `(0,0)` — once per ramp entry (`Runtime\Pyre\PyreShade.cs:209`, `PyreForkBlast.cs:373`, `PyreInferno.cs:544`); the other seven forms refuse the fill entirely via `UsesFill => false`. **The half of the design's sentence that survives intact, and is the half this contract needs, is that `Evaluate`'s entire input is `(life, u, v)`: no coverage, no height, no edge distance, no named quantity.**

6. **Two of today's four "coverage" planes are not occupancy.** ArcBurst's `"alpha"` comes out of a bloom accumulator and is published *without* the `Floor` cut-off and *without* the `alphaMul` the real raster applies (`ArcBurstForm.cs:498-501` vs `:526`); Torch's `alpha_f` is captured before the layer-alpha multiply (`TorchForm.cs:209-213`). Treat all four as *a plane the form calls alpha*, not as coverage. Migrating them is conversion work, not wiring.

**Nothing in this contract depends on the incorrect versions of 1, 2, 3 or 5.** The corrections make the rules easier to defend, not harder.

---

# Part 1 — BC-1, the block-fill rule

## BC-1.1 The rule

> **A generator MUST be able to fill any rectangular tile of the sample grid, given only that tile's origin and size, by writing into flat arrays of primitive numbers supplied by the host. A generator MUST NOT be invoked once per sample through a managed callback, virtual method or delegate.**

## BC-1.2 The shape of the call

Stated as a contract, not as C#. The signature that satisfies it has these properties, all of them load-bearing:

- **One call per tile per quantity-set**, not one call per sample. The generator's own inner loop is inside the call.
- **The tile is `(x0, y0, w, h)` plus a row stride**, and the generator writes `w` contiguous values per row for `h` rows. The tile MAY be the whole grid; the contract is that the generator cannot tell the difference. **Qualified in review:** that is a statement about the generator's *output*, not about its working set, and it has to be, because a generator that genuinely needs the whole grid before it can answer for any sample — a global normalisation, a wrapped neighbour read like Orb's despeckle (`PyreOrb.cs:636-650`), a stateful sim — can satisfy it only by computing the whole grid and returning the requested window. That is bit-identical and therefore conformant, and it is also precisely the implementation BC-1.6 cannot see through. Whether the new generators are permitted to be that is not decided here.
- **The destination arrays are supplied by the host**, hold blittable primitives (`float`, or `float`×3 for a direction) in a container a Burst kernel can actually address, and are allocated once per document resolution rather than per frame. **Sharpened in review:** an earlier draft said only "flat arrays of primitive numbers", which is not sufficient for BC-1.4's justification — a Burst job cannot address a managed `float[]` at all, which is why the worked conversion this contract points at uses `NativeArray<T>` (`SpriteFxBurst.cs:443-457`). Which container type is a build decision; that it must be one Burst can take is not. The generator never allocates them, never replaces them, and never nulls them (BC-3.7).
- **Everything the generator needs to decide with is resolved before the call** into a blittable parameter block: dial values, seeds, clocks, instance index. No managed object is dereferenced inside the loop.
- **No delegate crosses the boundary.** Today's `Prepare(Func<ZUIValue,int,float>)` (`Runtime\SpriteFx\SpriteFxModifiers.cs:138`, closure allocated per modifier per layer per frame at `PyreRenderer.cs:1285`, `:1365`, `:291`) is exactly the shape this forbids: the value funnel must be evaluated *into* the parameter block up front, not called *from* inside it.

## BC-1.3 What this makes illegal that today's code does

Named, so that porting a stage is a decision rather than a habit:

- `PixelModifier.ApplyPixel(ref Color, ref float, in PixelInfo)` — abstract, called per pixel through virtual dispatch (`SpriteFxModifiers.cs:664`, loop at `PyreRenderer.cs:1348`), from 16 call sites in `PyreRenderer.cs` and 7 more inside plug-in forms that opted back in (`ArcBurstForm.cs:583`, `JetFormBase.cs:171`, `OrbForm.cs:573`, `PlasmaBloomForm.cs:506`, `TorchForm.cs:305`, `PyreForkBlast.cs:398`, `PyreInferno.cs:824`).
- `GeometryModifier.InverseWarp(Vector2, float, in GeoCtx)` — abstract, per pixel (`SpriteFxModifiers.cs:194`, loop at `PyreRenderer.cs:1338`).
- Per-pixel dereference of a managed `ZuiFill` holding a `Gradient` (`ZuiFill.cs:24`, called at `PyreRenderer.cs:4990-4991`).
- Reflection with boxing in the frame path: `_postSetLife?.Invoke(post, new object[]{ life })` (`PyreRenderer.cs:1390-1392`, repeated in the simulation path at `:1422`, `:1434`) — three boxed values and three arrays per post pass per layer per frame.
- `new System.Random(...)` inside the value funnel's MinMax branch (`PyreRenderer.cs:5034`), and the `[ThreadStatic] static int _layerSalt` it reads (`PyreRenderer.cs:173`), which a job kernel cannot address at all.
- Per-layer per-frame buffer allocation: `new Color32[W*H]` (`PyreRenderer.cs:231`), `(Color32[])target.Clone()` (`:1181`, `:1213`), `new float[W*H]` (`:1803`, `:1857`).

For completeness: **LINQ is not an offender.** `using System.Linq` appears exactly once in all of Pyre and SpriteFx, in `Editor\Pyre\PyreWindow.CherryFraming.cs:22`, which is editor-only and not in the bake path. Do not repeat the claim that it is.

## BC-1.4 Why this is a precondition and not an optimisation hedge

The design's B9 promoted Burst from "reach for it if things get slow" to a constraint on the plug-in contract. B7's measurement is why that promotion is load-bearing rather than tidy: **a tilted resolve costs roughly seven to eleven times a flat one**, and that multiple sits inside a responsive budget when the work is bulk arithmetic over flat arrays and outside it when the picture is assembled one dot at a time through managed calls. So BC-1 is the precondition for BC-2's door ever being walked through. If BC-1 is broken, BC-2 is decorative.

Two qualifications on the seven-to-eleven figure, carried forward from B7 verbatim in substance so it is not over-quoted later: it was measured on the simplest case — an exactly-measured shape, flat top, no bevel — so **read it as the best case rather than the typical one**; curved tops and bevels make the march advance more cautiously and cost more by an unmeasured amount, and the stepped profile family cannot be bounded at all and needs its own treatment in the extrusion task. And at the straight-down view the cost is **exactly today's**, because the query collapses into the one the tool already asks.

## BC-1.5 The honest distance from here to there

Of 26 generators today, **9 are already block-shaped** — `PyreForm.Render(in PyreFormCtx ctx, Color32[] target)` (`Runtime\Pyre\PyreForm.cs:224`) is called once per layer per frame and handed the whole buffer, with dials resolved up front in `Prepare` (`PyreForm.cs:206`). **17 are per-pixel** through two layers of managed virtual dispatch, via `DrawParticle` (`PyreRenderer.cs:2810`). And **every generator of either kind funnels its modifier stack back through `ApplyPixel` per pixel**, so the 9 that look compliant are not.

Two things already exist in the target shape and are worth reading before writing the new one rather than after: `Runtime\SpriteFx\SpriteFxBurst.cs` is a complete worked conversion — blittable `SfxOp` structs (`:41-60`), an enum kernel tag (`:33`), `SfxStackJob : IJobParallelFor` over `NativeArray<Color32>` (`:443-457`) — and `Runtime\Pyre\Forms\Kiln\ArcRaster.cs:37` is a real `[BurstCompile]` kernel. **Neither is wired into `PyreRenderer`.** `SpriteFxBurst.cs:26-29` also records honestly what it could not convert: `VoronoiCrack`, *all* geometry modifiers ("interwoven with rasterisation") and *all* post modifiers ("read neighbours"). The second and third of those are exactly what BC-1 and BC-2 exist to prevent recurring.

## BC-1.6 Conformance test

**Tile independence.** Render a document twice: once as a single whole-grid tile, once decomposed into tiles (a prime-sized decomposition, so tile boundaries land at awkward offsets — 7×5 is fine). The two outputs MUST be bit-identical. This runs in CI on every build.

This one test is worth more than its cost because **it also goes a long way toward enforcing BC-2 as a side effect**: a stage that reads screen-space neighbours produces a seam at a tile boundary, so it fails the test the day it is written rather than the next time somebody remembers to look. See BC-2.7.

**Weakened in review — the earlier draft said this *mechanically enforces* BC-2, and that is overstated in both directions.** It **under-fires**, because the test can only see a stage that is genuinely *executed* per tile with no access to samples outside it: a stage that computes the whole grid internally and hands back the requested window is bit-identical under any decomposition while still reading screen-space neighbours, and whole-grid-then-slice is the least-effort way to port every one of BC-2.5's six. It **over-fires**, because BC-2.4 declares neighbourhood work on the shape's *own* grid legal, and whenever that grid coincides with the sample grid — which BC-2.4 itself says is today's situation and today's whole trap — a legal stage seams under tiling and fails; Orb's despeckle, which reads its four neighbours *wrapped* around the grid (`PyreOrb.cs:636-650`), cannot pass a per-tile execution at all. So read the test as a strong, cheap, continuous signal, not as a proof, and expect both an exclusion list (BC-4.3) and hand review of what lands on it.

---

# Part 2 — BC-2, the implicit field and the resolve

## BC-2.1 The rule

> **A shape node's output is defined as a function evaluated at a point in the node's own local space. The published sheets are that function *sampled*, never the shape's definition. No stage before the resolve may read the screen-space neighbours of an intermediate buffer.**

The consequence the rule exists for: today the tool stores one height per screen dot, and that single slot is why it cannot turn — tilt a solid and one dot has to answer for both the top and the side. Keeping the *rule* rather than the *picture* is what leaves the door open, and it costs nothing at the straight-down view because there the two are the same question.

## BC-2.2 The resolve query, in its general form

> **Given a ray, report every layer the ray passes through and, for each surface it crosses, the depth at the crossing and the local coordinates of the crossing point on that layer.**

Three properties of that sentence are contractual and none is optional:

- **Every layer, not the front-most.** Recording the whole ordered set of crossings is what makes the occluded-outline coverage bitmap fall out of the resolve for free, instead of costing a second march per outlined layer later. It also replaces today's two-deep see-through approximation with real depth through as many layers as there are.
- **Depth at each crossing**, so that ordering, occlusion and the `depth` quantity (BC-3.3) all read from one place.
- **Local coordinates at each crossing**, because every downstream consumer — fill, border, indexed strip, bevel band — is defined against the layer's own space, not the screen's. This is what makes those stages survive a tilt word for word.

## BC-2.3 The v1 implementation

v1 implements exactly one ray direction: **straight down, orthographic**. For that direction the query has a closed form — it is algebraically the same point sample the current tool performs, at the same cost. There is no march, no acceleration structure and no camera in v1.

**The API is the general one from day one; only the implementation is special-cased.** A caller MUST NOT be able to tell that v1 only handles one direction by the shape of what it calls. The moment a caller can, the door has closed and nobody will notice.

## BC-2.4 What "before the resolve" means, precisely

Three categories, because the naive reading of BC-2.1 would forbid things that are perfectly safe and permit things that are not.

- **Legal — neighbourhood work in the shape's own domain.** A generator that runs a simulation, a blur or an advection on *its own* grid, whose resolution and orientation are properties of the shape rather than of the camera, is fine. Under a tilt that grid is a texture on a surface; neighbour still means neighbour. Everything in `Runtime\Pyre\PyreFieldOps.cs` (`GaussianBlur:34`, `BoxBlur:77`, `Smear:115`, `BinomialBlur:163`, `SmearIIR:194`, `Warp:250`, `Bloom:274`), the Orb soften and despeckle (`OrbForm.cs:521`; the despeckle's own 4-neighbour gather is `PyreOrb.cs:636-650`, and it reads **wrapped** around the grid, which is a whole-grid topology read — `OrbForm.cs:73-77`, cited here in an earlier draft, is only the authored dial block, not the algorithm), the ArcBurst bloom (`PyreArcBurst.cs:182-190`) and the fire sim's advection (`PyreFireSim.cs:215-216`) are legal **provided the grid is declared as the shape's own domain** and is not silently the screen. Declaring it is a real requirement, not a formality: today those grids happen to coincide with the canvas, and the coincidence is the whole trap.
- **Legal — image-space work on the resolved picture.** Bloom, chromatic aberration, drop shadow and the rest are honest screen-space image effects and belong after the resolve, where screen space genuinely is the output space. Today exactly one stage is in this position: the spec-level `globalModifiers` post loop inside `FrameComposer.Finish()` (`PyreRenderer.Layers.cs:345-360`).
- **Illegal — neighbourhood work on an intermediate screen-space buffer that is neither.** This is the category that bakes "neighbour in the buffer" = "neighbour on the surface" into a stage that will be wrong under any tilt.

## BC-2.5 Today's illegal stages, enumerated

The list is short and finite, which is the point of enumerating it — each of these is a decision the port has to take, not a surprise it can discover.

| # | Stage | Entry | The neighbour read |
|---|---|---|---|
| 1 | A layer's own PostModifiers | `PyreRenderer.Layers.cs:174` → `ApplyLayerPost` `PyreRenderer.cs:1354`, `post.Apply(buf,W,H)` at `:1366` | the whole neighbour-reading post family, below |
| 2 | Border / edge rim | `Layers.cs:172` → `BuildBorderBuffer` `PyreRenderer.cs:995` | `BorderInsideDistance` `PyreRenderer.cs:958-986` — two-pass chamfer distance transform over the layer's rasterised alpha |
| 3 | Luma-matte blur | `Layers.cs:309` → `ApplyMatte` `:1099` → `MatteBlur` `:1175-1200` | variable-radius box gather over a clone of the layer buffer |
| 4 | Luma-matte displace | same → `MatteDisplace` `:1208-1227` | central-difference gradient of the mask (`:1220-1221`) then a gather (`:1225`) |
| 5 | Ramp relief lighting | `PyreRenderer.cs:1804` | `PyreField.ReliefLight` `PyreField.cs:191-210`, 4-neighbour central difference at `:204-205` |
| 6 | Heightmap-consumer relief lighting | `PyreRenderer.cs:1858` | same `ReliefLight` |

The neighbour-reading post modifiers reachable from #1, all in `Runtime\SpriteFx\SpriteFxModifiers.cs` unless noted: `BloomModifier:1787`, `OutlineModifier:1887`, `ChromaticAberrationModifier:2102`, `BallisticShockwaveModifier:2200`, `FuseModifier:2459`, `EdgeSmoothModifier:2734`, `DropShadowModifier:3386`, `KaleidoscopeModifier:3481`, `DissolveModifier:1157`, and `RelightModifier` (`Runtime\SpriteFx\SpriteFxRelight.cs:243`, central difference at `:147-149`).

**Two of these six have a clean re-expression already implied by the design and should not be ported as-is:**

- **#2, the border.** The design already says a border derives a strip from a node's *coverage* (C5), and BC-3 publishes `edgeDistance` as an analytic, signed, shape-local quantity. So the border stops being a chamfer transform over rasterised alpha and becomes a threshold on a sheet the shape already produces. This removes the single most-cited decayed rule in the project (correction 1) at the same time, because a border defined on a published quantity applies to every generator that publishes it rather than to a hand-maintained allow-list of six.
- **#5 and #6, relief lighting.** These are the same violation seen from the other side of BC-3.6: the shading stage differencing a height buffer to invent a normal. Under BC-3 the normal is published, so the finite difference disappears rather than being ported.

**#1, #3 and #4 are genuine decisions**, and this contract does not take them. `MatteBlur` and `MatteDisplace` are per-layer image effects whose author may well have wanted screen-space behaviour; the per-layer post stage is a real feature (C8 is explicit that the stage an effect runs at stays an explicit property, and withdraws an earlier claim that the stages could be collapsed — **narrowed in review:** C8's distinction is per-*instance* versus finished-buffer inside a swarm, so what it forbids is collapsing stage positions in general; it does not name today's per-layer post stage, which sits on the finished-buffer side of that split). The contract's requirement is only that each is **declared** as an image-space stage operating on a resolved per-layer picture, and therefore **excluded from the tilt conformance render**, rather than sitting in the pre-resolve path by accident. See BC-4.3.

## BC-2.6 The edge-distance bound, which is the only expensive-to-retrofit clause here

> **Every primitive MUST declare a bound on how far it may over-state its own edge distance, and every transform and combination MUST compose those bounds.**

This is B7's third day-one requirement, restated as a contract clause because it is the one item in this document that genuinely cannot be added later. Today's shape rules answer "how far is the edge?" with a number that is sometimes an over-estimate — measured at 1.5× at a diamond's corner, over 2× on a slanted triangle side, 5× on a shape squashed along one axis. That has never mattered, because the current tool only asks which side of the edge a point is on and treats the magnitude as a shaping value. It matters completely the moment a ray uses that number to decide how far to advance: it strides through thin shapes and leaves holes.

The cost is **one number carried per node and one division** — on the order of twenty lines — **if it is written when the primitives are written**. Retrofitted, the only remaining safe march costs roughly forty times as much, which is the difference between a feature and an impossibility. Enforcement in BC-4.2.

## BC-2.7 Conformance tests

- **Tile independence (BC-1.6) is the primary, continuous check on BC-2.** A pre-resolve stage that reads screen-space neighbours seams at a tile boundary and fails on the build it is written on — subject to the two gaps recorded in BC-1.6, that a whole-grid-then-slice implementation passes anyway and that legal shape-domain neighbourhood work at canvas resolution fails anyway.
- **Bound conformance (BC-4.2).** Sampled per primitive, per transform, per combine mode.
- **The tilted conformance render.** Before Wave 2 closes, one deliberately tilted frame is rendered through the general resolve path and kept, re-rendered whenever the resolve changes. It is not a feature and is exposed nowhere. **That requirement is stated here and is DONE in T-0109**, which is where extrusion lands and therefore the first task that can tilt an actual solid rather than a zero-thickness sheet. **It is not satisfiable in T-0102 and must not be treated as an outstanding item against this task.**

**The strongest argument against the arrangement in this part, stated rather than hidden** (carried from B7): a path that runs every frame is a better guarantee than a path checked occasionally, and building the general resolve immediately would keep it correct because everything would depend on it. A kept picture only catches a break the next time somebody looks. Tile independence narrows that gap — it is continuous where the tilted render is periodic — but it does not close it, because tile independence cannot detect a resolve that is *correct and flat-only*, and because of the two gaps recorded in BC-1.6. That residual gap is real and is accepted knowingly.

---

# Part 3 — BC-3, the quantity vocabulary

## BC-3.1 The rule

> **The vocabulary of named per-pixel quantities is closed at nine. A shape node declares which it publishes. A consumer needing one the shape does not publish is greyed out with the reason shown — never hidden, never silently inert.**

Silent inertness is the single most-reported confusion in the existing tool (design B4) and it must not survive the rebuild.

## BC-3.2 What is a sheet and what is not

A **sheet** is one flat array over the sample grid, one `float` per sample (three for a direction). Colour is never a sheet in the shape stage — a shape publishes no colour at all, ever; that is what lets borders, bags, masks, previews and swarm compositing work without knowing anything about paint.

Three things a fill is handed (design B4) are **not** sheets and are out of scope for this contract: the instance index and the instance's own life, which are per-instance scalars in the parameter block (BC-1.2); the document clock; and the shape-local coordinate, which is not published because it *is* the sampling point and comes back from the resolve query (BC-2.2).

## BC-3.3 The nine quantities

Coordinate frame for every one of them is the **layer's own local space**, right-handed, +Y up, origin at the layer's own origin. Not the screen. Not flipped. The per-form flip inconsistency in today's code (`JetFormBase.cs:143-150`, `OrbForm.cs:526`, `PyreForkBlast.cs:338` disagreeing) is a defect this convention exists to end.

| # | Name | Type | Range / units | Status | What exists today |
|---|---|---|---|---|---|
| 1 | `coverage` | float | ≥ 0, nominally 0..1, **unbounded above** — a fog, not a stencil (C3). Consumers clamp at use, the publisher never clamps. | **Partial promotion, with conversion work** | 4 of 9 composites publish *a plane they call alpha* (table below), debug-gated, in 3 coordinate frames, under 4 incompatible definitions (correction 6). Ordinary generators compute it per pixel and discard it into the `Color32` alpha byte (`PyreRenderer.cs:2997`, `:3004`, `:3006`; the shared substrate at `PyreField.cs:113-125`). One consumer recovers it from that byte, at 8-bit precision, post-composite, for the matte path only (`WriteMatteCoverage`, `PyreRenderer.cs:908-919`). |
| 2 | `height` | float | layer-local units along the layer's own up axis; 0 = base plane | **New as a published quantity; existing consumers to satisfy** | Exists as a *cross-layer matte channel derived from the luminance of finished pixels* (`PyreRenderer.cs:912` with `useLuma`, read back at `:229-230`, consumed at `PyreRenderer.Layers.cs:168` / `PyreRenderer.cs:1849-1883`), and internally in the Ramp pass then dropped (`PyreRenderer.cs:1749`, used `:1804`). Never published by a shape. |
| 3 | `edgeDistance` | float | **signed**, layer-local units. **Negative inside, positive outside, zero on the boundary** (the standard SDF convention, and the reference app's own — it treats `distance>0` as outside and `-distance` as inside-ness, `index.html:1421`, `:1423`, `:1428`). | **Partial promotion with a semantic change** | `BorderInsideDistance` (`PyreRenderer.cs:954-986`) produces an **unsigned**, chamfer-*approximate*, image-space distance, read once at `:1025` and garbage-collected. Note the polarity, not just the missing sign: Pyre's number *grows inward*, so it runs the opposite direction from the convention above, and the band expression it feeds — `Clamp01(width + 1 − dist[i])` at `:1025` — inverts if it is ported literally onto a negative-inside quantity. One call site, so the trap is small, but it is real. No signed distance field exists for any Pyre shape — every "SDF" hit in Pyre is the TextMeshPro glyph atlas (`PyreRenderer.cs:3790`, `:4118`). |
| 4 | `heat` | float | 0..1 normalised | **Promotion** | Published as `"H"` by 6 forms (`ArcBurstForm.cs:424`, `ForkBlastForm.cs:139`, `JetFormBase.cs:75`, `OrbForm.cs:467`, `PlasmaBloomForm.cs:399`, `TorchForm.cs:119`). |
| 5 | `density` | float | 0..1 normalised | **New as a distinct quantity — see BC-3.4** | Does not exist separately. `"H"` doubles as both: `PyreForm.cs:155` documents it as *"H" (density/heat)*, and `PyreJetEngine.cs:266` calls the pair `// accumulated heat / soot`. |
| 6 | `soot` | float | 0..1 normalised | **Promotion, narrow** | Published as `"T"` by 2 form families only (`ForkBlastForm.cs:140`, `JetFormBase.cs:76`; source `PyreForkBlast.cs:338`). |
| 7 | `depth` | float | layer-local units — **the thickness of solid the sampling ray traverses at this sample** (entry-to-exit along the ray). See BC-3.5. | **New construction** | No plane by this name or meaning exists. Not to be confused with the reference app's two Z numbers, which sit on one line (`index.html:1362`): `layer.depth` is extrusion thickness and `order*0.75` is the layer's base Z. **Corrected in review:** an earlier draft attributed `layerOrder × 0.75` to Pyre. It is the reference app's, not Pyre's — Pyre has no layer Z at all, no `layerOrder` symbol and no Z offset; its layers simply composite in order. |
| 8 | `age` | float | 0..1, normalised lifetime of the material at this sample | **New construction** | No per-pixel age exists anywhere: no `float[]` named age/birth/lifetime in the project, and the stateful sims carry no age grid (`PyreFireSim.cs:63` — `float[] heat, fuel, heatB, fuelB;`; `SpriteFx\FireSim.cs:60`; `FireballSim.cs:37`). **Per-instance** age does exist and is a different thing — `PyreForm.cs:64-65` (`own`, `spawnLife`), `PyreForkBlast.cs:318-320` per gob, `ExplosiveJetProgram.cs:887-899` per puff. One palette comment describes a colour axis as age (`PyreShade.cs:419`) but the data it reads is soot. |
| 9 | `surfaceDirection` | float×3 | unit vector, layer-local | **New construction — and mandatory, see BC-3.6** | Published by nothing. **Computed and immediately collapsed to a scalar in five places**: `PyreField.ReliefLight` `PyreField.cs:191-210` (normal built at `:206`, dotted and discarded at `:208`); `SpriteFxRelight.cs:83-88,134`, whose own header at `:229-230` says it *"invents a surface out of the sprite's own content"*; `PyreInferno.cs:749`, a pseudo-normal from a density gradient; the analytic 3D solids (`PyreRenderer.cs:4414` sphere, `:4212` facet, `:4635` ring plane), consumed inline by Blinn-Phong; and `SpriteFxAuxMap.cs:171-203` Sobel, which throws the direction away at the `sqrt`. So "published by nothing" is right, and "does not exist" would be wrong — four of these already have the arithmetic a publisher needs. |

**The 8-of-9 and 4-of-9 counts, verified per form.** The nine are the concrete `PyreForm` plug-ins.

| # | Form | Publishes? | Coverage plane? | Quantities |
|---|---|---|---|---|
| 1 | `ArcBurstForm` (`Forms\Kiln\ArcBurstForm.cs:38`) | yes | **yes** — `:426` `sink("alpha", _dumpA)` | H, ramp_t, alpha |
| 2 | `ForkBlastForm` (`:15`) | yes | no — `:137-141` H and T only | H, T |
| 3 | `InfernoForm` (`:15`) | **no** — `: PyreForm` only; `PyreInferno.Render` (`:522`) has no publish parameter | no | none |
| 4 | `JetForm` (`Jet\JetForm.cs:28`) | yes (inherited) | no — `JetFormBase.cs:73-79` | H, T, ramp_t |
| 5 | `RadialJetForm` (`Jet\RadialJetForm.cs:26`) | yes (inherited) | no | H, T, ramp_t |
| 6 | `ExplosiveJetForm` (`Jet\ExplosiveJetForm.cs:44`) | yes (inherited) | no | H, T, ramp_t |
| 7 | `OrbForm` (`:36`) | yes | **yes** — `:469` `sink("alpha_f", _dumpA)` | H, ramp_t, alpha_f |
| 8 | `PlasmaBloomForm` (`:109`) | yes | **yes** — `:401` | H, ramp_t, alpha, rim_mix |
| 9 | `TorchForm` (`:39`) | yes | **yes** — `:122` | H, C, ramp_t, alpha_f |

**8 of 9 publish; 4 of 9 publish something they call coverage. Both design numbers confirmed.** Also worth carrying into the port: **7 of the 9 ignore the layer fill entirely** (`UsesFill => false` at `ArcBurstForm.cs:50`, `JetFormBase.cs:23`, `OrbForm.cs:51`, `PlasmaBloomForm.cs:121`, `TorchForm.cs:55`), so "give the composites a fill" is new capability, not re-wiring.

**Three names in today's code that are deliberately NOT in the vocabulary.** `ramp_t` is a palette lookup position, which is a fill concern rather than a property of the shape; `rim_mix` is `PlasmaBloomForm`-private; `C` is `TorchForm`'s cooled field (`TorchForm.cs:120`, `:221`). A generator MAY carry private extras internally, but they are not sheets, no standard fill may depend on them, and they may not be given vocabulary-looking names. **Extending the vocabulary is a design decision requiring a new task, not a new string literal** — the free-form-string mechanism at `PyreForm.cs:156-159` is precisely how a vocabulary stops being one. One candidate tenth is already named by the design and is recorded here so that "closed at nine" is not read as a claim that nothing else is coming: B4 says the fix for a plasma fill looking like paint on a fireball is *the shape publishing which way its surface is moving*, and calls that worth building and not free. It is not one of the nine, it is not in B4's own table, and if it arrives it arrives through the procedure in this paragraph.

## BC-3.4 Decision taken here: `heat` and `density` are two quantities, not one

The design's B4 table lists them separately; today's code fuses them into `"H"`. **This contract keeps them separate**, on the grounds that a shape that is dense and cold (smoke, dust) and a shape that is hot and thin (a flame tip) are genuinely different, a single channel cannot express either, and the cost of separating them is one extra declared sheet in the forms that care. A form that has only one field publishes it as whichever it actually means and declares the other unpublished — it does **not** publish the same array twice under both names. **Corrected in review:** an earlier draft called this a decision taken in T-0102 rather than a ruling inherited from the design, and called it the one place this contract adds a quantity rather than transcribing one. Neither survives checking — B4's table and C3's list both name `heat` and `density` as separate quantities, so the *separation* is transcribed like everything else in BC-3.3. What is actually decided in T-0102 is only the two clauses above: that a form holding one field publishes it under the one name it means, and that publishing the same array twice under both names is forbidden.

## BC-3.5 Decision taken here: what `depth` means

The design lists `depth` among the named quantities without defining it, and the word is heavily overloaded in this project (in the reference app, extrusion thickness *and* a separate layer base Z, both on `index.html:1362`; a matte channel in Pyre). **This contract defines it as the thickness of solid the sampling ray traverses at that sample — the entry-to-exit distance along the ray.** The reason to prefer that reading over any other is that it falls straight out of BC-2.2's crossing list at zero extra cost, it is exactly what a volumetric fill wants, and at the straight-down view it degenerates to the layer's own extrusion thickness at that point, which is the intuitive meaning anyway. **This is a decision taken in T-0102 and is the one open to being overruled cheaply**, since nothing is built yet. Two limits of the definition, found in review and recorded rather than resolved, because resolving either would be a design decision this task is not taking. First, **it is single-valued only while the ray enters and leaves once.** A node a straight-down ray enters and exits more than once — which a subtractive combine produces, and which B12's "hollow it out from the middle" control makes ordinary rather than exotic — has several spans at one sample, and `depth` must then be defined as the first span, the sum of the spans, or first-entry-to-last-exit. BC-2.2's crossing list carries all three, so the data is there and only the naming is open; it belongs with the extrusion task (T-0109), beside the side-wall question in Part 5. Second, **until extrusion exists every layer is a zero-thickness sheet**, so `depth` is identically zero and cannot be exercised at all in v1.

## BC-3.6 Surface direction is mandatory, published, and calibrated

> **The shading stage MUST receive a surface direction published by the shape. It MUST NOT derive one by differencing a depth or height buffer.**

This is what makes BC-2 real rather than aspirational, and it is what lets Silhouette's normal be replaced later without touching the shading law or touching Solids (design C6).

v1's Silhouette normal is still a finite difference of its own height field — that is fine, because it is the *height-field implementation's* private business rather than the shading stage's. What is not fine is leaving its constants anonymous. **They become named dials so that a later true-gradient normal has a calibration target rather than a memory of how it used to look.** There are **three**, not two.

The reference expression, verbatim (`D:\CODEZ\AgentHQ\3D Shaper\public\index.html:1491-1495`):

```js
let normalX=(sampleHeight(gx-1,gy,h)-sampleHeight(gx+1,gy,h))*0.65, normalY=(sampleHeight(gx,gy-1,h)-sampleHeight(gx,gy+1,h))*0.65;
let normalZ=1.4+clamp01(Number(material.reflection)||0)*1.5;
```

| Dial | Value | What it does |
|---|---|---|
| `slopeGain` | 0.65 | scales the raw central-difference height delta into the tangent-plane components |
| `normalZBase` | 1.4 | the fixed out-of-screen component the tilt is measured against |
| `reflectionFlatten` | 1.5 | added to `normalZBase`, scaled by the material's reflection — an existing, undocumented dial |

The vector is normalised three lines later (`index.html:1494-1495`, and for a gelatinous surface a wobble is added to X and Y in between at `:1493`), so **only the ratio `slopeGain / normalZBase` ≈ 0.464 sets how fast the normal tilts**: a central difference of one height unit tilts it by `atan(0.65/1.4) ≈ 24.9°`. **Weakened in review:** an earlier draft called that ratio the *maximum* tilt. It is not a maximum — the tilt keeps growing with the height delta, toward 90° — it is the sensitivity, the tilt per unit of height difference. Raising `slopeGain` or lowering `normalZBase` makes every slope read steeper and darken faster as it turns away; the reverse flattens the model toward a card. **They are not interchangeable**, because `normalZ` is also read on its own — after normalisation, but not through a dot product — by the Fresnel term (`index.html:1510`, `pow(1 - clamp01(normalZ), 2.2)`): raising `normalZBase` pushes normalised `normalZ` toward 1 and kills the rim highlight everywhere, whereas lowering `slopeGain` flattens the shading and leaves Fresnel largely intact. That asymmetry is the reason both must be dials rather than one combined ratio.

**Unity's own equivalent has different constants and must be reconciled, not silently replaced.** `PyreField.ReliefLight` (`Runtime\Pyre\PyreField.cs:191-210`) hard-wires `nz = 1f` and exposes the slope scale as the authored `relief` dial (`layer.rampRelief` / `layer.heightRelief`, used at `PyreRenderer.cs:1804`, `:1858`), with `ambient = 0.18f`, `gain = 0.82f`, light elevation `lz = 0.72f`, and a downstream remap of `0.35 + light × 1.15` (`PyreRenderer.cs:1867-1868`). Its header at `PyreField.cs:189-190` records that those came verbatim from `BlastRenderer.RenderHeightBalls` Pass 2. `SpriteFxRelight.ShadeAt` (`SpriteFxRelight.cs:147-149`) duplicates the expression deliberately, with `relief` defaulting to `4f` (`:278`) and `ambient` to `0.5f` (`:352`). **So the two lineages are mirror images** — the reference app hard-codes the slope and exposes reflection; Pyre exposes the slope and hard-codes `nz`. Both also hard-code the one-pixel edge-clamp artefact that reads double slope at the border, which `SpriteFxRelight.cs:141-146` documents and deliberately preserves. **All of these become named, defaulted dials with their provenance in a comment. None of them is a magic number in the new code.**

## BC-3.7 Publishing hygiene

Each clause fixes a specific defect confirmed in today's mechanism.

- **BC-3.7a — Declared, not discovered.** A generator declares its published set statically, before any render. The host allocates only the declared sheets, and only those a downstream consumer actually asked for. *(Today's set is discovered from whatever string literals a form happens to pass.)*
- **BC-3.7b — Always available, never debug-gated.** If a sheet is declared and requested, it is produced in normal operation. *(Today every plane is gated on `PyreFormDebug.FieldSink != null` — `ArcBurstForm.cs:495`, `JetFormBase.cs:132`, `OrbForm.cs:496`, `PlasmaBloomForm.cs:436`, `TorchForm.cs:147`, `ForkBlastForm.cs:187` — and is not even allocated otherwise.)*
- **BC-3.7c — Non-destructive.** Publishing hands over a view, not ownership. A sheet is readable any number of times by any number of consumers. *(Today `PublishFields` nulls the form's fields as it hands them over: `ArcBurstForm.cs:427`, `ForkBlastForm.cs:141`, `JetFormBase.cs:78`, `OrbForm.cs:470`, `PlasmaBloomForm.cs:403`, `TorchForm.cs:123`. One read only.)*
- **BC-3.7d — One coordinate frame.** BC-3.3's frame, for every quantity, from every generator. *(Today three frames disagree, and the parity dumper silently drops any plane whose length ≠ W×H — `PyreParityDump.cs:89`.)*
- **BC-3.7e — One definition per name.** `coverage` means occupancy at the sample, before any layer-alpha multiply and before any style curve. A form whose internal alpha is something else converts, or declares `coverage` unpublished. *(Today ArcBurst publishes a bloom accumulator without its `Floor` cut-off or `alphaMul`, `ArcBurstForm.cs:498-501` vs `:526`; Torch publishes before the layer-alpha multiply, `:209-213`; Orb publishes after its style's alpha curve, `PyreOrb.cs:613-625`.)*
- **BC-3.7f — Host-allocated, host-owned.** Consistent with BC-1.2: the generator never allocates, replaces, resizes or frees a sheet.

## BC-3.8 Conformance test

For every generator, a table-driven test asserts: the declared set matches the set actually written; every declared sheet is fully written across the tile with no unwritten samples; values are inside the declared range; and the frame convention holds (asserted by rendering a deliberately asymmetric shape and checking a known sample). A generator that declares a quantity it does not write fails the build.

---

# Part 4 — enforcement, because a rule with no test decays

## BC-4.1 Why this part exists

Two day-one rules in this same codebase quietly closed, and both are documented above with line numbers: the border stage, wired to 6 of 26 generators and **not even constructed in the UI** for the other 20, with the allow-list duplicated by hand in two files (correction 1); and the picture-rect call, honoured by one caller and unknown to the main one, with the failure predicted in its own doc comment (correction 2). Neither was a bad idea. Both were somebody's constraint, and neither had a test.

## BC-4.2 The check for each rule

| Rule | Check | Cadence |
|---|---|---|
| BC-1 block fill | **Tile independence** — whole-grid render vs a prime-sized tile decomposition, bit-identical (BC-1.6) | every build |
| BC-1 block fill | No delegate, boxed value or managed dereference in a generator's inner loop — the generator's entry point takes a blittable parameter block by value, so nothing managed can *arrive* through the parameter | compile time, partially — see below |
| BC-2 no pre-resolve neighbours | **Tile independence again** — a pre-resolve neighbour read seams at a tile boundary, with BC-1.6's two gaps reviewed by hand | every build |
| BC-2 resolve generality | Every caller goes through the general query signature; v1's straight-down specialisation is not reachable by a separate entry point | compile time |
| BC-2.6 edge-distance bound | Sample each primitive densely; assert `reportedDistance ≤ declaredBound × trueDistance` at every sample. Repeat for each transform and each combine mode against the composed bound | every build |
| BC-2 tilt | **One tilted frame rendered through the general path, kept and re-rendered whenever the resolve changes** | **DONE in T-0109**, before Wave 2 closes — see BC-4.4 |
| BC-3 vocabulary | Declared set == written set; full coverage of the tile; range; frame convention (BC-3.8) | every build |
| BC-3.6 normal | The shading stage's input is a published direction; grep-level assertion that no shading code path differences a height or depth buffer | every build |

**Two rows of that table were corrected in review, and both corrections matter.**

- **The bound assertion was the wrong way round.** The earlier draft asserted `declaredBound × reportedDistance ≥ trueDistance`. That is vacuous: the defect being bounded is *over*-estimation, so `reportedDistance ≥ trueDistance` already holds by construction, and multiplying the reported side by any `declaredBound ≥ 1` only makes a true inequality truer — a primitive that over-states by 50× passes a declared bound of 1.5 just as easily as one that over-states by 1.5×. The multiplier belongs on the *true* side: `reportedDistance ≤ declaredBound × trueDistance`, i.e. the primitive promises never to over-state by more than its declared factor, and the march's safe step is `reportedDistance / declaredBound` — which is exactly the one division B7 prices the whole guarantee at.
- **The blittable parameter block is not a whole compile-time check.** It closes one door: nothing managed can arrive *through the parameter*. It does not stop the body touching a static, allocating, or reaching a `[ThreadStatic]` — `new System.Random(...)` at `PyreRenderer.cs:5034` and `_layerSalt` at `:173` are both legal C# inside a method whose signature is entirely blittable. The thing that turns those into compile errors is actually compiling the kernel with Burst, which is also why BC-1.2's destination arrays have to be in a container Burst can address. Read the row as *compile time for the signature, build time for the body*.

## BC-4.3 The declaration that stops #1, #3 and #4 of BC-2.5 becoming loopholes

Any stage that reads screen-space neighbours **MUST** be declared as an image-space stage against a named resolved picture (the document's, or one layer's). The declaration has teeth in two places: such a stage is excluded from the tile-independence test by name, so the exclusion list is a visible, reviewable inventory of every place the rule is bent rather than a silence; and it is excluded from the tilt conformance render, so its output is never mistaken for evidence the door is open.

## BC-4.4 What is explicitly NOT satisfiable in T-0102

The tilted conformance render. It requires extrusion — the first thing that can tilt a solid rather than a zero-thickness sheet — which lands in **T-0109**. This task closes long before it can be satisfied. **Stating the requirement is this task's deliverable; discharging it is T-0109's.** It must not be logged as an outstanding item against T-0102.

---

# Part 5 — what this contract deliberately does not decide

Recorded so they are answered rather than discovered, which is the same reason design C9 exists.

1. **The side-wall parameterisation.** An extruded solid has side walls, and a wall has no "how far in from the outline" value — yet `edgeDistance` is what the bevel band, the border region and the indexed-strip fill are all defined against. Three sensible rules exist (the wall inherits the rim value it descends from; the wall carries its own top-to-bottom parameterisation; the wall is a separately paintable surface) and one must be picked before fills and borders are built on top. **Recorded against the extrusion task (T-0109), not decided here.**
2. **Whether `MatteBlur`, `MatteDisplace` and the per-layer post stage stay image-space or get shape-space equivalents.** BC-2.5 requires each to be *declared*; it does not require either answer.
3. **The stepped extrusion profile's ray bound.** B7 records that this profile family cannot be bounded at all and needs its own treatment. BC-2.6 requires a bound from every primitive; this family is the known exception and its treatment belongs to the extrusion task.
4. **Grid geometry.** The reference app's grid is non-square and follows the canvas aspect (`index.html:1373`), authorable 32–256 with a renderer clamp that actually accepts 16 (`index.html:1372`, `:1700`), defaulting to 96, capped at 68 000 cells (`index.html:982`, enforced `:1374`) — so the default grid is 96×64 ≈ 6 144 cells and the worst case ≈ 67 840. Unity's Pyre canvas is a single square dimension with no aspect concept and no cap (`Runtime\Pyre\Pyre.cs:1185`, `:1259-1260`). Every one of those figures is verified; **which convention Shaper adopts is a document-model decision, not a buffer-contract one.** Note in passing that the cell cap only ever shrinks height (`index.html:1374`), so the aspect-follows rule silently breaks at the cap — a bug to not reproduce.
5. **Whether the `Playback3D`, `Sprite`, `Fire` and `Fireball` enum forms count toward the "nine imported effects".** They are `ShapeForm` cases in the legacy if-chain rather than `PyreForm` plug-ins, none publishes anything, and this document excludes them. If the design's nine was ever meant to include any of them, the 8-of-9 arithmetic in BC-3.3 changes and this table needs re-running.

---

# Part 6 — one thing that is not a design item

The branch carrying the current Pyre — the whole parallel rebuild this contract's evidence was read from — **exists on no remote**. Every claim in this document was verified against files on one disk. This is the highest-priority item arising from any of the Shaper work and it has nothing to do with the design. *(Recorded in design B10; repeated here because a document that says "verified against source" is worth exactly as much as the source's survival.)*
