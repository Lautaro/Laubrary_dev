# T-0098 "Future of Pyre" — Investigator A: Generator Inventory

Research-only. No code changed, no Unity run, no Coplay used. Every load-bearing claim carries a `file.cs:line` citation. Paths below are relative to `D:\UNITY\Laubrary Dev\Assets\Packages\Laubrary\` unless written in full.

## 0. What "a generator" turns out to be, structurally

There are **four separate mechanisms** that decide what a layer draws, and they are checked in this priority order inside `RenderLayer` (`Runtime/Pyre/PyreRenderer.cs:241`):

1. **`PyreLayer.form`** — a `[SerializeReference] PyreForm` plug-in (`Runtime/Pyre/Pyre.cs:318`). If non-null it wins outright and the whole enum path is skipped (`PyreRenderer.cs:248-252`).
2. **`PyreLayer.shapeForm`** — the `ShapeForm` enum (`Runtime/Pyre/Pyre.cs:312`, enum at `Pyre.cs:111-117`).
3. **`PyreLayer.coalesce`** — the `LayerCoalesce` field-pass render mode (`Pyre.cs:268`, enum at `Pyre.cs:173`). Only reachable on the enum path with the swarm ON, and it **discards the chosen form entirely**.
4. **`PyreLayer.heightFromChannel >= 0`** — the matte-heightmap consumer, which also discards the layer's shape (`Pyre.cs:233`, `PyreRenderer.Layers.cs:105`, `:168`).

That layering is itself the single biggest finding: "which generator is this layer?" cannot be answered by reading one field today.

## 1. Summary table

Maturity is my judgement from dial count + tooltip coverage + whether it has bespoke editor UI + whether the code calls itself a stub/POC. "Runtime" = does it produce pixels through `PyreRenderer.RenderFrame`, which is what `PyreBlastPlayer` calls live at runtime (`Runtime/Pyre/PyreBlastPlayer.cs:7-11`) and what `PyreBaker` bakes with (`Editor/Pyre/PyreBaker.cs:8-10`).

### 1a. Built-in `ShapeForm` enum cases

| Generator | Family | 2D/3D | Stateful? | Runtime? | Maturity |
|---|---|---|---|---|---|
| Disc | Particle stamp | 2D | No | Yes | Mature (the reference path, hash-gated byte-identical) |
| Crescent | Particle stamp | 2D | No | Yes | Mature |
| Ring | Particle stamp | **3D-lit** (analytic annulus) | No | Yes | Mature |
| Star | Particle stamp | 2D | No | Yes | Mature |
| Polygon | Particle stamp | 2D | No | Yes | Mature |
| Streak | Particle stamp | 2D | No | Yes | Mature |
| Sparkle | Particle stamp | 2D (lit cells) | No | Yes | Mature |
| Gem | Faceted 3D solid | **3D + lighting** | No | Yes | Mature |
| Box | Faceted 3D solid | **3D + lighting** | No | Yes | Mature |
| Pyramid | Faceted 3D solid | **3D + lighting** | No | Yes | Mature |
| Can | Faceted 3D solid | **3D + lighting** | No | Yes | Mature |
| Orb *(enum)* | Analytic 3D solid | **3D + lighting** | No | Yes | Mature |
| Sprite | **External input** | 2D | No | Yes (readable texture required) | Mature |
| Text | **External input** | 2D + optional extrusion | No | Partial — needs an explicit `textFont` at runtime | Mature |
| Fire | **Grid simulation** | 2D | **YES** | Yes | Mature |
| Fireball | **Grid simulation** | 2D | **YES** | Yes | Mature |
| Playback3D | **External input** | Real 3D scene | n/a | **NO — renders nothing** | **Proof-of-concept, self-declared** |
| Inferno *(enum)* | RETIRED slot | — | — | Draws as Disc | Dead |
| ForkBlast *(enum)* | RETIRED slot | — | — | Draws as Disc | Dead |

### 1b. `PyreForm` plug-ins (all in `Runtime/Pyre/Forms/Kiln/`)

| Generator | Family | 2D/3D | Stateful? | Runtime? | Maturity |
|---|---|---|---|---|---|
| Inferno | Whole-layer field | 2D (pseudo-normal lit) | No | Yes | Mature, swarm-native |
| Fork Blast | Whole-layer puff field | 2D | No | Yes | **Superseded by Explosive Jet (author's own words)** |
| Orb *(plug-in)* | Kiln contract port | 2D | No | Yes | Very mature (141 dials, 146 tooltips, 5 variants) |
| Torch | Kiln contract port | 2D | No | Yes | Mature (73 dials in `PyreTorch.TorchSettings`, 5 variants) |
| Arc Burst | Kiln contract port | 2D | No | Yes | Very mature (187 dials, 198 tooltips, 10 layouts) |
| Plasma Bloom | Kiln contract port | 2D | No* | Yes | Very mature (114 dials, 10 draws) — *has a whole-clip fit pre-pass |
| Jet | Kiln contract port | 2D | No | Yes | Mature (60 engine dials, 5 variants) |
| Radial Jet | Kiln contract port | 2D | No | Yes | Mature (+10 fork dials, 8 variants) |
| Explosive Jet | Kiln contract port | 2D | No | Yes | Most mature of the family (+89 fork dials, 10 variants, gen 7) |

### 1c. Field-pass / non-enum generators

| Generator | Family | 2D/3D | Stateful? | Runtime? | Maturity |
|---|---|---|---|---|---|
| Coalesce = Fuse (MetaBlob) | Swarm field pass | 2D | No | Yes | Mature |
| Coalesce = Ramp (HeightBalls) | Swarm field pass | 2D + relief lighting | No | Yes | Mature |
| Height consumer (`heightFromChannel`) | Matte field pass | 2D + relief lighting | No | Yes | Working, low-visibility |

---

## 2. Detailed entries — built-in enum forms

### 2.1 Disc — `PyreRenderer.cs:2810` (`DrawParticle`, the fall-through raster)

**Draws:** a flat soft disc, radius = `size`·sizeMul, rim feathered by `edgeSoftness`. It is the *default* (`Pyre.cs:312`) and the fallback every other form falls through to when it declines (Sprite with no readable texture, Text with no font).

**2D/3D:** 2D-in-plane. **State:** closed-form — the whole raster is a function of `(life, seed, particleIndex)` through the `Eval` funnel.

**Honours:** everything — `size`, swarm, `particleSpin`, lifetime window, fill/gradient, geometry+pixel modifiers, border (`PyreWindow.cs:1370`, `IsFlat2DBorderForm`).

**UI:** one `Edge` slider on top of the shared rows (`PyreWindow.cs:1333`). **Runtime:** yes.

### 2.2 Crescent — `PyreRenderer.cs:3140` (`DrawCrescentBody`)

A disc with a second offset disc masked out; own bite size / facing / push-out dials. 2D, closed-form. Honours everything; `edgeSoftness` feathers **both** rims (`PyreWindow.cs:1320-1322`). UI: `BuildCrescentRows` (`PyreWindow.cs:1336`). Runtime: yes.

### 2.3 Star — `PyreRenderer.cs:3254` (`DrawStarBody`)

Filled star polygon: N arms, tips at R = `size`·sizeMul, valleys at R·(1−`starLength`), plus `starBaseWidth` and `starSkew` (pinwheel). 2D, closed-form. Honours everything. UI: `BuildStarRows` (`PyreWindow.cs:1351`).

### 2.4 Polygon — `PyreRenderer.cs:3375` (`DrawPolygonBody`)

Flat filled regular convex N-gon. Same per-ray inside-test as Star with one edge per sector instead of a valley (`Pyre.cs:95-100` doc). 2D, closed-form. Honours everything. UI: `BuildPolygonRows` (`PyreWindow.cs:1354`). **This is a near-duplicate of Star** — same math, one parameter fewer.

### 2.5 Sparkle — `PyreRenderer.cs:3470` (`DrawSparkleBody`)

Random lit pixel-cells scattered inside the disc, twinkling deterministically per frame. 2D, closed-form (the twinkle is seeded by frame index, not carried). **Ignores:** `edgeSoftness` (hidden in the UI — `PyreWindow.cs:1320-1322`) and the Border box (`PyreWindow.cs:1370` comment excludes Sparkle). UI: `BuildSparkleRows` (`PyreWindow.cs:1339`).

### 2.6 Streak — `PyreRenderer.cs:3672` (`DrawStreakBody`)

Anchor-biased comet-tail capsule/rect; the particle sits at `streakAnchor` along it. 2D, closed-form.

**Ignores `size` entirely** — it has its own `streakLength`/`streakWidth` envelopes, and the shared Size row is hidden for it (`PyreWindow.cs:1315-1318`). It also gets a **unique swarm interaction**: `streakScaleLengthOnly` makes `swarmScaleByIndex` drive length only, not width (`PyreRenderer.cs:1531`) — the only form with a bespoke branch inside the swarm loop. `streakSoftTip` feathers both ends; `edgeSoftness` feathers the two long sides. UI: `BuildStreakRows` (`PyreWindow.cs:1348`).

### 2.7 Gem / Box / Pyramid / Can — `PyreRenderer.cs:4152` (`DrawFacetSolid`), geometry at `:4182-4184`

**True 3D with real lighting.** A vertex/face model is built (`BuildBoxGeometry` / `BuildPyramidGeometry` / `BuildCanGeometry`, `PyreRenderer.cs:4182-4184`; Gem is an octahedron), rotated, and shaded per-pixel with a point light + Blinn-Phong, hard edge lines and two staggered glows (`Pyre.cs:42-49`).

Closed-form. Honours `size` (the girdle radius — `Pyre.cs:326`), swarm, lifetime, fill. `particleSpin` is reinterpreted as **yaw about the vertical axis** rather than in-plane rotation (`Pyre.cs:645-647`). **Ignores `edgeSoftness`** (hidden — `PyreWindow.cs:1320-1322`) and the flat-2D Border box (they have their own edge lines). UI: shared `BuildSolidBox` (`PyreWindow.cs:1324-1331`).

Aspect/Depth are reinterpreted per solid (Box: height/z; Pyramid: apex height; Can: height) — one code path, three parameter readings.

### 2.8 Orb (enum) — `PyreRenderer.cs:4418` (`DrawOrb`)

**True 3D, analytic sphere with real lighting.** Its silhouette is a plain circle that never changes under spin/tilt; the *lighting frame* rotates instead, so the shading and specular hotspot roll around the ball (`Pyre.cs:51-54`). Closed-form. Same honouring profile as the facet solids. UI: `BuildSolidBox`; tooltip explicitly says "no geometry rows — a ball needs none" (`PyreWindow.cs:2636`).

**⚠️ Name collision:** this is *not* the same thing as the `OrbForm` plug-in (§3.3). Two different generators both called "Orb", both selectable from the same Shape section, drawing completely different things.

### 2.9 Ring — `PyreRenderer.cs:4609` (`DrawRing`)

Flat two-sided tilted annulus (a Saturn ring), analytic, reusing the 3D lighting/lines/glows math (`Pyre.cs:55-58`). Outer radius = `size`, hole = `size`·`ringInner`; `gemTilt` opens/closes the ellipse.

**Ignores the swarm's `orientDeg`** — documented as geometrically inert for a symmetric annulus, and `DrawRing` applies no roll at all (`PyreRenderer.cs:2864-2866`). Grouped with the 3D solids in the UI (`BuildSolidBox`), but listed in the 2D picker row (`PyreWindow.cs:1155`) — a small inconsistency.

### 2.10 Sprite — `PyreRenderer.cs:3553` (`DrawSpriteBody`) — **EXTERNAL-INPUT FAMILY**

Stamps a `Sprite`'s pixels, scaled to `2·radius / max(rw,rh)`, rotated by `particleSpin` + swarm `orientDeg`, optionally tinted by the gradient (`spriteTint`). Only two spec fields: `spriteImage`, `spriteTint` (`Pyre.cs:456-457`).

Closed-form, but **reads an external asset** and can *decline*: null sprite, null texture, or a non-readable texture makes it return `false` and fall through to Disc (`PyreRenderer.cs:3558-3560`). Not parallel-safe (`GetPixels32` is main-thread — `PyreRenderer.cs:203`). **Ignores `edgeSoftness`** and the Border box. Runtime: yes, provided the texture is import-flagged readable.

### 2.11 Text — `PyreRenderer.cs:3925` (`DrawTextChar`) + `:3859` (`RenderTextLine`) — **EXTERNAL-INPUT FAMILY**

Every character of a free string is one particle drawn from a **TMP SDF font atlas**: spatial gradient fill (`TextFillMode`, `Pyre.cs:123`), optional border, optional 3D extrusion (front face + darker sides). Swarm OFF ⇒ the string lays out as one centred line; ON ⇒ each character rides a swarm position.

**Overrides the swarm particle COUNT to the string length** (`PyreRenderer.cs:1942`) — the only form that rewrites a shared swarm parameter.

**Ignores the shared Fill row** — hidden in the UI because its per-char fill rules its colour (`PyreWindow.cs:1305-1307`). `size` is reinterpreted as character HEIGHT (`PyreWindow.cs:1300`).

**Runtime caveat (verified):** the font auto-pick is `#if UNITY_EDITOR` only (`PyreRenderer.cs:3841-3848`). At runtime a null `layer.textFont` means Text **falls back to Disc** (`PyreRenderer.cs:3843-3844`). With an explicit readable font it works at runtime. Not parallel-safe.

