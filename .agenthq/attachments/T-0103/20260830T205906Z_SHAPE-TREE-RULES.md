# The shape tree — the rules, locked

**Wave 1, T-0103, 2026-08-30.** This document closes the seven questions design sections B2 and C3 left open. It is a decision document, not an investigation: where a question had an answer implied by the design I state it and move on, and where it genuinely had no answer I make one and give the reason, so it can be overruled by a sentence rather than re-argued from scratch.

There is no code here, and none should be written against anything in this document that is marked as flagged or open until those marks are cleared.

Two things were checked fresh for this document rather than taken from the four earlier rounds: the actual Pyre source at `D:\UNITY\Laubrary Dev\Assets\Packages\Laubrary\Runtime\Pyre` and `D:\UNITY\Laubrary Dev\Assets\Packages\Laubrary\Editor\Pyre`, and 3D Shaper's actual source, which turns out to be three files in its own AgentHQ folder at `D:\CODEZ\AgentHQ\3D Shaper` — `public\index.html` is the entire renderer. The full evidence, cited to file and line, is at `D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0103\CODE-FACTS.md`. Six assumptions carried by the earlier rounds turned out to be wrong, and they are named in the last section rather than quietly corrected.

---

## The seven, in one line each

| # | Question | The rule |
|---|---|---|
| 1 | Visible ordering | A bag's children are an ordered **sequence**, not a set. The combine mode is on the member. The list reads bottom-to-top like the layer stack: the bottom member is the base, members above it add to or carve it. Nothing may reorder them, ever. |
| 2 | Local transform | Each member carries a full 2D affine transform — translate, rotate, scale, skew, **origin** — composed down the tree and applied by transforming the *sample point*, never by resampling a child's pixels. Distances are rescaled into canvas pixels on the way back up, which is what keeps the B7 bound composing unchanged. |
| 3 | Whose clock | A fill always runs on the clock of **the node it is attached to**. Since a bag owns its subtree's fill by default (C4), that is the bag's clock. A node's clock is its parent's clock remapped through its own window — the same composition rule as the transform, applied to time. |
| 4 | May a member swarm | Yes, and its whole cloud is **one contribution**. Which forces the real rule: a swarm resolves into a coverage *field* before the member publishes, never into pixels. |
| 5 | May a bag be a mask | Yes — because in this model the question dissolves. There is no mask role. Every node publishes coverage, and a *consumer* names the node it reads. The writer-or-consumer split goes away with the enum that caused it. |
| 6 | Soft combine | Two knobs, taken from 3D Shaper and then fixed twice: **blend width** authored in pixels rather than as a fraction of the canvas, and **blend profile**. And there are three modes with a width dial, not four modes — "soft" is a property of the seam, never of the amount. |
| 7 | Per-frame cost | A node costs one bounded pass, **not** a canvas pass — which requires every primitive to declare a **support extent**, a second number alongside B7's bound and not a free consequence of it. Cost is stated in node-pixel evaluations per frame, four named multipliers are cliffs rather than slopes, and the number is shown to the author before it costs them a hang. |

---

# R1. Visible ordering, and where the combine mode lives

**The combine mode is on the member.** A bag holds an ordered list of children; each child carries its own mode. The bag itself carries no mode, exactly as 3D Shaper does it — verified: its component record holds the mode and its container holds none, and stored order is evaluation order.

**A bag's children are a sequence, not a set.** Subtraction does not commute, and the implementation in both tools already reflects that: 3D Shaper folds one scalar left to right across its components, and Pyre's own matte channel is one shared array mutated in layer order with a clamp after each step. So the ordering is *data*, it is authored, and it means something. The consequence that has to be written down, because it is the kind of thing an optimiser breaks eighteen months later:

> **No stage may reorder a bag's members.** Not to batch the additive ones, not to group by generator, not to improve cache locality, not to skip ahead. A member's index is part of the meaning of the document and part of every cache key that touches it.

**Direction: the bag list reads like the layer stack.** The bottom of the list is evaluated first and is the base of the silhouette; each member above it applies onto the accumulated result — so the top item is the one applied last, exactly as the top of a layer list is the one in front. That is deliberate: B2's whole reason for having one nesting system is that two lists which look identical must not disagree about which end is which. A member above another carves or covers it, in a bag exactly as on the layer stack.

One honest caveat on that. It is established that Pyre's index 0 is evaluated first and sits at the back, but **which end of the *drawn* list index 0 appears at is unverified in both tools.** The rule above is the rule regardless; what needs checking before any document is ported is whether an existing tool draws its list the other way up, because an inverted list silently reverses every authored subtract — and a reversed subtract does not look like a bug, it looks like a shape.

**The first member combines against an empty bag.** The accumulator starts empty, so a bottom-most member set to Add gives itself, and a bottom-most member set to Subtract or Intersect gives nothing. 3D Shaper already produces exactly that result — its `1e6` seed for a leading subtract *is* an empty accumulator, since in a distance field a very large positive value means very far outside. What it does not do is tell the author. So our addition is not a change of behaviour, it is **a UI flag: a leading non-Add member is marked with the reason shown**, in the same style B4 already requires for a fill asking for a quantity its shape does not publish. One implementation note carried from that reading: the empty state should be an explicit sentinel rather than a magic `1e6`, which a large enough canvas could one day collide with.

