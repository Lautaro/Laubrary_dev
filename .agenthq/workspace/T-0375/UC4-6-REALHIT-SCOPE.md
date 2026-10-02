# Scope: a real weapon-hit Play-mode rig for UC4–6 (T-0375)

Read-only research task. No code written, no editor/Play-mode session run. Every claim below is a `file:line`
citation against the live `dev` tree (`D:\UNITY\Laubrary Dev`).

## 1. The real pipeline, traced end to end

A real gameplay kill goes through six components in this order:

1. **Input → fire.** `ZoeWeaponDriver.Update` (`Assets/Packages/Laubrary/Runtime/ZoeCharacter/ZoeWeaponDriver.cs:93-102`)
   reads the input source, resolves the currently-equipped `ProjectileWeapon` (`CurrentWeapon`, lines 65-91), and
   calls `weapon.TryFire()` when the trigger condition is met (`WantsToFire`, lines 104-113).
2. **Weapon fires a projectile.** `ProjectileWeapon.TryFire(Vector2)` (`Assets/Packages/Laubrary/Runtime/Combat2D/ProjectileWeapon.cs:164-171`)
   resolves aim/position and calls `FireInternal` (lines 199-220), which pulls a `Projectile` from the pool
   (`SpawnProjectile`, lines 152-161) and calls `p.Launch(...)`.
3. **Projectile travels and hits.** `Projectile.OnTriggerEnter2D`/`OnTriggerStay2D`
   (`Assets/Packages/Laubrary/Runtime/Combat2D/Projectile.cs:98-104`) call `TryHit` (lines 106-123), which finds
   the hit's `Hurtbox` via `Combat.FindHurtbox` and calls `Combat.TryDamage`.
4. **The funnel.** `Combat.TryDamage` (`Assets/Packages/Laubrary/Runtime/Combat2D/Combat.cs:20-48`) checks
   ownership/faction/filters, builds a real `DamageInfo` with a genuine `point` and attacker→target `direction`
   (lines 42-45), and calls `target.ReceiveHit(info)`.
5. **The hurtbox scales and applies.** `Hurtbox.ReceiveHit` (`Assets/Packages/Laubrary/Runtime/Combat2D/Hurtbox.cs:24-30`)
   scales by its multiplier and calls `Health.ApplyDamage(info)`.
6. **Health fires the events.** `Health.ApplyDamage` (`Assets/Packages/Laubrary/Runtime/Combat2D/Health.cs:61-73`)
   always fires `Damaged?.Invoke(info)` first (line 69), then — only if the hit was lethal — fires
   `Died?.Invoke(info)` (line 72). This is unconditional: there is no path to `Died` that skips `Damaged`.

**`Health.Kill()` (`Health.cs:98-103`) is not a side door.** It clears invulnerability and calls
`ApplyDamage(new DamageInfo(current))` — i.e. it goes through the exact same `ApplyDamage` method as a real hit,
so it fires `Damaged` then `Died` in the identical order, on the identical frame. The only thing it does
differently from a real hit is the `DamageInfo` it constructs: `source`, `point` and `direction` are all left at
their default/zero values (`DamageInfo.cs:23-32`), because there is no real attacker or contact point to supply.

## 2. What listens to those events, and what it reads at that moment

`ReactionFxPlayer` (`Assets/Packages/Laubrary/Runtime/Zoetrope/ReactionFxPlayer.cs`) subscribes directly to
`Health.Damaged`/`Health.Died` — not to anything projectile- or weapon-specific:

- `OnEnable` (`ReactionFxPlayer.cs:136`): `_health.Damaged += OnHit; _health.Died += OnDeath;`
- `OnHit` (`ReactionFxPlayer.cs:176-203`) always runs first on any lethal hit (built-in Hurt reaction), because
  `Damaged` always fires before `Died` (see §1.6).
- `OnDeath` (`ReactionFxPlayer.cs:361-402`) then runs the Death reaction.

Both build an `EventContext` via `BuildContext` (`ReactionFxPlayer.cs:433-450`), which reads the Zoe's **live**
renderer synchronously at call time: `Renderer = renderers.Count > 0 ? renderers[0] : null` from
`ResolveBodyRenderers(transform, parts)` (lines 435-436, 454-473) — i.e. whatever `SpriteRenderer.sprite` the
character is actually showing on that exact frame. This is the mechanism T-0347/T-0348 called "the Zoe's live
frame at the moment of the hit/death." It is driven entirely by the `SpriteRenderer`'s current state, **not** by
anything in `DamageInfo`. `SpawnChunkFx.Apply` (`Assets/Packages/Laubrary/Runtime/ZoetropePyre/SpawnChunkFx.cs:57-76`)
reads that same `ctx.Renderer` through `ZoeLiveSampler.SampleColours`/`LiveSprite`
(`Assets/Packages/Laubrary/Runtime/ZoetropePyre/ZoeLiveSampler.cs:17,24-70`) to get the palette and the
`SampleSourceOverride` sprite the UC4/UC6 Palette Splash and UC5/UC6 Fragment Fracture cards read.