### 2.12 Fire — `PyreRenderer.cs:392` (`RenderFireLayer`) — **STATEFUL, VERIFIED**

**Claim in `Pyre.cs:94-96` that Fire is one of only two sim-backed forms: CONFIRMED.** A retained heat/fuel grid lives in a `ConditionalWeakTable<PyreLayer, FireSimEntry>` (`PyreRenderer.cs:386-387`); normal forward playback steps one frame, and any scrub/backward step/skip/param change triggers a **cold replay from frame 0**, cost O(f) (`PyreRenderer.cs:369-383`, `:433`). Invalidation is content-hash based (`PyreRenderer.cs:374`, hash builder at `:531-534`). Checkpointing is an explicit TODO at `PyreRenderer.cs:383`. So **frame 9 genuinely requires 0..8 having been stepped** — it just does that automatically and deterministically.

It **reuses SpriteFx's public `FireSim`/`FireParams`** (`Runtime/SpriteFx/FireSim.cs`) rather than owning the physics.

**Ignores:** `size` (Size row hidden — `PyreWindow.cs:1315-1317`), `particleSpin`, per-particle travel, the whole Position section (`PyreWindow.cs:1375`), `edgeSoftness`, the Border box. **Honours:** fill (as its ramp), `alpha`, lifetime window, and — opt-in — the swarm.

