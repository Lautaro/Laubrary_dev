# Shaper — the design

**Fourth document on the future of Pyre. T-0098, 2026-08-30.**
Prior documents, all still valid except where this one says otherwise: *GUG — Future of Pyre*, *Shapes / Fills / Borders*, *Where I Stand — the nine answers*, three of the four tabs of the planning document at `D:\UNITY\Laubrary Dev\.agenthq\planning\PyrePlus.json`.

This one is different from the first three. Those were analysis. This is a decision. Where you gave a ruling I have taken it as final and written it into the design rather than re-arguing it. Where you asked a question I answer it here and then build on the answer. Where I went and looked at something new — the two Kiln Tapestry projects, the reference browser app behind 3D Shaper, how Shaper's extrusion actually works — the finding is reported with what it changes.

There is no code anywhere in this document.

---

# Part A — the whole thing in one page

**The idea.** A picture in Shaper is made of layers. Every layer holds one **generator**: a small pluggable thing that makes one kind of stuff. There are only three kinds of stuff — **shapes** (where the thing is), **fills** (what its surface looks like), **borders** (what its edge does) — plus one escape hatch for the big pre-existing effects that make finished pictures on their own and are not worth taking apart.

**Shapes nest.** A shape layer can be one primitive, or it can be a bag of sub-shapes combined into a single silhouette. There is only one nesting mechanism, used at every level: what Shaper today calls "its own internal layers" is the same thing as what Pyre would call a group. So a brand new layer is a disc in one flat colour, and if you never open it up, it stays that simple. Open it and you find it was a bag with one disc in it all along.

**Fills attach wherever you point them.** Put a fill on a sub-shape and it paints that sub-shape. Put it on the bag and it paints the whole fused silhouette as one surface, which is exactly the "complex shape made of several layers, textured as one" you asked for. Borders follow the identical rule, which answers "where do borders go": **a border belongs to whichever silhouette you want it to trace**, and that is the same node whose coverage it reads.

**A border is not a special kind of colour.** It is a strip of pixels derived from a shape's edge, and then it is filled by an ordinary fill generator. Gradient outlines, animated outlines, textured outlines are all free the moment you stop treating a border as its own colour concept.

**"Reflective" is not a kind of fill.** This is the biggest simplification in the document. A fill answers two questions per pixel — *what colour* and *how high* — and shine, sheen, bevel and relief all come from the document's single light rig acting on that height. So there is one place to add lights, both Silhouette and Solids obey them, and a plasma fill on a bevelled box gets lit correctly without the fill knowing anything about light.

**Tapestry already fits, better than expected.** *Tapestry Surface* makes seamless colour material — two of its three generators are fills, the third is not (B5). *Tapestry Shape* makes signed height fields with the bevel profile already authored into them — it is exactly the height source the lighting stage wants, and its "meant to be bevelled" is not aspiration, it is what it already emits. Neither has been ported to Unity, and 248 finished height fields are sitting there unused.

**The pixel-border thing you remembered is real, it is a fill, and Shaper lost the half that made it good.** In the reference app each palette entry carried a *height* as well as a colour, so one hand-painted strip coloured and sculpted at the same time. 3D Shaper reimplemented the strip and dropped the height. Putting it back is the single feature that best proves the colour-plus-height fill model.

**One thing genuinely cannot be deferred.** Shaper's extrusion is a screen-space height field — one depth per pixel — which structurally cannot rotate in 3D. Rotating in 3D means a different kind of buffer, and retrofitting it means rewriting the best-tuned 200 lines in the app. You do not have to *build* 3D rotation now, but you do have to decide now what the buffers mean, or you pay for it twice. Z translation, by contrast, is nearly free and should just go in.

**Arcs are cheap and universal.** Angular sweep and "hollow it out from the middle" are two operators applied to the finished shape, not changes to each primitive's formula. Every primitive gets both at once, and at their default settings nothing changes at all.

**Yes, clone the project.** You were right and the earlier objection was answering a different question. Details in B10 — the short version is a second working copy of the same repository, a second Unity editor, and the new tool in its own namespace, so the rest of Laubrary stays fully workable throughout.

**One thing to do before anything else.** The branch this entire five-week rebuild lives on has never been pushed anywhere. It exists on one disk. That is the largest risk in the project and it is unrelated to any of the design above.

---

# Part B — your points, answered

Each section is titled with a one-sentence summary of *your* point, in the order you raised it.

## B1. Generators are a good concept, keep them, and they output fill, border or shape

Kept, and made precise. A generator is a pluggable unit that produces one of four things, and it declares which:

- **A shape generator** answers, for every pixel, *how much of me is here* — a soft, unbounded coverage value, and optionally a height and a set of named per-pixel quantities (how hot, how dense, how sooty, how old, how far from the edge, which way the surface faces). It never produces colour.
- **A fill generator** answers, for every pixel, *what colour is the surface here* and optionally *how high is the surface here*. It may also emit a veil — a transparency that multiplies the shape's coverage — but it can never overrule the shape's own edge.
- **A border generator** takes a shape's finished coverage and derives a strip from it. The strip is a region, not a colour; a fill paints it like anything else.
- **A composite generator** is the escape hatch: it produces a finished picture directly, the way the nine big imported effects do today. Its contract is one line — *it must publish coverage, and it may publish nothing else*. With that it still groups, still masks, still swarms and still takes buffer-level post-processing. What it gives up is a choice of fill (its picker offers one entry), the border stage, and any effect that needs to know where it is inside the shape — which is roughly a third to a half of the effect library. That is three things, not one, and a layer using a composite generator should say so.

The fourth kind exists because you said you were open to it, and because it is the migration escape hatch: no imported effect has to be taken apart before it can move into the new tool. But it must be a declared *reason*, never a declared *exemption*, and there are only two legitimate reasons — either its look is authored data rather than a rule (permanent), or nobody has split it yet (temporary, and it has to say so on the layer).

