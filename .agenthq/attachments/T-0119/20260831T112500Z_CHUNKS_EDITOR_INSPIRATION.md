# Chunks editor inspiration — Shaper/Pyre composition UI, rough preview and timeline

## Answer

Yes. Chunks should deliberately borrow the eventual Shaper/Pyre authoring shape, not merely its modular data discipline.

The shared problem is composition: the author combines generators/capabilities, establishes their visual relationship, inspects a rough result, and optionally coordinates them in time. Chunks is different in what it produces—runtime event effects rather than a baked image—but that is not a reason to invent a completely different interaction language.

The target should be one coherent Chunks composition workspace with four coordinated views:

1. **Capability stack** — what the recipe contains.
2. **Layer plan** — what sits behind/in front of what.
3. **Rough spatial preview** — where things originate, spread, travel and land.
4. **One timeline** — when capabilities fire and, where meaningful, their trajectory occupancy.

Mirage remains the full-fidelity visual preview/playback destination. The Chunks rough preview is not a lesser Mirage; it answers different questions quickly while authoring.

## What should be shared with Shaper/Pyre

### Composition list rather than a field form

The primary view is an ordered list of capability cards, analogous to a Shaper tree/layer list or Pyre’s compact selected-item workflow. Every card has a stable identity, enabled state, concise outcome summary and remove control. Selecting it exposes its own compact details.

This means a recipe can read at a glance:

- Pyre formation — “Old School Explo × 5, line, 0.12 s stagger”
- Trajectory — “targets Pyre formation, up-right arc”
- Palette splash — “Floating Disc palette, 24 particles”
- Generic particles — “Smoke template, Zoe palette”
- Fragment fracture — “Floating Disc, 3 pieces”

The list must not become a list of full-height sections. Repeated Pyres are data within a single formation card unless the author intentionally adds independently timed/layered Pyre producers.

### One layer plan

Layers are a relationship view, not a set of fields spread across cards. The editor can show an optional compact layer rail/stack with named slots and the capability outputs assigned to them. The user sees “Smoke behind fragments; fire between; shockwave in front” in one place.

Layering is activated only when the recipe contains at least two outputs that require a deliberate order. A simple palette splash or one Pyre formation has no layer-plan UI to learn.

### One timeline

Chunks must have exactly one timeline/schedule. Its purpose is narrower than Mirage’s playback and different from Pyre/Shaper’s baking-oriented temporal tools:

- It shows when each schedulable capability starts.
- It shows stagger/repeat spans for a formation.
- It can show a trajectory’s duration and rough path occupancy.
- It owns cue/event markers.
- It does not need to reproduce every particle frame or full Pyre animation.

The timeline appears only when timing becomes an authored relationship: two or more schedulable producers, a non-zero delay/stagger worth inspecting, a trajectory duration, or an event/cue. It remains absent from the immediate single-module case.

## Rough spatial preview: required, but with a strict scope

Chunks needs a preview in its own editor because placement, spread, direction, gravity, arcs and layer relationship are spatial authoring decisions. Requiring a user to open Mirage after every 2D motion dial makes basic authoring too indirect.

The rough preview should be fast, deterministic and intentionally schematic:

- 2D origin/anchor.
- Direction arrow and spread cone.
- Formation positions and stagger order.
- Fragment initial positions/velocities.
- Projectile/Pyre trajectory paths, gravity arc and lifetime endpoint.
- Particle spray footprint and rough density cloud.
- Layer colour/label indicators and draw-order relationship.
- Playhead scrub for start times and motion guides.

It may use representative thumbnails, coloured discs, sprite silhouettes or simplified motion trails. It does **not** need to simulate Pyre frames, pixel-sampling exactness, ParticleSystem rendering, collision, pool timing or every generated fragment. If it claimed that fidelity, it would recreate Mirage poorly and make the editor slow.

The preview should be a stable canvas: manipulating a capability updates its visual guides without rebuilding/moving the panel under the pointer. It needs clear controls for playhead, zoom/frame bounds and reset, but those controls must stay compact and fixed.

## Mirage remains the truth for the finished effect

Mirage should be the “see the actual composed effect” handoff:

- Real Pyre frame playback.
- Actual palette particles and generic ParticleSystem backend.
- Real layers/sorting, source art, animation and runtime lifecycle.
- Replay in the broader scene/stage context.

The Chunks editor should provide a direct “Preview in Mirage” action that carries the current recipe and relevant preview source/anchor. This is complementary, not duplication:

| Question | Chunks rough preview | Mirage |
|---|---|---|
| Is the Pyre line pointing/spaced correctly? | Primary answer | Also visible, but slower |
| Does this arc overlap the fragments? | Primary answer | Final visual confirmation |
| Are layers conceptually ordered correctly? | Primary answer | Final rendered confirmation |
| Does the actual Pyre art/palette/particle system look good? | Deliberately approximate | Primary answer |
| Does the composed effect work in stage context? | Not the goal | Primary answer |

## Recommended mock-editor workspace

The non-production mock should validate this exact layout before data refactoring:

- **Left/top composition pane:** compact capability list, Add capability, selected-card controls.
- **Centre spatial canvas:** rough preview, origin/trajectory/formation/layer guides.
- **Optional right/secondary inspector:** selected capability’s short controls; for narrow windows it may collapse beneath the list without hiding the canvas.
- **Optional bottom timeline:** one shared schedule, only when timing is authored.
- **Mirage action:** fixed, direct and reachable from the recipe—not hidden inside an unrelated section.

The workspace must preserve the UI Guide’s stable-workspace rule: turning a capability on/off, adding a layer or revealing timing must not push the spatial canvas away during a gesture. Reserve slots or use overlays; do not make the main canvas jump.

## Design constraints for the prototype

1. Copy interaction principles, not source code. Shaper/Pyre and Chunks can share list/card/layer/timeline conventions, but Chunks needs its own spatial semantics.
2. Do not force Shaper-like nesting unless it solves a real Chunk relationship. The default Chunk structure is a flat composition list plus explicit target links; a trajectory targeting a formation is clearer than burying it as a child solely to resemble a tree.
3. Keep the rough preview fully mockable. It must run from mock module data and placeholder thumbnails before any runtime refactor.
4. Keep real editor data separate from visual mock data. The mock may clone/read real Pyre/Chunk assets for believable examples, but it must not mutate or depend on their current field shape.
5. Validate the complete authoring loop twice: add capability → set spatial values in the selected card → confirm guide in rough preview → add timing/layer relationship → confirm in one timeline → open Mirage for final visual confirmation.

## Recommendation

Adopt the Shaper/Pyre composition-editor family as the design reference for Chunks. The prototype should include a rough spatial preview and one optional shared timeline from its first iteration, because they are central to the Chunk workflow—not polish to bolt on later. Mirage remains the high-fidelity visual result, while Chunks provides the fast authoring map of the effect.