**Swarm exception, verified:** `layer.fireSwarmEmitters && layer.swarmEnabled` routes to a *parallel* implementation `RenderPlusFireLayer` (`PyreRenderer.cs:399-402`, `:579`) driving a Pyre-local `PyreFireSim` (`Runtime/Pyre/PyreFireSim.cs`) with one emitter per alive particle, on its own replay harness (`PyreRenderer.cs:557-568`). So the doc line "it ignores `size` and the swarm" (`Pyre.cs:79-80`) is **stale** — swarm support was added in slice 8.

UI: `BuildFireBox` (`PyreWindow.cs:1356-1358`) plus `fireSteps` and friends (`Pyre.cs:530-562`). Runtime: yes, but forces the whole spec serial (`PyreRenderer.cs:201`).

### 2.13 Fireball — `PyreRenderer.cs:757` (`RenderFireballLayer`) — **STATEFUL, VERIFIED**

The cheap "doom-fire" cellular flame: heat blooms outward from one central point, folded into `fireballArms` kaleidoscope wedges (`fireballMirror` alternates reflected wedges — `Pyre.cs:605`). Reuses SpriteFx's `FireballSim` (`Runtime/SpriteFx/FireballSim.cs`). Same replay harness pattern as Fire (`_fireballSims`, `PyreRenderer.cs:201-202`, `:261`).

**Genuinely single-source: no swarm support at all** (unlike Fire). Ignores `size`, `particleSpin`, travel, Position section, `edgeSoftness`, Border. Honours fill (as its ramp), `alpha`, lifetime window. UI: `BuildFireballBox` (`PyreWindow.cs:1359-1361`). Runtime: yes, serial.

### 2.14 Playback3D — `PyreRenderer.cs:269` — **EXTERNAL-INPUT FAMILY, PROOF-OF-CONCEPT**

Source is a real 3D **prefab** carrying ParticleSystem(s) (`Pyre.cs:614`), driven live in a `PreviewRenderUtility` scene by `Editor/Pyre/PyrePlayback3DPreview.cs` (443 lines) — auto-framed camera, bloom/glow grade, speed/scale/tint, `Simulate`-based scrubbing, and an optional point-filtered pixelated downsample so you can gauge how a baked pixel-art version would read.

**Renders NOTHING at runtime.** `RenderLayer` returns immediately for this form (`PyreRenderer.cs:269`), with a nine-line comment saying so explicitly ("honestly incomplete rather than faking a placeholder shape", `PyreRenderer.cs:262-268`).

**⚠️ Doc contradiction found:** `Pyre.cs:611-612` says the case is "a documented stub (a flat placeholder colour)". The code draws nothing at all. The `Pyre.cs` comment is stale; `PyreRenderer.cs:262-268` is authoritative.

