# Zoe movement ↔ visuals — the problem, before any solution

**Status:** context only. Nothing here is decided or built. Written 2026-08-06 to set up the reasoning, the way `ZOE_EVENTS_DESIGN.md` did for events.

**The gap in one line:** a Zoe says what a character IS (stats, look) and what it DOES on an occasion (events), but nothing says **how it moves** — and the one piece that maps motion to visuals is a two-clip on/off switch that cannot express a direction, a gait, a jump, or a knockback.

---

## 1. Three questions currently tangled into one

Every "movement" conversation is really three, and they have different owners:

| # | Question | Who should own it | Where it lives today |
|---|---|---|---|
| **1. INTENT** | What is trying to move this thing, and where to? | The GAME (input, AI, physics, a choreography) — never Laubrary | Outside the library, by design |
| **2. STATE** | What is the character's motion *right now*: speed, facing, airborne, dashing, reeling? | Shared vocabulary — this is the missing piece | A single `bool moving`, recomputed from transform deltas |
| **3. PRESENTATION** | Given that state, which laumination plays, at what rate, mirrored or not? | Laubrary | `LocomotionAnimator`: two clips, no direction |

The gap is **(2)**, and (3) is starved by it. Keeping (1) out is a deliberate and, I think, correct constraint — Asteroid+ (thrust/rotate inertia), a side-scrolling shmup wave, and a top-down walker have nothing in common at the intent layer, and the library already refuses to own input.

## 2. What exists today, precisely

**`Locomotion` (on the Zoe)** — `idleClip`, `moveClip`, `moveThreshold`. Two states, no direction. Its own doc comment is honest that it is "the floor".

**`LocomotionAnimator`** — measures `transform.position` deltas each `LateUpdate`, derives a speed, and plays idle or move on a change. Deliberately input-agnostic: it reads the world rather than taking a velocity from a mover, so it works for a Rigidbody, a Choreographer dancer or a scripted walk without any of them sharing an interface. That instinct is right and worth keeping.

**`ZoeState.CanAct`** — the one flag everything asks before acting. Exists precisely because movers, weapons and the animator each used to answer "may I?" their own way and disagreed (corpses walked; the hurt animation was stomped by the walk cycle a frame later).

**`ZonedAnimationPlayer.flipX`** — the only directional facility in the runtime: mirror the sprite.