One correction to carry forward, because it reverses the hardest objection in the second document: **there is no generator whose colour and shape are mathematically fused.** Eleven were classified; eight had their pixel-emitting loops read line by line, four of those re-read independently by a second pass. In four the outline is completely indifferent to the palette. In the rest the palette carries a transparency slider that gets *multiplied* into an edge rule the generator computed on its own — a product of two independent factors, not a blend. So the split is available everywhere; the question was only ever how much of it is worth doing.

## B2. Shapes can have sub-shapes

One nesting system, used at every level, and this is the decision that makes the UI guideline you set last round actually achievable.

A shape layer is a node. A node is either a **primitive** or a **bag**. A bag holds child nodes, each carrying its own combine mode (add to the silhouette, cut out of it, keep only the overlap, blend softly) and its own local position, rotation and scale. The bag's coverage is the combination; downstream, a bag is indistinguishable from a primitive.

The important consequence: **what Shaper calls "its own layer system" is not a second layer system.** A Silhouette generator's internal shapes are sub-nodes of the layer it sits on. There is one tree, one set of combine modes, one drill-in gesture, one mental model. If we had kept them separate you would have ended up with two nested-layer systems that behave differently, which is precisely the sort of accumulated near-duplicate the whole exercise is meant to remove.

That gives you the workflow you asked for, exactly:

- Add a layer. You get a disc in one flat colour. Nothing about bags, combine modes or sub-shapes is on screen.
- Change its size, its colour, its position: still nothing on screen but a disc.
- Click into the shape and you are inside the bag, which contains one disc. Now you can add a second shape, cut a hole, blend two blobs.
- Come back out and the layer is still one layer.

**The UI guideline, stated as a rule rather than an aspiration:** complexity that is not in use is not on screen. A bag with one child renders its editor as though it were that child. Combine modes appear when there are two things to combine. A sub-shape's own fill row only exists once the bag's fill is not what you want. This is worth writing down in the tool's own design notes because it is the difference between Shaper being pleasant and Shaper being a chore.

Nesting depth is not capped in the data. The UI shows one level at a time with a breadcrumb, so depth costs nothing in screen space. What depth does cost is compute — every node is a pass over the canvas — which is what the caching rules in B9 exist to contain.

## B3. Where do borders go

**A border attaches to a shape node — any shape node — and traces that node's coverage.** That is the whole rule, and it answers the question in every case you can construct:

- Want each blob in a cluster outlined separately? Put a border on each sub-shape.
- Want one outline around the fused cluster? Put the border on the bag.
- Want both — an outer outline plus interior division lines? Put a border on the bag and borders on the members. They are different nodes, so they do not fight.

Three further decisions the rule needs to be complete:

**A border is a region, not a colour.** It derives a strip from the shape's edge — inward, outward, or straddling, with its own width, and its width can animate — and then an ordinary fill paints that strip. So a border can be a gradient, a texture, an animated texture, or a single colour, with no new machinery whatsoever. Half of this already exists in Pyre today: the border stage measures inward distance from the edge, paints the band with its own separate fill, animates the width, and keeps border and interior in two separate buffers. It was simply gated to six of the twenty-nine generators and not even constructed in the UI for the other twenty-three.

**An outward border grows the shape.** You ruled last round that drawing outside the shape is allowed and that the shape either grows or the machine just processes more pixels. So an outward border's strip is added to the node's *published* coverage by default, which means a bag containing it sees the outline as part of the member, and a mask made from it includes the outline. There is a per-border switch to opt out, because there is one case where you want the outline drawn but not counted, but the default is the intuitive one.

**Three generators already draw a private rim of their own** by other means — the lit solids, the text generator, and the fire simulation. When the public border stage is ungated they will have a public rim beside a private one. Those three private rims should be reconsidered as ordinary borders during the port, or explicitly kept and documented as part of what that generator *is*. Silently having both is how the current confusion started.

## B4. Fills can be colour, gradient, texture, animated texture, and on top of that reflective

Almost all of that is one thing, and the last item is not a fill at all.

**One fill contract, many fill generators.** A fill is asked, per pixel, for a colour. It is handed a small fixed set of numbers to decide with, and that set is the entire vocabulary of the system:

| What the fill is handed | What it is for |
|---|---|
| Where am I inside this shape (a normalised coordinate) | gradients, textures, anything positional |
| How far am I from the edge, signed | rims, inner glows, bevels, contour bands |
| How high is the surface here | relief-aware colour, tier separation |
| How much shape is here | soft edges, fog, anti-aliasing |
| How old is this pixel, and which instance am I | lifetime fades, per-particle variation |
| What time is it | animated everything |
| The shape's named quantities: heat, density, soot, depth, surface direction | the imported effects' palettes, and anything wanting to react to what the shape is doing |

Each shape generator declares which of the named quantities it publishes. A fill that needs one the shape does not have is **greyed out with the reason shown** — not hidden, not silently inert. Silent inertness is the single most-reported confusion in the existing tool and it should not survive the rebuild.

**Honesty about that table:** it is not all promotion of things that already exist. There is a publishing mechanism in the current tool and eight of the nine imported effects use it — but what they publish is a *subset*, only four of them publish coverage at all, and two entries in the table (how old is this pixel, which way the surface faces) are published by nothing anywhere today. Coverage, height and edge distance are promotions. Age and surface direction are new construction, and the fireball-plus-plasma case below depends on the second of those, so it is not free.

A fill also declares **how it composites** — over, or added. Additive light is not expressible by coverage and colour alone, and leaving it out is how a glow ends up looking like paint.

