# T-0098 — Investigator E: SpriteFx triage against the SHAPES / FILLS / BORDERS model

Research only. No source file changed, no Unity run, no Coplay, no subagents. All paths are absolute or relative to `D:\UNITY\Laubrary Dev\Assets\Packages\Laubrary\`. Line numbers are from the working tree on `feat/lathe` as of 2026-08-30.

Inputs read first, as briefed: `D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0098\A-generator-inventory.md`, `...\B-compatibility.md`, `...\D-verification.md`, and `D:\UNITY\Laubrary Dev\PYRE_GUG.md` §6 and §9. Where D disagrees with A/B I follow D.

---

## 0. Summary

**Count: 41 concrete modifiers.** Method: I walked every `.cs` file under `D:\UNITY\Laubrary Dev\Assets\`, parsed every `class X : Y` declaration, resolved each class's base chain transitively to one of the five stage bases, and excluded the abstract bases themselves. Result: **17 Geometry + 12 Pixel + 10 Post + 1 Edge + 1 Simulation = 41**, plus 4 abstract stage bases (`GeometryModifier`, `PixelModifier`, `PostModifier`, `EdgeModifier`, each `: PyreModifier` — `SpriteFxModifiers.cs:191/661/1745/2565`) and `SimulationModifier` (`SpriteFxSimulationModifiers.cs:44`). This **matches D-verification §1.2 exactly** (17/12/10/1/1) and confirms B's sub-headings ("15", "11", "12") are wrong while B's total (41) is right. I did not find a 42nd anywhere.

Three modifier-shaped things are deliberately **not** in the 41 because nothing can put them in a modifier list: `SpriteFxAuxMapGate` (`SpriteFxAuxMapGate.cs:35`, no field of its type exists anywhere — D §3 confirms), `SpriteFxRecolor`, and `SpriteFxHurtFlash` (both are their own components/hosts, not `PyreModifier` subclasses).

### Bucket counts

| Bucket | Count | Members |
|---|---|---|
| **(a) SHAPE-STAGE OP** | **22** | Skew, Scale, Rotate, Wobble, Sunburst wobble, Ring wave, Blast, Profile, Ground, Sunburst, Pulse rings, Turbulence, Curl (swirl), Smudge, Ordered dither, Layer dissolve, Wipe, Dissolve, Ballistic shockwave, Fuse (blob melt), Kaleidoscope, Pixel fluid |
| **(b) FILL GENERATOR** | **2** | Voronoi crack, Fake light |
| **(c) FILL OP** | **9** | Tint, Contrast, Brightness, Saturation, Posterize, Colour tint, Colour replace, Colour remap, **Sphere (fake depth)** |
| **(d) BORDER GENERATOR / OP** | **4** | Outline (gen), Bloom (gen), Edge smooth (op), Edge warp (op) |
| **(e) POST-ONLY** | **2** | Chromatic aberration, Drop shadow |
| **(f) CULL** | **2** | Vortex field (progress), Pin warp |
| | **41** | |

**Nine of the 41 are genuinely contested** and are argued in §3: Tint, Colour tint, Ordered dither, Sphere, Kaleidoscope, Bloom, Fuse, Ballistic shockwave, Pixel fluid. Their placement above is my primary call, not a clean one.

### The headline

**The owner's hypothesis is half right, and the half that is wrong is the load-bearing half.** "Many SpriteFx would become fill generators" is **not** what the code says: only **2 of 41** synthesise a picture from position/time/noise well enough to stand alone as a fill. What the code actually says is that **the SpriteFx list is overwhelmingly a SHAPE-stage system already** — 22 of 41 operate on geometry or coverage and never needed the colour underneath. The second half of the hypothesis ("some SpriteFx would be available to process shape layers, others would only affect fills") is **strongly confirmed**, and is in fact the real finding: the split is ~22 shape / ~11 fill / ~4 border / ~2 post, and today all four groups are shown to the user as one undifferentiated list.

**And the biggest problem is not in the list at all — it is that two thirds of the proposed architecture already exists, half-built, under other names, and the report never says so.** Pyre already has a border stage that reads a chamfer distance field and paints it with a `ZuiFill` (`Pyre.cs:517-528`, `PyreRenderer.cs:950-960`); `ZuiFill` already is a small fixed fill system (`Zui/Scripts/Runtime/ZuiFill.cs:29-64`); and `PixelInfo` already carries a generator-written normalised edge-distance scalar (`crossFrac`, `SpriteFxModifiers.cs:651`). The re-architecture is therefore a **merge**, not a greenfield build, and the merge has at least three live name collisions (§4).

---

## 1. How my axis relates to B's five stages

B's axis is **where in the pipeline a thing attaches** (geometry / pixel / post / edge / simulation). Mine is **what the thing is conceptually about** (shape / fill / border / post). They are genuinely independent, and the cross-tabulation is the most useful single artefact here:

| | (a) shape | (b) fill gen | (c) fill op | (d) border | (e) post | (f) cull |
|---|---|---|---|---|---|---|
| **Geometry (17)** | 14 | 0 | **1** | 0 | 0 | 2 |
| **Pixel (12)** | **4** | **1** | 7 | 0 | 0 | 0 |
| **Post (10)** | **3** | **1** | 0 | **3** | 2 | 0 |
| **Edge (1)** | 0 | 0 | 0 | 1 | 0 | 0 |
| **Simulation (1)** | **1** | 0 | 0 | 0 | 0 | 0 |

**Where the axes agree.** The Geometry stage is almost pure shape (14/17), and it is the one place B's stage genuinely predicts the new bucket.

**Where they cut across each other — the interesting cells, all bolded above.**

1. **4 of the 12 "pixel" effects are not fill work at all** — Ordered dither, Layer dissolve and Wipe touch alpha/coverage, not colour (Wipe's whole implementation is `SfxKernels.KAlphaMask`, `SpriteFxModifiers.cs:1440-1442`; it is the retired `AlphaMaskModifier` folded in, `:10`/`:1279`). They are shape ops that happen to run in the recolour slot because the recolour slot is where per-pixel work lives.
2. **1 of the 17 "geometry" effects never touches the silhouette** — Sphere. Its own comment proves it: *"r=1 (the shape's own edge) always has ratio=1 on EITHER branch … so the edge never moves, for any Strength, either direction"* (`SpriteFxModifiers.cs:3142-3143`). It is a lens on the interior — a fake-depth **fill** op wearing a geometry hook.
3. **3 of the 10 "post" effects are border work** — Outline, Bloom and Edge smooth all key off the alpha silhouette and paint at or outside the rim. Edge smooth's code is explicit that it must *not* be a general filter: *"a pixel that already has meaningful alpha keeps its EXACT original colour, always — only its alpha may soften. Only pixels near the true boundary … borrow a blurred colour"* (`SpriteFxModifiers.cs:2762-2764`). That is a border op with a post-stage implementation.
4. **1 "post" effect is a fill generator** — Fake light. It builds a height field with `SpriteFxAuxMap.BuildMap(..., AuxMapGenerator.EdgeDistanceIn, ...)` (`SpriteFxRelight.cs:458-463`) and shades it. On a blank white silhouette it produces a lit dome: that is a material, not a filter.
5. **The 1 simulation effect is a shape op** — Pixel fluid's persisted state is *"a velocity displacement field and an alpha-erosion field"* (`SpriteFxSimulationModifiers.cs:~118`): displacement and coverage, no colour.

So: **stage does not predict bucket in 10 of 41 cases (24%)**, and every one of those 10 is a case where the current UI puts an effect in the wrong mental category.

---

## 2. The table — all 41

Stage column is B/D's (authoritative counts from D §1.2). "Reads edge/coverage?" flags §5's survivors.

| # | Modifier (display name) | Class / line | Stage (B) | **New bucket** | Reads edge-dist or coverage? | One-line reason |
|---|---|---|---|---|---|---|
| 1 | Skew | `SkewModifier` `SpriteFxModifiers.cs:203` | Geometry | **(a) shape** | — | Pure affine coordinate shear; colour never consulted. |
| 2 | Scale | `:228` | Geometry | **(a) shape** | — | Axis-wise stretch about centre; a silhouette transform. |
| 3 | Rotate | `:266` | Geometry | **(a) shape** | — | Rigid rotation of the sample grid. |
| 4 | Wobble | `:302` | Geometry | **(a) shape** | — | Sinusoidal horizontal ripple keyed to Y; deforms coverage. |
| 5 | Sunburst wobble | `:339` | Geometry | **(a) shape** | radius-relative | Radial push keyed to angle about the shape's own centre. |
| 6 | Ring wave | `:386` | Geometry | **(a) shape** | radius-relative | Radial ripple keyed to distance — a shockwave over the silhouette. |
| 7 | Blast | `PointBlastModifier` `:437` | Geometry | **(a) shape** | — | Expanding displacement cone from an arbitrary origin. |
| 8 | Profile | `:546` | Geometry | **(a) shape** | — | *Silhouette molder* — sets width at each height. Its own doc word. |
| 9 | Ground | `:583` | Geometry | **(a) shape** | — | Re-anchors the shape's base to a surface line and grows it up. |
| 10 | Sunburst | `:779` | Geometry | **(a) shape** | radius-relative | Spokes that *"genuinely reach further out than the shape's own radius"* — silhouette modulation; was wrongly a PixelModifier once. |
| 11 | Pulse rings | `:830` | Geometry | **(a) shape** | `crossFrac` from `ctx.radius` (`:857`) | Concentric rings that push pixels; also was wrongly a PixelModifier once. |
| 12 | Turbulence | `:2653` | Geometry | **(a) shape** | — | Domain-warped noise displacement of the sample coordinate. Contested (§3.7). |
| 13 | Curl (swirl) | `:2882` | Geometry | **(a) shape** | — | Curl-noise + placed vortices displacing coordinates. Contested (§3.7). |
| 14 | **Vortex field (progress)** | `:3007` | Geometry | **(f) CULL** | — | Duplicate of Curl's vortex authoring, progress-driven; **0 assets** use it (D §3). |
| 15 | Sphere (fake depth) | `:3084` | Geometry | **(c) fill op** | radial normalised | **Reclassification.** Sphere-impostor remap of the interior; `:3142-3143` proves the edge never moves. Fake depth = material. |
| 16 | Smudge | `:3167` | Geometry | **(a) shape** | — | Drags pixels along painted strokes; explicitly *"type-agnostic"*. 2 assets use it. |
| 17 | **Pin warp** | `:3340` | Geometry | **(f) CULL** | — | 0 assets; **animation is dead** — `SetFrame` has no caller repo-wide (D §3). |
| 18 | Tint | `:686` | Pixel | **(c) fill op** *(+b)* | **`p.crossFrac` at `:707`** | Multiply + a gradient painted across the shape. The cross-gradient half IS a radial fill. Contested (§3.1). |
| 19 | Contrast | `:722` | Pixel | **(c) fill op** | — | Scalar contrast on existing colour; nothing to generate from. |
| 20 | Brightness | `:738` | Pixel | **(c) fill op** | — | Scalar; needs what was underneath. |
| 21 | Saturation | `:754` | Pixel | **(c) fill op** | — | Scalar; needs what was underneath. |
| 22 | Posterize | `:869` | Pixel | **(c) fill op** | — | Quantises colour into bands; `affectAlpha` optionally also coverage. |
| 23 | Ordered dither | `:902` | Pixel | **(a) shape** | alpha | Converts smooth **alpha** into a hard Bayer stipple — coverage quantisation, not recolour. Contested (§3.3). |
| 24 | **Voronoi crack** | `:942` | Pixel | **(b) FILL GENERATOR** | `p.crossFrac` (`:1098`) | Worley cells hashed from `p.wx/wy` — synthesises a full pattern; would read fine on blank white. |
| 25 | Layer dissolve | `:1247` | Pixel | **(a) shape** | — | Hashed removal of pixels keyed to the *warped* position; coverage erosion. |
| 26 | Wipe | `:1299` | Pixel | **(a) shape** | alpha | An alpha mask (`KAlphaMask`, `:1441`) shaped by a growing form. Pure coverage. |
| 27 | Colour tint | `:1450` | Pixel | **(c) fill op** *(+b)* | — | Washes toward a colour; *"at amount 1 every pixel IS the colour"* — a flat fill at the limit. Contested (§3.2). |
| 28 | Colour replace | `:1602` | Pixel | **(c) fill op** | — | Hue-band → new hue. Keys on the existing colour by definition. |
| 29 | Colour remap | `SpriteFxColorRemap.cs:142` | Pixel | **(c) fill op** | — | Source-colour clusters → swatches/gradients. Cannot exist without a source. |
| 30 | Dissolve | `:1157` | Post | **(a) shape** | neighbour alpha | Erases a fraction of pixels; a Post *"specifically so smoothness can see real NEIGHBOUR pixels"*. Coverage. |
| 31 | Bloom (glow) | `:1787` | Post | **(d) border generator** | alpha + threshold | Bright pixels bleed a halo **outward into empty space** and lift alpha there. Contested (§3.6). |
| 32 | Outline | `:1887` | Post | **(d) border generator** | alpha silhouette | Draws a band in the transparent ring around the silhouette. **Duplicates Pyre's own border stage** (§4.2). |
| 33 | Chromatic aberration | `:2102` | Post | **(e) post-only** | — | Per-channel resample of the finished composite; meaningless before compositing. |
| 34 | Ballistic shockwave | `:2200` | Post | **(a) shape** | alpha erosion | Advects a density field through a velocity field + erodes alpha. Contested (§3.8). |
| 35 | Fuse (blob melt) | `:2459` | Post | **(a) shape, group-level** | alpha | Blur-threshold-resolidify the **silhouette**; `colorBleed` is a side effect. This IS the proposed shape-layer grouping (§3.5). |
| 36 | Edge warp | `EdgeWarpModifier:2585` | Edge | **(d) border op** | rim parameterisation | Warps the rim with noise; **currently dead** (no `EdgeOffset` caller). The new model *revives* it (§5.2). |
| 37 | Edge smooth | `:2734` | Post | **(d) border op** | alpha, interior-gated | Softens only near the boundary; interior colour explicitly preserved (`:2762-2764`). |
| 38 | Drop shadow | `:3386` | Post | **(e) post-only** | alpha silhouette | An offset darkened copy composited **behind** — a compositing operation, not a border. |
| 39 | Kaleidoscope | `:3481` | Post | **(a) shape, group-level** | — | Repeats the layer into N arms. Contested (§3.4) — and its own doc says Post was chosen as a *workaround*. |
| 40 | Fake light | `RelightModifier` `SpriteFxRelight.cs:243` | Post | **(b) FILL GENERATOR** | **chamfer `EdgeDistanceIn` (`:458-463`)** | Invents a height field from the silhouette and shades it — that is a material pass, not a filter. |
| 41 | Pixel fluid | `SpriteFxSimulationModifiers.cs:115` | Simulation | **(a) shape** | alpha erosion field | Persisted velocity-displacement + alpha-erosion fields. No colour anywhere. Contested (§3.9). |

---

## 3. The contested nine — the tension, not a forced bucket

**3.1 Tint — one modifier holding a fill op and a fill generator.** `tint` is a flat multiply (fill op: it cannot brighten, cannot add a hue that isn't there). `crossGradient` is a gradient *painted across the shape*, evaluated at `p.crossFrac` (`:707`) — i.e. a **radial/cross gradient fill**, which is exactly `ZuiFill.Mode.Radial`. On a blank white silhouette the cross-gradient half draws a complete radial ramp; the multiply half draws nothing. **Under the new model Tint must be split**, and the crossGradient half must be reconciled with `ZuiFill` rather than shipped alongside it.

**3.2 Colour tint — a fill generator only at the boundary of its range.** Its doc is unambiguous: *"at amount 1 every pixel IS the colour, at 0 nothing changes"* (`:1447-1448`), and *"Alpha is left alone, so a silhouette keeps its shape."* So at amount 1 it is a **flat-colour fill generator**; anywhere else it is a fill op. I filed it (c) because its authored use is the wash, not the limit — but a "flat colour" fill generator in the new model would make it redundant at the limit and keep it as the blend op. Genuine tension, and a clean example of why "generator vs op" is a spectrum here, not a partition.

**3.3 Ordered dither — coverage quantiser or final-frame dither?** The brief's example (e) mentions "dithering of the final frame" as post-only. This one is *not* that: it dithers **alpha** specifically to turn soft edges into stipple (*"Converts smooth alpha (from Outer softness, a Bloom halo, a churned Turbulence edge…) into a hard stipple"*). That is a shape/coverage op. But it is *also* the classic last-pass pixel-art treatment, and the same class would be wanted on the composited frame. **It genuinely belongs in two places**, and a model with one home per effect will lose one of them.

**3.4 Kaleidoscope — the clearest case of the current architecture forcing the wrong bucket.** Its own doc says it was made a Post *"deliberately … because that is the only place that is universal: Pyre's existing star/spreadCount does radial repeat too, but it lives inside the SCATTER path, so MetaBlob, Height balls and Fire never reach it."* That is not a design statement about kaleidoscopes; it is a **workaround for exactly the compatibility gap the new model removes**. If shape layers become groupable and a shape stage is universal, Kaleidoscope wants to be a shape-group op (repeat the silhouette, then fill once) — which is also visually different, because repeating *after* the fill duplicates the fill's own animation phase per arm.

**3.5 Fuse (blob melt) — the post-hoc implementation of the owner's own "group several shape layers" feature.** It box-blurs the premultiplied frame and re-thresholds alpha so nearby silhouettes read as one (`:2459-2555`). Its whole purpose is *silhouette union across layers*. In the proposed model, "several shape layers groupable so ONE fill is applied to the combined silhouette" **is** Fuse, done exactly and cheaply instead of by blur-and-threshold. So: keep it as a shape-group op, and expect the grouping feature to make its `colorBleed` dial obsolete (there would be one fill, so there is nothing to bleed between). **This is the single strongest piece of evidence in the codebase that the owner's grouping idea is right.**

**3.6 Bloom — border generator or post?** It reaches outside the silhouette (`OutwardReachPx` non-zero, `:1790`) and produces a rim of light, which is the brief's own example of a border ("glow rim"). But it is *threshold-driven by interior brightness*, so it needs the fill to exist, and on a flat white silhouette it would halo the entire shape uniformly — a border generator that reads the fill. I filed it (d) with that caveat. If borders are defined as reading only the coverage mask, Bloom does not fit and must stay post.

**3.7 Turbulence and Curl — the ambiguity the new model creates rather than resolves.** Today a geometry modifier warps the sample coordinate *before the fill is sampled*, so it necessarily moves the shape **and** the paint together, and nobody has to decide. Split shape from fill and there are suddenly three distinct effects per warp: warp the silhouette only (fill stays put, paint slides under a moving edge), warp the fill only (edge holds, interior churns), warp both (today's behaviour). `EdgeWarpModifier`'s doc already names this exact problem from the other side: it exists to give *"a jagged-but-smoothly-shaded look that a whole-frame GeometryModifier warp can't give, since that warps the fill along with the silhouette"* (`:2585` region). **So the new model does not merely re-file the 14 warps; it multiplies each of them by a target selector that does not exist today.** That is new UI surface, new serialized state, and new ways to be confused — and it is not in the owner's sketch.

**3.8 Ballistic shockwave — a shape op that also paints.** It advects a *density* field through a velocity field and erodes alpha (`:2200-2273`). Density-through-a-ramp is a fill generator's shape; the displacement and erosion are shape ops. It is really a mini-generator implemented as a post effect. Under the new model the honest answer is that it **splits into a shape op + a fill generator**, or it stops being a modifier and becomes a *generator* in its own right.

**3.9 Pixel fluid — same, plus it is currently a reachable no-op in one host.** Fields are velocity + alpha erosion, so: shape op. But D §3 confirms it is offered in the SpriteFx Stack add-menu and does literally nothing there (`SpriteFxStackView.Catalog()` filters only `EdgeModifier`; `RunStack`'s switch — `SpriteFxBurst.cs:640-667`, re-read here — has cases for Geometry/Post/Pixel only). In the new model, if the shape stage is uniform, a simulation that erodes coverage is just a shape op with state and the special "simulation slot" (`Pyre.cs:766`) can disappear. **That is a real simplification the new model buys.**

---

## 4. What the new model fixes, and what it breaks — for SpriteFx specifically

### 4.1 What it fixes

**F1. The "silently does nothing" class largely vanishes — but only if the shape stage is a mask, not a per-generator hook.** The current failure is precise and structural: a `GeometryModifier` is an **inverse warp of a sample coordinate**, applied *inside each generator's own raster loop*, which each generator must opt into by calling `ApplyGeo`/`ResolveSample`. Eight built-ins never do (D §3 re-measured: `DrawFacetSolid`, `DrawOrb`, `DrawRing`, `RenderTextLine`/`DrawTextChar`, `RenderPlusRampField`, plus Fire/Fireball/height-consumer which take no `mods` parameter at all). If instead every shape generator emits a **coverage field** and warps are applied by resampling that field, the hook is universal by construction and all 22 shape ops work on all shapes for free. **So yes, the class of silent no-ops does vanish — for the shape bucket.** Caveat, and it is a real one: whole-field resampling is precisely what `PyreFormWarp.BuildMap/Apply` already does for plug-in forms (`PyreRenderer.cs:349-350`), and `InfernoForm` opts out of it (`HandlesGeometry => true`, `InfernoForm.cs:19`) — evidence from inside the codebase that a post-hoc resample is **not** always visually equivalent to a per-sample warp. The no-op is traded for an aliasing/quality risk, not eliminated for free.

**F2. The fill bucket's no-ops vanish too, and more completely.** The 11 fill ops/generators currently fail on exactly four generators (Fire, Fireball, Playback3D, mask-fed height field) because those never call `ApplyPix`. Three of those four are *field* generators that already compute a scalar and push it through a ramp in the same breath (PYRE_GUG §9's own observation, which I confirm). Split the ramp out and every fill op applies to them by construction. **Playback3D stays broken because it produces nothing at all** (`PyreRenderer.cs:269`) — no architecture fixes that.

**F3. The substance channel already exists, unnamed.** `PixelInfo` (`SpriteFxModifiers.cs:642-657`) already carries three generator-written per-sample scalars: `wx/wy` (post-warp position), **`crossFrac` — "0..1 across the shape (centre→edge, or bar back→tip)"**, and `life`. This *refines* the report's "no effect reads anything a generator wrote" (PYRE_GUG §6, verified by D): true for **side buffers and aux maps**, false for **inline per-sample scalars**. Two modifiers read `crossFrac` today (Tint `:707`, Voronoi crack's spread mask `:1098`). The proposed "substance" contract is therefore not a new invention — it is `PixelInfo` promoted to a first-class, named, per-generator-documented contract.

**F4. The border stage is already built, for six generators.** `borderEnabled` / `borderWidth` / `borderFill` (`Pyre.cs:517-521`) with a chamfer inside-distance transform (`PyreRenderer.BorderInsideDistance`, `:955+`), gated by `IsFlat2DBorderForm` to Disc/Crescent/Ring/Streak/Star/Polygon (`:950-952`). Its design comment already states the new model's own principle: it *"keeps the FILL and the BORDER as separate buffers"*. Generalising it to all shapes is the smallest, highest-confidence slice of the whole re-architecture.

**F5. One effect stops being dead.** `EdgeWarpModifier` is unreachable today because no host calls `EdgeOffset`/`EdgeSoftness` (D §3 confirms). It is unreachable *because there is no border stage that owns the silhouette rim as a parameterisable curve*. Give borders a stage and Edge warp is the first real citizen of it. **This is the one modifier whose usefulness increases from zero.**

**F6. The simulation slot can be folded away** — see §3.9.

### 4.2 What it breaks

**B1. Three live name/feature collisions, all in Laubrary already.**
- *Fills.* `ZuiFill` is already a fill system: `Mode { Solid, OverLife, Linear, Radial }` × `TextureKind { None, Sprite, Noise, Grid, Dots }` × `NoiseKind { Value, Ridged, Steps }` × `FillSpace { Stamped, Fixed }` × `FillFit { Uniform, Stretch }` (`Zui/Scripts/Runtime/ZuiFill.cs:29-64`). It is **enum-based, not pluggable**. Promoting Voronoi crack to a "fill generator" collides with `TextureKind.Noise`; promoting Tint's crossGradient collides with `Mode.Radial`. Either `ZuiFill` becomes the `[SerializeReference]` plug-in point (and every authored `ZuiFill` needs migration), or Pyre ships two unrelated ways to make a radial gradient.
- *Borders.* `OutlineModifier` and `borderEnabled`/`borderFill` are two implementations of one idea, both reading a distance-from-edge field, differing mainly in that the modifier draws **outside** the silhouette and the layer border draws the **outermost band inside** it. They must merge, or the new border picker will contain both.
- *Fuse.* Adding "group several shape layers → one fill" creates a **fourth** thing called fuse, on top of the three B §3 already documented. Name it something else from day one.

**B2. Splitting one ordered list into three destroys ordering — in one host only.** Pyre already buckets by category (`CollectMods`, `PyreRenderer.cs:1273-1290`, builds a `geo` list and a `pix` list and skips `PostModifier` entirely), so cross-category order is *already* lost there. The **SpriteFx Stack does not**: `RunStack` walks the authored list in order, flushing the Burst batch whenever it hits a non-shaped modifier so that *"everything below must see the pixels the batch produced"* (`SpriteFxBurst.cs:636-668`). Any Stack asset whose meaning depends on interleaving (e.g. a warp between two recolours) is **not expressible** once the list is split into shape/fill/border sub-lists. This is a concrete migration hazard for a real host, not a hypothetical.

**B3. `crossFrac` means four different things and would become load-bearing.** Today it is minor. In a model where fills read a normalised shape coordinate it becomes the primary fill input — and it is currently: shape-local centre→edge in Pyre's raster (`PyreRenderer.cs:3090` and ~10 more sites); **canvas-local**, `radius = min(halfW,halfH)`, in a SpriteFx Stack (`SpriteFxBurst.cs:115`); **the Inferno form's heat value**, with a comment saying so outright — *"crossFrac = the heat value"* (`Runtime/Pyre/Forms/Kiln/PyreInferno.cs:814`, constructed at `:820`); a fork parameter `t` in ForkBlast (`PyreForkBlast.cs:394`); and **hardcoded `0f`** in `SpriteFxRecolor` (`:167`), which silently kills Tint's cross gradient in that host. Formalising the substance contract means fixing all five, and Inferno's usage shows the channel is *already* being used as an ad-hoc energy scalar — which is the good news and the bad news at once.

**B4. The warp-target question (§3.7) is new, mandatory, and unaddressed.** 14 shape ops become 14 × {silhouette / fill / both}.

**B5. Two shape-bucket sub-kinds, not one.** 14 of the 22 shape ops are **invertible coordinate warps** (`InverseWarp`, `SpriteFxModifiers.cs:194`); the other 8 — Ordered dither, Layer dissolve, Wipe, Dissolve, Ballistic shockwave, Fuse, Kaleidoscope, Pixel fluid — are **coverage-buffer operations** that cannot be expressed as an inverse map and need a real mask buffer plus (for two of them) persisted state. A single "shape op" plug-in interface will not fit both. The owner's sketch says "SHAPES — pluggable generators"; it does not say the shape *stage* needs two op contracts, and it does.

**B6. Nothing here fixes the canvas-reach problem.** Four post effects reach outside the silhouette and Pyre never pads for them (D §3, upgraded to CONFIRMED). Bloom and Outline are in my border bucket, Drop shadow and Chromatic aberration in post — the re-architecture re-files them but does not make Pyre grow the buffer.

### 4.3 What the new model would make *worse* — one item

**Fill ops become 9 controls hunting for a fill that may not exist.** Today Contrast/Brightness/Saturation/Posterize/Colour replace/Colour remap are cheap because there is always a finished pixel underneath. In a shape/fill/border model, a shape layer with no fill assigned (or a shape group whose fill is applied at group level) gives them nothing to act on, and the "silently does nothing" failure the model is meant to abolish reappears in a new place — **the same bug, relocated from the generator axis to the stage-population axis.** The mitigation is trivial (a shape always has a fill, defaulting to flat white) but it must be a rule, not an accident.

---

## 5. Modifiers that already read distance-from-edge or coverage — the ones that survive unchanged

These are the effects that already speak the new model's language and would need essentially no rework:

| Modifier | What it reads | Citation |
|---|---|---|
| **Fake light** | chamfer distance field, `AuxMapGenerator.EdgeDistanceIn` (also `Alpha`, `Luma` via `SfxReliefSource`, `SpriteFxRelight.cs:46-52`) | `SpriteFxRelight.cs:458-463` |
| **Tint** (cross half) | `p.crossFrac` — normalised centre→edge | `SpriteFxModifiers.cs:707` |
| **Voronoi crack** | `p.crossFrac` for its spread mask | `:1098`, `:1113-1129` |
| **Pulse rings** | `dist / ctx.radius` — a shape-local edge fraction | `:857` |
| **Sphere** | radial fraction `d.magnitude / effRadius` | `:3122-3123` |
| **Outline** | alpha silhouette + a thickness band | `:1887+` |
| **Edge smooth** | alpha, interior-gated | `:2762-2764` |
| **Fuse (blob melt)** | blurred premultiplied alpha | `:2532+` |
| **Drop shadow** | alpha silhouette + `alphaThresholdValue` | `:3386+` |
| **Bloom** | luma threshold over alpha | `:1787+` |
| **Wipe** | writes alpha via `KAlphaMask` | `:1440-1442` |
| *(non-modifier, same language)* Pyre's border stage | chamfer `BorderInsideDistance` | `PyreRenderer.cs:955+` |
| *(non-modifier)* `SpriteFxAuxMap` | `EdgeDistanceIn/Out/Interior/Edge/Detail/…` — 10 generators | `SpriteFxAuxMap.cs:16-28` |

**`SpriteFxAuxMap` is the substance contract, already written.** It builds exactly the maps the new model needs (edge distance in/out, interior, alpha, luma, saturation, hue proximity, Sobel edge, detail) from a finished buffer. Under the new model it stops needing to *invent* them from pixels — the shape stage would hand them over directly. That also gives `SpriteFxAuxMapGate` (154 lines, fully built, zero call sites — D §3) an actual reason to exist.

---

## 6. Modifiers that make no sense under the new model

Only two, and both are already cull candidates on independent grounds (D §3: zero asset usage for each):

- **Pin warp** — hand-keyframed pin lattice. Not shape-vs-fill in any interesting way (it warps whatever is under it), its authoring UI was deleted with Pyre1, and its animation hook `SetFrame` has no caller anywhere, so every pin holds keyframe 0 forever. Under any model it is a warp with no target selector and no authoring surface.
- **Vortex field (progress)** — the same `List<VortexPoint>` data as Curl with a different driver. Duplicate authoring surface for zero assets.

**Not culled, but worth flagging:** *Chromatic aberration* and *Drop shadow* are the only two effects with no shape/fill/border role at all. If the new model is billed as "everything becomes a shape, fill or border plug-in", these two are the counterexample proving a **post stage must survive** — the SpriteFx modifier system cannot be fully absorbed.

---

## 7. What I could NOT determine

1. **Nothing was rendered.** Every "would produce a usable picture on a blank white silhouette" judgement is a reading of the maths, not an A/B render. The two FILL GENERATOR calls (Voronoi crack, Fake light) are the ones a rendered test would most usefully confirm or refute.
2. **Whether "fill" in the owner's model means per-sample or per-buffer.** The whole (b)-vs-(c) distinction shifts if a fill is a whole-buffer pass (then Voronoi crack, Bloom and Ballistic shockwave all become fills) versus a per-sample function of the substance (then only Voronoi crack qualifies, and Fake light needs a neighbourhood so it cannot be one). **This is the single question that most changes my table**, and it is a decision, not a fact I can read out of the code.
3. **Whether a resampled coverage mask is visually acceptable** as the universal shape stage (F1's caveat). `InfernoForm.HandlesGeometry => true` is evidence it sometimes isn't; I did not read `PyreFormWarp.cs` closely enough to say when it degrades. B's open question 3 says the same.
4. **Which of the 9 fill ops read the shape's own frame vs the canvas frame** in a SpriteFx Stack. I confirmed `crossFrac` differs between hosts (B4/B3 above) but did not audit each of the 9 for whether it uses `p.x/p.y`, `p.wx/p.wy`, or a normalised coordinate — that matters for how they migrate.
5. **Extrusion.** The owner's sketch includes "extrusion plugins applicable to any shape". No SpriteFx modifier does extrusion; the extrusion that exists is inside the generators (`Pyre.cs:58`, facet solids' front face + darker sides). Out of scope for a modifier triage, and I did not investigate it.
6. **Whether any authored asset depends on cross-category ordering in a SpriteFx Stack** (B2's hazard). I established that Stack ordering is meaningful and Pyre's is not; I did not census `SpriteFxFilter`/Stack assets for actual interleaving.
7. **Dial-level migration cost.** I classified by role, not by how many of each modifier's ZUIValues survive a re-parenting.
