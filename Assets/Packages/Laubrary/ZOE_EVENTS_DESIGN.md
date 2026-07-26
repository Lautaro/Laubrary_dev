# Zoe events — design

**Status:** designed, not implemented. Decisions locked 2026-07-26 (user: "go with your recommendations").

**One line:** a Zoe event configures how a Zoe reacts to something — a trigger, a typed set of in-params, and a list of pluggable effects that read those params plus the Zoe's own live data. It is the "Director" layer that cues Pyre / Chunks / SpriteFx / physics / the reel on one game beat.

## Where it comes from

This EVOLVES the existing `ReactionFx` (Zoetrope), which is already ~80% of it: a `clip` + a `List<FxEntry>` (each entry with an `FxPlacementType` + a `[SerializeReference] ICombatFx`), plus the `bodyFx` (`SpriteFxSpec`) slot added 2026-07-26. **Decision: EVOLVE `ReactionFx` in place, not a parallel type.** Existing Zoe hit/death reactions must keep working (additive / `[MovedFrom]` migration, no data loss).

## The three generalizations

### 1. A typed event CONTEXT (the "in-params")

Today the event context is baked into `DamageInfo` (point, direction, amount) and effects reach it crudely through `FxPlacementType`. Promote it to a typed context the trigger fills, that every effect can read:

- **The target Zoe's live data** — its `SpriteRenderer` / current reel frame (for colour-sampling), `Transform`, `Health`, brain/AI. Always present.
- **Positions** — hitPosition, the Zoe's own position, sprite-bounds centre, named meta-layer points. (`FxPlacementType` already enumerates these four.)
- **Directions** — hitDirection (NEW as a first-class param; `DamageInfo.direction` exists, it's just not exposed).
- **Scalars** — damage amount / "scale" (NEW — so an effect can size itself to the hit).

**Decision: a FIXED per-event-type schema.** Each event TYPE (Hit, Death, later Spawn / AbilityUsed / …) declares which in-params it carries (Hit = hitPosition + hitDirection + amount; Death similar). Effects pick from that known, typed set. Chosen over a generic named-param bag: simpler, fully typed, no runtime param-matching machinery.

### 2. A pluggable EFFECT palette (`IEffect`)

Today an entry's effect is one `ICombatFx` (spawn-VFX-at-a-world-point). Generalize to `IEffect` — pluggable (`[SerializeReference]`, exactly like `ICombatFx` is today), runs on the event, reads the context. Palette:

- **Spawn VFX (Pyre / Chunks)** — today's `ICombatFx`, but spawned at a CHOSEN position param and sized by a scalar param. A Chunk effect samples the Zoe's CURRENT reel sprite for its burst colours (it reads the live renderer from the context, so debris matches the enemy).
- **SpriteFx** — apply a `SpriteFxSpec` to the Zoe's body renderer for a duration (the `bodyFx` slot, now a first-class effect — the SpriteFx stack tool built 2026-07-26 authors these).
- **Pushback** — a physical impulse on the Zoe along a direction param (hitDirection, negated), magnitude from a scalar or a fixed value.
- **Play Reel** — play a clip (today's `ReactionFx.clip`, as an effect).
- **Stun** — a timed effect that puts the brain/AI into a stun state for X s. **DEFERRED** — needs an AI stun-state that doesn't exist yet.

`ICombatFx` STAYS as the "spawn VFX at a point" effect kind (its impls, e.g. `PyreChunksFx`, keep working); the other kinds are new `IEffect`s.

### 3. Effects pick their params (typed pickers)

The key ergonomic the user asked for: an effect's position / direction / scalar field is a DROPDOWN of the event's available params of that type. A Spawn-Pyre effect's *position* = {hitPosition, zoePosition, sprite-centre, meta-point}; a Pushback's *direction* = {hitDirection, …}; a size field reads a scalar param. This is `FxPlacementType` promoted from a fixed position enum into per-type param PICKERS (position + direction + scalar), fed by the event's declared in-params. The Zoe-event editor exposes exactly the current event type's params as options when adding an effect (esp. positions for a Pyre/Chunk, direction for a Pushback).

## The rule that keeps it clean

Effects stay pluggable PER BRIDGE MODULE, exactly like `ICombatFx` does now — `PyreChunksFx` lives in `ZoetropePyre`, a SpriteFx effect sits above the (lower-level) SpriteFx module, Pushback needs a movement/rigidbody hook on the Zoe, Stun needs the brain to expose a stun state. Core Zoetrope stays dependency-free; each effect kind ships in the module that owns its tool.

## First spine (build order)

Everything but Stun is buildable today (the tools exist):

1. Generalize the context: a typed `EventContext` (target Zoe + positions + directions + scalars), filled by `ReactionFxPlayer` from `DamageInfo` on Hit/Death; expose hitDirection + amount.
2. `IEffect` + the param pickers (position / direction / scalar), generalizing `FxEntry` / `FxPlacementType`; keep `ICombatFx` as the Spawn-VFX effect kind.
3. The effect palette, each in its owning module: Spawn-Pyre / Spawn-Chunk (at a position param, sized by a scalar; Chunk colour-samples the reel), SpriteFx (bodyFx), Pushback, Play-Reel.
4. The Zoe-event editor: an event's effect list, each effect picking its params from the event's declared in-params.
5. **Stun** — after the AI/brain gains a stun state.

The first playable "hit event": flash (SpriteFx) + a Pyre core-burst + reel-coloured Chunks + knockback — everything but stun.

## Caveats

- Chunk colour-sampling needs the Zoe's CURRENT reel frame (the live `SpriteRenderer`), so the effect reads it from the context, never from authored data.
- Pushback needs the Zoe to have a movement / rigidbody component; the effect no-ops (with a warning) if absent.
- Stun is gated on the AI/brain (Daemon?) exposing a stun state — the one deferred effect.
- Migration: `FxEntry` becomes an effect entry; `FxPlacementType` becomes the position picker; add direction/scalar pickers; `bodyFx` becomes a SpriteFx effect; `clip` becomes a Play-Reel effect (or stays a top-level convenience). Keep every existing Zoe reaction working (no data loss).