**Three modes, not four.** Add, Subtract, Intersect — each with the blend width of R6. Intersect is new: neither tool has it today. 3D Shaper has add/subtract/soft-add/soft-subtract and no intersect at all; Pyre has no silhouette combination whatsoever, only a three-mode matte channel. B2's list of four ("add, cut out, keep only the overlap, blend softly") is therefore reinterpreted here as three modes plus a dial, which is a small divergence from the design's wording and is flagged as such at the end.

**What this costs.** Nothing structural. Ordering is already how both tools work; what is new is Intersect and the refusal to seed a leading subtract.

---

# R2. The per-member local transform

Pyre has nothing that can serve. It does have per-layer `shapeOffsetX/Y`, `shapeScale`, `shapeRotation`, `shapePitch` and `shapeYaw`, and they are animatable — but the function that applies them takes an absolute point, bakes a perspective scalar into the result, and returns another absolute point. It does not compose, there is no matrix anywhere in Pyre, and there are exactly three hardcoded tiers (layer, whole-cloud, per-particle) with no layer ever referencing another. A nesting transform is new construction. 3D Shaper, by contrast, already has the whole thing.

**The rule: adopt 3D Shaper's transform block verbatim — translate, rotate, scale, skew, origin — as a 2D affine matrix, composed `T · P · R · S · K · P⁻¹`, and apply it by transforming the sample point down the tree rather than transforming the shape up it.**

Four reasons, in order of how much they matter.

**It is the only form compatible with B9's generator contract.** B9 rules that a generator is handed a block of the canvas and fills in rows, and is never asked what colour pixel 4,7 is. Inverse point-chaining preserves that exactly: the block's sample points are pushed down through each node's inverse matrix, and every primitive still answers over a plain run of points in its own local space. The alternative — rasterising each child into its own buffer and transforming the buffer — costs one full buffer per node, one resampling filter per node, and a generation of quality loss per level of nesting. That alternative also multiplies R7's cost by the number of nodes rather than leaving it bounded. This decision is therefore load-bearing for two of the other six answers.

**Origin is the fix for a known bug class, not a nicety.** The warps in the standalone effect stack pivot at the top-right corner of the canvas rather than its centre, affecting ten of the seventeen warp effects, because the pivot was an implied convention rather than an authored field. Making the pivot an explicit part of every node's transform means there is no convention left to get wrong. Skew comes along free once there is a matrix at all.

**Pitch and yaw are not invited.** Pyre's `shapePitch`/`shapeYaw` bake a perspective scalar and belong to B7's extrusion and resolve stage. The shape tree's transform is strictly 2D affine. Anything out of plane is B7's business, and mixing the two is how the flat-first ruling gets quietly undone.

**Distances come back up in canvas pixels — this is the rule that makes everything else compose.** Under inverse point-chaining a child answers in *its own local units*, so a member scaled four times would report a distance four times too large and R6's blend width, authored in pixels, would mean nothing. So:

> **Only the sample point travels down in local space. Every node publishes its distance rescaled into canvas pixels**, by multiplying by `minStretch(M)` — the *smallest* singular value of the linear part of its matrix — at each level on the way back up.

`minStretch` and not `maxStretch`, and this is worth being careful about because the direction is easy to invert. Under a linear map, a parent-space distance relates to the child-space distance the child reports by a factor somewhere between the smallest and the largest singular value. Taking the smallest is the conservative choice — it can only understate the true distance, never overstate it — which is what a distance field must do to stay sound. The concrete check: halve a member, so the linear part is `0.5·I` and a child unit circle becomes a parent circle of radius one half. A parent point at `(1.5, 0)` maps to a child point at `(3, 0)`, where the child correctly reports a local distance of two. The true parent distance is one. Rescaling by `minStretch = 0.5` gives exactly one; passing it through unscaled would overstate it by a factor of two, and multiplying by the *largest* singular value would make it worse still.

**With that rescaling, the B7 bound composes unchanged.** B7 requires every primitive to declare a bound on how far it may claim its own edge is, and calls composing those bounds the single most expensive thing to retrofit later. Because the rescaling above already absorbs the transform, **a member publishes `bound_child` — the same number — and a bag publishes the maximum over its members.** That is a far better outcome than a bound that inflates at every level, and it is the reason the rescaling belongs in R2 rather than being left to whoever implements R6.

**The honest cost of that choice:** under a non-uniform scale or a skew, rescaling by the smallest singular value understates distance along the stretched axis, so a soft seam is narrower there than the width the author typed. That is a visible consequence of anisotropy, it is conservative rather than wrong, and it should be stated in the UI rather than discovered.

**Where the largest singular value *is* the right number: the extent box of R7, and nowhere else.** The two are different quantities and must not be shared or conflated. Which brings up the one thing B7 does not give us:

> **Every primitive declares two numbers, not one.** B7's edge-distance overestimate bound, *and* a **support extent** — a local-space bounding radius or box saying where it can put coverage at all. The bound is a ratio and carries no spatial information whatsoever, so a bounding region cannot be derived from it; R7's entire cost model needs the extent, and the extent is therefore a second, new item on the primitive contract rather than a free consequence of B7. It transforms by `maxStretch(M)`.

Two further rules the transform needs to be complete:

**A singular transform publishes nothing.** Scale zero, or a degenerate skew, makes the matrix non-invertible, and inverse point-chaining then produces a degenerate or unbounded result rather than an error — 3D Shaper, for instance, substitutes a tiny epsilon for a zero determinant, so the result is huge and finite rather than obviously broken. A member whose linear part has a determinant below epsilon publishes empty coverage and is flagged. This is cheap now and is otherwise a mystery bug reported as "my shape vanished and the preview went black".

**A bag has a transform too, and it applies to the assembly.** A bag is a node like any other. A sample point passes through the bag's inverse, then the member's inverse, then reaches the primitive. Depth is uncapped in the data, per B2; what constrains it is R7's budget, not an arbitrary limit. For reference, 3D Shaper caps at four levels and 512 leaves — a limit worth knowing about and not worth copying, since the reason to stop nesting should be a cost the author can see rather than a number in a constant.

**Transform fields are animatable**, on the same terms as any other dial, which is R3's business.

---

# R3. Whose clock a bag's fill runs on

**The rule: a fill runs on the clock of the node it is attached to.**

That is the whole answer, and it is deliberately phrased so that "the bag's clock" falls out of it rather than being a special case. C4 already decides *which node owns the fill* — the nearest ancestor that owns one paints the whole subtree, and the default for a new bag is that the bag owns it. Combine the two and the default answer is the bag's clock, without a second rule to remember. A member that takes its own fill takes its own clock along with it, automatically and consistently.

The reason this matters, from the first round and still correct: if the fill ran on any member's clock, the whole "fuse several shapes and texture the assembly as one" case collapses back into per-member colouring, because a fill would then have N clocks and no way to choose between them.

**What a node's clock is.** Every shape node has a time window, and publishes a local normalised time: **its parent's clock, remapped through its own window.** The root layer's clock is the document's normalised life remapped through the layer's window — which is exactly what Pyre's `plan.layerLife` already is, and it is already what a layer body runs on. Children of a bag inherit the bag's clock, each remapped through their own window. So time composes down the tree by the same shape of rule as space does in R2. One idea, two applications. That is worth stating out loud because it is what makes the model learnable rather than a list of behaviours.

**A member cannot shift the bag's fill.** A member with its own window animates its own *shape* on its own clock; the bag's fill still paints on the bag's clock. This is the point of the ruling and the thing that must not erode.

**Windows currently do two jobs and must be split.** In Pyre, `startFrame`/`endFrame` remap the layer's time onto a sub-range *and* gate the layer to nothing outside it — you cannot use the remap for phase or speed without also making the layer disappear. Nothing else exists: there is no time offset and no time scale field anywhere in Pyre. So:

> A node's window keeps controlling **when it exists**. A separate repeat mode — clamp, loop, ping-pong — controls **how its clock runs inside that window**. This is what makes several members of one bag animate out of phase, which is currently impossible, and it is new construction.

**Age is not the clock, and must not be confused with it.** The second round worried that filling over an assembly rather than per member loses lifetime colour fades. B4's named-quantity table already prevents that by carrying `age` per pixel, but the mechanism has to be stated: **age travels *with* the coverage, in its own plane, and is not derived from the clock at paint time.** A bag's fill running on the bag's clock still reads the age deposited by whichever member or instance put that pixel there.

**Which forces one rule past the seven, because R3 makes it unavoidable — how named quantities combine.** When two members overlap, the bag has two candidate values for heat, density, soot, depth, age and surface direction. The rule:

- Continuous quantities — heat, density, soot, depth, age — combine as a **coverage-weighted average** of the contributing members at that pixel.
- Surface direction combines as a coverage-weighted average, then renormalised.
- Identity-like quantities — instance id, and anything else discrete — take the value of the **strongest contribution** at that pixel, since averaging an identifier is meaningless.
- **A Subtracted member contributes no values at all.** It removes coverage; it does not deposit heat.

Coverage-weighted average rather than winner-takes-all because winner-takes-all puts a hard switch through the middle of a soft blend band, which is visible and which defeats the point of having soft blending at all. This is flagged at the end as an extension past the seven questions; it is included because R3 cannot be implemented without it.

---

# R4. Whether a member may itself swarm

**Yes. A swarming member contributes its whole cloud to the bag as one contribution.** That was the first round's recommendation and it is right. But the fresh code reading turns it from a permission into a requirement with teeth, because of one fact:

> At layer granularity Pyre's swarm already yields a single contribution — one `Color32[W*H]`, composited once. **But with coalesce off — which is the default, and which is every form in the tool today — the particles composite `Over` into the layer's byte buffer one instance at a time.** The union is baked into bytes and cannot be recovered as a field.

That path cannot be used inside a bag. The next member up may need to subtract from the cloud, and you cannot subtract from pixels that have already been alpha-composited — the information about where the cloud's edge was is gone. So the real rule is not about permission:

> **A swarm resolves into the member's coverage field before the member publishes. Never into pixels.** The instances combine into one coverage plane using the swarm's own combine — union by default, with the same blend width available as R6 — and the member publishes that single plane.

This is new construction and is *not* today's `Fuse`. Fuse collects instances and resolves one silhouette, which is structurally the right shape, but it does it by summing a metaball density and thresholding it, which is a different look with three knobs of its own. Fuse stays available as a deliberate choice; it is not conscripted into being the union path.

**A bag may swarm too**, since a swarm is a modifier on a node and a bag is a node — a swarm of the whole assembly. Recursion is allowed in the data. Today it does not exist at all: Pyre's swarm renderer is not recursive, a spawn point has no swarm of its own, and every reference reads the layer's own flag.

**Swarm counts multiply along the path, and the author sees the product.** A bag swarming eight times, holding a member swarming eight times, is sixty-four evaluations of that member. The rule: **the product of swarm counts on the path to a node is shown on that node.** This is C7's "which implementation is in use is shown, not hidden" applied to the number that actually hurts. The author should buy sixty-four evaluations knowingly, not discover them as a hang.

**Simulation members may not be generically swarmed.** C7 already rules that simulation generators need many seeds inside one simulation rather than many simulations, because a realistic swarm of them costs on the order of two hundred times a single instance — a figure re-derived and checked in an earlier round, from cell-update counts rather than a stopwatch. Inside a bag that cost compounds silently under the nesting above it, so the rule needs restating at member level: **a simulation member that has not implemented the native many-seeds path shows the generic swarm greyed out, with the reason.**

**Swarming replicates the shape stage only — which is what makes R3's chain well-defined for a swarmed bag.** A swarmed node's instances each run on their own clock, but the node's **fill and border run once, on the node's own clock, over the single resolved plane.** Per-instance variation reaches a fill only through the named-quantity planes of R3 — age, instance id — and never by giving the fill several clocks. Without this, a bag that swarms would have no defined answer to whether its gradient scrolls in lockstep across all its instances or per instance; with it, the answer falls out of R3 and R4 together rather than needing a rule of its own.

**Instance clocks extend R3's chain.** An instance's clock is its member's clock remapped through that instance's own lifetime. The generic wrapper already works this way — verified this round, including that its default envelope deliberately staggers births across the first half of the timeline. Whether the large native forms do the same is **disputed between rounds and is flagged as unresolved** at the end; it does not change this rule, only how much porting work sits behind it.

---

# R5. Whether a bag can also be a mask source

**Yes, and the interesting part is that the question dissolves rather than being answered.**

Today's constraint is real and is enforced in code, not merely conventional: there is a role enum with three values — draw, write-matte, luma-matte — and three separate guards, and both mask roles return early before the consumer fields are ever populated. A layer is a writer or a consumer and never both. A bag is a consumer by construction, so under today's code a bag could never feed a mask.

But the reason for that restriction is an implementation accident rather than a concept. In Pyre a mask is a *separate scalar plane*, written by a *special kind of layer*, into *one shared channel*. In the new model **every shape node already publishes coverage, and coverage is exactly what a mask is.** There is nothing left to be a special kind of.

> **The rule: drop the role enum. There is no mask layer. Any node's published coverage may be read as a mask by any later node, and the reference lives on the consumer — a node does not declare that it is a mask, a consumer declares which node it reads.**

Inverting the dependency this way kills, at once, all three of the concrete breakages that made the exclusivity necessary:

- *Read-before-write.* Today a consumer's channel pointer is fetched before the layer renders and the mask is written after, so a layer reading its own channel would read its own previous state. With the reference on the consumer, the reference names a node that must already be resolved — it is a dependency edge, and the resolve order follows from the graph instead of being a convention the code has to defend.

**Two orders exist and they never conflict — but they must not be confused.** The **fold order** is authored, immutable, and is what R1 protects: it governs how a bag's members combine into coverage. The **resolve order** is topological over the DAG: it governs *when* each node's own coverage gets computed. If the first member of a bag masks off the third, the fold still applies the first member first while the resolve computes the third one earlier. R1's prohibition on reordering binds the fold order only, and says nothing about the resolve order.
- *Shared-array corruption.* Today there is one mutable channel, so a layer that both wrote and read it would corrupt every consumer above it. Each node's coverage is its own plane; there is no shared mutable channel to corrupt.
- *Stale caches.* Today the layer cache key explicitly excludes the clip and luma fields, so a both-ways coupling would serve stale buffers. With the reference as an ordinary upstream input it is part of the consumer's key by B9's existing rule that a key covers own plus upstream settings — no special case needed.

