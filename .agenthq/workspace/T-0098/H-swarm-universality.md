# H — Swarm universality: is "run the generator N times with offsets" a correct universal model?

Investigator H, T-0098. Every load-bearing claim carries `file:line`. Paths are relative to `Assets/Packages/Laubrary/` unless marked otherwise. Where a prior report contradicts the code, the code wins and the report is named.

Per the brief, saved-asset usage counts are **not** used as evidence for or against any technique. Two migration-risk mentions are flagged as such.

---

## Bottom line

- **The owner is right, and more right than he knows.** "Run the generator N times with per-instance offsets" is not a proposal — it is **already the implementation** in 7 of the 9 plug-in generators. `ArcBurstForm.cs:474-483`, `OrbForm.cs:512-519`, `TorchForm.cs:178-199`, `JetFormBase.cs:120-129`, `PlasmaBloomForm.cs:454-465` are all literally `for (i in ctx.swarm) { set origin, set scale, set seed; RunGenerator(); }` accumulating into one shared plane. There is no architectural obstacle to generalising this. It is the majority pattern.
- **"Swarm does nothing here" is true for exactly three generators, and in all three the blocker is a missing parameter, not an impossibility.** Fireball's simulation hardcodes its source at the canvas centre (`Runtime/SpriteFx/FireballSim.cs:66`) and `FireballParams` has no position field at all (`:20-30`) — that is a ~10-line change, not a limit. Fire without the opt-in uses its own fixed arm emitters (`PyreRenderer.cs:405`). Playback3D renders nothing at bake time whatsoever (`PyreRenderer.cs:269`), so there is nothing to swarm.
- **The distinction the owner draws — "many instances composited" vs "many seeds interacting in one simulation" — is already a real, built, load-bearing split in Pyre, and BOTH halves already exist.** Interacting-seeds is implemented three times: Fire's `fireSwarmEmitters` (N emitters into ONE fluid grid, `PyreRenderer.cs:579-697`), Inferno/ForkBlast's `SwarmOrigin` (N blasts as N *events* in one simulation that know they are siblings and bias each other, `PyreInferno.cs:289-315`), and Coalesce Fuse/Ramp (N particles read as one metaball/height field, `PyreRenderer.cs:1497-1595`).
- **The real defect is not that swarm can't be universal — it is that the per-instance contract is under-specified, so forms improvised it inconsistently.** `PyreSwarmInstance` (`PyreForm.cs:61-71`) carries no seed at all, so every form invents one from the index (three different magic primes). And **only 2 of the 7 replicate-field forms give an instance its own clock** — ArcBurst (`:481-482`) and PlasmaBloom (`:462`) do; Orb, Torch and the three Jets use the *shared layer clock* and use `sp.own` only as an alive/dead filter (`OrbForm.cs:506,515,518`; `TorchForm.cs:155,181,184`; `JetFormBase.cs:113,123,128`). So an Orb/Torch/Jet instance pops into existence already mid-animation instead of starting its own lifetime — exactly the thing the owner assumed swarm did.
- **The one genuinely forceful counter-argument is cost, and it applies to precisely two generators — the stateful sims — where it is asymptotic, not constant-factor.** Fire/Fireball reach frame *f* by replaying 0→*f* (`PyreRenderer.cs:608-609`), so a clip bake is O(f²). N naive instances make that **O(N·f²)**; at canvas 64, 24 frames, 200 particles that is ~236 M cell-updates versus ~1.2 M for one. The interacting-seeds version costs **O(f²) regardless of N** — one grid, N emitters. This is not an argument against universal swarm; it is a proof that for stateful sims the owner's *second* alternative is the only sane one, and it is already built for Fire.
- **The melt-into-a-blob control is offered on layers where it provably cannot act.** `layer.coalesce` is read at exactly two lines in the entire runtime — `PyreRenderer.cs:1497-1498`, inside `RenderSwarm`, which `RenderLayer` returns before reaching for every plug-in form, Fire, Fireball and Playback3D (`:248-269`). The UI shows the control for all of them (`PyreWindow.cs:2206,2492-2495` — gated only on `swarmEnabled`). Report 1 is correct on this, and understates it: Playback3D shows the *entire* swarm section live while contributing zero pixels.

---

## 1. What swarm actually is today

### 1.1 One placement function, two consumers

`ComputeSpawns(spec, layer, into)` (`Runtime/Pyre/PyreRenderer.cs:1927`) is the **single** placement authority. It is pure and deterministic (`:1923-1926`) and is also called directly by the editor's preview overlay, so the dots you see are the real placements.

It emits `SpawnPoint` (`PyreRenderer.cs:154-165`):

| field | meaning |
|---|---|
| `spawnLife` | the particle's spawn moment on the layer timeline, 0..1 |
| `pos` | absolute canvas-pixel position (origin = buffer (0,0)) |
| `zNorm` | pseudo-3D depth after the shape tilt, −1..1 |
| `orientDeg` | per-particle facing, math angle CCW from +x |

Count: `n = max(2, layer.swarmCount)`, **except Text**, where `n = string length` (`:1942-1944`). Slider range 2..200 (`Editor/Pyre/PyreWindow.cs:2229-2230`).

Placement is a **spawn-time snapshot**: the whole shape transform (radius, offset, rotation, pitch, yaw) is evaluated at *that particle's* `spawnLife`, not at the current frame (`:1981-1989`), so an animated spawner leaves a trail rather than retroactively moving already-placed particles.