**`AnimZone` strips** — a laumination can already be a phased strip (the asset's own example is a jump: Start / Air / Fall / Land) with `EnterAt`/`WantZone`/`EndIn`. **The asset layer is ahead of the runtime here**: airborne phases are already expressible and nothing drives them.

**Events + cues (new)** — one-shots with consequences, and "the animation reached a point" as a trigger. Footstep dust is already solvable.

**`LauminaryVersion.controller`** — a dormant `RuntimeAnimatorController` field. A second, unused path to "what plays when".

### Latent problems in what exists

- **Knockback reads as walking.** Speed is measured from transform deltas, so being shoved plays the walk cycle. Nothing distinguishes self-willed motion from being moved.
- **No hysteresis** on `moveThreshold`: a character hovering at the boundary flaps between idle and walk.
- **One frame of lag**, inherent to measuring after the fact.
- **The arbiter is a two-party hack.** `ZoeState.CanAct` plus `LocomotionAnimator.SuppressFor` resolves exactly one conflict — locomotion vs a reaction. A third claimant (a dash, a jump, an aim pose) has nowhere to plug in, and the fix will not be a third flag.
- **Facing is a mirror.** Anything beyond left/right needs §6's directional sets.

## 3. The vocabulary — what kinds of movement actually need covering

Grouped by how they behave, because that is what decides the mechanism:

1. **Steady state.** Idle ↔ walk ↔ run. Continuous, interruptible, loops. Wants: gait selection by speed, or one cycle whose playback rate scales with speed.
2. **Directional steady state.** The same, but the body faces somewhere: 2-way (mirror), 4-way, 8-way, 16-way. Complicated by **facing ≠ heading ≠ aim** — a strafing shooter walks left while aiming right.
3. **Phased / airborne.** Jump, fall, land; a dash with wind-up and recovery. Has an ordered internal structure and an *unknown duration in the middle* (hang time). `AnimZone` strips are exactly this shape.
4. **Impulses.** Dash, dodge-roll, knockback, recoil. Short, interruptive, often carry consequences (i-frames, no-act windows). **These are already events** — a one-shot laumination plus consequences is precisely what a `ZoeEvent` is.
5. **Non-legged.** Hover/float (the Floating Disc), inertial drift, swarm steering. Often has no "walk cycle" at all — motion is conveyed by tilt, bob, thruster FX, not by a gait.
6. **Externally driven.** Choreography paths, cutscene walks, tethered/pooled wave motion. The character is a passenger; the visuals should still respond.
7. **Terminal.** Death disposal, corpse settling — already owned by `ZoeState`/disposal.

Note what this list implies: **(1), (2) and (5) are steady-state resolution; (3) and (4) are one-shots.** That split is the load-bearing observation, because the project already has an excellent mechanism for one-shots.

## 4. Typical solutions, and which fit here

| Approach | What it is | Fit here |
|---|---|---|
| **Boolean clip switch** | What `Locomotion` does today | The floor. Already outgrown. |
| **Rate-scaled cycle** | One walk cycle, playback rate ∝ speed | Very strong for pixel art — no extra authoring, reads instantly, and removes the walk/run threshold problem entirely |
| **Gait bands** | Discrete clips per speed band + hysteresis | Needed only where run is a *different* animation, not a faster one |
| **Directional sets** | 4/8/16 members, mirrored to halve authoring | This is §6 / task A4, already agreed |
| **Blend trees (Mecanim)** | 2D freeform blending between clips | Blending is meaningless for sprite frames — the sprite analogue is *snap to nearest member*. The useful part is the indexing, not the blending |
| **Animator state machines** | Transitions authored in a graph | The trap this project keeps avoiding: a second place where "what plays when" is authored, guaranteed to drift from the data. The dormant `controller` field is this path |
| **Animation as a pure function of state** | Gameplay publishes a state struct each frame; a resolver deterministically returns (laumination, rate, mirror, direction member) | **The natural fit.** No transition graph, no drift, trivially previewable in Mirage, and it is the same move the event model already made for occasions |
| **Layering / part split** | Upper body aims while legs walk | Needs composite bodies (`CompositeLauminaryView` exists but is parked). Matters the moment aim ≠ heading |
| **Root motion** | Animation drives displacement | Almost always wrong for 2D pixel action; code moves, animation decorates |

## 5. The shape I would argue for (to be attacked, not adopted)

**Split by duration, not by subject:**

- **Steady state → a resolver.** The Zoe carries a *locomotion set*: which laumination for which motion state, plus how direction indexes it. Each frame something publishes a small `MotionState` (speed, heading, facing/aim, grounded, self-willed vs pushed) and the resolver returns what should be playing. Pure function, no state machine, no transitions to author.
- **One-shots → events.** A dash, a jump, a knockback reel is a `ZoeEvent` raised by game code — it already has a laumination, a duration, consequences, effects and now cue triggers. Nothing new is needed except the raising.
- **One arbiter owns the body.** Locomotion, events and anything else *submit* to a single component that decides by priority, replacing `CanAct` + `SuppressFor`. That is the piece whose absence is already causing bugs.

The reason this appeals: it introduces exactly one new concept (the motion state + its resolver) and reuses events for everything episodic, rather than inventing a parallel movement-animation system that would then need its own trigger, duration and consequence models.

## 6. The answers, and what each one forces

Answered 2026-08-06. Each one removes a simplification I was leaning on, so they are worth reading as constraints rather than preferences.

| Answer | What it forces |
|---|---|
| **All 2D sprite genres: shmup, side-scroller, platformer, pseudo-3D, depth shooter** | Motion is not a 2D vector. A depth shooter moves along a Z the camera does not show, and pseudo-3D scales with it. The state needs a **depth axis**, and "how depth changes the picture" (scale, sorting) is a VIEW concern, not a locomotion one — keep them apart or every genre leaks into the walk cycle. |
| **Jump / fall / land, first-class** | Airborne is **not** an event. An event is a one-shot of known shape; a jump has an **open-ended middle** whose length is decided by physics, frame by frame. That is precisely an `AnimZone` strip (Start → Air, held → Fall → Land), so airborne is *steady state with phases*, steered by the resolver. My "a jump is just an event" shortcut is dead. |
| **2 / 4 / 6 / 8 / 16 or smooth 360, all possible** | Direction resolution is a property of the **set**, not of the project — and there are three genuinely different mechanisms: pick-a-member, mirror, and **rotate the transform** (smooth 360 with one sprite, which is what an Asteroid+ ship wants). A set has to declare which it uses; a 16-way walker and a rotating ship cannot share one code path pretending to be the other. |
| **Aim decoupled from facing** | At least two direction channels, and the character no longer has *a* direction — different parts answer to different channels. |
| **Composite bodies planned** | The resolver cannot assume one body. It resolves **per part**, and the part is the unit that owns a laumination. This also forces the `(part, laumination)` pair the event design explicitly parked and told us to leave room for — so movement is what makes composite bodies real, and the two land together. |

## 7. The resolved shape

Three pieces. The first is new; the second generalises §6's directional sets; the third replaces a hack that is already biting.

### 7.1 `MotionState` — the published truth about how this thing is moving

One small struct, refreshed per frame. Inferred from the transform by default (so Choreography, physics and scripted walks keep working with zero wiring), overridable by a game that knows better:

- `velocity` (x, y, **depth**) and the scalar speed derived from it
- `heading` — where it is travelling
- `facing` — where the body is turned (may lag heading; may be driven by aim)
- `aim` — from `Combatant.aimDirection`, already exists
- `grounded` + `verticalVelocity` — the platformer axis
- `selfWilled` — moving under its own power vs being shoved. **This is the fix for knockback reading as a walk cycle**, and it cannot be inferred from a transform, which is exactly why the state must be publishable.

Everything else reads this. Nothing else measures the transform.

### 7.2 `LauminationSet` — one laumination per direction, per gait, per phase

The §6 set, widened by the direction answer. A set declares its own `DirectionMode`:

- **None** — one laumination, no direction
- **Mirror** — one laumination plus its flip (the 2-way case, what `flipX` does today)
- **Members(N)** — 2 / 4 / 6 / 8 / 16 members tagged by angle, **mirroring inside** so 16 directions come from ~5 authored lauminations; resolve by nearest angle
- **Rotate** — one laumination, the transform rotates to the angle (smooth 360)
- **Members + Rotate** — members for the coarse sector, rotation for the remainder; the technique that buys smooth aiming from few sprites

Resolution is a pure function: `(set, angle) → (laumination, mirrored, rotation)`.

### 7.3 `MotionPose` — per part, what plays for which state

The Zoe carries one of these per part (a single-body Zoe has exactly one, unnamed — so nothing changes for today's assets):

- **Which channel aims this part**: Heading / Aim / Fixed. This is where "legs follow movement, torso follows the crosshair" is expressed, and it costs one enum.
- **Grounded sets**: Idle, Move — plus optional gait bands if run is a *different* animation rather than a faster one.
- **Airborne**: a zoned strip, with the resolver steering the zone from `verticalVelocity` (rise → hold Air → fall → Land on grounding).
- **Rate**: constant, or scaled by speed. Rate-scaling is the cheap win — one cycle covers walk and run with no second animation and no threshold to flap across.

### 7.4 The arbiter — the piece whose absence is already causing bugs

A component that owns `PlayClip` **per part**. Locomotion, events and one-shots stop calling the player directly and instead *submit a claim* (what to play, at what priority, for how long). Highest priority wins; when a claim expires the resolver reasserts steady state on its own.

This replaces `ZoeState.CanAct` + `LocomotionAnimator.SuppressFor`, which is a two-party special case that a dash, a jump and an aim pose have no way to join. `CanAct` stays as the gameplay gate ("may this character act"); it stops being the animation arbiter.

## 8. Three test cases, run through the model

Given 2026-08-06. Each one breaks something different, which is what makes them worth keeping as the acceptance set.

### 8.1 Enemy tank — turret aims, hull drives

| Part | Channel | Direction mode | Sets |
|---|---|---|---|
| `Turret` | **Aim** | Rotate (free or snapped) | one 1-frame laumination |
| `Hull` | **Heading** | Rotate | `Accelerating → 3-frame tread cycle`, else `1-frame static hull` |

**What it breaks — 1: "when accelerating" is not idle-or-moving.** The whole grounded vocabulary I proposed (Idle set, Move set, gait bands) is fixed slots — the exact mistake the event model already paid for with `hit`/`death`. A tank wants treads while *under power*, and something else will want "while braking" or "while airborne and rising". So `MotionPose` stops being named slots and becomes an **ordered list of rules — condition → set — first match wins**, with an unconditional final rule as the fallback. That is the same idiom as `Zoe.EventFor`, so there is one "first match wins" pattern in the library rather than two dialects. It also means `MotionState` must carry **acceleration** (or "under power"), which a transform-derived velocity can supply but only with a frame of lag — another reason the state wants to be publishable.

**What it breaks — 2: a rotating parent rotates its children.** Composite parts are real child GameObjects (`CompositeZonedPlayer` builds one per part and parents them), so if the hull's transform rotates to its heading, the turret inherits that rotation and its aim is wrong by exactly the hull's angle. Position inheritance is *wanted* — the turret rides the hull's attach point as it turns — rotation inheritance is not. So a part's resolved rotation must be written as **world rotation**, never local. This would have been an infuriating bug to find later; it is free to avoid now.

**What it breaks — 3: a stationary tank has no heading.** Velocity is zero, so the channel has no value. The hull must keep pointing where it last drove. **Resolved direction latches** when its channel goes quiet — true for the player's legs standing still too.

### 8.2 Player — torso aims in 16, legs run in 4

| Part | Channel | Direction mode | Sets |
|---|---|---|---|
| `Torso` | **Aim** | Members(16), mirrored | one member per direction (1-frame each) |
| `Legs` | **Heading** | Members(4), mirrored | `Moving → 3-frame run`, else `idle` |

This is the decoupling case and it works as designed: two parts, two direction modes, two channels, no shared assumption. Worth noting the authoring economics that mirroring buys — 16 aim directions come from **9** authored members (0° through 180°), and 4 leg directions from **3** (up, down, side).

It also confirms a small thing that must be true: a "set member" is a laumination, and a **1-frame laumination is a perfectly ordinary one**. Static-per-direction and animated-per-direction cannot be two different concepts in the model, or the torso and the legs stop sharing a mechanism.

### 8.3 Flying disc — travels any direction, has a death animation

| Part | Channel | Direction mode | Sets |
|---|---|---|---|
| (single body) | — | **None** | one looping hover laumination |

Plus a **death event**, which already works today — nothing about movement is involved in dying.

This is the case that proves direction must be *optional*: a rotationally symmetric hovering disc looks identical travelling in any direction, and a model that insists on a direction channel would force a meaningless answer. It is also the graceful-degradation check: **a single-part pose with one unconditional rule and no direction is exactly today's `Locomotion`**, so every existing Zoe keeps working with no authoring.

### What the three together add to the design

1. `MotionState` gains **acceleration / under-power**.
2. `MotionPose` becomes an **ordered rule list (condition → set), first match wins** — no fixed idle/move slots.
3. Resolved direction **latches** when its channel has no value.
4. Rotation is written as **world rotation, per part** (children must not inherit a parent's facing).
5. `Rotate` mode gains an optional **angle snap** — free rotation of a pixel sprite shimmers, and most pixel games quantise to 16 or 32 steps even when they look smooth.
6. Direction is **optional**; "None" is a first-class mode, and the no-direction single-part case must reproduce today's behaviour exactly.

## 9. What is still open

1. **Turn-in-place** — when facing changes but the character is standing still, is that a transition (a turn animation) or an instant snap? Snap is normal for pixel art; a turn set would be a later addition to `MotionPose`.
2. **Depth → picture.** Scale-with-depth, sorting and shadow offset for pseudo-3D: a view concern, but it needs an owner. Probably the view, reading `MotionState.depth`.
3. **Per-part events.** Does a hurt flash claim the whole body, or may an event target one part? Default whole-body; the claim model can express either.
4. **Where a part list is declared** — on the composite view, or on the Zoe? The view knows the parts; the Zoe knows the poses. Likely the view declares, the Zoe references by name (never typed — picked).
5. **Gait bands vs rate scaling as the default** for a new Zoe.

## 10. A build order that stays useful if it stops early

1. **`MotionState` + inference**, replacing `LocomotionAnimator`'s internal speed guess. No visible change; unblocks everything and fixes the knockback misread the moment `selfWilled` is published.
2. **The arbiter.** Retire `SuppressFor`. Immediately fixes the class of bug where two systems fight over the body.
3. **`LauminationSet` + resolution** (this absorbs task A4 — directional sets are the same work).
4. **Grounded poses**: idle/move, rate-scaled, directional. This is the first *visible* payoff.
5. **Airborne**, steering an `AnimZone` strip. The platformer half.
6. **Per-part**, when composite bodies land — the model already allows it, so this is wiring, not redesign.
7. **A Mirage trigger** to drive a motion state ("pretend it is moving at 3 u/s heading NE, airborne") so all of it is previewable without a game.
