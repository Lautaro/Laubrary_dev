# Pyre — Shapes, Fills and Borders

*The addendum to the Grand Unified Generator report. A direct answer to one question: should Pyre be rethought as pluggable shape generators, pluggable fills and pluggable borders — and would that replace the whole of Pyre? Written 2026-08-30 for T-0098. Like the first report, this contains no code and no code names. Read `PYRE_GUG.md` first if you have not; this document assumes it and does not repeat it.*

---

## 1. The answer, in one page

**Your instinct is right, and it is more right than you think — but "replace the whole of Pyre" is the wrong verb, and the reason is not caution. It is that most of what you just described is already in there.**

**The strategy is correct.** You have independently arrived at the same seam the first report recommended, and then pushed past it. The first report proposed splitting *shape* from *material*. Your addendum adds three things it did not: **borders as their own pluggable kind**, **extrusion as something you apply to any shape**, and **grouping** — several shapes combined into one silhouette that gets coloured once. All three are correct additions. The grouping one in particular is the strongest of the four ideas and is the one the code most obviously wants.

**Two thirds of it already exists, built and unused.** This is the finding that should change how you think about the size of the job. Pyre already has a border stage — a real one, that measures distance in from the edge and paints that band with its own separate fill, kept in its own separate buffer. It is switched off for 23 of the 29 generators by a hardcoded list — partly for a real reason nobody wrote down, since several of those 23 already draw a rim of their own. Pyre already has grouping — layers can contribute their silhouette into a shared channel, be combined with union / add / carve, and be coloured once as a single thing. It works today. **It has been used on zero of the 305 saved layers.** Pyre already has a fill abstraction and already has a per-sample "how far across this shape am I" value that two effects read. **You are not proposing a new architecture. You are proposing to finish and join four things that were each built once, separately, and never introduced to each other.**

**One half of your hypothesis is wrong, and the correction is good news.** You guessed that "many SpriteFx would become fill generators". Counted against the code: of the 41 effects, only **two** clearly generate a picture on their own — a cracked-cell pattern and the fake-light one. (That number is not fixed: it depends on a decision you have not made yet — see §13, question 1 — and nine of the 41 sit genuinely on a boundary. But it is nowhere near "many" under any reading.) The real answer is the *other* half of your sentence, and it is much stronger than you put it: the effect list is **already overwhelmingly a shape system**. Twenty-two of the 41 operate on geometry or coverage and never need the colour underneath to decide what they do. Nine are colour operations. Four are edge work. Two are genuinely neither. **The one undifferentiated list of 41 effects that appears under every generator is really four different lists wearing one coat** — which is exactly why nobody can predict what any of them will do.

**The generators split much more cleanly than feared.** Of the 29 techniques: **14 split cleanly** (in several the seam is already a line in the file where one routine ends and the colouring routine begins), **13 need routine work**, **exactly one is genuinely inseparable** (the imported-sprite one — and its shape and its colour being the same thing is not a problem, it is what makes it the natural landing site for 3D Shaper), and one is not a shape at all. **No generator is inseparable because its colour is driven by heat or density or height.** Every one of those already keeps that number in its own store and colours it in a second pass. The first report guessed the lit 3D solids would be the *entangled* case; they are not — they already carry **five separate fills each**, and the code itself already calls one of them the material. They are still routine work, just not a design problem.

**Where it genuinely breaks — and it does, in five places that must be designed for, not discovered.** (1) For **all nine big scene generators, and for fire as well, the fill decides the silhouette**: they turn an energy field into a shape by choosing where it becomes opaque. Swap the fill and the shape changes. This is not a majority case to work around; it is how 11 of the 29 generators fundamentally work. (2) **The swarm.** Today a fill is evaluated per particle, in that particle's own frame, on that particle's own clock — a 200-particle cloud is 200 independent fill evaluations. "One fill over the group" is a *different picture*, not a refinement, and per-particle colour-fade-over-lifetime — **127 of the 305 saved layers, 42%, the single largest group of authored work in the tool** — simply ceases to exist unless the shape stage starts carrying an age value per pixel. (3) **Glows.** Several generators deliberately paint light where there is no shape at all. "Colour inside a silhouette" has no seat for that. (4) **Two effects have no shape, fill or border role whatsoever.** So the model is **not** three kinds. It is three kinds plus a surviving post stage, and pretending otherwise will cost you those two. (5) **The "does nothing" bug can reappear in a new place**: nine colour operations would be sitting in front of shapes that may have no fill to act on. Trivially fixable, but only if it is a rule from day one.

**"Fill" is the right word.** Not material, not texture. A material implies a surface with physical behaviour, which is only the most advanced case; texture implies an image and is a word Unity has already taken and hardened. Fill is the only one of the three where "just one flat colour" is an unembarrassing answer — which was your own test for the word. Keep *material* as a description of a rich fill, never as the name of the kind.

