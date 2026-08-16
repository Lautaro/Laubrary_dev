# Player-character-module architecture — design

> Independent design pass for a generic, reusable player-character module in Laubrary.
> Authored without touching any implementation files. Reconcile with the parallel
> research agents' findings on the current Launimator/Zoetrope/input code before
> locking the API surface.

The design reuses the vocabulary the project's own design docs already committed to
(`ZOE_MOVEMENT_DESIGN.md`, `ZOE_EVENTS_DESIGN.md`, `WEAPON_SYSTEM_DESIGN_QUESTIONS.md`)
and the existing runtime types in `Assets/Packages/Laubrary/Runtime/Zoetrope/` —
`Zoe`, `Locomotion`, `LocomotionAnimator`, `ZoeEvent`, `WeaponDef`, `IActivatable`,
`IAnimatedView`, `LoadoutController` — so the new module slots in rather than
bypassing them.

---

## 1. Vocabulary

A "character" is the composed, runtime-presented entity. Three layered concepts:

### Existing — DO NOT REDEFINE

| Term | What it is | Where it lives today |
|---|---|---|
| **Zoe** | The top-level composed entity asset (body + weapons + FX + behaviours). Authored once, instanced many. | `Runtime/Zoetrope/Zoe.cs` |
| **Laumination** | A *single named animation clip* (or sprite frame) on a `LauminaryVersion`. Replaces the older "AnimationDef". | `Runtime/Launimator/`, `ZOE_MOVEMENT_DESIGN.md` |
| **Lauminary / Reel** | A named, versioned bundle of `Laumination`s for one sprite part. | `Runtime/Launimator/` |
| **ZoeEvent** | A first-class uniform event entry on a Zoe (hit, death, dash, knockback). Owns its laumination, duration, consequences, FX, cue triggers. | `Runtime/Zoetrope/ZoeEvent.cs` |
| **Locomotion** + **LocomotionAnimator** | The existing steady-state movement clip picker on a Zoe. Today: 2 clips, `bool moving`, no direction. The migration target. | `Runtime/Zoetrope/Locomotion.cs` |
| **WeaponDef** | The asset describing a weapon (cadence, delivery mechanism, projectile/beam/etc). Aim resolution is OUT of scope — game code. | `Runtime/Zoetrope/WeaponDef.cs` |
| **LoadoutController / WeaponSwitcher / IActivatable** | Already-built runtime wiring for "which weapon is active, fire it". | `Runtime/Zoetrope/` |
| **IAnimatedView** | Interface a part's SpriteRenderer view implements so FX/cues can poke it. | `Runtime/Zoetrope/IAnimatedView.cs` |
| **Combat2D** | The plain-named 2D-combat backbone namespace. No cool name because no authoring window. | `Runtime/Combat2D/` |

### Introduced by this design

