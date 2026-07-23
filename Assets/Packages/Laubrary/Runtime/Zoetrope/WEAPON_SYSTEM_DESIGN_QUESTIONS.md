# Weapon system — dev goal

**Status: goal + scope decided; delivery-mechanism ownership is a working hypothesis; several items below
are still explicitly open.** History: this started as a raw needs list, got merged with OutBurner's own
unbuilt Combat System taxonomy (`DESIGN.md`/`GLOSSARY.md`: Hitscan/Projectile/Beam/Explosive), and firing
range was moved out of scope. This version supersedes the earlier open-questions-only pass.

## Goal

One consistent `WeaponDef`/`AmmoDef` pipeline that can express every weapon shape a Laubrary-based game
needs — hitscan, physics projectile (straight/lob/homing/Choreography-driven), beam, explosive/AoE, and
Zoe-as-projectile — without a game reinventing its own weapon system. This is the same problem OutBurner's
`ROADMAP.md` already names as a "make GENERIC" candidate ("Weapon/equipment as data... define once, consume
in many scene types"); solve it once here rather than per-game.

## Scope boundary (decided)

- **Aim resolution is OUT of scope.** Firing range/arc/360° is player-control (or AI/Daemon) code's job —
  it decides what direction is even legal to fire in before ever calling a weapon. Combat2D/Zoetrope only
  ever consumes an already-resolved direction or target point. This matches what already exists
  (`ProjectileWeapon.TryFire(dir)` / `TryFireAt(worldTarget)`) — no new arc/range concept needed on the
  weapon side, confirmed, not just assumed.
  - Auto-targeting-within-an-arc likely follows the same logic (it's "who do I aim at," one step upstream
    of "what direction do I fire," so it's plausibly the same caller's responsibility) — **not fully
    confirmed the same way firing range was**, listed again under Open Questions below.
- **Reload time + magazine capacity ARE in scope.** No real design question here: `WeaponDef` gains
  `magazineSize`/`reloadSeconds`, `ProjectileWeapon` gains `ammoInMag` + a reload timer gating `CanFire`.
  Build when convenient — not blocked on anything else in this doc.

## The delivery-mechanism taxonomy (merged, one list)

Previously treated as two overlapping lists (the raw brief's "firing types" + OutBurner's named Combat
System kinds); they're the same axis:

1. **Hitscan** — instant raycast, no travel time, no `Projectile` GameObject at all.
2. **Projectile** — a real moving `Combat2D.Projectile`. Trajectory SHAPE is a sub-choice within this
   mechanism, not a separate mechanism (see below).
3. **Beam** — a persistent ray/line dealing continuous per-tick damage to everything under it. Not a
   discrete moving object — doesn't fit `IProjectileMotion`'s shape (position-over-time for ONE object).
4. **Explosive / AoE** — on impact or expiry, damage everything in a radius, not just whatever was
   collided with. Structurally close to Hitscan (both are "resolve an overlap query, apply damage"),
   just a sphere query instead of a ray.
5. **Zoe** — firing spawns a full `Zoe` (its own `Health`/`Combatant`, animation, optionally a Daemon
   brain) as the "shot" — e.g. a homing missile that's a real combat participant, not ammo. Structurally
   the biggest departure: doesn't produce a `Combat2D.Projectile` at all.

**Target-only / depth-resolve (Cabal, Operation Wolf-style rail shooters) is NOT treated as its own
mechanism** — it's the existing `DepthMotion : IProjectileMotion` (lerp to a resolved world point), i.e. a
trajectory shape under mechanism #2, not a new mechanism.

### Trajectory shape (within Projectile only)

Already has a home: `AmmoDef.motion : IProjectileMotion` ([SerializeReference], the exact pluggable seam
this needs). `PlanarMotion` (straight, today's default) and `DepthMotion` (resolve-to-target, built for
depth shooters) exist now. Lob/arc and Choreography-driven paths are new implementations of the SAME
interface (`LobMotion`, `ChoreographyMotion : IProjectileMotion` wrapping a `Choreography` asset) — this
part needs no new architecture, just new implementations.

## Working hypothesis: where each concept is owned

- **`WeaponDef`**: identity, cadence (`fireRate`), magazine/reload, and the **delivery mechanism choice**
  (a weapon structurally IS a beam-gun or a missile-launcher — fixed identity, not something that swaps at
  runtime the way loaded ammo might).
- **`AmmoDef`**: trajectory shape (within Projectile mechanism only), visual, impact FX, damage
  amount/multiplier.
- **`Combat2D` core**: stays mechanism-agnostic. `Health`/`Faction`/`Hurtbox`/`Combat.TryDamage` is the
  SAME funnel for all five mechanisms — only "how did a hit get proposed" (ray / moving collider /
  continuous line / sphere query / a whole other Combatant) varies per mechanism. This is the load-bearing
  assumption of the whole design: if `Combat.TryDamage` can't stay this general, the hypothesis breaks.

## Open questions (genuinely undecided)

- **How does a `WeaponDef` express its delivery mechanism?** An enum with branching logic in
  `ProjectileWeapon`, or a pluggable `IWeaponDelivery` strategy (mirroring `IProjectileMotion`'s own
  pattern, one level up)? The latter is more consistent with how the rest of Zoetrope/Combat2D is built
  (pluggable `[SerializeReference]` everywhere) but is more upfront work.
- **Auto-targeting-within-an-arc**: fully game/Daemon-side like firing range, or does Laubrary still want
  to offer a reusable "find nearest hostile in arc" helper (used BY game/AI code, not owned by the weapon)?
- **Where does AoE damage application live?** A new radius-damage entry point on `Combat2D.Combat` itself
  (parallel to `TryDamage`), or a Zoetrope/bridge-level helper that calls `TryDamage` per-target after its
  own overlap query? Affects whether Explosive is "core" or "bridge."
- **Zoe-as-projectile composition**: does `ZoeSpawner` need a new spawn entry point (distinct from
  `SpawnCharacter`) for "a Zoe fired as a shot"? Does the spawned Zoe collide against the SAME
  `Hurtbox`/`Faction` pipeline as everything else (so it can itself be shot down — an intercepted missile),
  or does it need its own damage-eligibility rules?
- **Beam's damage cadence**: continuous per-`Tick`, or sampled at a fixed rate independent of frame rate?
  Affects balance and whether it reuses `Health.invulnerableAfterHit` sensibly (a beam ignoring i-frames
  entirely vs respecting them would feel very different).

## Candidate build order (once the open questions above are settled)

**Hitscan first** — simplest possible mechanism (a ray + `Combat.TryDamage`, zero new moving parts), and
proves whether "delivery mechanism as a pluggable concept" actually holds before investing in the harder
cases. Then **Explosive** (reuses Hitscan's "resolve a query → damage each hit" shape, just spherical).
Then **trajectory variants** on the existing `Projectile` mechanism (mostly free — `IProjectileMotion`
already exists, just needs `LobMotion`/`ChoreographyMotion`). Then **Beam** (genuinely new: a persistent,
ticking damage source). **Zoe-firing last** — the biggest structural lift, and benefits most from the other
four already having settled what "delivery mechanism" means as a concept.