**Extrusion is not a fourth kind.** It changes how much stuff there is and how high it stands. It does not change the colour. It is a **shape operation** — the same kind of thing as bending or eroding a silhouette — and treating it as a peer of Fill and Border would give you a fourth menu for something that belongs in an existing one.

**So: should it replace the whole of Pyre?** **It should become the frame of Pyre. It should not be built as a replacement.** A rewrite is the one route that actually fails here, and not for the usual reasons — it fails because behind the plug-in generators sit roughly ten thousand lines of deliberately bit-exact ports, several of whose entire visual signature depends on *where in the sequence* a step happens, and because the whole authored body of work this would be risked for is about **32 effects**. The additive route costs nothing and loses nothing: every generator keeps working untouched, opts into the new model one at a time, and the first slice — ungating the border stage that already exists — is a few days' work that is immediately visible in the tool.

**The uncomfortable part, and the real risk.** This idea has effectively been built three times already — the melt-into-one-blob mode, the channel-based grouping, the border stage — and each time it landed invisible, and each time it got **zero or near-zero use**. Not because the idea was wrong; because you could not find it. **The architecture is not the bottleneck.** If this re-architecture ships without the discoverability work from the first report — one generator picker, honest badges, effect rows greyed out with the reason written on them — it will be the fourth good idea nobody used.

---

## 2. The model, made precise

Your sketch says three kinds. The code says three kinds, two operation stages, and one survivor. Here it is stated exactly.

### The three generator kinds — things that make something out of nothing

| Kind | Produces | A trivial example | A rich example |
|---|---|---|---|
| **Shape** | *Substance* — where the stuff is, how much of it, how high, how hot, which way the surface faces | A disc | A whole flamethrower |
| **Fill** | Colour, read off the substance | One flat colour | Churning plasma animating on its own clock |
| **Border** | The treatment of the rim | A one-pixel outline | A ragged, warping, glowing edge |

### The two operation stages — things that change what a generator made

| Stage | Acts on | How many of today's 41 effects | Examples |
|---|---|---|---|
| **Shape ops** | Substance, before any colour exists | **22** | Bend, skew, erode, dissolve, wipe, melt-together, **extrude** |
| **Fill ops** | Colour, after a fill has run | **9** | Brightness, posterise, palette swap, fake depth |

### The one survivor

**Post.** Two of the 41 have no honest home in the model — the one that splits colour channels apart across the whole finished picture, and the one that drops an offset shadow *behind* everything. Both are compositing operations on a finished frame. They prove the post stage must survive. It should be small, and it should be honestly labelled "this runs last, on everything".

### And one special shape

**Group** — a shape whose members are other shapes. Combined into one silhouette, then coloured once, by one fill, with one border. Exactly what you described, and exactly what 3D Shaper does.

**That is the whole model.** Three kinds, two op stages, one survivor, one special shape. Everything in Pyre today lands somewhere in it.

---

## 3. The words

You asked directly for a better word for what fills a shape. Here are the four decisions.

**Fill — for the thing that colours a shape.** Your own test was the right one: it must not be embarrassing for the answer to be "one flat colour". *Material* fails that test — a material that is just red is a joke. *Texture* fails it too and also collides head-on with a word Unity owns absolutely. **Fill passes, and it is already the word Pyre uses.** Keep *material* as an adjective for a rich fill ("a lit metal material"), never as the name of the kind. One warning: the existing fill type is used well beyond Pyre — other tools in the library depend on it — so "fill becomes pluggable" is a change with a blast radius outside Pyre. It is still the right name.

**Border — good, with a collision to resolve first.** There are already **two** implementations of this idea in the codebase: a layer-level border that draws the outermost band *inside* the silhouette, and an effect that draws a band *outside* it. They read the same information and differ mainly in which side of the edge they paint. If borders become a picker, both will be in it, and users will pick the wrong one forever. **Merge them into one border generator with an inside/outside/straddle choice before shipping the picker.**

**Do not call grouping "fuse".** The word already means three different things in the tool (the first report, §7). Adding grouping under that name makes it four. **Call it a Group.**

**Substance — the word for what a shape hands over.** You need a name for "the colourless output of a shape stage", because "silhouette" is too flat a word for something that also carries height and heat. It is not a word the user has to see often, but the system needs one, and every other candidate ("field", "mask", "coverage") is already taken by something narrower.

---

## 4. What already exists — the four half-built pieces

This is the section that most changes the size of the job. Each of these was built properly, works, and is either invisible or artificially restricted.

**The border stage — built, then gated to six generators.** It measures distance inward from the edge, paints that band with **its own separate fill**, animates its width, and keeps the border and the interior as **two separate buffers** so they never contaminate each other. That last detail is the new model's own principle, already implemented. It is switched on for six flat 2D shapes by a hardcoded list and off for the other 23, and nothing in the tool explains why — though there is a real reason hiding behind part of it: the lit solids, the text generator and fire **already draw a rim of their own**, by completely different means. So "ungate it" is not one change, it is: lift the gate, *and* decide what happens to the three existing private rim implementations that would now have a public one next to them. **Generalising this is still the smallest, highest-confidence, most immediately visible slice of the entire re-architecture** — and it is the slice that proves the model works before anything expensive is committed.

