# PyrePlus — advanced forms & matte parity (design)

Research handoff for bringing Pyre1's remaining capabilities into PyrePlus: the full luma-matte model, MetaBlob, HeightBalls, Bars, Fire, Fireball, and the stateful modifier. The governing constraint (agreed with the user): **do not port these as monolithic Pyre1 `LayerShape`s. Decompose each into PyrePlus's `shape + swarm + shared-capability` primitives**, so a Pyre1 "shape" becomes a *combination* of things PyrePlus already has plus a small number of new shared passes. That decomposition is also what makes them convertible from Pyre1 assets (see the companion conversion assessment). All line refs are into `Assets\Packages\Laubrary\Runtime\` unless noted.

## The core picture — three integration shapes

Everything here slots into one of three ways a PyrePlus layer can turn its swarm into pixels. Naming them explicitly is the whole design:

1. **Per-particle compositing** (exists today) — the swarm loop draws each particle and `Over`-composites it. Disc, Gem, Streak, Sprite, Star, Text, the solids.
2. **Stateless field-pass ("Coalesce" mode)** — after the swarm loop completes, read the *whole particle set* and replace per-particle compositing with a single field accumulation → threshold/ramp → shade. This is **MetaBlob (Fuse)** and **HeightBalls (Ramp)**. It is NOT a `PostModifier` (once each disc is `Over`-composited through its own edge clamp, the smooth pre-threshold field is destroyed — you cannot recover it from `buf.a`; `PyrePlusRenderer.cs:1142-1165`), and NOT a `ZuiFill` mode (fill is per-particle colour; fuse merges silhouettes *across* particles). Pyre already proved this shape: its `fuse` flag (`Layer.cs:555`) is a per-layer render-mode that collects `FusionCircle`s and runs `RenderFusedField` (`BlastRenderer.cs:847` + `:948` + `:1957`), not a modifier.
3. **Stateful per-layer sim (replay-keyed)** — retains frame-to-frame state; needs a replay harness. This is **Fire**, **Fireball**, and the **SimulationModifier**. It is the only shape that breaks PyrePlus's "every frame renders standalone" purity — but it stays fully deterministic (same seed+content+frame ⇒ same bytes, reached by replay from a fixed reset).

The unifying per-layer sequence all advanced features hook into (mirrors Pyre1's `FinishLayerPost`, `BlastRenderer.cs:137-201`), all on the **isolated per-layer scratch buffer**, after the swarm loop and before `CompositeLayer`:

```
RenderLayer (swarm loop)
  → [A metaball-fuse]     (stateless field-pass)
  → [B density-ramp]      (stateless field-pass)
  → [existing ApplyLayerPost: Bloom/Outline/... PostModifiers]
  → [C stateful sim]      (retained state + replay)
  → [D luma-matte apply]  (masks the FINISHED layer)
  → CompositeLayer
