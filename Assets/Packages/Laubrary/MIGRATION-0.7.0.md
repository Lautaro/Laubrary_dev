# Migrating a consumer project to Laubrary 0.7.0

Two breaking renames landed in 0.7.0. Apply the **package update and the code changes together** (in one commit),
because a project's game code and its Laubrary copy must agree on the names.

## 1. Update your Laubrary copy to 0.7.0
Sync the Laubrary package into your project as you normally do. This is the version the renames are in.

## 2. Combat backbone: `Colosseum` → `Combat2D`  (any project using the combat tool)
The health/faction/damage/hitbox/projectile backbone was renamed (it has no authoring UI, so it gets a plain name).
**Only the namespace + asmdef name changed** — every type (Combatant, Health, Hitbox, Hurtbox, Faction, Projectile,
ProjectileWeapon, DamageInfo, IHitFilter, Combat, …) is identical. Scenes/prefabs are unaffected (script GUIDs are
unchanged).

In YOUR project, one find/replace does it:

    Laubrary.Colosseum   →   Laubrary.Combat2D

That covers `using Laubrary.Colosseum;`, fully-qualified `Laubrary.Colosseum.X`, and the asmdef reference string
`com.Lautaro-Arino.Laubrary.Colosseum` (it contains `Laubrary.Colosseum`).

## 3. Enemy recipes: `Codex` → `Bestiarium`  (only if you used Codex)
Renamed (it now has a browser UI) **and** decoupled so its core needs only Combat2D. Changes:

- Namespace `Laubrary.Codex` → `Laubrary.Bestiarium`; asmdef `…Codex` → `…Bestiarium`. `CodexArsenal` → `Bestiary`.
- **Effects are pluggable.** The old `CombatVfx` (Pyre + Chunks) is gone from core; it's now `PyreChunksFx` in the
  OPTIONAL module `com.Lautaro-Arino.Laubrary.Bestiarium.Pyre` (namespace `Laubrary.BestiariumPyre`). Add that asmdef
  reference where you build Defs, then:

      def.hit.blast = b;            →   def.hit    = new PyreChunksFx { blast = b };
      def.death.blast/.chunks = …   →   def.death  = new PyreChunksFx { blast = …, chunks = … };
      weaponDef.muzzle.blast = …    →   weaponDef.muzzle = new PyreChunksFx { blast = … };
      proj.impact.blast/.chunks = … →   proj.impact = new PyreChunksFx { blast = …, chunks = … };

- **Look is pluggable.**

      def.idleSprite = s; def.spriteScale = k;   →   def.view = new SpriteView { sprite = s, scale = k };

  Animated look: `new ZoeView { … }` from `com.Lautaro-Arino.Laubrary.Bestiarium.Zoetrope`. Vector-art look: a Lazor
  view, once the Lazor tool ships.
- `ProjectileDef.trail` (a Chunks field) was removed — it was unused.
- **Optional AI on the recipe:** `def.brain = new DaemonBrainSpec { brain = …, behaviours = … }` from
  `com.Lautaro-Arino.Laubrary.Bestiarium.Daemon` (the game still supplies the `IAgentBody`).

## Per-project status (as of 0.7.0)
- **Asteroids+** — uses the combat backbone → apply **§2** (one find/replace) alongside its next Laubrary sync.
  Does not use Codex, so **§3 is not needed**.
- **TrueEye** — no game-code use of the combat backbone and no Codex → **no changes needed** for these renames.