**Grouping — built, and used exactly zero times.** Layers can be told "contribute your silhouette to shared channel *k*"; the contributions are combined with **union, add, or carve**; and a later layer can throw away its own shape entirely and colour that combined channel through a single fill, with relief lighting for body. That is your grouping proposal, working, today. Two of 3D Shaper's four combine modes are already there — union and carve. Pyre's third, arithmetic add, has no Shaper counterpart, and **both** of Shaper's soft-blending modes are missing. A one-knob smooth-combine primitive does exist elsewhere in the codebase, but Shaper's soft blend is a two-knob strength-and-sharpness pair, so that is a starting point rather than the thing itself. **Zero of the 305 saved layers use any of it.** Not "rarely" — zero. Because it is expressed as a run of sibling layers wired together by an integer channel number chosen in a mask panel, and nobody has ever found that.

**Fill — a real abstraction, but a closed one.** There is a genuine fill type with four modes, four texture kinds, a stamped-or-fixed coordinate space, and animatable ramp transforms. It is a fixed set of choices, not a plug-in point. Making it pluggable is real work with real migration risk, and it is the one part of your proposal that is honestly a new type system rather than a rename.

**Substance — already there, unnamed and inconsistent.** Every effect already receives, per pixel, its warped position, its life, **and a normalised "how far across this shape am I" value**. Two effects already read that last one. Separately, there is a whole utility that derives edge-distance, interior, luminance and detail maps from a finished picture — it computes, from pixels, exactly the information the shape stage would simply *hand over*. **The substance contract is not an invention. It is an existing value promoted to a named, documented, per-generator promise.** With one serious catch: that value currently means **five different things** depending on where you are — distance across the shape in one place, distance across the whole canvas in another, *the heat value* inside one scene generator, a branch parameter inside a second, and a hardcoded zero in a third host, which silently kills one effect there. Formalising it means fixing all five.

**Extrusion — this one really is new.** There is exactly one worked instance of extrusion in Pyre, welded into the text generator, and a separate unrelated height mechanism, and a third unrelated one in the lit solids. No shared concept, no way to apply it to an arbitrary shape. Of the four pieces, this is the only one where "new build" is the honest description.

---

## 5. The effect list is already four lists

Counted against the code — 41 effects. The total has been stable throughout; it is the breakdown *within* it that earlier drafts got wrong, so this one was recounted from scratch and then independently recounted again.

| Where they'd go | How many | What they are |
|---|---|---|
| **Shape ops** | **22** | Every bend, skew, ripple, shockwave, erosion, dissolve, wipe, stipple, melt-together and repeat |
| **Fill ops** | **9** | Brightness, contrast, saturation, posterise, colour replace, palette remap, tint, and — reclassified — fake depth |
| **Border** | **4** | Outline, glow, edge softening, edge warping |
| **Post (survivors)** | **2** | Channel splitting, drop shadow |
| **Fill generators** | **2** | Cracked cells, fake light |
| **Cull** | **2** | Two unused warps, both already condemned in the first report |

**Ten of the 41 are currently filed in a category that does not predict what they actually are.** The most striking: the **fake-depth effect is filed as a geometry warp, and at its default settings it does not move the silhouette at all** — a comment in its own implementation works through why the edge cannot move. It is a lens on the interior, i.e. a fill operation, sitting in the warping menu. (It *can* become a real warp, but only if you push its lens wider than the shape or off-centre, which two of its own dials exist to let you do — so it is a fill operation with a warp escape hatch, which is its own argument for the split.) Three effects filed as whole-frame post are really border work. One effect filed as a colour operation is literally an alpha mask. **These are not filing errors; they are the symptom.** Every one of them was put where it is because that was the only stage that could reach the pixels it needed.

**One of them will genuinely want two homes.** The ordered-dither effect turns soft alpha into a hard stipple, which makes it a coverage operation — but the same treatment is also the classic last pass over a finished frame. A model with exactly one home per effect loses one of those two uses. It is a small thing; it is also the clearest single case that the four lists are a simplification, not a partition.

**Two of these deserve calling out because they argue your case better than anything else in the document.**

**The melt-together effect is your grouping feature, implemented backwards.** It works by blurring the finished coloured picture and re-thresholding it so nearby shapes read as one blob. Its entire purpose is silhouette union across layers — done *after* colouring, by blur, because there was no way to do it before colouring. Your proposal does that same thing directly and cheaply. The effect itself still survives afterwards, as a post treatment over layers you have *not* grouped; what changes is that its main job stops being a workaround.

**The kaleidoscope effect's own documentation is a written record of the gap you are proposing to close.** It states, in as many words, that it was made a whole-frame post effect *because that is the only place that is universal* — the tool already had radial repetition, but it lives inside the particle-scattering path, so three named generators can never reach it. **That is not a design decision about kaleidoscopes. It is the shape of the hole**, described by the person who fell in it.

