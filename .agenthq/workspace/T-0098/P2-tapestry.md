# P2 — Kiln "Tapestry Surface" / "Tapestry Shape": what they actually are, and how they fit the Pyre redesign

Investigator P2, task T-0098. Read-only investigation of `D:\CODEZ\Kiln\projects\Tapestry Surface` and `D:\CODEZ\Kiln\projects\Tapestry Shape`, plus the Unity side. Nothing was modified. All measurements below were taken by importing the generators with Python bytecode writing disabled (no `__pycache__` written into Kiln) and calling their render entry points in memory — no publish, no file writes.

---

## 0. Headline findings, before the detail

Five things are true that materially change the framing of the owner's question.

1. **The owner's belief about Tapestry Shape is correct, and stronger than he stated.** Tapestry Shape does not merely produce "a texture meant to be bevelled" — it produces a **literal per-pixel height field as its only legal return type**, and the bevel is authored *inside* it as an explicit, per-feature transition profile. This is enforced by the harness, not merely requested: a generator returning anything with a colour channel is rejected at publish time with an error.
2. **"An external tool that generates pure textures and the shapes can use them" already exists and is already in Laubrary.** It is called **Tapestry**, it lives at `D:\UNITY\Laubrary Dev\Assets\Packages\Laubrary\Runtime\Tapestry\` and `...\Editor\Tapestry\`, it is opened via `Laubrary/Tapestry`, it edits a `TapestrySpec` asset, it has a layer stack with blend modes, a modifier system and a tiled preview, and it exports PNG. The two Kiln projects were spawned *from* it. The owner appears to have forgotten this.
3. **The Shape/Surface split the owner is now re-inventing for Pyre was already designed, in writing, for Tapestry — and deliberately left unbuilt** pending exactly the evolved candidates these two Kiln projects have now produced. The design is spelled out in `D:\CODEZ\Kiln\docs\tapestry_briefing.md` §0.
4. **Neither Tapestry project has been ported to Unity in any form.** A whole-tree search of `D:\UNITY\Laubrary Dev\Assets` for `tapshape`, `tapsurface`, `steel_clean`, `HeightCanvas` returns zero hits. The nine ported PyreForm plug-ins all come from the *other* Kiln projects (Energy Explosion, Energy Projectile, Flame).
5. **Pyre already contains a working height-field → relief-lit-fill pipeline.** `Runtime/Pyre/Pyre.cs` exposes `matteWriteLuma`, `heightFromChannel` (0..3), `heightRelief` and `heightLightAngle`; `Runtime/Pyre/PyreField.cs` is a documented scalar-field substrate; `PyreRenderer.cs` passes a `float[] heightField` into layer rendering. So the consumer half of "Tapestry Shape emits height, something bevels it" is *already built in Pyre*, for a different producer.

---

## 1. How a Kiln project is structured (so algorithm can be told from scaffolding)

Both Tapestry projects follow the standard Kiln layout. The convention, confirmed against the sibling projects listed in `D:\CODEZ\Kiln\projects` (Cloud, Directional Blast, Energy Explosion, Energy Projectile, Flame, Pyre Edge, Pyre Edge Dissolve, Pyre Fill, Pyre Fill Dissolve, Pyre Wipe, Sigil):

- `BRIEF.md` + `TERRITORIES.md` — the human-authored design brief and the map of claimed/unclaimed idea-space. These are the highest-value reading in the project.
- `<pkg>/` (`tapshape/`, `tapsurface/`) — the **shared harness library**: `spec.py` (the genome/parameter type), `registry.py` (registration + contract enforcement), `canvas.py` (the reusable maths primitives), `imageout.py` (rendering to PNG + seam checks), `evolve.py` (mutation/crossover), `run.py` (CLI), `snapshot.py`. This is scaffolding, *except* `canvas.py`, which is genuinely core algorithm.
- `agents/<name>/gen.py` — **the algorithm**. One file per agent, containing the current generation only, with a long module docstring recording what changed and why. These are the files that matter.
- `agents/<name>/contract.py` + `MANIFEST.md` — the port-verification contract writer and the agent's accumulated history.
- `_work/snapshots/genN/<agent>/` — frozen copies of every past generation. Historical, not current.
- `_media/GENn/_output/` — published survivors (what the owner kept); `_output/_previews/` — 3×3 tiled previews; `_media/GENn/contract/` — stage-by-stage reference dumps for porting.
- `_ui/briefs/*_PROMPT.md` and `*_FEEDBACK.md` — the per-generation instruction and the owner's own graded notes. `_ui/questions/`, `_ui/requests/`, `_ui/grades/` — the feedback loop's data.

The fitness function is deletion: the owner deletes what he doesn't like, and what survives in `_output` breeds the next generation. There is no automated score.

---

## 2. TAPESTRY SURFACE — what it actually is

**Location:** `D:\CODEZ\Kiln\projects\Tapestry Surface`. **Brief:** `...\BRIEF.md`. **Territories:** `...\TERRITORIES.md`. **Harness:** `...\tapsurface\`.

### 2.1 What it generates

Tapestry Surface is the **material / colour half** of a deliberate two-project split. It generates **still, seamlessly tileable, full-colour RGBA surface appearance** — steel, paint, rust, grime, plasma glow — *applied to a silhouette it is handed and never invents*.

Its contract (enforced in `...\tapsurface\registry.py`) is: a generator registers a render function taking exactly three things — the genome (its dial values), a random source, and a **height field supplied by the harness**. It must return an RGBA image at the same resolution as that field. The three-argument signature is checked at registration time, so a generator that would have to invent its own shape is rejected before it can ever run. The RGBA return shape is checked at publish time.

The height fields it is tested against are three fixed, deliberately generic test shapes in `...\tapsurface\testshapes.py`: **panel** (one large square plate, raised, smooth-bevelled), **stripe** (a wrapped diagonal ridge — stands in for a cable or energy line), **disc** (a raised dome). Every genome is rendered against all three and published as a single side-by-side montage, so a material that only convinces on one silhouette is visibly exposed as secretly shape-dependent. Native evaluation resolution is **128×128**.

### 2.2 The three algorithms, catalogued individually

There are **three distinct generators**, all at their evolved current state.

**(a) `plasma` — glowing energy, generation 4.** File: `...\agents\plasma\gen.py` (~31 KB, 577 lines, 23 dials). Everything it draws is derived from the handed height field plus its own tileable noise; it invents no geometry. Its structural vocabulary is four selectable modes: **wash** (no discrete structure — a low-frequency tileable fractal-noise cloud), **ring** (raised-cosine isolines of the field's own pseudo-distance, damped inward so rings are strongest at the rim and dissolve into the core), **veil**, and **arc**. On top of that sit a rim term (brightest at the field's zero-crossing), a bloom-like halo built from repeated wrapped box blurs at three radii, and a fractal-noise turbulence field with an optional domain warp. Colour comes from one of **six named palettes** — `aqua`, `arc`, `ion`, `magenta`, `solar`, `toxic` — baked to a 256-entry lookup table that the shading step indexes with a tone-mapped energy value. Dials group as: colour (palette, coreWhite, exposure, rampGamma, dispersePx), structure (structure mode, bodyLight, structDepth, softenPx, anis, cellCount, arcWidth, branch, ringPitch, ringDecay), energy (rimGain, rimTight, haloGain, haloRadius), texture (turbScale, turbOctaves, turbAmount, warpAmount). Generation 4's whole thesis, recorded in its docstring, is *"the light belongs in the body, and every transition wants a gradient"* — a hard posterise dial was deleted outright as "the definition of harsh".

**(b) `steel` — clean-to-worn painted metal, generation 8.** File: `...\agents\steel\gen.py` (~102 KB, 1491 lines, 30 dials). This is a layered physical-ish material model: a painted coat over a metal substrate, with an environment/reflection term, oxide growth, grime, scratches and pitting. Notable mechanisms: a crossed **two-direction signed scratch field** (each band's polarity hashed, so some scratches are bright exposed metal and some are dark grooves — deliberately two directions so it can never read as brush grain), a second finer **speck/crater lattice**, an environment reflection whose sweep is driven by a surface-normal perturbation, and **oxide/rust that grows out of the handed field's low ground and edges** rather than floating arbitrarily. Dials group as colour (coatHue/Sat/Val, metalVal, oxideHue/Amount/Crust), finish, coat (amount, wet, matte, gloss, peel), wear (edgeWear, grime, scratchAmount, scratchDir, scratchDensity, pitting) and light (lightAngle, bevelHeight, dent, waviness, envStrength, envContrast, specStrength, specSharp, contrast). Generation 8's thesis is *"the plate goes flat"* — the base tone was deliberately calmed so that small, countable, hard incident reads as the material.

**(c) `steel_clean` — machined/bolted alloy panel, generation 12.** File: `...\agents\steel_clean\gen.py` (~156 KB, 2440 lines, 30 dials). The most evolved thing in either project. Same broad family as `steel` but pushed toward clean manufactured alloy: an alloy base with a polymer/coat mix, micro-texture, scratches, and — its defining feature — a **rivet/bolt-head placement engine** that traces the handed shape's own contour, finds curvature peaks, places a corner bolt on every corner *first and unconditionally*, then fits optional side runs, clusters or single accents around them with an explicit clearance rule. On a shape with no corners (the disc, the stripe) it falls back to an even ring. Dials cover colour (alloyHue/Sat/Val, polymer, tintDrift), texture (peel, warp, micro, scratch, bump, bumpStyle, bumpLayout, bumpInset, bumpSpacing, bumpSize, glossVar, occlusion) and light (envRot, envSharp, studioDark, reflect, lightAngle, bevelHeight, specRough, specStrength, coat, coatRefl, contrast).

### 2.3 What the output actually looks like — measured, not assumed

- **Colour, not greyscale.** Full 24-bit RGBA.
- **Still, not animated.** There is no time parameter anywhere in the contract. Confirmed by reading the signature and by `contract.py`'s own note that its `frames/` directory holds *the three test shapes*, not three moments in time, and that `loop` is false.
- **Tileable.** Verified numerically on a rendered fill: the wrap-seam pixel delta versus ordinary interior deltas came out at ratios of 0.80 / 1.14 (steel) — i.e. the seam is indistinguishable from any other interior transition.
- **Deterministic.** Same genome + same seed rendered twice produced a byte-identical image.
- **Colour count is already low.** A `steel` fill used 936 unique colours across 16,384 pixels; `steel_clean` used **85**. Neither is deliberately palette-quantised, but they are already close to pixel-art-friendly territory.

### 2.4 The single most important measurement: two of the three are already genuine FILLS

I rendered each generator twice — once against the bevelled panel test shape, once against a **completely flat, all-zero field** (i.e. "no shape at all") — and compared.

| generator | alpha on the panel | alpha on a flat field | mean pixel difference between the two renders |
|---|---|---|---|
| `steel` | opaque everywhere | **opaque everywhere** | **4–6 / 255** |
| `steel_clean` | opaque everywhere | **opaque everywhere** | **4–8 / 255** |
| `plasma` | alpha 2–254, field-driven | **alpha 0 everywhere — renders nothing** | 140–165 / 255 |

The conclusion is unambiguous and directly answers the redesign question. **`steel` and `steel_clean` are already shape-independent tileable fills.** Roughly 97–98% of what they produce does not depend on the shape at all; the height field only contributes a small edge-local increment (bevel shading, edge wear, corrosion seeding, rivet placement). Hand either one a blank field and you get a complete, usable, seamless metal texture. **`plasma` is not a fill** — it is a shape-coupled glow whose alpha *is* the shape's silhouette. Given no shape it produces literally nothing.

That distinction matters: it means Tapestry Surface has *already* produced two drop-in fill generators and one thing that is really a different category (an emissive edge/body treatment, closer to a Pyre effect than to a fill).

---

## 3. TAPESTRY SHAPE — what it actually is

**Location:** `D:\CODEZ\Kiln\projects\Tapestry Shape`. **Brief:** `...\BRIEF.md`. **Harness:** `...\tapshape\`, of which `canvas.py` is the important file.

### 3.1 The owner's belief, tested

The owner wrote: *"Tapestry Shape seems to be a way to procedurally create a texture that is meant to be bevelled, extruded."*

**This is correct, and understates it. Tapestry Shape does not produce a colour image at all. Its only legal output is a per-pixel floating-point HEIGHT.** The evidence, in order of strength:

1. **The type contract makes colour unrepresentable.** `...\tapshape\registry.py` wraps every generator's render call and rejects any result that is not a plain two-dimensional array, with an error message that explicitly says a Shape must return an HxW height field, that there is no colour channel, and that if you wanted colour it belongs in the sibling project. The brief calls this "the split's real enforcement mechanism, not the brief text".
2. **The brief states the semantics directly:** 0 = base level, positive = raised above it, negative = recessed below it.
3. **The bevel is not something added later — it is a parameter of the drawing operation.** The core primitive in `...\tapshape\canvas.py` is a *stamp*: you hand it a shape and a target elevation, plus a **profile** and a **profile width**. The four profiles are `smooth` (a rounded, smoothstep bevel), `linear`, `chamfer` (kept as a separate name because that is how a person asks for it, though geometrically identical to linear) and `step` (no ramp at all — a hard notch, for a stamped/cut-metal look). The brief's own words: *"Bevel is not decoration bolted on afterward — it's the transition profile you choose when you stamp."*
4. **Layering is real elevation composition.** A raised stamp only wins where it is already higher than what is there, so two overlapping plates share a top surface instead of adding up; a recessed stamp only wins where it is lower, so a groove always cuts down. To get a deliberate second tier — a rivet sitting *on* a plate rather than fused into it — you stamp again at a higher elevation.
5. **Every published result ships the raw height data.** `...\tapshape\imageout.py` writes a float32 `.npy` of the actual elevation field alongside every PNG, described in its own docstring as being "for real porting fidelity beyond what the visual PNG can carry".
6. **The grey PNGs are a fixed, generator-independent *visualisation*, not the output.** `imageout.py`'s visualiser shades the field from the field's own gradient under a fixed light direction and shifts base tone with elevation, precisely so that grading compares geometry and never colour. A genome has no say in it.

So: **the bevel/extrude capability is inside Tapestry Shape already. It would not have to be added.** What *would* have to be added is only the *consumer* — something that turns the height field into lit pixels. And in Pyre that consumer largely exists (see §5).

### 3.2 The two algorithms, catalogued individually

**(a) `plates` — sci-fi hull panelling as pure geometry, generation 11.** File: `...\agents\plates\gen.py` (~213 KB, 3518 lines, 62 functions, 30 dials). Lays out differently-shaped rectangular/octagonal plates on a wrapped lattice, each raised to its own elevation with its own bevel profile, with recessed seams between them. Selectable layouts, symmetry modes, corner styles, and a `shell`/`hull` mode where the ground itself sits at a raised floor so the seams read as **channels milled into solid** rather than gaps between separate parts — giving three real elevation registers instead of two. It also supports a **fault**: a wide diagonal channel milled to true zero, cutting across the whole tile. Detail features are nested inset panels, vents, ports and rivets, each with its own tier. Dials group as layout (layout, varyAxis, shell, density, gap, blockVariety, cornerRadius, cornerStyle, symmetry, notchChance, shapeVariety, shearChance, plateScale, faultWidth, dropoutChance, clearance), bevel (bevelWidth, profile, bevelVariance), tier (tierVariance, terraceGrid, sinkChance, recessDepth), mix (baySize, scaleContrast) and detail (insetChance, insetDepth, rivetChance, ventChance, detailVariety). Generation 11's headline rule is a hard geometric guarantee — **no two ramps may touch**: where one silhouette is stamped concentrically inside another, there must be a minimum of genuinely flat deck between the two bevels, and if there is not room the inner tier is *dropped* rather than drawn into its parent's ramp. Its bevel band was narrowed about 40% at the same time.

**(b) `lines` — circuit-trace geometry as pure height, generation 8.** File: `...\agents\lines\gen.py` (~125 KB, 2155 lines, 43 functions, 32 dials). Self-avoiding walkers on a wrapped lattice that turn only in 45° steps, rasterised as capsule strokes and stamped as either **raised ridges** (an energy line or cable) or **recessed channels** (a groove) — the same movement grammar, opposite elevation sign. All walkers in one tile share a single occupancy set plus a clearance rule, and a walker whose finished path comes out shorter than a threshold is **discarded entirely and retried** from a fresh start, up to a bounded attempt budget. Generation 8 added a per-tile **compass axis**: one heading per tile, off which every walker starts and the tile's single figure is aligned, so a tile is either an orthogonal part or a diagonal one and never an arbitrary mixture. It also enforces **one figure per tile** — the figure modes are none / ring / plate / comb / rivets, and the previous compound two-figure modes were removed so that a two-figure tile is *no longer representable*. Connectors dock end-on into a figure's own axis rather than meeting it broadside. Dials group as walk (gridSize, traceCount, tileAxis, minLengthFrac, separation, minRunFrac, maxTurns, turn90Chance), stroke (traceWidth, trunkWidthMul, trunkShare, coverageCap), figure (figureMode, figureShape, figureSize, figureRepeat, figureLink), curve (curveMode, cornerRung), elevation (elevation, thinTierMul, signMode, recessDepth, shoulderShare, shoulderTier), bevel (bevelRatio, profile) and terminal (terminalMode, padRadiusMul, padTier, viaDepth).

### 3.3 What the output looks like, and the exact numbers

I inspected the tiled previews for the latest generations. The `plates` output reads as clean sci-fi hull plating — rounded-octagon and rectangular plates at two or three visibly distinct elevations, crisp narrow bevels, a diagonal fault channel cutting across, tiling seamlessly. The `lines` output reads as circuit-board routing — long clean traces with 45° corners and large radius sweeps, terminating in small round pads, with a single small comb figure per tile, all as shallow relief on a flat ground.

Measuring all **248 published height fields** across every generation:

- **Data shape:** a plain 2D `float32` array. **245 are 256×256**; 3 early ones are 128×128. Single channel, no colour, no alpha.
- **Value range:** global minimum **−1.648**, global maximum **+2.206**. Median per-file minimum 0.000, median per-file maximum 1.326. **The data is NOT normalised to 0..1** — it is a signed elevation in arbitrary units where 0 is the base plane.
- **Sign distribution:** 126 files are purely raised, 39 purely recessed, 83 contain both.
- **Structure:** genuinely plateau-based. A histogram of a typical `plates` field shows a huge spike at exactly 0 (the ground), then discrete populations at roughly 0.7–0.8, 0.85 and 1.2, with almost nothing in between except the thin bevel transitions. It is a stack of flat decks joined by narrow ramps — exactly what you want to light.

---

## 4. Fit as a Pyre/Shaper **FILL** generator

### 4.1 The good news

`steel` and `steel_clean` fit the fill role **better than the owner probably expects**, because of the measurement in §2.4: they are already 97–98% shape-independent, already seamless, already deterministic, and already low-colour-count. Their algorithms are also structurally the right kind: pure closed-form functions of pixel position with deterministic hash-based per-cell randomness (never a sequential random stream), which is what makes tiling possible at all.

### 4.2 What they already have

- **Tiling / wrapping — already solved, by construction.** Every noise and pattern primitive in `...\tapsurface\canvas.py` is toroidal: value noise samples a wrapped grid of hashed corner values, fractal noise sums wrapped octaves, gradients are taken with wrapped differences. There is no "make seamless" post-process to port; it is baked into the primitives.
- **A shape input.** They already accept a height field and already use it for edge-local effects. Pointing that at a Pyre shape's own coverage/height instead of a test shape is a rename, not a redesign.
- **A palette/LUT step — but only in `plasma`.** `plasma` already resolves colour through a 256-entry baked lookup table indexed by a tone-mapped scalar, which is exactly the structure a palette/quantisation stage wants. `steel`/`steel_clean` do *not* — they compute continuous RGB directly.

### 4.3 What they do **not** have, and would need

- **Shape-local coordinates — genuinely missing, and a real design decision.** Every Surface generator's own pattern is anchored to the **tile**, not to the shape. Its noise, grids, scratch lattices and rivet lattices are functions of absolute wrapped UV. Painted into an arbitrary Pyre shape, the pattern will not follow, rotate or scale with the shape; it will show through it like a static backdrop the shape is a window onto. For a static texture tool that is correct behaviour. For an animated sprite it is the classic "texture swims" artefact. Anything moving needs either shape-local UVs (which will break tiling at the shape's own boundary unless the shape's UV space itself wraps) or an explicit decision that the fill is world-anchored. `steel_clean`'s rivet engine is the exception and the proof: it traces the *shape's own contour* and would follow correctly.
- **A time input — completely absent.** There is no time parameter in the contract at any level, and `contract.py` explicitly records that this project's output is static and its frame dimension is the three test shapes, not time. There is no animated fill here and nothing that becomes one by adding a dial. To animate, you would drive a generator's existing dials over time (cheap, but every frame is a full re-render), or animate the *noise offset* (a small, well-defined change to the noise primitives), or resample one baked buffer per frame the way the existing Unity Tapestry compositor already does for its Animate Transform feature.
- **A palette/quantisation step for pixel-art.** `steel`/`steel_clean` output continuous colour. They are already unusually low-colour (85 unique colours in one `steel_clean` fill), so a quantisation stage would be cheap and would not destroy them — but it does not exist and would have to be added, most naturally as a shared post-step rather than per-generator.
- **A border concept.** Neither project has one. Tapestry Shape produces a bevel, which is an *edge treatment in height*, but nothing in either project emits a border as a separate channel or product.

### 4.4 Performance — measured, not guessed

All timings are wall-clock on this machine, numpy, single-threaded, median of several genomes.

**Tapestry Surface, scaling with resolution:**

| resolution | `plasma` | `steel` | `steel_clean` |
|---|---|---|---|
| 128×128 | 32 ms | 116 ms | 61 ms |
| 256×256 | 114 ms | 505 ms | 253 ms |
| 512×512 | 769 ms | 3750 ms | 1752 ms |

**Tapestry Shape, at its native 256×256:** `plates` 33–124 ms typical, `lines` 64–102 ms typical — but **with occasional spikes to 588 ms and 1773 ms**. Those spikes are not noise; they are the discard-and-retry loops (the minimum-length walker filter in `lines`, the clearance/no-touching-ramps rejection in `plates`) hitting an unlucky genome and re-rolling many times. Worst case is roughly **20–50× the median**.

**Interpreting this for C#.** These numbers are numpy, and numpy's cost here is dominated by the *number of whole-array passes* and the transcendental functions in them (`steel` makes ~120 distinct array-level numpy calls and many more operator passes). A straight C# per-pixel port does the same arithmetic but pays no per-call array overhead and can be threaded — Pyre already renders frames multi-threaded. My estimate, based on the op counts in the source rather than a guess: **a C# `steel_clean` at 256×256 should land in the low tens of milliseconds single-threaded, and single-digit milliseconds threaded**; `steel` perhaps 1.5–2× that; `plasma` cheaper than both. That is comfortably interactive for a single preview repaint.

**But it is not fast enough to run per-frame across an animation.** At, say, 24 frames, even an optimistic 10 ms/frame is 240 ms per full rebuild, and the `plates`/`lines` retry spikes mean a *shape* generator has an unpredictable worst case that you cannot put on a UI repaint path. **Conclusion: a Surface fill can be evaluated live for a single preview frame; anything animated, and any Shape generator at all, must be cached.** This is not a new problem for Pyre — `PyreLayerCache` is content-addressed and `PyrePrepassCache` exists precisely for one-shot pre-passes, so the caching machinery to hang this on is already there.

---

## 5. Fit as a **HEIGHT SOURCE**

This is the strongest fit of anything in this investigation.

**Exactly what the data is:** a single-channel `float32` array, `H × W` (256×256 as published), signed, where 0 is the base plane, positive is raised and negative is recessed. Absolute range across all 248 published fields is −1.648 to +2.206, **not normalised**. The field is toroidally continuous — its left edge is genuinely adjacent to its right edge — which means wrapped finite differences on it are exact, not an approximation at the boundary. Its internal structure is flat plateaus separated by narrow ramps whose width and shape were chosen by the generator.

**What a consumer has to do with it** — and the design decision here was already made deliberately on the Kiln side, recorded in `docs\tapestry_briefing.md` §0. The first instinct was that the *material* would need to know about bevel and lighting. It does not. A height field already carries everything a bevelled composite needs, for free:

1. **Its sign gives you a mask.** Clip to `height >= 0` (or to any threshold) and you have the silhouette, with no separate coverage channel needed.
2. **Its gradient gives you a lighting direction.** Take the wrapped finite-difference gradient, normalise it, dot it against a light vector, and you get a highlight on one side of every edge and a shadow on the other — *automatically at whatever profile and width the generator actually stamped*. Nothing is assumed or added by the consumer. A `step`-profile stamp yields a hard edge; a wide `smooth` stamp yields a soft one; the consumer does not need to know which.
3. **The value itself gives you tier separation.** Base tone can track elevation so a rivet on a plate on a hull floor reads as three distinct levels rather than "in vs out".
4. **Normalisation is the consumer's job.** Because the range is not 0..1 and varies per field, a consumer needs either a per-field normalise or an explicit height-scale dial. This is the one real piece of glue.

**Why this matters for Pyre specifically:** Pyre already implements steps 1–3 for a different producer. `Runtime/Pyre/Pyre.cs` has a matte-heightmap system where layers deposit luminance × alpha into one of four numbered channels, several such layers fuse into one scalar heightmap, and a consumer layer renders that channel **relief-lit through its own fill gradient**, with `heightRelief` controlling how steeply local slope bends the surface normal and `heightLightAngle` controlling the light direction. `PyreRenderer.cs` threads a `float[] heightField` into layer rendering. That is precisely the consumer a Tapestry Shape field wants, and it already exists and already works. Feeding a Tapestry Shape height field into that channel is a much shorter path than building a bevel/extrude stage from scratch.

**The remaining gap:** Pyre's heightmap is *derived* from particle luminance (metaball-ish, soft, organic). Tapestry Shape's is *authored* (hard plateaus, controlled ramps, crisp geometry). They are the same data type serving very different aesthetics, and the relief lighting tuned for one may need different defaults for the other. That is a tuning problem, not an architecture problem.

---

## 6. External tool, or in-tool generator? — recommendation

**Recommendation: keep the *authoring* external, make the *evaluation* internal. Concretely — port the algorithms into the tool as native generators, and do not build a texture-file export/import pipeline as the primary path.**

The owner floated "an external tool that generates pure textures and the shapes can use them". I recommend against that as the main mechanism, for four reasons, weighted by the three requirements he named.

**(a) Real-time preview response to UI edits.** A texture imported as a file is frozen. Every dial on it becomes dead — you cannot drag `bevelWidth` and watch the plates sharpen, you cannot drag `oxideAmount` and watch rust grow. That is precisely the interaction the redesign is being built for, and file export destroys it. The measured performance says you do not have to give it up: a C# `steel_clean` at 256×256 should be single-digit-to-low-tens of milliseconds, which is a live dial. This argument alone is close to decisive.

**(b) Animated fills.** A still PNG cannot become an animated fill. A native generator can, by evaluating its noise at a time-varying offset, or by having its dials driven by Pyre's existing animatable-value system — a capability that only exists if the algorithm is *in* the tool. An external-export pipeline can only give you an animated fill by exporting a strip of N frames, which multiplies asset count and file size by N and still cannot respond to a dial.

**(c) Animation caching.** This is the one point that argues *for* an intermediate baked form, and it is real — §4.4 shows per-frame evaluation is too slow for an animation loop, and the Shape generators have unbounded retry spikes. But the answer is caching *inside* the tool, not files on disk. Pyre already has exactly this: a content-addressed layer cache keyed on the layer's own parameters, plus a one-shot pre-pass cache. A Shape/fill generator slots into that as a pre-pass — evaluated once when its dials change, reused for every frame — which gives you both live editing and cheap playback. A file export gives you cheap playback only.

**(d) There is already an external tool, and its lesson is instructive.** Tapestry exists in Laubrary and does exactly the "generate pure tileable textures" job. It should stay, because a standalone tileable-texture asset is a legitimate deliverable in its own right and some users will want a PNG. But note that Tapestry's own architecture chose *in-tool generators re-run on every edit* over baked assets, for the same live-editing reason — its whole bake is one synchronous call re-run in full on every edit. The precedent inside the codebase already points the same way.

**The nuance I would add.** There are three separable things here and they should get three different answers:

- **The *algorithms*** (plates, lines, steel, steel_clean, plasma) → port natively into the new generator model. This is where the value is and where live editing lives.
- **The *evolved parameter sets*** — the specific genomes the owner kept — → these are just numbers, and should ship as **presets** on the native generators. Roughly 248 graded, hand-selected genomes exist. That is a large, free, curated preset library and it costs almost nothing to carry across.
- **The *248 published height fields themselves*** (`.npy`, 256×256 float32) → these are usable **immediately**, before any algorithm is ported, as a drop-in library of authored relief maps. If you want a fast first win that proves the height→bevel path end to end without porting 3,500 lines of `plates`, convert those float arrays to a Unity texture/asset and feed one into Pyre's existing heightmap channel. That is a day's work, not a project, and it de-risks the whole direction.

---

## 7. Things in these projects the owner has probably forgotten, and should not lose

Ordered by how much they change the redesign.

1. **The Unity Tapestry tool exists.** `Runtime/Tapestry/` and `Editor/Tapestry/`: `TapestrySpec` (resolution 16–512, seed, frame count, preview FPS, a layer list, global modifiers), `TapestryLayer`, `TapestryCompositor`, `TapestrySdf`, `TapestryGenerator` + `TapestryPanelsGenerator` + `TapestryLinesGenerator`, plus a full editor window with a generator picker, layer list, modifier picker, tiled preview and PNG export. The two Kiln projects are its evolution arm. Any plan that says "we should build an external texture tool" is proposing something already built.

2. **The Shape/Surface C# split was already designed and deliberately deferred — and it is the same idea as the new Pyre generator model.** `docs\tapestry_briefing.md` §0 states the intended architecture explicitly: a `TapestryShape` base producing a height field, a `TapestrySurface` base consuming it, and a layer picking **one of each** instead of one fused generator. It records that this was *intentionally not built yet*, specifically so that real evolved Shape and Surface candidates would exist first. Those candidates now exist. **The owner's new "generators produce a shape / a fill / a border" model is the same decomposition, arrived at independently, one tool over.** The two designs should be reconciled deliberately rather than diverging by accident.

3. **The settled decision that a fill must never author its own bevel.** Recorded in the same section: the material half receives the field and the *compositor* derives bevel from the field's gradient. This was considered, argued and settled — and it is directly load-bearing for the new model, because it says the FILL generator does not need a lighting stage and the BORDER/bevel is a property of the SHAPE, not of the fill. Re-deciding this from scratch would be wasted effort.

4. **Pyre's own heightmap channel already exists** (`matteWriteLuma`, `heightFromChannel`, `heightRelief`, `heightLightAngle`, `PyreField.cs`). The consumer for a height source is built.

5. **248 raw float32 height fields, hand-graded and kept.** 245 at 256×256. Written deliberately "for real porting fidelity later". This is a free asset library sitting unused.

6. **A complete per-agent port-verification contract system.** Each agent has a `contract.py` that re-renders every published genome from the recorded genome, checks it byte-for-byte against the published PNG, then dumps what a C# port needs: resolved parameters with units, the palette ramp as stops *plus* the exact 256-entry baked lookup table the renderer indexes, the **ordered component passes with a plain-language note describing each stage's maths**, the raw pre-shade intermediate fields as `.npy`, the noise specification with a golden reference tile, and an explicit account of every random draw. Dumps exist for most generations of all five agents under `_media\GENn\contract\`. This is the same infrastructure that made the nine existing Kiln→Pyre form ports verifiable, and it means a Tapestry port can be checked stage-by-stage rather than eyeballed. Caveat: the dumps are per-generation, so check the dump matches the generation you are porting.

7. **The `components.json` stage notes are, in effect, a plain-language specification of each algorithm.** They describe each pass and its maths in a sentence. For anyone porting, they are far more useful than the source.

8. **The `MANIFEST.md` and `_ui/briefs/*_FEEDBACK.md` files record hard-won aesthetic rules that cost many generations to learn.** A partial list, all of which generalise beyond Tapestry: a busy tangle of crossing lines reads as noise, not tech detail; lots of short disconnected fragments read as damage, not design, and the fix is a hard minimum-length discard-and-retry, not a tuned average; when a human asks for zero exceptions, the fix is a hard filter in the algorithm, not a nudged dial; a range that permits an old failure will keep producing it, so delete the gene rather than turning it down; one strong visual feature per tile beats two; no two bevel ramps may touch or the result reads as blurry; a metal's base tone must be nearly flat and everything you read as "steel" is small, dark and countable. **These are exactly the kinds of rules that get re-learned expensively if the projects are treated as a bag of algorithms.**

9. **Unclaimed territories with written design notes, ready to be picked up.** For Surface: `plastic`, `dusty` (framed as a *modifier* over any base tone as much as a material — with an open question flagged about whether it should compose rather than stand alone), `scratchy`, `riveted`. For Shape: `edges` (boundary treatments as their own elevation concern, stamped on top of a plates result), `cables` (curved conduits with junction nodes and elevation varying *along* a run). Note that `dusty` and `edges` are described in language that maps onto the new model's "border" and "modifier" slots rather than onto "fill" or "shape" — worth reading before finalising the three-way generator taxonomy.

10. **Tapestry has blend modes; Pyre does not.** Tapestry layers carry Normal / Add / Multiply / Screen, chosen deliberately because panel/line composition needs multiply-as-groove and add-as-highlight as first-class options. The briefing notes explicitly that Pyre has only straight-alpha Over and no blend-mode concept at all. If the new Pyre model is going to composite shape + fill + border as layers, this is a known gap with a known justification already written down.

11. **Three questions are still unanswered** in the feedback UI, so those generations stalled waiting on the owner: `lines` gen 8 — should every trace have the wide-shallow-trough-with-a-narrow-deep-channel double bevel, or keep it rarer? `plates` gen 10 — every tile now varies in exactly one respect and nine of twenty vary in none; has that gone too far? `plates` gen 11 — the bevel is 40% narrower and no two nested edges may share a ramp; where has the sharpness landed?

12. **A minor but real caveat about the seam check.** Both projects' seam-check helpers carry a warning, learned from a real mistake during Tapestry's own development: comparing the wrap-seam delta against the average of *all* interior pixel pairs is misleading for grid-like content, because flat interiors drag the average down and a perfectly legitimate seam reads as anomalous. The correct comparison is against other *real* interior boundaries at the same spacing. The visual 3×3 tiled preview is the trustworthy check. Anyone verifying a port's tiling will hit this.

---

## 8. Answers in one line each

1. **Tapestry Surface** — the material half: three evolved generators (`plasma` gen 4, `steel` gen 8, `steel_clean` gen 12) producing still, seamless, full-colour RGBA surface appearance at 128×128 from 23–30 dials each, painted onto a height field handed in by the harness and never invented.
2. **Tapestry Shape** — the geometry half: two evolved generators (`plates` gen 11, `lines` gen 8) whose *only legal output is a signed per-pixel height field*; the bevel is an explicit stamp profile chosen per feature, not a rendering afterthought, so the owner's belief is correct and the bevel does **not** need to be added.
3. **As a fill** — `steel` and `steel_clean` are already genuine shape-independent tileable fills (measured: 4–8/255 difference between a shaped and an unshaped render); `plasma` is not a fill but a shape-coupled glow; all three lack a time input, a palette/quantisation step and shape-local coordinates, and are fast enough for a live single-frame preview in C# but not for per-frame animation without caching.
4. **As a height source** — the data is a single-channel float32 `H×W` toroidal field, signed, unnormalised (measured range −1.65 to +2.21), structured as flat plateaus joined by narrow authored ramps; a consumer takes the mask from its sign, the lighting from its wrapped gradient, and tier separation from its value, and Pyre already implements exactly that for a different producer.
5. **External vs in-tool** — port the algorithms in natively (live dials and animated fills are impossible with exported files), keep Tapestry as a standalone texture tool, cache inside the tool using Pyre's existing content-addressed caches rather than on disk, ship the 248 kept genomes as presets, and use the 248 published height fields immediately as a zero-port proof of the height→bevel path.
6. **Forgotten** — the Unity Tapestry tool already exists; the Shape/Surface split was already designed and deliberately deferred and is the same idea as the new generator model; the "a fill never authors its own bevel" decision was already settled; Pyre already has a heightmap channel; there is a full stage-by-stage port-verification contract system; there are 248 graded height fields and genomes; there is a written body of hard-won aesthetic rules; and three questions are sitting unanswered.
