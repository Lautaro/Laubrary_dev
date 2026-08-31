# Chunks — quick manual (as of 2026-08-31)

## TL;DR

Chunks is the "stuff flies apart" tool. You built a much bigger version of it over the last two weeks and then stepped away — the good news is it's not half-built. Every planned piece got finished, and a follow-up pass fixed the confusing parts of the window you flagged before you left. It's ready to sit down and use.

## What Chunks is for, in plain terms

When something explodes, breaks, or gets hit, Chunks throws a bunch of pieces outward with believable physics — gravity, drag, spin, bouncing. You describe a "burst" once (as a saved recipe), and then anything in the game can trigger that recipe at any position, as many times as you like.

It works together with Pyre (the tool that makes the explosion FLASH/fire animation) but doesn't need it — Chunks can throw plain sprites, sampled debris, or nothing but colored squares if you want.

## What you can make it do today

Think of the Chunks window as a stack of switches. Each switch turns on one capability. Leave a switch off and it doesn't show up or cost you anything — a simple "throw some debris" recipe still looks like a simple, short window.

- **Plain debris** — the original stuff: procedural squares, your own sprite pieces, or debris literally cut from the exploding picture's own colors and given a fake tumbling-in-3D look. Still all here, unchanged.
- **Break into big chunks** — cut a picture into a few large, still-recognizable pieces (a ship splitting into three hull sections), instead of confetti.
- **Explosions as part of the burst** — pick one specific explosion, or hand it a whole group and let it choose randomly each time, so the same burst never looks exactly the same twice.
- **Explosions that fly** — instead of going off in place, an explosion can be thrown outward with a speed, arc, gravity and drag, so it reads as burning wreckage tumbling away.
- **Several explosions in a pattern** — arrange multiple explosions in a line or a ring, firing one after another instead of all at once, with a little live picture showing you the arrangement and the firing order before you press Play.
- **Color-particle spray** — a shower of tiny colored specks pulled straight from the exploding picture's own colors.
- **Layering** — name your own layers (like "smoke", "blast", "fragments") and decide which draws in front of which. Before this existed, everything just piled up in whatever order happened to occur.
- **A timeline** — say when each piece of the burst happens relative to the start, and drop markers on it that either fire a signal your game code can react to, or play a sound (picked from your sound library, never typed by hand).
- **Follows a moving thing** — attach a Chunks recipe to something that moves (a character, a missile) and it will chase it, throw a particle trail out behind it, and set off an explosion every few seconds, for as long as it's active. Everything else in Chunks fires once; this is the one thing that keeps going.
- **One-click preview** — a button that shows you the whole recipe playing in the Mirage preview tool without saving anything, with a Replay button since explosions are a one-shot, not a loop.

## How well the window itself works

You flagged this window as confusing before you stepped away — specifically: a section mysteriously labeled "Blast 3" with no obvious way to add or remove one, and pickers that looked like plain text boxes you had to type into. Both of those were real bugs, not you misreading it, and both got fixed:

- Explosions now live in a proper list you can add to, remove from, rename, and drag into a new order — no more mystery numbering.
- Every "pick an asset" control now actually looks like a clickable button with a small picture on it, instead of looking like plain text.
- The window now has a bar of toggles at the top so you can jump straight to the section you want instead of scrolling past everything.
- The most commonly wanted sections (the "compose an explosion" ones) were moved near the top, ahead of the older plain-debris ones.
- Leftover, unused test files sitting in the project were cleaned out, and the ones still in use now show a real picture of what they actually do when you browse them.

## What's honestly still unverified

Everything above has been checked by having the computer drive it and read the result back (it compiles, it runs, the pieces show up where they should). Nobody has actually sat down with a mouse and clicked around in it yet — dragged a layer into a new order by hand, dragged an explosion card to reorder it, or watched a burst play out during actual gameplay. That first hands-on pass is the one thing left, and it's exactly what the "try it" steps below are for.

## Try it right now (5 minutes)

1. Open the Chunks demo scene and press Play.
2. **Click** = a simple radial burst. **Space** = a directional burst that arcs and lands. **T** = the tumbling sampled-debris look. **C** = the "everything at once" demo — a shape breaking into three big pieces while a color spray happens behind them, on two separate layers. Pressing C is the fastest way to see what all the new work actually adds up to.
3. Open the Chunks tool window from the menu and look at the recipe that C uses — you'll see it's really just two switches turned on plus a two-layer stack, not something complicated.
4. To make your own: create a new recipe, flip on whichever switches match what you want, then trigger it from a script or by dropping the built-in emitter component on something in the scene.

## Bottom line

You didn't drop a half-finished feature. Everything that was planned two weeks ago is built, and the rough UI edges you personally caught got a dedicated fix pass. The only genuinely open item is the first real hands-on test drive — which is low-risk and is literally step 1 above.