**The model also revives something from fully dead.** There is an edge-warping effect that cannot be reached at all today, because nothing in the tool owns the rim as a thing you can push around. Give borders a real stage and it becomes the first proper citizen of it.

---

## 6. How cleanly the generators actually split

| Verdict | Count | Who |
|---|---|---|
| **Clean** — the seam is already a boundary in the implementation | **14** | Both fire simulations, and all twelve energy-field generators including every big scene one |
| **Mechanical** — interleaved but not entangled; routine work | **13** | The six flat stamps, the five lit solids, the ring, and text |
| **Genuinely inseparable** | **1** | The imported-sprite one |
| **Not a shape at all** | **1** | The 3D playback viewer |

**Three findings inside that table matter more than the counts.**

**Nothing is inseparable because "the colour comes from the heat".** That was the intuitive objection, and it is wrong everywhere it was tested. Every generator whose colour is driven by heat, density, soot, charge or height already keeps that number in its own store and colours it in a distinct second pass. What a shape stage would have to carry turns out to be small and finite — how much stuff, one or two energy numbers, height, which way the surface faces, distance to the edge, and a couple of per-generator indices — and six of those seven are already computed somewhere today.

**The lit 3D solids are not the expensive case.** The first report assumed they were, because their lighting is computed and consumed in the same breath. In fact they already carry **five separate fills apiece** — surface, specular, inner glow, line, edge glow — plus a full authored light rig, and the code itself already calls the first one the material fill. A material stage for the solids is not something to invent; it is something to **move**. There is one real decision inside it: **does the light belong to the shape or to the fill?** If it belongs to the fill, "a box wearing churning plasma" works and the plasma is properly lit. If it belongs to the shape, the fill receives a pre-lit number and your plasma looks flat. **It should belong to the fill, with the shape exporting which way the surface faces** — which one of these generators already computes exactly, and another already fakes from its own density.

**The imported-sprite generator being inseparable is a promotion, not a problem.** Its shape and its colour are the same artwork; there is no number a fill could read. Under the new model it stops being a *fill provider* and becomes the best available **shape provider** — a silhouette brought in from outside. Which is precisely the slot the first report wants 3D Shaper to occupy. The two conclusions were reached independently and agree.

---

## 7. Where the model genuinely breaks

Not caveats. Four real design problems that must be answered before building, plus three smaller ones. Each has an answer; none of the answers is free.

### 7.1 For all nine scene generators — and for fire too — the fill decides the shape

This is the hardest one, and it is **universal across that family, not a majority case**. Those generators do not compute a silhouette and then colour it. They compute a smooth energy field and then decide *where that field becomes opaque* — a window, a curve, a floor, a speckle cleanup. **That opacity decision is what turns a soft exponential glow into a readable bolt or a jet tongue.** It is the shape, and it is currently part of the colouring. Counting fire, that is **11 of the 29 generators** — over a third of the library, and the third that contains almost all of the interesting looks.

So: swap the fill and the silhouette changes, often drastically. Worse, the shape stage cannot answer "what is my silhouette?" without running a fill — which breaks the group model (a member's contribution would be undefined until coloured), breaks the border stage (which needs a silhouette to measure from), and breaks the compatibility badges the first report proposed.

**The answer, and it is a genuine reading of the existing code rather than a patch:** split the fill into two declared halves. An **opacity rule** (energy → how solid) that belongs to the **shape** and travels with it, and a **colour rule** (energy → what colour) that is the swappable fill. Those two are already separate variables in the implementations — the window/curve/floor on one side and the colour lookup on the other. **The cost is that "fill" becomes narrower than "material": a plasma fill cannot make a solid shape wispy.** That is a real limitation on the promise, and it should be stated on the tin rather than discovered.

### 7.2 The swarm — the biggest behavioural change, hiding in the most-used feature

Today, when a layer scatters a shape into hundreds of particles, **the fill is evaluated separately for every single particle, in that particle's own local frame, at that particle's own point in its own lifetime.** There is even a named setting for this, with the canvas-anchored alternative sitting next to it. A colour ramp that runs white → orange → smoke over a particle's life is the default look of the tool, and **127 of the 305 saved layers — 42%, the single largest group — are exactly that**.

**"One fill over the assembly" deletes that.** There is no longer a per-particle life to sample. This is not speculation: the one place in Pyre that already fuses a swarm into a single field states both losses in its own comments — depth-based brightness is dropped, and a spatially-varying fill "degrades to its centre-line sample".

**The answer** is to make it a visible, named choice rather than a consequence — *fill each piece* (today) versus *fill the assembly* — and, in the second mode, have the shape stage carry a **per-pixel age**: the life of whichever particle dominates that pixel. That does not exist today and is not free, but it is the whole difference between "grouping loses your fades" and "grouping keeps them".

### 7.3 Glows: several generators paint where there is no shape

The lit solids emit a halo outside their own body. One scene generator composites an ignition flash and embers at positions its density field does not cover. Several energy-field generators blur their field *before* deciding where it is opaque, so the visible extent generally exceeds any silhouette you could name from the unblurred field.

**If coverage is a stencil and the fill paints inside it, all of that is deleted** — and it is a large part of why those generators look the way they do.

**The answer:** substance must carry **emission** as well as coverage, and a fill must be allowed to paint where emission exists and coverage does not. The alternative reading — that a glow is a *border that reaches outward* — is also defensible and there is precedent for it, but it collides with a problem the first report already found: **Pyre never gives an effect room to draw outside the canvas.** Four effects are clipped in Pyre that would not be clipped in a standalone stack. That has to be fixed regardless of which reading wins.

### 7.4 Fill has to become a genuinely new type, and seven generators have already voted

Seven of the nine scene generators explicitly declare that they do not use the layer's fill at all. (A different seven from §7.1's nine — two of them *do* take the layer fill and still derive their silhouette from it, which is why the two counts are not the same set.) That is not laziness. It is **seven independent verdicts that the fill type is not expressive enough** — one of them needs two whole colour ramps and two opacity ceilings crossfaded by a soot ratio; another needs a baked lookup table mixed against a smoke grey plus a fake light; another needs five hard colour bands by threshold.