| Term | What it is | Why |
|---|---|---|
| **CharacterModule** | A `MonoBehaviour` attached to a Zoe's instance root. Wires motion → laumination and owns the per-frame resolver tick. The "one arbiter owns the body" piece ZOE_MOVEMENT_DESIGN already calls out as missing. | Today, locomotion/events/weapons each grab the body independently. The module is the single arbiter they submit to. |
| **MotionState** | A small per-frame struct published by the *input/AI source* (player controller, enemy brain, choreography). Contains: `speed`, `heading` (world-relative radians), `aim` (radians, may be `null` if not aiming), `grounded`, `underPower` (acceleration-ish), `selfWilled`. Pure data; no behaviour. | The project's own design already calls for a "publishable MotionState (speed, heading, facing/aim, grounded, self-willed vs pushed)". This is that struct. |
| **Channel** | An enum: `None`, `Heading`, `Aim`. Each `BodyPartSlot` is wired to one channel — its laumination is indexed by that channel's value (or `None` if the part never rotates). Today the ZOE_MOVEMENT_DESIGN already uses the term with exactly this meaning ("`Legs` Channel=Heading", "`Torso` Channel=Aim"). | Reusing the term keeps the doc prose aligned with the prior design. |
| **DirectionMode** | How a part's direction channel resolves to a member of its current set. Enum: `None`, `Members(N)`, `Rotate`, `FlipHorizontal`. Today the design says "`Members(16), mirrored`" / "`Members(4), mirrored`" / "`Rotate`". | Lets the resolver pick "this is a discrete-frame sheet" vs "this is a single sprite that rotates with the body". |
| **DirectionBin** | A function `Direction → int binIndex` plus a flag `mirrored`. Concrete implementations: `Bin16` (22.5° steps, mirrored → 9 authored members), `Bin8` (45° steps), `Bin4` (90° steps), `Bin2` (just left/right). | Lets the artist pick resolution without changing the data layout. "Mirroring inside" (the project's stated goal) lives here. |
| **MotionSet** | A named group of lauminations on a `LauminaryVersion` plus a `DirectionMode`. Example: `Idle` (Members(16)), `Run` (Members(4), mirrored), `Static` (single member, None). | The "first-class laumination SET" ZOE_EVENTS_DESIGN already calls out: "alongside `animations`: a name plus members tagged by direction, with mirroring inside". This is the C# type. |
| **MotionRule** | One ordered entry in a `MotionSet`'s rule list: `condition` (predicate over `MotionState`) → `setName` (which `MotionSet` to play). First match wins; an unconditional last rule is the fallback. | Same "first-match-wins" idiom as `ZoeEvent.EventFor` — the project already chose this shape. One pattern in the library, not two. |
| **MotionPose** | An ordered list of `MotionRule`s plus the parts they apply to. Owned by the CharacterModule, NOT by the Zoe. | "Where a part list is declared — on the composite view, or on the Zoe? The view knows the parts; the Zoe knows the poses." This is the design's choice: Zoe owns the pose, the view (SpriteView in the part prefab) declares the parts by name. |
| **BodyPartSlot** | A *named attachment point* on the character's prefab (a Transform child + the `IAnimatedView` SpriteRenderer that draws there). Named by a string — never typed (per the project rule "a name is typed once where it's declared; every place that REFERENCES it is a picker"). | A character with upper body + legs has two slots. A flying enemy with one sprite has one slot. A character with hat + hair + torso + legs has four. |
| **BodyPartView** | The MonoBehaviour on a slot's child Transform. Holds the slot's name, the SpriteRenderer / `IAnimatedView`, an optional local pivot offset for depth-stacking. Knows nothing about lauminations — only "I am the slot named X". | Per "where a part list is declared" (ZOE_MOVEMENT_DESIGN § 1.4): the view declares, the Zoe references by name. |
| **PlayerIntent / AgentIntent** | A tiny MonoBehaviour on the instance root that *publishes* `MotionState` and triggers events. Player has a `PlayerIntent` that reads input; an NPC has an `EnemyIntent` driven by AI; a turret-on-rails has a `ScriptedIntent` driven by a choreography. | Intent is OUTSIDE Laubrary per ZOE_MOVEMENT_DESIGN § "Who should own it": "INTENT — what is trying to move this thing, and where to? — The GAME (input, AI, physics, a choreography) — never Laubrary". So the type lives in `Runtime/Combat2D/Player/` (game-facing) but the *interface* it publishes IS the library's vocabulary. |
| **LocomotionMode** | The high-level state name on the Zoe's pose: `Idle`, `Walk`, `Run`, `Airborne`, `Dead`. NOT a state machine — just labels a `MotionRule`'s `condition` may match on. | Lets `MotionRule.condition` say `state == Walk` without inventing a new flag each time. |
| **AimSource** | The single component on the instance root that owns the aim angle. The Player's is fed by mouse/right-stick; an NPC's by `IAimBrain`; a fixed turret's by a configured angle. The `MotionState.aim` is just `AimSource.AngleRadians` cached each tick. | Without a single owner, two writers fight over the angle. This is the same lesson as "one arbiter owns the body". |
| **FiringSource** | The single component that fires weapons. Receives a `FireCommand` (direction, weapon slot). The character module doesn't fire directly — events (`ShootFiredEvent`) bubble up to whatever the game wires them to. | Mirrors `AimSource`: one owner, multiple inputs. Lets you swap input sources without touching the body. |

### Names this design does NOT introduce

- No new "cool" tool name. This is infrastructure, not an authoring window. The
  parent module is **Combat2D** (plain name, per project rule). The module
  itself is `Combat2D.Character.CharacterModule` (or shorter — see § 6).

---

## 2. Top-down vs side-scroller — reuse question

**Argue both sides briefly:**

- **One module, parameterised.** A character is a character: pose slots + rules +
  sets. Top-down uses 16-bin Aim; side-scroller uses 2-bin FlipHorizontal (or
  4-bin Heading for stairs/crouching). Both have a "still / moving" gait
  decision. Both raise the same `ZoeEvent`s for hit/death/dash. The character
  is the same; only the `DirectionMode` and which `Channel`s are wired change.
  Sharing code maximises parity (a dash feels identical regardless of view).
- **Two modules.** Top-down and side-scroller have *different needs*:
  top-down needs an aim channel AND a heading channel per part (the decoupling
  case the design calls out as "two parts, two direction modes, no shared
  assumption"); side-scroller usually has only one (heading drives the whole
  sprite, mirror = facing). Top-down needs depth sort and shadow offset
  (`MotionState.depth`); side-scroller needs Y-sort within a platform. If you
  share too much, you pay for "could be either" everywhere — the worst of both.

**Pick: one module, parameterised.**

Reason: the *data* differs (DirectionMode, which Channel is wired, whether
`MotionState.aim` is even populated), but the *resolver* — given a
`MotionState`, pick a `MotionSet` per slot — is identical. The cost of two
modules is two code paths to keep in sync and two authoring UIs; the cost of
one module is a few fields that go unused in each configuration. The library
already chose this path for the locomotion decoupling case ("two parts, two
direction modes, two channels, no shared assumption") — extending the same
shape to view-mode is the natural next step.

**What drives the difference at runtime, in data:**

| Knob | Top-down | Side-scroller |
|---|---|---|
| `LocomotionMode` enum members used | `Idle`, `Walk`, `Run` (+ `Airborne` if jumping) | same |
| `DirectionMode` of the *body* slot | `Members(16)` (or `Members(8)`) + `Bin16` mirrored | `Members(2)` + `FlipHorizontal` mirrored |
| `MotionState.aim` | populated by `AimSource` | `null` |
| Per-slot `Channel` wiring | `Torso → Aim`, `Legs → Heading` | `Body → Heading`, no separate upper/lower |
| Depth handling | `MotionState.depth` drives Y-sort + scale | Y-sort within platform only |

A single `CharacterModule` reads these knobs; both views are the same code.

---

## 3. The "upper body 16 + legs 3-walk + 16-idle" pattern — generic data model

The current Zoe locomotion is "two clips, no direction". This design replaces
it with the generic shape ZOE_MOVEMENT_DESIGN already laid out, and then
parameterises it for any character.

### The types

```csharp
namespace Combat2D.Character
{
    /// One published state per tick. Pure data. Written by an Intent, read by the resolver.
    public struct MotionState
    {
        public float      speed;        // metres/sec
        public float      heading;      // radians, 0 = +X, CCW positive
        public float?     aim;          // radians if aiming, null if not
        public float      depth;        // pseudo-3D ordering hint (0..1)
        public bool       grounded;
        public bool       underPower;   // accelerating or actively driven
        public bool       selfWilled;   // false if pushed (knockback, cutscene)
        public LocomotionMode mode;     // Idle | Walk | Run | Airborne | Dead
    }

    public enum LocomotionMode { Idle, Walk, Run, Airborne, Dead }

    /// A function from a MotionState (or a single Channel value) to a bin index.
    public interface IDirectionBin
    {
        int  Bin(float angleRadians);     // angle in radians → index into MotionSet members
        int  MemberCount { get; }         // the N the artist authored (after mirroring, max is this)
        bool UsesMirroring { get; }
    }

    public sealed class Bin16Mirrored : IDirectionBin { /* 22.5°, N=9 authored, 16 logical */ }
    public sealed class Bin8Mirrored  : IDirectionBin { /* 45°,   N=5 authored, 8  logical */ }
    public sealed class Bin4Mirrored  : IDirectionBin { /* 90°,   N=3 authored, 4  logical */ }
    public sealed class Bin2Mirrored  : IDirectionBin { /* 180°,  N=2 authored, 2  logical */ }

    /// One channel's direction value for this tick.
    public enum DirectionChannel { None, Heading, Aim }

    /// Picks the Channel value out of MotionState for a given slot.
    public interface IDirectionProvider
    {
        DirectionChannel Channel { get; }
        float? Current(MotionState s);     // null if not aiming AND channel=Aim → use None
    }

    /// A named group of lauminations on a LauminaryVersion + a DirectionMode.
    /// This is the "first-class laumination SET" the project's event design called for.
    [Serializable]
    public sealed class MotionSet
    {
        public string           name;          // "Idle", "Run", "AimUp", "Static"
        public LauminaryVersion lauminary;     // picked by asset reference (never typed)
        public int              memberCount;   // how many frames the artist authored
        public DirectionMode    direction;     // None | Members(N) | Rotate | FlipHorizontal
        public bool             mirrored;      // whether Bin16/Bin8/Bin4 should mirror
    }

    public enum DirectionMode { None, Members, Rotate, FlipHorizontal }

    /// One ordered entry: condition → set name. First match wins. Unconditional last is the fallback.
    /// Same idiom as ZoeEvent.EventFor — one pattern in the library, not two.
    [Serializable]
    public sealed class MotionRule
    {
        public MotionCondition  condition;     // serialised predicate; simple enum for v1
        public LocomotionMode   mode;          // optional shorthand: rule matches when state.mode == this
        public bool             requireMoving; // convenience: rule matches when speed > 0
        public bool             requireAiming; // rule matches when state.aim.HasValue
        public bool             requireGrounded;
        public string           setName;       // references a MotionSet on the owning MotionPose
    }

    /// Per-slot wiring. One BodyPartSlot per visible part of the character.
    /// Channel = which direction value drives it. MotionSetLookup = rules for THIS slot.
    [Serializable]
    public sealed class BodyPartSlot
    {
        public string                 slotName;   // "Torso", "Legs" — picked by name (never typed)
        public DirectionChannel       channel;    // None | Heading | Aim
        public List<MotionRule>       rules;      // ordered; last must be unconditional
        public List<MotionSet>        sets;       // named sets the rules refer to
    }

    /// A full pose for one Zoe: one BodyPartSlot per visible part.
    [Serializable]
    public sealed class MotionPose
    {
        public string              poseName;     // "TopDownWalk", "SideScrollerIdle", etc.
        public List<BodyPartSlot>  slots;        // one entry per part on this character
    }

    /// One body part's per-frame resolved pick.
    public readonly struct ResolvedPart
    {
        public readonly BodyPartSlot slot;
        public readonly MotionSet    set;
        public readonly int          memberIndex;  // -1 if not yet resolved
        public readonly float        playRate;     // 1.0 = authored speed, scaled by speed if applicable
    }

    /// Owns the per-frame resolve. Pure function: (MotionState, MotionPose) → per-slot pick.
    public static class MotionResolver
    {
        public static IReadOnlyList<ResolvedPart> Resolve(
            MotionState            state,
            MotionPose             pose,
            IDictionary<string,IDirectionBin> binsPerSlot) { /* first-match-wins */ }
    }
}
```

### How the example character uses it

The player character — top-down, upper-body 16-bin aim + legs 3-anim walk + legs 16-idle:

```csharp
// On the Zoe asset:
var playerPose = new MotionPose {
    poseName = "TopDownWalk",
    slots = {
        // Upper body — 16 single-frame members, indexed by aim
        new BodyPartSlot {
            slotName = "Torso",
            channel  = DirectionChannel.Aim,
            sets = {
                new MotionSet { name="AimStatic",  lauminary=aim16, memberCount=16,
                                direction=DirectionMode.Members, mirrored=true },
            },
            rules = {
                new MotionRule { requireAiming = true, setName = "AimStatic" },
                new MotionRule {                          setName = "AimStatic" }, // unconditional fallback
            },
        },
        // Legs — Heading picks between idle-16 (still) and walk-3 (moving)
        new BodyPartSlot {
            slotName = "Legs",
            channel  = DirectionChannel.Heading,
            sets = {
                new MotionSet { name="LegIdle", lauminary=legsIdle, memberCount=16,
                                direction=DirectionMode.Members, mirrored=true },
                new MotionSet { name="LegWalk", lauminary=legsWalk, memberCount=3,
                                direction=DirectionMode.Members, mirrored=true }, // up/down/side
            },
            rules = {
                new MotionRule { requireMoving = true, setName = "LegWalk" },
                new MotionRule {                            setName = "LegIdle" },
            },
        },
    },
};
```

The resolver sees `MotionState { speed=3, heading=π/4, aim=π/2, mode=Walk, grounded=true }` and returns:

- **Torso**: rule matched `requireAiming` → set `AimStatic` → bin = `Bin16(aim=π/2)` → member index for East.
- **Legs**: rule matched `requireMoving` → set `LegWalk` → bin = `Bin4(heading=π/4)` → "Side" member index.

Same data shape expresses the tank (`Hull`/`Turret` from the design's own example), the floating disc (`(single body) — None — one looping hover laumination`), the flying enemy with 8-bin single sprite (`DirectionMode.Members(8)` only, `DirectionChannel.Heading`), and a future side-scroller hero (`FlipHorizontal`, single `BodyPartSlot`).

### Key properties this buys

- **Pure-function resolver.** No state machine, no transitions to author per character. The "first match wins" idiom matches `ZoeEvent.EventFor`.
- **Mirroring is a flag, not duplicated art.** 16 directions from 9 authored members — already the project's stated goal, encoded once in `IDirectionBin`.
- **Same data drives top-down and side-scroller.** The DirectionMode / Channel / MotionSet knobs are the only differences (§ 2).
- **Graceful degradation.** A single-part pose with one unconditional rule and `DirectionMode.None` is exactly today's `Locomotion` — every existing Zoe keeps working with no authoring change. Already a goal in the design.

---

## 4. Aim + shoot plumbing

The model's claim: **aim and shoot are not the character's job; they are inputs to it.** The character module reacts to a published `MotionState` (which includes aim) and a `ZoeEvent`-like `FiredEvent`. It does not own the mouse, the AI, or the trigger.

### The flow

```
   ┌──────────────┐                                 ┌────────────────────┐
   │ Input / AI   │ -- MotionState published -->   │                    │
   │ (PlayerIntent│                                 │  CharacterModule   │
   │  / EnemyAI / │ -- FireCommand  pressed  -->   │   (the arbiter)    │
   │  Scripted)   │                                 │                    │
   └──────────────┘                                 │  ├ MotionResolver  │
            │                                       │  │  (per slot)     │
            │                                       │  ├ AimSource       │
            │                                       │  │  (current angle)│
            │                                       │  └ FiringSource    │
            │                                       │     (active weapon)│
            │                                       └─────────┬──────────┘
            │                                                 │
            │       published back as events                   │
            │                                                 ▼
            └───────────────── <-- ZoeEvent('ShootFired', 'Damaged', 'Death') ←────────┘
                                                  (game code subscribes; FX/cues/consequences)
```

### Where each thing lives

| Concern | Owner | Lives in | Why |
|---|---|---|---|
| Mouse / gamepad read | `PlayerInput` (existing or new) | `Runtime/Combat2D/Player/` (game-facing, NOT in Laubrary core) | ZOE_MOVEMENT_DESIGN: INTENT is the game's job, never Laubrary. |
| Aim angle, current | `AimSource` | `Runtime/Combat2D/Character/AimSource.cs` | The single component that owns the angle. Reads `PlayerInput` for player; reads `IAimBrain` for NPC; reads a configured value for turret. Writes it into `MotionState.aim`. |
| Trigger pressed | `FiringSource` | `Runtime/Combat2D/Character/FiringSource.cs` | Single owner of "which weapon slot is active, did the trigger go this frame". Reads input, talks to `LoadoutController`/`WeaponSwitcher` (already exist) to fire the current weapon. |
| Active weapon | `LoadoutController` / `WeaponSwitcher` (existing) | `Runtime/Zoetrope/` | Already built. `FiringSource` calls it. |
| Per-tick laumination pick | `MotionResolver` | `Runtime/Combat2D/Character/MotionResolver.cs` | Pure function. Reads `MotionState`, picks per slot. Already designed in § 3. |
| Body assertion | `CharacterModule` | `Runtime/Combat2D/Character/CharacterModule.cs` | "One arbiter owns the body" — locomotion, events, FX submission all flow through this. |
| FX, cues, consequences of shooting | `ZoeEvent("ShootFired")` | `Runtime/Zoetrope/ZoeEvent.cs` | Already designed — a `ZoeEvent` with `PlayLauminationEffect` + a `CueBinding` for the muzzle flash + a `ZoundEngine.PlayZound` by name. Nothing new. |

### What the CharacterModule does NOT do

- It does not read input.
- It does not decide when to fire.
- It does not know the difference between a player and an NPC.
- It does not know there is a mouse.

It knows: "every tick, given a MotionState, resolve each slot's laumination; if a `FiredEvent` was raised, forward it as a `ZoeEvent` (so existing FX/cue/il-lumination-of-the-muzzle all just work)."

### What the Player does

```csharp
// On the player instance root:
[SerializeField] PlayerInput   input;     // the game's input source
[SerializeField] AimSource     aim;       // reads input.look, owns the angle
[SerializeField] FiringSource  fire;      // reads input.fire, owns active weapon
[SerializeField] CharacterModule character; // the arbiter

void Update() {
    var s = new MotionState {
        speed      = rb.velocity.magnitude,
        heading    = Mathf.Atan2(rb.velocity.y, rb.velocity.x),
        aim        = aim.CurrentAngle,
        grounded   = groundSensor.IsGrounded,
        underPower = input.move.sqrMagnitude > 0.01f,
        selfWilled = true,
        mode       = rb.velocity.magnitude < 0.1f ? LocomotionMode.Idle : LocomotionMode.Walk,
        depth      = 0.5f,
    };
    character.PublishMotionState(s);        // resolver runs, picks lauminations
    if (input.fire.IsPressed) fire.TryFire(); // FiringSource → LoadoutController → existing weapon code
}
```

### What an NPC does

```csharp
// On an enemy instance root — DIFFERENT intent source, SAME arbiter:
[SerializeField] EnemyBrain     brain;     // AI — replaces PlayerInput
[SerializeField] AimSource      aim;       // same component, fed by brain.aimTarget
[SerializeField] FiringSource   fire;      // same component
[SerializeField] CharacterModule character; // SAME component
// No CharacterModule code changed.
```

This is the "adding a new agent type is ≤ 3 files" promise — the arbiter is shared.

---

## 5. Extension story — adding a new agent type

> "I want a new flying enemy: no legs, single sprite, 8 directional frames, can hover and shoot."

### Files touched (target: 3)

1. **One data asset** — a `Zoe` asset (`EnemyZoe.asset`). Authored in the Zoetrope
   editor window: pick `Body` lauminary (the 8-frame sprite), add `MotionPose`
   with one `BodyPartSlot` (channel = `Heading`, `DirectionMode.Members(8)`,
   `Bin8Mirrored`), one `MotionSet` ("Hover8"), one `MotionRule` (unconditional
   fallback), one `WeaponDef` slot. The user opens the existing Zoe editor — no
   new editor window needed (per project rule: "Do NOT add editor menu items
   that weren't explicitly requested"; a flying enemy uses existing UI).
2. **One prefab** — a `EnemyFlying.prefab` GameObject with:
   - `Rigidbody2D`, `Collider2D`
   - `BodyPartView` (single child) with `slotName = "Body"`
   - `EnemyBrain` (game-side `MonoBehaviour` — game's code, not Laubrary)
   - `AimSource`, `FiringSource`, `CharacterModule` (all from Laubrary)
3. **One scene placement** — drop the prefab in a test scene. Done.

### Files NOT touched

- No `CharacterModule.cs` edits.
- No `MotionResolver.cs` edits.
- No `Zoe.cs` edits (the asset is a *content* edit, not a code edit).
- No new editor window.
- No new namespace under `Laubrary.*`.

### Why this stays ≤ 3 files

- The character module is generic — it doesn't know about flying, walking, or
  being a turret. The *data* on the Zoe asset encodes the difference.
- The arbiter (`CharacterModule`) is the same component on the player, the
  NPC, the flying enemy, the boss. The intent source is the only thing that
  varies, and that's a game-side script (`PlayerIntent`, `EnemyBrain`,
  `ScriptedIntent`) — each game picks its own.
- `BodyPartView` declares the slots a character has; a flying enemy has one,
  the player has two, a boss might have four. The character module iterates
  whatever slots the Zoe declares. No "isFlying" flag.

### The "wait for 3 call sites" test

Three characters using this module prove the design: the player (this design's
target), the existing tank from the ZOE_MOVEMENT_DESIGN examples, and one
new NPC. If a fourth needs a code change, that's a smell — push it back into
the data shape.

---

## 6. Generic vs specific — where the line sits

The project rule (CLAUDE.md → "Naming: cool names are earned by a UI"):
infrastructure has plain names; visual authoring tools get cool names. The
Character Module is infrastructure — it does NOT have its own authoring window
in this design. Its authoring happens through:

- The **Zoetrope window** (existing) — for `Zoe`, `BodyPartView` lists, `MotionPose`, `MotionSet`, `MotionRule`. The ZOE_MOVEMENT_DESIGN's "Where a part list is declared" answer ("the view knows the parts; the Zoe knows the poses") maps to: the **Zoetrope window** edits the Zoe (poses/sets/rules); the **Launimator** side edits the lauminations themselves.
- The **AssetKit base** (existing) — for `WeaponDef`, `ZoeEvent` lists.
- **Inspector only** — for `AimSource`, `FiringSource`, `CharacterModule`. These are plumbing, not authoring surfaces.

### What's generic (in Laubrary)

- `MotionState`, `MotionSet`, `MotionRule`, `BodyPartSlot`, `MotionPose`, `DirectionMode`, `IDirectionBin` and its 16/8/4/2 implementations, `MotionResolver`.
- `CharacterModule` (the arbiter).
- `AimSource`, `FiringSource` (the single-owner inputs).
- The pattern "one arbiter owns the body, everything else submits" — encoded by the module's API surface.
- The "first-match-wins rules" idiom (shared with `ZoeEvent.EventFor`).
- `IDirectionBin` mirroring (so 16 directions come from 9 authored frames — the project's stated art-economics goal).

### What's project-specific (in consumer code, NOT Laubrary)

- `PlayerIntent` (or whatever the project's input system is) — the script that reads mouse/keys and calls `character.PublishMotionState(...)`.
- `EnemyBrain`, `BossBrain`, `TurretBrain` — the AI scripts that do the same for non-player characters.
- The actual numeric tunables: `idleMoveThreshold`, `walkSpeed`, `runSpeed`, `jumpImpulse`. These go in `ScriptableObject` config assets the project owns.
- Per-character "feels" — squashing on landing, weapon recoil amount, aim smoothing curve. These are per-character `ScriptableObject`s or per-prefab serialized fields, NOT library types.
- The HUD, reticle, sound design — not a character module concern.

### Where the line is fuzzy, and how this design resolves it

| "Is this generic?" | Answer |
|---|---|
| "Hover bob amplitude" | Per-character data. A field on `BodyPartSlot` or a config asset. |
| "Run cycles speed-scaled by `state.speed`" | Generic — the resolver always offers `playRate = state.speed / authoredSpeed`. The artist opts in per-set via a checkbox. |
| "Y-sorting by `state.depth`" | Generic — a `SpriteRenderer.sortingOrder` setter on `BodyPartView`. |
| "Aim smoothing (lerp angle to target)" | Generic — `AimSource` has a `SmoothingMode` enum: `Instant`, `LinearLerp`, `Spring`. Project picks per character. |
| "Cinemachine camera shake on shoot" | Project-specific. Subscribe to the `ZoeEvent("ShootFired")` from your camera controller. Library doesn't know. |
| "Multi-frame shoot animation (recoil kick + recovery)" | Generic — the `FiredEvent` is a `ZoeEvent` with a 2-frame laumination. Already built. |
| "Aim while dead should be ignored" | Generic — `MotionResolver` checks `mode != Dead` before resolving; if dead, a dead-state `MotionRule` is matched first. |

---

## 7. Open questions — ranked by importance

### 1. (HIGH) Where does `MotionPose` data LIVE on the Zoe asset?

ZOE_MOVEMENT_DESIGN § 1.4 leaves this open: "Where a part list is declared — on the composite view, or on the Zoe? The view knows the parts; the Zoe knows the poses. Likely the view declares, the Zoe references by name."

This design chose: Zoe owns the pose (the *rules/sets per slot*); the view (per-slot MonoBehaviour) declares the *slots by name*. The Zoe then references slots by string name (picked, never typed). But:

- Does the Zoe hold a `List<MotionPose>` (one pose per "view mode": top-down vs side-scroller, swapped at runtime by a `MotionPoseSelector`), or one pose per Zoe (multi-view = multi-Zoe asset)?
- If the former, who picks? The CharacterModule at startup? A `PoseSelector` component on the instance root? A camera-level signal?

**This blocks the data layout.** Decide before the Zoe asset format is locked in. Recommendation: `List<MotionPose>` per Zoe, picked by a `MotionPoseSelector` strategy (configurable per scene — top-down vs side-scroller — for now a `MonoBehaviour` set in the inspector; future a `Camera2D.ViewKind` signal).

### 2. (HIGH) `MotionCondition` — declarative predicate DSL or simple enum?

This design sketched `MotionCondition` as a serialised predicate. But:

- Enums (Idle, Walk, Run, Airborne, Dead + flag combinations on MotionRule) are simpler and ship faster.
- A real predicate DSL ("speed > threshold AND grounded AND NOT aim") is more expressive but needs authoring UI.

**This blocks the editor pass.** Recommendation for v1: enum + flag fields (`requireMoving`, `requireAiming`, `requireGrounded`, `requireUnderPower`, `requireSelfWilled`). Promote to DSL when a real character can't be expressed. Already the project's bias ("ship the simplest thing that fits three characters").

### 3. (MEDIUM) Does `CharacterModule` host `AimSource` and `FiringSource`, or are they separate components on the same GameObject?

This design puts them on the same GameObject as separate components. Pro: single-owner clarity. Con: more component slots in the inspector.

Alternative: embed them as `[SerializeField]` fields on `CharacterModule`. Pro: one component. Con: reusability — a turret-only enemy might want `AimSource` without `FiringSource`.

**Recommendation: separate components.** A turret that doesn't fire has no `FiringSource`. A camera-only prop has no `AimSource`. Single-component would force the empty field.

### 4. (MEDIUM) How does `MotionResolver` handle the case where a slot's `MotionSet` references a `LauminaryVersion` that doesn't exist?

The Zoe could be authored with a typo'd `lauminary` reference; in edit-time the editor should surface this. At runtime, the safe default is: fall back to the unconditional last rule's set; if THAT is also missing, log once and skip the slot (don't crash). Already aligns with the project's "graceful degradation" goal.

**Recommendation: enforce at edit time (Zoetrope window warns on broken refs); at runtime, silent skip with one console warning per asset-load, never per frame.**

### 5. (LOW) Mirroring — does it live on `MotionSet.mirrored`, on `IDirectionBin`, or on `BodyPartSlot`?

This design put it on `MotionSet.mirrored`. Alternative: on the bin (so the bin knows "I mirror"). Pro of bin-owned: `Bin16` *always* mirrors (it has no choice if you have 9 authored frames); con: not every Bin needs to mirror (a `Bin4` for a non-symmetric sprite might not). Putting it on the slot lets each slot opt in or out.

**Recommendation: keep `mirrored` on `MotionSet`** — it's a property of the *art*, not the bin. A non-mirrored 16-bin sheet is a real thing (asymmetric character); a `Bin16` should be able to handle both modes.

---

## Appendix A — What this design is NOT

- Not a state machine. Steady state is a pure resolver; one-shots are `ZoeEvent`s.
- Not an animation tool. It indexes existing `Laumination`s on existing `LauminaryVersion`s. The authoring UI is whatever Launimator already provides.
- Not a new editor window. Everything is authored through Zoetrope's existing Zoe editor + per-component inspectors.
- Not input. The intent source is the game's job.
- Not AI. Same — the brain is the game's job.

## Appendix B — Mapping to the existing runtime

| New type | Lives near | Replaces / extends |
|---|---|---|
| `MotionState` | `Runtime/Combat2D/Character/MotionState.cs` | The implicit `bool moving` on today's `Locomotion` |
| `MotionSet` / `MotionRule` / `MotionPose` | `Runtime/Zoetrope/MotionPose.cs` | The current `Locomotion.cs` ("two clips, no direction"). Per ZOE_MOVEMENT_DESIGN: "a resolver. The Zoe carries a *locomotion set*" |
| `BodyPartSlot` / `BodyPartView` | `Runtime/Zoetrope/BodyPartView.cs` | New — slot wiring (per "view declares, Zoe references by name") |
| `DirectionMode` / `IDirectionBin` / `BinN` | `Runtime/Zoetrope/Direction/` | New — the direction-mode vocabulary the design's own table already uses |
| `CharacterModule` | `Runtime/Combat2D/Character/CharacterModule.cs` | New — "one arbiter owns the body" |
| `AimSource` / `FiringSource` | `Runtime/Combat2D/Character/` | New — single-owner inputs |
| `PlayerIntent` / `EnemyBrain` / etc. | Game-side | OUT of Laubrary (per ZOE_MOVEMENT_DESIGN INTENT rule) |

The migration path from today's `Locomotion` to this design is a gradual one:
existing Zoes keep working (single part, one unconditional rule, no direction =
today's behaviour), new Zoes opt into the new shape. No big-bang port.