Three rules the change needs to be complete:

**The reference graph is a DAG, and a cycle is refused at edit time** with the offending edge named. With references on the consumer a cycle becomes expressible for the first time; catching it at edit time is cheap and catching it at render time is not.

**Visibility and being-read-are independent.** Today a matte layer is invisible by construction. In the new model a node used as a mask is still drawn unless the author hides it, and hiding is its own flag. This is strictly more capable and removes a surprise.

**Coverage references and luminance references have different depths.** A *coverage* reference depends only on the source node's shape stage — cheap, and available early. A *luminance* reference (today's luma-matte) depends on the source's full resolve: fill, border, and pre-composite effects. Distinguishing them is what lets the common case stay cheap and the expensive case stay correctly ordered. And per C5, an outward border joins its node's published coverage by default, so a mask taken from a bordered node includes the outline — which is the intuitive result and is already the design's stated default.

**A bag reading one of its own members as a mask is allowed.** It is an ordinary edge inside the bag, not a cycle, and it is precisely the case today's split makes impossible.

**This is the largest departure in the document**, and no owner ruling exists on it — the earlier rounds catalogued the exclusivity as a defect requiring deliberate lifting and left it there. So the cheaper fallback is named explicitly, in case this is more change than wanted: keep the role enum and simply permit a combined draw-plus-write role, fixing only the read-before-write ordering. That preserves today's structure, costs much less, and gives up the DAG, the per-node planes, the cache correctness and the coverage-versus-luminance distinction. It is worse, and it is one sentence away if preferred.

---

# R6. Which soft-combine control is the target

**Two knobs, based on 3D Shaper's pair, with two defects fixed and the four modes collapsed into three.**

**Where the choice actually stands.** Pyre has one soft merge with one knob — a smooth-max used to melt one layer's own dome swarm together — and it has never acted between two shapes. 3D Shaper's soft modes take a viscosity and a sharpness. An earlier round already established that both of 3D Shaper's soft modes are missing from Pyre entirely and that this needs new renderer code either way. **That is the fact that decides it:** since neither option is free, the one-knob version buys nothing in implementation cost, and the only argument left for it is UI simplicity — which B2's own rule already handles. Complexity not in use is not on screen: ship the second knob collapsed behind a good default, exactly as a bag with one child renders as its child. The UI argument dissolves, so take the more expressive control.

**The knobs are genuinely orthogonal, and this was worth checking rather than assuming.** 3D Shaper's blend is `min(a,b) − hⁿ·k/(2n)` with `h = max(k−|a−b|, 0)/k`, `k` driven by viscosity and `n = 8^sharpness` ranging over one to eight. `k` sets how *wide* the blend band is; `n` sets the *profile* within that band, from a broad merge at one end to a much tighter surface-tension fillet at the other. They are not two dials on one axis.

**Today's Pyre look is *not* on this control's axis, and that is a real if small cost.** It is tempting to say Pyre's existing one-knob smooth-max is simply the `n = 1` end of the new control, and it is not: Pyre's is the polynomial form, whose pull-in from the minimum is quadratic in the gap and peaks at `k/4`, whereas `smoothMinShaped` at `n = 1` has a pull-in that is linear in the gap and peaks at `k/2` — a different curve with twice the depth. So either accept that the one place Pyre softens today changes look in the port, or carry the polynomial as a third profile setting. Recommended: accept the change, since that one use is a layer melting its own dome swarm together rather than anything authored across shapes, and a third profile is a permanent knob bought to preserve one internal effect.

**Fix one: the width is authored in pixels.** 3D Shaper computes `k = viscosity · min(shapeWidth, shapeHeight) · 0.55` — proportional to the *member's own size*, not to the canvas. The consequence is subtler than a resolution dependence but just as unwanted: resizing a member silently rescales its seam, and two members of different sizes set to the same viscosity get visibly different fillets, so the control does not mean one thing. **Blend width is authored in pixels and means pixels** — which is exactly what R2's rescaling of published distances into canvas units makes possible.

**Fix two: soft is a property of the seam, never of the amount — so there are three modes, not four.** 3D Shaper's soft-subtract reads viscosity twice: once as the amount of cut and once, through a sine, as the width of the blend band. The result is that strength zero is an exact no-op, strength one is a hard cut, and softness peaks in the middle — so the author simply *cannot ask for* a full-depth cut with a soft edge. Only its sharpness is a clean axis. That overload must not be inherited. The clean form:

> **Add, Subtract and Intersect, each with a blend width and a blend profile. Width zero is the hard boolean. "How much" is not a blend parameter — a partial cut is expressed by the member's own coverage.**

This removes the overloaded knob, and it gives soft *intersect* for free, which 3D Shaper does not have in any form.

**Combining happens on signed distance, not on coverage.** This is the implementation constraint that makes the rest of it true, and it locks R6 to R2 and B7. A smooth-min composes *distances*; blending two already-antialiased coverage values does not produce a fillet, it produces a cross-fade, and the two look nothing alike. So a primitive that can publish a signed edge distance does so, combination happens on that distance, and coverage is derived from the combined distance at the end.