So "any fill on any shape" means either making the existing fill type dramatically richer — and it is used across the whole library, so every change there is a compatibility risk for other tools — or introducing a new pluggable fill type of which today's is one member. **The second is right, and there is an exact precedent for how to do it inside Pyre already**: the plug-in generator system is that same pattern, with migration, cloning and change-detection all solved. But it is a new type system with a migration, not a rename, and it should be costed as one.

### 7.5 Three smaller ones, stated so they are not discovered later

**Splitting one ordered list into four destroys ordering — in two places, not one.** Pyre already sorts *bending* and *colouring* effects into separate buckets internally, so splitting those two loses nothing. But Pyre runs its whole-frame effects **in the order you authored them**, so carving three border effects out of that run changes results inside Pyre as well. And the **standalone effect stack** keeps strict order throughout — it walks the authored list and deliberately flushes so that later effects see what earlier ones produced. Any saved stack whose meaning depends on interleaving a warp between two colour operations **stops being expressible**. That is a concrete migration hazard in a real, separate tool, not a hypothetical.

**Each warp becomes three effects.** Today a warp bends the shape and the paint together, because the paint is sampled through the warped coordinate — nobody ever has to decide. Split them and each of the fourteen warps becomes: bend the shape only (paint slides under a moving edge), bend the fill only (edge holds, interior churns), or bend both. That is new interface surface, new saved state, and a new way to be confused, and it is not in the sketch. It is also, to be fair, three genuinely useful looks where there was one.

**The shape stage needs two op contracts, not one.** Fourteen of the 22 shape operations are invertible coordinate warps — cheap, composable, expressible as "where did this pixel come from". The other eight are **buffer operations** — stipple, dissolve, wipe, erode, melt-together, repeat, and two that keep state between frames. They cannot be written as a coordinate lookup. One plug-in contract will not fit both, and pretending it will is the kind of thing that gets discovered three months in.

### 7.6 The model can recreate the very bug it exists to kill

Today the nine colour operations are cheap and safe because there is always a finished pixel underneath them. In a shape/fill/border model there need not be: a shape with no fill assigned, or a group member whose colour is applied at group level, gives them nothing to act on. **That is the same silent-does-nothing failure the whole exercise is meant to abolish, relocated from "which generator" to "did anyone put a fill here".**

The mitigation is trivial — **a shape always has a fill, defaulting to flat white** — but it has to be a rule stated at the start, not a default someone notices is missing later. Worth writing on the wall, because it is exactly the class of thing that gets rediscovered as a bug report in eight months.

### 7.7 Timing — the one place the news is unexpectedly good

Your headline example is a fireball shape wearing a churning plasma surface, with the plasma running on **its own clock**. Nothing in Pyre has two clocks today: a layer has one lifetime, one frame count, and everything evaluates against it. The colour ramp's position is fed by whatever the caller happens to pass — the particle's life for a stamp, and for the field generators, *the energy value itself*, jammed into the slot where time is supposed to go.

**But the machinery for two clocks is already written and has simply never been switched on.** The animated-ramp type already separates "where am I on the ramp" from "what is the ramp itself doing over time" — hue, saturation, brightness, contrast and phase all animate independently, and there is a palette-cycling driver sitting there. **Every single call site in Pyre passes it a zero.** So a material clock is a field and a call-site change, not an engine change. Of everything in this document, this is the cheapest path to the specific picture you described.

### 7.8 And two order-of-operations traps

Two of the scene generators **anti-alias after colouring, on purpose**, and their entire visual signature depends on it: one evaluates its field at triple resolution, pushes it through a hard seven-band palette, and only then filters down. Colour a downsampled substance instead and you get blurred band boundaries where the original had crisp steps — a different picture. These are bit-exact ports whose fidelity is the point. **The clean seam has an ordering parameter**, and some generators will need to hand over at their own internal resolution.