Ignores `size` (row hidden, `PyreWindow.cs:1316`), the swarm, `particleSpin`, the Position section (`PyreWindow.cs:1375`), `edgeSoftness`, Border, and effectively the fill (its own `playbackTint` grades the image). Honours only the lifetime window (which does nothing, since it draws nothing) and its own 8 playback dials (`Pyre.cs:614-640`). UI: `BuildPlaybackBox` — a substantial preview surface for a generator that produces zero pixels.

### 2.15 Inferno / ForkBlast — RETIRED ENUM SLOTS

Both are `[System.Obsolete]` (`Pyre.cs:114-115`). **What survives:** the int values, so every later case (`Playback3D` = 18) keeps its serialized meaning. **What is dead:** there is no draw code for them at all — a layer still carrying one, with `form == null`, falls through to **Disc** (`Pyre.cs:108-110`). They are **absent from the editor picker** — `Forms3D`/`Forms2D`/`FormsSpecial` (`PyreWindow.cs:1148-1163`) list neither, so a user cannot select them any more. Their behaviour lives on as the `InfernoForm`/`ForkBlastForm` plug-ins (§3.1, §3.2).

---

## 3. Detailed entries — `PyreForm` plug-ins

### Structural facts that apply to ALL nine

`PyreFormCtx` (`Runtime/Pyre/PyreForm.cs`, struct fields listed at the top of `PyreFormCtx`) carries: `W, H, life, seed, layerSalt, fill, alpha, swarm[], geo[], pix[], phase, frameIndex, frameCount, Eval`.

**It does NOT carry `size` and it does NOT carry `particleSpin`.** So **every plug-in form structurally ignores the layer's `size` envelope and `particleSpin`** — they cannot read them. The only size influence available is `PyreSwarmInstance.sizeMul` (depth + scale-by-index), and the only rotation influence is `PyreSwarmInstance.orientDeg`. Each form instead owns its own scale/placement dials.

**Coalesce is unavailable to plug-in forms.** `RenderLayer` dispatches to `RenderFormLayer` and returns (`PyreRenderer.cs:248-252`) *before* `RenderSwarm`, which is the only place `LayerCoalesce` is read (`PyreRenderer.cs:1497-1498`). So Fuse/Ramp can never combine with a plug-in form.

**Geometry modifiers** are applied generically to a form's finished buffer unless the form sets `HandlesGeometry` (`PyreRenderer.cs:349-351`). Only `InfernoForm` opts out (`InfernoForm.cs:19`).

**Colour:** a form declaring `UsesFill => false` ignores the layer's Shape Fill entirely. Verified `UsesFill => false` on: `ArcBurstForm.cs:50`, `OrbForm.cs:51`, `PlasmaBloomForm.cs:121`, `TorchForm.cs:55`, `Jet/JetFormBase.cs:23` (so Jet, Radial Jet and Explosive Jet too). **Only `InfernoForm` and `ForkBlastForm` use `ctx.fill`** (1 hit each, measured).

**All nine honour** `alpha`, the layer lifetime window (gated upstream in `PlanFrame`, `PyreRenderer.Layers.cs:79-87`), pixel modifiers, and the swarm as *placement* (6 of them read `ctx.swarm`; the three Jet variants inherit it from `JetFormBase`).

### 3.1 InfernoForm — `Runtime/Pyre/Forms/Kiln/InfernoForm.cs:15`, render at `:173`

A volumetric fireball explosion: per-pixel density/heat/smoke field from a torn angular silhouette + metaball clumps, warped by seeded directional-sine noise, shaded by a density-gradient pseudo-normal light, with ignition flash and contained embers. Engine at `PyreInferno.cs` (914 lines).

**State: closed-form, verified.** `PyreInferno.cs:11-13` — "Unlike Fire/Fireball this is NOT a sim: every frame is a pure function of (params, life, seed) — events and embers are rebuilt deterministically per frame — so scrubbing is exact with no replay harness, no CWT cache, no content hash."

2D (pseudo-normal lighting, not real 3D geometry). Swarm-native: one blast per swarm instance placed in y-up NDC at its spawn moment (`InfernoForm.cs:193-207`). Uses `ctx.fill` as its ramp. `HandlesGeometry => true` (`:19`) so it warps per sample itself. 40 dials, 36 tooltips. Has a `progress` remap dial that "only works because every frame is closed-form" (`PyreInferno.cs:32-33`).

### 3.2 ForkBlastForm — `ForkBlastForm.cs:15`, render at `:154` — **SUPERSEDED**

A swarm-of-puffs radial detonation: hundreds of small anisotropic soft puffs thrown outward, shaped by drag/entrainment, union-accumulated into one heat field, shaded once. Engine `PyreForkBlast.cs` (450 lines). Closed-form, verified: `PyreForkBlast.cs:13-16` — "every puff's position/size/amplitude at a given moment is a pure function of (its slot index, the blast's own local clock, the dials) — no accumulated simulation state." Uses `ctx.fill`. 40 dials.

**Explicitly superseded.** `Jet/ExplosiveJetForm.cs:59-63`: "This form **REPLACES** `ForkBlastForm` / `PyreForkBlast` (ported from generation 5, which had no fracture, dust or shed swell, re-fitted the exposure per frame, used a default gradient for the EMBER ramp and lerped to grey for soot — Appendix D's 47-row divergence table). Every row of that table is closed here." It is still in the picker under group `"Explosions"` (`ForkBlastForm.cs:13`), i.e. **a live, selectable, obsolete generator**.

### 3.3 OrbForm — `OrbForm.cs:36`, render at `:490` — Kiln "Energy Projectile / agent2"

A glowing energy ball with a trailing wake travelling toward +x: additive energy field of soft kernels + looping plane-wave turbulence, speed-smeared and toned. Five **variants**, each a different program: Emberdrift (default), Wisp, Coronal, Membrane, and a fifth (shed filaments). 637 lines + `PyreOrb.cs` 655 lines. **141 dials, 146 tooltips** — the second-densest form in the package.