**Conclusion: the "Hit reaction fires before the Death reaction" and "the burst reads whatever frame is on screen
at that instant" behaviours T-0347/T-0348/T-0349 observed from `Health.Kill()` are not an artifact of using
`Kill()`.** They are exactly what a real weapon hit produces too, because both go through the identical
`ApplyDamage` → `Damaged`/`Died` → `ReactionFxPlayer.OnHit`/`OnDeath` → `BuildContext` chain. T-0348's "Not
verified: death without a preceding Hit reaction was never tested" describes something that, per this trace,
**cannot happen in this codebase at all** — `Health` has no path from full health to 0 that does not pass through
`Damaged` first (§1.6). This should be reported back as a resolved concern, not left open.

## 3. Where a real hit genuinely differs from the walks' `Kill()`

Not everything is identical. `DamageInfo.point`/`direction` do matter, downstream of `ReactionRequest.From`
(`Assets/Packages/Laubrary/Runtime/Zoetrope/ReactionRequest.cs:85-89`) and `EventContext.ResolveDirectionDeg`
(`Assets/Packages/Laubrary/Runtime/Zoetrope/EventContext.cs:253-270`):

- **Position.** `Kill()`'s `point` is zero → `ReactionRequest.Position` is `null` (`ReactionRequest.cs:86`) →
  `EventContext.HitPosition` falls back to the Zoe's own transform (`ReactionFxPlayer.cs:445`). A real hit
  carries the projectile's actual contact point. This affects any FX entry placed at `HitPosition`, not the
  Chunks burst's own spawn point — `SpawnChunkFx.Apply` bursts at `ctx.Position`, which for `Spawn Chunks` uses
  the entry's own placement (typically `TargetOrigin`/`TargetPosition`, i.e. the Zoe's anchor or sprite-bounds
  centre) — worth a quick check per-recipe but not expected to matter for UC4–6, which spawn "at the Zoe."
- **Direction.** `Kill()`'s `direction` is zero → `ReactionRequest.Direction` is zero → `Direction.sqrMagnitude`
  check fails in `ResolveDirectionDeg` (`EventContext.cs:266-269`) → `NaN` ("omni-directional"). Fed into
  `ChunksFx.Burst`/`ChunkEmitter.SpawnBurst`, a `NaN` `directionDegOverride` falls back to the recipe's own
  authored `spec.directionDeg` (`Assets/Packages/Laubrary/Runtime/Chunks/ChunkEmitter.cs:67`). A real hit's
  nonzero attacker→target direction would instead **drive** the burst's aim for any card with "inherit burst
  direction" on — UC5's Fragment Fracture explicitly has this on (`PaletteSplash`-style field, confirmed at
  `Assets/Packages/Laubrary/Runtime/Chunks/Capabilities/FragmentFracture.cs:159`, walk-observed as "Inherit burst
  direction: on (90)" in `T-0348/WALK.md`). **This is the one real, expected visual difference**: fired from a
  real weapon, UC5/UC6's fragments would fly outward from wherever ProtoGuy's shot actually came from, not from
  the fixed 90°/fallback direction the walks saw with `Kill()`. This is the correct/desired behaviour, not a bug
  to fix — it is in fact the whole point of testing with a real hit.
- **Damage amount / hurtbox multiplier.** Trivial — `FlyingDiscSpawner` already sets `overrideHealth` low enough
  (see §4) that ProtoGuy's real weapon kills a disc in a handful of shots.