With that contract the list you gave stops being a taxonomy and becomes a list of plug-ins: *Solid* (ignores everything), *Gradient* (linear, radial, angular, or by distance-to-edge), *Ramp by quantity* (heat or density or height into a palette — this is what the fire and explosion effects are already doing internally), *Texture* (still image, tiled or fitted to the shape), *Animated texture* (the same plus time), *Procedural* (noise, plasma, and the Tapestry surfaces of B5), *Indexed strip* (the reference app's material strip, B6). None of them needs a special case anywhere.

**Reflective, shiny, bevelled and lit are not fills.** They are what happens *after* a fill, and this is the load-bearing simplification of the whole design:

> A fill emits **albedo** — flat, unlit colour — and optionally a **height delta**. The document's light rig turns albedo plus height into the final pixel. Shine belongs to the lights, not to the paint.

Everything downstream falls out of that. A plasma fill on a bevelled box is lit correctly without the plasma knowing what a light is. A brushed-metal texture gets a specular streak because the metal's height field has grooves in it, not because someone wrote a metal shader. The reference app's material strip works because its palette entries carry height (B6). Tapestry Shape's output plugs straight in because it *is* a height field (B5). And you get the thing you asked for in the very first document — a fireball shape wearing a churning-plasma surface — with the honest caveat that the disappointing half is real: the churn will look like paint on a fireball rather than a fireball made of churning plasma, because the shape's own motion and the fill's motion are two independent animations that do not know about each other. The fix for that is the shape publishing *which way its surface is moving* as one of its named quantities, and a fill that reads it. That is worth building, and it is not free.

**One consequence worth naming now:** there is an existing fill abstraction used across the whole Laubrary library. It is closer to the table above than it first looks — it does take a life clock and a spatial sample point — but it takes none of the coverage, height, edge-distance or named quantities, and, more damningly, **every call site in Pyre passes zero for its spatial point**, which is why every spatial and noise fill mode collapses to a single sample and cannot churn. Because it is used library-wide it cannot be extended in place without a blast radius outside this tool. The new fill contract is a new thing that lives in the new namespace; the old one keeps working for everyone else and is not touched.

## B5. Procedural texture generation is the natural next step, and Kiln's Tapestry has the algorithms

I went and read both Kiln projects. Your reading of them is correct, and understated in one place.

**Tapestry Surface is the material half.** Three evolved generators — a plasma, a painted-steel-with-rust, and a machined clean alloy with a rivet engine — each producing a still, seamless, full-colour image from a height field it is handed. Between them they expose twenty-three to thirty dials each and six named palettes. There is no time input anywhere in its contract, so these are still images today.

**Two of the three are fills; the plasma is not.** Rendered against a completely flat field the two metals come out within four to eight levels out of two hundred and fifty-five of what they produce against a bevelled shape — they are, already, shape-independent tileable surfaces, which is exactly what a fill must be. The plasma given a flat field renders **nothing at all**: its transparency *is* its silhouette. So it is not a fill; it is a shape-coupled emissive treatment, and it should be filed as a composite generator or an effect rather than forced into the fill contract where it would be the one entry that does not obey it.

**Tapestry Shape is the height half, and the bevel is already in it.** Two generators whose *only legal output* is a signed per-pixel height field — a colour return is rejected outright. The bevel profile is authored explicitly per feature: smooth, linear, chamfer or step, with a width. So your "seems to be a way to procedurally create a texture that is meant to be bevelled, extruded" is not a hopeful reading; producing something meant to be bevelled is literally the only thing that project is allowed to do, and the bevel shape is already a parameter. What is missing is only the *consumer* — something to light it.

**Where each goes in this design.** Tapestry Surface's two metal generators become **fill generators** (they emit albedo). Tapestry Shape's generators become **height generators** — a fill that emits height and no colour, or a modifier on a shape's height, both of which the contract in B4 already allows. And 248 finished height fields already exist in that project, published and unused, single-channel, signed and tileable, 245 of them 256 pixels square and three at 128. Shipping them as presets is a genuine feature with no algorithm to port — but not with *no work*: they are unnormalised, ranging from about minus one and a half to plus two and a bit, so they need either a per-field normalise on import or a height-scale dial at the point of use.

**Three things that need adding to make them fills rather than pictures:** a time input (they have none — a fill that cannot animate is half a fill), a palette-quantise step so the output stays pixel art rather than photographic, and a choice of coordinate space. That last one matters more than it sounds: the patterns are anchored to their tile, so a moving sprite filled with them will appear to *swim* over a stationary texture unless the fill is told to use shape-local coordinates instead. That switch — pattern anchored to the world versus anchored to the shape — is a control every texture fill needs and it is easy to forget until it looks wrong.

**External tool or in-tool generator?** You floated external — a tool that makes texture files, and shapes use the files. **I recommend in-tool, and against your floated version**, for three reasons that all come from your own requirements: an exported file cannot respond to a dial in real time (requirement 1), cannot animate (you asked for animated textures), and pushes cache management onto the filesystem where it becomes the user's problem. Port them as native generators and cache their output in memory keyed by their settings, which is the same caching every other layer needs anyway.

There is a wrinkle you may have forgotten and should know before deciding: **a Tapestry tool already exists inside Laubrary** — a real Unity tool with a layer stack, blend modes, a tiled preview and PNG export. So "an external tool that generates pure textures" is not a thing to build; it is a thing you already built. It keeps its job as the place you *author and browse* surfaces. What this design adds is that a Tapestry surface can also be selected directly as a fill inside Shaper, without a round trip through a PNG. It also has blend modes that Pyre lacks, which are worth taking.

Two more notes from the same reading. The split into a shape project and a surface project was **deliberate, documented, and already identified in Kiln's own briefing as the decomposition to bring to C#** — so this is not a new idea being imposed on it, it is the same conclusion reached independently on both sides. And Kiln already settled the rule that *a fill never authors its own bevel* — the bevel is the height stage's business — which is the same rule B4 states from the other direction.

## B6. 3D Shaper's pixel-border function acts as a divider and can mix fills and extrusion

Found it, and this is the most interesting thing in the whole investigation.

**What it actually is.** In the reference browser app each layer owns a small hand-painted strip of palette slots. Per pixel, the shape is parameterised — either by the angle around it, or by projection across it — and that parameter selects a slot from the strip. A separate control decides how far in from the outline the strip reaches; **turn it to maximum and it covers the whole shape**, which is why a disc becomes an angular sunburst rather than an outlined disc.

**The half that matters, and the half Shaper lost.** In the reference app a palette slot carries not just a colour but a **protrusion** — a height, positive or negative, over a wide range. The renderer adds that height into the pixel's depth at full weight where the strip is patterned and at a quarter weight in plain fill. So one hand-painted strip *paints and sculpts simultaneously*: the raised slots get real surface normals, catch the light, and cast real shadows. And because the height field doubles as the depth buffer, a raised strip can win the depth test against a neighbouring layer — that is the "divider" you remember, and why it produces such strong output for so little authoring effort.

3D Shaper reimplemented the strip, and improved the parameterisation considerably — more slots, whole-number repeats, true arc-length around the perimeter, controls for orientation and reach and position. **It dropped the per-slot height entirely** and substituted a single fixed bump. So the capability you remember genuinely does not exist in the app today; you are remembering the reference, not the port.

**Where it files.** It is a **fill**, not a border. It is an indexed-palette fill driven by a pluggable parameterisation of the shape, whose palette entries carry height as well as colour. Filing it under borders would make its headline behaviour — covering the entire shape — look like a degenerate edge case of an edge treatment, which is exactly backwards. The edge-reach control is one optional parameter of the fill, not its identity.

**Why it is load-bearing for this design rather than just a nice feature.** It is the existence proof for B4's central claim. A palette whose entries carry colour *and* height, lit by a shared rig, is the entire "material" idea in its simplest possible form — and it was already built, in the simplest possible way, years before this design existed. Restoring per-slot height should be an early task, not a late one, because it validates the fill contract cheaply and visibly.

One caveat on all of the above: it was read from source, not watched running. Opening the reference app and dragging the protrusion control with a patterned layer on screen would confirm the visual claim in about thirty seconds, and it is the highest-value manual check arising from this whole round.

## B7. You want to rotate extruded shapes in 3D and translate them in Z

Two very different answers.

**Z translation: cheap, add it.** The control currently labelled depth is *extrusion thickness*, not position. Actual Z position is derived from layer order alone. Giving each layer a real Z offset added to its ordering base is a handful of lines and needs no new buffer, because the height field already is the depth buffer. It should go in.

**3D rotation: structurally excluded today, and this is the one decision you cannot defer.** Shaper's extrusion is not a mesh and not a voxel field. It is a **screen-space height field**: a 2D distance field decides what is inside, a closed-form function turns that into one depth value per pixel, and normals come from comparing neighbouring pixels. There is exactly one depth slot per pixel. A solid tilted in 3D has *two or more* surfaces over the same pixel — a front face and a back face — so there is nowhere to put them. This is not a missing feature; it is a property of the representation.

The route out is to treat the shape as a genuine implicit 3D field and march rays through it. That keeps the entire authoring model, every primitive, every combine mode and every parameter — it replaces only the renderer. But the renderer is around two hundred lines of very well-tuned code, and those two hundred lines *are* seven separate pieces that read the screen-space buffers directly: the fusion, the layer-below slot, the surface normals derived by comparing neighbouring pixels, the shadows, the occlusion outlines, the edge band and the glow spill. Retrofitting means rewriting all of it, and it is the best-tuned code in the app.

**So: decide now, build later.** You do not have to implement 3D rotation before the rebuild. You have to make the rebuild not *preclude* it, which costs almost nothing if done on day one and is expensive on day five hundred:

- Define the shape stage's published output as *an implicit field that can be sampled at a given depth*, even though today's implementation only ever samples one depth per pixel. The words are free; the constraint is what matters.
- Forbid every stage except the final resolve from reaching into screen-space neighbours. Stages that want a surface direction ask the shape for it as a named quantity (B4's table already has it) rather than differencing the buffer themselves. This is the rule that actually keeps the door open, and it is also just better layering.
- Keep the depth buffer's meaning explicit — "nearest surface depth" — so that the day it becomes "nearest surface depth along a ray" nothing downstream changes its interpretation.

**Is it important?** Given that Solids — the lit 3D primitive family — *does* do real 3D rotation and is staying (B8), you already have a way to get a rotating 3D primitive. What Silhouette would gain is rotating an *arbitrary fused silhouette* in 3D, which is a genuinely different and quite powerful thing. I would not build it in the first pass. I would spend the day one cost to keep it possible, and revisit once the tool is real.

## B8. Silhouette and Solids are different generators that never share a layer but must share lighting

Agreed, and the naming is adopted throughout: the tool is **Shaper**, the 2D-derived extruded generator is **Silhouette**, the existing lit 3D primitive family is **Solids**.

**They cannot share a layer** because they publish different things — Silhouette publishes a height field, Solids publishes real 3D geometry with genuine per-pixel normals and real occlusion. Trying to unify them at the layer level would either flatten Solids to a height field (losing its rotation, which is its whole point) or force Silhouette into a 3D pipeline it has no need for. Keeping them as two generators in one document is right.

**They must share lights, and here is how.** The document owns a single **light rig** — the one place lights are added, exactly as you said, because a Shaper document should read as one scene. Each light has a direction or position, a colour, an intensity. Every layer that publishes height or normals is a light receiver, and carries a small per-layer response block: receive lighting on or off, an intensity scale, cast and receive shadows, rim strength. That is the standard arrangement and it does what you want: lights in one place, per-layer control over how much they matter.

**One honest limitation to state up front, because it will otherwise be reported as a bug.** Silhouette shades a height field; Solids shades real geometry. They will not produce *identical* results from identical lights — a sphere and an extruded disc lit the same way genuinely differ. What can and should be made identical is the *shading law*: the same falloff, the same colour mixing, the same ambient term, evaluated by one shared piece of code that both renderers call. Then the two read as being in one scene even though one of them is a relief and the other is a solid. Promising more than that would be promising something the representations cannot deliver.

Some existing capability transfers straight across: the machinery for turning a height channel into relief lighting with a light angle already exists in the current tool and works, and it is exactly what a shared rig needs underneath it.

**And this is the named exception to "Silhouette is the only way to make primitives".** You ruled last round that the Shaper primitive engine should be the only way to make a disc, and I agree — for flat and extruded work. Solids is out of that ruling, because it does real 3D rotation, which Silhouette structurally cannot (B7). So the rule as it will actually be implemented is: *one primitive engine for everything flat and extruded, and Solids beside it for the lit 3D family*. That exception exists from day one; it should be stated in the tool rather than discovered.

## B9. Real-time preview response, animation caching, and Burst if calculations get out of hand

The first two I take as hard requirements. The third you phrased as "consider Burst *if* calculations get out of hand" — I am promoting it, on my own recommendation rather than on your instruction, from an optimisation you might reach for into a constraint on the plug-in contract, because the *option* to use it later is only cheap if the contract is shaped for it now.

**Requirement 1 — a dial moves, the preview moves.** Achieved by structure, not by speed: every node caches its output keyed by a hash of its own settings and its children's hashes. Editing a dial dirties that node and everything downstream of it, and *only* that. Editing the fill on layer seven of twelve recomputes layer seven's fill and the composite, not layers one through six. Canvases here are small — the current tool allows sixteen to two hundred and fifty-six pixels square, and defaults to sixty-four — so the budget is generous as long as the work is not repeated needlessly. The failure mode to design against is not slow maths, it is recomputing the whole document on every mouse-move.

**Requirement 2 — animations cache, first loop may be slow.** Each node caches per frame under the same key. The first pass through an animation may compute; every subsequent loop replays. Two additions worth having: a background pre-bake that fills the cache without blocking the UI, and a visible indicator of which frames are cached, so a slow first loop reads as *working* rather than as *broken*. The Kiln timings support this being necessary rather than precautionary: a Tapestry *surface* measures at roughly thirty to a hundred and twenty milliseconds per image in its original Python, and Tapestry *Shape* has retry paths that spike past a second and a half. A port to C# should be considerably faster than those numbers — that is an estimate, not a measurement — but "considerably faster than 1.8 seconds" is still nowhere near a frame budget, so the cache is load-bearing rather than nice to have.

**Requirement 3 — Burst, and what it forces.** This is the important one. Burst compiles tight numeric loops into fast native code, but only if the loop touches no managed objects. That means the *plug-in contract itself* has to be shaped for it from the start:

> A generator does not get asked "what colour is pixel 4,7?" a hundred thousand times. It is handed a block of the canvas and fills flat numeric arrays for it.

Chunked buffer writes, not per-pixel callbacks. This is a small distinction on paper and an enormous one in practice: a per-pixel callback interface cannot be made Burst-friendly later without redesigning every generator that implements it. Get it right on day one and Burst becomes a switch to flip when a document gets heavy; get it wrong and Burst becomes a rewrite that will never be scheduled.

The same decision pays off twice, because it also makes the sheets in B4's table cheap — coverage, height, edge distance and the named quantities are all just flat arrays over the canvas, produced once and read by everything downstream, instead of being recomputed inside each consumer.

**One thing to explicitly design for, since you named it:** many layers, animated, with many effects. The cost model is one pass per node per frame, so a document with a dozen layers each holding a bag of four sub-shapes is around sixty passes a frame before effects. That is fine at these canvas sizes with caching and Burst, and it is not fine without them — which is why both are in the design rather than in a future optimisation pass.

## B10. If two agents must never drive one Unity editor, why oppose cloning — does the refactor block all other Laubrary work?

**You are right, and the earlier objection was answering a different question.** The one-editor rule and the cloning question are not in tension at all: the rule forbids two agents driving *one* editor, and cloning gives you *two* editors, one agent each. Cloning is the solution to that rule, not a violation of it. The earlier recommendation against a whole-project clone was about a different cost — the pain of merging a diverged copy back — and it should not have been stated in a way that read as a general objection to a second working copy.

And your consequence is correct: **without a second copy, the refactor blocks the rest of Laubrary for its entire duration.** For a project of this size that is not acceptable.

**What I recommend, which is your "both".**

**Clone as a second working copy of the same repository, not as a separate copy of the project.** Git can check out a second branch into a second folder from the same repository. That gives a second folder to open as a second Unity editor — which is all the one-editor rule needs — while keeping one history, so bringing work back is an ordinary merge rather than a manual replay of five hundred files. A true duplicated folder would give you the same editor isolation and none of the merge safety.

**Build in a new namespace, so the merge back is additive.** Shaper is new files in new assemblies. It does not edit Pyre's files. That means the branch's changes and the main branch's changes touch disjoint files almost everywhere, so merging is close to conflict-free. The places they *will* meet are few and known: the shared UI toolkit, the package version and changelog, and the menu registration. Those are worth agreeing on now rather than discovering at merge time.

**Merge the main branch into the Shaper branch on a regular cadence — weekly is about right.** The shared UI toolkit and asset machinery are under active development, and absorbing that drift continuously is nearly free while absorbing it once at the end is exactly the merge nightmare the earlier objection was worried about.

**Keep one board.** The task board lives in the main working copy. Shaper tasks say in their text that the code lives in the second copy. That way the board is not duplicated, the two copies do not fight over task files, and you keep one place to look.

**Before any of this: push the branch.** The branch carrying the current Pyre — the entire thirty-three-day parallel rebuild — exists on no remote. Not on the origin, not anywhere. Every piece of design in this document assumes that work as its starting point, and it is currently one disk failure from gone. This is the single highest-priority item arising from this task and it has nothing to do with the design.

## B11. Sparkle can go, Streak can go, and Shaper should get a real polygon and star engine

**Sparkle and Streak: removed**, in the sense that they are simply not carried into Shaper. I am not deleting them from the existing tool. That is not timidity about your disposable-assets ruling — it is that removing things from a working tool is a change with its own risk and its own testing, and it buys nothing while the old tool is still the one you use. When Shaper reaches parity the old tool goes as a whole, and the two removals come for free.

**Polygon and star: yes, and the design work is already done on both sides.** Shaper today has eight primitives, and its entire primitive registry is about a dozen lines — the code itself advertises adding a case as the extension mechanism. But two of those eight are worth knowing about before they are ported: its hexagon and octagon are hand-fitted axis-aligned approximations rather than true regular polygons, and its star is a fixed five-lobed flower with no parameters at all.

The existing Pyre star, by contrast, is a real specification — arm count, arm length, base width, skew, all animatable, with a documented clamp that stops the valleys crossing the tips. That is a genuine star engine of the kind you asked for.

So: **take the mechanism from Shaper and the parameters from Pyre.** A true N-gon is one more case in that registry; a parameterised star is a slightly bigger one. Both are small. Porting Shaper's primitives as they stand, without this, would be a day-one regression on two of the flat forms you already have.

One caution, because it is the same argument that got a similar proposal withdrawn last round: **a true regular hexagon is not the same picture as the hand-fitted one**, and a parameterised star is not the same picture as the fixed cosine flower. So the N-gon and the star *supersede* the old ones in the sense that nothing new should be built on the old ones — but if either hand-fitted shape turns out to look better in some case, keeping it as a named preset is a reasonable outcome, not a failure. Do not delete a look to satisfy a taxonomy.

Do this in the same pass as B12, because they multiply: a swept, shelled, parameterised star or N-gon is a far larger vocabulary than either feature gives on its own.

## B12. Every Shaper primitive should have an arc control, plus a way to hollow it out from the middle

Both are cheap, both are universal, and — importantly — **neither requires touching any primitive's formula.** They are two operators applied to the finished shape:

- **Sweep** — keep only a wedge of the shape, from a start angle across an extent. At full extent nothing is removed. This is your arc control.
- **Shell** — keep only a band at a given distance from the surface, discarding the interior. At its identity setting it does nothing. This is your "remove from the middle outwards", and it gives you rings, tubes, frames and, combined with sweep, actual arcs.

There is a second, more obvious way to define the second one — *subtract a scaled-down copy of the shape from itself*, which is what the existing ring's inner-radius control actually does and what most people mean by "inner radius". I am recommending the shell instead because it keeps a constant wall thickness on shapes that are not radially symmetric, where a scaled copy gives a wall that is thick at the ends and thin in the middle. It is worth knowing that the alternative exists, because the scaled-copy version is what users coming from the existing ring will expect, and the two disagree visibly on anything long and thin.

Applied as operators, **all eight primitives gain both at once**, and at their identity settings the output is bit-for-bit what it is today. Shaper already computes the angle it needs and already ships a reach-and-position control pair — they are just wired to the pattern rather than to the shape.

The non-radial cases work out better than you might expect. A swept rectangle is a wedge or gusset, which is arguably more useful than a pie slice. A shelled rectangle is a picture frame. The one genuinely awkward family is the long thin *primitives* — the capsule, the line, the streak shape — where sweeping by angle around the centre produces a meaningless bowtie. (These are primitive shapes; they are unrelated to the Streak *generator* retired in C9.) The fix is one line in the design: **each primitive declares whether its sweep runs around it or along it.** Hub-shaped things sweep angularly; line-shaped things sweep along their length. Same two controls, same UI, sensible result either way.

One thing to be aware of rather than to fix: shelling changes what "inside" means for everything downstream. A shelled dome becomes a tube. That is almost certainly what you want, but it falls out of the maths rather than being chosen, so it should be tested deliberately rather than discovered.

## B13. Inline a node's planning document into the dispatch brief automatically

**Done.** The task-tracker now looks up the planning document belonging to the dispatched task's own group and includes it in the brief it hands the agent, so an agent starting work automatically arrives knowing that group's design context — instead of that depending on whoever filed the task remembering to paste it in.

One deliberate limit: it is the task's **own** group only, never the groups above it. A task filed in a sub-group therefore inherits nothing from its parent. That was the right call for a first version — a parent group is a broader area, and chaining them upward multiplies the size problem the budget below exists to contain — but it means a sub-group that needs context has to carry its own planning document.

Because planning documents can be very large (this one's group carries roughly a hundred and eighty thousand characters across four tabs, which would swamp the brief), there is a size budget. Under budget, the document is included whole. Over budget, each tab contributes its opening section and is explicitly marked as truncated with its true size and the file path to read the rest. **The agent always learns that the document exists, where it is, and what every tab is called**, even when the content is cut. Truncation is never silent.

If the group has no planning document, or the file is missing or damaged, or the task is not in a group at all, the brief comes out exactly as it did before — no error, no empty section.

**It needs a restart of the tracker to take effect**, and I deliberately did not restart it, because it is running right now and being used.

## B14. Should this be its own board, a group of tasks, or one enormous to-do list

A **group of tasks under one node on the existing board**, which is what I have set up. Reasoning:

- A separate board would cut Shaper off from the rest of Laubrary, and Shaper depends on the shared UI toolkit and asset machinery that live there. Cross-referencing across boards is worse than scrolling one.
- One task with an enormous to-do list cannot be worked by more than one agent, cannot record separate handovers, and cannot be paused in one place while progressing in another.
- A group gives each unit of work its own brief, its own to-dos, its own history, and — now, because of B13 — automatic delivery of this group's design document to whoever picks up a task in it. Realistically that means the opening quarter of it plus the file path to read the rest, since the document is well over the inlining budget; that is still the difference between arriving oriented and arriving cold.

On the **overwatcher** idea from last round: still not recommended, for one reason rather than the two I gave before. Dispatched agents here are **one-shot processes** — when the turn ends the process is gone, and there is no primitive anywhere in the tracker for a long-lived agent that holds context across dispatches. That is the whole objection and it stands on its own. I should not have added the one-editor argument alongside it: as B10 establishes, that rule constrains editors, not agents, so it does not apply here. (The tracker does already have a modest form of group oversight — a supervised group promotes its next task automatically when nothing else in it is moving — so "no mechanism at all" would be too flat. It just does not hold context, which is the thing you were asking for.) The re-reading cost is better attacked by B13, which is small, already built, and hits the actual problem.

What I have created, and what you can delete if you disagree with any of it, is listed in Part D.

---

# Part C — the design, stated once

This section is the specification. It is what Part B's answers add up to.

## C1. A document

A Shaper document has a canvas size, a frame count and a rate, a palette, **one light rig**, and an ordered list of layers. It resolves to a sequence of frames, and those frames bake to a sprite sheet or an animation.

## C2. A layer

A layer holds exactly one generator plus optional stages, and layers composite in order. Layer kinds are distinguished only by what their generator produces:

- a **shape layer** — a shape generator, then optionally a border stage, a fill stage, and effects;
- a **composite layer** — a composite generator, then optionally effects; it may not be re-filled;
- an **adjustment layer** — no generator of its own, only effects, acting on everything beneath.

Masking is unchanged in principle from today: a layer may contribute its silhouette to a shared channel and a later layer may read it. This machinery already exists, is generator-agnostic, and works.

## C3. The shape tree

A shape node is a primitive or a bag. A bag holds children; each child carries a combine mode and a local transform. A bag publishes coverage exactly like a primitive, so nesting is invisible to consumers.

Every shape node publishes, per pixel:

- **coverage** — soft and unbounded, a fog rather than a stencil;
- optionally **height**;
- optionally a set of **named quantities** it declares: heat, density, soot, depth, age, surface direction.

The shape always owns its own edge. A fill can veil it but never replace it. This is what lets borders, bags, masks, previews and swarm compositing all work without knowing anything about colour.

## C4. Fills

A fill is attached to a shape node. The **nearest ancestor that owns a fill paints the whole subtree**; a child that owns its own fill wins inside its own coverage. The default for a new bag is that the bag owns the fill and the children own none — which is the "fuse several shapes, then texture as one" case, made the default rather than a mode.

A fill emits **albedo** and optionally a **height delta** and a **veil**, and it declares whether it composites **over or added**. It reads only the numbers in B4's table. A fill needing a quantity the shape does not publish is greyed out with the reason shown.

## C5. Borders

A border is attached to a shape node and derives a strip from that node's coverage — inward, outward or straddling, with an animatable width. The strip is a region; a fill paints it. An outward strip joins the node's published coverage by default.

## C6. Lighting

The document owns the lights. Any layer publishing height or normals receives them, subject to a small per-layer response block. Silhouette and Solids call one shared shading law; their results are consistent, not identical, and that limit is stated in the UI rather than hidden.

## C7. Swarm

Swarm is a modifier available on **every** generator, and your intuition about it was right: running a generator many times with per-instance offsets is already how most of the big effects are implemented internally. There are two implementations behind one control — a generic wrapper that instantiates any generator N times with per-instance clocks, seeds, positions and parameter jitter, and a native path for generators that can do it more efficiently or want their instances to interact. Which one is in use is shown, not hidden, because it changes what the controls mean.

Two cases need real work rather than wrapping, and should be scheduled as such rather than discovered. The **simulation-based generators** cannot simply be run N times: their cost is not linear in the instance count, and a realistic swarm measures at roughly two hundred times the cell updates of a single instance — for those, swarming has to mean many seeds inside one simulation, not many simulations. And several generators have **no per-instance parameters at all** to vary, so a generic wrapper would produce N identical copies; those need the parameters added before the wrapper means anything.

One existing defect to fix during the port: most of the big effects start every instance on the shared clock, so instances pop into existence mid-animation instead of having their own lifetimes. It is a small fix and it changes what they render, so it belongs in the rebuild rather than in the old tool.

## C8. Effects

Effects are universal, with two honest qualifications.

The first is that universality has a price, and it is three things, not one. An effect that needs to know where it is inside a shape needs the shape to publish that — the same sheets the fills need. An effect that draws outside the silhouette needs the buffer to be padded, which nothing does today. And a padded buffer needs the picture-rect call that tells an effect where the artwork sits inside it, which exists and is never invoked anywhere. Between thirteen and twenty of the forty-one effects depend on the first of those; the rest work on the raw buffer and are universal already. Exactly one effect is genuinely stuck, and it currently has no host anywhere in the project.

The second is a real distinction that must **not** be flattened. Some effects act on a single instance *before* it is composited with its siblings; others act on the finished buffer. For a single shape these are the same thing. For a swarm of overlapping instances they are emphatically not, and an effect that drops pixels gives visibly different results depending on which side of the composite it runs. So the stage an effect runs at stays an explicit property, shown in the UI, rather than something the system decides. An earlier document claimed this could be collapsed with no behavioural cost; that claim is withdrawn.

The recommendation on presentation is one ordered effect list with honest greying-out, rather than four typed lists — a single list is what people expect, and greying with a stated reason is more informative than hiding.

## C9. What is deliberately not carried across

Writing this list down is itself part of the design, because silence here is how things get carried by accident.

| Not carried | Why |
|---|---|
| Sparkle | Your ruling; superseded |
| Streak | Your ruling; to be replaced by something better later |
| Pin warp | Its animation hook is called from nowhere; every keyframed pin has always held frame zero |
| Edge warp | Unreachable in both hosts, attached to nothing anywhere |
| The hand-drawn-path authoring surfaces | Their editing window was deleted in the last rename; the effects now tell the user to open a window that does not exist |
| Vortex field as a separate effect | Folds into the swirl effect |
| The 3D playback generator | Never finished, no bake path, and its preview replaces the entire canvas while you author it |
| Shaper's hexagon and octagon | Superseded by a true N-gon (B11) |
| Shaper's fixed five-lobe star | Superseded by the parameterised star (B11) |
| The dead auxiliary-map gate | *Still your call, not decided here.* Fully built, fully documented, twelve authored dials, attached to nothing anywhere. Wire it up deliberately or let it go with the old tool |
| Two per-particle generator dispatch modes | Declared and documented, never dispatched; they make the plug-in contract look more capable than it is |

The nine large imported effects are **not** on this list. They come across as composite generators, and — importantly — they should be *referenced* rather than copied, so that ten thousand lines of carefully verified ports do not get duplicated and then drift.

Two honest caveats on that. First, it only holds if the existing plug-in contract is unchanged by the new one, which has **not** been checked — the check is scheduled as the third task of wave 1 and consists of taking two of the big effects and confirming they satisfy the new contract with no edits. If they do not, this decision has to be revisited. Second, reference-rather-than-copy is in genuine tension with your own instruction to duplicate rather than modify. I think the tension is resolvable — duplicate the *tool*, reference the *ported algorithms*, because those are the one part of the codebase that was verified numerically against its original and therefore the one part where a silent divergence would be hardest to notice. But it is a real disagreement with something you said, so it is named rather than smoothed over.

---

# Part D — how this gets built, and what I need from you

## D1. What I have set up

A new group on the Laubrary board called **Shaper**, holding this document as its planning document and seventeen tasks, T-0099 through T-0115. Nothing in it is set to run on its own — the group is not supervised and every task sits in the open state, so nothing dispatches until you ask it to. If you disagree with the shape of it, deleting the group costs nothing.

The tasks are ordered in four waves:

**Wave 0 — before anything is built (T-0099 to T-0101).** Push the branch to a remote. Create the second working copy and confirm a second editor can be driven independently. Render one frame from every generator in today's tool and keep the sheet — this is the only empirical evidence that exists anywhere in this whole investigation, and after your ruling that usage counts prove nothing, it is the only evidence there *can* be.

**Wave 1 — decisions locked on paper before code (T-0102 to T-0104).** The buffer contract (B9's chunked-not-callback ruling, plus the implicit-field framing from B7 and the list of named quantities from B4, marked for which are promotions and which are new construction). The shape-tree rules (combine modes on the member, local transforms, whose clock a bag's fill runs on, whether a bag can also be a mask, and what a bag costs per frame). And the check that decides whether the imported effects can be shared rather than copied.

**Wave 2 — the spine (T-0105 to T-0110).** Silhouette with sweep, shell, N-gon and parameterised star. The fill contract with its first four fills. The border stage. The light rig, with Solids ported onto it. Extrusion, bevel and Z offset. Then the indexed-strip fill with per-slot height, because it is the cheapest thing that proves the whole colour-plus-height model.

**Wave 3 — the rest (T-0111 to T-0115).** Tapestry Surface's two metals as fills and Tapestry Shape as height, with the 248 existing fields as presets. The composite-generator escape hatch hosting the nine imported effects unmodified. Universal swarm. Universal effects. The caching and preview-responsiveness harness.

## D2. What I did not decide, and why

**Whether to build toward 3D rotation at all.** I decided the *cheap insurance* (B7) because not deciding it is itself a decision with a price. Whether Silhouette should eventually rotate in 3D is a product question and it is yours.

**Whether the old tool gets deleted at parity or kept.** Everything here is written so that both remain possible.

**Eighteen older open questions** — eleven from the first two documents, seven from the third — are listed in the digest at `D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0098\P1-digest.md`. Most are now answered implicitly by this design. The ones that are not are: how much resolution memory you are willing to spend, since three generators anti-alias after colouring and preserving that costs several times the buffer memory for those three only; whether the standalone effect stack should continue to exist as a separate tool at all, given that it and the main tool disagree about pivots, ordering and canvas growth; and whether the dead auxiliary-map gate gets wired up or let go. None of them blocks wave 0 or wave 1.

## D3. What has still never been checked

Four rounds of investigation, four documents, and **not one frame of Pyre or 3D Shaper has ever been looked at.** No window opened, no output eyeballed. Every visual judgement about either tool, in all four documents, is inferred from reading code. With usage counts ruled out as evidence by your own instruction, a rendered baseline is now the only empirical ground there is, which is why it is in wave 0 rather than somewhere polite and late.

The one exception, and it is worth noting because it is the only measured thing in four rounds: this round the Kiln Tapestry generators were actually **executed**, and the claims in B5 about which of them are shape-independent, what their height fields contain and how long they take are measurements rather than readings.

Two specific things would benefit disproportionately from thirty seconds of human eyes: dragging the protrusion control in the reference browser app with a patterned layer on screen (B6), and confirming that editing a colour ramp on a fire, fireball or text layer really does nothing, which the reading says it does not.

## D4. Bugs found and not fixed

Eighteen defects were found across the four rounds and **none have been fixed**, deliberately — they are in the old tool, and several of them change output, which makes them the wrong thing to touch while the old tool is the one in use. They are catalogued with locations in the digest at `D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0098\P1-digest.md`. Four of them are worth naming here because they affect the rebuild rather than just the old tool:

- **Warps in the standalone effect stack pivot at the top-right corner of the canvas** rather than its centre, affecting ten of the seventeen warp effects. The rebuild must not inherit the convention that caused it.
- **The picture-rect call that lets an effect know where the artwork sits inside a padded buffer is never made.** Harmless today because nothing pads. It becomes live the instant effects are allowed to draw outside the shape — which your ruling requires — so it has to be fixed in the same change, not afterwards.
- **One number means five different things across five files.** Two effects read it. It is the same number the new fill contract needs, so the rebuild has to define it once and mean it.
- **Two retired effect types are still referenced by saved files and now load as nothing** — in a sibling project, not this one. Under your disposable-assets ruling this is not a blocker, but it is the live precedent for what retiring something without a migration does.