---

## 8. Extrusion

You listed it as a peer of shapes, fills and borders. It should not be.

Extrusion adds **height and thickness**. It does not decide colour, and it does not decide the rim treatment. It is the same category of thing as bending, eroding or melting a silhouette: **it takes substance in and gives substance out.** It belongs in the shape-operations stage, alongside the other 22.

Filing it there buys two things. It automatically applies to every generator instead of needing its own compatibility story. And it makes the height information it produces available to everything downstream — a border can measure a rim on an extruded shape, a fill can shade the sides differently from the face, and the existing relief lighting can light it.

There is one honest cost. **Extrusion is the only one of your four proposals that is essentially new construction.** There is exactly one worked implementation in the whole tool, welded into the text generator, plus two unrelated height mechanisms that do not share a concept. Everything else in your addendum is finishing or joining. This is a build.

---

## 9. Grouping

**Recommendation: build a group as a first-class kind of layer, holding an ordered list of shape *members* — not as nested layers.**

One layer whose generator is "Group". Inside it, an ordered list, each entry being a shape generator plus its own offset, rotation and scale, plus its own combine mode (union / add / carve / soft-union / soft-carve), plus optionally its own time window. They resolve into one substance. That layer's single fill, single border and single extrusion then apply to the whole assembly.

**Members must not themselves be layers.** A colourless silhouette contributor and a fully-timed participant in the composition are different kinds of thing, and conflating them is the fastest way to misdesign this. 3D Shaper's layers and Pyre's layers already are two different things wearing the same word — the first report flagged that as a naming hazard before the two tools meet, and this is the same hazard one level down.

**One level of nesting, not a tree.** 3D Shaper nests four deep because a Shaper shape is a *reusable asset* that can contain another one, so nesting buys reuse. Pyre has no reusable-shape concept, so nesting would buy recursion with nothing to justify it. One level covers exactly what you described. If reusable silhouettes arrive later — which is the shape the Shaper import will take — the nesting lives inside *that* asset, not inside Pyre's layer list.

**Build it over the machinery that already exists**, not next to it. The combine operations, the shared plane they accumulate into, the "one fill over a combined field" consumer and the relief lighting are all written and debugged and have zero users. Rebuilding them would be a second implementation of something nobody uses; re-fronting them gives the existing code its first user. What is genuinely missing is **soft blending — both of Shaper's soft modes**, not one; a single-knob smooth-combine primitive exists elsewhere in the codebase and is the obvious starting point, but it is not the same control (see point 6 below).

**Do not sequence grouping and the shape/fill split as two projects.** They are one change. Today's grouping harvests only *opacity* from its members — each member is fully rendered, in colour, and then everything but its alpha is thrown away. So a member cannot contribute height, or heat, or which way its surface faces. **Grouping without the substance split is exactly today's feature, which is the feature nobody used.** It also means the substance split is a **performance win** for grouping, not a cost: members stop paying for colour they discard.

**Seven things a group must answer**, and they should be answered on paper before anything is built:

1. **Order matters and must be visible.** Carve is not commutative. Today combine mode is a per-contributor setting with no explicit ordering beyond position in the layer list, so authored order is silently significant. Put the combine mode **on the member**, matching Shaper, so a member can be dropped in as "carve".
2. **Members need a local transform.** Pyre has no per-layer transform at all — position comes from particle scattering, rotation from particle spin, scale from the size dial. A member needs an honest offset/rotation/scale that is not a swarm. Small, new, unavoidable.
3. **Whose clock does the fill run on?** Decide explicitly that **the fill runs on the group's clock**, not any member's. Members may still enter and leave mid-clip — that is the good case, and it is literally your fireball-with-plasma example: the shape changes over time while the surface churns on its own schedule.
4. **May a member swarm?** If yes, a group is a cloud of clouds. If no, you have removed the most-used feature in the tool from groups. **Recommended: yes, and a swarming member contributes its whole cloud as one contribution** — which is exactly what the existing melt-together mode already does within a layer, so it is a re-use rather than a new rule. Put the group's *own* swarm on the group layer, stamping the whole assembly repeatedly, which is what the scene generators already do.
5. **Can a group also be a mask?** Under today's code a layer is either a mask writer or a mask consumer, never both — and a group is a consumer by construction. So today a group could never feed a mask. That restriction must be lifted deliberately or written down.
6. **Soft combining.** Shaper's soft union takes two knobs, strength and sharpness. Pyre's existing smooth-combine primitive takes one. They will not behave identically; decide which is the target.
7. **Cost.** Eight members is eight full-canvas passes per frame per group. Cheaper once members stop rendering colour, but worth measuring before it is promised.

---

## 10. The uncomfortable finding

The first report's uncomfortable number was that 78% of all authored layers are a plain disc and ten of the seventeen built-in generators have never been used. Here is the version of that which applies to *this* proposal, and it is sharper.

