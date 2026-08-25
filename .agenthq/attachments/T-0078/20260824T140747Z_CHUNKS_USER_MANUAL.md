# Chunks — User Manual (as of 2026-08-24)

## What Chunks is for

Chunks is the "flying debris" tool. When something explodes, breaks, or gets hit, Chunks throws a swarm of small pieces outward with realistic-ish physics (gravity, drag, spin, bounce). It is the runtime companion to Pyre/PyrePlus: Pyre bakes the explosion's sprite-sheet FLASH, Chunks throws the physical BITS that fly away from it. They don't have to be used together — Chunks works standalone.

A "burst" is one explosion event: you author a **Chunk Spec** (an asset — a recipe: how many pieces, how fast, what they look like, how long they live) and fire it at a world position. Chunks spawns that many small sprite objects, each with its own randomised velocity/spin/lifetime, and they fly, arc under gravity, optionally bounce/settle on a floor, fade out, and clean themselves up automatically. Nothing needs to be pooled or destroyed manually.

Four distinct debris looks are supported, chosen by which optional field on the recipe is filled in:
1. **Plain procedural squares** — the zero-setup default. No art needed at all; every chunk is a small tinted pixel square.
2. **Authored sprites** — hand a list of your own debris sprites to pick from at random.
3. **Sampled pseudo-3D debris** — the standout feature. Chunks cuts small pieces directly out of the exploding object's OWN sprite (random sub-rects biased toward opaque pixels), then fakes them tumbling in 3D with a squash + light/dark shading trick, so it reads as chunks of the actual thing breaking apart, not generic debris. Can also be tinted (whole piece / just the rim like a burning edge / everywhere except the rim).
4. **Animated content** — instead of a static image, every flung piece plays a small animation (a Pyre/PyrePlus blast, a Launimator reel). Good for jet-engine fireball trails or animated embers.

On top of the base burst, two optional extras exist: chunks can carry a **trail** (a puff of something — e.g. fire fading to smoke — spawned behind them as they fly) and can optionally **deal damage** to whatever they touch (a cheap circle-collider approximation, off by default, meant for things like "wall peppered by bullet debris").

## What it can actually do today

Everything above is real, built, and compiles clean — this is not a stub. Confirmed present in the code:
- Full burst physics: count range, speed range, direction cone (or a full 180° radial burst), upward bias, gravity, drag, spin (random or "face direction of travel"), lifetime range, size range with an over-life curve, an alpha-over-life fade curve, and a colour-over-life gradient tint.
- Floor bounce/settle: an optional flat floor line the pieces can bounce off (with configurable bounciness and friction) and settle to rest on instead of just despawning mid-air.
- Sampled pseudo-3D debris cut straight from a source sprite's pixels, with tumble simulation and three tint modes.
- An optional baked-in pixel-effect stack (tint, posterise, dither, dissolve, etc.) applied once to each sampled piece at spawn time.
- Plugging in animated content from Pyre or Launimator so flung pieces play a mini-animation instead of a static image.
- An optional trail puff spawned behind each flying piece on a timer.
- Optional cheap hit-detection so debris can deal damage to things it flies through.
- A fire-and-forget code API (`Chunks.Burst(position, spec)`) plus a drop-on-a-GameObject component (`ChunkEmitter`) for when you want the burst tied to something in the scene (e.g. spawn-point, damage attribution).

**What is NOT built yet** (the bigger "Chunks 2.0" redesign is fully written up as a design document but every part of it is still unstarted — status "todo" on all 8 of its planned pieces): a shared named-layer system so a burst can be composed of several styled layers at once (e.g. "shockwave layer + smoke layer + fireball layer + fragment layer" all timed together); a Fragment Slicer that cuts a sprite into a handful of LARGE recognizable pieces (today's sampling only produces small generic bits); a dedicated Pyre-spawning module with real physical movement (today you can attach ONE animated source per whole burst, not "spawn several little pyres that each move independently"); a timeline for sequencing multiple different effects at different times within one burst; and a one-click "preview in Mirage" shortcut. In short: today's Chunks is genuinely good at "one burst, one look, thrown all at once" — it does not yet do "one explosion made of several different coordinated effects layered together."

## How well-developed the UI is

Well-developed, and it's a real modern editor window, not a fallback inspector. Opening `Laubrary → Chunks` gives you a dedicated authoring window (same family as the Pyre/PyrePlus windows — browse/create/duplicate/rename/delete a Chunk Spec asset for free) with the fields grouped into clearly labelled sections: Emission, Physics, Life/Look, Floor/Collision, Sampled Pseudo-3D Debris (with its own live Preview sub-section), Animated Content, Hit Detection, and Trail. Every dial is undo-safe individually (each field edit is its own separate undo step, not one blanket "undid everything since I opened the window").

The standout part of the UI is the **live slicing preview**: pick any sprite as a "subject," and the window shows that sprite with the actual cut rectangles drawn on top of it, plus thumbnails of the exact debris pieces those cuts would produce — using the identical code path the game uses at runtime, so what you see in the editor is exactly what you'll get in play mode. Tweaking a tint or size dial re-cuts the same pieces live so you can see the effect of one change at a time, and a "Resample" button lets you reroll to a fresh random set of cuts. This preview only appears/matters when you're using the sampled-debris feature; the other three debris types (procedural, sprite list, animated) don't have their own live burst-motion preview yet — you see the source art/thumbnail, not the pieces actually flying.

Sections that don't apply currently collapse away — e.g. the sampled-debris detail fields don't show until you've picked a source sprite, hit-detection's damage/radius fields don't show until you've turned it on. Nothing about the UI looked unfinished, mislabeled, or broken while reviewing it.

## How to try it (recommended first steps)

1. Open the pre-built demo: `Assets/Demos/ChunksDemo/ChunksDemo.unity`, then press Play. Left-click anywhere for a radial "pop" burst, press Space near the left wall for a directional burst that arcs and settles on a floor, press T to fire the sampled-pseudo-3D debris burst (cut from a demo shape, no art needed). This is the fastest way to actually SEE what Chunks produces before authoring your own.
2. Open `Laubrary → Chunks` and either open one of the demo's three Chunk Spec assets or create a new one, to see the authoring window and its live slicing preview firsthand.
3. To use it in your own game: create a Chunk Spec asset (via the Chunks window, or right-click → Create → `Laubrary/Chunks/Chunk Spec`), tune it, then either call `Chunks.Burst(worldPosition, yourSpec)` from code wherever something explodes, or add a `ChunkEmitter` component (drag your spec onto it) to a GameObject and call its `Burst()` method — e.g. from an existing "on death" or "on hit" event.
4. If you want the standout "breaks into pieces of itself" look, set the spec's "Sample Source" to the sprite of the thing that's exploding (its texture needs "Read/Write Enabled" turned on in its import settings) and use the window's Preview section to dial in size/tint before trying it in Play mode.

Nothing here was play-tested by an automated check as part of writing this manual beyond confirming the demo scene has all three specs wired up and the code compiles — actually seeing the bursts fly is worth doing yourself via step 1 above.