```

The work is therefore ~4 shared capabilities — **(1) a luma-matte port, (2) a Fuse field-pass, (3) a Ramp field-pass, (4) a stateful sim/replay harness + modifier slot** — and everything else is wiring existing swarm/form pieces into them.

## Correction: HeightBalls is stateless, not a sim

The `ShapeForm` enum comment (`PyrePlusSpec.cs:72-73`) currently groups *"Fire/Fireball/HeightBalls are simulation-backed and deferred."* **That is wrong for HeightBalls.** `BlastRenderer.cs:1493-1496` states it outright: *"Every ball's entire state at a frame … is a CLOSED-FORM function of (layer hash, group, ball index, layer life progress). Nothing accumulates between frames, so this scrubs, bakes and plays back identically."* The only retained sim state in the whole Pyre1 renderer is `_fireballSims` (`BlastRenderer.cs:208`) and `_fireSims` (`:256`) — Fire and Fireball only. So **three of the four "deferred sim forms" (MetaBlob, HeightBalls, and — trivially — Bars) are stateless field-passes and are quick, low-risk wins**; only Fire and Fireball need the hard replay machinery. The enum comment must be corrected as part of this work.

## Access & reuse facts (load-bearing)

| Symbol | Decl | Namespace | PyrePlus reuse |
|---|---|---|---|
| `FireSim`, `FireballSim` | `public class` (`FireSim.cs:50`, `FireballSim.cs:30`) | `Laubrary.Pyre` | **Reuse directly.** `Allocate`/`Reset`/`Step`/`Render` + `W`/`H`/`LastFrame` all public. |
| `FireParams`, `FireballParams`, `FireArmMode` | `public` | `Laubrary.Pyre` | Constructible, all fields public. |
| `SimulationModifier` | `public abstract : PyreModifier` (`PyreModifiers.cs:1280`) | `Laubrary.Pyre` | **Reference + drive via reflection.** `Render` is public; `EnsureFrame`/`SetSeed` are `internal` (`:1291`,`:1311`). |
| `PixelFluidModifier` | `public : SimulationModifier` (`PyreModifiers.cs:2081`) | `Laubrary.Pyre` | Reuse the instance; drive through the base via reflection. |
| `PyreModifier` (geo/pixel/post) | public | `Laubrary.Pyre` | Already reused today (`PyrePlusRenderer.cs:10-16`, `BuildMods`). |

`PyrePlus.asmdef` already references `com.Lautaro-Arino.Laubrary.Pyre` — **zero project-file edits needed**. There is no `InternalsVisibleTo` from Pyre to PyrePlus, so the `internal` sim-driver methods must be called by reflection — PyrePlus already carries that idiom (`PyrePlusRenderer.SetPostContext`, `:390-406`, reflecting `PostModifier.SetLife/SetSeed/SetFrameIndex`). Standing rule to honor: **PyrePlus must not modify Pyre** (`PyrePlusRenderer.cs:387`) — so anything needing a new Pyre entry point is either a PyrePlus-local re-port or a deliberate, separately-owned Pyre edit.

---

## Capability 1 — Luma-matte port (the convertibility keystone)

Today PyrePlus's matte is a deliberately-simplified **coverage-clip** model (`PyrePlusSpec.cs:84` literally says *"Simplified from vanilla Pyre's Draw/Matte"*): a `WriteMatte` layer deposits its alpha coverage into one of 4 numbered channels (`WriteMatteCoverage`, `PyrePlusRenderer.cs:258-270`); a Draw layer opts in via `clipByChannel` and the only effect is alpha-clip (`CompositeLayer`, `:280-283`). Pyre1's model is a **luma matte** with six effect channels. The two are different ideas, which is why matte does not convert — the earlier "do it like Pyre1" fix relocated matte into the per-layer row (correct) but never brought the *semantics* to parity.

**Decision: port Pyre1's model as the BASE** (strictly richer — 6 channels vs 1, scoped reach, luminance×alpha mask, animatable strength), and **keep PyrePlus's numbered-channel + `MatteCombine` as an additive union/carve option** on top.

### Push vs pull, reconciled

- **Pyre1 = PUSH.** The `Matte`-role layer is authoritative: it carries which channels, strength, blur/displace/hue amounts, and scope, and imposes them on the passive layers above. The mask is captured by the LOWER layer and applied to layers drawn ON TOP.
- **PyrePlus = PULL.** The `WriteMatte` layer is dumb (coverage only); the Draw layer opts in.
- **Winner: PUSH is the base.** Both are spatially identical (a lower layer supplies the mask, an upper layer is affected — `PyrePlusRenderer.cs:155`), so this is a semantics upgrade, not a direction flip. Keep the numbered-channel PULL path as a cheap explicit opt-in alongside.

### The exact Pyre1 pipeline to port faithfully (`BlastRenderer.cs`)

Mask build — `BuildMatteMask` (`:351-362`): `m = Luma(c) * (c.a/255)`; `if invert m = 1-m`; `mask[i] = Clamp01(m) * strength`, where `Luma` is Rec.601 (`:349`) and `strength = Clamp01(Eval(matteStrength, life, …))` (`:174`), a per-frame value over the matte layer's own life.

Channel dispatch — `ApplyMatte` (`:371-382`): `MatteChannel` is a `[Flags]` set; every set bit runs in this FIXED order so any combination is deterministic — **Displace → Blur → Saturation → Hue → Brightness → Alpha** (spatial, then colour, then alpha). `None` falls back to `Alpha` (legacy-asset guard, `:374`).

Per-channel math (copy verbatim — all are dependency-free `Color32[]`/`float[]` ops, so they cross the asmdef boundary cleanly):
- **Alpha** (`:384-392`): `target.a = round(c.a * mask)`, RGB untouched.
- **Brightness** (`:394-403`): `target.rgb = c.rgb * mask`, alpha untouched (a shadow/light pass).
- **Saturation** (`:405-419`): `target.ch = Clamp(Lerp(Luma*255, c.ch, mask))`, alpha untouched.
- **Hue** (`:421-434`): RGB→HSV, `h = Repeat(h + (hueDegrees/360)*mask, 1)`, HSV→RGB; S/V/alpha untouched.
- **Blur** (`:436-467`): per-pixel VARIABLE radius `r = round(amount*mask)`, `maxR = Clamp(Ceil(amount),0,12)`; alpha-weighted box gather over `(2r+1)²`; clones target→src first. Separable is rejected (radius differs per pixel).
- **Displace** (`:469-488`): push each pixel along the mask's own SLOPE — `gx = mask[right]-mask[left]`, `gy = mask[up]-mask[down]`, sample src at `(x+round(gx*amount), y+round(gy*amount))`, edge-clamped. Only mask edges bend pixels.

`amount` is `Eval(matteBlurAmount…)` for Blur and `Eval(matteDisplaceAmount…)` for Displace (independent fields, so both spatial channels can act at once); `hueDegrees = Eval(matteHueDegrees…)`.

Scope — `MatteState.oneShot` (`:131`,`:182`,`:189-193`): a Matte layer captures its finished pixels into `matteState` and `return`s instead of compositing. Each subsequent Draw layer runs `ApplyMatte` before its own composite. `oneShot = (matteScope == NextLayer)` clears the state after one layer; `AllAbove` keeps masking every higher layer until another Matte layer overwrites it. Carried by-ref through the layer loop (`:555-596`).

### Concrete port

Spec (`PyrePlusSpec.cs`, `PyrePlusLayer` matte block, `:104-116`): keep `matteEnabled` (UI gate). **Serialization gotcha:** the existing `matteChannel` int (0..3) collides with the new `[Flags] MatteChannel` — RENAME the old int to `matteWriteChannel`, add a NEW `MatteChannel matteFlags` field (never reinterpret a serialized int as a different-meaning field). Add Pyre1's set: `LayerRole role` (rename `WriteMatte`→`Matte`, keep the serialized int value stable — enums here are append-only), `MatteScope matteScope`, `bool matteInvert`, `ZUIValue matteStrength=1`, `matteBlurAmount=3`, `matteDisplaceAmount=4`, `matteHueDegrees=60`. Keep additive: `matteWriteChannel` (0..3), `MatteCombine matteCombine`, and Draw-side `readChannel`(-1..3)/`readInvert` for the cheap alpha-clip path.

Renderer (`PyrePlusRenderer.cs`): extract the inline layer-finish (`:204-222`) into a `FinishLayer(ref MatteState)` mirroring `FinishLayerPost`. Copy the six channel functions + `Luma` + `BuildMatteMask` + `ApplyMatte` verbatim. A Matte-role layer builds `matteState.mask` after its own post/sim; each Draw layer runs `ApplyMatte(scratch, …)` before `CompositeLayer`, clearing on `oneShot`. Extend `needScratch` (`:208`) so a Draw layer under an active `AllAbove` matte also isolates (mirrors `BlastRenderer.cs:583`). New NEGATIVE single-field ids per the registry rule: `FldMatteStrength=-19, FldMatteBlur=-20, FldMatteDisplace=-21, FldMatteHue=-22`.

### The α-strength source (new feature the user proposed)

An option where the effect strength is driven by the COVERED layer's own alpha as `(1−α)`: fully opaque ⇒ no effect, soft/thin/edge pixels ⇒ full effect; the Alpha channel itself untouched. It is an **edge / soft-region mask derived from coverage** (same intent as a fresnel/edge mask, but from alpha).

Design: add `bool matteAlphaSource`. It does NOT change how the mask is built (still luminance×alpha of the matte layer); it modulates the mask per covered pixel: `effMask[i] = mask[i] * (matteAlphaSource ? (1 - alpha0[i]) : 1)`, where `alpha0[i]` is a SNAPSHOT of the covered layer's alpha taken ONCE at `ApplyMatte` entry — before any channel runs, because Displace/Blur rewrite alpha mid-pipeline and all six channels must read the same finished-coverage reference. Fully deterministic (no RNG; a pure function of the layer's own pixels). Pass `effMask` (not `mask`) to each channel. Subtlety to document: at α=0, `1−α=1` (full effect) but there is nothing visible to affect (the colour channels `continue` on `c.a==0`), so the visible effect lives entirely in the partial band `0<α<1` — exactly the anti-aliased rims / soft wisps / dissolve fronts you want. Combining it with the Alpha channel is near-degenerate; the UI should exclude or warn.

---

## Capability 2 — Fuse field-pass → MetaBlob

Stateless (confirmed: `RenderMetaBlob`, `BlastRenderer.cs:1353`, sums `layer.metaOrbs` every frame via `MetaEnv(t)` with `t` a pure function of layer life; no CWT, no buffer). Decompose as **swarm + a `Fuse` layer render-mode**: the swarm already produces N placed orbs each with a `size` (radius) envelope; when `Fuse` is on, `RenderSwarm` collects `FuseCircle{pos, r, weight}` for each alive particle instead of calling `DrawParticle`, then after the loop runs `RenderPlusFusedField` ported near-verbatim from `RenderFusedField` (`BlastRenderer.cs:1957`).

Per-pixel math (deterministic — reads only the seeded placements the swarm already produced):

```
field(px) = Σ_i  w_i · (1 − d_i²/r_i²)²         (d_i < r_i; else 0)      // BlastRenderer.cs:1422
threshold = max(0.02, fuseThreshold)
band      = clamp(fuseSoftness, 0.01, threshold)                         // cap prevents whole-frame wash
if field <= threshold-band: discard
alpha  = clamp01((field-(threshold-band))/band) · layerAlpha
frac   = clamp01((field-threshold)/fuseShadeRange)                       // 0=surface, 1=core
colour = shapeFill gradient at frac
```

New params on the layer: `fuseThreshold`, `fuseShadeRange`, `fuseSoftness`. Field map: `MetaOrb.Radius`→`layer.size`; `birth`→`swarmSpawnTiming`; `life`→`swarmParticleLife` (per-orb life becomes uniform — minor loss); `metaThreshold/metaShadeRange/metaSoftness`→the three new params; `colorMode/colorOverLife`→`shapeFill`. **Genuine gap:** `metaExpand` (a *live* uniform radial scale about centre over layer life) has no home — `shapeScale` animated is a spawn-*snapshot* (leaves a trail), not a live expand. This motivates a shared **`swarmScale`** (below).

Rejected alternatives (documented so it isn't relitigated): Fuse as a PostModifier — impossible, the SDF field is gone by composite time; Fuse as a fill mode — wrong axis, fill is per-particle.

---

## Capability 3 — Ramp field-pass → HeightBalls

Stateless (confirmed above). Same architectural hook as Fuse (a swarm render-mode reading the particle set), but a heavier field pass — `RenderHeightBalls` (`BlastRenderer.cs:1763`): a three-field `SmoothMax` accumulation (density / heat / height), relief lighting from the height field's local slope, then a single smoke→fire gradient. It needs two extra per-particle scalars the plain swarm doesn't carry, so add two per-particle envelopes (over own life) to the layer: **`density`** and **`heat`**, evaluated in the collect-loop alongside `size`/`alpha`.

```
Pass 1 (per live particle): dome s = √(1−q) inside its ellipse;
        density = SmoothMax(density, s·mass,  kD);  heat = SmoothMax(heat, s·height, kHe);
        height  = SmoothMax(height, s·(mass·2.65+height·0.72), kHi)      // BlastRenderer.cs:1855-1857