Conveniently the bound survives, and this was checked rather than hoped: differentiating `smoothMinShaped` gives weights of `1 − h^(n−1)/2` and `h^(n−1)/2` on its two arguments, which are non-negative and sum to exactly one for every `n` in the range. It is a convex combination, so the gradient magnitude of the result never exceeds the larger of its inputs'. Hard min, max and negation preserve the same property. So R2's rule — a bag publishes the maximum bound over its members — holds through soft combines as well as hard ones, for every profile setting.

**Not every generator can publish a distance, and the design says so.** C3 defines coverage as soft and unbounded, *a fog rather than a stencil*, and a fog has no signed edge — which is true of the simulation generators, the fire family and the Tapestry ports. Requiring a distance from everything would quietly reduce that whole family to a hard edge with an anti-aliasing band. So:

> **A generator declares which domain it combines in.** One that can publish a signed field combines in the distance domain and gets the full blend width. One that publishes only a fog declares itself **coverage-only**: it combines in the coverage domain, with hard Add, Subtract and Intersect available and the blend width greyed out with the reason shown. Which domain a member is combining in is shown, not hidden — the same rule C7 already sets for which swarm implementation is in use.

A bag may mix the two; the fold simply switches domain per member, converting the accumulator to coverage at the first coverage-only member and staying there. That the conversion is one-way, and therefore that a coverage-only member low in a bag disables soft blending for everything above it, is a real limitation and belongs in the UI rather than in a comment.

---

# R7. What a bag costs per frame

**First, the premise in the question is not true today, and should not become true.** "Every node is a pass over the canvas" is the worst case, not the rule. In Pyre the particle rasters are already bounded by a bounding box; what actually costs a full canvas pass is a specific short list — the background, the composite, the matte write, the border distance transform twice, the luma apply, and global post — plus two passes that are *worse* than full-canvas, the fused-field render and the dome accumulation, which are the whole canvas once per particle. And there is a cliff already in the code: enabling any geometry modifier makes *each particle* scan the entire canvas. That behaviour is the single worst thing in the current render path and must not be carried across.

**The model. A node costs one pass over the pixels it can affect** — which needs the **support extent** R2 adds to the primitive contract. This is worth being precise about, because it is tempting to think B7 already paid for it and B7 did not: B7's bound is a *ratio*, a statement about how far a field may overstate its own edge distance, and a ratio carries no spatial information at all. You cannot get a box out of it. The extent is a separate, new declaration, and R7's whole cost model rests on it.

- A primitive's declared support extent, transformed by `maxStretch(M)`, gives a conservative box in canvas space.
- **A bag's box is folded in member order, exactly as its coverage is.** Start empty; an Add member unions its box in; an Intersect member intersects the accumulated box with its own; a Subtract member leaves the box unchanged, since it can only remove coverage. Order matters here for the same reason it matters everywhere else in R1: `[Add A, Intersect B, Add C]` is `(A ∩ B) ∪ C`, and the order-blind shortcut of "union the adds, then intersect the intersects" gives `(A ∪ C) ∩ B`, which throws away everything in C that lies outside B. An under-conservative box does not fail loudly — it silently clips real geometry, and the result looks like a broken generator rather than a broken bound.
- Every box is then dilated by the blend width of R6, because a soft seam reaches outside both operands. With that dilation in place, a Subtract member genuinely never enlarges the box even when its seam is soft: soft subtraction only ever increases distance and shrinks coverage, and its seam lies within one blend width of the subtractor's surface, which is already inside the dilated result.

So the answer to "what does a bag cost" is: **the sum of its members' bounded areas, not the member count times the canvas.** For most real documents that is dramatically less, and it degrades gracefully to the canvas-pass worst case only when the shapes genuinely fill the canvas.

**The unit, and an honest number.** State cost as **node-pixel evaluations per frame** — the sum over nodes of that node's bounded pixel count. Taking the design's own worst structural example, a dozen layers each holding a bag of four sub-shapes, sixty nodes, at *fully unbounded* cost:

| Canvas | Per frame | Per 16-frame animation |
|---|---|---|
| Pyre's assets today, 64² = 4,096 cells | ~246,000 | ~3.9 million |
| B9's default, ~6,100 cells | ~369,000 | ~5.9 million |
| B9's stated worst case, ~68,000 cells | ~4.1 million | ~65 million |

The first row is a floor, not a target — it is what today's assets happen to use, and B9 explicitly says to budget against its own two figures rather than against sixty-four square. At the reading-level estimate of roughly ten to thirty float operations per covered pixel, the default case is order 10⁶ operations a frame and the worst case order 10⁸. **Every number in that table is arithmetic from reading source, not a measurement**, and R7's last rule is about fixing that.

**Four multipliers, and each is a cliff rather than a slope.** Naming them is most of the value of this section:

1. **Canvas area** — quadratic. Today's real 64² is 4,096 cells; B9's worst case of about 68,000 is over sixteen times that.
2. **Node count** — linear in depth times breadth. The least dangerous of the four.
3. **Swarm counts, which multiply along the path** (R4). The only super-linear multiplier under the author's direct control, which is exactly why R4 requires the product be shown on the node.
4. **Simulation members** — on the order of two hundred times, and not fixable by wrapping (C7).

And a fifth that exists today and must be left behind: **a geometry modifier must never turn a bounded raster into a full-canvas scan per instance.** Modifiers stay bounded.

**The budget rule.** B9's requirement is that the dial moves and the preview moves. The rule: **the preview carries a per-frame node-pixel budget; the document's current figure is shown against it; and when a subtree exceeds it, the author is told which node, by name.** Not a hard cap — a hard cap makes some legitimate documents unauthorable. When over budget the preview degrades in rate or resolution and *says* it is degrading. Never silently, and never by simply becoming slow with no explanation.

**Caching, restated as two shape-tree rules.** Today's caches key on every serialized field plus the layer index plus canvas and frame counts, with the frame carried as a separate slot index rather than in the key — and they are backed by a 64 MiB LRU. Two things follow for a tree:

- **Keeping time out of the key is right, and a tree can exploit it much harder than a flat list can.** Each node declares whether it is time-varying — any animated dial, any clock-reading generator, any swarm with per-instance lifetimes — **anywhere beneath it, *or in any node it references, transitively*.** That second clause is not optional: R5 makes the graph a DAG with mask edges that are not parentage edges, so a structurally static node reading a mask from an animated one is time-varying without any of its own children being so, and a rule that only walked the subtree would cache it once and be wrong on every frame but one. Time-variance propagates along every dependency edge, not only along parentage. **A time-invariant subtree is evaluated once for the whole animation, not once per frame.** With frame counts of six to forty-nine, that is the largest single saving available, and it falls straight out of R3's clock chain.
- **One evaluation path, used by both the preview and the bake.** Today there are three caches — the layer cache, the prepass cache and the editor orchestrator's — and none of them is used by the runtime bake path at all. Two paths is how a preview and a bake drift apart, and it means the bake never benefits from precisely the caching a tree needs most.

**Parallelism: the axis is the canvas block, not the job.** Today two worker pools parallelise whole layer-and-frame jobs, but only in the editor preview, only behind a safety gate, and each thread needs its own clone of the spec. There is exactly one Burst kernel in the whole tool. B9 already requires that a generator be handed a block of the canvas and fill in rows — which makes **per-block** parallelism and Burst the natural fit, identical in preview and bake, with no clone and no gate. Parallelism across frames stays available on top of it for animation bakes.

**The honest bottom line, and the one action that fixes it.** No Pyre runtime timing has ever been measured, in four rounds. Every figure described as "measured" in the earlier reports was counted by reading source, and the only wall-clock millisecond table anywhere in the investigation belongs to Kiln/Tapestry — a different project, in numpy. (One 3D Shaper performance script was found and not opened, so a stray number may exist on that side; nothing on the Pyre side.) So:

> **Wave 0's render-one-frame-from-every-generator sheet should be instrumented to report per-layer milliseconds.** That is a small addition to a task that is already scheduled, it produces the first real datum this project has ever had, and R7's model should be re-checked against it before wave 2 commits to a budget number.

---

# What this changes about the code that exists

Not a plan — a list of what is genuinely new, so that wave 2 is not surprised by it.

| Item | Status today |
|---|---|
| Shape combination at all | **Does not exist in Pyre.** Its three-mode matte channel combines into a mask, not a silhouette. New. |
| Intersect | Exists in neither tool. New. |
| Soft combine between two shapes | Exists in 3D Shaper only. Pyre's one smooth-max never acts between shapes. New in Unity either way. |
| Composable local transform | Exists in 3D Shaper. Pyre has no matrix at all and its transform function is not composable. New in Unity. |
| Edge-distance bound and its composition | Exists nowhere. New, and B7 flags it as the one thing that is expensive to retrofit. |
| Support extent on the primitive contract | Exists nowhere, and is **not** the same thing as B7's bound. New, and R7's cost model does not work without it. |
| Distances published in canvas units | New, and required the moment transforms nest. |
| Clock composition down a tree | Half exists — Pyre's layer window is the first link of the chain. The repeat mode that separates phase from existence is new. |
| Swarm resolving to a field before publishing | Structurally similar to today's Fuse, but Fuse is a metaball threshold with a different look. The union path is new. |
| Nested swarm | Does not exist. Not recursive anywhere. New. |
| Per-node coverage planes replacing the shared matte channel | New, and it is what makes R5 possible. |
| Named-quantity combination across members | New. |
| Bounded-region cost accounting | Partly exists (rasters are bbox-bounded) but nothing composes boxes up a tree. New. |

---

# Flagged: departures, extensions, and one unresolved conflict

Everything in this section is something the owner may want to overrule, and each is one sentence away from being reversed.

**Three departures from the design's own wording.**