Per-instance randomness comes from the `particleIndex` argument to the one Eval funnel: `Eval(v, life, seed, particleIndex, fieldId)` → `new System.Random(Hash(seed, particleIndex, fieldId, _layerSalt))` for `MinMax` mode (`:5026-5036`). **The particle index IS the seed.** There is no separate seed value anywhere in the swarm data model.

### 1.2 Consumer A — built-in enum forms: `RenderSwarm`

`PyreRenderer.cs:1440-1596`. Per iteration `i`:

- `own` = the particle's own life clock, `(life − spawnLife) / particleLife`, or mapped onto a shared `deathPoint` when die-together is on (`:1510-1512`). **Dead/unborn particles are skipped** (`:1513`).
- `sizeMul = clamp(1 + 0.35·zNorm, 0.5, 1.6)`, `brightMul = clamp(1 + 0.30·zNorm, 0.55, 1.45)` (`:1517-1518`).
- `swarmScaleByIndex` folds a per-index size multiplier keyed on `i/(n−1)` (`:1525-1535`); the one bespoke per-form branch in the whole loop is Streak's `streakScaleLengthOnly` (`:1531-1534`).
- Live whole-cloud spin then scale about the canvas centre, evaluated frame-globally at `life` with the modifier-scope sentinel index — *one* value for the whole cloud, not a per-particle draw (`:1476-1486`, applied `:1541-1547`).
- Then either **collect** into a field (Fuse/Ramp, `:1553-1583`) or **draw** via `DrawParticle(buf, W, H, dp.x, dp.y, own, spec, layer, i, mods, phase, frameIndex, sizeMul, brightMul, sp.orientDeg, lenByIndex)` (`:1584-1585`).

So the varying quantities per iteration are: **position, own-life clock, size multiplier, brightness multiplier, facing, and the index that seeds every `MinMax` dial.**

Inside `DrawParticle` (`:2810`), every shape dial re-evaluates at `own` with `particleIndex = i` — size (`:2878`), alpha (`:2881`), per-particle travel path (`:2890-2894`), fill (`:2896-2900`). Geometry and pixel modifiers are applied **per particle** on this path (`mods` is passed into each draw).

### 1.3 Consumer B — plug-in `PyreForm`s: `RenderFormLayer`

`PyreRenderer.cs:285-360`. The renderer builds `PyreSwarmInstance[]` (`:297-339`) — the same math as `RenderSwarm`, but **every instance is included, alive or not**, and the form decides what an unborn/dead one means (`PyreForm.cs:58-60`). Fields (`PyreForm.cs:61-71`): `x, y, own, spawnLife, index, orientDeg, zNorm, sizeMul, brightMul`. **No seed field, no per-instance lifetime window, no per-instance rotation of the *layer's* own spin, no access to `layer.size`.**

`PyreFormCtx` (`PyreForm.cs:96-119`) carries `W, H, life, seed, layerSalt, fill, alpha, swarm[], geo[], pix[], phase, frameIndex, frameCount, evalAtLife`. It does **not** carry the `PyreLayer`, so a form cannot reach `layer.size` or `layer.particleSpin` even indirectly.

Then `form.Render(ctx, target)` (`:345`), and — unlike the stamp path — geometry modifiers are applied **once to the finished whole-layer buffer** (`:349-350`), not per instance, unless the form sets `HandlesGeometry` (`PyreForm.cs:201`; only Inferno does).

### 1.4 Where the swarm sits in the render sequence

`RenderLayerBody` (`Runtime/Pyre/PyreRenderer.Layers.cs:151-178`), in order:

1. `BuildMods` — this frame's geometry/pixel stack (`:164`)
2. **`RenderLayer` → the swarm dispatch and all generator work** (`:169`)
3. **Border** built from the finished silhouette (`:172-173`) — so the border rims the **whole cloud**, once, not each particle
4. `ApplyLayerPost` — post modifiers (`:174`)
5. `ApplyLayerSim` — simulation modifier slot (`:176`)
6. `FrameComposer.Apply` — clip/matte/composite into the frame (`PyreRenderer.cs:233`)

**The swarm is entirely internal to one layer, ahead of border, post and composite.** Nothing downstream of step 2 knows a swarm happened.

### 1.5 The dispatch fork (this is where "swarm does nothing" is decided)

`RenderLayer`, `PyreRenderer.cs:241-283`, in source order:

```
:248  if (layer.form != null)                  → RenderFormLayer   (swarm handed over as instances)
:258  if (shapeForm == Fire)                   → RenderFireLayer   (→ :399 opt-in swarm emitters, else fixed)
:261  if (shapeForm == Fireball)               → RenderFireballLayer (swarm never consulted)
:269  if (shapeForm == Playback3D)             → return            (nothing rendered at all)
:271  if (!layer.swarmEnabled)                 → one centred particle / one Text line
:282  else                                     → RenderSwarm
```

---

## 2. What "swarm does nothing here" actually means, per generator

