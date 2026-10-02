# Pyre feedback: the questions that need your call

*30 September 2026. The bugs from your list are fixed, and all five explosion-study features are built (summary at the end). What's left are four design questions. For each one, this page gives what I measured, what I think, and what I'd build if you say yes. Nothing on this page has been built yet.*

---

## 1. Is Coalesce dead weight?

**What it is.** Coalesce is a render mode for **swarms only**. Instead of drawing each particle, it merges them:
- **Fuse** melts the particles into one blob, shaded from surface to core by the Fill.
- **Ramp** turns them into one lumpy, lit cloud. This is where Mass shading now lives.

**Measured.** With the swarm on, switching Coalesce replaces almost the whole picture (about 3,370 of 3,371 pixels). With the swarm **off**, it changes nothing at all (0 pixels).

**Why it feels like dead weight.** Three honest reasons:
1. Its control stays visible and clickable while the swarm is off, where it does nothing.
2. Its "Fuse" option has the same name as the separate **Fuse (blob melt)** modifier, a different thing that works on any finished picture. It's easy to change one while looking at the other. (That modifier also had a real bug, now fixed.)
3. It ignores the particle's shape completely: a Coalesce swarm of crescents or stars becomes round blobs, with no warning.

**My recommendation.** Keep it, because Ramp is the base for the soot-and-fire look. But:
- grey it out, with a tooltip explaining why, while the swarm is off;
- rename the Coalesce option "Fuse" to **"Merge"**, so the two Fuses stop colliding;
- mention in the tooltip that it ignores the particle shape.

**Your call:** keep and clean up as above, or remove?

---

## 2. Swarm, Transform, Scale, Swarm scale: what's what

**What each one actually does today (measured):**

| Control | What it really does | When it's read |
|---|---|---|
| Transform > **Scale (px)** | Size of the shape that new particles are **placed onto** | Only at the moment each particle appears. It **never moves particles already out.** |
| Transform > Offset / Spawner turn, tilt, roll | Where that placement shape sits, and how it's tilted | Same: only when each particle appears |
| Swarm spin > **Swarm scale** | Grows or shrinks the **whole cloud already out**, around the middle | Every frame, for every particle |
| Swarm spin > Swarm turn, tilt, roll | Spins the **whole cloud already out** | Every frame |
| **Scale by index** | Each particle's own **size**, by its number | Once per particle |
| Snap | Rounds the placement size to steps | At appearance |

**"The scale value doesn't seem to do anything."** I tested your open pyre (the Crescent swarm of 4) on an in-memory copy. Every scale control does change the picture, so nothing is broken. The catch is the first row of the table: your 4 particles all appear in the first half of the animation. So when you scrub to a later frame and drag Scale (px), no particle on screen moves; it only changes where particles *would* appear. That reads exactly as "does nothing." The design is confusing, not broken.

