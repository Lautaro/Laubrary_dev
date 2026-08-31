# Pyre — the Grand Unified Generator

*A full view of what Pyre generates, how the pieces fit, and what to keep, fuse, finish or cut. Written 2026-08-30 for T-0098. Deliberately contains no code and no code names — everything below is described the way the tool presents itself to a person using it.*

---

## 1. TL;DR — the whole thing in one page

**What Pyre is now.** Pyre started as "a shape you can put on a layer" and has quietly become **a library of about 29 different picture-making techniques**, each with its own way of thinking, its own dials, and its own opinion about which of Pyre's shared features it will bother to respect. Calling them *generators* instead of *shapes* is correct and overdue — most of them are not shapes in any meaningful sense. A Disc is a shape. A flamethrower with 60 dials that fills the whole canvas by itself is not.

**The single thing that is actually wrong.** There is no one place that says *"this layer is generating with X."* There are **four unrelated ways** a layer decides what to draw, and only one of them lives in the picker. The other three are hidden: a plug-in effect quietly overrules the picker; a "melt into a blob" mode throws the picked shape away entirely; and a setting buried in the mask controls makes the layer stop drawing its shape and draw a mask instead. So a user can pick "Star", and get circles, or get an explosion, or get nothing. That is the root of nearly every confusion in your list.

**Why some effects do nothing.** It is not arbitrary and it is not per-generator-per-effect. There are only **five points** in the pipeline where an effect can attach, and a generator supports an effect exactly when that generator bothers to stop at that point. Roughly **half the generators skip the "bend the shape" stop**, so all seventeen warping effects silently do nothing on them. The two fire simulations skip almost every stop. One generator draws nothing at all. **Nothing in the interface tells you any of this** — the same effect list appears under every generator. Publishing **four small facts per generator** ("can be bent / can be recoloured / draws pixels at all / what the swarm does to it") closes the entire gap, and would let the interface grey out the effects that can't act, with a reason.

**Masks.** There are two mask systems and they are not redundant — but they are named almost identically and they read **different things**. One reads *how solid* a layer is; the other reads *how bright* it is. A dark, fully-solid shape is a perfect mask in the first and a useless mask in the second. That alone probably accounts for most of "the mask system seems complex". Every generator can be a mask and be masked, with one exception (the 3D playback one, which draws nothing).

**Simulation.** Only two generators are simulations — the two fire ones. A simulation *remembers* the previous frame, which buys you fire that genuinely spreads and dies down, and costs you: no bending effects, no recolouring effects, essentially no swarm, and scrubbing backwards silently re-runs the whole clip. **Verdict: worth keeping, worth labelling loudly, and it should never be the default answer to "I want fire"** — there are nine other hot-thing generators that can jump straight to any frame.

**"Fuse."** The old per-shape fuse control you remember is **already gone** — it was deleted in the rename and replaced by the layer's melt-into-a-blob mode. But the word now means three different things in the tool at once, which is why it feels unresolved. That's a naming fix, not a feature fix.

**Hand-drawn effects.** Four of them (smudge strokes, swirl points, a vortex field, and pinned warp points). They all still render correctly, but **the drawing surface they were authored in no longer exists** — it was deleted in the rename. Both add-menus block all four with a message pointing at a window that isn't there. Two of them are used by real saved effects. Keep two, cut two.

**The uncomfortable number.** Across **every authored Pyre effect on this machine** — 123 saved files, ~305 layers, though three of the four projects are clones of this one, so the real body of work is about **32 distinct effects and 79 layers** — **78% of all layers are a plain Disc.** Ten of the seventeen built-in generators have **never had their value set once**, and an eleventh has only ever been overridden. Four of the nine big plug-in generators have never been used once. **The mask system has never actually run**: its master switch is off on all 305 layers, so even the three that look like mask sources are inert. The library has grown roughly ten times faster than it has been used. That isn't a failure — it's exactly the "collection of techniques" feeling you described — but it means **the highest-value work here is not more generators; it's making the ones that exist findable, predictable and combinable.**

**3D Shaper.** It should join Pyre, but **not as a generator** — as a **shape source**. Its layers and Pyre's layers are two different things wearing the same word: Shaper's layers combine *silhouettes before any colour exists*, Pyre's combine *finished coloured pictures*. Flattening one into the other would be a mistake. One Pyre layer should hold **one whole Shaper model**, edited in Shaper, not a nested sub-editor and not one-Pyre-layer-per-Shaper-layer. **Blocking fact: Shaper cannot export anything at all today.** Nothing can be integrated until that exists.