1. **B2 lists four combine modes; R6 delivers three plus a dial.** "Blend softly" becomes a property every mode has rather than a fourth mode, because 3D Shaper's fourth mode is where its knob overload lives. Reversible by keeping four modes, at the cost of inheriting the overload or reinventing it.
2. **C2 says masking is "unchanged in principle"; R5 removes the role enum.** The change is deliberate and argued, and the cheaper fallback is stated inside R5.
3. **The task's premise, and B9's phrasing, say every node is a pass over the canvas; R7 says a node is a *bounded* pass.** This is a correction rather than a disagreement — the bounded reading is both cheaper and closer to what the current code already does.

**Four decisions past the seven questions, included because the seven cannot be implemented without them.**

4. **How named quantities combine across overlapping members** (in R3). Coverage-weighted average for continuous quantities, strongest-wins for identifiers, and a subtracted member deposits nothing.
5. **That combination happens on signed distance and coverage is derived at the end** (in R6), with a declared coverage-only domain for generators that publish a fog rather than an edge. Not optional if soft combining is to look like a fillet rather than a cross-fade — and the coverage-only carve-out is not optional either, or C3's fog model and the whole simulation family are stranded.
6. **A second number on the primitive contract — the support extent** (in R2 and R7). B7 requires a bound; a bound is a ratio and cannot produce a bounding region. This is an addition to B7's primitive contract, not a reading of it, and it is the one item here that would be as expensive to retrofit as B7's own bound, for the same reason: it has to be declared by every primitive, so adding it after primitives exist means revisiting all of them.
7. **That published distances are rescaled into canvas units on the way up** (in R2). Without it, blend width in pixels is meaningless under any non-unit transform.

**Two items where no owner ruling exists and I have decided rather than stalled.** Both were catalogued by the earlier rounds as genuinely undecided: whether the writer-or-consumer exclusivity gets lifted (R5) and whether the soft control is one knob or two (R6). Decided here, with reasons, and both are cheap to overturn now and expensive to overturn after wave 2.

**One unresolved factual conflict, which should be settled before wave 2 rather than by wave 2.** The earlier rounds report that several of the large multi-instance forms — Orb, Torch, Jet, RadialJet, ExplosiveJet — start every instance on the shared document clock, so instances pop into existence mid-animation, and C7 schedules that as a fix belonging to the rebuild. This round's fresh reading found the opposite for the *generic* swarm wrapper: it has a genuine per-instance clock and its default envelope deliberately staggers births across the first half of the timeline. The two are probably both true of different code paths — the generic wrapper versus each form's own native instancing, and it is already established that some forms bypass the wrapper entirely — but that reconciliation is inference, not verification. It does not change any rule in this document; it changes how much porting work sits behind R4.

**Six assumptions the earlier rounds carried that this round's reading contradicts.** Recorded so they do not get re-inherited: Pyre has no per-member *silhouette* combine (it does have a per-member matte-channel mode, which is a different thing); Pyre's swarms *do* have per-instance clocks; 3D Shaper's fuse uses the power form of smooth-min, not the polynomial one that Pyre's smooth-max mirrors; nodes are not full canvas passes; Pyre's threading exists but is preview-only and gated, so "single-threaded or parallel" was a false dichotomy; and no Pyre millisecond figure exists anywhere. Full evidence, cited to file and line, is at `D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0103\CODE-FACTS.md`.

---

## How this document was checked

The first draft was reviewed adversarially against both codebases and against the design, and it did not survive intact — eight blocking findings, all corrected above. Three are worth naming, because they are the errors this document would have propagated into wave 2 and because they show what kind of mistake this material invites:

- **R2's bound composition was inverted.** It said a member publishes its child's bound times the *largest* singular value; the correct treatment is to rescale the published distance by the *smallest*, after which the bound composes unchanged. Getting this backwards would have produced unsound distance fields — and B7 names the bound as the single most expensive thing in the whole design to retrofit, so it is exactly the wrong place to be wrong.
- **R7's bounding-box rule ignored member order**, in a document whose first rule is that member order is meaning. The order-blind form drops geometry that lies outside an Intersect member, and it does it silently — clipped output looks like a broken generator, not a broken bound.
- **R6 claimed Pyre's existing smooth-max was the new control's `n = 1` case.** It is not; the two differ in both curve shape and depth. The document's own errata section already said so four pages later, which means the body and the errata had been written and never reconciled against each other.

The remaining five corrections were a misread of 3D Shaper's empty-accumulator seed, a misattribution of its blend scaling to the canvas rather than the member, a missing unit on published distances, a time-invariance rule that did not follow mask edges, and the conflation of B7's bound with a bounding region. Two mathematical claims were checked and *held*: that the soft-combine formula preserves the bound for every profile setting, and that a Subtract member never enlarges a bag's box even with a soft seam.

Recording this is not ceremony. Four consecutive rounds of this investigation had claims withdrawn, and the two most consequential errors here were both in the arithmetic under a decision rather than in the decision itself — the rulings were sound while the maths beneath them was not. Anything built on this document should assume the same is still possible and check the algebra rather than the prose.
