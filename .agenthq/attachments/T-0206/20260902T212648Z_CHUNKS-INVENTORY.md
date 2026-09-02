# Chunks inventory — file:line facts (T-0206, 2026-09-02)

Read-only research. Every claim below is grounded in source, checked by grep/read on 2026-09-02 against `D:\UNITY\Laubrary Dev` (branch `dev`). Paths are given relative to `D:\UNITY\Laubrary Dev\Assets\Packages\Laubrary\` unless stated otherwise (outside consumers give the full path). Where I could not verify something, I say so rather than guessing.

---

## §1. `ChunkSpec` — every serialized field, its consumer

`Runtime/Chunks/ChunkSpec.cs` (497 lines). One `[CreateAssetMenu]` `ScriptableObject : IVisualPreview`. Field groups, in file order:

### Emission (`ChunkSpec.cs:20-35`)
| Field | Line | Consumer |
|---|---|---|
| `countMin`, `countMax` | 20-22 | `ChunkEmitter.SpawnBurst` — `Random.Range(countMin, countMax+1)` for the chunk loop count (`ChunkEmitter.cs:59`) |
| `speedMin`, `speedMax` | 25-27 | `ChunkEmitter.SpawnBurst:99` — per-chunk launch speed |
| `directionDeg` | 30 | `ChunkEmitter.SpawnBurst:60` centre direction fallback when no override; also read by `ChunkFollowEmitter.ResolveDirectionDeg:294`, `ChunksDemoSpawner` |
| `spreadDeg` | 33 | `ChunkEmitter.SpawnBurst:97` cone half-angle |
| `upwardBias` | 35 | `ChunkEmitter.SpawnBurst:101` added to `vel.y` |

### Physics (`ChunkSpec.cs:40-50`)
| Field | Line | Consumer |
|---|---|---|
| `gravity` | 40 | `Chunk.Update:128` |
| `drag` | 43 | `Chunk.Update:129` |
| `angularSpeedMin/Max` | 46-48 | `ChunkEmitter.SpawnBurst:105` (non-tumbling spin roll) |
| `faceVelocity` | 50 | `Chunk.Update:167` |

### Life / look (`ChunkSpec.cs:55-74`)
| Field | Line | Consumer |
|---|---|---|
| `lifeMin/Max` | 55-57 | `ChunkEmitter.SpawnBurst:106` |
| `sizeMin/Max` | 60-62 | `ChunkEmitter.SpawnBurst:107` |
| `sizeOverLife` (curve) | 65 | `Chunk.ApplyLook:218` |
| `alphaOverLife` (curve) | 67 | `Chunk.ApplyLook:232` |
| `colorOverLife` (gradient) | 69 | `Chunk.ApplyLook:231`; also `ChunkSpec.ProceduralSwatch:323` (thumbnail) |
| `sprites` (List<Sprite>) | 72 | `ChunkEmitter.SpawnBurst:88-90` random pick when no anim/sample; `ChunkSpec.ResolvePreviewSource:271-273` |
| `pixelsPerUnit` | 74 | `ChunkSprites.Random/Get/Ensure` (procedural fallback PPU), `SampledChunkSprites.Sample` PPU param |

### Floor / collision (`ChunkSpec.cs:79-89`)
| Field | Line | Consumer |
|---|---|---|
| `useFloor` | 79 | `Chunk.Update:135` |
| `floorY` | 81 | `Chunk.Update:135-137`; `ChunksDemoSpawner.BuildFloor` reads it for the demo floor visual |
| `bounciness` | 84 | `Chunk.Update:138` |
| `floorFriction` | 87 | `Chunk.Update:139` |
| `restOnFloor` | 89 | `Chunk.Update:143-154` |

### Sampled pseudo-3D debris (`ChunkSpec.cs:97-125`)
| Field | Line | Consumer |
|---|---|---|
| `sampleSource` | 97 | `ChunkEmitter.SpawnBurst:77-78` via `UsesSampledDebris`; `FragmentSlicerModule.Fire:140` fallback; `ParticleSplashModule.ResolveSprite` indirectly via slicer fallback does NOT touch this directly — splash's own fallback chain in `ParticleSplashModule.Fire:175` reads `ctx.Spec.sampleSource` |
| `samplePxMin/Max` | 99-101 | `SampledChunkSprites.Sample` size range, called at `ChunkEmitter.cs:78` |
| `tumble` | 104 | `ChunkEmitter.SpawnBurst:93` (`sampled && spec.tumble`) |
| `tumbleSpeedMin/Max` | 106-108 | `ChunkEmitter.SpawnBurst:104` |
| `tumbleShadeStrength` | 111 | `Chunk.ApplyLook:224` via `ChunkTumble.Evaluate` |
| `tintMode`, `tintColor`, `tintStrength`, `edgeThicknessPx` | 117-125 | all four forwarded verbatim into `SampledChunkSprites.Sample(...)` at `ChunkEmitter.cs:78-79` |
| `modifiers` (`[SerializeReference] List<PixelModifier>`) | 133 | `SampledChunkSprites.Sample → Build → ApplyModifiers` (`SampledChunkSprites.cs:180-201`), one-time bake at spawn via `SpriteFxFilter.Apply` |
| `UsesSampledDebris` (computed) | 136 | `ChunkEmitter.cs:77` |

### Animated content (`ChunkSpec.cs:143-146`)
| Field | Line | Consumer |
|---|---|---|
| `animationSource` (Object) | 143 | `ChunkEmitter.SpawnBurst:63` via `AnimationSource`; `FragmentSlicerModule.ResolveSource:109` does NOT read this field (it has its own `sourceVisual`) — `ChunkSpec.animationSource` is consulted only by the emitter and by `ResolvePreviewSource:260` |
| `AnimationSource` (computed) | 146 | as above |

### Chunks 2.0 modules (`ChunkSpec.cs:155-180`)
| Field | Line | Type / consumer |
|---|---|---|
| `particleSplash` | 155 | `ParticleSplashModule`; dispatched by `ChunkModules.Run:44` |
| `pyreSpawn` | 158 | `PyreSpawnModule`; dispatched by `ChunkModules.Run:52` (only when no formation supersedes it) |
| `pyreMotion` | 161 | `PyreMotionModule`; NOT dispatched by `ChunkModules` — applied inline by `PyreSpawnModule.SpawnOne:214` (`ctx.Spec?.pyreMotion?.Apply(...)`) |
| `fragmentSlicer` | 164 | `FragmentSlicerModule`; dispatched by `ChunkModules.Run:45` |
| `spawnFormation` | 167 | `SpawnFormationModule`; dispatched by `ChunkModules.Run:49-50`, supersedes `pyreSpawn` when enabled |
| `blastGroups` (List<PyreSpawnModule>) | 173 | dispatched by `ChunkModules.Run:57-59`, one per group, keyed by `ChunkModules.BlastGroupTrack(group, i)` |
| `layers` (`LayerSpec`) | 177 | folded into `ChunkModuleContext.Layers` at `ChunkModules.Run:37`; consulted by every module via `ctx.OrderFor`/`ctx.ApplyOrder` |
| `timeline` (`ChunkTimeline`) | 180 | `ChunkModules.Run:42` fires markers; `ChunkModules.Dispatch:113` reads `DelayFor` per module name |

### Hit detection (`ChunkSpec.cs:188-193`)
| Field | Line | Consumer |
|---|---|---|
| `useHitDetection` | 188 | `Chunk.Init:85` |
| `hitDamage` | 190 | `Chunk.Init:104` → `Hitbox.damage` |
| `hitRadiusScale` | 193 | `Chunk.Init:98` |

### Trail (`ChunkSpec.cs:200-206`)
| Field | Line | Consumer |
|---|---|---|
| `trailSource` (Object) | 200 | `Chunk.Update:194-201` via `TrailSource` |
| `trailInterval` | 203 | `Chunk.Update:198-200` |
| `TrailSource` (computed) | 206 | as above |

**Legacy fields NOT yet migrated per the design's schema-1 plan:** the design doc (§2 of `CHUNKS-DESIGN-DECISIONS.md`) describes `schemaVersion` and `[HideInInspector]` legacy fields, but **`ChunkSpec.cs` currently has no `schemaVersion` field and no `capabilities` list at all** — every field above is the CURRENT (pre-migration) flat layout; there is no legacy/new split in source yet. W1.1 is building schema 1 from scratch, not migrating an already-half-built one.

**Fields NOT read anywhere I could find a consumer for:** none found — every field above traced to at least one reader.

---

## §2. Module classes — fields, `Fire`, timeline keying, `ChunkModuleContext` needs

All in `Runtime/Chunks/`. All implement `IChunkModule` (`Modules/ChunkModuleContext.cs:71-82`: `bool Enabled`, `string LayerName`, `void Fire(in ChunkModuleContext)`) except `PyreMotionModule` (not an `IChunkModule` — see below).

### `ParticleSplashModule` — `Splash/ParticleSplashModule.cs` (282 lines)
- Fields: `enabled`(18), `layerName`(21, default `"Splash"`), `sprite`(28), `emitFromFootprint`(34), `countMin/Max`(37-39), `sizePxMin/Max`(43-45), `speedMin/Max`(48-50), `inheritBurstDirection`(55), `directionDeg`(59), `spreadDeg`(62), `gravity`(65), `drag`(68), `lifeMin/Max`(71-73), `alphaOverLife`(76), `seed`(80).
- `Fire` at `ParticleSplashModule.cs:144-242`. Source-resolution waterfall (`ResolveSprite:96-101` → caller's `ctx.Palette` → `ctx.Spec.sampleSource` → white) documented at 156-177. Spawns loose `GameObject`s with `SpriteRenderer` + a private `ChunkSplashParticleFade` component (251-281) for alpha+destroy; motion goes through `ctx.Runner.Move` (239).
- Timeline key: `ChunkModules.Splash` (`"Splash"`, `Modules/ChunkModules.cs:19`).
- `ChunkModuleContext` needs: `Origin`, `Container`, `DirectionDeg`, `Spec` (for `ResolveSprite`+`sampleSource`), `Palette`, `Runner`, `ApplyOrder`.

### `PyreSpawnModule` — `PyreSpawn/PyreSpawnModule.cs` (249 lines)
- Fields: `enabled`(32), `layerName`(35, default `"Blast"`), `label`(44, group display name), `useFormation`(51), `formation`(55, `SpawnFormation`), `source`(63, Object→`IChunkEffectSpawner`), `pool`(68, `List<Object>`), `offset`(72), `rotationMode`(76, enum `PyreSpawnRotation`), `fixedAngleDeg`(79), `randomAngleMinDeg/MaxDeg`(82-85), `scaleMin/Max`(88-91), `seed`(95).
- `Fire` at `PyreSpawnModule.cs:234-247`: if `useFormation`, delegates the WHOLE group to `SpawnFormationRunner.Fire` (241); else `ResetRandom()` + `SpawnOne(ctx, ctx.Origin + offset)` (245-246).
- `SpawnOne` (193-217) is the shared picking+rotate+scale+spawn entry both a plain fire and `SpawnFormationRunner` call; it also applies `ctx.Spec?.pyreMotion?.Apply(ctx, spawned, orderOffset)` (214) — this is the ONLY place `pyreMotion` is invoked.
- Timeline key: `ChunkModules.PyreSpawn` (`"Pyre Spawn"`, `ChunkModules.cs:20`); each `blastGroups[i]` gets its own key `ChunkModules.BlastGroupTrack(group,i)` = `"Blast " + (index+2)` (`ChunkModules.cs:73-74`).
- `ChunkModuleContext` needs: `DirectionDeg` (rotation resolve), `Layers`/`OrderFor` (slot), `Origin`.

### `PyreMotionModule` — `Motion/PyreMotionModule.cs` (101 lines) — **NOT an `IChunkModule`**
- Deliberately not dispatched by `ChunkModules.Run` (per its own doc comment, `PyreMotionModule.cs:9`: "it fires nothing of its own and has no burst-time hook"). Its only entry point is `Apply(in ChunkModuleContext, Transform spawned, int index)` at line 75, called exclusively from `PyreSpawnModule.SpawnOne:214`.
- Fields: `enabled`(21), `speedMin/Max`(25-27), `inheritBurstDirection`(31), `directionDeg`(35), `spreadDeg`(39), `upwardBias`(42), `gravity`(46), `drag`(49), `faceVelocity`(51), `untilTargetEnds`(56), `lifeSeconds`(60), `seed`(64).
- Hands off to `ctx.Runner.Move(spawned, velocity, gravity, drag, life, faceVelocity)` (98) — never touches the transform itself, for the pool-reuse reason documented at lines 10-15.
- **Implication for W1.1**: a `Trajectory` capability (design §3) is a MODIFIER whose `Fire` would need to reach into a sibling producer's just-spawned transform — today that coupling is hard-wired one level deep (`PyreSpawnModule` calls it directly). The new model's `targetId` resolution has no direct analogue yet; this is new plumbing, not a lift.

### `FragmentSlicerModule` — `Slicer/FragmentSlicerModule.cs` (309 lines)
- Fields: `enabled`(26), `layerName`(29, default `"Fragments"`), `sourceVisual`(35, Object→`IChunkAnimation`), `source`(40, Sprite fallback), `pieceCount`(44), `seed`(47), `minPieceAreaPx`(51), `speedMin/Max`(55-57), `useBurstDirection`(60), `directionDeg`(63), `spreadDeg`(68), `gravity`(71), `drag`(75), `angularSpeedMin/Max`(78-80), `lifeMin/Max`(83-85), `alphaOverLife`(88).
- `ResolveSource()` (105-123) is THE precedence resolver (animated first frame → plain sprite), reused by `ParticleSplashModule.ResolveSprite:100` and `ChunkSpec.ResolvePreviewSource:264`.
- `Fire` at 129-227: falls back to `ctx.Spec.sampleSource` (140) if `ResolveSource()` is null; cuts via `FragmentCutter.Cut` (148, `Slicer/FragmentCutter.cs:116`); builds one `GameObject`+`SpriteRenderer` per piece, `ctx.ApplyOrder(sr, layerName, i)` (199); motion via `ctx.Runner.Move` (217); a shared coroutine `LiveAndDie` (237-275) drives spin+fade+destroy+texture cleanup for the whole set.
- Timeline key: `ChunkModules.Fragments` (`"Fragments"`, `ChunkModules.cs:22`).
- `ChunkModuleContext` needs: `Spec` (sampleSource fallback), `Origin`, `Container`, `Runner`, `ApplyOrder`, `DirectionDeg`.

### `SpawnFormationModule` — `Formation/SpawnFormationModule.cs` (37 lines) + `SpawnFormation` — `Formation/SpawnFormation.cs` (266 lines) + `SpawnFormationRunner` — `Formation/SpawnFormationRunner.cs` (70 lines) + `SpawnPlacement` — `Formation/SpawnPlacement.cs` (32 lines)
- `SpawnFormationModule` fields: `enabled`(17), `layerName`(20, default `"Blast"`), `formation`(23, `SpawnFormation`). `Fire` (28-35) just calls `SpawnFormationRunner.Fire(ctx, ctx.Spec?.pyreSpawn, formation, LayerName, ctx.Origin)` — it supersedes the SPEC-level `pyreSpawn`'s own placement (not its picking) per the design's stated rule.
- `SpawnFormation` fields (the shared placement engine, reused per-group by `PyreSpawnModule.formation`): `shape`(40, enum `Line/Ring`), `count`(44), `length`(48)/`angleDeg`(53) (Line-only), `radius`(58)/`arcDeg`(65)/`startAngleDeg`(69) (Ring-only), `positionJitter`(77), `staggerSeconds`(85), `staggerJitter`(94), `staggerOrder`(98, enum `Sequential/Reverse/FromCentre/Random`), `seed`(106). `Resolve(origin, results[, seedOverride])` (119-204) is the ONE layout+stagger resolver, also called with a seed override by the editor preview (per `ChunkWindow.Formation.cs`).
- `SpawnFormationRunner.Fire` (18-49): resolves placements, calls `spawner.ResetRandom()` ONCE (32), splits zero-delay placements (fired immediately, 38) from delayed ones (sorted + staggered via a coroutine `FireStaggered`, 47-48/54-68).
- `SpawnPlacement` is a readonly struct: `Position`, `Delay`, `Index` (13-23).
- Timeline key: `ChunkModules.Formation` (`"Spawn Formation"`, `ChunkModules.cs:21`).
- `ChunkModuleContext` needs: `Origin`, `Runner` (for the stagger coroutine), plus whatever `PyreSpawnModule.SpawnOne` needs (it's the thing actually spawned per placement).

### `LayerSpec` — `Runtime/Layering/LayerSpec.cs` (142 lines) — coordinator, no time
- Fields: `sortingLayerName`(26, default `"Default"`), `baseOrder`(30), `step`(34, default 10), `layers`(38, `List<string>`).
- `OrderOf`/`OrderAt` (59-75) is THE resolver every module calls through `ChunkModuleContext.OrderFor`/`ApplyOrder`. Degrades to `baseOrder` on an unknown/empty name (never throws) — line 74's comment: "an unknown, renamed, null or empty layer name resolves to baseOrder instead of raising."
- `Move(from,to)` (122-131) — drag-reorder; `UniqueName(desired)` (108-118) — dedupe on Add.
- Used by THREE other tools' own hand-rolled sortingOrder fields per its own doc comment (line 9): `ZoetropePyre/SpawnPyreFx.cs`, `ZoetropePyre/PyreChunksFx.cs`, and `Chunks/ChunkEmitter.cs` each still carry an independent flat `sortingOrder` — `LayerSpec` is the shared answer but is NOT yet adopted by those two ZoetropePyre files (they are named in the comment as the motivating problem, not as consumers of the fix).

### `ChunkTimeline` — `Timeline/ChunkTimeline.cs` (282 lines) — occupies time only via markers
- `ChunkTimelineTrack` (41-49): `moduleName` (always one of the `ChunkModules` consts), `delay`.
- `ChunkTimelineMarker` (53-83): `kind` (enum `Code/Zound`), `time`, `codeName`, `zoundName`, `IsEmpty`, `DisplayName`.
- `ChunkTimeline` fields: `enabled`(91), `tracks`(95, `List<ChunkTimelineTrack>`), `markers`(98, `List<ChunkTimelineMarker>`), `windowSeconds`(103, view-only, clamped `[MinWindow=0.25, MaxWindow=30]`, lines 107-108).
- `DelayFor(moduleName)` (118-127) — 0 when off/unscheduled; called by `ChunkModules.Dispatch:113` and `ChunkEmitter.ContainerLifetime` (via `TimelineDelay`, `ChunkEmitter.cs:182-183`).
- `Duration` (170-184) — max over ALL tracks+markers, "**not yet consulted by `ChunkEmitter.ContainerLifetime`**" per its own comment at line 169 — confirmed true: `ContainerLifetime` (`ChunkEmitter.cs:135-179`) sums `TimelineDelay` PER MODULE directly, never calls `spec.timeline.Duration`. This is a real, live discrepancy in current code, not just a stale comment — worth flagging to the migration since W1.1's `ChunkCues` will need the CORRECT max-of-everything computation the design spec (§2) describes, and the current `Duration` property already does that but is dead code.
- `Fire(in ChunkModuleContext)` (196-203) → `Schedule` (218-239): fires markers ≤0 immediately, schedules later ones on `ResolveRunner` (245-258, reuses the burst's own `ChunkModuleRunner` or spins up a throwaway `GameObject` host for a container-less `Play()` call).
- `Play(spec, origin, container=null)` (209-214) — standalone entry a game/preview can use WITHOUT going through `ChunkModules`.
- Zound playback routes through `ChunkZoundHook.Play` (277) — never references `Zounds` directly.

### `ChunkZoundHook` — `Timeline/ChunkZoundHook.cs` (30 lines) — runtime bridge seam
- `public static Action<string> Play` (24), filled by `Runtime/ChunksZounds/ChunkZoundPlayLink.cs:14-20` (`[RuntimeInitializeOnLoadMethod]` + `[InitializeOnLoadMethod]`) → `ZoundEngine.PlayZound(name)`.
- `Available` (28) — whether an audio tool registered.

### Hit detection — no separate module class
- Lives directly on `ChunkSpec` (§1 above) and is applied inline in `Chunk.Init:85-115` (adds `Rigidbody2D`+`CircleCollider2D`+`Hitbox` conditionally). There is no `HitDetectionModule` type — W1.1's `Hits` capability (design §3) has no existing class to lift; it would be new code extracting this inline block.

### Trail — no separate module class either
- `ChunkSpec.trailSource`/`trailInterval` (§1) consumed inline in `Chunk.Update:194-203`. `IChunkTrailSource` (`Runtime/Chunks/IChunkTrailSource.cs`, 15 lines) is the seam interface. No `TrailModule` class exists; W1.1's `Trail` capability is also new extraction, not a lift.

### Animation source — no module class
- `IChunkAnimation` (`Runtime/Chunks/IChunkAnimation.cs`, 17 lines): `GetFrames()`, `Fps`, `Loop`. Implementations found: `SpriteChunkAnimation` (own file, static single-sprite), `Laubrary.Pyre.PyreChunkAnimation`, `Laubrary.Pyre.Pyre` itself (`PyreChunksDirect.cs`, direct), `Laubrary.Launimator.LauminaryAnimationChunkAdapter`.

---

## §3. `ChunkEmitter.SpawnBurst` + `ContainerLifetime` + `ChunkModules.Run` control flow

`ChunkEmitter.cs:49-125` (`SpawnBurst`, static, shared by the component and the static `Chunks.Burst` API):
1. Early-out on null `spec` (53).
2. Make a throwaway `GameObject("ChunkBurst")` container at `worldPos`, parent it if `parent != null` (55-57).
3. Roll `count`, resolve `centerDeg` (override or `spec.directionDeg`), resolve `anim` (override or `spec.AnimationSource`) (59-63).
4. Loop `count` times: `ChunkPool.Get()`, parent under container, resolve sprite (sampled → sprites list → procedural `ChunkSprites.Random`) UNLESS an animation is set, roll velocity/spin/life/size, `chunk.Init(...)`, wire `Finished` → `ChunkPool.Release` (65-114).
5. `ChunkModules.Run(spec, worldPos, container, sortingOrder, centerDeg, palette)` (118) — hands off to the 2.0 modules.
6. If we own the container (`parent == null`), `Destroy(container.gameObject, ContainerLifetime(spec))` (121-122).

`ContainerLifetime(spec)` (`ChunkEmitter.cs:135-179`):
- Base `life = spec.lifeMax`, raised to `fragmentSlicer.lifeMax` if that module is enabled (143-145), `+= 2f` slack (146).
- If `!ChunkModules.AnyEnabled(spec)`, return `life` as-is (148).
- Otherwise sums the WORST-CASE scheduled tail across: `Splash`/`Fragments` timeline delays (155-156), `PyreSpawn` delay + `FormationTail` if that module's own formation is on (158-160), `Formation` (standalone) delay + tail (162-164), and EVERY `blastGroups[i]` keyed by `BlastGroupTrack` + its own formation tail (169-176). Returns `life + scheduled` (178).
- **As noted in §2, this method does NOT call `ChunkTimeline.Duration` — it re-derives the same "worst scheduled tail" independently, module by module.** Any new timed capability kind that W1.1 adds must be added to BOTH `ChunkModules.AnyEnabled`-style checks AND a line in this method (or the method must be rewritten to consult `ChunkTimeline.Duration`, which the design's §2 "clock length = max over ALL timed capabilities" language implies it should).

`ChunkModules.Run` (`Modules/ChunkModules.cs:27-60`):
1. Early-out on null spec/container or nothing enabled (30-31, via `AnyEnabled`).
2. Get-or-add a `ChunkModuleRunner` on the container (33-34).
3. Build one `ChunkModuleContext` (36-37).
4. `spec.timeline.Fire(ctx)` FIRST — markers at t=0 land before same-tick module dispatch (42, comment at 39-41 explains the ordering intent explicitly).
5. `Dispatch(spec.particleSplash, Splash, ...)` (44), `Dispatch(spec.fragmentSlicer, Fragments, ...)` (45).
6. Formation-supersedes-spawner branch: if `spawnFormation.Enabled`, dispatch IT under the `Formation` key; ELSE dispatch `pyreSpawn` under `PyreSpawn` (49-52).
7. Loop `blastGroups`, dispatch each under its own `BlastGroupTrack` key (57-59).
- `Dispatch` (108-117): reads `spec.timeline.DelayFor(moduleName)`; `delay<=0` fires synchronously; else starts `FireAfter` coroutine (`WaitForSeconds(delay)` then check `ctx.Container != null` before firing, 119-126).

---

## §4. Outside consumers of Chunks types (what W1.1 must keep compiling)

Grepped `Laubrary.Chunks`, `ChunkSpec`, `IChunkAnimation`, `IChunkEffectSpawner`, `IChunkTrailSource`, `ChunkEmitter`, `ChunkModule`, `Chunks.Burst`, `ChunkZoundHook`, `PyreSpawnModule`, `ChunkTimeline`, `LayerSpec` across `Assets/`, excluding `Runtime/Chunks/` and `Editor/Chunks/` themselves.

### asmdefs that reference `com.Lautaro-Arino.Laubrary.Chunks` (the runtime asmdef)
`D:\UNITY\Laubrary Dev\Assets\Demos\Laubrary.Demos.asmdef`, `Editor/Chunks/ChunksEditor.asmdef`, `Editor/ChunksZounds/com.Lautaro-Arino.Laubrary.Chunks.Zounds.Editor.asmdef`, `Editor/Launimator/LaunimatorEditor.asmdef`, `Editor/Zoetrope/ZoetropeEditor.asmdef`, `Runtime/ChunksZounds/com.Lautaro-Arino.Laubrary.Chunks.Zounds.asmdef`, `Runtime/Launimator/Launimator.asmdef`, `Runtime/Mirage/com.Lautaro-Arino.Laubrary.Mirage.asmdef`, `Runtime/Pyre/Pyre.asmdef`, `Runtime/Zoetrope/com.Lautaro-Arino.Laubrary.Zoetrope.asmdef`, `Runtime/ZoetropePyre/com.Lautaro-Arino.Laubrary.Zoetrope.Pyre.asmdef`.

### `Runtime/Pyre` — Pyre implements Chunks' interfaces directly (surprise #1, see report)
- `Pyre.cs` is `partial`; `Runtime/Pyre/PyreChunksDirect.cs` (194 lines) adds `IChunkEffectSpawner, IChunkAnimation, IVisualPreview` to `Laubrary.Pyre.Pyre` itself (class decl line 33). `SpawnEffect` delegates to `PyreDirectSpawn.Spawn`; `GetFrames/Fps/Loop` delegate to `PyreRenderer.GetFrames`/`DirectFps`. Comment at file top (lines 1-20) explains WHY: `PyreSpawnModule`'s `Object source` cast to `IChunkEffectSpawner` originally had ZERO real implementors in the project (only the wrapper below existed, unused), so a user opening the Chunks Blast picker got an empty browser. Direct implementation on `Pyre` fixed the dead end.
- `Runtime/Pyre/PyreSpawnSource.cs` (77 lines) — the WRAPPER `IChunkEffectSpawner`, still kept ("THE WRAPPERS STAY") for per-use overrides a shared Pyre asset can't carry (a play-this-one-at-30fps-looping-2s dial). `Editor/Pyre/PyreSpawnSourceEditorLink.cs` registers its LauAsset Open/Create.
- `Runtime/Pyre/PyreChunkAnimation.cs` (46 lines) — the WRAPPER `IChunkAnimation`, per-use fps/loop override over a shared Pyre.
- `Runtime/Pyre/PyreRenderer.cs:5153-5156` — a comment marking the "IChunkAnimation adapter support" section other Pyre code coordinates through.

### `Runtime/Launimator`
- `Runtime/Launimator/LauminaryAnimationChunkAdapter.cs` (full file read) — `ScriptableObject : IChunkAnimation, IVisualPreview`, picks a named animation off a `LauminaryVersion` by string (case-insensitive) so it survives rebakes.
- `Editor/Launimator/LauminaryAnimationChunkAdapterEditorLink.cs` — registers an "Edit" jump into the Laumination Builder for that adapter type via `ChunkAnimationEditors.Register<T>`.
- `Editor/Launimator/SpriteChunkAnimationEditorLink.cs` — registers Open/Create for the OTHER, simpler `SpriteChunkAnimation` type (lives in `Runtime/Chunks/SpriteChunkAnimation.cs` itself, not outside — listed here because its editor registration is a Launimator-side file).

### `Runtime/Zoetrope`
- `Runtime/Zoetrope/AmmoDef.cs:1-40` — `visual` field is `[RequireInterface(typeof(IChunkAnimation))] public Object visual;` (line 26-28) — a projectile's look is an `IChunkAnimation` reference, same unifier Chunks uses.
- `Editor/Zoetrope/ZoetropeWindows.cs:2729` — `AmmoDefWindow` builds `LauAssetElement.Build(a.visual, ..., typeof(IChunkAnimation), ...)` — the picker constraint.

### `Runtime/ZoetropePyre` (the presentation bridge)
- `Runtime/ZoetropePyre/PyreChunksFx.cs` — `ICombatFx` holding BOTH a `PyreAsset blast` and a `ChunkSpec chunks`; `Play`/`PlayFollowable` call `ChunksFx.Burst(worldPos, chunks, directionDeg)` (the aliased static `Laubrary.Chunks.Chunks.Burst`). Has its own `sortingOrder` field — one of the three flat-sortingOrder holdouts `LayerSpec.cs:9`'s comment names.
- `Runtime/ZoetropePyre/SpawnChunkFx.cs` — `IEffect, IEventParamUser` holding a `ChunkSpec chunks` field, scales the whole burst by a Zoe-event scalar param and samples live colours off the Zoe's current sprite (`EventContext`'s renderer) rather than authored data — the Chunks-only, scalable counterpart of `PyreChunksFx`.

### `Runtime/Mirage`
- `Runtime/Mirage/MirageChunkBurst.cs` — `[ExecuteAlways] MonoBehaviour`, the "this content type does not loop" driver: fires a `ChunkSpec spec` once via `ChunkEmitter.SpawnBurst` (line 89) and stands ready to `Fire()` again on demand (Replay button in `MirageHud`); Play-mode only, by design (doc comment explains why: `Chunk`/`ChunkModuleRunner` are not `[ExecuteAlways]`).
- `Runtime/Mirage/MirageRig.cs:284-301` — `case ChunkSpec spec:` in the previewable-realize switch, adds a `MirageChunkBurst` and assigns `spec`; explicitly NOT scale-applied (comment 290-298 explains why: `ChunkSpec` sizes are WORLD units, not source-sprite PPU-relative, so a container scale would desync trajectories from sprite size).
- `Runtime/Mirage/MirageAssetPicker.cs:44,51` — `SupportedTypes` includes `typeof(ChunkSpec)`; `FindAll` adds it under the label `"Chunk"`.

### `Editor/Mirage/MirageWindow.cs:726` — comment-only mention of `ChunkSpec` as an example of a `LauAssetEditors`-registered type; not a structural dependency.

### `Runtime/ChunksZounds` + `Editor/ChunksZounds` (the audio bridge, lives INSIDE the Chunks family but is its own asmdef, outside `Runtime/Chunks`/`Editor/Chunks`)
- `Runtime/ChunksZounds/ChunkZoundPlayLink.cs` — fills `ChunkZoundHook.Play` with `ZoundEngine.PlayZound`.
- `Editor/ChunksZounds/ChunkZoundPickerLink.cs` — fills `ChunkZoundPickerHook.Show`/`.Preview` (the EDITOR-side picker popup + audition-while-editing) with `ZoundPickerPopup.Show`/`ZoundEngine.PlayZound`.

### Demos (`Assets/Demos/`, own asmdef `Laubrary.Demos.asmdef`)
- `ArenaDemo/ArenaEnemy.cs:23,88-89` + `ArenaDemo/ArenaSpawner.cs:22,77` — `public ChunkSpec debris`, fired via `Chunks.Burst(center, debris, TintPalette())` on a killing click.
- `ChunksDemo/ChunksDemoSpawner.cs` — the Chunks tool's own demo driver: `radialSpec`, `directionalSpec`, `sampledSpec`, `composedSpec` (four separate `ChunkSpec` fields), fired via `Chunks.Burst(...)` on mouse/key input; `composedSpec` is explicitly the "fully AUTHORED composed burst" demo asset (`Floating Disc Blowup.asset`, see §8).
- `ColosseumDemo/ShmupDirector.cs:27,167` + `ColosseumDemo/ShmupEnemy.cs:9,26,88-91` — `public ChunkSpec debris`, fired via an aliased `ChunksFx.Burst(c, debris, new Color32[]{...})` palette overload.
- `DaemonDemo/ColosseumAgentBody.cs:22,96` — `public Laubrary.Chunks.ChunkSpec debris`, fired via the fully-qualified `Laubrary.Chunks.Chunks.Burst(pos, debris)`.
- `GalleryDemo/GalleryDirector.cs:24,29` — `public ChunkSpec deathDebris`, `public ChunkSpec impactSparks` (fields declared; a NOTE at lines 65-68 says no `IChunkAnimation` visual is wired on the demo's `AmmoDef`, deliberately deferred demo polish).

### `_T0075_Probe/ProbeChunk.asset` — a leftover probe asset from an earlier task (T-0075), all Chunks 2.0 modules disabled (§8).

### False positives (mention the string, no structural dependency)
`Runtime/SpriteFx/SpriteFxSpec.cs:12` (doc comment citing `ChunkSpec` as a naming-convention example), `Zui/Toolkit/Zui.cs:626` (doc comment citing `ChunkSpec`'s curve fields as the reason `Z.Curve` exists), `Editor/SpriteFx/SpriteFxStackEditorLink.cs`/`Editor/TextSplash/TextSplashEditorLink.cs`/`Editor/Pyre/PyreSpawnSourceEditorLink.cs` (doc comments citing `ChunkSpecEditorLink` as "same shape as" — no code reference), `Runtime/Zoetrope/ZoeReactionTelemetry.cs:9,19,32` (doc comments analogising to `ChunkTimelineEvents.HasListeners`'s "harmless when unheard" pattern), `Assets/Tests/Pyre/PlusFrameFillTests.cs:70` (a test METHOD named `MultiLayerSpec_IsParallelSafe` — matched the literal substring "LayerSpec", unrelated to `Laubrary.Layering.LayerSpec`).

**Bottom line for W1.1:** the hard compile-time surface to preserve is `ChunkSpec` (type name + the fields/properties actually read: `AnimationSource`, `TrailSource`, `UsesSampledDebris`), `IChunkAnimation`, `IChunkEffectSpawner`, `IChunkTrailSource`, `ChunkEmitter.SpawnBurst`/`.Burst`, the static `Chunks.Burst` overloads, `ChunkZoundHook`, and `LayerSpec`/`ChunkModuleContext` (consumed only from inside `Runtime/Chunks` itself plus the three ZoetropePyre files that hand-roll their own sortingOrder rather than using it). `PyreSpawnModule`/`ChunkTimeline` are NOT referenced outside `Runtime/Chunks`+`Editor/Chunks` by anything other than doc comments — they can be freely restructured by the capability-stack rewrite without touching another assembly.

---

## §5. Old `ChunkWindow` — helpers worth lifting into the new window

`Editor/Chunks/ChunkWindow.cs` (437 lines) + 10 partials (`ChunkWindow.Formation/Layers/MiragePreview/Modifiers/Preview/PyreMotion/PyreSpawn/Slicer/Splash/Timeline.cs`), total ~4400 lines. `public partial class ChunkWindow : ZuiAssetWindow<ChunkSpec>` (`ChunkWindow.cs:22`).

**Worth lifting as-is (the pattern, not necessarily the exact code — the field model is changing):**
- `Dial(string undoLabel, System.Action apply)` (`ChunkWindow.cs:60`) and `DialAndRebuild(string undoLabel, System.Action apply)` (71) — the two Undo-wrapping mutation helpers every other `Build*` method routes through. `Num2`/`Int2`-style wrappers (77-81) that pair a `Z.Field` label with a `Z.Float`/`Z.Int` bound through `Dial`. This IS the pattern the design spec's `Dial`/`DialAndRebuildCard` calls for — a direct lift of the shape, re-keyed per-card instead of per-window.
- **Per-card / per-section rebuild discipline**, not a whole-window rebuild: `RebuildModifiers()` (`ChunkWindow.Modifiers.cs:46`), `RebuildLayerSorting`/`RebuildLayerSlots`/`RefreshLayerOrders`/`RebuildLayerAssignments` (`ChunkWindow.Layers.cs:128,166,247,399`), `RefreshSlicerPreview`/`DrawSlicerStage` (`ChunkWindow.Slicer.cs:307,376`), `RefreshChunkPreview`/`DrawDebrisCell` (`ChunkWindow.Preview.cs:232,192`). None of these clear-and-rebuild the whole tree — each targets its own subtree. Directly matches the design's "per-card rebuild (`RebuildCard(id)`), not whole-window" requirement (§4 of `CHUNKS-DESIGN-DECISIONS.md`).
- **The formation live-preview registry pattern** (`ChunkWindow.Formation.cs:44-49,132-269`): a shared `List<IMGUIContainer> _formationPreviews` so ANY number of formation previews (the standalone module, EVERY blast group's own formation) can exist at once and all repaint together via `RepaintFormationPreviews()` (264-...), which also PRUNES entries a rebuild detached (checking `el.panel == null`). This solves exactly the "several unlike capabilities, each wanting a live spatial picture" problem the new Preview stage has, at smaller scale — worth reading before designing the new stage's per-capability guide-drawing dispatch.
- **The cut/slice preview stage** (`ChunkWindow.Slicer.cs` — `DrawSlicerStage`(376), `EnsureSlicerTex`(399), `OutlineCut` shared helper referenced at `ChunkWindow.Preview.cs:318`) — a worked example of a raw-IMGUI stage island painting on top of a real source sprite, exactly the "genuinely bespoke canvas painting stays raw" exception the UI guide allows. The new Preview's per-capability guide overlays (formation points, splash swatches, burst rays — already prototyped in the mock, §9) should follow this same "one shared texture buffer + `Z.DrawPixels`" idiom (`ChunkWindow.Preview.cs:186,201`), not reinvent it.
- **`LauAssetElement.Build` usage sites** — every module reference is already a picker, never a typed field: `ChunkWindow.PyreSpawn.cs:266` (single source), `:485,503` (pool add/edit), `ChunkWindow.cs:393` (`animationSource`), `ChunkWindow.cs:427` (`trailSource`). The exact 6-argument call shape (`current, onPick, constraint, thumbCache, suggestedName, folder, tooltip`, per §7 below) is the one to keep using for every new capability's asset-reference fields (Pyre picker, Trail source picker, Trajectory target — though target pickers are sibling-capability pickers, a different shape, see §2's Trajectory note).
- **The Zound marker picker hook** — `Editor/Chunks/ChunkZoundPickerHook.cs` (33 lines) is the EDITOR half of the same bridge-seam pattern as the runtime `ChunkZoundHook`; `Editor/ChunksZounds/ChunkZoundPickerLink.cs` fills `Show`/`Preview`. The new `Cues` capability's Zound marker (design §3) should reuse this hook unchanged — it is not Chunks-window-specific code, it is a static seam already living at the right layer.
- **Mirage preview handoff** — `ChunkWindow.MiragePreview.cs` (full file, 2 partials read): `BuildMiragePreview` gates the button on `worthPreviewing = anyDebris || anyModule` (bool check against `c.countMax > 0 || ChunkModules.AnyEnabled(c)`) so a truly-empty spec doesn't offer a preview that would show nothing; `EnsureMirageStageOpen()` finds-or-declines-to-open the `MirageStage` scene; `PreviewInMirage(spec)` creates an IN-MEMORY (never-saved) `MirageView` via `ScriptableObject.CreateInstance` + `AddEntry` + `MirageWindow.OpenFor(view)`. This whole file is a direct, near-line-for-line copy target for the new window (its own header comment at lines 1-9 says it was itself copied from `ZoetropeWindows.PreviewInMirage(Zoe)` — third use of the same pattern).
- **Legacy field editors NOT worth lifting as-is** (their bodies are keyed to the current flat-field model and will not survive the capability-stack rewrite, but their CARD STRUCTURE / row-packing is a good reference for the equivalent new capability): `BuildEmission`(191)/`BuildPhysics`(230)/`BuildFloor`(295)/`BuildHitDetection`(400)/`BuildTrail`(422) in `ChunkWindow.cs`; `BuildPyreSpawn`(59)/`BuildBlastBody`(245) in `ChunkWindow.PyreSpawn.cs`; `BuildFragmentSlicer`(59) in `ChunkWindow.Slicer.cs`; `BuildParticleSplash`(14) in `ChunkWindow.Splash.cs`; the whole of `ChunkWindow.Timeline.cs` (889 lines — the OLD single-track-list timeline UI, superseded outright by the new multi-lane `ZuiTimeline`-successor design).
- **`ChunkSpecEditor`** (`Editor/Chunks/ChunkSpecEditor.cs`, 210 lines) and **`ChunkFollowEmitterEditor`** (`Editor/Chunks/ChunkFollowEmitterEditor.cs`, 278 lines) are the two `[CustomEditor]` Inspector-panel classes (jump-into-window + animation-source-editor-jump for the spec; full ZUI inspector for the MonoBehaviour). Both stay largely as-is per the design doc's note that `ChunkFollowEmitter` "stays a component with its own inspector; it is not a capability" (§3) — only vetting against the UI guide is called for, not a rewrite.
- **`ChunkAnimationEditors`** (`Editor/Chunks/ChunkAnimationEditors.cs`, 26 lines) — the type-keyed "open this animation source in its own tool" registry (mirrors Pyre's `PyrePreviewSubjectProvider`). Stays unchanged; it is not part of the capability-stack model at all.
- **`ChunkSpecEditorLink`** (`Editor/Chunks/ChunkSpecEditorLink.cs`, 18 lines) — registers `LauAssetEditors.RegisterOpen<ChunkSpec>(ChunkWindow.OpenFor)` + `RegisterCreate`. Unchanged; `ChunkWindow.OpenFor` signature (`ChunkWindow.cs:29`) must stay stable for this to keep compiling.

---

## §6. PyreWindow patterns to copy — file:line

All in `Editor/Pyre/PyreWindow.cs` (2931 lines) unless noted. `PyreWindow : ZuiAssetWindow<Pyre>` (`PyreWindow.cs:20`).

- **`BuildAsset` layout order** — `PyreWindow.cs:307-408`. Order: `split` row container (309-312) → left `ScrollView` (fixed width, clamped `[360, 4*360+3*6]`, 318-326) → `Z.ColumnFlow(360f)` (333) holding, top to bottom: Views section (336-347, wrapped in a `Z.Section` so it joins the toggle bar), then `BuildCanvas`/`BuildLayerList`/`BuildGlobalModifiers`/`BuildShape`/`BuildSwarm`/`BuildModifiers` (354-359) → right pane: `preview` `IMGUIContainer` (371-377, class `zui-stage`, height clamped `[PreviewHeightMin, PreviewHeightMax]`) → `BuildPreviewResizeBar()` (378) → `chrome` div holding `BuildTransport` + `BuildBackdropPanel` (380-385) → `BuildCherryPanel` (389, grows to fill remaining space) → `split.Add(left, splitter, rightPane)` (391-393) → **`root.Add(BuildSectionToggleBar())` FIRST, then `TagsSection`, then `split`** (402-404) — the toggle bar spans the FULL window width above everything, confirmed by the comment at 396-401 explaining `TagsSection` is re-parented (detach-and-reattach is automatic via `VisualElement.Add`) to sit just under the bar. `viewBar.RestoreLast()` (407) runs LAST, after the whole tree is attached.
- **Vertical splitter (dial pane ↔ preview)** — `BuildVerticalSplitter()` at `PyreWindow.cs:413-430`: a 6px `VisualElement`, `PointerDownEvent` captures the pointer (419), `PointerMoveEvent` clamps `leftPaneWidth` into `[360, min(4*360+3*6, position.width-260)]` and live-sets `leftPane.style.width` (420-427), `PointerUpEvent` releases (428). **No double-click-to-reset handler exists in this splitter** — the blueprint (`CHUNKS-EDITOR-BLUEPRINT.md §7.3`) calls a layout reset a REQUIRED, NOT-YET-BUILT feature; `PyreWindow` itself does not have one either, so there is no working example to copy for that specific requirement — it has to be designed fresh (a `PointerDownEvent` with `e.clickCount == 2` check is the obvious shape, but Pyre's own splitter doesn't demonstrate it).
- **Preview resize bar (preview island height)** — `BuildPreviewResizeBar()` at `PyreWindow.cs:435-451`: same capture/move/release idiom, 6px `VisualElement` on the BOTTOM edge of the preview, clamps `previewHeight` into `[PreviewHeightMin, PreviewHeightMax]`.
- **Transport row** — `BuildTransport`/`RebuildTransport` at `PyreWindow.cs:519-644`. `transportHost` is a persistent `VisualElement` field (517) that gets `.Clear()`'d and rebuilt (528-529) rather than the whole chrome, when the Strip toggle needs to show/hide the Tile-px slider (546, `RebuildTransport(s)` called from inside its own toggle callback). Row 1 (`Z.HGroup`, 592): Play/Pause button (530-534, flips its own label text), `Frame` border toggle (538-541), `Strip` contact-sheet toggle (542-546), conditional `Tile px` `Z.MicroSlider` (548-553), `GIF…` export button (557-560), `GIF scale`/`GIF dither` (561-584), `Bake` button (587-591). Row 2: frame scrubber `Z.SliderInt` (601-610, PAUSES playback on drag — sets `playing=false` + resets the Play label, 606-607) wrapped in `Z.Field("Frame", ...)` (611-612). Row 3 (`WrapRow`, 640): `Zoom`/`Speed`/`Delay` `Z.MicroSlider`s (619-639) + two `Z.Text` readouts (`frameReadout`, `fillReadout` — the latter reserved-width-but-`Visibility.Hidden` when idle so its appearance never reflows the row, 630-632). `RefreshTransportReadout()` (649-660) keeps the scrubber range/value and the "frame N/M" text in sync after every frame change.
- **`BuildBackdropPanel`** — `PyreWindow.cs:676-687` (partial view read; body continues past 690) — wraps `BackSplashZui.Build(backSplash, "Preview backdrop", ...)`, persisted per-asset on `spec.previewBackSplash` (comment 81-90 explains the per-asset persistence + the property getter that lazily creates one).
- **`ZuiViewBar` minting + capture/apply** — `BuildViewBar(paneRoot)` at `PyreWindow.cs:462-480`. `Capture()`/`Apply()` closures (464-473) walk `paneRoot.Query<ZuiBox>().ToList()` and call `CaptureView`/`ApplyView` on every box found — this is how "a view" is defined: fold/gear/shown-control state on EVERY `ZuiBox` under the asset root, nothing about authored values. Constructed via `new ZuiViewBar(getStore, createStore, Capture, Apply, ViewPrefsKey)` (474-479, `ZuiViewBar` ctor signature at `Zui/Toolkit/ZuiViewBar.cs:40`). `CreateViewStore()` (`PyreWindow.cs:503-511`) mints the committed asset (`Assets/Pyre/PyreViews.asset`, folder-create-if-missing, `Undo.RegisterCreatedObjectUndo`) — ONLY ever called from the bar's own Save-as flow.
- **`ZuiSectionToggleBar`** — `BuildSectionToggleBar()` at `PyreWindow.cs:488-499`: `new ZuiSectionToggleBar("Pyre", (label, ZuiSection)...)` listing all eight top-level sections (Tags, Views, Canvas, Layers, Global Mod, Shape, Swarm, Modifiers) as `(string label, ZuiSection section)` tuples; a null section entry is skipped harmlessly by the bar itself (comment 482-487). Ctor signature: `ZuiSectionToggleBar(string prefsKey, params (string label, ZuiSection section)[] sections)` (`Zui/Toolkit/ZuiSectionToggleBar.cs:87`).
- **Per-section rebuild helpers** (naming convention to mirror for Chunks' per-capability-card rebuilds): `RebuildTransport`(526), `RebuildLayerList`(793), `RebuildAllForSelection`(949), `RebuildShape`(1248), `RebuildSwarm`(2146) — each targets one section's body, never the whole `BuildAsset` tree.

---

## §7. ZUI facts

All in `Assets/Packages/Laubrary/Zui/Toolkit/` unless noted.

- **`ZuiTimeline`** (`ZuiTimeline.cs`, ~450+ lines) — a SINGLE-STRIP scrub bar over consecutive `ZuiTimelineSegment` bands (name/seconds/color/tooltip struct, lines ~38-55), NOT multi-lane. Reached via `Z.Timeline(...)` per its own doc comment (line 34) — I did not find a `Z.Timeline` wrapper method in `Zui.cs` in this pass; it may be called directly as `new ZuiTimeline(...)` (ctor at line 105) rather than through a `Z.` factory — **worth double-checking before assuming a `Z.Timeline` factory exists.** Host contract: never owns the seconds (`SetSecondsWithoutNotify` for playback ticks, `Seconds` setter fires `OnChanged` for user drags, lines 60-103) and never decides what a band MEANS (just name/seconds/colour/tooltip, lines 18-20). **The occluded-tick bug the blueprint (`CHUNKS-EDITOR-BLUEPRINT.md §7.4`, P7) says is UNFIXED "because that file is production ZUI and this task's scope excludes production changes" appears to be ALREADY FIXED in current source**: `PlacePlayhead()` at `ZuiTimeline.cs:342-353` explicitly hides (`.visible = false`, never removes) any static tick whose span overlaps the playhead readout's span (351-352), and `AddTick` (322-338) already drops a new tick that would overlap an EXISTING one (325-326). This is either a fix landed after the blueprint was written (2026-08-31) or the blueprint's finding was about a different code path — flag to PM as a correction either way. **Multi-lane is still genuinely missing** — `ZuiTimeline` fundamentally draws one strip; the new Timing section (design §4/§6) needs a NEW control (one lane per timed capability, shared ruler+playhead) that does not exist yet in ZUI. The mock's `MockTimingTracks` (§9 below) is the prototype named by both the blueprint and the design doc.
- **`Z.Split`** — `Zui.cs:77`: `public static TwoPaneSplitView Split(string stateKey, float initialLeftWidth, ...)`. Persists the divider position in `EditorPrefs` keyed by `stateKey` (confirmed by the blueprint's own finding, §7.3, that a cold open with no prior EditorPrefs entry can come up at an unexpected width — "a cold open is not a first run"). **No reset method found on `Z.Split`/`TwoPaneSplitView` itself** — the "double-click resets" requirement (both the blueprint §7.3 and `CHUNKS-DESIGN-DECISIONS.md §4`) has to be hand-added at the call site (as `PyreWindow`'s OWN hand-rolled `BuildVerticalSplitter` would also need, since it isn't built on `Z.Split` either — see §6).
- **`ZuiBox`/`BoxKeyed` fold + view capture** — `Zui/Toolkit/ZuiBox.cs`: class at line 31; `CaptureView(Dictionary<string,bool> into)` (249) and `ApplyView(IReadOnlyDictionary<string,bool> from)` (258) are the fold/gear/shown-control state round-trip every `ZuiViewBar` aggregates over (see `PyreWindow.BuildViewBar`, §6); a `ViewChanged` event fires after any USER toggle/gear/fold change, never on a programmatic `ApplyView` (comment at line 97). Two `Z.BoxKeyed` overloads in `Zui.cs:183` (`(title, tooltip, stateKey, children...)`) and `:194` (adds an `icon` 4th positional arg — a comment at line 192 warns the plain-overload 4-arg call with a `VisualElement` still binds correctly, i.e. overload resolution here is a real footgun to be careful with when adding the icon overload to a call site).
- **`ZuiMenu`** — TWO classes of this name exist: `Zui/Scripts/Runtime/ZuiMenu.cs` (older, runtime-side) and `Zui/Toolkit/ZuiMenu.cs` (the current editor-toolkit one, `sealed class ZuiMenu` at line 26 — this is the one `ChunkWindow.PyreSpawn.cs`'s `ShowNewSlotForBlastMenu`/`ChunkWindow.Layers.cs`'s `ShowAddLayerSlotMenu`/`ChunkWindow.Modifiers.cs`'s `ShowAddModifierMenu` all build against). Fluent builder API: `.Search(...)`(55), `.Width(px)`(64), `.Above()`(67), `.OnClosed(cb)`(70), `.Section(title,tooltip)`(73), `.Separator()`(86), `.Item(label,tooltip,onClick,...)`(101), `.IconItem(...)`(122), `.Toggle(...)`(130), `.Radio(...)`(149), `.IconRow(...)`(171), `.Custom(build)`(208), terminated by `.Show()` (215, returns a `ZuiPopover`).
- **`LauAssetElement.Build`** — `Editor/AssetKit/LauAssetElement.cs:23-24`: `public static VisualElement Build(Object current, Action<Object> onPick, Type constraint, Dictionary<Object, Texture2D> thumbCache, string suggestedName, string folder, string tooltip)`. Returns a `ZuiChip` (left-click → browser, right-click → New/Edit/Clear context card, drag-drop accepted) — replaces an older always-visible button-row per `LAUASSET_PICKER_SWEEP.md`'s rule (comment at lines 15-19 admits that doc's claim "every chip site was already rewritten" was itself false for THIS control until it was fixed).
- **`Z.Pad`** — `Zui.cs:788`: `public static ZuiPad Pad(Vector2 value, Rect range, string tooltip, Action<Vector2> onChanged, ...)`.
- **`Z.Value2D`** — `Zui.cs:838`: `public static ZuiValue2DControl Value2D(string label, ZUIValue x, ZUIValue y, ...)` — the labelled ZUIValue-pair editor (each axis independently Static/MinMax/Curve via its own ⋯ config menu, per comment at line 812).
- **`Z.MicroMinMax` — DOES NOT EXIST under that name.** Grepped `Zui.cs` for `MicroMinMax`: zero matches. The actual min/max-range control is **`Z.MinMax`** (`Zui.cs:703`: `public static VisualElement MinMax(float low, float high, float min, float max, string tooltip, ...)` — a numeric low field + `MinMaxSlider` + numeric high field, kept in sync, line 699 comment). `Z.MicroSlider` (a single bounded scalar, `Zui.cs:349`) is a different, unrelated control. **Both `PROGRAMME_RULES.md` and `CHUNKS-DESIGN-DECISIONS.md` reference "Z.MicroMinMax" — this name is wrong in both docs; the correct call is `Z.MinMax`.** Flag this as a correction other task agents need before they write code against the wrong name.
- **`Z.Curve`** — `Zui.cs:628`: `public static CurveField Curve(AnimationCurve value, string tooltip, Action<AnimationCurve> onChanged, ...)` — wraps Unity's own curve editor (explicitly chosen over `ZUIEnvelope`'s custom multi-point editor because `ChunkSpec`'s size/alpha-over-life fields are real `AnimationCurve`s, comment lines 624-627 — this is the false-positive `ChunkSpec` mention noted in §4).
- **`Z.Gradient`** — THREE overloads in `Zui.cs`: `:639` (`Gradient value, ...`), `:653` (`Func<Gradient> get, Action<Gradient> set, ...` — a getter/setter pair rather than a value+callback, for a host that can't hold the gradient by value), `:666` (`ZuiGradient value, Action onChanged` — the RUNTIME `ZuiGradient` type from `Zui/Scripts/Runtime/ZuiGradient.cs`, a different serialized shape than a plain `UnityEngine.Gradient`).
- **`ZuiAudit`** — `Zui/Toolkit/ZuiAudit.cs`. `public static class ZuiAudit`, entry points `Audit(EditorWindow window)` (line 44) and an overload returning `out int foldedSkipped` (comment 46-50 warns a window sitting with collapsed sections can report a false-clean pass — the exact caveat the T-0123 blueprint's own "ZuiAudit clean" verification claim needs read alongside). NO menu item — call it from tooling/Coplay `execute_script`/tests (comment lines 1-4). Checks: `tooltip-missing`, `off-screen`, `stretch` (a `BaseField`-derived control resolving to `flex-grow > 0`), `over-width` (>600px, excluding containers/plots). Does NOT check row-packing waste, redundant titles, or explanatory labels — "stays a human's job" (line 15).

---

## §8. `ChunkSpec` assets in the project — the migration test set

All `.asset` files containing the string `ChunkSpec`, found via `grep -rl --include=*.asset`:

| Asset | Location | 2.0 modules enabled | Notes |
|---|---|---|---|
| **`Floating Disc Blowup.asset`** | `Demos/ChunksDemo/` | **`pyreSpawn` (label "Background", layer "Back Blast"), `fragmentSlicer` (layer "Fragments"), `blastGroups[0]` (label "Between", `useFormation=1`, Ring, 4pts), `blastGroups[1]` (label "In front", `useFormation=1`, Ring, 5pts), `layers` (4-slot: Back Blast/Between/Fragments/In Front), `timeline` (enabled, 4 tracks: Pyre Spawn@0, Fragments@0.08, Blast 2@0.26, Blast 3@0.46)** | **The ONLY asset in the project exercising the full composed-effect stack** — countMin/Max = 0 (no plain debris at all, per the "a pure-blast recipe emits no debris" design note in `ChunkSpec.cs:346-348`); is the flagship object W1.1's migration routine must round-trip losslessly. Wired into `ChunksDemoSpawner.composedSpec` (`ChunksDemo.unity:387`, guid `ee42fe208948c2a48a8e87646d878165`). |
| `SampledDebris.asset` | `Demos/ChunksDemo/` | none — all `enabled: 0` | Legacy plain debris; `sampleSource: {fileID: 0}` despite `tumble: 1` (tumble flag has no effect with no sample source set — a pre-existing, harmless authoring inconsistency, not a bug to fix here). Wired to `sampledSpec`. |
| `Sparks.asset` | `Demos/ChunksDemo/` | none | Legacy plain debris. Wired to `radialSpec`. |
| `WallDebris.asset` | `Demos/ChunksDemo/` | none | Legacy plain debris. Wired to `directionalSpec`. |
| `ArenaDebris.asset` | `Demos/ArenaDemo/` | none | Legacy plain debris, used by `ArenaEnemy`/`ArenaSpawner`. |
| `Debris.asset` | `Demos/ColosseumDemo/` | none | Legacy plain debris, used by `ShmupDirector`/`ShmupEnemy`. |
| `ProbeChunk.asset` | `Assets/_T0075_Probe/` | none | Leftover probe asset from an earlier task (T-0075); not referenced by any scene I found. Candidate for cleanup, not this task's to touch. |

`ChunksDemo.unity` (`Demos/ChunksDemo/`) wires all four `ChunksDemoSpawner` fields by GUID (`ChunksDemo.unity:378-387`) — confirmed the scene's spawner component IS `ChunksDemoSpawner` via `m_EditorClassIdentifier: com.Lautaro-Arino.Laubrary.Demos::ChunksDemoSpawner` (line 378).

---

## §9. Mock (`ChunksMockWindow.cs`) — `MockTimingTracks` + `MockCapabilityStage` behaviours

`Assets/ChunksMock/Editor/ChunksMockWindow.cs` (997 lines, untracked, disposable — never edit, never copy its controls verbatim per `PROGRAMME_RULES.md`). `public sealed class ChunksMockWindow : ZuiWindow` (line 19).

### `MockTimingTracks : VisualElement` (lines 524-794) — the multi-lane clock prototype
- Structure: a `bar` (painted lanes, `generateVisualContent += Paint`, 562-567) stacked over a `ruler` (tick numbers + playhead readout, `PickingMode.Ignore` — the ruler itself is not a hit target, 569-574).
- `Refresh()` (594-614): rebuilds `lanes` from `recipe.capabilities` filtering `cap.Timed`; **DISABLED lanes still count toward `total`** (604-606, comment: "or toggling one off rescales the ruler and every remaining band jumps sideways") — this is the exact behaviour `CHUNKS-DESIGN-DECISIONS.md §2` codifies ("max over ALL timed capabilities (enabled or not)").
- Gestures live on the WHOLE control (`this.RegisterCallback`, 587-590), not just the bar, so clicking the ruler also scrubs (comment 585-586: "a ruler you cannot click reads as broken"); `Scrub()` resolves position against `bar.WorldToLocal` specifically, never the raw event target (652-653 comment explains why — an absolutely-positioned band label has its own local space).
- `PlaceBandLabels()` (681-708): one `Label` per lane, skipped if the band is narrower than `MinBandForName=52f` (692) — the colour alone still identifies it.
- `BuildTicks()` (712-740) + `PlacePlayhead()` (752-758): **the playhead's own `PlacePlayhead()` calls `BuildTicks()` again every time it moves** (757) — so in THIS mock, unlike the current `ZuiTimeline.Assign()` (§7, which calls `PlacePlayhead()` but not `BuildTicks()`), tick occlusion is recomputed fresh on every scrub, not just relative to other ticks. A tick within ±22px of the playhead's CURRENT x (720-721, `playLeft`/`playRight`) is skipped outright at BUILD time (726) rather than hidden after the fact — a different, arguably simpler mechanism than production `ZuiTimeline`'s hide-after-placement approach, worth comparing when building the real multi-lane control.
- `Paint()` (760-781): for each lane, fills a `Track`-coloured background band the full bar width, then an over-paint of `cap.BandColor` sized to `[X(cap.delay), X(cap.delay+cap.duration)]`; **a disabled capability's band is drawn at 22% alpha** (775, `new Color(c.r,c.g,c.b,0.22f)`) rather than omitted — "keeps its lane, drawn dim" per the design.
- **There is NO wall-clock playback anywhere in the mock's timeline** — `seconds` only ever changes via `Scrub()` (a pointer drag/click), never advances on its own. `CHUNKS-DESIGN-DECISIONS.md §6` explicitly requires REAL wall-clock playback ("pressing Play advances the playhead at wall-clock speed... the clock loops when Loop is on") — this is new behaviour the mock does not demonstrate at all, not a lift.

### `MockCapabilityStage : VisualElement` (lines 799-995) — the spatial-guide prototype
- A raw `Painter2D` island (`generateVisualContent += Paint`, 815) — the sanctioned exception, drawn explicitly for "visualises placement, not a UI control ZUI provides" (comment 796-798).
- `Paint()` (818-841): fills background, draws a faint grid (`DrawGrid`, 930-933, 20px spacing), an origin cross (`Cross`, 827), then iterates `recipe.capabilities` and **draws ONLY enabled ones** (832) via a `switch (cap)` dispatching to one drawer per concrete mock capability type: `MockFormation → DrawFormation`, `MockPaletteSplash → DrawSplash`, `MockParticleBurst → DrawBurst`; `MockLayerPlan` has NO case at all — explicitly non-spatial (comment 838: "A Layer Plan is not spatial, so it deliberately draws nothing at all").
- `DrawFormation` (843-878): lays out Line or Ring positions locally (not calling any production `SpawnFormation.Resolve` — the mock cannot reference production Chunks types at all, per its own file-4 comment noted in §4's false-positive line); colours points on a `First`(blue)→`Last`(orange) gradient by stagger rank (873-875) and draws a small arrow between consecutive points (876) — the "blue is the first spawn and orange the last, and the small arrows show their order" behaviour named in the control's own tooltip (814).
- `DrawSplash` (880-891): swatches arranged in a circle of radius driven by a `spread` field, coloured by lerping the capability's `BandColor` toward white.
- `DrawBurst` (893-917): ray-fan drawing, three DISTINCT shapes (`Cone` = a directional fan, `Disc` = a full 360° fan, default/`Sphere` = three concentric fans of decreasing length+alpha to fake depth so a Sphere doesn't look identical to a Disc, comment 908-910).
- **Production implication**: the design's real Preview (§6 of `CHUNKS-DESIGN-DECISIONS.md`) needs the SAME per-capability-type dispatch shape (a discovery/interface mechanism, not a `switch` on concrete mock types, since real capabilities are `[SerializeReference]` polymorphic and the window can't enumerate every kind in one switch without violating the "preview overlays — an effect draws its own" rule in `CLAUDE.md`) — but the DRAWING PRIMITIVES here (rays for a burst/splash, coloured+arrowed points for a formation, a Layer Plan drawing nothing) are the right visual vocabulary to reuse, confirmed against the design doc's §6 list (origin cross, direction arrow+spread cone, formation points numbered in stagger order, discs for blasts, dots on arcs for debris) which reads as a superset of what the mock already proves out.

---

## Corrections for the programme (read before writing code)

1. **`Z.MicroMinMax` does not exist** — both `PROGRAMME_RULES.md` and `CHUNKS-DESIGN-DECISIONS.md` use this name; the real control is `Z.MinMax` (`Zui/Toolkit/Zui.cs:703`). (§7)
2. **`ChunkSpec` currently has NO `schemaVersion` field and no `capabilities` list** — the design doc's §2 describes them as already-decided shape, but nothing in current source has begun that migration; W1.1 is greenfield on the data model, not resuming partial work. (§1)
3. **`ZuiTimeline`'s occluded-tick bug (blueprint P7) appears already fixed in current source** (`ZuiTimeline.cs:340-353`) — the blueprint (written 2026-08-31) says it's unfixed and out of scope; verify with the PM whether this was patched since, or whether the blueprint's finding was about something else, before assuming it still needs fixing. (§7)
4. **`ChunkEmitter.ContainerLifetime` does not use `ChunkTimeline.Duration`** even though that property already computes exactly the "max over all tracks+markers" value the design's clock-length rule wants — it independently re-derives the same sum module-by-module (`ChunkEmitter.cs:135-179`). Any new timed capability needs a line added there, or the method rewritten to call `Duration`. (§2, §3)
5. **`PyreMotionModule` is not an `IChunkModule`** and is invoked from exactly one call site inside `PyreSpawnModule.SpawnOne` (`PyreSpawnModule.cs:214`) — the design's `Trajectory` modifier capability (§3) has no existing "reach into a sibling's just-spawned transform" plumbing to lift; this is new wiring, not a refactor of existing wiring. (§2)
6. **`Hits` and `Trail` capabilities have no existing module classes** — both live as inline `ChunkSpec` fields consumed directly in `Chunk.Init`/`Chunk.Update`. Extracting them into standalone capabilities is new code, not a lift. (§2)
7. **Only one asset in the whole project (`Floating Disc Blowup.asset`) exercises the composed 2.0 module stack** — every other `ChunkSpec` asset (5 demo assets + 1 probe) is legacy plain debris with every 2.0 module disabled. The migration routine's real test coverage is exactly one asset; everything else tests only "legacy fields stay untouched." (§8)