**The big idea, and the one I'd actually build.** Almost every generator already computes a *silhouette plus a scalar* (heat, density, height) internally, and then immediately squashes it through a colour ramp in the same breath. **The seam you want already exists inside them — they just don't expose it.** If a generator's output were standardised as a **substance** (how much is here, how high it is, how hot it is) and *colour* became a second, separate, independently-animating **material** pass, you would get exactly the fireball-shape-with-churning-plasma-surface you described. And the same change would collapse three of the hidden generators into "materials", let a Box wear a plasma surface, let Shaper silhouettes wear Pyre materials, and give Shaper's own material system somewhere to converge on instead of growing a second one. **This is the recommendation of this document.**

---

## 2. What a generator is — the proposed standard

Today, "what does this layer draw?" is answered by four mechanisms in a priority order nobody can see. **Standardise on one field: the Generator.** Everything that decides what a layer draws becomes an entry in one picker — including the three that are currently hidden as modes and settings. Nothing is lost; three invisible techniques become visible.

Then every generator publishes a short, honest **contract** — a handful of promises shown as small badges on the generator itself:

| Promise | What it answers | Why it matters |
|---|---|---|
| **Swarm role** | Does the swarm *multiply* me, *place* me, or is it *ignored*? | Explains "the swarm does nothing on my flamethrower" and why melt-into-a-blob is unavailable on half the list |
| **Bendable** | Do the warping effects reach me? | Explains 15 effects silently doing nothing on 8 generators |
| **Recolourable** | Do the per-pixel colour effects reach me? | Explains 11 effects doing nothing on the fire ones |
| **Draws pixels** | Do I produce an image at all? | One generator currently answers *no* and nothing says so |
| **Colour source** | Do I use the layer's Fill, or bring my own? | Seven of the nine big plug-ins ignore the Fill entirely |
| **Time behaviour** | *Instant* (any frame directly), *Whole-clip* (one fitting pass first), *Sequential* (must be played from the start), *Looping* (seamlessly repeats by construction) | This is the honest version of the "simulation" question |
| **Needs an asset** | Must I hand it a sprite, a font, a model? | These can fail, and currently fail silently by falling back to a plain Disc |

Two consequences worth stating plainly. **First**, the effect list should grey out what cannot act on the current generator, with the reason written on the row — the interface already knows how to grey a row with a reason, it just isn't being asked the right question. **Second**, "Looping" is a real, valuable property that only one family currently has and nobody advertises: those generators repeat seamlessly no matter what frame count you set. That belongs on a badge.

---

## 3. The four families

Derived from what the generators actually *do*, not from what they look like.

### Family 1 — **Stamps**: a small shape drawn once per particle
*Ask yourself: "if I turn the swarm on, do I get lots of little copies?"*

Disc, Crescent, Star, Polygon, Streak, Sparkle, Sprite, Text — and the lit solids Gem, Box, Pyramid, Can, Orb, Ring.

They share one honest contract: size, colour from the Fill, spin, travel, lifetime, and both kinds of per-pixel effect. Worth keeping the existing split visible inside the family: **flat** stamps get edge softness and an outline; **lit solids** give those up in exchange for real 3D rotation and lighting.

### Family 2 — **Scenes**: one whole effect that fills the canvas by itself
*Ask yourself: "is one of these already a whole explosion?"*

The nine big plug-ins: Inferno, Fork Blast, Orb, Torch, Arc Burst, Plasma Bloom, Jet, Radial Jet, Explosive Jet.

They are a different animal and should be presented as one. They own their own scale and placement, so **they cannot read the layer's size or spin at all** — not by design, but because the handover they receive simply doesn't include those two values. **That is a cheap fix, not an inherent limit**: adding size to what a plug-in is handed would close the single largest "ignores the shared controls" gap in Pyre without breaking any of the nine. The swarm means "ignite one of me at each particle", not "make N copies". They **cannot melt into a blob** — yet the control for it is still offered on them, doing nothing. Seven of nine bring their own colours and ignore the Fill. Between them they carry the overwhelming majority of Pyre's dials.

