# T-0098 — Investigator J: Shaper as Pyre's only primitive engine

Research only. No code changed in either project. No Unity/Coplay used. Every load-bearing claim carries a `file:line`.

Sources read in full or in the cited region: `PYRE_GUG.md` §8–10, `PYRE_SHAPE_FILL_BORDER.md` §1–11, `C-shaper3d.md`, `A-generator-inventory.md` §2, `C:\Users\Lauta\.claude\skills\laubrary\references\ui-layout-rules.md` (full), `D:\Unity\UNITY_DEV_GUIDE.md` (searched), `Assets/Packages/Laubrary/Runtime/Pyre/{Pyre.cs, PyreRenderer.cs, PyreForm.cs, PyreSupersample.cs, PyreLayerCache.cs, PyreBlastPlayer.cs}`, `Assets/Packages/Laubrary/Editor/Pyre/PyreWindow.cs`, `Assets/Packages/Laubrary/Zui/Toolkit/{ZuiFillControl.cs, ZuiBox.cs}`, and 3D Shaper at `D:\CODEZ\AgentHQ\3D Shaper\public\index.html` (4103 lines) + `project_document.py`.

---

## Bottom line

- **The ruling is feasible, and the cost is far lower than either prior report implies.** Shaper's entire silhouette engine — 8 SDF primitives, the pulge warp, affine inverse-transform, the 4 combine modes with two-knob soft blending, the nested-tree compiler with its depth/parts guards — is **98 non-comment code lines** (`public/index.html:981-1141`). Extrusion + bevel registries add **26 more** (`:1142-1171`). It is dependency-free scalar maths: no DOM, no canvas, no async, no `UnityEngine.Object`. A C# port is ~124 lines, not a rewrite.
- **The seam is already the shape of an SDF.** Every Pyre flat form is *already* an inverse-transformed point test: `ResolveSample` folds spin and the geometry warps into a sample point (`PyreRenderer.cs:3105-3139`), then the form asks "is this point inside?" (`PyreRenderer.cs:2996`). Shaper does the identical thing (`matInvert` at `:1013`, `evalShape` at `:1128`). Swapping the inside-test for an SDF evaluation changes one expression per form, not the pipeline. **Pyre's disc and Shaper's ellipse are literally the same function**: `(hypot(x/rx,y/ry)-1)*min(rx,ry)` at `rx==ry==r` reduces exactly to `hypot(x,y)-r`, which is Pyre's `d - radius`.
- **Two of Pyre's six flat forms would be a regression if Shaper's primitives are taken as-shipped.** Shaper's star is a hardcoded 5-lobe cosine with **zero parameters** (`:1039`); Pyre's has 2–20 arms plus valley depth, valley position and skew, all per-particle animatable (`Pyre.cs:489-492`). Shaper has no arbitrary N-gon — its hexagon/octagon are cheap approximations (`:1036-1037`), not regular polygons; Pyre's Polygon is N=3..12 (`Pyre.cs:502`). Both are fixable with ~4 new lines each in `primitiveSdf`, which the code itself says is the extension point (`:1030`).
- **Sparkle and Streak are not primitives and must be exempted from the ruling, not ported through it.** Sparkle is a disc-bounded per-frame stochastic cell twinkle keyed on the particle's own-life frame bucket (`PyreRenderer.cs:3462-3548`) — it belongs in the **fill** kind. Streak is a rect plus a per-particle placement policy (anchor bias, swarm-facing forward axis, two independent length/width envelopes, the only bespoke branch inside the swarm loop at `PyreRenderer.cs:1531`) — its silhouette is trivially a Shaper `rect`; everything that makes it Streak is layer transform data, not shape.
- **The lit 3D solids must survive as a separate kind.** Shaper's "3D" is a height inferred from inside-distance, rendered from one fixed unrotatable direction (`renderModelGrid` reads only `lx/ly` and `inside`, `:1420-1424`). Pyre's Gem/Box/Pyramid/Can are real vertex/face geometry with real 3D rotation matrices and back-face culling (`PyreRenderer.cs:4182-4184, 4193-4198, 4207-4211`). A Shaper extrusion **structurally cannot be turned**. This is a capability gap, not a quality one.
- **Export is off the critical path. Report 1 §8's "until export exists, no integration of any shape is possible" is now false.** Verified: `grep -c toDataURL public/index.html` = 0; no download/blob/spritesheet/export hits in `index.html` or `server.py`. Export still does not exist — and under this ruling nothing crosses the process boundary, because what moves is 124 lines of maths, transcribed once, by hand.
- **The real risk is not technical.** `PYRE_SHAPE_FILL_BORDER.md` §10 records that Pyre has already shipped this exact class of idea three times (channel grouping: zero uses; melt-together radio: 20/305; the border stage: 1/305), and every one failed on *discoverability*, not design. A shape engine hidden behind a chip is candidate four. The disclosure design in §5 below is the whole risk, not a garnish.

---

## 1. Capability coverage, primitive by primitive

**Shaper's 8 primitives** (`public/index.html:981`, SDFs at `:1032-1041`): `rect` (with corner rounding), `ellipse`, `diamond`, `triangle`, `hexagon`, `octagon`, `capsule`, `star`. Each carries width/height/skew/cornerRounding plus a three-band pulge (pinch/bulge) warp (`compileBasicShape:1048`, `evalBasicShape:1054`).

**Shaper's combine ops** (`evalShape:1128-1140`): `add` = `min` (union); `subtract` = `max(d,-other)` (a true hole); `softAdd` = a compactly-supported power smooth-min with **two independent knobs** — viscosity→`k` (band half-width) and sharpness→`n` (falloff exponent, `blendExponent:1005` maps sharpness 0..1 to `n` 1..8); `subtractSoft` = a strength-scaled fraction of a hard subtract with a sine-weighted blend band. Nesting to depth 4 / 512 leaves (`:989`).