**State: closed-form.** The "smear"/"persistence trail"/"smearDecay" dials are a **spatial** blur along the travel axis within one frame (`OrbForm.cs:157-162` — "taps in source px", "9 at decay 0.80 ≈ 4 px of exposure trail"), not temporal accumulation. No frame-history buffer, no replay harness, no `PyrePrepassCache`.

`UsesFill => false` (`:51`) — carries its own ramps and answers `IPlusRampProbe`. Ignores `size`/`particleSpin` (structural).

### 3.4 TorchForm — `TorchForm.cs:39`, render at `:135` — Kiln "Flame / agent2"

A **grounded** flame — torch / brazier / campfire. ONE program (`heat_field`) run with five parameter sets ranged on MOTION: calm → pulsating → licking → rolling → violent. Default variant `barbs`. 385 lines + `PyreTorch.cs` 668 lines holding `TorchSettings` (`PyreTorch.cs:75`) with **73 dials**.

Closed-form (no prepass cache, no replay harness, no history buffer). `UsesFill => false` (`:55`). Reads `ctx.swarm` 6× — the heaviest swarm consumer of the family.

### 3.5 ArcBurstForm — `ArcBurstForm.cs:38`, render at `:449` — Kiln "Energy Explosion / agent4"

Electric arc / lightning burst, "transparent to the core". **Ten layouts** over one stroke library (Bolt is canonical), each with its own settings class. 622 lines + `PyreArcBurst.cs` 1115 lines + `ArcRaster.cs` (unsafe SIMD-ish rasteriser). **187 dials, 198 tooltips** — the densest form in the package.

**State: closed-form, with a subtlety.** Bolt trees are **re-rolled every frame by design** — `random.Random(seed·m + frame)` (`ArcBurstForm.cs:20-22`). That is still a pure function of frame index, so frame 9 is directly computable. Uses `PyrePyRandom` (a bit-exact CPython Mersenne Twister replica) and `PyreNumpyRng`. `[ThreadStatic]` scratch buffers only (`PyreArcBurst.cs:75-78`) — no cross-frame state.

`UsesFill => false` (`:50`).

### 3.6 PlasmaBloomForm — `PlasmaBloomForm.cs:109`, render at `:406` — Kiln "Energy Explosion / agent3_fork"

A directional plasma detonation, gen 5, "the blast has a direction". Ten contract draws (`detonate` canonical). 548 lines + `PyrePlasmaBloom.cs` 613 lines. **114 dials, 112 tooltips.**

**State: closed-form per frame, BUT it is the one form with a whole-clip pre-pass.** `rMax`/`orgX`/`orgY` are SOLVED at clip level by `PyrePlasmaBloom.Solve`, which samples the field at `fitFrames` clock samples and re-`Prepare`s the form at each of those lives (`PlasmaBloomForm.cs:412-425`). The result is memoised in a `PyrePrepassCache<PlasmaFit>` (`:377`) shared between the form and its render clones (`PyreForm.cs:208-214`, `PyreFrameFill.cs:10`).

**This is not frame-to-frame accumulation** — you can still ask for frame 9 first; it just triggers the whole-clip solve. But it *is* a real dependency on the whole clip, and it is the only generator with that shape. Worth its own category.

`UsesFill => false` (`:121`). It is the **only** `PyrePrepassCache` consumer in `Runtime/Pyre/` outside the (commented-out) `PyreClipStats` example.

### 3.7–3.9 The Jet family — `Jet/JetForm.cs:28`, `Jet/RadialJetForm.cs:26`, `Jet/ExplosiveJetForm.cs:44`, all on `JetFormBase` (`Jet/JetFormBase.cs:20`, render at `:101`)

One shared **jet engine** (`Jet/PyreJetEngine.cs`, 795 lines, 60 dials) ported once, with the three forms as stage-pipeline overrides:

- **Jet** — the directional flamethrower stream, gen 2. Five variants: needle / cone / drooping / swept / ringed (default `gout`).
- **Radial Jet** — the same puff physics emitted into a 124°–360° arc, gen 3. Eight variants: corona / fan / crown / whirl / shockring / maw / starburst / halo. +10 dials in `RadialJetProgram.cs`.
- **Explosive Jet** — the jet's puff physics with the emission clock rewritten into an authored blast schedule, plus fracture, second crack, flash, chunks, gobs, dust, sparks, ring fronts. Gen 7. Ten variants: detonate / backdraft / chain / frag / fuelair / lash / muzzle / shatter / shockfront / starshell. **+89 dials, 93 tooltips** in `ExplosiveJetProgram.cs` (1005 lines).

**State: closed-form, explicitly and strongly.** `PyreJetEngine.cs:12-13`: "Every term is a closed-form function of the loop phase (slot ages `(phase − i/N) mod 1`, noise scrolled by whole lattice periods, integer-frequency sinusoids)". `JetForm.cs:20-21`: "phase = frame / frameCount, exactly periodic by construction … one period = the clip, whatever the frame count." **These are the only generators that are inherently LOOPING** — a genuinely distinct behavioural property no other form has.

All three: `UsesFill => false` (`JetFormBase.cs:23`), swarm as placement (one jet per particle, centred on it), ignore `size`/`particleSpin`, honour `alpha` and the lifetime window. Zero editor code — cards drawn by ZuiReflect (`PyreWindow.Forms.cs:1-6`).

---

## 4. Detailed entries — the field-pass modes (`LayerCoalesce`)

`LayerCoalesce { Off, Fuse, Ramp }` (`Pyre.cs:173`), field `PyreLayer.coalesce` (`Pyre.cs:268`). Read at `PyreRenderer.cs:1497-1498`, inside `RenderSwarm`.

**Combinability — the key fact.** Coalesce is reached ONLY when: `layer.form == null` (else `RenderFormLayer` returns first, `PyreRenderer.cs:248-252`), `shapeForm` is not Fire/Fireball/Playback3D (they return at `:258`, `:261`, `:269`), and `layer.swarmEnabled == true` (else `DrawParticle` is called directly at `:281`).