**Three of your four ideas have already been built in some form. All three landed invisible. All three got zero or near-zero use.**

- The melt-into-one-blob mode — your grouping idea, within a single layer — is a radio button inside another panel. Used on 20 of 305 saved layers, and never once in the mask-fed variant.
- Channel-based grouping across layers — your grouping idea, exactly — works today. **Zero of 305.**
- The border stage — your border idea, with its own fill and its own buffer — works today, for six of 29 generators, with no explanation anywhere of why not the rest. **Switched on in exactly one of the 305 saved layers.** Nothing in the interface tells you the other 23 generators could have it, or that this one exists at all.

**None of these failed because the design was wrong. They failed because you cannot find them.** Each was expressed as a setting inside another feature's panel, wired by a mode or a channel number, with no name of its own in any picker.

**The implication for this project is direct.** The re-architecture is worth doing and this document says so. But if it ships as a better internal structure without the discoverability half from the first report — one generator picker instead of four hidden mechanisms, honest badges saying what each generator supports, effect rows greyed out with the reason written on them — **then it will be the fourth correct idea nobody used**, and it will have cost far more than the first three. Structure and visibility are not two phases here. They are one deliverable.

---

## 11. What this replaces, concretely

| Today | Under the model |
|---|---|
| One shape picker with 17 entries, plus 3 mechanisms that silently override it | One **Shape** picker holding every technique that actually draws, including the currently hidden ones — and, per the first report, *not* the 3D playback viewer, which draws nothing and belongs elsewhere |
| One effect list of 41, shown identically under every generator | Four lists — 22 shape ops, 9 fill ops, 4 borders, 2 post — each only offering what can act |
| A fill that 7 of 9 scene generators refuse to use | A **Fill** picker, of which today's fill is the default member |
| A border stage available to 6 of 29 generators, undocumented | A **Border** picker available to all, absorbing the three private rim implementations |
| Melt-together, and a channel-wired grouping nobody found | One **Group** shape, visible, ordered, transformable |
| Extrusion existing once, welded inside the text generator | An extrusion **shape op**, applicable to anything |
| A separate special slot for the one stateful effect | A shape op that happens to keep state — the *slot* disappears; the two fire simulations do not, and keep their "must be played from the start" badge exactly as the first report proposed |
| Effects that silently do nothing on half the generators | Effects that cannot act are not offered, with the reason shown |

**Two places where this quietly revises the first report, said out loud rather than left to be noticed.** That report proposed that the melt-into-a-blob and height-relief modes become *materials* that read height; this one makes them a **shape** (a Group) plus an ordinary fill. This version is better, because it is what they actually are — ways of combining silhouettes, not ways of colouring one. And that report proposed fusing the two height techniques into one; worth knowing before doing it that **one of the two has eight authored uses and the other has zero**, so it is a merge of a used thing with an unused one, not a merge of equals.

**What is *not* replaced, and should stop being promised as if it were:** the two post effects with no shape/fill/border role; the imported-sprite generator's inseparability; the nine scene generators' internal complexity, which is not reduced by any of this — they get a cleaner handover, not fewer dials.

---

## 12. The route

Seven slices, in five shippable stages. **Six of the seven cannot break a saved effect. Exactly one can, and it is named below** — which is a far better position than a rewrite, and is what makes "replace" unnecessary.

1. **Ungate the border stage.** Turn the existing, working border on for all generators instead of six. Days, not weeks. Immediately visible. Proves the shape/border seam is real before anything expensive is committed. **Start here** — and note the reason it is safe: **exactly one of the 305 saved layers has a border switched on at all**, on a shape that already supported it, so ungating changes **zero** existing renders. Two things to do properly rather than quickly: the gate is two conditions, not one, and three generators already draw a private rim of their own that would now have a public one beside it.
2. **Split the effect list into four.** Pure interface work, no engine change. Kills the single biggest source of "why did that do nothing" — and does it before any architecture changes at all. One caveat carried from §7.5: pulling the border effects out of the whole-frame run changes ordering, so this is interface work with one real behavioural edge.
3. **Name the substance.** Promote the per-pixel value that already exists into a documented, named, per-generator promise, and fix the five places it currently means five different things. **This is the one slice that changes existing output.** Two of those five fixes are not cosmetic: one switches an effect that is currently dead back on, and one re-bases a value from canvas-relative to shape-relative, which re-tunes every standalone stack using the two effects that read it. Small, contained, and much cheaper now than after anything is built on top.
4. **Make fills pluggable.** Introduce the fill plug-in type with today's fill as its default member, byte-identical. Migration risk lives here; nothing else waits on it being finished.
5. **Substance opt-in, generator by generator.** Add the new handover as optional, defaulting to the old behaviour. Nothing is forced to move, ever. The 14 clean ones opt in most easily — several already have the seam as a function boundary — but "cheaply" is too strong for the plug-in scenes: the codebase already contains one generator that explicitly refuses the equivalent whole-buffer treatment because it does not look the same as being warped sample by sample. **Expect at least one to need special handling, and budget for finding out which.**
6. **Group as a real layer kind**, over the existing combiner. **Slices 5 and 6 are one project, not two** — grouping without the substance handover is exactly today's unused feature — so treat them as a single stage that ships together.
7. **Extrusion as a shape op**, and the warp target selector. Last, because both are genuinely new and both are more valuable once everything else can receive them.