**Categories used** (derived from the code, replacing report 1's three-way "multiply / place / ignored", which is too coarse):

- **STAMP** — one raster of the shape per particle, alpha-Over composited; per-particle life, size, brightness, facing, index-seed.
- **FIELD** — the *whole generator* runs once per particle into one shared float accumulator; interaction is additive/max, not Over.
- **MULTI-SEED** — instances are handed to a native multi-origin algorithm that knows how many siblings exist and varies them relative to each other.
- **FUSE** — the swarm is read as one field and the chosen generator is *discarded*.
- **INERT** — the swarm is computed and/or offered in the UI but never read by this generator.
- **DEAD** — the generator produces no pixels at all.

| # | Generator | Kind | Per-instance: pos | own clock | scale | bright | facing | seed | spawnLife | Evidence |
|---|---|---|---|---|---|---|---|---|---|---|
| 1 | Disc | STAMP | ✔ | ✔ | ✔ | ✔ | ✔ | index | — | `PyreRenderer.cs:1584`, `:2878-2894` |
| 2 | Gem | STAMP | ✔ | ✔ | ✔ | ✔ | ✔ | index | — | `:2847-2851` |
| 3 | Crescent | STAMP | ✔ | ✔ | ✔ | ✔ | ✔ | index | — | falls to Disc arith `:2878` |
| 4 | Sparkle | STAMP | ✔ | ✔ | ✔ | ✔ | ✔ | index | — | `Pyre.cs:443` |
| 5 | Sprite | STAMP | ✔ | ✔ | ✔ | ✔ | ✔ | index | — | `Pyre.cs:456-457` |
| 6 | Box | STAMP | ✔ | ✔ | ✔ | ✔ | ✔ | index | — | `:2847-2851` |
| 7 | Pyramid | STAMP | ✔ | ✔ | ✔ | ✔ | ✔ | index | — | `:2847-2851` |
| 8 | Can | STAMP | ✔ | ✔ | ✔ | ✔ | ✔ | index | — | `:2847-2851` |
| 9 | Orb (enum) | STAMP | ✔ | ✔ | ✔ | ✔ | ✔ | index | — | `:2857-2861` |
| 10 | Ring | STAMP | ✔ | ✔ | ✔ | ✔ | **✘ ignored** | index | — | `:2862-2867` — "intentionally ignores orientDeg" |
| 11 | Text | STAMP, **swarm consumed** | ✔ | ✔ | ✔ | ✔ | ✔ | index | — | `:1942-1944` count = string length; `:2819-2837` particle i draws char i |
| 12 | Streak | STAMP + bespoke branch | ✔ | ✔ | ✔ (or length-only) | ✔ | ✔ | index | — | `:1531-1534`, `:2871-2876` |
| 13 | Star | STAMP | ✔ | ✔ | ✔ | ✔ | ✔ | index | — | Disc path `:2878` |
| 14 | Polygon | STAMP | ✔ | ✔ | ✔ | ✔ | ✔ | index | — | Disc path `:2878` |
| 15 | **Fire** (default) | **INERT** | ✘ | ✘ | ✘ | ✘ | ✘ | ✘ | ✘ | `:258` → `:405` fixed-emitter sim; UI says so, `PyreWindow.cs:2179-2182` |
| 15b | **Fire** (`fireSwarmEmitters`) | **MULTI-SEED** | ✔ | ✔ | ✘ | ✘ | ✘ | index×7919 | ✔ (via own) | `:399-403` → `:641-697`; one `HeatEmitter` per **alive** particle into ONE grid |
| 16 | **Fireball** | **INERT (hard)** | ✘ | ✘ | ✘ | ✘ | ✘ | ✘ | ✘ | `:261`; sim centre hardcoded `Runtime/SpriteFx/FireballSim.cs:66`; `FireballParams` has no position `:20-30` |
| 17 | **Playback3D** | **DEAD** | ✘ | ✘ | ✘ | ✘ | ✘ | ✘ | ✘ | `:262-269` — "renders NOTHING here"; swarm UI shown in full (`PyreWindow.cs` `RebuildSwarm` special-cases only Fire/Fireball, `:2176-2191`) |
| 18 | ArcBurst (form) | FIELD | ✔ | **✔** | ✔ `swarmSize·sizeMul` | ✘ | ✘ | `7919·(index+1)` | ✘ | `ArcBurstForm.cs:467-485` |
| 19 | Orb (form) | FIELD | ✔ | **✘ shared clock** | ✔ | ✔ | ✘ | `index·104729` | ✘ | `OrbForm.cs:506,509-519` — `t = ctx.frameIndex` hoisted out of the loop |
| 20 | PlasmaBloom | FIELD (`accumulate:true`) | ✔ | **✔** `Clock(sp.own)` | ✔ | ✔ (amp) | ✘ | ✘ (none) | ✘ | `PlasmaBloomForm.cs:441-466` |
| 21 | Torch | FIELD | ✔ | **✘ shared clock** | ✔ | ✔ | **✔** (rotate-add) | `index·104729` | ✘ | `TorchForm.cs:155,158-201` |
| 22 | Jet | FIELD | ✔ | **✘ shared clock** | ✔ | ✔ (amp) | **✔** `fr.rot` | `index·104729` | ✘ | `JetFormBase.cs:113,117-129` |
| 23 | RadialJet | FIELD | ✔ | **✘ shared** | ✔ | ✔ | ✔ | `index·104729` | ✘ | same base, `RadialJetForm.cs:38` |
| 24 | ExplosiveJet | FIELD | ✔ | **✘ shared** | ✔ | ✔ | ✔ | `index·104729` | ✘ | same base, `ExplosiveJetForm.cs:56` |
| 25 | **Inferno** | **MULTI-SEED** | ✔ (NDC) | ✘ | ✘ | ✘ | ✘ | `i·977` | **✔** `start` | `InfernoForm.cs:193-208` → `PyreInferno.cs:289-315` |
| 26 | **ForkBlast** | **MULTI-SEED** | ✔ (NDC) | ✘ | ✘ | ✘ | ✘ | per-event | **✔** `start` | `ForkBlastForm.cs:166-180` |
| 27 | Coalesce = Fuse | **FUSE** (generator discarded) | ✔ | ✔ | ✔ | ✘ | ✘ | index | — | `:1497,1553-1565,1590-1591`; `:1552` "The form is ignored in both modes" |
| 28 | Coalesce = Ramp | **FUSE** (generator discarded) | ✔ | ✔ | ✔ | ✘ | ✘ | index | — | `:1498,1566-1583,1594-1595` |

### 2.1 The code that ignores the swarm, quoted

**Fireball.** `PyreRenderer.cs:259-261`:
> `// Fireball (slice 6b) — the second stateful sim form; identical treatment to Fire (no swarm, isolated`
> `// scratch, own replay harness). Dispatch and return before the swarm/DrawParticle path.`
> `if (layer.shapeForm == ShapeForm.Fireball) { RenderFireballLayer(target, W, H, life, spec, layer, frameIndex); return; }`

The *why* is in the sim, not the dispatch — `Runtime/SpriteFx/FireballSim.cs:66`:
> `float cx = (W - 1) * 0.5f, cy = (H - 1) * 0.5f;`

and `FireballParams` (`:20-30`) has exactly eight fields: `sourceHeat, sourceRadius, cooling, spread, reach, sharpness, arms, mirror`. **No position.** There is no API through which a caller could place a Fireball anywhere but the canvas centre.

**Playback3D.** `PyreRenderer.cs:266-269`:
> `// this canvas yet). A spec using this form renders NOTHING here (this layer contributes no pixels) —`
> `// honestly incomplete rather than faking a placeholder shape.`
> `if (layer.shapeForm == ShapeForm.Playback3D) return;`

**Fire, default path.** `PyreRenderer.cs:399-403` gates the swarm behind an opt-in; without it, `:405` builds a `FireSim` whose emitters are its own fixed arms.

### 2.2 Answers to the specific sub-questions in the brief

**"Is the plug-in scene case really 'ignite one of me per particle'?"** Yes, literally — all 9 plug-in forms contain a `for (i in ctx.swarm)` loop that re-runs the whole generator. Report 1's framing is correct.

**"What varies per instance — distinct seed? birth time? position? scale?"**
- **Position:** yes, universally.
- **Seed:** yes in 7 of 9, but **not from the contract** — `PyreSwarmInstance` has no seed field (`PyreForm.cs:61-71`); each form synthesises one from `index` with its own magic prime (`ArcBurstForm.cs:482` uses 7919; `OrbForm.cs:518`, `TorchForm.cs:184`, `JetFormBase.cs:125` use 104729; `PyreInferno.cs:298` uses 977). PlasmaBloom synthesises none at all — every bloom in the swarm shares the form's seed, decorrelated only by position.
- **Birth time:** carried as `spawnLife` (`PyreForm.cs:65`) but read by **only two forms**, Inferno and ForkBlast (`InfernoForm.cs:204`, `ForkBlastForm.cs:177`). The other seven ignore it.
- **Life phase:** carried as `own` (`PyreForm.cs:64`). **Only ArcBurst (`:481-482`) and PlasmaBloom (`:462`) use it as a clock.** Orb, Torch and the three Jets use it purely as an alive/dead filter and drive the generator on the *shared layer clock* (`OrbForm.cs:506`, `TorchForm.cs:155`, `JetFormBase.cs:113`). Inferno and ForkBlast use neither — they don't even filter dead instances.
- **Scale:** yes — `sizeMul` (`PyreForm.cs:69`) times each form's own `swarmSize` dial (`JetFormBase.cs:35-36`, `ArcBurstForm.cs:104-105`). Used by all 7 FIELD forms; ignored by Inferno and ForkBlast.
- **Rotation:** yes — `orientDeg` (`PyreForm.cs:67`). Used by Torch (`:198`, via an isolated scratch + nearest-rotate-add) and the Jets (`JetFormBase.cs:126`). Ignored by ArcBurst, Orb, PlasmaBloom, Inferno, ForkBlast.

**Correction to report 1 (`PYRE_GUG.md:67`).** The claim *"they cannot read the layer's size or spin at all — because the handover they receive simply doesn't include those two values"* is **half wrong**. `PyreFormCtx` indeed does not carry `layer.size` or `layer.particleSpin` (`PyreForm.cs:96-119`) — that part is correct. But it *does* carry a per-instance scale (`sizeMul`) and a per-instance rotation (`orientDeg`), and five forms actively use them. The honest statement is: **the layer-wide size and spin dials are absent; the swarm-derived per-instance scale and facing are present and used.** Report 1's wording reads as though no scale or rotation reaches a form at all, which the code contradicts.

---

## 3. The owner's central claim, tested against the code

> *"if it can be generated once it could be generated many times with different parameters… That would work for any generator right?"*

### 3.a Is there any generator where N-with-offsets is *impossible*?

**No. Not one.** Three come close and each fails for a different, fixable reason:

**Fireball — blocked by a missing parameter, not by physics.** The sim is a per-cell rule reading a neighbour one step closer to the source (`Runtime/SpriteFx/FireballSim.cs:106-125`). The source is hardcoded at `cx = (W-1)*0.5f, cy = (H-1)*0.5f` (`:66`) and `FireballParams` cannot express any other (`:20-30`). Adding `sourceX/sourceY` to the params struct and threading them through `Step` is the whole fix. Nothing about the algorithm requires a central source — the confinement (`:86`), wedge folding (`:97-104`) and cooling (`:118-124`) are all expressed relative to `cx,cy` and would follow a moved origin unchanged. Note that N *coexisting* Fireball sims is also not blocked: each is two `float[W·H]` (`:44`) — at canvas 64 that is 32 KB per instance.

**Fire (default) — already solved once.** `PyreFireSim` (`Runtime/Pyre/PyreFireSim.cs:57`) is a fork of `FireSim` whose only difference is that the fixed N-arm inject loop was replaced with *"one soft disc per caller emitter"* (`:110-126`). The generalisation the owner is describing has already been performed on the harder of the two sims and shipped.

**Playback3D — not a swarm problem.** It has no bake-time renderer at all (`PyreRenderer.cs:262-269`). You cannot swarm a generator that does not generate. This is the only honest "not found" in the set, and it is a hole in the generator, not in the swarm.

**One case where the swarm slot is genuinely already occupied: Text.** `ComputeSpawns` overrides the count with the string length (`:1942-1944`) and `DrawParticle` maps particle *i* to character *i* (`:2825`). Swarming Text "N times with offsets" would mean N copies of the whole string — a different, incompatible meaning for the same control. This is a **semantic collision**, not an impossibility, and it is the one place where the owner's uniform model needs an explicit second axis (instances × glyphs).

### 3.b Where the blocker is genuinely cost, and how bad

Canvas: default 64×64, range 16..256 (`Runtime/Pyre/Pyre.cs:1185,1259-1260`; slider `PyreWindow.cs:714-717`). Swarm count: 2..200 (`PyreWindow.cs:2229-2230`, min clamped at `:1944`).

| quantity | at canvas 64 | at canvas 256 |
|---|---|---|
| one `Color32[W·H]` layer buffer | 4,096 px = **16 KB** | 65,536 px = **256 KB** |
| PlasmaBloom supersampled plane (SS=2, `PlasmaBloomForm.cs:428-429`) | 128² = 16,384 samples | 512² = 262,144 samples |
| Torch supersampled plane (SS=3, `TorchForm.cs:139-140`) | 192² = 36,864 | 768² = 589,824 |
| ArcBurst plane (k = clamp(ceil(128/canvas),1,4), `ArcBurstForm.cs:455-456`) | k=2 → 128² = 16,384 | k=1 → 256² = 65,536 |
| Fireball sim state (2 × `float[W·H]`, `FireballSim.cs:44`) | 32 KB | 512 KB |
| PyreFireSim state (4 × `float[W·H]`, `PyreFireSim.cs:73-74`) | 64 KB | 1 MB |

**The composite term is negligible.** A naive wrapper that renders each instance to its own scratch and composites costs N × W·H blends: 200 × 4,096 = 819 K blends per frame at canvas 64. That is not a cost argument against anything. And it needs **one** reusable scratch, not N — 16 KB total, not 3.2 MB.

**The generator term is where it bites, and it already bites today.** Two patterns exist in the current code:

- **Bounded (good).** `PyreTorch.Accumulate` computes a per-instance bounding box from the instance's own footprint and loops only that (`PyreTorch.cs:495-498`, `if (rw <= 0 || rh <= 0) return;`). Total cost scales with painted area, not N × canvas.
- **Unbounded (bad).** `PyrePlasmaBloom.Energy` is an unconditional full `for i in 0..S, for j in 0..S` pass with Fbm noise inside (`PyrePlasmaBloom.cs:358-379`). Per instance. At canvas 64 that is 16,384 heavy samples × 200 instances = **3.3 M samples per frame**; at canvas 256, 262,144 × 200 = **52 M per frame**, and a 24-frame bake is ~1.26 G samples. **This is present behaviour, not a hypothetical of universal swarm.**

So the cost answer is: *universal swarm is affordable if generators paint bounded regions; the one measured offender already ships and is a bug in that generator, not in the idea.*

**The exception is stateful.** Fire and Fireball reach frame *f* by replaying from 0 (`PyreRenderer.cs:606-610`, `:786-798`), i.e. O(f) per shown frame and O(f²) per clip bake. **N naive instances make that O(N·f²).** At canvas 64, 24 frames, 200 particles: Σ over frames of N·f = 200·24²/2 = 57,600 full-grid steps × 4,096 cells ≈ **236 M cell updates**, versus ~1.2 M for a single fireball. **That is a ~200× blow-up and it is asymptotic, not a constant factor.** The interacting-seeds version costs the same as one sim regardless of N: one grid, N emitters injected per step (`PyreFireSim.Step`, `:114-131`). This is the single strongest technical fact in this report, and it points *toward* the owner's second alternative rather than against his question.

### 3.c Where N-composited would be different and *worse* than N-interacting

This is the crux, and the code answers it directly.

**Fire.** N separate fire sims composited would show N flames that pass through each other without merging: no shared buoyancy field, no combined plume, no fuel competition. The built path instead injects one `HeatEmitter` per alive particle into **one** velocity/heat/fuel field (`PyreRenderer.cs:641-697` → `PyreFireSim.Step`, `:114-131`), so two nearby particles produce one wider flame. The editor states this to the user verbatim (`PyreWindow.cs:2197-2199`): *"each alive swarm particle injects heat/fuel into ONE shared fire field."*

**Inferno / ForkBlast.** These are the sharpest case, because the instances are **not independent**. `PyreInferno.MakeEvents` (`:289-315`) sets `bool multi = origins != null && origins.Length > 1` and then, per event, computes a fire↔smoke character from the event's position in the sequence:

```
float bal = !multi ? 0f
    : Mathf.Clamp(pLocal.charDrift * (q * 2f - 1f)
                  + SignedHash(i, 617, seed + 29) * pLocal.charJitter, -1f, 1f);
```
(`PyreInferno.cs:304-307`, with `q = 0` first blast … `1` last, and `fireMul`/`smokeMul` derived at `:313-314`.)

A blast **knows how many siblings it has and where it falls among them**, and the whole sequence is authored as a gradient from sootier to fierier. N independently-rendered Infernos composited could not reproduce this — each would be "neutral" (`bal = 0`), and the drift/jitter dials would become dead controls. Note also that ForkBlast's header records that multi-detonation *used to* be an in-generator array (`blast_at`/`blast_pow`/`blast_off`) and was **deliberately replaced by the swarm** (`PyreForkBlast.cs:36-38`) — the swarm is the sanctioned multi-seed mechanism here, not an outer wrapper.

**Coalesce Fuse/Ramp.** The whole point is interaction: the metaball sum `field = Σ weight·(1−d²/r²)²` produces *necks* between overlapping particles (`PyreRenderer.cs:1605`, pass at `:1617`), which N composited discs cannot. Ramp likewise builds one height field and relief-lights it (`:1594-1595`).

**The FIELD forms are already halfway to interacting.** PlasmaBloom passes `accumulate: true` (`PlasmaBloomForm.cs:463`) and takes the *minimum* rim distance across instances (`RimPlane(..., min: true)`, `:464`), so overlapping blooms genuinely brighten and share one rim. Torch accumulates into shared `_Fp`/`_C` planes (`TorchForm.cs:184,197`). Jets accumulate into `_scratch.H`/`_scratch.T` (`JetFormBase.cs:118,128`). None of these is "N pictures composited" — they are all "N contributions summed into one field, then shaded once". **The owner's two alternatives are not a dichotomy; the existing code sits between them and mostly on the interacting side.**

**Where composited is genuinely the right answer:** the STAMP forms. Alpha-Over of discrete objects is the correct semantics for gems, sprites, letters and streaks; two 50 %-alpha discs building to 75 % is what you want. Report F correctly notes (`F-decomposability.md:39`) that no accumulation rule reproduces Over exactly — so a forced "one field" model would *break* these, which is the honest cost of the interacting-seeds direction.

---

## 4. The asymmetries

### 4.1 Fire can use the swarm as emitters; Fireball cannot — verified, and the *why* is a struct field

Report 1 (`PYRE_GUG.md:98`) is correct. The mechanism:

- Fire: `layer.fireSwarmEmitters && layer.swarmEnabled` diverts to `RenderPlusFireLayer` (`PyreRenderer.cs:399-403`), which drives `PyreFireSim` — a fork of `FireSim` whose *only* change is a caller-supplied emitter list replacing the fixed arm loop (`PyreFireSim.cs:110-113`, inject loop `:123-131`). `HeatEmitter` carries `x, y, radius, heat, fuel, phaseSeed` (`PyreFireSim.cs:47-54`), so an emitter is fully placeable.
- Fireball: no such fork exists, because `FireballParams` cannot express a position (`FireballSim.cs:20-30`) and the sim reads the canvas centre directly (`:66`).

This is a **missing four floats**, not a design boundary. Report 1's recommendation (`:291` "Give Fireball the swarm-emitter option Fire already has") is technically sound and cheap. Report 1's characterisation of it as an *"odd asymmetry"* (`:98`) is right; its earlier framing that simulations have *"essentially no swarm"* (`:17`) is **wrong for Fire**, which has a full native multi-emitter path.

### 4.2 Melt-into-a-blob is only for plain stamps with the swarm on, and the control is offered where it cannot act — verified, and worse than reported

**Read sites.** `layer.coalesce` is read at exactly **two** lines in the entire runtime:
```
PyreRenderer.cs:1497   bool fuse = layer.coalesce == LayerCoalesce.Fuse;
PyreRenderer.cs:1498   bool ramp = layer.coalesce == LayerCoalesce.Ramp;
```
(verified by `grep -rn '\.coalesce' Runtime/` — no other hits). Both are inside `RenderSwarm`, reachable only via `RenderLayer:282`, i.e. only when `layer.form == null` **and** `shapeForm ∉ {Fire, Fireball, Playback3D}` **and** `swarmEnabled`.

**It discards the generator.** `PyreRenderer.cs:1552`:
> `// The form is ignored in both modes: a coalescing particle is always a circle/dome.`

**The UI offers it regardless of form.** `RebuildSwarm` (`PyreWindow.cs:2146`) returns early only for Fire-without-emitters and Fireball (`:2177-2191`); otherwise it falls through `if (!s.swarmEnabled) return;` (`:2206`) to the Coalesce radio at `:2492-2495`. It is **not** gated on `s.form == null`, and Playback3D is **not** in the early-return set.

So the control appears and does nothing on:
1. every layer with a plug-in form (all 9),
2. every Playback3D layer — which additionally shows the entire swarm section live while rendering zero pixels,
3. a Fire layer with `fireSwarmEmitters` on (the early-return is skipped at `:2176`, but `RenderPlusFireLayer` never reads `coalesce`).

Report 1 (`PYRE_GUG.md:308`) lists all three plus swarm-off layers. **Confirmed. The swarm-off case is handled by the UI** (`:2206` returns before the control), so that fourth item in report 1's list is wrong — the control genuinely is hidden when the swarm is off.

### 4.3 Report 2 §7.2: a fill is evaluated per particle, in the particle's own frame, at its own life — verified, and the setting is named

**Mechanism.** In `DrawParticle`, the fill is hoisted at the particle centre and re-evaluated per pixel for spatial modes (`PyreRenderer.cs:2896-2900`), where `life` is the parameter — which for a swarm particle **is** `own` (`:1584` passes `own` as the `life` argument; the comment at `:2884-2889` states this explicitly: *"this method's `life` IS that own clock in both callers"*).

**The named setting** is `ZuiFill.space`, of type `ZuiFill.FillSpace { Stamped, Fixed }` (`Zui/Scripts/Runtime/ZuiFill.cs:47`, field at `:111`). Its own documentation (`:108-110`):
> *"Default Stamped reproduces v1 exactly — the consumer feeds shape-local (u,v). Fixed asks the consumer to feed canvas-anchored (u,v) instead, so the pattern stays put while the shape moves through it."*

The Pyre implementation is `EvalFill` (`PyreRenderer.cs:4987-4992`): `Fixed` substitutes the pixel's absolute canvas position for the shape-local one. **Report 2 §7.2 is correct on both halves** — per-particle evaluation, and a named canvas-anchored alternative sitting beside it.

One addition report 2 does not make: `brightMul` (swarm depth shading, `:1518`) multiplies the *result* per particle, so a "one fill over the assembly" mode loses depth shading too, not just the per-particle clock. Report F flags this (`F-decomposability.md:39`); report 2's §7.2 does not.

---

## 5. What a universal swarm would actually require

### 5.1 The smallest honest contract

`PyreSwarmInstance` (`PyreForm.cs:61-71`) is already **90 % of it**. The gaps, in order of importance:

1. **A per-instance seed field.** Today every form invents one from `index` with a different prime (7919 / 104729 / 977; PlasmaBloom invents none). Add `public int seed;` to `PyreSwarmInstance` and have `RenderFormLayer` fill it with `PyreRenderer.Hash(spec.seed, i, FldSwarmSeed, layerSalt)`. Zero forms break — they can keep their current expression until migrated. This is the single change that makes "different parameters per instance" a contract rather than a folk practice.
2. **A per-instance lifetime window, stated as a contract obligation.** `own` and `spawnLife` already exist; what is missing is the *rule* that a generator must run on `own`, not on `ctx.life`. Five of seven FIELD forms currently violate what an author would reasonably expect (§2). Fixing Orb/Torch/the three Jets to drive their internal clock from `sp.own` is a one-line change each and is arguably a **latent bug fix**, not a feature — but note it is an output-changing edit, so authored assets using those forms with a swarm would shift (migration risk, flagged as such, no usage claim made).
3. **A per-instance transform that is honestly a transform.** `sizeMul` + `orientDeg` is a similarity transform missing only a non-uniform scale and an explicit pivot. Promoting it to an explicit 2×3 (or `pos/rot/scale` triple with a documented pivot at the instance origin) would let a form apply it mechanically instead of each form threading two floats by hand.

**What a generator must return.** Today, a whole-layer form paints into an isolated transparent `Color32[]` that the renderer hands it (`PyreForm.cs:221-223`) and may `SET` pixels freely. That is *already* the "returns a buffer" contract, and it is why 7 forms could implement N-instance loops without renderer changes. **The only generators that violate it are the two sims**, whose `Render` methods **assign rather than composite**:
- `FireSim.Render`: `buf[i] = new Color32(...)` for above-threshold cells, leaving the rest untouched (`Runtime/SpriteFx/FireSim.cs:297-307`) — hence `PyreRenderer.cs:253-257` forces an isolated scratch.
- `FireballSim.Render` is worse: below threshold it writes `buf[i] = default` (`Runtime/SpriteFx/FireballSim.cs:146`), **erasing** whatever was there. A second Fireball rendered into the same buffer would wipe the first everywhere it isn't hot.

So the honest contract addition is: *a generator must either composite Over into the target, or declare that it overwrites and be given its own scratch to be composited by the caller.* Both sims already get an isolated scratch, so the fix is one reusable scratch + one `CompositeLayer` call per instance — 16 KB at canvas 64.

**What is NOT needed:** a `PyreFormKind.PerParticle` dispatch. That enum value exists (`PyreForm.cs:33`, documented "reserved — the renderer does not dispatch it yet") but **`Kind` is never read anywhere in the codebase** (verified: `grep -rn '\.Kind' Runtime/Pyre Editor/Pyre` returns nothing) and no form overrides it. It is a dead declaration. The universal-swarm contract does not need a new dispatch mode; it needs the existing `WholeLayer` hand-off to be complete.

### 5.2 The three buckets, with fractions

Counting the 28 rows in §2 (17 live `ShapeForm` slots including the two dispatch variants of Fire, 9 plug-in forms, 2 Coalesce modes; the two `[Obsolete]` enum slots `Inferno`/`ForkBlast` at `Pyre.cs:114-115` are excluded as retired):

**(i) Compliance is essentially free — 21 of 28 (75 %).**
The 14 STAMP forms (`Disc`…`Polygon`) already do per-instance everything through `DrawParticle`. The 7 FIELD plug-in forms already loop the generator per instance into a shared accumulator. Together these are **already the universal model in operation.** What they need is not implementation, it is *normalisation*: a contract seed, a clock rule, and Ring/ArcBurst/Orb/PlasmaBloom picking up the `orientDeg` they currently drop.

**(ii) Would need a naive wrapper (loop full renders, composite) — 2 of 28 (7 %).**
**Fireball** and **Fire-without-emitters**. Both are stateful; both overwrite rather than composite; both need per-instance scratch. And for both, the naive wrapper is the **wrong** answer on cost grounds (§3.b: O(N·f²)) and on look grounds (§3.c). Fire already has the right answer built. Fireball's would cost four params.

**(iii) A native interacting-seeds implementation exists and is better — 5 of 28 (18 %).**
**Fire-with-emitters**, **Inferno**, **ForkBlast**, **Coalesce=Fuse**, **Coalesce=Ramp**. In each, N seeds enter one simulation/field and the result is not reachable by compositing N renders.

**(iv) Out of scope — 1 (Playback3D).** No bake-time generator exists (`PyreRenderer.cs:269`).

**So: the owner's model is already implemented for three quarters of the library, has a strictly better native alternative for a fifth of it, and needs real work on exactly one generator.**

---

## 6. The honest counter-argument

There is one, it is real, and it is narrow.

**Universal swarm as a naive N-full-renders wrapper is asymptotically wrong for stateful generators, and would be a performance regression that no amount of tuning fixes.** Fire and Fireball reach frame *f* only by replaying 0→*f* from a fixed reset (`PyreRenderer.cs:606-610`; `:786-798`), and any authoring edit forces a cold replay because invalidation is content-hash based (`:596`, `:419-420`). A clip bake is therefore O(f²) per instance. Wrapping N instances makes it O(N·f²): at canvas 64 / 24 frames / 200 particles, ~236 M cell updates versus ~1.2 M — and every dial twiddle in the editor pays it again. Checkpointing is explicitly deferred (`:381-384`), so there is no mitigation in place. **For these two generators, "swarm as a wrapper" must be refused and "swarm as many emitters into one grid" offered instead** — which costs O(f²) regardless of N and is already built for Fire (`PyreFireSim.cs:110-131`).

**A second, smaller objection.** Forcing the STAMP forms into an interacting/one-field model would change their semantics for the worse. Per-particle alpha-Over is correct for discrete objects, and no field accumulation rule reproduces it (report F, `F-decomposability.md:39`, is right about this). So "one swarm model for everything" must mean *one contract with two composition rules* (Over for discrete stamps, accumulate for fields), not one composition rule.

**What is NOT a valid objection, and should not be recycled from the earlier reports:**

- *"Cost forbids it."* Only for the two sims, and only in the naive form. The generator-side cost of the FIELD forms is dominated by whether they bound their pass (`PyreTorch.cs:495-498` bounded; `PyrePlasmaBloom.cs:358-362` not) — a per-generator quality issue that exists today with or without any swarm redesign.
- *"Plug-in scenes are a different animal that can't take the shared controls"* (`PYRE_GUG.md:67`). Half wrong; see §2.2.
- *"Simulations have essentially no swarm"* (`PYRE_GUG.md:17`). Wrong for Fire, which has a full native multi-emitter path (`PyreRenderer.cs:399-403`, `:579-697`).
- *"Swarm is inherently three roles — multiply / place / ignored"* (`PYRE_GUG.md:39`). Under-describes it. The code shows **five** behaviours (STAMP, FIELD, MULTI-SEED, FUSE, INERT) plus one dead generator, and the "ignored" bucket contains exactly one hard case (Fireball) whose blocker is four missing floats.

**Overall verdict for the synthesis writer.** The owner's intuition is correct and the reports were sloppy: "swarm" has been used for at least four distinct implemented mechanisms, and the claim that it cannot generalise does not survive contact with the source. The correct framing is not *"can every generator be swarmed?"* (yes, essentially all already are) but *"what does an instance receive, and does the generator merge with its siblings or stack on top of them?"* — two axes, both already present in the code, neither currently named.

---

### Appendix: files read

`Runtime/Pyre/PyreForm.cs`, `Runtime/Pyre/PyreRenderer.cs`, `Runtime/Pyre/PyreRenderer.Layers.cs`, `Runtime/Pyre/PyreFireSim.cs`, `Runtime/Pyre/Pyre.cs`, `Runtime/Pyre/Forms/Kiln/{ArcBurstForm, ForkBlastForm, InfernoForm, OrbForm, PlasmaBloomForm, TorchForm, PyreInferno, PyreForkBlast, PyreTorch, PyrePlasmaBloom}.cs`, `Runtime/Pyre/Forms/Kiln/Jet/{JetFormBase, JetForm, RadialJetForm, ExplosiveJetForm}.cs`, `Runtime/SpriteFx/{FireSim, FireballSim}.cs`, `Zui/Scripts/Runtime/ZuiFill.cs`, `Editor/Pyre/PyreWindow.cs`. Prior reports: `PYRE_GUG.md`, `PYRE_SHAPE_FILL_BORDER.md`, `.agenthq/workspace/T-0098/{A,B,F,G}*.md`.

**Not found / not verified:** no bake-time renderer for `Playback3D` exists to inspect (`PyreRenderer.cs:262-269` is a documented stub). No profiling data exists in-repo; all cost figures in §3.b are derived analytically from buffer dimensions and loop bounds cited above, not measured at runtime.