And when it IS reached, **the chosen shape is thrown away**: `PyreRenderer.cs:1552` — *"The form is ignored in both modes: a coalescing particle is always a circle/dome."* Only `size`, `alpha` (and for Ramp, `density`/`heat`) are read off each particle (`:1557-1584`).

So: Fuse and Ramp are **not** modifiers of a generator — they are **two additional generators wearing a generator-selector as a hat**. A user picking "Star + Fuse" gets circles.

### 4.1 Coalesce = Fuse — MetaBlob metaball fuse — `PyreRenderer.cs:1617` (`RenderPlusFusedField`)

Collects every alive particle as `FieldParticle{pos, radius, weight=own-life alpha}`, then reads the whole set as ONE scalar field and composites a single merged, gradient-shaded silhouette:

```
field     = Σ weight·(1−d²/r²)²                     (PyreField.Sample)
threshold = max(0.02, fuseThreshold)
band      = clamp(fuseSoftness, 0.01, threshold)
alpha     = clamp01((field−(threshold−band))/band) · fillColour.a
frac      = clamp01((field−threshold)/fuseShadeRange)   // 0 = surface, 1 = core
colour    = shapeFill gradient at frac
```
(`PyreRenderer.cs:1606-1616`.) Three plain-float dials: `fuseThreshold` / `fuseShadeRange` / `fuseSoftness` (`Pyre.cs:271-282`).

**Stateless** (self-described at `Pyre.cs:168` and `PyreRenderer.cs:1602`). Honours geometry modifiers (they fold each sample point, bending the fused field) and pixel modifiers (`PyreRenderer.cs:1613-1614`). Uses the layer's Shape Fill. Runtime: yes. Ported from `BlastRenderer.RenderFusedField`.

### 4.2 Coalesce = Ramp — HeightBalls density/height relief — `PyreRenderer.cs:1697` (`RenderPlusRampField`)

Collects `RampParticle{pos, radius, density, heat, alpha}` — two **extra per-particle envelopes** beyond the plain swarm: `density` = mass/body, `heat` = height/energy (`Pyre.cs:283-296`, evaluated at `PyreRenderer.cs:1573-1577`, field ids −25/−26). Then:

- Pass 0: a shared per-pixel **surface-noise rim** (`rampRimScale`) sampled once per pixel and shared by every dome, so neighbouring domes bulge/pinch together and interlock into one mass (`PyreRenderer.cs:1719-1741`). Drifts over the layer's life (`driftX = life·8`) — a pure function of life.
- Pass 1: three `SmoothMax` dome fields — density, heat, and height = `density·2.65 + heat·0.72` — with per-channel melt knees sized relative to the strongest ball in each channel so `rampFusion` means the same regardless of how hot the cloud is (`PyreRenderer.cs:1707-1717`).
- Pass 2: relief-light from the height field's local slope, shade through the Shape Fill as one smoke→fire cloud.

Dials: `rampFusion`, `rampCoverage`, `rampRimScale`, relief toggle, plus the two envelopes (`Pyre.cs:283-300`).

**Stateless — the `Pyre.cs:94-99` claim that HeightBalls is closed-form and not a sim is CORRECT**, but ⚠️ **its cited proof is a dead link**: `Pyre.cs:95-96` cites `Runtime/Pyre/BlastRenderer.cs:1493-1496`, and **`BlastRenderer.cs` does not exist anywhere in `Assets/`** (verified by filesystem search). The claim is nonetheless independently verifiable from the live code: every input to `RenderPlusRampField` is Eval'd at `own`/`life` in the collect loop, and the only per-pixel noise drifts as `f(life)` (`PyreRenderer.cs:1735`). `PyreRenderer.cs:1691-1696` similarly cites `RenderHeightBalls (:1791-1805)` and `AddHeightBall (BlastRenderer.cs:1584)` — same dead file.