**Pyre's `ShapeForm` enum**: `Pyre.cs:111-117` — `Disc, Gem, Crescent, Sparkle, Sprite, Box, Pyramid, Can, Orb, Ring, Text, Streak, Star, Fire, Fireball, Polygon, [Obsolete] Inferno, [Obsolete] ForkBlast, Playback3D`.

### Matrix — flat-2D generated primitives (the population the ruling actually addresses)

| Pyre form | What it actually is (code) | Shaper reproduction | Verdict | What differs, precisely |
|---|---|---|---|---|
| **Disc** | `d = sqrt(dx²+dy²)`, reject `d > radius`, feather `inner=radius*(1-soft)` → `radius` (`PyreRenderer.cs:2916, 2996-2997`) | `ellipse` at `rx==ry` | **EXACT — bit-for-bit equivalent maths** | Nothing. `(hypot(x/r,y/r)-1)*r ≡ hypot(x,y)-r`. Shaper additionally gives free ellipse/eccentricity that Pyre's Disc has never had. |
| **Crescent** | Main disc minus an offset bite disc; bite size/angle are ZUIValues over the particle's own life, offset is a float (`Pyre.cs:432-434`); `edgeSoftness` feathers **both** rims (`DrawCrescentBody`, `PyreRenderer.cs:3140`) | `ellipse` `subtract` `ellipse` with a component transform | **EXACT geometry, APPROXIMATE animation** | Geometry is a strict superset (Shaper's bite can be any primitive, any transform, and can be soft). Lost without new work: `crescentBite`/`crescentAngle` are per-particle ZUIValues; Shaper component params are static numbers or model-timeline envelopes. Also Shaper has **no coverage feather at all** (see §3). |
| **Star** | N=2..20 arms (`Pyre.cs:489`), valley radius `R·(1−starLength)`, valley angular position `starBaseWidth`, pinwheel `starSkew` — all ZUIValues (`Pyre.cs:490-492`); two valleys per sector with a coincide guard (`DrawStarBody`, `PyreRenderer.cs:3254-3283`) | `star` — `lobe=0.5+0.5*cos(5*angle−π/2)`, `(hypot(x/rx,y/ry)−(0.42+0.58*lobe^0.8))*unit` (`:1039`) | **NOT AS SHIPPED — a real regression** | Shaper's star is **fixed at 5 points**, has **no parameters at all**, and is a smooth cosine lobe, not a straight-edged star polygon. Every one of Pyre's four star dials is absent. Fix = one new parameterised case in `primitiveSdf` (~4 lines; the code's own comment at `:1030` says "adding a primitive is one more case here plus one more entry in SHAPE_TYPES"). |
| **Polygon** | Regular convex N-gon, N=3..12 (`Pyre.cs:502`), per-ray inside test one edge per sector (`DrawPolygonBody`, `PyreRenderer.cs:3375`) | `triangle` / `diamond` / `rect` / `hexagon` / `octagon` | **APPROXIMATE, and only for N∈{3,4,6,8}** | No arbitrary N. Shaper's hexagon (`max(b−1, a+b*0.55−1)`) and octagon (`max(max(a,b)−1,(a+b)/1.42−1)`) are cheap axis-aligned approximations, not regular polygons — the constants `0.55` and `1.42` are visual fits. Fix = one true N-gon SDF case (~4 lines). |
| **Streak** | A **rect**, not a capsule: reject `|f| outside [−behind, ahead]` and `|s| > halfW` (`PyreRenderer.cs:3752-3757`). Anchor bias puts the particle at `streakAnchor` along it (`Pyre.cs:470`); forward axis = swarm `orientDeg` + spin, defaulting to +90° when unoriented (`:3690-3694`); **ignores `size` entirely** in favour of its own `streakLength`/`streakWidth` envelopes; `streakSoftTip` feathers both ends, `edgeSoftness` the sides; `streakScaleLengthOnly` is the **only bespoke branch inside the swarm loop** (`PyreRenderer.cs:1531`) | `rect` (or `capsule`) | **SILHOUETTE EXACT — but Streak is 90% not-silhouette** | The outline is trivially a Shaper rect. Everything that makes Streak *Streak* is per-particle transform + envelope policy: anchor bias, orientation binding, two-axis independent sizing, the axis-selective swarm scale. None of that is expressible in a shape document; all of it must stay on `PyreLayer`. **Reclassify: Streak = rect primitive + a Pyre placement policy.** |
| **Sparkle** | **Not a silhouette.** A virtual grid of `sparkleSize`-px cells over the disc; a cell lights iff a stable presence hash < density **and** a per-frame twinkle hash passes, where the frame salt is the particle's **own-life** frame bucket (`PyreRenderer.cs:3462-3548`, notably `:3489` and `:3524-3527`). Lit cells are hard full-colour, **no edge falloff**; explicitly ignores `edgeSoftness` (`PyreWindow.cs:1320-1322`) and is excluded from the border stage (`PyreRenderer.cs:950-952`) | none | **NOT AT ALL, and correctly so** | Shaper's shape stage has **no time dimension whatsoever** (confirmed: `evalShape` takes only `(compiled, x, y)`, `:1128`) and no stochastic anything. Sparkle's whole content is a per-frame random draw over an interior. It is a **fill/fill-op over a Disc**, matching `PYRE_SHAPE_FILL_BORDER.md` §2's Fill kind. Routing it through a shape engine deletes it. |
| **Ring** | Analytic two-sided **tilted** annulus reusing the 3D solids' point-light + Blinn-Phong, edge lines and glows (`Pyre.cs:55-58`; `DrawRing`, `PyreRenderer.cs:4609`); outer = `size`, hole = `size·ringInner`; `gemTilt` opens/closes the ellipse; deliberately ignores swarm `orientDeg` (`PyreRenderer.cs:2864-2866`) | flat annulus = `ellipse` `subtract` `ellipse` | **SILHOUETTE YES, LOOK NO** | The flat outline is easy CSG. The tilt-as-3D and the two-sided lit surface are the solids' renderer, which Shaper has no counterpart for. UI already treats it inconsistently — it is in the 2D picker row (`PyreWindow.cs:1155`) but built with `BuildSolidBox` (`PyreWindow.cs:1324-1331`). **Belongs with the solids, not the primitives.** |

### The non-primitives, for completeness (all outside the ruling)

| Form | Why it is not a generated primitive |
|---|---|
| **Sprite** | Shape and colour are the same external artwork; there is no scalar a fill could read (`DrawSpriteBody`, `PyreRenderer.cs:3553`). `PYRE_SHAPE_FILL_BORDER.md` §6 calls it the one genuinely inseparable generator. |
| **Text** | External TMP SDF atlas; already owns Pyre's **only** worked extrusion and its own border (`RenderTextLine:3859`, `DrawTextChar:3925`). |
| **Fire / Fireball** | Stateful grid simulations, no swarm, isolated scratch buffers (`PyreRenderer.cs:258-261`). |
| **Playback3D** | Editor-preview only; the runtime path **draws nothing** and says so (`PyreRenderer.cs:262-269`: "A spec using this form renders NOTHING here"). |
| **Inferno / ForkBlast** | Retired enum slots, `[System.Obsolete]` (`Pyre.cs:114-115`). |

### What Shaper adds that Pyre has no equivalent for

Arbitrary composed silhouettes with **true holes** and **two-knob soft blending**, at up to 4 nesting levels — `PYRE_GUG.md` §8 states Pyre has "no equivalent at all" and that is confirmed: `ShapeForm` has no combine concept, and the nearest thing (channel-based grouping) harvests only alpha from fully-coloured members, per `PYRE_SHAPE_FILL_BORDER.md` §9. Also: `capsule` and `diamond` as first-class primitives, the three-band pulge warp (`:1054-1059`), per-component skew/rotate/scale/origin (`nodeMatrix:1019`), and a 7-profile extrusion × 5-profile bevel registry (`:1144-1171`).

---

## 2. The lit 3D solids

**Recommendation: the ruling should NOT extend to them. Keep Gem / Box / Pyramid / Can / Orb / Ring as their own kind, and rename the ruling's scope to "2D primitive silhouettes".**

**Where Pyre wins, structurally (not by degree):**
- Real 3D rotation of real geometry. `BuildBoxGeometry`/`BuildPyramidGeometry`/`BuildCanGeometry` build vertex/face models (`PyreRenderer.cs:4182-4184`), a `Rot()` closure composes actual yaw/tilt/roll matrices (`:4193-4198`), and faces are culled by normal dot-product (`:4207-4211`). `particleSpin` is reinterpreted as **yaw about the vertical axis** per particle (`Pyre.cs:645-647`).
- Orb's silhouette is invariant under spin while its *lighting frame* rotates, so the hotspot rolls around the ball (`Pyre.cs:51-54`). No height-map technique can produce that.
- Per `PYRE_SHAPE_FILL_BORDER.md` §6, the solids already carry **five separate fills apiece** plus a full authored light rig — the code itself calls the first one the material fill.

**Where Shaper wins:** composed silhouettes get depth for free via `extrusionHeight` × `bevelFactor` (`:1144-1171`), an occlusion-aware outline that a *buried* layer can paint onto whatever covers it (`renderModelGrid`'s per-layer coverage bitmaps, `:1402-1411`), and a smooth-max fusion band that welds touching bodies into one object (`:1440-1445`). Pyre has none of these.

**The structural fact that decides it:** Shaper's renderer is a fixed-view height-map compositor. `renderModelGrid` samples only `(lx, ly)` and derives `inside = clamp01(-distance/span)` (`:1420-1423`); there is no camera, no view matrix, no rotation of the extruded body. A Shaper "box" is a rectangle with a bevel profile seen from exactly one direction, forever. That is a genuinely different technique, not a weaker version of the same one.

**The consolidation that IS worth doing** (and which `PYRE_SHAPE_FILL_BORDER.md` §6 already frames correctly): port Shaper's `extrusionHeight`/`bevelFactor` as a **shape operation** (26 code lines) so any silhouette gains height, and have the shape stage export a surface normal so the *solids' existing light rig* can light a Shaper-composed silhouette. That gets the owner "Shaper handles the flat stuff and can look solid" without deleting real 3D rotation. The open decision named in that report — does the light belong to the shape or the fill — is unchanged by this ruling and still needs the owner.

---

## 3. What rides on Pyre's primitives that a Shaper silhouette must be given

| Rider | Where it lives in Pyre | Can an SDF silhouette carry it? |
|---|---|---|
| **Edge softness / feather** | `inner = radius*(1f - soft)` then `edge = d <= inner ? 1 : 1 − InverseLerp(inner, radius, d)` (`PyreRenderer.cs:2916, 2997`). Per-form: Disc rim; Crescent **both** rims; Streak sides via `sideInner` + both tips via `streakSoftTip` (`:3706-3714`); Star/Polygon radial rim. Animatable over life via `edgeSoftnessAnim` (`:2914`). Default 0.4 (`Pyre.cs:327`). | **YES, and better.** Shaper today has **no feather at all** — coverage is a binary `if (distance>0) continue;` at one sample per cell (`:1421`), and materials have no alpha field (`project_document.py:343-347`). But a *signed distance* is exactly the input a feather wants: `coverage = clamp01(0.5 − d/feather)` reproduces every one of the above with **one** expression instead of five per-form ones, and generalises to composed silhouettes, which the current hand-written per-form feathers cannot. **Net simplification.** |
| **Anti-aliasing** | **There is none.** The inside test is a hard reject (`:2996, 3074, 3196, 3518`); the only edge gradient is the authored feather, so `edgeSoftness = 0` gives a stair-stepped circle. `PyreSupersample` exists (`PyreSupersample.cs:24`) but is **opt-in for `PyreForm` plug-ins only** — `PyreRenderer.cs` contains zero references to it. | **YES, free gain.** Same `clamp01(0.5 − d/px)` gives analytic coverage AA at no cost. |
| **Sub-pixel positioning under the swarm** | `dx = x + 0.5f − cx` with `cx` a float carrying the particle's continuous position (`PyreRenderer.cs:2988`). | **YES for a live evaluator** (an SDF is evaluated at a float point; Shaper already inverse-transforms per sample, `matX/matY:1016-1017`). **NO for a baked bitmap** — this is where option (a) in §4 fails. |
| **Per-pixel effect hooks** | `ApplyPix(mods.pix, ref pc, ref pa, x, y, wx, wy, frameIndex, crossFrac, life, pHash, W, H)` — six call sites, each computing `crossFrac = clamp01(d / max(0.001, radius))` in its own way (`:3090, 3217, 3356, 3454, 3539, 3618`). | **YES, and it fixes a known defect.** `PYRE_SHAPE_FILL_BORDER.md` §4 flags that this value currently "means five different things". A normalised SDF gives one definition — signed distance / extent — for *any* silhouette, composed or not. |
| **Geometry modifier hooks (14 invertible warps)** | `ApplyGeo` is an **inverse** warp applied to the sample point *before* the inside test; `ResolveSample` (`PyreRenderer.cs:3105-3139`) folds spin then geo, and an active warp forces a whole-canvas scan (`:3049`). | **YES, verbatim.** This is the single strongest structural argument: Pyre's shape drawing is *already* "inverse-map the sample point, then ask if it is inside". Shaper's evaluator has exactly that signature. The fold is unchanged; only the inside-test expression is replaced. |
| **The border stage** | `BuildBorderBuffer` (`PyreRenderer.cs:996`) reads the **rendered coverage buffer** and chamfer-distance-transforms it (`BorderInsideDistance:958`). It is already silhouette-agnostic — its own comment says it "rims whatever silhouette the layer drew (a single shape, or the union of a whole swarm)" (`:944-947`). | **YES, for free.** The only change is adding the new form to `IsFlat2DBorderForm` (`:950-952`). It would work on a composed silhouette on day one — and an SDF makes the chamfer approximation replaceable by the true distance. |
| **Fill coordinate space (stamped vs canvas)** | `EvalFill(f, life, lu, lv, px, py, W, H)` — `FillSpace.Fixed` uses canvas-anchored `-1..1`, otherwise the caller's shape-local `(lu,lv)` (`PyreRenderer.cs:4987-4992`). Every flat form feeds `(dx/radius, dy/radius)`. | **YES, with one decision.** A composed silhouette has no single "radius". `compileShape` already returns `halfW`/`halfH` (`:1113, 1136`) — normalise by those. Note the documented caveat there: an off-axis-rotated nested child's extent is a transform-corner box, ~2.5× larger at 45°, so the local space would be slightly loose for rotated composites. |
| **Per-particle shape animation (ZUIValues)** | Every shape dial is a `ZUIValue` evaluated per particle at that particle's **own life**: `Eval(layer.starLength, life, spec.seed, particleIndex, FldStarLen)` (`:3262`), same for `crescentBite`, `sparkleDensity`, `streakLength/Width`, `edgeSoftnessAnim`, `size`, `alpha`. | **NO — this is the real new work.** Shaper's shape params are plain numbers (`compileBasicShape:1048` coerces with `Number(...)`). Every parameter Pyre wants animatable must become a `ZUIValue`, and `compileShape` must either run per particle per frame or be split into a static structural compile plus per-sample parameters. This is the one item on this list that is genuinely a build, and it is also the item that decides the performance story in §6. |
| **Swarm transforms: `orientDeg`, `particleSpin`, `sizeMul`, `brightMul`, `lenByIndex`** | Folded into the sample point / colour before the shape test (`:3035-3047`, `:2802-2808`). | **YES.** These are an affine transform plus two scalars; Shaper's `nodeMatrix`/`matInvert` path is the same operation. `lenByIndex` (Streak-only, axis-selective) is the exception — see §6. |
| **Determinism + threading contract** | Frames render concurrently on worker threads, each on its own deep clone; a form must be a pure function of `(ctx, fields)`, must not keep mutable statics, must never touch a `UnityEngine.Object` (`PyreForm.cs:12-20`). | **YES.** Shaper's evaluator is pure scalar maths with no I/O and no shared state. Fully compliant. One caution: `compileShape` allocates arrays per call — a per-particle compile would be GC churn on a worker thread and must be pooled or restructured. |
| **Content-addressed frame caching** | `PyreLayerCache` keys a rendered buffer by layer content + index + frame variant (`PyreLayerCache.cs:3-10`). | **YES, unchanged** — provided the shape document participates in `PyreLayerKey`'s content hash. |

---

## 4. The representation question

### The constraint that eliminates half the option space

**Pyre renders every frame at runtime, in C#, through the same `PyreRenderer`.** `PyreBlastPlayer` "shows a Pyre with **no baked assets**: it renders every frame to an in-memory Sprite through the shared PyreRenderer (so it matches the editor preview and any bake)" (`PyreBlastPlayer.cs:7-9`). So whatever the primitive engine is, **it must exist as pure C# in the `Runtime/Pyre` asmdef**. Anything that requires a browser, a Python server, or a pre-baked file at play time is not a candidate for the *engine*; at best it is a candidate for the *authoring tool*.

### (a) A baked silhouette / coverage bitmap produced by Shaper

- **Must be built:** a Shaper export path (**does not exist** — 0 hits for `toDataURL`, no download/blob/spritesheet/export in `index.html` or `server.py`); a Unity import + asset type; a re-bake-on-edit invalidation story.
- **Lost:** sub-pixel edge under the swarm (a bitmap resamples); **all per-particle shape animation** (a bitmap is one shape — `starLength` over own life, `crescentBite` over own life, animated `edgeSoftness` all die, and each distinct parameter set would need its own bake, an unbounded count); rotation quality (`particleSpin` is per-particle, so every particle needs a filtered rotate); and the fill/coverage separation (a baked picture is colour, which is what makes `ShapeForm.Sprite` "genuinely inseparable" per `PYRE_SHAPE_FILL_BORDER.md` §6).
- **Swarm cost:** cheapest per pixel (one array fetch, as `DrawSpriteBody` already does at `PyreRenderer.cs:3553`) — but it is *already* `ShapeForm.Sprite`. This option does not add a primitive engine; it adds a second sprite path.
- **Precedent against:** `Playback3D` is the existing "import external content as a ShapeForm" experiment and its runtime renders **nothing** (`PyreRenderer.cs:262-269`). `PYRE_GUG.md` §8 itself says "Don't build a second one." **Reject.**

### (b) A live C# port of Shaper's evaluator inside Pyre — **RECOMMENDED**

- **Must be built, measured from source:**
  - `primitiveSdf` — 8 cases, 9 lines (`:1032-1041`).
  - `compileBasicShape` + `evalBasicShape` (skew, rounding, 3-band pulge) — 7 lines (`:1048-1060`).
  - `smoothMin` / `smoothMax` / `smoothMinShaped` / `blendExponent` — 5 lines (`:998-1006`).
  - Affine `matMul` / `matInvert` / `matX` / `matY` / `nodeMatrix` — 6 lines (`:1013-1029`).
  - `compileShape` (nested tree, depth cap 4, shared 512-leaf budget, extent accumulation) + `evalShape` (4 combine modes) — ~35 lines (`:1090-1141`).
  - **Silhouette engine total: 98 non-comment code lines, `public/index.html:981-1141`.**
  - Optional shape-op: `extrusionHeight` + `bevelFactor` — **26 code lines** (`:1142-1171`).
  - **Not ported:** `renderModelGrid` (214 code lines, `:1371-1583`) — that is Shaper's *material and lighting* pipeline, a rival to Pyre's own. The arc-length edge-ring machinery (134 lines, `:1173-1370`) serves Shaper's pixel-edge feature and is out of scope.
  - **Plus, in Pyre:** a serializable `PyreShape` data model, ZUIValue-ification of the parameters Pyre wants animatable, participation in `PyreLayerKey`'s content hash, and the editor UI of §5.
- **Lost:** nothing at the silhouette level. Shaper's material surfaces, patterns, plasma, emission glow and shadow ray-march are *deliberately* not brought over — Pyre has its own Fill.
- **Swarm behaviour, concretely.** Per candidate pixel the cost is: one affine inverse-transform (6 mul + 4 add) + `evalShape`, which is O(leaves) with each leaf costing another inverse-transform plus one `primitiveSdf` call. **A one-leaf ellipse costs the same arithmetic as today's disc** (`hypot` vs `sqrt(dx*dx+dy*dy)`), so the trivial case — the case the owner insists must stay simple — is free. An 8-leaf composite is ~8× the per-pixel arithmetic *of the shape test only*, inside the same bounding box (bounds come from `compileShape`'s `halfW`/`halfH`, `:1136`). At Pyre's defaults (64px canvas, 16 frames — `Pyre.cs:1185-1186`), and with `PyreLayerCache` making the render once-per-(layer, frame), this is not the dominant term. It becomes the dominant term only in the case named in §6.
- **Fits every rider in §3** without a translation layer, because the evaluator's signature is already the signature `ResolveSample` produces.

### (c) A Shaper document as a Unity asset, evaluated by a shared runtime

This is (b) **plus** an asset type and a load path. It buys **reuse** (one silhouette used by many layers, edited once) — genuinely valuable, and it is the natural home for the nesting `PYRE_SHAPE_FILL_BORDER.md` §9 says should live "inside *that* asset, not inside Pyre's layer list". But it does not answer the primitive-engine question; it answers a later reuse question. Shipping it first adds a load path, a picker, and a broken-reference failure mode to the *simplest* case, which is exactly what the owner's ruling forbids.

### (d) The recommendation

**Option (b), with the shape document embedded on the layer, and (c) reserved as a later opt-in.**

- `PyreLayer` gains a `PyreShape shape` — an ordered list of components, each = `{ primitive, mode, transform, viscosity, sharpness, params }`. A brand-new layer gets **one** component: an ellipse at `rx == ry`. That is a disc, exactly, by identical arithmetic.
- The evaluator lands as a static `PyreShapeSdf` in `Runtime/Pyre/` — pure, thread-safe, no `UnityEngine.Object`, satisfying `PyreForm.cs:12-20`.
- **Dispatch it as a new `ShapeForm` enum case, not as a `PyreForm` plug-in, for the first slice.** `PyreFormKind.PerParticle` is declared but explicitly **"the renderer does not dispatch it yet"** (`PyreForm.cs:31-32`), so the plug-in route requires building per-particle dispatch first; a new enum case slots into the existing `DrawParticle` if-chain (`PyreRenderer.cs:2843-2884`) alongside Crescent/Star/Polygon with zero new dispatch machinery.
- Later, `PyreShape` becomes serialisable to a `LauAsset` for reuse — option (c) as an upgrade, not a prerequisite.

---

## 5. Progressive disclosure

### Does the guide already have this rule? Partly — say which parts, and do not re-invent them

I read `ui-layout-rules.md` in full and searched `UNITY_DEV_GUIDE.md`. **There is no progressive-disclosure rule.** What exists and *constrains* this design (cite these, do not compete with them):

- **"Stable workspace — contextual UI must NEVER move what the user is working on"** — contextual UI must not reflow the layout around the workspace; the sanctioned shape is `Z.Split(stateKey, …)` with controls left and workspace right, "so left-pane churn cannot move the canvas at all."
- **"Label = action"** — a control labelled with an action verb must perform it; if it opens more UI, name it for the destination with a trailing ellipsis. Preference order is explicit: **inline the choices where the label is** (rule 1) over opening a chooser (rule 2).
- **"Card layout" #5** — "A card with nothing but a name and one field is ONE row." Decide by *counting the fields*, so a new one-field kind gets it automatically.
- **"Labeling — tooltip, not title"** — no on-screen instructional prose; explanations go in tooltips.
- **`UNITY_DEV_GUIDE.md:96`** — "Don't add surface the user didn't ask for."

**Existing built precedents in this codebase** (the pattern the owner is describing already ships three times over):
- `ZuiFillControl` — "one labelled header row whose compact body changes with the fill's ACTIVE thing … Acts as a plain colour picker in Solid mode, grows a gradient + a centre pad + angle/zoom in the spatial modes" (`ZuiFillControl.cs:1-7`). **This is exactly the owner's ask, applied to Fill.**
- `ZuiBox` gear accordion — opt-in controls hidden from the body, recoverable via a ⚙ in the title row; "a box nobody calls Toggleable/ToggleGroup on is byte-for-byte the old plain box (no gear, no accordion, no layout shift)" (`ZuiBox.cs:14-24`).
- `PyreSwarmOnlyAttribute` — "The editor hides it while the swarm is off, so the card never shows a dead control" (`PyreForm.cs:53-55`).
- Pyre's own form picker already lives behind a caret / right-click on the Shape box title (`ShowShapeMenu`, `PyreWindow.cs:1191-1199`), and the Shape box already hides dials that don't apply (Size hidden for Streak/Fire/Fireball/Playback3D at `PyreWindow.cs:1315-1318`; Edge hidden for the solids/Sparkle/Sprite at `:1320-1322`).

### The concrete design

Today's Shape box for a Disc is: **Fill · Alpha · Size · Edge**, plus the Border box (`PyreWindow.cs:1300-1371`). The disclosure design must not add a row at rest.

**L0 — creation.** "New layer" produces a layer whose shape is one ellipse component at `rx == ry`, one flat fill. No Shaper vocabulary appears anywhere. **Layer row** shows what it shows today: enable toggle, name, and the shape glyph — the glyph resolves from the shape's *first* component (a circle icon for a one-component ellipse), so a plain layer still reads "disc" at a glance.

**L1 — the Shape box, always visible, unchanged count of rows.** `Fill · Alpha · Size · Edge` exactly as today, **plus the existing form-picker caret**, whose meaning changes from "pick 1 of 18 forms" to "pick 1 of 8 primitives + the non-primitive families". The primitive's *own* parameters (star arms/length/skew, polygon sides, rect rounding, pulge) stay **here at L1**, because they are the shape's identity, not its composition — this is the decision that keeps Star as fast to author as it is today (see §6).

The one addition: the form-picker row gains a trailing **`Edit shape…`** affordance (ellipsis, per "Label = action" rule 2 — it genuinely opens more space) and the picker's own caption reads the resolved shape name: **`Disc`** for one component, **`Disc + 2`** for three. **Nothing on screen says "Shaper", "CSG", "SDF" or "component".**

**L2 — the composition list, inline in the same box, one row at rest.** Activating `Edit shape…` (or right-clicking the Shape box title, reusing `ShowShapeMenu`'s existing affordance) reveals a component list **below** the L1 rows. With one component it is **one collapsed card header** — per the Card-layout #5 rule: `▾ ≡ ☑ [◯ Ellipse] [Add|Add soft|Cut|Cut soft] … ×` — followed by a `+ Shape` button. So going from L1 to L2 adds **two rows**, one of which is a button, and never a panel.

**L3 — per-component detail.** The card's fold caret opens Width/Height/Skew/Rounding/Pulge + offset/rotation/scale, and — only when the mode is a soft one — Strength and Sharpness. Same fold idiom the modifier stack already uses.

**Where it lives: inline in the Pyre window's Shape box. Not a modal, not a separate window, not the preview canvas.**
- A **modal** takes the preview away, and the preview is the only way to judge a silhouette.
- A **separate window** duplicates the two-layer-lists hazard `PYRE_SHAPE_FILL_BORDER.md` §9 warns about, and decouples live preview.
- The **preview canvas** already is the sanctioned bespoke raw-IMGUI island; adding a second editing canvas puts two canvases in one window.
- **Inline is safe against "Stable workspace"** precisely because the Shape box is in the left control pane of the split — left-pane growth cannot move the canvas. The `Edit shape…` row itself must be a fixed-height, non-wrapping row so revealing the list does not reflow it.

**Getting back:** collapse the component list (the same affordance that opened it, now reading `Edit shape…` with the list hidden), or fold the Shape box. The L1 caption always names the composite (`Disc + 2`), so the collapsed state never lies about what is inside.

### The guideline, phrased for pasting into the project's UI rules

> ## Progressive disclosure — complexity must earn its space
>
> When a general mechanism replaces a specific one, the general mechanism starts in the specific one's clothes.
>
> 1. **A generator's default state is its simplest useful one, and at rest it costs ZERO extra rows versus the control it replaced.** If a general shape engine's fresh layer shows one more row than the old dedicated disc did, the disclosure design has failed and the replacement is not ready to ship.
> 2. **The next level opens from the control that IS the simple value — never from a separate "Advanced" toggle or a mode switch.** Precedents already in the codebase: `ZuiFillControl` (a colour swatch until you change its mode), `ZuiBox`'s gear accordion, `PyreSwarmOnlyAttribute`. There is no simple-vs-advanced mode; there is a thing, and it can be opened.
> 3. **Each level adds ONE row at rest, not a panel.** A one-item list renders as its own card header, never a header plus a body. (This is the existing **Card layout #5** rule; decide by counting, so new one-item kinds get it free.)
> 4. **The collapsed control must NAME what is hidden inside it** — `Disc`, `Disc + 2`, `Fill: gradient`. A disclosure that hides its own existence is not disclosure, it is a lost feature: three Pyre features shipped this way and got 0, 1 and 20 uses out of 305 layers.
> 5. **Opening a level must not move the workspace.** Grow the control pane; never the canvas side. (Existing **Stable workspace** rule, applied — reserve the opener as a fixed-height non-wrapping row.)
> 6. **The opener is either the inline choice itself, or is named for its destination with a trailing ellipsis.** (Existing **Label = action** rule, applied — do not invent a competing affordance, and never a bare action verb that opens a place to act.)
> 7. **A parameter that identifies the thing stays at the top level; only parameters that COMBINE things go deeper.** A star's arm count is what makes it a star — it stays where the star is chosen. How that star is fused with a second shape is composition, and that goes one level down.

Rules 3, 5 and 6 are restatements of rules already in `ui-layout-rules.md` and should be written as cross-references, not as new text. Rules 1, 2, 4 and 7 are new.

---

## 6. The honest counter-argument

**Stated at its strongest: the ruling trades a fixed, cheap, bounded cost for an unbounded one, in the exact feature that 42% of authored layers use, and nothing in the tool will warn the user before it becomes slow.**

1. **The multiplicative case is real and has no LOD.** Per-particle shape *animation* and *composition* multiply. Pyre evaluates every shape dial per particle at that particle's own life (`PyreRenderer.cs:3262`), so a composed shape whose parameters vary per particle cannot be compiled once — `compileShape` allocates a parts array and an inverse matrix per component (`:1119-1124`) and would run per particle per frame, on worker threads, with the GC churn that implies. Then each candidate pixel walks O(leaves). An 8-component shape × 200 particles × a 20×20 box is ~640k leaf SDF evaluations *plus* 200 tree compiles, per layer per frame, where today's disc is ~80k `sqrt`. `PyreLayerCache` (`PyreLayerCache.cs:3-10`) rescues the *static* case and does nothing for this one. **This is a cost class Pyre has never had, and there is no budget dial, no leaf cap surfaced to the user, and no warning.** (Shaper itself needed two guards for the same reason — `MAX_COMPONENT_DEPTH` and `MAX_COMPONENT_PARTS`, and the code says why: "8 components nested 4 deep is 4096 primitives evaluated for every single grid cell", `:986-989`.)
2. **The disc is not the casualty; Star and Polygon are.** Shaper's star has zero parameters (`:1039`) and its hexagon/octagon are hand-fitted approximations (`:1036-1037`). If "Shaper's primitives" is taken literally, two of Pyre's six flat forms get materially worse on day one. The fix is small — but it is a fix that must be *scheduled*, not assumed.
3. **Sparkle is destroyed by a literal reading.** It has no silhouette to route (§1). Any plan that says "the shaper generator is the only way to generate primitives" and does not explicitly exempt Sparkle deletes a shipped effect.
4. **Streak loses its one bespoke swarm behaviour.** `streakScaleLengthOnly` makes `swarmScaleByIndex` drive length only, not width — the only per-form branch inside the swarm loop (`PyreRenderer.cs:1531`). A general shape has one uniform scale; axis-selective swarm scaling is either new plumbing on the general engine or a lost feature.
5. **Authoring can get slower despite the disclosure design, if the level boundary is drawn wrong.** Today, choosing Star is one click and its four dials are immediately present. If a general engine puts a primitive's *own* parameters one level down, every non-disc shape becomes two clicks deeper than today, and the owner's requirement ("as easy as it is now") is met only for the disc. Rule 7 in §5 exists specifically to prevent this, and it is the single design decision most likely to be got wrong.
6. **The historical argument, which is the strongest one.** `PYRE_SHAPE_FILL_BORDER.md` §10 documents that Pyre has already built three versions of this idea and all three landed invisible: channel grouping (0 of 305 layers), the melt-together blob mode (20 of 305), the border stage (1 of 305, gated to 6 of 29 generators with no explanation anywhere). None failed on design. All failed because you cannot find them. **A general shape engine hidden behind a chip is structurally the fourth instance of that failure**, and the disclosure design is not a UX nicety here — it is the load-bearing half of the deliverable.

**What the counter-argument does NOT support:** it does not support keeping the current disc. Routing a disc through the SDF evaluator is arithmetically the *same operation* (§1), gains free AA and a single well-defined `crossFrac`, and inherits the silhouette-agnostic border stage. There is no quality or speed argument against the disc case. The objection is entirely about the composed case, the two regressed primitives, and discoverability.

---

## 7. Export

**Direct answer: no, export is not on the critical path, and the owner's ruling routes around it entirely.**

- **The fact is still true.** Verified independently: `grep -c toDataURL` on `public/index.html` returns **0**, and there are no download / blob / spritesheet / export hits in `public/index.html` or `server.py`. Shaper still cannot emit a file of any kind. `PRODUCT_CONTRACT.md`'s "Intentional v1 limits" says so on purpose.
- **`PYRE_GUG.md` §8 says: "Until export exists, no integration of any shape is possible."** **That sentence is now false, and I am flagging it explicitly as contradicted.** It is true only for the import-a-finished-artefact reading — options (a) and (c) in §4 — which is the model report 1 recommended and which the owner has since reversed. Under "Shaper's engine becomes Pyre's primitive engine", **nothing crosses a process boundary, at author time or at run time**. What moves is 124 non-comment lines of scalar maths (`public/index.html:981-1171`), transcribed into C# once, by a person. There is no file format, no serialiser, no importer, and no runtime dependency on the Python server or the browser.
- **What would still need a path, and it is not export.** If the owner wants to *draw* a shape in Shaper's browser UI and then use it in Pyre, that is **document interchange** — a JSON shape record, which Shaper already persists through `project_document.py` — not image export. And it is optional: the ruling implies the shape is authored **in Pyre**, in which case Shaper's web app becomes a reference implementation and a design source, not a dependency, and can keep evolving on its own schedule without ever growing an exporter.
- **Corollary worth stating for the synthesis writer:** this reframing also removes report 1 §8's cautionary link to `Playback3D`. That precedent (`PyreRenderer.cs:262-269`, a form with a full editor preview and a runtime that draws nothing) is a warning about *importing external content as a ShapeForm*. Porting maths into the runtime asmdef is the opposite kind of change — it produces no new runtime load path to leave unfinished.

---

## Contradictions with prior reports, stated explicitly

| Source | Claim | Status |
|---|---|---|
| `PYRE_GUG.md` §8 | "One Pyre layer should hold one whole Shaper model … a Shaper model becomes a named, reusable Silhouette asset." | **Reversed by the owner.** Its underlying *reason* (Shaper layers and Pyre layers are different kinds of thing) survives intact and still binds: Shaper shape components must never become Pyre layers. `PYRE_SHAPE_FILL_BORDER.md` §9 already says the same and is compatible with the new ruling. |
| `PYRE_GUG.md` §8 | "**Until export exists, no integration of any shape is possible** — and that's a Shaper task, not a Pyre task." | **Contradicted.** See §7. True for import-a-picture; false for port-the-engine. |
| `PYRE_GUG.md` §8 | "Not a nested sub-editor … a nested sub-editor inside a Pyre layer means two differently-shaped layer lists at two nesting depths in one window, plus re-hosting or re-implementing Shaper's whole renderer." | **Half contradicted.** The two-lists hazard is real and §5's design avoids it (the component list is a card list inside one box, not a second layer list). But "re-implementing Shaper's whole renderer" overstates it by ~2×: the *silhouette* engine is 98 lines and the *renderer* (`renderModelGrid`, 214 lines) is explicitly not wanted. |
| `PYRE_SHAPE_FILL_BORDER.md` §8 | "Extrusion is the only one of your four proposals that is essentially new construction." | **Partly contradicted.** It is new *in Pyre*, but not new in design or magnitude: Shaper ships a working 7-profile extrusion × 5-profile bevel registry in **26 code lines** (`:1144-1171`), in exactly the "inside-distance → height" form that report calls the right shape-op contract. It ports with the SDF. |
| `C-shaper3d.md` §5, Option D | "Port the shape maths into Pyre and drop the web app … highest ongoing cost of upkeep … not recommended now." | **Contradicted on the cost estimate, not on the conclusion.** That option was costed as "the per-cell SDF/height-map rasteriser, the 4-mode silhouette fusion, the 7 surface-category shading branches, the 12 patterns" — i.e. the whole app. The owner's ruling asks for a strict subset: the silhouette engine only, no materials, no patterns, no rasteriser. Measured, that subset is 98 lines. And "drop the web app" is not implied — Shaper survives as its own tool. |
| `A-generator-inventory.md` §2.6 | Streak described as a "comet-tail capsule/rect". | **Sharpened, not contradicted.** The code is a **rect**: `if (f < -behind || f > ahead) continue; … if (as_ > halfW) continue;` (`PyreRenderer.cs:3752-3757`) — axis-aligned box test in the streak frame, with independent side and end feathers. No capsule end caps.

---

## UNVERIFIED / open questions

- **Whether `compileShape` can be split into a static structural compile plus per-sample parameters**, so a per-particle-animated composite avoids re-compiling per particle. Plausible from its structure (the tree topology and modes are static; only `rx/ry/skew/rounding/pulge` and the transform vary) but not prototyped. This is the load-bearing assumption behind §4(b)'s "the trivial case is free" and §6's mitigation.
- **No performance measurement was taken.** Every cost statement in §4 and §6 is an operation count read off the source, not a profile. Nobody has run an N-leaf SDF through Pyre's swarm at 64px × 16 frames.
- **Whether the owner wants the primitive's own parameters at L1 or L3** (§5 rule 7). I recommend L1 and gave the reason, but this is the decision that determines whether "as easy as it is now" is actually met for Star and Polygon, and it is his call, not mine.
- **The light-belongs-to-shape-or-fill question** (`PYRE_SHAPE_FILL_BORDER.md` §6) is untouched by this ruling and still blocks the §2 recommendation about lighting a Shaper silhouette with the solids' rig.
- **I did not read `B-compatibility.md`, `D-verification.md`, `E-spritefx-triage.md`, `F-decomposability.md`, `G-verification-2.md` or `H-swarm-universality.md`.** H in particular may contain swarm-cost evidence that would sharpen or contradict §6; the synthesis writer should reconcile.
- **Migration: a footnote, per instruction.** Every authored Pyre asset holding `shapeForm = Disc/Crescent/Star/Polygon/Streak` maps mechanically to a one-component shape (Star and Polygon require the two new SDF cases first). Sparkle has no mapping and must be re-homed as a fill. The owner has ruled that no authored asset matters, so this is not a constraint on the design.
