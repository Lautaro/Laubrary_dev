# Chunks — quick manual (as of 2026-09-03)

## TL;DR

Chunks is the tool for "something breaks apart." A recipe is a stack of switches — turn one on and it does one thing (throw debris, cut a picture into pieces, spray colour, spawn an explosion), turn several on and they compose into one burst, on one shared clock, with one preview that actually plays before you press anything in the game. Open it from **Laubrary → Chunks**.

## What Chunks is, in plain terms

An explosion, a shattering, a puff of smoke, a shower of sparks — anything that fires once and comes apart — is authored as a **recipe**: an ordered list of capabilities, each one small and named for what it does. A recipe with one capability is a short, simple thing to look at. A recipe with several is a composed effect: a character fractures into pieces while a colour spray goes off behind them and a ring of small explosions pops in front, all timed against each other on one clock. You never write code for any of this — you build the recipe by adding capabilities and turning their dials, and you fire it with one line from a script, or by dropping the built-in "follow emitter" component on something that moves.

## The nine capabilities, one line each

- **Debris Scatter** — a spray of small pieces: procedural squares (no art needed), your own sprites, pieces cut from a picture's own pixels with a tumbling pseudo-3D look, or an animated thing playing on every piece.
- **Fragment Fracture** — cuts one picture into a handful of large, still-recognisable pieces (a ship splitting into hull sections) and flings them outward.
- **Palette Splash** — a spray of tiny coloured specks pulled from a picture's own colours — a "colour" version of the scatter above.
- **Pyre Blast** — spawns an explosion (a Pyre effect), on its own or a whole pattern of them in a line or a ring, staggered so they fire in an order you can see.
- **Trajectory** — makes what a Pyre Blast spawned actually fly: a launch speed, an arc, gravity, drag.
- **Trail** — leaves a puff behind whatever a Debris Scatter or Fragment Fracture is throwing.
- **Hits** — lets the pieces something else throws deal damage where they land.
- **Layer Plan** — names your own draw-order slots ("smoke", "blast", "fragments") so you decide what draws in front of what, instead of everything landing in whatever order happened to occur.
- **Cues** — moments on the clock that aren't visual: a named signal your game code can listen for, or a sound played from your sound library (picked, never typed).

A recipe only shows the surfaces its own capabilities need. One Debris Scatter and nothing else is a short window with no clock, no layers, no timing section — because there is nothing yet to time against or to stack in front of anything.

## The window, briefly

- **Left side** — the recipe: one card per capability, in the order they're authored. Each card has an On switch, move-up/move-down arrows, and a remove button in its header, and a body full of that capability's own dials. An **Add capability…** button at the bottom offers all nine kinds — the ones that would do nothing yet (a Trajectory with no Pyre Blast to fly, for instance) are greyed out and say why.
- **Right side, top** — the **preview**. This is not a static picture: press Play and it actually moves, at real speed, drawing the recipe's own arcs — gravity, drag, an upward kick, a floor bounce — the same physics the real burst uses, in the same order. Loop it, replay it, or drag the scrub handle to freeze on one instant. Turning a dial while it's playing changes what you see on the very next frame, without restarting.
- **Right side, further down** — the **Timing** section, which only appears once a recipe actually has more than one thing to time against each other. Each timed capability gets its own band on one shared ruler; drag the playhead and every band (and the preview above it) moves together.
- A **Preview in Mirage** button sends a throwaway copy of the whole composed burst into Mirage, Laubrary's shared preview stage, so you can see it at real scale against a real backdrop without saving anything. Chunks only actually moves in Play mode, so press Play once Mirage is open.

## Try it yourself (5 minutes)

The shipped demo lives at `Assets/Demos/ChunksDemo/ChunksDemo.unity`. Open it and press Play, then:

1. **Left-click** anywhere — a plain, snappy scatter of small procedural squares (`Sparks.asset`). One capability, nothing else on.
2. **T**, at the mouse — a "sampled" debris burst: small pieces cut from a shape's own pixels, tumbling with a fake 3D squash-and-shade look, settling on the floor (`SampledDebris.asset`).
3. **C** — the character in the middle fractures into four recognisable pieces while a spray of its own colours goes off behind them, on two named layers — Splash behind, Fragments in front (`Floating Disc Blowup.asset`). This is the composed example: two capabilities plus a Layer Plan, nothing hand-coded.
4. **Space** — a ring of small explosions fires from a wall point, one after another, each one launched outward on its own arc, with a named moment partway through the flight your game code could react to (`Ring Blast.asset`). Three capabilities: a Pyre Blast in a Ring pattern, a Trajectory, and a Cue.

Then open **Laubrary → Chunks** and look at whichever of those recipes interests you — every one of the four is genuinely just a small stack of switches, which is the whole point: you can see everything a recipe does by reading its cards top to bottom, nothing is hidden in a script.

## What's honestly still unverified

- **The immediate (delay-0) point of a staggered Pyre Blast pattern lands somewhere the recipe's own placement formula didn't predict.** Measured directly on the Ring Blast demo, twice, reproducibly: four of the five ring points spawn exactly where the same maths the preview uses says they should; the very first one (the one that fires with no delay at all, inside the same call that starts the others) lands about half a ring-spacing away from its predicted spot. Every OTHER point — including every staggered one — matched bit-for-bit. This wasn't chased to a root cause in this pass; it's flagged here so nobody mistakes "the ring looks a little off" for a report of jitter working as intended.
- **Two of the Pyre effects originally picked for the Ring Blast demo turned out to render nothing at all** (`Directional Grenade Blast 1/2 Plus` — confirmed via a direct frame read, not a guess) and were swapped for two that do render. That's a pre-existing gap in those two imported assets, not a Chunks defect, but it's worth knowing if you reach for either of them elsewhere.
- **The demo camera doesn't naturally frame the wall-mounted burst.** The scene's camera sits at the origin with a tight view; the directional/wall-mounted recipe fires well outside it. This predates this pass (the original wall burst fired from the same spot) — screenshots for this manual had to move the camera temporarily to actually see it. Worth a look if the demo is ever handed to someone cold.
- **A human has not sat down and used the window itself yet** — every check above was done by firing recipes programmatically and reading back what actually spawned, plus reading the window's own preview by eye from captures. Nobody has clicked through Add capability, dragged a card, or scrubbed the timeline by hand.
- **The Mirage handoff was confirmed to open and load the right recipe**, but was not watched actually play (that needs a further round of Play-mode-inside-Mirage verification this pass didn't reach).