**Exception found (not in the brief's list):** **geometry modifiers do NOT warp the Ramp fused field** — "the shared accumulate is geometry-agnostic" (`PyreRenderer.cs:1694-1696`). Pixel and Post modifiers still apply. This is a divergence from Fuse, which *does* honour geometry modifiers.

Also documented as out of scope: per-ball squash ellipses, idle-boil churn, the `hbFold` confinement fold-under, and cross-group fusion (`PyreRenderer.cs:1690-1694`).

### 4.3 Height consumer (`heightFromChannel`) — `PyreRenderer.cs:1849` (`RenderHeightConsumer`) — a THIRD field-pass generator not in the brief

A Draw layer with `heightFromChannel >= 0` (`Pyre.cs:233`) **does not draw its shape at all** (`Pyre.cs:226-228`, `PyreRenderer.Layers.cs:105`, dispatched at `:168`). Instead it reads an already-fused matte channel — deposited by `WriteMatte` luminance layers and combined by Max/Add/Subtract — relief-lights it from its own local slope with the same `PyreField.ReliefLight` helper Ramp uses, shades the scalar through this layer's Fill, and composites one fused surface.

Described in-code as "exactly the HeightBalls look but fed by mattes, not a swarm" (`PyreRenderer.cs:1845-1846`). Reads only the channel plus two plain floats (`heightRelief`, `heightLightAngle`) — no Eval, no per-particle state, so it bakes/scrubs identically. Draws nothing where the field is 0.

It **ignores** the layer's form, `size`, swarm, `particleSpin`, and all envelopes. It honours only the Fill and the two relief knobs. **This is a fourth generator-selection mechanism** and it is invisible in the Shape section — you select it from the matte controls.

---

## 5. `PyrePlus/` leftovers — CONFIRMED EMPTY

`ls -laR` on `Assets/Packages/Laubrary/Runtime/PyrePlus` and `Assets/Packages/Laubrary/Editor/PyrePlus`: both contain **only `.` and `..`**. Zero files, zero subdirectories. Safe to delete.

The name survives only in `[MovedFrom]` attributes for deserialization (`PyreForm.cs`: `MovedFrom(true, "Laubrary.PyrePlus", "com.Lautaro-Arino.Laubrary.PyrePlus", null)`) — those must stay, or every existing spec's `layer.form` deserializes to null. There is also a stale `pyreplus_N` versioning prefix in the baker's collision guard (`Editor/Pyre/PyreBaker.cs:19`).

---

## 6. Proposed classification

Derived from what I found, not imposed. **Six families**, each with a question a user could actually answer.

### Family A — **Stamps** (a shape drawn once per particle)
*Discriminating question: "If I turn the swarm on, do I get N copies of the same little shape?"*

Disc, Crescent, Ring, Star, Polygon, Streak, Sparkle, Gem, Box, Pyramid, Can, Orb(enum), Sprite, Text.

Common contract: radius from `size`·sizeMul, colour from Shape Fill at own life, `alpha` envelope, `particleSpin`, travel, per-pixel geometry+pixel modifiers, lifetime window. Every one of them is closed-form and bakes at runtime. Sub-split worth keeping in the UI: **flat 2D** (Disc/Crescent/Star/Polygon/Streak/Sparkle — these get `edgeSoftness` and the Border box) vs **lit 3D** (Gem/Box/Pyramid/Can/Orb/Ring — these get the solid box with tilt/light/lines/glows and give up `edgeSoftness`/Border).

### Family B — **Scenes** (one whole-canvas procedural effect, placed by the swarm rather than multiplied by it)
*Discriminating question: "Is one of these a whole explosion / whole flame on its own?"*

All nine `PyreForm` plug-ins: Inferno, Fork Blast, Orb(plug-in), Torch, Arc Burst, Plasma Bloom, Jet, Radial Jet, Explosive Jet.

Common contract: owns its own scale/placement dials; **cannot** read `size` or `particleSpin`; swarm means "ignite one of me per particle"; **cannot** coalesce; usually brings its own ramps (only Inferno and Fork Blast use the layer Fill). All closed-form, all bake at runtime.

### Family C — **Simulations** (a retained grid stepped frame to frame)
*Discriminating question: "Does scrubbing backwards make it recompute the whole clip?"*

Fire, Fireball. Only two members, both delegating to SpriteFx's public sims, both behind the same CWT + content-hash replay harness, both forcing the spec serial.

### Family D — **Field passes** (the swarm is read as one continuous mass, not as N shapes)
*Discriminating question: "Do I want my particles to melt into a single blob?"*

Coalesce=Fuse (MetaBlob), Coalesce=Ramp (HeightBalls), Height consumer.

Common contract: the per-particle shape is discarded; only position/radius/weight matter; shading comes from a scalar field through the Fill; stateless. Currently these are *modes on a layer*, not *generators in the picker* — which is why they are invisible.

### Family E — **Imports** (the pixels come from outside the spec)
*Discriminating question: "Does this need me to assign an asset before it draws anything?"*

Sprite (a `Sprite`), Text (a `TMP_FontAsset` + a string), Playback3D (a prefab).

Common contract: can *fail* and either fall back to Disc (Sprite, Text) or draw nothing (Playback3D); main-thread-only, so they force the spec serial; the pixel-art look is not authored by dials. Sprite and Text also sit in Family A structurally (they are stamps), so this is a cross-cutting flag rather than an exclusive bucket — I'd keep it as a **badge**, not a folder.

### Family F — **Retired**
Inferno(enum), ForkBlast(enum) — int values preserved for serialization, no draw code, absent from the picker.

### 6.1 Near-duplicates that could be fused

| Pair | Evidence | Verdict |
|---|---|---|
| **Star ↔ Polygon** | Same per-ray boundary test, Polygon is Star with one edge per sector instead of a valley (`Pyre.cs:95-98`) | **Fuse.** One "N-gon" generator with a `valleyDepth` of 0 = polygon. |
| **Gem ↔ Box ↔ Pyramid ↔ Can** | One shared `DrawFacetSolid`, differing only in which `Build*Geometry` runs (`PyreRenderer.cs:4182-4184`) | **Already fused internally; expose as one "Solid" generator with a shape dropdown.** They are four picker buttons for one code path. |
| **Fork Blast ↔ Explosive Jet** | `ExplosiveJetForm.cs:59-63` says Explosive Jet *replaces* Fork Blast, closing a 47-row divergence table | **Retire Fork Blast.** It is a strictly-worse gen-5 port still sitting in the picker. |
| **Jet ↔ Radial Jet ↔ Explosive Jet** | One `PyreJetEngine`, three stage-override sets, 23 variants total | **Do NOT fuse — but present as one "Jet" generator with a mode.** Three picker entries for one engine is the same problem as Gem/Box/Pyramid/Can. |
| **Coalesce=Ramp ↔ Height consumer** | Both are "relief-light a scalar field through the Fill", sharing `PyreField.ReliefLight`; one is fed by a swarm, one by mattes (`PyreRenderer.cs:1845-1846`) | **Fuse the concept:** one "Height field" generator with a *source* selector (swarm / matte channel). |
| **Orb(enum) ↔ OrbForm** | Nothing in common except the word "Orb" | **Not duplicates — RENAME one.** This is a live user-facing ambiguity: two "Orb" buttons in the same Shape section. |
| **Fire ↔ Fireball** | Both retained-grid sims sharing one replay harness; Fire has swarm emitters and Fireball does not | Keep separate (genuinely different looks) but they should share one "Simulation" family card and Fireball should probably gain the swarm path Fire already has. |

### 6.2 Abandoned experiments

- **Playback3D** — self-labelled PROOF OF CONCEPT in three separate files (`Pyre.cs:609`, `PyreRenderer.cs:262`, `PyrePlayback3DPreview.cs:1`). Draws literally nothing outside the editor preview. 443 lines of preview code + 8 spec fields + a UI box for a generator with no output. Either finish the offscreen-camera bake or cut it.
- **ForkBlastForm** — superseded by its own successor, still shipping.
- **`PyreFormKind.PerParticle` and `.Stateful`** — declared in `PyreForm.cs` but "the renderer does not dispatch it yet" / "(reserved)". Every real form is `WholeLayer`. Dead API surface.
- **`Runtime/PyrePlus/` + `Editor/PyrePlus/`** — empty directories.

### 6.3 Load-bearing

- **Disc** — the universal fallback. Sprite, Text, and both retired enum slots all fall through to it. Cannot be removed or renamed casually.
- **`DrawFacetSolid`** — four picker entries depend on it.
- **`PyreJetEngine`** — three forms and 23 variants depend on it.
- **`PyreField`** (`Sample` / `Accumulate` / `AccumulateDomes` / `ReliefLight`) — shared substrate under Fuse, Ramp, and the height consumer.
- **The `PyreForm` plug-in contract** — nine forms and ~10,600 lines of Kiln port depend on `PyreFormCtx`'s exact shape. Adding `size` to it is a cheap, non-breaking way to close the biggest "ignores shared machinery" gap.
- **Fire/Fireball's replay harness** — the only thing standing between a scrub and O(f²) rendering.
- **The `ShapeForm` int values** — append-only, serialized; Inferno/ForkBlast must keep their slots.

### 6.4 Consolidated "ignores shared machinery" table

| Generator(s) | Ignores `size` | Ignores swarm | Ignores `particleSpin` | Ignores Fill | Ignores lifetime | Other |
|---|---|---|---|---|---|---|
| Streak | ✔ (own length/width) | — | — | — | — | unique `streakScaleLengthOnly` swarm branch |
| Fire | ✔ | opt-in only (`fireSwarmEmitters`) | ✔ | — (Fill = ramp) | — | no Position section, no edge, no border |
| Fireball | ✔ | ✔ (single emitter) | ✔ | — (Fill = ramp) | — | same |
| Playback3D | ✔ | ✔ | ✔ | ✔ | (moot) | **draws nothing at all** |
| Text | reinterprets as char height | overrides swarm COUNT | — | ✔ (own per-char fill) | — | editor-only font auto-pick |
| Sprite | — | — | — | partly (`spriteTint` toggle) | — | ignores `edgeSoftness`, border |
| Sparkle | — | — | — | — | — | ignores `edgeSoftness`, border |
| Gem/Box/Pyramid/Can/Orb(enum) | — | — | reinterprets as 3D yaw | — | — | ignores `edgeSoftness`, border |
| Ring | — | ignores `orientDeg` | reinterprets | — | — | ignores `edgeSoftness`, border |
| All 9 plug-in forms | ✔ **structurally** (not in `PyreFormCtx`) | — (placement only) | ✔ **structurally** | 7 of 9 (`UsesFill=>false`) | — | **cannot coalesce** |
| Coalesce Fuse | — | — | ✔ | — | — | **discards the form** |
| Coalesce Ramp | — | — | ✔ | — | — | **discards the form**; ignores geometry modifiers |
| Height consumer | ✔ | ✔ | ✔ | — | — | **discards the form**; ignores all envelopes |

---

## 7. UNVERIFIED / open questions

1. **Visual quality / "does it look good"** — UNVERIFIED. I did not run Unity, render a frame, or look at any output. Every maturity rating is inferred from code density, tooltip coverage, bespoke UI presence, and the authors' own self-descriptions. Investigator B (or a Unity pass) should eyeball the actual output of each Kiln form before anything is retired on my say-so.
2. **`PyreRenderer.cs` full read** — I did not read all 5191 lines (per the brief). I greped the draw switch, the dispatch, the coalesce seam, both sim harnesses, the two field passes, and the height consumer. **A generator reachable by some path I did not grep would have been missed** — though the four dispatch points in `RenderLayer` (`:241-283`) plus `RenderLayerBody`/`PlanFrame` (`PyreRenderer.Layers.cs:168`) are exhaustive as far as I can see.
3. **`OrbForm`'s fifth variant name** — I read 4 of 5 variant `Resolve` overrides by line number (`OrbForm.cs:193, 239, 285, 357`); the header names Emberdrift / Wisp / Coronal / Membrane / "shed filaments". The fifth variant's identifier is UNVERIFIED.
4. **Dial counts** are regex counts of `public <type> <name>` declarations including nested settings classes. They are indicative, not exact — a settings class shared between variants is counted once, and non-dial public fields may be included. Method is stated so it can be re-run.
5. **`PyreFormKind.PerParticle` / `.Stateful`** — declared but "the renderer does not dispatch it yet" (`PyreForm.cs`). I did not find any dispatch. UNVERIFIED whether any partial plumbing exists elsewhere.
6. **Dead citations found (worth fixing whatever the outcome of T-0098):**
   - `Pyre.cs:95-96` cites `Runtime/Pyre/BlastRenderer.cs:1493-1496` as proof HeightBalls is closed-form. **That file does not exist.** (The claim is still true — independently verified from `RenderPlusRampField`.)
   - `PyreRenderer.cs:1691` and `:1602` cite `BlastRenderer.cs:1791-1805` / `:1584` / `:1957`. Same dead file.
   - `Pyre.cs:79-80` says Fire "ignores `size` and the swarm". **Stale** — slice 8 added `fireSwarmEmitters` (`PyreRenderer.cs:399`).
   - `Pyre.cs:611-612` says Playback3D renders "a flat placeholder colour". **Stale** — it renders nothing (`PyreRenderer.cs:269`).
7. **Is `ForkBlastForm` still referenced by any authored asset?** UNVERIFIED — I did not scan `.asset` files for `SerializeReference` type names. Must be checked before retiring it, or specs will deserialize to null forms.
8. **`Assets/Pyre/Imported/Heightballs Plus.asset`** exists — an authored spec presumably using Coalesce=Ramp. UNVERIFIED whether it is a demo, a test fixture, or live content.
9. **Whether Fuse/Ramp should be reachable from a plug-in form** — the code makes this structurally impossible today (`PyreRenderer.cs:248-252`). Whether that is a deliberate design line or an accident of dispatch order is a question for the owner.
