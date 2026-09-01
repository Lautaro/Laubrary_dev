# T-0098 — Investigator I: effect universality ("if SpriteFX affects pixels and every generator produces pixels, why can't they always be used?")

> Scope: read-only. Every load-bearing claim carries a `file:line` from the working tree on `feat/lathe`, 2026-08-30. Where a prior report contradicts the code, the code wins and I say which report was wrong. No usage counts are used as an argument (owner's ruling); usage appears only as migration risk. I assume the owner's ruling that **drawing outside the shape and outside the frame is allowed and expected**.
>
> Path abbreviations: `RT/` = `D:\UNITY\Laubrary Dev\Assets\Packages\Laubrary\Runtime\`, `ED/` = `…\Editor\`.

---

## Bottom line

- **He is right, and the code proves it more sharply than the earlier reports did.** Nine effects — Contrast, Brightness, Saturation, Posterize, Colour tint, Colour replace, Colour remap, Ordered dither, Wipe — read **nothing** from the generator. Their kernels take no `PixelInfo` at all (`RT/SpriteFx/SpriteFxBurst.cs:131,139,146,155,293,303`) or read only buffer coordinates (`:168`, `:204-207`). They are dead on the fire sims, Playback3D and the height consumer for one reason only: the recolour hook is called **inside each generator's draw loop** instead of over the finished buffer. That is wiring, not a requirement. Nothing about a finished RGBA buffer prevents any of them.
- **20 of the 41 effects are buffer-only today.** 10 Post + 1 Simulation + those 9 Pixel. The Post and Simulation stages already *are* universal — they run outside the generator dispatch entirely (`RT/Pyre/PyreRenderer.Layers.cs:174,176`), for every layer, including Playback3D which draws nothing. So the tool already contains a working demonstration of the owner's principle; it just stopped applying it one stage short.
- **20 read something that only exists mid-draw, and it is exactly two numbers.** All 17 geometry warps read the sample position, and 10 of them read the shape's **centre** and/or **radius** (`GeoCtx`, `RT/SpriteFx/SpriteFxModifiers.cs:180-190`). Three pixel effects read `crossFrac` (how far across *this shape*) or `hash` (a stable per-*shape* seed). Publish those two per-pixel planes — a shape-local coordinate and a shape seed — and 13 of the 20 become buffer-runnable **with their present meaning intact**. The other 7 need nothing but the buffer and already run that way elsewhere.
- **Exactly one effect is genuinely stuck, and it is stuck because it is meaningless on a buffer, not because it is hard.** `EdgeWarpModifier` roughens the silhouette *without* rippling the fill (`RT/SpriteFx/SpriteFxModifiers.cs:2558-2564`). A finished buffer cannot separate silhouette from fill; a buffer version would just be Wobble. **Separately and more damningly: it currently has no host at all** — Pyre's collector skips it (`RT/Pyre/PyreRenderer.cs:1272`), Pyre's add-menu excludes it (`ED/Pyre/PyreWindow.Modifiers.cs:384`), and the standalone stack's dispatch has no case for it (`RT/SpriteFx/SpriteFxBurst.cs:640-668`). **The "edge" stage is a stage with zero consumers.** Report 1 §6 describes five live attachment points; there are four.
- **The true cost of universality is not "effects break" — it is a pivot moving.** If everything ran on the finished buffer inside Pyre: 20 effects change nothing at all; 7 change only by resampling quality; **13 move their pivot from the individual particle to the canvas and their scale from the particle's radius to `min(W,H)/2`.** On a single centred shape the pivot is already identical and only the scale changes. On a **swarm** the loss is real and undialable: N per-particle pivots collapse to one.
- **A bug fell out of this.** The standalone stack pivots every shape-centred warp at the canvas's **top-right corner**, not its centre: `RunWarp` passes `center = (W/2, H/2)` into a field documented and used everywhere else as *an offset from the canvas centre* (`RT/SpriteFx/SpriteFxBurst.cs:683` vs `RT/Pyre/PyreRenderer.cs:3017`, `RT/Pyre/PyreFormWarp.cs:34`, `RT/SpriteFx/SpriteFxModifiers.cs:180-183`). Report 1 §6 says the standalone version is "centred on the canvas". It is not; it is centred one full canvas-half up and right of that. Ten of the seventeen warps are affected.

---

## 1. The five attachment stages, re-verified

The five type-level hooks exist and are correctly named by report 1. Their real signatures:

| Stage | Hook | Declared at |
|---|---|---|
| bend / geometry | `Vector2 InverseWarp(Vector2 off, float phase, in GeoCtx ctx)` | `RT/SpriteFx/SpriteFxModifiers.cs:194` (class `:191`) |
| recolour / pixel | `bool ApplyPixel(ref Color col, ref float alpha, in PixelInfo info)` | `RT/SpriteFx/SpriteFxModifiers.cs:664` (class `:661`) |
| post | `void Apply(Color32[] buf, int W, int H)` | `RT/SpriteFx/SpriteFxModifiers.cs:1747` (class `:1745`) |
| edge | `float EdgeOffset(float angleRad, int hash, in GeoCtx ctx)` + `float EdgeSoftness(...)` | `RT/SpriteFx/SpriteFxModifiers.cs:2568`, `:2575` (class `:2565`) |
| simulation | `ResetState(Color32[] seedBuf,…)` / `Step(…)` / `Render(Color32[] buf,int W,int H)` | `RT/SpriteFx/SpriteFxSimulationModifiers.cs:66,68,71` (class `:44`) |

The two context structs are the whole of what a generator ever hands an effect:

- `GeoCtx { hHalf, vHalf, Vector2 center, radius }` — `RT/SpriteFx/SpriteFxModifiers.cs:180-190`. `center` is documented as **"the shape centre as an offset from the canvas centre"** and every reader uses it that way (`off - ctx.center`, e.g. `:366`, `:411`).
- `PixelInfo { x, y, frame, wx, wy, crossFrac, life, hash, W, H }` — `RT/SpriteFx/SpriteFxModifiers.cs:642-657`. `wx/wy` = the geometry-warped sub-pixel position; `crossFrac` = 0..1 across the shape; `life` = the shape's own life; `hash` = a stable per-pixel seed.

### Which generator calls which hook (measured, not inferred)

Mapped by function span over `RT/Pyre/PyreRenderer.cs` and checking each body for `ApplyGeo(` / `ResolveSample(` / `ApplyPix(`:

| Draw routine | lines | bend | recolour |
|---|---|---|---|
| `DrawParticle` (Disc) | 2810-3104 | ✔ `:3038` | ✔ `:3091` |
| `ResolveSample` (shared fold) | 3105-3139 | ✔ | — |
| `DrawCrescentBody` | 3140-3253 | ✔ | ✔ `:3218` |
| `DrawStarBody` | 3254-3374 | ✔ | ✔ `:3357` |
| `DrawPolygonBody` | 3375-3469 | ✔ | ✔ `:3455` |
| `DrawSparkleBody` | 3470-3552 | ✔ | ✔ `:3540` |
| `DrawSpriteBody` | 3553-3634 | ✔ | ✔ `:3618` |
| `DrawStreakBody` | 3672-3803 | ✔ | ✔ `:3774` |
| `RenderPlusFusedField` (Fuse) | 1617-1696 | ✔ | ✔ `:1669` |
| `DrawTextChar` (Text) | 3925-4066 | ✘ | ✔ `:4046` |
| `DrawFacetSolid` (Gem/Box/Pyramid/Can) | 4152-4417 | ✘ | ✔ `:4386,4403` |
| `DrawOrb` | 4418-4608 | ✘ | ✔ `:4578,4593` |
| `DrawRing` | 4609-4791 | ✘ | ✔ `:4763,4778` |
| `RenderPlusRampField` (height relief) | 1697-1848 | ✘ | ✔ `:1832` |
| `RenderFireLayer` | 392-465 | ✘ | ✘ |
| `RenderPlusFireLayer` | 579-624 | ✘ | ✘ |
| `RenderFireballLayer` | 757-806 | ✘ | ✘ |
| `RenderHeightConsumer` | 1849-1894 | ✘ | ✘ |
| Playback3D | — returns immediately, `RT/Pyre/PyreRenderer.cs:269` | ✘ | ✘ |
| `RenderFormLayer` (plug-in forms) | 285-391 | **whole-buffer resample**, `:349` | forms call `ApplyPixel` themselves |

**Report 1 §6's tallies check out exactly.** Bend does nothing on Text, Gem, Box, Pyramid, Can, Orb, Ring, height-relief (8), climbing to 11 with the two fire sims and Playback3D. Recolour does nothing on the two fire sims, Playback3D and the mask-fed height consumer (4). Playback3D takes nothing because it draws nothing.

### Three corrections to the prior description

1. **The edge stage is not live.** `CollectMods` skips it with an explicit comment (`RT/Pyre/PyreRenderer.cs:1272`: *"EdgeModifier / SimulationModifier have no apply stage in Pyre's disc raster, so they are ignored here"*), Pyre's add-menu excludes it (`ED/Pyre/PyreWindow.Modifiers.cs:384`), and `SpriteFxStack.RunStack`'s dispatch switch has cases only for `GeometryModifier`, `PostModifier` and `PixelModifier`, with **no default** (`RT/SpriteFx/SpriteFxBurst.cs:640-668`) — an `EdgeModifier` in a saved list is silently ignored. `EdgeOffset` has zero call sites anywhere under `Assets/`. **Four live stages, one dead one.**
2. **Post and Simulation are already universal, and this is the strongest evidence for the owner's argument.** Both are invoked from `RenderLayerBody` *outside* the generator dispatch: `if (plan.hasPost) ApplyLayerPost(...)` at `RT/Pyre/PyreRenderer.Layers.cs:174`, `if (plan.hasLayerSim) ApplyLayerSim(...)` at `:176`. Nothing about the generator is consulted. A Playback3D layer that renders nothing still runs its post passes. Spec-wide posts run again at the end over the composited frame (`RT/Pyre/PyreRenderer.Layers.cs:346-361`).
3. **Simulation has no host in the standalone stack.** `SpriteFxSpec` has a single flat `List<PyreModifier> modifiers` (`RT/SpriteFx/SpriteFxSpec.cs:32`) with no simulation slot, and `RunStack` has no `SimulationModifier` case. `PixelFluidModifier` is therefore a silent no-op in the standalone tool. No prior report says this.

---

## 2. The load-bearing classification — does the effect need anything besides a finished RGBA buffer?

**Counts: (a) buffer-only = 20 · (b) needs per-sample context = 20 · (c) irreconstructible = 1.**

Method: for each effect I read its `ApplyPixel`/`InverseWarp`/`Apply` body and, where it delegates, the kernel it delegates to (`SfxKernels`, `RT/SpriteFx/SpriteFxBurst.cs:82-320`). "Reads" below is what the code actually touches, not what its docs imply.

| # | Effect | Stage | Declared | Reads beyond the buffer | Class | Reconstructible from a buffer? What is lost? |
|---|---|---|---|---|---|---|
| 1 | Dissolve | Post | SFxM:1157 | host `life`/`seed`/`frame` only | **a** | n/a — already buffer-only |
| 2 | Bloom | Post | SFxM:1787 | — | **a** | n/a |
| 3 | Outline | Post | SFxM:1887 | — | **a** | n/a |
| 4 | Chromatic aberration | Post | SFxM:2102 | — | **a** | n/a |
| 5 | Ballistic shockwave | Post | SFxM:2200 | host `life` | **a** | n/a |
| 6 | Fuse | Post | SFxM:2459 | host `life` | **a** | n/a |
| 7 | Edge smooth | Post | SFxM:2734 | — | **a** | n/a |
| 8 | Drop shadow | Post | SFxM:3386 | — | **a** | n/a |
| 9 | Kaleidoscope | Post | SFxM:3481 | host `seed` | **a** | n/a |
| 10 | Relight | Post | Relight:243 | host picture rect (`SetPicture`) | **a** | n/a. Note Pyre never calls `SetPicture` — `SetPostContext` sets only life/seed/frame (`PyreRenderer.cs:1379-1394`) — so it falls back to `W,H` (`Relight:472-474`). Harmless today because Pyre never pads. |
| 11 | Pixel fluid | Sim | SFxSim:115 | seeds itself **from the finished composited buffer** (`ResetState(seedBuf,…)`, SFxSim:66) | **a** | n/a — it is *defined* as a buffer operation. Its dedicated slot exists for replay determinism (SFxSim:30-44), not for generator data. |
| 12 | Contrast | Pixel | SFxM:722 | **nothing** — `KContrast` takes no `PixelInfo` (Burst:131) | **a** | Dead on 4 generators for no reason at all. |
| 13 | Brightness | Pixel | SFxM:738 | **nothing** (Burst:139) | **a** | as above |
| 14 | Saturation | Pixel | SFxM:754 | **nothing** (Burst:146) | **a** | as above |
| 15 | Posterize | Pixel | SFxM:869 | **nothing** (Burst:155) | **a** | as above |
| 16 | Colour tint (wash) | Pixel | SFxM:1450 | **nothing** (Burst:293) | **a** | as above |
| 17 | Colour replace | Pixel | SFxM:1602 | **nothing** (Burst:303) | **a** | as above |
| 18 | Colour remap | Pixel | Remap:142 | `info.life` only (Remap:186) | **a** | `life` is host progress, not generator geometry. |
| 19 | Ordered dither | Pixel | SFxM:902 | `p.x, p.y` (Burst:168) | **a** | Buffer coordinates. Identical on a buffer pass. |
| 20 | Wipe / alpha mask | Pixel | SFxM:1299 | `p.W, p.H, p.x, p.y` (Burst:204-207); `p.hash` **only** in the Noise shape (Burst:234) | **a** | Already canvas-relative inside Pyre (`p.W` is the canvas). Only the Noise shape's speckle changes (per-particle seed → per-pixel seed). |
| 21 | Skew | Geo | SFxM:203 | `off`, `phase` | **b** | Yes — a whole-buffer resample is the same map. Lost: sub-pixel precision (nearest-neighbour) and anything already clipped off-canvas. |
| 22 | Scale | Geo | SFxM:228 | `off`, `phase` | **b** | as 21 |
| 23 | Rotate | Geo | SFxM:266 | `off`, `phase`, `ctx.hHalf/vHalf` | **b** | Yes, exactly — canvas halves are buffer-derivable. |
| 24 | Wobble | Geo | SFxM:302 | `off`, `phase` | **b** | as 21 |
| 25 | Curl (progress) | Geo | SFxM:3007 | `off`, `phase` | **b** | as 21 |
| 26 | Smudge | Geo | SFxM:3167 | `off`, `phase` | **b** | as 21 |
| 27 | Pin warp | Geo | SFxM:3340 | `off`, `phase`, own frame index | **b** | as 21 |
| 28 | Sunburst wobble | Geo | SFxM:339 | **`ctx.center`** (`:366`) | **b** | Approximately — centroid of the alpha field. Lost: on a swarm, N pivots → 1. |
| 29 | Ring wave | Geo | SFxM:386 | **`ctx.center`** (`:411`) | **b** | as 28 |
| 30 | Point blast | Geo | SFxM:437 | **`ctx.center`** | **b** | as 28 |
| 31 | Sunburst | Geo | SFxM:779 | **`ctx.center`** | **b** | as 28 |
| 32 | Turbulence | Geo | SFxM:2653 | **`ctx.center`**, seed | **b** | as 28 |
| 33 | Curl | Geo | SFxM:2882 | **`ctx.center`**, seed | **b** | as 28 |
| 34 | Profile | Geo | SFxM:546 | **`ctx.center` + `ctx.radius`** | **b** | Approximately — centroid + radius of gyration. Lost: the exact scale (see §3). |
| 35 | Pulse rings | Geo | SFxM:830 | **`ctx.center` + `ctx.radius`** | **b** | as 34 |
| 36 | Sphere (fake depth) | Geo | SFxM:3084 | **`ctx.center` + `ctx.radius`** (`:3122-3124`) | **b** | as 34. Its own `radius` dial already overrides `ctx.radius` when non-zero (`:3122`) — the pattern for how the rest could opt out of needing it. |
| 37 | Ground | Geo | SFxM:583 | **all four `GeoCtx` fields**, seed, frame; raises `WarpPass` so it reframes first | **b** | as 34, plus its pass ordering must survive. |
| 38 | Tint | Pixel | SFxM:686 | **`p.crossFrac`** (`:707`) | **b** | Yes — `MakePixel` already reconstructs a `crossFrac` from picture centre + half-size (Burst:113-116). Lost: the cross-gradient re-bases from *per-shape* to *per-canvas*. |
| 39 | Voronoi crack | Pixel | SFxM:942 | **`p.hash`** (`:1051`), **`p.wx/wy`** (`:1055`) | **b** | `wx/wy` reconstructs exactly if the warp already ran on the buffer. `hash` reconstructs as a positional hash. Lost: per-particle crack coherence. |
| 40 | Layer dissolve | Pixel | SFxM:1247 | **`p.wx, p.wy, p.hash, p.frame`** (Burst:189-192) | **b** | as 39. Lost: it can no longer eat *whole particles*, only a uniform speckle. |
| 41 | Edge warp | Edge | SFxM:2585 | **a per-angle boundary test around a shape centre**, plus `hash` | **c** | **No.** See below. |

*(SFxM = `RT/SpriteFx/SpriteFxModifiers.cs`; Burst = `RT/SpriteFx/SpriteFxBurst.cs`; SFxSim = `RT/SpriteFx/SpriteFxSimulationModifiers.cs`; Remap = `RT/SpriteFx/SpriteFxColorRemap.cs`; Relight = `RT/SpriteFx/SpriteFxRelight.cs`.)*

### Proof for the single (c)

`EdgeWarpModifier`'s contract is a **radius delta added to the outer-boundary test at a given angle**, applied *without touching anything downstream that reads the shape's true geometry — colour fill, the gradient core, hole sizing* (`RT/SpriteFx/SpriteFxModifiers.cs:2558-2564`). Two things make that irreconstructible from a buffer:

1. A finished buffer has **no separation between silhouette and fill**. Displacing the alpha boundary on a buffer necessarily drags the colour that sits at that boundary with it — which is precisely what the class exists *not* to do, and precisely what `GeometryModifier` already does.
2. A buffer of a **swarm** has one merged alpha field and no per-shape centres, so "the angle around the shape centre" has no referent. Pyre gives each particle its own `GeoCtx` (`RT/Pyre/PyreRenderer.cs:3017`); a buffer cannot recover that partition.

So Edge warp is not "hard to make universal" — a universal version would be a different, already-existing effect. It is the one honest **cannot**, and §7 argues it is also a **useless-if-you-did**.

### The one-line answer to the owner's question

Of the 41, **20 need nothing but pixels**, and 11 of those 20 already run on every generator. The other 9 are dead on four generators purely because the recolour hook is a callback inside each draw loop rather than a pass over the finished buffer. That is a wiring accident, exactly as he suspects. The remaining 20 need **two numbers per pixel** — a shape-local coordinate and a shape seed — both of which the code already computes and already passes; they are simply not published anywhere a buffer pass could read them.

---

## 3. "Same effect, different meaning" — verified, and worse than reported

There are **three** hosts for a geometry warp in this codebase, not two, and all three build `GeoCtx` differently:

| Host | `hHalf, vHalf` | `center` | `radius` | sampling |
|---|---|---|---|---|
| Pyre per-sample (Disc/Crescent/Star/Polygon/Sparkle/Sprite/Streak/Fuse) — `RT/Pyre/PyreRenderer.cs:3017` | `W/2, H/2` | **`(cx − W/2, cy − H/2)`** = this particle's centre as an offset | **this particle's own radius** | none — the warped offset feeds the shape's analytic distance test (`:3070-3074`) |
| Pyre plug-in forms — `RT/Pyre/PyreFormWarp.cs:34` | `W/2, H/2` | **`Vector2.zero`** = canvas centre | **`min(W,H)/2`** | nearest-neighbour, **one fused map for the whole chain** (`:41-43`) |
| Standalone stack — `RT/SpriteFx/SpriteFxBurst.cs:683` | `W/2, H/2` | **`(W/2, H/2)`** | **`min(W,H)/2`** | nearest-neighbour, **one resample per modifier** (`:698-705`) |

### The pivot bug

`GeoCtx.center` is documented as *"the shape centre as an offset from the canvas centre"* (`RT/SpriteFx/SpriteFxModifiers.cs:180-183`), Pyre computes it that way (`PyreRenderer.cs:3017`), `PyreFormWarp` passes `Vector2.zero` consistently with it (`PyreFormWarp.cs:34`), and every consumer subtracts it from an offset that is itself measured from the canvas centre (`off - ctx.center`, `SpriteFxModifiers.cs:366`, `:411`).

`SpriteFxStack.RunWarp` builds `off = (x + 0.5 − W/2, y + 0.5 − H/2)` — the same convention — and then passes `center = (W/2, H/2)`. So `off − ctx.center = (x + 0.5 − W, y + 0.5 − H)`: **the pivot lands at absolute pixel `(W, H)`, the top-right corner of the canvas.** On a 64×64 canvas that is 45.3 px from where `PyreFormWarp` puts it, and the entire picture sits in one quadrant relative to the pivot — a starburst becomes an off-screen fan, a ring wave becomes a corner ripple.

**Report 1 §6 is wrong on this point.** It says the standalone version "resamples the whole finished canvas, centred on the canvas". It is centred on the canvas's *corner*. Ten of the seventeen warps (rows 28-37) read `ctx.center` and are affected. This is a one-line fix (`Vector2.zero`) and should be raised separately from the universality question; note it is behaviour-changing for any saved standalone stack that uses those ten.

### The sharper question — what would change inside Pyre if everything ran on the finished buffer?

Costed against the table in §2, assuming a buffer stage that uses the `PyreFormWarp` conventions (canvas-centred, `radius = min(W,H)/2`):

- **20 effects change nothing whatsoever** (rows 1-20). They already ignore everything a generator could tell them.
- **7 geometry effects change only by resampling** (rows 21-27): nearest-neighbour quantisation instead of an analytic fold, and clipping at the canvas boundary. Under the owner's "outside the frame is allowed" ruling this second one matters *more*, not less — see §4.
- **13 effects move their pivot and/or their scale** (rows 28-37 geometry, plus Tint/Voronoi/Layer dissolve for `crossFrac`/`hash`):
  - **On a single, centred shape the pivot is unchanged.** `DrawParticle` for a non-swarm layer is called at `cx = W*0.5f, cy = H*0.5f` (`RT/Pyre/PyreRenderer.cs:280`), so `center` is already `(0,0)` — identical to the buffer stage. **Only the scale changes**: `ctx.radius` goes from the particle's own radius to `min(W,H)/2`. For a disc occupying a quarter of the canvas that is a **2× change in the effect's reach** at the same Strength dial — Sphere's lens, Profile's taper and Pulse rings' ring spacing all double. That is a re-tune, not a break, and Sphere already ships the escape hatch: its own `radius` dial overrides `ctx.radius` when non-zero (`SpriteFxModifiers.cs:3092-3101,3122`).
  - **On a swarm the loss is real and has no dial.** Every particle currently gets its own `GeoCtx`; a buffer pass has one. A per-particle wobble becomes a whole-layer wobble. `PyreFormWarp`'s own comment already concedes this and calls a per-instance warp *"a possible later refinement, not built"* (`PyreFormWarp.cs:14-16`).
  - **Tint's cross-gradient re-bases** from `d / particleRadius` (`PyreRenderer.cs:3090`) to distance-from-picture-centre / `min(halfW,halfH)` (`SpriteFxBurst.cs:113-116`). A gradient painted centre→edge of each ember becomes one painted centre→edge of the frame.
  - **`hash` re-bases** from a per-particle seed (`PyreRenderer.cs:3019`) to a per-pixel positional hash (`SpriteFxBurst.cs:117`). Layer dissolve stops eating whole particles and starts eating uniform speckle.
- **1 effect (Edge warp) has nowhere to go** and 1 (Pixel fluid) is already there.

**Nobody has costed this before; that is the cost.** It is smaller than the earlier reports imply for single-shape layers and larger for swarms.

---

## 4. Per-sample warping vs resampling the finished buffer — when are they genuinely different?

Report 2 §12 slice 5 cites `InfernoForm.HandlesGeometry => true` as the codebase's own proof of non-equivalence. **That citation is correct and the line is real** (`RT/Pyre/Forms/Kiln/InfernoForm.cs:19`), but the reason it gives is not the one in the code.

Inferno does **not** opt out because a resample looks worse. It opts out because it would otherwise be **warped twice**: it already applies `ctx.geo` per field sample (`RT/Pyre/Forms/Kiln/PyreInferno.cs:580`), and `RenderFormLayer` would then apply the generic map on top (`RT/Pyre/PyreRenderer.cs:349`, `if (mods.AnyGeo && !form.HandlesGeometry)`). `PyreForm.cs:197-201` states this plainly: *"Override to true ONLY if you apply ctx.geo per sample yourself (Inferno does, so its containment follows the warp)"*, and `PyreFormWarp.cs:8-9` says the same. **Report 2 §12 slice 5 and report G finding 26 both read this as an aesthetic opt-out; it is a double-application guard.** The equivalence question is genuinely open in the code, not settled against it.

That said, there **are** four places where the two are genuinely different, all citable:

1. **Chained resampling compounds; a folded map does not.** Pyre folds the entire warp chain analytically and samples **once**: `ApplyGeo` walks the whole `geo[]` array applying `InverseWarp` in reverse and returns a single offset (`RT/Pyre/PyreRenderer.cs:1336-1342`), used once per pixel. `PyreFormWarp.BuildMap` does the same for plug-in forms — one fused map (`PyreFormWarp.cs:41`). But the **standalone stack resamples once per modifier**: `case GeometryModifier g: … RunWarp(px, W, H, g, life)` inside the per-modifier loop (`SpriteFxBurst.cs:645-648`). Three stacked warps in the standalone tool are three successive nearest-neighbour resamples of an 8-bit buffer; the same three in Pyre are one. This is a measurable, monotone quality loss that the folded path does not have, and it is the strongest technical argument in the file.
2. **A buffer has already been clipped; a shape has not.** When any geometry warp is active, `DrawParticle` widens its raster scan to the **entire canvas** — `if (anyGeo) { px0 = 0; py0 = 0; px1 = W-1; py1 = H-1; }` with the comment *"a warp can pull any pixel into the disc"* (`RT/Pyre/PyreRenderer.cs:3020`). A resample cannot do that: `RunWarp` and `BuildMap` both read `default` (transparent) for any source outside the buffer (`SpriteFxBurst.cs:704`, `PyreFormWarp.cs:43,53`). **Under the owner's ruling that drawing outside the frame is allowed and expected, this becomes the dominant difference**: content that legitimately went off-canvas is simply absent from the buffer and can never be warped back in, whereas the per-sample path re-derives it from the shape's analytic definition. Anything that was going to rely on "the user is responsible for framing" needs the buffer stage to run on a **padded** buffer, not the cropped one — the machinery for which already exists (`SpriteFxStack.OutwardReach`, `SpriteFxBurst.cs:492-505`, and the padded `RunStack` overload at `:596`) but is **not wired into Pyre**: `SetPostContext` sets only life/seed/frame (`PyreRenderer.cs:1379-1394`) and never `SetPicture`.
3. **8-bit round-trip per pass.** Pyre composites through `Over` into `Color32` (`RT/Pyre/PyreRenderer.cs:4957-4969`), so every buffer pass reads and writes quantised 8-bit straight alpha. A per-sample warp operates on the pre-composite float colour and alpha (`ApplyPix` takes `ref Color`, `ref float alpha`, `:1343`). Soft alpha repeatedly resampled and requantised erodes; folded once, it does not.
4. **Supersampling would break it — but is not currently in play.** `PyreSupersample` renders at k× and box-filters down with premultiplied alpha (`RT/Pyre/PyreSupersample.cs:24-33`), which would make a post-hoc 1× nearest-neighbour resample discard exactly the antialiasing it paid for. **I must report honestly: `PyreSupersample.Render` has no callers.** The only occurrence in `Runtime/` is its own doc comment (`PyreSupersample.cs:7`). So this is a future hazard, not a present one.

**The honest general answer.** Per-sample warping and whole-buffer resampling are mathematically equivalent *only* when (i) exactly one warp is applied, (ii) the source raster extends at least as far as the warp reaches, (iii) sampling is exact — i.e. the map is an integer permutation, or you accept nearest-neighbour, and (iv) nothing has been quantised or antialiased between drawing and warping. Condition (i) fails in the standalone stack by construction. Condition (ii) fails whenever the picture is cropped, which the owner's framing ruling makes normal. Conditions (iii) and (iv) fail whenever a shape has a soft edge, which is Pyre's default (`edge = 1 − InverseLerp(inner, radius, d)`, `PyreRenderer.cs:3075`). So: **not equivalent in general, and the gap is dominated by chained resamples and by clipping, not by the pivot.**

---

## 5. Ordering

Report 2 §7.5's claim — that splitting one list into four destroys ordering in two places — is **verified in both places, and understates the situation**: the two hosts already disagree about ordering *today*, for the same saved list.

**Pyre already runs three fixed buckets, not an authored order.** `CollectMods` walks the list once, pushing `GeometryModifier`s into one bucket and `PixelModifier`s into another, skipping `PostModifier` entirely (`RT/Pyre/PyreRenderer.cs:1256-1290`, the `if (m is PostModifier) continue;` at `:1283`). The geometry bucket is then **sorted by `WarpPass`** (`:1305-1315`), so even *within* geometry the authored order is not authoritative. Pixels are appended in list order. So in Pyre, "pixel effect between two geometry effects" **is already inexpressible**. Post *is* ordered — `ApplyLayerPost` walks the authored list and applies each `PostModifier` in that order (`:1354-1368`); spec-wide posts likewise (`PyreRenderer.Layers.cs:346-361`). Then simulation last (`Layers.cs:176`).

**The standalone stack honours strict authored order.** `RunStack` walks the list in order, batching only *adjacent* Burst-shaped pixel effects, and **flushes** before every geometry, post, or unshaped pixel effect — *"everything below must see the pixels the batch produced"* (`RT/SpriteFx/SpriteFxBurst.cs:626-670`, flush at `:638`). Warp-between-two-tints is fully expressible there.

**So the same `List<PyreModifier>` means different things in the two hosts, right now.** That is a bigger fact than the four-way-split hazard and neither prior report states it.

**Does universality make ordering better or worse? Better — decisively — provided universality means "one stage, authored order", which is what it should mean.** If every effect can run over the buffer, then the natural implementation is the one `RunStack` already has: walk the list once, in order, flushing between passes. Ordering becomes **total and expressible in both hosts**, and Pyre's three-bucket approximation disappears. Universality would *fix* the ordering divergence, not aggravate it.

It gets worse only under the four-typed-lists design of report 2 §7.5 — which is the opposite of universality. That design takes an effect set that is *mostly* buffer-only and partitions it by a stage distinction that the universality argument is trying to dissolve. **The two proposals are in direct tension and should not both be adopted.**

One real cost survives either way: `WarpPass` (`SpriteFxModifiers.cs:198-200`, used by Ground to reframe before shape-local warps read the silhouette). Under strict authored order, `WarpPass` becomes redundant at best and a second, conflicting ordering authority at worst. It needs an explicit decision.

---

## 6. What universal effects would actually cost — the four buckets

| Bucket | Count | Members | What it takes |
|---|---|---|---|
| **(0) Universal today, correctly advertised** | **11** | 10 Post + Pixel fluid | Nothing. Already run outside the generator dispatch (`PyreRenderer.Layers.cs:174,176`). *Caveat: Pixel fluid has no host in the standalone stack — `SpriteFxSpec.cs:32` has no slot and `RunStack` has no case.* |
| **(i) Universal-capable today, mis-advertised** | **9** | Contrast, Brightness, Saturation, Posterize, Colour tint, Colour replace, Colour remap, Ordered dither, Wipe | **Move the recolour stage out of the draw loop, or add a buffer fallback for generators that skip it.** These read nothing (or only buffer coordinates). Zero behavioural cost on the generators that already support them; they light up on the two fire sims, Playback3D and the height consumer. **This is the owner's point, unqualified, and it is the cheapest win in the report.** |
| **(ii) Universal via a buffer fallback, at a named cost** | **7** | Skew, Scale, Rotate, Wobble, Curl (progress), Smudge, Pin warp | **Cost: nearest-neighbour quantisation + canvas clipping + one resample per chained warp.** Mitigation: fold the chain into a single map (as `PyreFormWarp.BuildMap` already does, `PyreFormWarp.cs:41`) rather than resampling per modifier as `RunStack` does; and run on a padded buffer sized by `OutwardReach`. |
| **(iii) Universal if the generator publishes two extra per-pixel planes** | **13** | Sunburst wobble, Ring wave, Point blast, Sunburst, Turbulence, Curl, Profile, Pulse rings, Sphere, Ground, Tint, Voronoi crack, Layer dissolve | **The data: (1) a per-pixel shape-local normalised coordinate — `crossFrac` is already exactly one scalar of it (`PyreRenderer.cs:3090`), plus the shape's centre offset and radius; (2) a per-pixel stable shape seed — `hash` is already exactly that (`PyreRenderer.cs:3019`).** Both are computed today and thrown away at the end of the draw loop. Publishing them as planes alongside RGBA — the mechanism `PyreFormWarp.Apply(float[] plane, int[] map)` already exists for (`PyreFormWarp.cs:57-62`) — makes all 13 buffer-runnable **with their present per-shape meaning intact, swarms included**. This is the single highest-leverage change in the report. |
| **(iv) Genuinely stuck** | **1** | Edge warp | Needs a per-angle boundary test the generator performs; irreconstructible on a merged buffer (§2). Also currently hostless. |

**Total 41.** For the tool to say "every effect works on every generator", three things must be true: (1) the recolour stage runs over a buffer, not inside a draw loop; (2) generators publish a shape-local coordinate plane and a shape-seed plane, with a documented fallback (canvas-centred, `min(W,H)/2`, positional hash) for generators that genuinely have no shapes — the fire sims, the height field; (3) the buffer the stage runs on is padded by `OutwardReach`, which requires Pyre to start calling `SetPicture` (it does not today, `PyreRenderer.cs:1379-1394`). Edge warp is then either deleted or promoted to a real per-generator capability, but it is not part of "universal".

---

## 7. The honest counter-argument

**Three real objections. I am not softening them, and I am not manufacturing a fourth.**

**1. Edge warp would be *useless*, not merely impossible — and this is the strongest single argument against treating universality as an unconditional goal.** Its entire reason to exist is stated in its own doc: it roughens the rim *"without touching anything downstream that reads the shape's true geometry — colour fill …, the gradient core, hole sizing"*, giving *"a jagged-but-smoothly-shaded look … that a whole-frame GeometryModifier warp can't give, since that warps the fill along with the silhouette"* (`RT/SpriteFx/SpriteFxModifiers.cs:2558-2564, 2578-2581`). A buffer version cannot separate silhouette from fill, so it would produce exactly the picture Wobble already produces. **Making it universal would be shipping a duplicate under a name that promises something else.** That is worse than admitting it is generator-specific. The right move is to give it a real host — the disc/crescent/sparkle raster, which is what it was written for — or delete it. Not to universalise it.

**2. Per-particle behaviour on a swarm is genuinely lost, and no dial recovers it.** Ten warps currently pivot on *each particle* (`PyreRenderer.cs:3017`); a buffer pass pivots on the canvas. `PyreFormWarp` already lives with this and explicitly logs it as unbuilt (`PyreFormWarp.cs:14-16`). For a 200-ember explosion, "each ember wobbles" and "the whole cloud wobbles" are different effects, and only one of them is what the dial says. **Bucket (iii) is the answer to this — publish the shape-local plane — but if bucket (iii) is not built, universality for those ten is a downgrade dressed as a feature.**

**3. Chained resampling degrades, and universality multiplies the number of resamples.** The standalone stack already resamples once per geometry modifier (`SpriteFxBurst.cs:645-648`) rather than folding the chain as Pyre does (`PyreRenderer.cs:1336-1342`). A universal buffer stage naively implemented inherits the bad pattern, and every extra effect a user adds costs another 8-bit nearest-neighbour pass. This is fixable — fold the map — but it is a real engineering obligation, not a footnote.

**Two objections I checked and will NOT make:**

- **"An effect must stay inside the silhouette" is dead.** Under the owner's ruling, effects that draw beyond the shape are fine. Five effects declare `OutwardReachPx` (Bloom, Outline, Chromatic aberration, Drop shadow, Relight, `SpriteFxModifiers.cs:1789`, `:1889`, and siblings) and the summing machinery exists (`SpriteFxBurst.cs:492-505`). Any earlier conclusion that gated an effect on staying inside the silhouette should be dropped. **What survives is a narrower, real bug: Pyre never calls `SetPicture`, so a post pass that normalises a position measures against the buffer rather than a picture rect (`PyreRenderer.cs:1379-1394` sets only life/seed/frame; `SpriteFxRelight.cs:472-474` falls back to `W,H`). Harmless while Pyre never pads — a live bug the moment it honours `OutwardReach`, which universality requires it to.**
- **"Combinatorial dial explosion" is not an objection to universality.** Report 2 §7.5 warns that each warp becomes three effects (bend shape / bend fill / bend both). That explosion comes from the **shape/fill/border split**, not from universality. A universal buffer stage bends shape and fill together — exactly what happens today — and adds no dials at all.

---

## Confidence and limits

**Measured from source:** the five hook signatures and their declaration sites; the complete 41-effect roster and every effect's stage (enumerated programmatically from class declarations, cross-checked to 41, matching the prior reports' total); every effect's actual reads of `PixelInfo`/`GeoCtx`, traced through `SfxKernels`; the per-generator hook matrix (function-span analysis of all 5,191 lines of `PyreRenderer.cs`); the three divergent `GeoCtx` constructions; the ordering behaviour of both hosts; the absence of any `EdgeOffset` consumer and of any `PyreSupersample.Render` caller.

**Inferred but strong:** the reconstructability verdicts in column 6 of §2 — they follow from what each effect reads, but no rendered A/B was performed (I am forbidden to open Unity). The 2× scale estimate in §3 is arithmetic from `ctx.radius`'s two definitions, not a measurement.

**Not verified:** that the top-right-corner pivot in `SpriteFxStack.RunWarp` is visually as wrong as the arithmetic says — it should be confirmed with one rendered frame before anyone changes the line, since the fix is behaviour-changing for saved stacks. Also unverified: whether the editor preview path reproduces the bake for any of the above.

**Prior reports corrected here:** report 1 §6 on the number of live stages (five → four; the edge stage has no consumer) and on where the standalone stack pivots ("centred on the canvas" → centred on the canvas's top-right corner). Report 2 §12 slice 5, and report G's finding 26 which endorses it, on *why* `InfernoForm` sets `HandlesGeometry => true` (an aesthetic opt-out → a double-application guard, per `PyreForm.cs:197-201` and `PyreFormWarp.cs:8-9`); the equivalence question they treat as settled is in fact still open in the code. Report 2 §7.5 is right about the four-way split but omits that the two hosts already order the same list differently today.
