# Chunks — User Manual (updated 2026-08-24)

## Your three use cases — can you do this today?

**Short answer: yes, all three.** The previous version of this manual (written earlier the same day) said "no, not one of the three" because Chunks 2.0 was a design document with no code. It is now built. Here is each case, what you switch on, and what is honestly still unproven.

Everything below happens in one window: **`Laubrary → Chunks`**. Pick or create a Chunk Spec, and each capability is its own section you switch on with the checkbox in its header. A section you have not switched on shows nothing but its title, so a simple recipe still reads as a short window.

### 1. Spawn X explosions in a line/shape, staggered, picked from a picker (and 1b: at random from a group)

**Switch on "Pyre Spawn" and "Spawn Formation".**

- *Pyre Spawn* decides **what** appears: one explosion you pick, or a **Pool** you fill with several — every spawn then draws one at random, which is case 1b. Nothing is ever typed; you pick from a browser with thumbnails.
- *Spawn Formation* decides **where and when**: choose **Line** or **Ring**, how many points, how far apart, and a **stagger** in seconds with the order they fire in — along the shape, backwards along it, outwards from the middle, or shuffled. A live picture of the arrangement sits right in the section, the dots shaded bright-to-dim by firing order, so you can read "a 270° arc, firing outwards from the middle" without pressing Play.
- Add **"Pyre Movement"** if you want each explosion thrown rather than going off where it appeared — speed, an arc, gravity, drag.

Switching the formation on means "not one, several", so it replaces the single spawn rather than adding an extra one at the centre.

### 2. Break a picture into big pieces and layer different explosions behind, in front of, and between them

**Switch on "Fragment Slicer", "Pyre Spawn", and fill in the "Layer Stack".**

- *Fragment Slicer* cuts your sprite into a few **large, still-recognisable pieces** — a spaceship becoming three bits of hull, not confetti. (The older "Sampled Pseudo-3D Debris" section is the opposite tool: many tiny generic specks. Both still exist; they are for different looks.) The section draws the cut live, each piece in its own colour over a ghost of the original, so you can see the break lines before playing. **Reshuffle** rerolls them.
- *Layer Stack* is the new one that makes "behind / in front / between" possible at all. You **name** the layers you want (`Smoke`, `Blast`, `Fragments`, `Splash`…), **drag** them into front-to-back order by the grip, and each switched-on effect gets a row where you **pick** which layer it draws in. The concrete depth number each layer resolves to is shown next to it, so the order is something you authored rather than something that happened.
- An empty Layer Stack means no layering at all — exactly how Chunks behaved before — so you never have to think about it for a simple burst.

**Requirement:** the sprite you cut needs **Read/Write Enabled** ticked in its import settings. Without it there is nothing to cut and the slicer quietly does nothing rather than erroring.

### 3. A debris source that follows a moving character, throws particles backwards, and flares up every few seconds

**Add a `ChunkFollowEmitter` component to a GameObject and give it a Chunk Spec.**

It tracks a Transform every frame, works out which way that thing is travelling, sprays **Particle Splash** particles in the *opposite* direction (so the trail falls behind it), and fires a **Pyre Spawn** burst on a repeating interval at the tracked position. Its inspector tells you, in plain state on the controls themselves, when it will do nothing and why — no spec assigned, or a spec with neither of those two sections switched on.

**One thing worth knowing:** for the spray to actually aim backwards, the Particle Splash section needs its **"Follow Burst Direction"** switch on. If it is off, the follow emitter's inspector says so explicitly rather than letting you wonder why the spray points the wrong way — it deliberately does not reach into the other section and change your setting for you.

## What else is new

- **Particle Splash** — a shower of 1–3px particles sampled from a sprite's *own colours*, sprayed out of that sprite's own footprint. Usable entirely on its own; it does not need fragments, explosions, layers or a timeline.
- **Timeline** — schedule *when* each switched-on section fires relative to the start of the burst, and drop two kinds of marker along the way: a **Code Event** (a name your game code can listen for) and a **Zound Event** (a sound, picked from the Zounds browser, never typed). Markers sit on a track you can see, with a time ruler.
- **Preview in Mirage** — a button at the bottom of the Chunks window that opens your recipe in Mirage without saving anything. Because an explosion is a one-shot and Mirage is built for things that loop, the Mirage HUD now carries a **Replay burst** button, plus an opt-in auto-replay on an interval (off by default — a continuously re-exploding preview is unreadable). A Chunk Spec can also now be **added to an existing Mirage view** alongside a character or another effect.

## What Chunks is for

Chunks is the "flying debris" tool. When something explodes, breaks, or gets hit, Chunks throws a swarm of pieces outward with realistic-ish physics (gravity, drag, spin, bounce). It is the runtime companion to Pyre: Pyre bakes the explosion's sprite-sheet FLASH, Chunks throws the physical BITS. They do not have to be used together — Chunks works standalone.

A "burst" is one event: you author a **Chunk Spec** (an asset — a recipe) and fire it at a world position, either from code (`Chunks.Burst(position, spec)`) or by putting a **`ChunkEmitter`** component on something and calling its `Burst()`. Nothing needs to be pooled or destroyed by hand.

The original four debris looks all still work exactly as before: **procedural pixel squares** (zero setup), **your own sprites**, **sampled pseudo-3D debris** cut from the exploding object's own art and tumbled with a squash-and-shade trick, and **animated content** where every flung piece plays a Pyre effect or a Launimator reel. So do the optional **trail** puffs and the optional cheap **hit detection**.

## How well-developed the UI is

It is a real modern editor window, not a fallback inspector: browse/create/duplicate/rename/delete a Chunk Spec for free, fields grouped into labelled sections, and **every dial is its own undo step** rather than one blanket undo for the whole session.

Three sections draw live pictures rather than just numbers — the original slicing preview for sampled debris, the new fragment-cut preview, and the new formation-layout preview. Sections that are not in use collapse to a single title line.

## How to try it (recommended first steps)

1. Open `Assets/Demos/ChunksDemo/ChunksDemo.unity` and press Play. Left-click for a radial pop; **Space** for a directional burst that arcs and settles on the floor; **T** for the sampled pseudo-3D tumble; and — new — **C** for a **composed** burst that cuts the demo shape into three big pieces *and* sprays a splash of that shape's own colours behind them, on two named layers. Pressing C is the fastest way to see what Chunks 2.0 actually adds.
2. Open `Laubrary → Chunks` and select `Composed` (in the demo folder) to see how that recipe is put together — it is two sections switched on and a two-entry Layer Stack, nothing more.
3. For your own game: create a Chunk Spec, switch on the sections you want, then either call `Chunks.Burst(worldPosition, yourSpec)` or drop a `ChunkEmitter` on a GameObject.

## Honest status

**Verified working:** everything compiles; a burst with every section switched on runs without error and produces the right pieces on the right layers; the fragment cut and colour sampling produce real output from a real sprite; timeline Code and Zound markers both fire with the right names; the sound bridge to Zounds is wired. Every section of the window has been opened and looked at.

**Not yet verified by a human:** nobody has sat in front of Play mode and watched these effects fly, dragged a layer slot with the mouse, or dragged a timeline marker. The behaviour is verified structurally and at the API level, and the window has been seen laid out — but "does it *look* good in motion" is a judgement only you can make, and step 1 above is the way to make it.

**One known rough edge:** on/off switches in this window stretch to the full width of the pane, which reads oddly on a wide window. That is pre-existing behaviour shared with the rest of the window (the original "Face Velocity" and "Rest On Floor" switches do it too), not something the new sections introduced — but it is worth a tidy-up pass across the whole window rather than in one section.