### Family 3 — **Masses**: the swarm read as one continuous substance
*Ask yourself: "do I want my particles to melt together into one thing?"*

Melt-into-a-blob, height-relief, and the mask-fed height field.

These are **real generators wearing a disguise**. They are currently a radio button and a mask setting, which is precisely why you can't find them and why they've been used on a total of 20 layers ever — against 305. They discard whichever shape you picked — a particle in this family is always a blob. Promote them into the picker, say plainly that they replace the shape, and they stop being confusing.

### Family 4 — **Sims**: a picture that remembers the previous frame
*Ask yourself: "does scrubbing backwards make it re-think?"*

Fire and Fireball. Exactly two members, and everything strange about them follows from the one property they have and nothing else does.

**3D Playback belongs to none of these**, which is itself the finding — it is a viewer wearing a generator's clothes. See §10.

**Two badges cut across the families rather than forming their own:** *Imported* (Sprite, Text, 3D Playback — needs an asset, and can fail) and *Preview-only* (3D Playback — produces no image outside the editor preview).

---

## 4. Is "simulation" useful? — a direct answer

**Yes, but narrowly, and it should stop being invisible.**

A simulation keeps a grid of heat and fuel between frames. That gets you something no instant generator can fake: fire that genuinely propagates, feeds on what it burnt, and dies down unevenly. It is the difference between fire and *a picture of fire*.

The costs are large and currently unstated:

- **Frame 9 requires frames 0–8.** Scrubbing backwards, jumping, or touching any dial silently re-runs the clip from the start. It works, it's deterministic, and it gets slower the further in you scrub.
- **Warping effects don't reach them. Neither do the per-pixel colour effects.** Only whole-image post effects work.
- They ignore size, spin, travel and the whole position section.
- **Fire can use the swarm as multiple emitters; Fireball cannot.** The interface does say so, to its credit — but it's an odd asymmetry within a two-member family.
- They force the whole effect to render single-threaded.

**Verdict: keep both, label them "Sequential" on the badge strip, and give Fireball the swarm-emitter option Fire already has** so the family is internally consistent. But recognise that of the roughly eleven generators that make hot glowing things, only two are simulations — and the other nine can jump to any frame instantly, take every effect, and swarm properly. **"Simulation" should be the specialist choice, never the obvious one.** The interface currently gives no hint of that ordering.

One further distinction worth naming, because it is a third thing and is currently mislabelled as neither: **Plasma Bloom needs a whole-clip fitting pass.** You can still ask for frame 9 first — but doing so solves the entire clip. That is not a simulation and not quite instant. It's the "Whole-clip" badge, and it has exactly one member.

---

## 5. The mask system, in plain words

You called it complex. It is — but the complexity is **almost entirely naming**, not design.

**There are two mask systems, doing genuinely different jobs.**

**The cut-out mask.** An invisible layer deposits *how solid it is* into one of four numbered slots. Layers above can be clipped by a slot. It can only ever *remove*. Four slots, combinable (union, add, carve), invertible. This is a stencil.

**The light mask.** An invisible layer deposits *how bright it is*, and that becomes a mask which can impose any combination of six effects on the layers above: fade them, darken them, drain their colour, rotate their hue, blur them, or push their pixels along the mask's own slope. One mask at a time, with a reach (next layer only, or everything above).

**They overlap on exactly one thing** — both can fade a layer. Everything else is exclusive. They are not redundant and neither should go.

**The trap that makes it feel unpredictable.** They read different things. A dark smoke cloud is a *perfect* cut-out mask and a *useless* light mask, because it's solid but not bright. Nothing warns you, and the failure looks exactly like "the mask doesn't work with this shape."

**The fix is a rename, and it's cheap.** Call them what they read: **Cut-out mask (solidity)** and **Light mask (brightness)**. Right now they share one box, one master switch, and two settings whose names differ by a single word while meaning entirely different things. Renaming these two things is probably the single highest ratio of clarity-gained to work-done in this whole document.

**Which generators can take part?** Essentially **all of them, on both sides.** Masks are harvested from a layer's finished picture and never ask what drew it. Two exceptions: 3D Playback produces no picture, so it makes an empty mask and has nothing to mask; and the mask-fed height field **can be masked but can never be a mask source**, because a layer is a writer or a reader, never both. That is a real answer to your question, and a reassuring one: **the mask system is not generator-dependent at all.** Your instinct that "some generators can be part of it and some can't" was pointing at the brightness-vs-solidity trap, not at a compatibility limit.