Pass 2: relief light = 0.18 + max(0, n·L)·0.82 from height-field slope    // :1892-1913
Pass 3: value = clamp01(density+heat)·light; colour = shapeFill gradient at value;
        opacity = clamp01(max(density,heat)·coverage)·alpha              // :1915-1942
```

New ramp-pass params: `fusion` (SmoothMax knee), `coverage`, `lighting`/`relief`/`lightAngle`, plus shared surface-noise rim deform (`rimScale` etc., the bit that makes it read as one boiling mass rather than fused flat discs). Field map: `waves×waveBalls`→`swarmCount`; wave births→`swarmSpawnTiming`/`SwarmTiming.FrameStep`; `ballSize`→`size`; `mass`→new `density` env; `height`→new `heat` env; `rotation`→`swarmRoll`/`swarmTurn` (already live); `colorOverLife`→`shapeFill`. **Genuine losses:** `squash` (per-ball ellipse — PyrePlus discs are circular; a ramp-pass ellipse param would restore it), `churn`/`churnSpeed` (idle boil), `hbFold` (confinement fold-under), and single-pass **cross-group fusion** (PyrePlus layers composite, they don't melt — to keep "a cool smoke base and a hot burst fuse into one mass" the ramp pass must accept several swarm sub-populations within ONE layer, mirroring `slices` in `BuildHeightBalls`, `:1633`).

Fuse and Ramp are the same family — a layer-level **"Coalesce" mode** that swaps compositing for a field accumulation over the swarm set, differing only in the field/shade math. One collect-loop in `RenderSwarm` populates a `List<FieldParticle>`; a mode enum dispatches to the chosen field pass after the loop.

---

## Capability 4 — Bars → Streak + Swarm (almost free)

Stateless (pure `Eval(lp)`). Key finding that removes most of the work: **the PyrePlus Streak is already a uniform-width rectangle/capsule, not a tapering comet** — `DrawStreakBody` (`PyrePlusRenderer.cs:1716`) uses constant `halfW` along the streak; only `edgeSoftness` (sides) and `streakSoftTip` (ends) feather, silhouette stays rectangular. A Bar (`RasterBar`, `:1269`) is the same shape. So a Bar maps to a Streak with **no new geometry**.

Field map: `barWidth`→`streakWidth`; `barForward`→`streakLength`; `barBackwardFrac`→`streakAnchor`; `barSoftness`→`edgeSoftness`+`streakSoftTip`; the row (`barCount`/`barSpacing`)→a **Swarm on a LINE**; the star (`spreadCount`/`spreadDegrees`)→a **Swarm on a RING** (`SwarmShapeKind.Circle` + `SwarmSpawnMode.Path` + `swarmOrient=Outward` + `swarmPathSpread=spreadDegrees/360`); `barStagger`→`swarmSpawnTiming`; `BarDecay.Contract`→the `streakLength` grow-then-shrink envelope (the default already arcs, `PyrePlusSpec.cs:536-546`); `barAngleDeg`/`baseAngleDeg`→`shapeRotation`/`particleSpin`; `barMirror`→a second mirrored layer.

Two small additions needed: (1) a **`Line`** `SwarmShapeKind` (or use a straight Custom Path today) — `SwarmShapeKind` has no Line. (2) the ONE enrichment: **decouple per-index length from width** so `barTaper` reproduces exactly (equal-width bars of graduated length = a flame). `swarmScaleByIndex` currently scales `sizeMul` which hits both `streakLength` and `streakWidth`; for the Streak form, pass the index multiplier as a separate `lenByIndex` argument multiplying `len` only (`DrawStreakBody:1720`), leaving `w` (`:1722`) on the plain `sizeMul`. One argument, one multiply. **Does NOT map:** `BarDecay.Dissolve` (a per-index alpha front advancing across the row over layer life — cross-index coordination that per-own-life `alpha` can't express); defer as a future `swarmAlphaByIndex`-over-life or a per-swarm "dissolve front".

---

## Capability 5 — Stateful sim harness → Fire, Fireball, SimulationModifier

The only genuinely stateful shape, and the one architectural addition. **Reuse `FireSim`/`FireballSim` directly** (public, already referenceable) — do NOT re-port the grid physics; PyrePlus writes only a thin replay harness around them. `layer.gradient` (default fire ramp, `PyrePlusSpec.cs:464`) maps straight onto their `Render(…, Gradient ramp, …)`.

Sim state: FireSim keeps four `float[W*H]` grids (`heat/fuel/heatB/fuelB`) + `LastFrame` + a dirty AABB; FireballSim keeps two grids + `LastFrame`. Both `Render` only write above-threshold pixels and do NOT clear — so a sim layer must ALWAYS render into isolated scratch, never the shared-buf fast path (force `needScratch=true`, mirroring `hasLayerPost` including Fire/Fireball at `BlastRenderer.cs:582`).

Replay harness (a PyrePlus mirror of Pyre1's discipline, `BlastRenderer.cs:308`/`:210`, adapted to the stateless-call model):
- Cache = `ConditionalWeakTable<PyrePlusLayer, SimEntry>` where `SimEntry { sim; int contentHash; int lastFrame; }`.
- `contentHash` = hash of every field feeding the sim (form params, gradient, seed, W/H, swarm config). **This is the invalidation** — stronger than Pyre1's implicit same-frame-re-request: any edit changes the hash ⇒ forced cold replay, so PyrePlus never forward-steps across a param change.
- Per frame in `RenderLayer`: if hash/size changed → reset; if `f == lastFrame+1` → warm forward-step (O(1)); else → cold replay `start→f`; then `sim.Render(scratch, …)`; `Over`-composite.
- **Checkpoints (a PyrePlus-only win):** because invalidation is content-hash-based (params provably frozen within one hash — unlike Pyre1, which couldn't safely checkpoint), snapshot sim state every K≈8–16 frames and replay only `checkpoint→f`, turning a cold scrub from O(f) into O(K).

Swarm-driven emitters: **Fire is natural** — `FireSim` is already a multi-emitter shared grid (N arms injected into one field, `FireSim.cs:122-150`); generalizing "N arms around centre" to "one injection per alive swarm particle at its own position, heat/fuel from the layer's Fire envelopes at that particle's own life, phase-seeded by particle index" is the same operation with an external emitter set. Minimal API: lift the fixed arm loop into a caller-supplied `IReadOnlyList<HeatEmitter{x,y,radius,heat,fuel,phaseSeed}>` passed to `Step`. Because `ComputeSpawns` is `public static` and seed-keyed, replay recomputes the identical emitter list at each step ⇒ deterministic. **Fireball is forced single-source** (its cellular pass folds every cell into `arms` wedges assuming one centre; off-centre sources break the kaleidoscope that is its identity) — keep it single-source, let the swarm parameterize the one core's position/heat/arms but not multiply it. Sequencing recommendation: ship fixed-emitter Fire/Fireball first (reuse the sims verbatim, zero Pyre edits), then do swarm-emitter Fire as a PyrePlus-local re-port of just `Step` (genuinely new geometry, not duplicated code) or an upstream Pyre `public` overload.

SimulationModifier slot: add a per-layer `SimulationModifier` field that holds the same `PixelFluidModifier` instances and drives them by reflecting `SetSeed`+`EnsureFrame` (both `internal`) then calling public `Render` — the same reflection idiom as `SetPostContext`, null-guarded to degrade gracefully. This is the keystone that makes "swarm of emitters + a fluid SimulationModifier" a general recipe, not bespoke code. It runs in the sim slot (after the stateless posts, before the matte), on the isolated scratch.

---

## Determinism & verification

Non-sim layers/forms stay byte-identical to today (all advanced features are opt-in per layer; the default/empty path is untouched — same discipline as the existing modifier byte-identity gate). For sim layers, replace the "standalone hash" invariant with a **replay-determinism gate**: hash the canonical `0→N` cold replay for a fixed seed as the reference sequence, then assert (a) **cold==warm** — sequential forward-step reproduces the reference per-frame hashes; (b) **scrub==replay** — a cold jump to frame f equals reference frame f; (c) **checkpoint==replay** — checkpoint-accelerated `K→f` equals reference f; (d) **invalidation** — perturbing one sim field changes the content hash and drops the cache (no stale forward-step). These hold exactly (not within tolerance) because the sims use only integer `Hash01` + `PyreNoise` + float ops (no `Random`/`Time`), and `PixelFluidModifier` uses an explicit reset `ulong` PRNG. Field-ID registry additions: matte `-19..-22` (above); Fire/Fireball reuse Pyre's own `F_Fire*`/`F_Fireball*` id blocks (`BlastRenderer.cs:47-51`) when driven through the shared `Eval` funnel.

## Build slices (proposed order — risk-ascending, value-front-loaded)

0. **Foundations** — correct the `ShapeForm` sim-comment; extract `FinishLayer(ref MatteState)`; add the `Coalesce` render-mode scaffold (collect-loop → `List<FieldParticle>` → mode dispatch stub); add shared **`swarmScale`** (live uniform radial, sibling to `swarmTurn/Tilt/Roll`).
1. **MetaBlob** — the Fuse field-pass + params + field map. Stateless, byte-gateable, verbatim reuse of `RenderFusedField`. Quick win.
2. **HeightBalls** — the Ramp field-pass + the two per-particle envelopes (`density`,`heat`) + relief lighting + surface-noise rim. Note the losses (squash/churn/fold/cross-group).
3. **Bars → Streak** — the `Line` swarm kind + the one-line length-by-index enrichment. Defer Dissolve. Mostly authoring.
4. **Luma-matte port** — the six channels + mask + scope + `MatteState` by-ref; keep numbered-channel/`MatteCombine` as an additive option. Convertibility-critical.
5. **α-strength source** — small addition on top of slice 4.
6. **Stateful sim harness + Fire/Fireball (fixed emitter)** — the replay harness (CWT + content-hash + forward-step/replay/checkpoint), reuse `FireSim`/`FireballSim` as-is, new replay-determinism gate.
7. **SimulationModifier slot** — per-layer stateful modifier, reflection-driven `PixelFluidModifier`.
8. **Swarm-driven Fire emitters** (optional/later) — re-ported `Step` with the emitter-injection API.
9. **Pyre1→PyrePlus converter** — now real, because matte + all forms exist; the field maps above ARE the converter's per-form dispatch.

Slices 0–5 are all stateless and independently verifiable; 6–8 are the one architectural investment; 9 closes the original question that started this (convertibility).