**Proposal (a regroup and rename, no behaviour change):**
- **"Spawn shape"** box: Size, Offset, Rotation, 3D tilt, Snap. Tooltip: *"where NEW particles appear; changing it never moves particles already out."*
- **"Cloud motion"** box: Grow (today's Swarm scale), Spin, 3D turn/tilt. Tooltip: *"moves every particle already out, every frame."* Fling sits next to it, since it's also motion.
- **Particle size by index** moves to sit with the other particle-size controls.
- **Turn/tilt/roll folded away.** All six rotation rows (three in each box) collapse into one line per box, "3D tilt: off". It opens on click and stays open whenever any value is non-zero. This is the same compact-until-used pattern the Border, Edge response and Mass shading boxes now use, so it's already an established Pyre pattern. It saves about six rows in the common case.

**Your call:** yes to the regroup? Are the names right (Spawn shape / Cloud motion / Grow)?

---

## 3. Distribution, spawn order, Area/Path, and the "bloom" you want

**What the current system really does (measured):**
- In Area mode, "Distribution 0" fills a circle with **concentric rings**, not the spiral the old notes describe.
- "Spawn order 0" then walks from each point to its **nearest neighbour**, which goes **round each ring**. That's the "orderly line parallel to the edge" you saw.
- **"Distribution" between 0 and 1 is not a jitter.** It blends each neat point toward an unrelated random point. Mid values pull the cloud toward the middle and wreck the even spacing, which is why they look worse than either end.
- Reverse only picks where the walk starts, and its tooltip wrongly says it flips row and column order on polygons.
- **Line** is a shape that ignores Area/Path, and in Path mode it still shows Travel and Even spacing, neither of which does anything for it.

So the complexity is **partly real** (you do want paths, neat fills and chaotic fills) and **partly accidental**: three controls pull on the same thing, and one of them is mislabelled.

**Proposal: three clear controls instead of Distribution + Reverse + Spawn order:**
- **Layout:** Neat (evenly spaced) / **Bloom** / Random.
- **Order:** Neighbour / **Outward** / **Inward** / Shuffle.
- **Jitter** (0..1): nudges each position randomly *around its neat spot*, keeping the even spacing. This replaces today's misleading blend.

**The bloom you asked for.** It uses the sunflower (golden-angle) arrangement:
- Particle *n* sits at a radius that grows with *n* and is turned 137.5° from the previous one.
- So each new spawn lands a little further out than the last, as far as possible from the ones before it at that distance. That's "as far apart within their distance as possible."
- **Jitter** adds randomness on top. **Order: Outward** gives centre-to-edge, **Inward** edge-to-centre.
- It works with the existing spawn timing, so the bloom grows over the animation.
- On polygons the same rule is scaled to the polygon's edge.
- About 30–60 lines. Old pyres keep their current layout, because the new choices are additions.

Two things you can try today without any new code:
- **A growing ring with chaotic placement:** Path mode, Circle, Spawn travel set to random (Min/Max 0..1), and Scale (px) rising over time.
- **A rough spiral:** Spawn travel rising by about 0.618 per spawn.

**Your call:** build the bloom? Do the regroup (Layout / Order / Jitter) at the same time, or keep the current controls and only add Bloom + Jitter?

---

## 4. Turbulence: Rotation, Offset X/Y, Warp: worth it?

**First, the fix (built).** Turbulence had **no time input at all**, and it re-seeded its pattern from the shape's position. A still shape was frozen; a moving one (every swarm particle) got a completely unrelated pattern every frame. Two new controls:
- **Evolve** makes it change gradually and loop seamlessly. Measured at Evolve 2: 20–36 outline pixels change per frame, with no jump at the loop.
- **Steady** keeps one pattern that rides along with a moving shape.

Old pyres are unchanged.

**Do the other dials earn their place? (measured on a still shape)**
- **Warp:** yes, clearly. It changes the *character*: 0 gives smooth lobes, 1.8 gives a torn, fragmented edge (the outline got about 70% longer).
- **Rotation, Offset X, Offset Y as fixed values:** each just picks *a different pattern of the same character*. They're interchangeable, and effectively a "seed".
- **Animated, they're distinct:** rotation spins the churn, offsets scroll it. Now that Evolve exists, scrolling is rarely what you want for fire.

**My recommendation:**
- Keep Amplitude, Size (today's Zoom), Warp, Evolve and Steady visible.
- Add a **Seed** number, replacing the use of Rotation/Offset as "pick another pattern".
- Fold Rotation and Offset into a collapsed **"Motion"** line (a Drift 2D pad plus a Spin), shown only when used.

**Your call.**

---

## Already built today (for reference)

- **Bugs fixed:**
  - Double-click reset on every Pyre slider (34 of 35 in a live window; the one without is a computed readout).
  - Preview Delay works without CherryFraming.
  - Fuse (blob melt) no longer hazes the whole canvas.
  - Turbulence Evolve and Steady.
- **Explosion study, all five:**
  - Pixel-scale noise fill.
  - Mass shading (soot line, ignition, bleed, dither) on both height tools.
  - Edge response (rim colour, inner glow, edge grain, and Flow, which runs the grain along the outline).
  - Fling for swarm debris.
- Everything new is **off by default**. With it off, old pyres were measured pixel-identical.
- All of it is also in Laubrary Studio.