Nothing else in the traced chain (Hit-before-Death ordering, live-sprite/palette sampling, `ReactionFxPlayer`
arming logic) depends on how `Health.ApplyDamage` was reached. **No pipeline or recipe change is required** for a
real hit to produce the same class of result the walks validated — a real hit is a strict superset (it adds a
correct spawn direction the walks couldn't exercise).

## 4. The demo that already runs this pipeline live: `ProtoGuyDemo`

`Assets/Demos/ProtoGuyDemo/ProtoGuyDemo.unity` already wires up the *exact* rig this task would otherwise have to
build from scratch:

- **`ProtoGuySpawner`** GameObject (scene line ~262) runs `ProtoGuySpawner.Start`
  (`Assets/Demos/ProtoGuyDemo/ProtoGuySpawner.cs:25-36`), which calls `ZoeSpawner.SpawnCharacter(zoeDef, ...)`
  with `zoeDef` = `Assets/Demos/ProtoGuyDemo/ProtoGuy FreeAim.asset` (scene YAML, guid
  `4b40ce07da0df0044a1bcb27c64f58e4`). That asset has two real weapon-loadout entries
  (`ProtoGuy FreeAim.asset:105-111`, `loadout` list with two `weapon:` refs), so the spawned ProtoGuy carries a
  real, player-input-driven `ProjectileWeapon` via `ZoeSpawner.EquipWeapon`
  (`Assets/Packages/Laubrary/Runtime/Zoetrope/ZoeSpawner.cs:183-225`, which is what `WeaponDef.cs`/
  `ZoeWeaponSlot.cs` feed — stats/ammo from `WeaponDef.cs:17-32`, per-character attach point/muzzle from
  `ZoeWeaponSlot.cs:17-36`).
- **`Disc Spawner`** GameObject (scene line ~133) runs `FlyingDiscSpawner`
  (`Assets/Demos/ProtoGuyDemo/FlyingDiscSpawner.cs`), whose `discDef` field in the scene is
  `Assets/Zoetrope/Floating Disc.asset` (guid `df6d774acf094b948b983102c9ef9a34` — **the owner's real asset**,
  the same one T-0347/348/349 duplicated for their walks). `FillSlot` (`FlyingDiscSpawner.cs:82-97`) spawns it
  via `ZoeSpawner.SpawnCharacter` (a real `Combatant`+`Health`+`Hurtbox`+`ReactionFxPlayer`, per
  `ZoeSpawner.cs:13-107`) and immediately overrides its health down to `overrideHealth: 24` (scene YAML,
  `FlyingDiscSpawner.cs:89-93`) — low enough that ProtoGuy's real weapon kills a disc in a handful of shots.
- `Assets/Demos/ChunksDemo/UseCases/UC5 Disc Fracture.asset:282` and `UC6 Disc Breakup.asset:342` already have
  their Fragment Fracture `sourceVisual` field pointing at `Floating Disc.asset` (the same guid) — i.e. UC5/UC6
  already reference the owner's real Floating Disc asset as their cut Source, not a copy.

**This is precisely the "reuse the existing pattern" rig the task brief asks for.** ProtoGuy already fires a real
weapon at a real, `Health`-bearing Floating Disc that dies from real damage, in a real saved scene, exercising the
full chain traced in §1. Nothing about the pipeline needs building — only the **wiring of UC4/5/6 onto the disc's
Death reaction row**, which is the same swap-in step T-0347/T-0348/T-0349 already did by hand on their own
duplicate asset (`UC Floating Disc (walk copy).asset`) via the Zoe editor's Death-row library picker (see each
WALK.md, "Wiring it to the Zoe").

## 5. What the build task (not this one) will actually need to do

1. **Do not repoint the owner's `Assets/Zoetrope/Floating Disc.asset` directly** — same rule the walks followed.
   Either:
   - (a) duplicate `Floating Disc.asset` again (or reuse `UC Floating Disc (walk copy).asset`, which the three
     walks already built and iterated Death rows on), and duplicate `ProtoGuyDemo.unity` into a scratch scene
     that points its `Disc Spawner.discDef` at the copy instead of the owner's asset; or
   - (b) build a small dedicated scene under `Assets/Demos/ChunksDemo/UseCases/` that copies just the two
     relevant GameObjects (`ProtoGuySpawner` + `Disc Spawner`) from `ProtoGuyDemo.unity`, again pointed at the
     Floating Disc copy.
   (a) is less work and proves the exact production wiring; (b) keeps Chunks-use-case testing self-contained
   away from the ProtoGuyDemo folder. Either is small; no new C# is required for the rig itself.
2. On the Floating Disc copy, repoint its Death reaction's library row at UC4, then UC5, then UC6 in turn (one
   asset, one row, three passes) — exactly the J5/K-route the walks already exercised, through the Zoe editor.
3. Run Play mode, actually fire ProtoGuy's weapon (human input, since `ZoeWeaponDriver` reads a real
   `IZoeInputSource` — see `ZoeWeaponDriver.cs:93-102`; there is no scripted-fire test hook in this rig the way
   `ChunkEmitter.Burst()` gave T-0374's UC1-3 rig a one-line trigger) until a disc dies, and observe/capture the
   burst.
4. Because `overrideHealth` is only 24 and `fireRate`/`damage` are real weapon stats, a disc should die in well
   under ten shots — this is a fast, repeatable test loop, not a rare event to wait out.

## 6. Size estimate

**Small.** No runtime or editor code changes are required — §3 establishes that the existing pipeline already
produces the same-or-better result for UC4–6 than the walks' `Kill()` calls did. The work is: duplicate one Zoe
asset (already exists as `UC Floating Disc (walk copy).asset`), duplicate or trim-copy one scene
(`ProtoGuyDemo.unity`), and three Death-row repoints + three short Play-mode passes with human-driven fire. Rough
order: under an hour of hands-on editor time per use case, once the rig scene exists (which is itself under 30
minutes — it's a scene duplication plus an asset-reference swap, not new construction).

## 7. Open questions for the owner

None of the pipeline tracing surfaced a genuine ambiguity that blocks scoping — §3's "burst direction now follows
the real hit" is a described consequence, not a decision point, and is the intended outcome of doing this at all.
One process question worth a explicit nod before the build task starts:

1. **Scene reuse shape (§5.1a vs 5.1b).** Duplicate `ProtoGuyDemo.unity` wholesale (fastest, proves the literal
   production wiring, but leaves a near-duplicate scene under `ProtoGuyDemo/`), or copy just the two relevant
   GameObjects into a new scene under `ChunksDemo/UseCases/` (keeps Chunks-use-case test scenes self-contained,
   marginally more setup)? Either is small and reversible; this is a filing preference, not a technical fork, so
   the build task can pick (a) by default unless the owner says otherwise.