**And a third thing shares the plumbing.** A layer can be told to *stop drawing its own shape* and instead render an accumulated mask as a lit relief surface. That's not a mask — that's a generator (Family 3), reached from the mask controls. It belongs in the picker.

**One number worth sitting with:** across every authored Pyre effect on this machine, **the mask system's master switch is off on every single one of the 305 layers.** Three layers are set up as mask sources, but with the master switch off they do nothing — and all three are the same layer, in three clone copies of the same project. The most intricate subsystem in Pyre has, in practice, **never actually run in a saved effect.** That is the strongest argument in this document for simplifying and renaming it rather than extending it.

---

## 6. Why some effects do nothing — and how to make it predictable

**The rule, in one sentence:** there are five points in the pipeline where an effect can attach, and a generator supports an effect exactly when its drawing routine stops at that point.

The five points: **bend** (warp the shape as it's being drawn), **recolour** (change each lit pixel), **post** (process the finished picture), **edge** (perturb the outline), and **simulate** (a stateful grid in its own dedicated slot).

**What this means in practice:**

- **Post effects work on everything** that produces a picture. If it drew something, you can bloom it, outline it, shadow it, kaleidoscope it, melt it.
- **Bend effects — all seventeen of them — silently do nothing on eight generators**: Text, Gem, Box, Pyramid, Can, Orb, Ring, and the height-relief mass. That's eight of the seventeen built-in generators, and it climbs to eleven once you add the two fire sims and 3D Playback, which skip this stop too.
- **Recolour effects — all twelve — fail on four generators**: both fire simulations, 3D Playback, and the mask-fed height field.
- **3D Playback takes nothing**, because it produces nothing.
- **The big plug-in scenes take both kinds**, but their bending is applied to the finished picture afterwards rather than during drawing — which is not always visually equivalent, and one of them opts out and does its own.

**Something important that is *not* true.** I expected to find that some effects need special data the generator has to write — a depth map, a heat channel — and that this was the source of the incompatibility. **It isn't. No effect anywhere reads anything a generator wrote.** The ones that look like they need depth (the relighting effect, the auxiliary-map machinery) **invent** their depth from the finished picture's own pixels. That's good news: it means the compatibility problem is purely about *where an effect attaches*, and is therefore completely describable by the four badges in §2.

**A second axis, now confirmed.** Four post effects deliberately draw *outside* the silhouette — bloom, outline, drop shadow, chromatic aberration. The standalone effect stack grows its canvas to make room for them. **Pyre does not** — it never asks how much room an effect needs. So those four are **quietly clipped in Pyre and not in a standalone stack**: the same effect, with the same settings, genuinely produces a different picture depending on where you use it. That is a second, independent "works here but not there", and it has nothing to do with which generator you picked.

**A third axis, worth knowing before you tune anything.** "Bend" means two different things in the two places it appears. Inside Pyre it warps the shape *as it is drawn*, centred on that shape. In a standalone effect stack it resamples the *whole finished canvas*, centred on the canvas. Same effect, same dials, different pivot and different scale — **an effect tuned in one will not land the same way in the other.**

---

## 7. The three "fuse"s, and the hand-drawn effects

### Fuse

The old per-shape fuse toggle you remember was real, it lived in the layer view, it only worked on discs — **and it is already gone**, deleted in the rename with a documented migration onto the layer's melt-into-a-blob mode. **There is nothing left to retire.**

What survives is two different things that both say "fuse", and they are **not** redundant:

- **Melt-into-a-blob (the layer mode).** Works on the particles *before* they're drawn: reads the whole cloud as one field and produces a true merged silhouette with real necks between blobs, re-shaded through the Fill. Exact, but confined to **one layer's swarm**, and only available on plain stamp generators with the swarm on.
- **Blob melt (the effect).** Works on the *finished picture*: blur, threshold, re-solidify. Cruder — but it works on **anything**, and as a whole-frame effect it can fuse **across layers**, welding text to a sprite to a fire. The layer mode structurally cannot do that.

**Keep both. Rename the effect to "Blob melt" and stop calling it fuse.** Three things named fuse plus a set of dials with the same prefix is the entire reason this felt unresolved.

### The hand-drawn effects

Four of them, all warp effects driven by something you draw or place in the preview: **Smudge** (painted strokes that drag pixels along their tangent), **Swirl** (click-placed vortex points), **Vortex field** (the same points, progress-driven), and **Pin warp** (placed pins you keyframe by scrubbing and dragging).

**They all still render correctly. The surface you drew them on no longer exists** — it belonged to the old window that was deleted in the rename. Both add-menus block all four with a message telling you to go and author them in a window that isn't there any more.

**Two of them are used by real saved effects:** Smudge (two) and Swirl (two). Vortex field and Pin warp are used by nothing, anywhere.

**Verdicts:**

- **Smudge — keep and rebuild the drawing.** It's the only effect in Pyre that can smear a picture along a path you choose, two saved effects depend on it, and its input is the simplest possible: drag records points, release ends the stroke.
- **Swirl — keep and rebuild the drawing.** Two saved effects depend on it, and its input is a single click to place a point.
- **Vortex field — cull.** Nothing uses it and it duplicates Swirl's authoring surface. If you want to keep it, it must live behind the *same* editor as Swirl, not a second one.
- **Pin warp — cull.** Nothing uses it, it needs by far the most elaborate authoring (place, scrub, drag, keyframe), **and it is additionally broken in a way nobody noticed**: the hook that tells it which frame it's on is never called any more, so even a fully authored pin would hold its first position for the entire clip. Rebuilding the hardest interface in the set, for an effect nothing uses, that would then still not animate, is the worst trade in this document.
- **Regardless: fix the four blocked-effect messages.** They currently send the user to a deleted window, which is worse than saying nothing.

---

## 8. 3D Shaper — what it is, and how it should join

**What it is.** A local browser app for building stylised 2.5D pixel-art models: draw a 2D silhouette, combine several of them with boolean operations, give the result a material, extrude it for depth, light it, and optionally animate parameters on a timeline. Eight 2D primitives, seven extrusion profiles, four ways of combining silhouettes (add, soft add, subtract, soft subtract), seven material surfaces, twelve patterns. It's substantially finished against its own contract.

**The blocking fact.** **Shaper cannot export anything.** No image, no sequence, no sprite sheet, no file of any kind. Its animation exists only as a live preview in a browser tab. **Until export exists, no integration of any shape is possible** — and that's a Shaper task, not a Pyre task. It is bounded work (the renderer already runs once per displayed frame; it needs wrapping in a capture loop), but it must come first.

**Where it genuinely beats Pyre.** Arbitrary composable silhouettes with real holes and soft blending. Pyre has **no equivalent at all** — its solids are six fixed primitives with no way to subtract or blend one into another. Shaper's occlusion-aware outlines are also something Pyre can't do.

**Where Pyre genuinely beats it.** Real 3D rotation of real geometry with proper back-face culling; and everything a Pyre layer gets for free — timeline, swarm, masks, effect stack, and an actual runtime that plays the result in a game.

**Where they duplicate.** Both have independently built "make a flat canvas read as a lit solid". Two lighting pipelines answering nearly the same question. That shouldn't be permanent.

### The answer to your nested-layers question

**One Pyre layer should hold one whole Shaper model. Not a nested sub-editor, and not one Pyre layer per Shaper layer.**

The reason is the word "layer" meaning two different things:

- A **Shaper layer** is one step in a *colourless, timeless* silhouette stack. Its only job is to add to or cut from an outline. Colour and depth attach *once*, after the whole stack has resolved.
- A **Pyre layer** is a *fully coloured, fully timed* render citizen with its own lifetime, swarm, mask role and effect stack, composited onto the frame.

They sit at **different depths of the same idea**, and flattening them together goes wrong in both directions. Mapping Shaper layers 1:1 onto Pyre layers would mean pressing Pyre's mask system into doing boolean subtraction — which it can only approximate, and **cannot do softly at all**, since soft blending has no equivalent in Pyre's masks. It would also hand every silhouette fragment its own swarm and effect stack, which is far more machinery than "one shape" should carry. And a nested sub-editor inside a Pyre layer means two differently-shaped layer lists at two nesting depths in one window, plus re-hosting or re-implementing Shaper's whole renderer.

**So: a Shaper model becomes a named, reusable Silhouette asset.** You author it in Shaper, where its own internal layer stack belongs and already works. Pyre picks it the way it picks a sprite today. The internal stack stays internal, exactly as it should.

### The refinement that makes this much better than "import a picture"

If Shaper exports only a *finished picture*, a Shaper layer in Pyre is just a fancy sprite — and Pyre already has a cautionary example of exactly that: **3D Playback**, an import-external-3D-content generator that was built, given a full editor preview, and then never given a runtime path at all. It draws nothing. Don't build a second one.

If instead Shaper exports a **substance** — coverage, height, and which material each region wants — then a Pyre layer wearing a Shaper silhouette still gets **Pyre's** materials, lighting, swarm, masks and effects. That is the difference between importing a picture and importing a *shape*. It also means Shaper never needs to grow its material system any further; it converges on Pyre's. Which brings us to the last section, and the only genuinely large idea here.

---

## 9. The shape / material split — the recommendation

**Your instinct is right, and the seam already exists — it's just not exposed.**

Look at what almost every generator actually does internally: it computes *where the stuff is and how intense it is* — a coverage field with a heat, density or height value — and then, **in the same breath**, pushes that scalar through a colour ramp and emits a finished pixel. The fire sims do it. The blob fields do it. The relief generators do it. The big plug-in scenes do it (one of them is *literally* an intensity field read through a ramp — the field **is** the picture). The lit solids do it too, computing a surface normal and a light angle and immediately consuming both.

**Every one of those is a shape stage and a material stage that were never allowed to be two things.**

### The proposal

Standardise what a generator hands over. Instead of finished pixels, a generator produces a **substance**:

- **coverage** — how much of this pixel is occupied
- **height / depth** — how far toward the viewer
- **energy** — one or two scalars the generator defines (heat, density, charge)
- **normal** — optional, for the generators that already compute one

Then **colour becomes a second, separate pass**: a **material**, chosen independently, animating on **its own clock**, reading the substance.

**A fireball shape wearing a churning-plasma material. Exactly what you described.**

### Why this is the right change and not just a nice one

- **It's compatible by construction.** Today's behaviour is one trivial material — "read the energy scalar through the layer's Fill". Every existing effect keeps rendering identically. Nothing has to be migrated to start.
- **It absorbs three of the hidden generators.** Height-relief, the mask-fed height field, and the melt-into-a-blob shading all stop being separate techniques and become **materials that read height**. Family 3 mostly dissolves into Family 1 plus a material choice.
- **It uncrosses shape from look.** A Box could wear churning plasma. A fire cloud could wear brushed metal. Right now every generator's look is welded to its geometry, which is why you need eleven separate hot-glowing generators to get eleven hot-glowing looks.
- **It gives Shaper somewhere to land.** Shaper already separates shape from material as *vocabulary*; this makes it real, and lets a Shaper silhouette wear a Pyre material.
- **The imported effect algorithms already assume it.** The library of interior-fill and edge treatments that Shaper's own investigation wants to port takes exactly two inputs: a coverage mask and a distance-from-edge map. That's a **substance**. Two projects independently arrived at the same seam — that's the strongest evidence it's a real one.
- **It's the honest fix for "what will this effect actually do?"** A material declares what it reads; a generator declares what it provides. Compatibility becomes a statement about data, not a table of special cases.

### What it costs, honestly

The generators must be split internally into resolve-substance and resolve-colour. For the field-based ones (the fires, the blobs, the reliefs, most of the plug-in scenes) this is close to free — they already compute the field as a distinct step. For the lit solids it's more real work, because their lighting is computed and consumed in the same place. **And one design decision cannot be deferred: do shape and material become two fields on one layer, or two linked layers?** Two fields is closer to how Pyre works today; two layers is closer to Shaper. **That's a question for you, not something to infer** — see §12.

---

## 10. The verdict table

**Keep, as-is.** Disc (the workhorse and the universal fallback — 78% of all authored layers), Crescent, Sparkle, Streak, Sprite, Text, **Ring**, **Orb (the built-in sphere)**, Fire, Fireball, Inferno, Orb (the plug-in — renamed, see below), Torch, Arc Burst, Plasma Bloom, both mask systems, melt-into-a-blob, blob melt, Smudge, Swirl.

Ring and the built-in Orb are worth calling out explicitly because they are easy to lose in a cull: neither has ever been used in a saved effect, but both are distinctive analytic shapes that nothing else in the library replaces, and both are cheap. **Keep them, and make them findable** — their problem is discoverability, not value.

**Fuse into one generator each.**

| Fuse these | Into | Why |
|---|---|---|
| Star + Polygon | one **N-gon** with a "valley depth" of zero meaning polygon | Same maths, one parameter apart |
| Gem + Box + Pyramid + Can | one **Solid** with a shape choice | Four buttons, one drawing routine, three different readings of the same dials |
| Jet + Radial Jet + Explosive Jet | one **Jet** with a mode | One engine, three sets of overrides, 23 presets between them |
| Height relief + mask-fed height field | one **Height field** with a source choice (swarm or mask) | Same relief lighting, different feed |

**Cull.**

- **Fork Blast** — its own successor's documentation says it *replaces* it and closes a 47-row list of differences. **Nothing in this project uses it any more**, but two saved effects in an older sibling copy of the project still carry it, so removing it needs a migration note rather than a straight delete.
- **Vortex field** and **Pin warp** — unused, duplicative, and Pin warp is animation-dead (§7).
- **The dead outline-perturbing effect** — reachable from nowhere, authorable from nowhere.
- **The unreachable auxiliary-map gate** — fully built, fully documented, and not attached to anything. Wire it up or delete it; leaving 150 lines of high-quality dead code is the worst option. **Your call — see §12.**
- **The two empty leftover folders** from the rename.
- **Two declared-but-never-reachable generator kinds** in the plug-in system — placeholders for "one per particle" and "keeps state" that nothing has ever dispatched. Dead surface that makes the plug-in contract look more capable than it is.

**Decide, then finish or cut: 3D Playback.** It has a substantial editor preview and produces **no image whatsoever** outside it. **No saved effect anywhere uses it.** It sits in the shape picker next to fifteen real generators with nothing marking it. With Shaper arriving as the real 3D story, its remaining value is as a *reference viewer* — something you look at to judge how a 3D effect might read as pixel art. **Recommendation: take it out of the generator picker and make it an honest reference tool, or cut it.** Leaving a picker entry that draws nothing is the one thing not to do.

**Rename.**

- **"Shape" → "Generator"** throughout. You're right, and everything above follows from it.
- **Two different generators are both called "Orb"**, both selectable from the same list, drawing completely different things. Rename the plug-in one (it's an energy projectile with a wake — "Bolt" or "Projectile").
- **"Fuse"** means three things (§7).
- **The two masks** → "Cut-out mask (solidity)" and "Light mask (brightness)" (§5). Highest clarity per unit of work in the document.
- **Guard the word "layer"** before Shaper arrives (§8).

**Develop.**

- The **generator contract badges**, and greying out effects that can't act with the reason written on the row (§2).
- Promote the three hidden **Mass** generators into the picker (§3).
- Give **Fireball** the swarm-emitter option Fire already has.
- **Hand the plug-in scenes the layer's size** (§3). One small change, closes the biggest shared-controls gap in the tool, breaks nothing.
- **Make Pyre grow its canvas for the effects that draw outside the silhouette** (§6) — otherwise four effects keep producing a different picture in Pyre than they do in a standalone stack.
- The **shape/material split** (§9) — the big one.
- **Shaper export** (§8) — a Shaper-side prerequisite for everything Shaper-related.
- Rebuild the drawing surface for **Smudge** and **Swirl**.

**Net effect, counted:** 29 techniques today (17 built-in, 9 plug-in scenes, 3 hidden masses). The four fusions remove seven picker entries without removing a single capability; retiring Fork Blast and taking 3D Playback out of the picker removes two more. **29 → 20, three previously-hidden techniques become visible, and nothing a user could actually do is lost.**

---

## 11. Live problems found on the way

Not part of the design question, but real and worth logging:

1. **An effect that is offered and does nothing.** The pixel-fluid simulation appears in the standalone effect stack's add-menu and that stack has no handling for it at all. Silent no-op, reachable today.
2. **Pin warp never learns which frame it's on.** Its frame hook lost its caller in the rename. Keyframed pins hold frame one forever.
3. **The melt-into-a-blob control is offered where it cannot act, with no warning** — on every plug-in scene, on 3D Playback, on any layer with the swarm off, and on Fire when its swarm-emitter option is on. (The two fire sims without emitters are handled correctly: the control isn't shown at all.)
4. **Two retired effect types are still sitting in saved files and no longer exist in the tool.** Two modifiers folded into others back in August still appear in three saved effects in a sibling project; they now load as nothing. This is the live precedent for the Fork Blast migration warning in §10 — retiring something is not finished until the saved files that reference it are migrated.
5. **While authoring a 3D Playback layer, the preview replaces the entire composited canvas** — you can't see your other layers or your masks at all.
6. **Four blocked-effect messages point at a deleted window** (§7).
7. **Several code comments cite a file that no longer exists** as proof of their claims, and four more describe behaviour that has since changed. The claims mostly turned out to still be true, but they're no longer checkable — worth a cleanup pass whatever else happens.
8. **Pyre clips the four effects that draw outside the silhouette** (§6). Confirmed: Pyre never asks an effect how much room it needs, so bloom, outline, drop shadow and chromatic aberration are cut off at the canvas edge here but not in a standalone stack.
9. **Each project also carries a saved layer library on the pre-rename schema** — thirteen more authored layers that the census above does not include, and that any generator removal would also touch.

---

## 12. Questions only you can answer

1. **Shape and material: two fields on one layer, or two linked layers?** (§9) The first is closer to Pyre today; the second is closer to Shaper and composes better. This decision shapes everything downstream and shouldn't be guessed.
2. **Should melt-into-a-blob keep discarding your chosen shape?** Today a Star with melt on gives you circles. Is that permanent, or should it fuse the actual shapes?
3. **3D Playback: finish the runtime, demote it to a reference viewer, or cut it?** (§10)
4. **The unreachable auxiliary-map gate: wire it up or delete it?** It's finished, documented, and attached to nothing.
5. **Does the enormous plug-in scene library match how you actually want to work?** Four of the nine have never been used, and they carry the majority of Pyre's dials. That's not an argument to delete them — but it might be an argument that they need presets and a browser far more than they need more dials.
6. **Do you want Shaper's boolean silhouette composition inside Pyre eventually, or is Shaper a one-way asset pipeline forever?** (§8) Both are defensible; they lead to different amounts of work.

---

## 13. Confidence and limits

**What is measured and solid:** every claim about which generator ignores what, which effects attach where, what the two mask systems read, what the three fuses are, the state of the four hand-drawn effects, and the usage statistics in §1 and §5. Those were counted directly from every saved Pyre effect on this machine — 123 files, ~305 layers, across four projects — then independently recounted by a second pass. **Read the ratios, not the totals:** three of the four projects are clones of this one, so the real body of authored work is about 32 distinct effects and 79 layers, counted roughly four times. The proportions hold either way; the absolute numbers are inflated about fourfold. A further 13 authored layers sit in per-project layer libraries on the older schema and are excluded entirely.

**What is inferred but strong:** the "silently does nothing" claims come from reading the drawing routines and finding no attachment point, not from rendering a before-and-after. The inference is solid; a rendered comparison would make it certain.

**What is not verified at all:** **nothing here was checked by looking at Pyre running.** No frame was rendered and no window was opened. Every judgement about *how good a generator looks* is inferred from how much care went into it — dial counts, documentation, whether the authors called it a stub — not from seeing its output. **Before anything is cut on the strength of this document, look at it.** Fork Blast is the closest thing to a conclusive case without looking — its own successor's documentation says it replaces it, and nothing in this project still uses it — but even there, two saved effects in an older sibling copy of the project would need migrating first.

**Also unverified:** whether applying a bend to a plug-in scene after the fact looks the same as bending it during drawing. (The canvas-clipping question that was open in an earlier draft is now settled — see §6.)

**On the process, since it matters for how much weight to give this:** the report was assembled from three independent investigations and then **adversarially checked by a fourth pass** whose only job was to attack it. That pass found five real factual errors in the draft, all corrected above — including one that alleged data corruption that did not exist, and one that would have justified deleting something still in use. The architecture and the recommendations survived; the numbers needed work. Treat the design argument as solid and any individual number as worth re-checking before it justifies a deletion.

---

*Detailed evidence, with file and line citations for every claim above, is in the working notes at `D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0098\` — four files covering the generator inventory, the compatibility rule, the 3D Shaper study, and the verification pass. Those are for whoever implements this; this document is the view.*
