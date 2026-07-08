# Daemon / Loom / Story — integration report

*2026-07-08. How an enemy composes across Laubrary's tools, whether the systems are in shape to work together, and everything built this session.*

---

## Part 1 — How does an enemy work now? (the anatomy of an enemy)

An enemy is **one GameObject** with a stack of single-responsibility components from different tools. No tool does everything; each owns one layer, and they meet at small, explicit seams.

```
                        ┌─────────────────────────────────────────────┐
   DECIDES  ──────────► │  Daemon:  DaemonRunner + Brain (a Loom graph) │
                        │           steps the brain → flips Behaviours  │
                        └───────────────┬─────────────────────────────┘
                                        │ enable / disable / set-param / swap
                        ┌───────────────▼─────────────────────────────┐
   ACTS     ──────────► │  Daemon:  AgentBehaviours (Seek, Shoot, …)   │
                        │           the active ones .Tick() the body    │
                        └───────────────┬─────────────────────────────┘
                                        │ IAgentBody  (the one seam)
                        ┌───────────────▼─────────────────────────────┐
   BODY     ──────────► │  ColosseumAgentBody  (game adapter)          │
                        │   move transform · fire weapon · read HP     │
                        └───┬───────────────┬───────────────┬─────────┘
                            │               │               │
               ┌────────────▼──┐  ┌─────────▼────────┐  ┌───▼──────────────┐
   COMBAT ───► │  Colosseum    │  │  Zoetrope (Zoe)  │  │  Pyre + Chunks   │ ◄── VFX
               │ Health/Faction│  │ ZoePlayer.Play() │  │ blast + debris   │
               │ Hurtbox/Weapon│  │  idle/walk/atk…  │  │  on death        │
               └───────────────┘  └──────────────────┘  └──────────────────┘
```

### Who owns what

| Layer | Tool | Responsibility | Key API |
|---|---|---|---|
| **Brain (logic)** | **Daemon** (`Brain` = a Loom graph) | *Decides.* State nodes enable a behaviour; condition-gated edges transition; twist nodes tweak/swap behaviours. Never moves or fights. | `DaemonRunner.Boot(brain, set)`; `StateNode`, `ConditionBranch`, `BehaviourTwist`, `Fork`, `WaitEvent` |
| **Behaviours (skills)** | **Daemon** (`AgentBehaviour`) | *Acts.* Configurable data SOs with defaults + lifecycle; the active one drives the body each tick. Per-agent analogue of a `GameRule`. | `OnActivate/Tick/OnDeactivate`; the graph mutates them via `IBehaviourBridge` (Enable/Disable/SetParam/**Swap**/Inject) |
| **Body adapter** | **game** (`ColosseumAgentBody`) | The one seam. `IAgentBody` + `IAgentConditions`. Behaviours cast `ctx.Body` to it to move/fire; the brain's conditions call `Evaluate("inRange")`. | you write it — ~90 lines; example shipped in `Assets/Demos/DaemonDemo/` |
| **Combat/stats** | **Colosseum** | HP, factions, hurt/hitboxes, projectiles. **No movement, no targeting** — those are the game's. | `Health.Died` event, `ProjectileWeapon.TryFire(dir)`, `Combatant.faction` |
| **Visuals/anim** | **Zoetrope** (a Zoe) | Plays idle/walk/attack/hurt/death by name. | `ZoePlayer.Play("Attack", loop:false, onHit:DealDamage, hitFrame:3)` |
| **Death/hit VFX** | **Pyre + Chunks** | Explosion sprite + physical debris at the death point. | `BlastPlayer` component + `Chunks.Burst(pos, spec, tint)` |
| **Global mechanics** | **Rulesets** | Difficulty, spawn rates. Daemon *conditions read* rules; **Story** *mutates* them. | `RulesHost`, `GameRule` |
| **Scenario flow** | **Story** (a Loom graph) | Spawns waves, stages the fight, mutates rules. Sits *above* enemies. | `StoryRunner`, `Screenplay` |

### The data flow, per frame

1. `DaemonRunner.Update` → **step the Brain**: the current State node's `Tick` checks its transition conditions (via `IAgentConditions.Evaluate`, which reads the Colosseum body). If one fires, it `Exit`s (disables the old behaviour) and enters the next State (enables the new one).
2. `DaemonRunner` → **tick active behaviours**: `SeekBehaviour.Tick` moves the transform toward the target; `ShootBehaviour.Tick` calls `weapon.TryFire(dir)`. On activation, a behaviour also calls `body.SetStatus("Walk"|"Attack")` → the `onAnim` hook → `ZoePlayer.Play(...)`.
3. A hit lands (a projectile/hitbox → `Combat.TryDamage` → `Health`). When HP hits 0, **`Health.Died` fires** → the adapter's handler spawns a Pyre `BlastPlayer` + `Chunks.Burst`, sets status `"Death"` (→ Zoe death anim).

The brain **decides**, the behaviours **act through the body**, Colosseum **arbitrates combat**, and death **fans out to VFX via one event**. Each arrow is a typed seam, not a tangle.

---

## Part 2 — Are the systems in shape to work together? (readiness)

**Yes, with two known glue gaps.** Every tool is individually built; the composition is proven end-to-end for Daemon + Colosseum + Pyre/Chunks (verified headlessly this session), and the Zoetrope hook is a one-liner. The honest gaps:

| Seam | State | Note |
|---|---|---|
| Daemon → Behaviours → `IAgentBody` | ✅ Solid | Verified: Seek↔Shoot transitions + behaviour ticking drive the body. |
| `IAgentBody` → **Colosseum** | ✅ Solid | Adapter compiles against + drives the real Colosseum API. Death via `Health.Died`. |
| Death → **Pyre + Chunks** | ✅ Solid | Two direct calls; verified a blast spawns on death. |
| Behaviour → **Zoetrope** anim | 🟡 One-liner | `body.onAnim = clip => zoePlayer.Play(clip)`. Runtime playback is fully built; not wired in the headless demo only because it needs a Zoe sprite asset. |
| **Zoetrope pixel-perfect hits** | 🔴 Glue unwritten | Colosseum defines `IHitFilter.ConfirmHit`; Zoetrope has the pixel-overlap primitives (`PixelOverlaps`/`IsMetaPainted` on `ZonedAnimationPlayer`) — but **no component bridges them**. Today combat uses collider overlap (fine for most games). |
| **Movement / targeting** | 🟢 By design the game's | Colosseum provides neither on purpose. The adapter moves the transform (or a nav system) and picks targets. Not a gap — a boundary. |

**Composition layer (optional but recommended next):** Codex already has `CharacterDef`/`WeaponDef` + `CodexArsenal.SpawnCharacter`. An "EnemyDef" that bundles *{ ZoeVersion + CharacterDef + WeaponDef + Brain + BehaviourSet + deathBlast + debris }* would make an enemy type a single authored asset. The `DaemonEnemyDemo.Spawn` builder is the seed of this.

---

## Part 3 — What was built this session

All merged to **master** (tip `d79cf59`; not pushed).

| Commit | What |
|---|---|
| `1c78b42` | Pyre: MetaBlob gradient-flow + motion, Smudge stroke rebuild, `Box(null)` crash fix |
| `b258dec` | **Story** back-ported into the canonical Laubrary host (+ Notifyer parity) |
| `30bdc19` | **Loom** — the shared node-graph engine extracted; Story refactored onto it |
| `1b08f8f` | **Daemon** — per-agent AI: `AgentBehaviour` + `BehaviourSet`/`BehaviourHost` + `IBehaviourBridge` + `Brain` + node palette + `DaemonRunner` |
| `0b95aa5` | **Loom.Editor** — one shared GraphView window authors both Screenplay and Brain |
| `18f6706` | **Monolith** deprecated (migration note → Story/Daemon on Loom) |
| `d79cf59` | Daemon palette (Event/Fork/Blackboard/Status) + the **DaemonDemo** composed enemy |

**The architecture:** one engine, two graph types, two data-object hosts.

```
Laubrary.Loom          engine: Edge, Bookmark, GraphRunner<TNode,TCtx>, IGraphAsset/INode
Laubrary.Loom.Editor   one GraphView window for any IGraphAsset
├─ Laubrary.Story      Screenplay/Page  → drives Rulesets   (Daemon : Behaviours ∷ Story : Rulesets)
└─ Laubrary.Daemon     Brain/BrainNode  → drives Behaviours
```

## Part 4 — What's verified

- **Loom/Story** (headless): entry resolution, edge traversal, parking on a `WaitCondition`, resume, journal write, `Stop`. Story now runs on the shared engine with `StoryRunner` collapsed to a thin subclass.
- **Daemon** (headless): a Chase↔Attack state loop enabling/disabling behaviours that drive a mock body; conditions gate transitions; `SetParam` mutates a live field.
- **Composed enemy** (headless): a spawned Daemon+Colosseum enemy Seeks a far target, transitions to Shoot in range (status "Attack"), cycles back out of range, and `SpawnDeathVfx` fires a Pyre blast. Compiles against the real Colosseum/Pyre/Chunks APIs.
- **Editor** (compile + smoke): the shared window opens on both a Brain and a Screenplay and populates node views + in-node list editors without exceptions.

> **Needs a human eyeball:** the interactive GraphView UI (dragging nodes, wiring ports, in-node edits) — it's a direct port of Story's proven editor, but headless tests can't drive the mouse. Open **Laubrary ▸ Brain Graph** on a new Brain asset to confirm feel.

## Part 5 — Remaining work

1. **Zoe animation hookup** — wire `body.onAnim = clip => zoePlayer.Play(clip)` (+ set `ZoePlayer.version`). One line; needs a Zoe asset.
2. **Pixel-perfect hits** (optional) — write a `ZoeHitFilter : MonoBehaviour, IHitFilter` that calls `ZonedAnimationPlayer.IsMetaPainted/PixelOverlaps`.
3. **EnemyDef composition asset** — bundle Zoe + Colosseum + Weapon + Brain + BehaviourSet + VFX into one authored type (extend Codex).
4. **TrueEye port** — the deferred Phase 4. Recipe: implement `IAgentBody`/`IAgentConditions` over TrueEye's `Enemy`/`Combatant` (Step/Attack/ClosestFriendly/Agent/SetStatus), rewrite each `EnemyBehaviourRuntime` as an `AgentBehaviour`, author a Brain per enemy type, give each `Enemy` a `DaemonRunner`. Needs the updated Laubrary package pulled into TrueEye first.
5. **Palette depth** — sensor nodes, a per-agent event channel (vs. global Notifyer) for clean OnHit/OnDeath interrupts, a curve/timer node.

## Part 6 — How to try it

```csharp
using Laubrary.Demos.DaemonDemo;
// Spawn a full Daemon-driven Colosseum enemy that chases + shoots `target`:
var enemy = DaemonEnemyDemo.Spawn(pos, enemyFaction, target, attackRange: 3f);
// Author a brain visually: create a Brain asset (Create ▸ Laubrary ▸ Daemon ▸ Brain),
// hit "Open in Brain Graph", right-click to add State/Condition/Twist nodes, wire ports.
```

The default brain is Seek↔Shoot; swap in your own `Brain` + `BehaviourSet` for richer AI. To add the Zoe: attach a `ZoePlayer` (its `version` set) and set `body.onAnim = c => zoe.Play(c);`.