**And running alongside all seven, not after them:** the badges, the picker unification and the greying-out from the first report. That is the part that decides whether any of this gets used.

---

## 13. Questions only you can answer

The first report left six. These are five more, all created by the addendum, and none of them can be inferred from the code.

1. **Is a fill evaluated per pixel, or over a whole buffer?** This single decision changes which effects can be fills at all. Per-pixel and only the cracked-cell pattern qualifies; whole-buffer and the fake light, the glow and one of the shockwaves all become fills too. **This is the most consequential open question in the document.**
2. **Does light belong to the shape or to the fill?** (§6.) It should be the fill — otherwise plasma on a box renders flat-lit — but it moves real work.
3. **In a group, is the fill applied per member or over the assembly?** (§7.2.) Per member is today's behaviour and keeps lifetime colour fades; over the assembly is what your proposal describes and loses them unless a per-pixel age is added. Probably both, as a named choice — but that is a decision.
4. **How much fidelity are you willing to spend?** Two scene generators anti-alias after colouring and their look depends on it. Handing over at their internal resolution preserves them at a memory cost; forcing them into the standard seam changes what they look like. Neither is wrong.
5. **Does the rich fill promise apply to the scene generators at all?** Mechanically it can. But "brushed metal on an explosion" is not a feature anyone asked for, and an any-shape × any-fill menu is mostly meaningless cells. Should generators **recommend** fills and mark the rest as unusual — the machinery for greying with a reason already exists — or is orthogonality the promise?

---

## 14. Confidence and limits

**Measured and solid:** the 41 effects and their classification; the count of 29 generators and their decomposability verdicts; what already exists for fills, borders, extrusion and grouping, including the zero-usage figures; the fact that seven scene generators explicitly decline the layer fill; the five different meanings of the substance value. All read directly from the source. The total of 41 has been stable all along — it was the breakdown inside it that an earlier draft of the first report got wrong — and every count in this document was recounted from the source and then independently recounted again by a separate pass.

**Read the usage ratios, not the totals.** The figures here — 305 layers, 78% discs, 42% lifetime fades, zero grouping, one border — are counted across four copies of this project on this machine, three of which are clones of the fourth. The real body of authored work is about **32 effects and 79 layers**, counted roughly four times over. The proportions hold either way; the absolute numbers are inflated about fourfold. A further 13 authored layers sit in per-project libraries on an older schema and are excluded entirely.

**Inferred but strong:** every judgement about what would be *lost* by a split. Those come from reading the arithmetic, not from rendering a before and after. The four hard cases in §7 are the ones where a rendered comparison would most change the picture.

**Not verified at all:** **nothing here was checked by looking at Pyre running.** No frame was rendered, no window opened. This is the same limit as the first report and it has the same consequence: **before anything is deleted or restructured on the strength of this document, look at it.**

**One specific unverified claim worth acting on separately.** While measuring the fill system, a probable live bug surfaced: **editing the colour ramp of a fire, fireball or text layer may do nothing at all.** Those four places read a frozen legacy copy of the ramp rather than the live one, the editor replaces the live object rather than modifying it, and — worse — the change-detection that decides whether to re-render reads the same stale copy, so it would not even notice. The code path is confirmed by reading; **it has not been reproduced in the editor, and it should be before it is fixed.** It is worth mentioning here because it is the proposal's own case study: a fill that four generators reach *past* instead of *through*, because the fill's shape never fitted what they needed.

**On the process, since it decides how much weight to give this.** The document was built from two fresh investigations on top of the first report's four, and then **adversarially checked by a separate pass whose only job was to attack it**. That pass found twelve factual errors in the draft, all corrected above — including a claim about which generators are affected by the hardest objection that was wrong in *both* directions, a percentage attached to the wrong noun, and a "nothing breaks" promise that had one exception hiding in it. It also found nine places where the draft was more confident than the evidence, and nine things missing that argued against the proposal — §7.6 and §7.7 exist because of it.

**§7 is the most valuable section here, and it is where to push hardest if you want to break this.** It was written to be as hostile to the proposal as the evidence allows, and then made more hostile by the checking pass. The proposal survived; the numbers around it needed a lot of work. **Treat the design argument as solid and any individual number as worth re-checking before it justifies a deletion** — which is the same advice the first report ended on, for the same reason.

---

*Detailed evidence with file and line citations is in the working notes at `D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0098\` — six files now. The two written for this addendum are the effect triage and the decomposability, grouping and hard-cases study; the four from the first report cover the generator inventory, the compatibility rule, the 3D Shaper study and the verification pass. Those are for whoever implements this. This document, like the first, deliberately carries no citations.*
