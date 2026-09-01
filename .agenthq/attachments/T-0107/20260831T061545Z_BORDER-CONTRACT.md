# Shaper — the border contract

**T-0107, 2026-08-31. Wave 2, third stage.**

This document specifies the border stage: how a strip is derived from a shape node, what publishes it, what paints it, and what happens to the three generators that draw a private rim of their own today.

It stands on four documents and does not restate them: `SHAPER_THE_DESIGN.md` (B3 and C5 are the ruling text), `SHAPE-ENGINE-SPEC.md` and the shape-tree rules from T-0105, and `FILL-CONTRACT.md` from T-0106 — whose clause **FC-8.4** was written specifically for this task and is honoured here in full. Where a clause below contradicts one of those, the contradiction is named and argued, never left silent.

Clauses are numbered `BD-n.m` (border derivation). A clause that overrides or amends an earlier document says so in bold.

---

# Part 0 — three things that changed while checking

Stated first, because each one reverses something a reader of the earlier documents would otherwise assume.

**1. A border is derived from the node's signed DISTANCE, not from its coverage.** Both B3 and C5 say a border "traces that node's coverage" (`SHAPER_THE_DESIGN.md:76`, `:333`). In effect that is what happens; in mechanism it is not, and the difference is the whole reason the new stage is better than the old one. Coverage is zero everywhere outside the silhouette, so an outward strip is not recoverable from it — which is precisely why today's Pyre border is inward-only and why it has to run a two-pass chamfer distance transform over the whole canvas to invent a distance it already threw away. The shape engine publishes an exact signed distance field, negative inside (`ShaperField.cs:9`). Deriving the strip from that is exact, costs no distance transform, works outward for free, and is measured from the true boundary rather than from the alpha-quantisation fringe. See BD-1.2.

**2. The fire simulation has no private rim; Inferno does.** The task brief names "the lit solids, Text, Fire" as the three generators with a private rim. `FireSim.Render` (`Runtime/SpriteFx/FireSim.cs:295-308`), its swarm twin `PyreFireSim.cs:258-271`, and `FireballSim` (`Runtime/SpriteFx/FireballSim.cs`) were each read end to end and **none of them draws a rim of any kind**. The fire-family generator that does is **Inferno** (`Runtime/Pyre/Forms/Kiln/PyreInferno.cs:700-712`), and what it draws is not a border at all. Ruling in BD-5.3.

**3. Joining an outward strip to the published coverage is a DILATION, not a union.** The obvious implementation — fold the strip's field into the node's with a `min`, the way any other member folds in — is wrong, and wrong in a way that produces a visible artefact rather than an error. Worked out in BD-2.2.

---

# Part B1 — what a border is

## BD-1.1 The attachment rule

**A border attaches to a shape node — any shape node — and derives a strip from that node's own field.** This is B3's rule unchanged, and it answers every placement question by construction:

- a border on each member outlines each blob separately;
- a border on the bag outlines the fused silhouette;
- borders on both give an outer outline plus interior division lines, and they do not fight because they are different nodes tracing different fields.

A node owns at most one border. Two strips on one node is not a missing feature: it is a bag with one child, and the bag's border and the child's border are the two strips. That keeps the "one nesting mechanism used at every level" rule (B2) intact rather than adding a per-node list that duplicates it.

A border is optional on every node **including the root**. This is deliberately unlike FC-3.2, which makes the root's *fill* compulsory. FC-3.2's argument is that a compulsory root fill makes the nearest-ancestor search total, so there is no "no fill found" branch anywhere. Borders have no inheritance and therefore no search to make total: a node without a border simply has no strip. Nothing is gained by forcing one and a permanent unwanted outline would be lost.

## BD-1.2 The strip is derived from the signed distance field

**The border stage reads the node's signed distance, `d`, and nothing else.** It never reads the node's coverage, never reads the painted alpha, and never runs a distance transform.

The argument, since this is the clause that departs from the ruling text's wording:

- **Outward is impossible from coverage.** Coverage is `1 − smoothstep(−h, +h, d)` (`ShaperField.cs:54`) and is identically zero more than `h` outside the boundary. Every point of an outward strip beyond half a pixel is in that dead zone. An implementation reading coverage must therefore reconstruct an exterior distance, and the only way to reconstruct one is a distance transform over the whole canvas — which is exactly what Pyre does (`PyreRenderer.cs:958-985`) and exactly why Pyre's border cannot go outward (`PyreRenderer.cs:1021`, `if (fa == 0) continue;`).
- **Coverage measures from the wrong place.** Pyre seeds its transform at pixels whose alpha is *exactly* zero (`PyreRenderer.cs:962`). On any shape with a soft edge every pixel of the fade has alpha above zero, so the distance origin is the outer end of the fade rather than the visual boundary, and the whole band lands inside the fade where the rim is then multiplied by a small coverage and washes out. That defect is not fixable inside a coverage-reading design; it disappears entirely in a distance-reading one.
- **Distance is already there.** `ShaperEvaluator.FillTile` writes the signed distance and the coverage into two arrays in the same pass (`ShaperEvaluator.cs:133`), and the resolver already keeps a per-owner `ownDistance` sheet because a `ByEdgeDistance` gradient needs it (`ShaperFillResolver.cs:116`). The border stage reads a buffer that is already being produced.

**Amendment recorded:** `SHAPER_THE_DESIGN.md:76` and `:333` should read "traces that node's edge" rather than "traces that node's coverage". The behaviour they describe is unchanged; the mechanism they imply is not the one that works. Flagged, not edited — that document is signed off.

## BD-1.3 The strip field is the existing Shell operator, exactly

Given the node's distance `d` and the three alignments, the strip's own signed distance is:

| Alignment | Strip field `s(d)` | The band, in `d` |
|---|---|---|
| Inward | `max(d, −d − w)` | `d ∈ [−w, 0]` |
| Outward | `max(−d, d − w)` | `d ∈ [0, +w]` |
| Straddling | `abs(d) − w/2` | `d ∈ [−w/2, +w/2]` |

Those three expressions are `ShaperOps.Shell(alignment, w, d)` verbatim (`ShaperOps.cs:167-176`), with `ShaperShellAlignment.Centred` reading as Straddling. **The border stage adds no new geometry operator.** It reuses Shell, reuses `ShaperBound.Shell` (`= childBound`, `ShaperBound.cs`), and inherits both the Lipschitz argument and the audit coverage that already exist for it.

This reuse is a decision, not a coincidence, and it is worth stating why it is safe: Shell as a *shape node modifier* replaces the node's field with the band. The border stage evaluates the same function but **keeps both** — the node keeps publishing `d`, and the strip is a second field derived alongside it. That is the entire structural difference between a shell and a border, and it is one buffer, not one operator.

The strip inherits the node's declared bound unchanged, because `abs` and a hard `max` both preserve gradient magnitude.

## BD-1.4 Width is a total thickness, in canvas pixels, animatable

`width` is the **total** thickness of the strip in every alignment: an Inward border of width 4 occupies four pixels inside the edge, an Outward border of width 4 occupies four outside it, and a Straddling border of width 4 occupies two on each side. The table in BD-1.3 already encodes this. Stating it matters because Straddling is the one where the natural implementation (`abs(d) − w`) silently means double.

Units are canvas pixels at every nesting depth, which is free: the compiler's `σ_min` rescale already guarantees that a distance means pixels at any depth or scale (`ShaperField.cs:10-12`), so a 2px outline on a member scaled to a quarter size is still 2px on screen.

Width is a `ZUIValue`, so it animates over the layer's life, and it is resolved **at compile time** through `ShaperValue.Sample` like every other dial (FC-5.3). Animating the width therefore costs a recompile of one node's strip program per frame and nothing per sample.

## BD-1.5 A zero or negative width is an exact no-op

`width <= 0` produces **no strip and no owner at all** — not an empty strip, not a strip painted with zero alpha. It is tested before the strip program is compiled.

This is the same house rule the shape engine already applies at three places (`ShaperOps.SmoothMinShaped`'s `k <= 0` early-out, `Combine`'s `carveStrength <= 0` early-out, the identity checks in `ShaperFieldAudit.V1`/`V2`), and it exists for the same reason: an author sweeping a width dial to zero must get back exactly the picture they had before the border existed, bit for bit, and "exactly" cannot be left to the arithmetic.

## BD-1.6 Antialiasing: the strip uses the same kernel and the same band as the shape

The strip's coverage is `ShaperField.Coverage(s(d), halfBand)` with **the same `halfBand` the node itself uses**. Both edges of the strip are therefore antialiased identically to the shape's own edge, and neither is ever hard-cut.

Two consequences worth writing down, because both are exact rather than approximate:

- **At an inward strip's outer edge the strip's coverage equals the shape's coverage exactly.** Near the boundary `s(d) = max(d, −d − w) = d` for all `d > −w/2`, so the two fields are the same field there. The rim can never be brighter or dimmer than the silhouette it traces at the silhouette's own edge, and there is no seam to tune.
- **At an outward strip's inner edge the strip's coverage is exactly the complement of the shape's.** Near the boundary `s(d) = −d`, and `smoothstep` is odd about its midpoint, so `Coverage(−d, h) = 1 − Coverage(d, h)` identically. The pair sums to exactly 1 at every sample.

Both replace Pyre's arrangement, which is a hard-coded one-pixel linear feather on the inner edge (`PyreRenderer.cs:1025`, the `+ 1f`) multiplied by the shape's own coverage (`:1035`). The fixed feather is not authorable, does not scale with the shape's own softness, and the multiply is what makes the rim wash out on a soft edge. **Neither is carried across.** In particular the strip's coverage is *not* multiplied by the node's coverage; it does not need to be, because BD-1.3 derives it from the same field, and multiplying would double-count the antialiasing at the shared edge.

---

# Part B2 — publication: what the rest of the tree sees

## BD-2.1 Outward reach

A border's **outward reach** is how far past the node's boundary its strip goes: `w` for Outward, `w/2` for Straddling, `0` for Inward. Only the reach matters to publication; the rest of the strip is inside the node and changes nothing.

## BD-2.2 Joining is a DILATION of the node's field, not a union with the strip's

C5 rules that "an outward strip joins the node's published coverage by default" (`SHAPER_THE_DESIGN.md:333`), so that a bag containing the node sees the outline as part of the member and a mask made from it includes the outline.

**The rule: when a border joins, the node's published distance becomes `d − reach`. Nothing is combined; a constant is subtracted.**

The obvious alternative is wrong. Folding the strip in the way any other member folds in gives `min(d, s(d))`, and for an Outward strip that is `min(d, max(−d, d − w))`, which at `d = +0.1` evaluates to `−0.1`. The union of a shape with a band hugging its outside is the shape dilated by `w`, whose true distance at that point is `0.1 − w`. The `min` form is not merely a loose approximation of it: **it has a spurious zero crossing exactly on the original silhouette**, because the strip's own field is zero at its inner edge and the node's field is zero at the same place. The coverage kernel turns that into a ring of coverage ≈ 0.5 following the original outline — a visible seam, one pixel wide, in the middle of what should be a solid dilated silhouette. It looks like an antialiasing bug and it is a topology bug.

`d − reach` is not an approximation either. For a strip derived from the node's own field, the union of node and strip *is* the set `{d ≤ reach}`, whose exact signed distance is `d − reach`. The join is therefore exact, is one subtraction, has no zero crossing anywhere but at the dilated boundary, and preserves the declared bound exactly because adding a constant does not change a gradient.

Joining is the **default**, per C5.

## BD-2.3 The opt-out, and the one consequence it has

`joinsCoverage = false` means the strip is drawn but not counted: the node publishes `d` unchanged, so parents, masks and any later consumer see the silhouette without the outline. The strip is still painted.

The honest consequence, stated rather than discovered: **painted alpha and published coverage then disagree.** A mask built from this layer will not contain the outline that is visibly on screen. That is what the switch is for and it is the only thing it does, but it is exactly the sort of divergence that reads as a bug six months later, so any UI over this must say "drawn, not counted" in those words.

A second consequence follows from BD-3.4 rather than from this clause: on a non-root node, opting out also means the part of the strip lying outside the parent's silhouette is clipped away, because the parent's coverage clips the strip and the parent never learned about it. On the root — where the use case actually lives — there is no ancestor and nothing is clipped.

## BD-2.4 The join dilates the culling box, and NOT the anchor box

A joined outward strip makes the node bigger, so the **support box** — the conservative box used to cull tiles and to bound the node — must grow by the reach on all four sides. It is a bound; a bound that excludes real samples is a correctness failure, not a performance one.

The **anchor box** — the node-local box a fill normalises its coordinates against (FC-1.5, `ShaperProgram.localSupport*`) — **must not grow.** If it did, switching a border on would silently move every gradient on that node: a linear ramp authored across the shape would rescale by `(halfExtent + reach) / halfExtent` the moment an outline appeared, and rescale again on every frame in which the width animates. Enabling an outline must never repaint the thing it outlines.

The two boxes are already separate fields on `ShaperProgram` (`supportC*/supportHalf*` versus `localSupport*`), so this costs nothing but the discipline of touching one and not the other.

---

# Part B3 — painting the strip

## BD-3.1 A border owns an ordinary fill, and the fill stage needs no change

The strip is a region. It carries a `ShaperFillDef` — the same type, the same four kinds, the same compiler, the same blittable op, the same tile call. Gradient outlines, textured outlines, animated outlines and ramp-by-quantity outlines are all free, exactly as C5 promises, and **no new fill machinery is added by this task.**

A border with no fill authored is a border with no colour, which is nothing. Rather than inventing a second default, a border whose `fill` is null uses `ShaperFillDef.DefaultRootFill()` — the same guaranteed-binding Solid that FC-3.2 uses for the root — so that a border which is switched on always draws something and can never be silently inert.

## BD-3.2 FC-8.4 holds: positional coordinates normalise on the NODE's box

Verbatim from the fill contract, and the reason this stage was allowed to be deferred at all:

> **FC-8.4 — the anchor box (FC-1.5) is always the NODE's box, never the painted region's box.**

So a Linear, Radial or Angular gradient painting a border ramps **across the shape it traces**, not across the ring's own thickness. The failure it prevents is a linear ramp on an outline running across two pixels of outline thickness instead of across the shape — "almost never what anyone wants, and nearly impossible to diagnose after the fact".

## BD-3.3 …but the `edgeDistance` sheet handed to a border's fill is the STRIP's, not the node's

This is the trap that sits one inch from BD-3.2 and points the other way, so it is stated as its own clause.

FC-1.2 says a fill reads the sheets its owning node published. A border's owning region is the strip, and a `ByEdgeDistance` gradient exists precisely to ramp from an edge inward. On a border the useful, expected and only sane meaning is **across the strip's own thickness** — that is what makes a bevelled or double outline expressible at all. So:

- **positional** inputs (`x`, `y` → Linear/Radial/Angular gradients, texture coordinates) normalise on the node's anchor box — BD-3.2;
- the **`edgeDistance`** input is the strip's own field `s(d)` — this clause.

They are different inputs and there is no inconsistency: one asks *where am I on the shape*, the other asks *how deep am I into the region I am painting*. Getting either backwards produces a picture that looks deliberate and is wrong, which is why both are conformance-tested (BT-6, BT-7).

Polarity is unchanged from FC's: `t = clamp01(−s(d) / depthPixels)`, so `t = 0` at both faces of the strip and `t = 1` at its centre line. Note this differs in *shape* from a fill's ramp — a fill's region has one edge and its ramp is monotone; a strip has two and its ramp is a ridge. That is the honest geometry of a band, not a defect, and it is what makes a gradient-out-to-both-edges outline the natural default.

## BD-3.4 A border's claim is clipped by the same ancestor that clips its node's fill

The claim rule for fills is `claim = min(coverage_N, coverage_A)` where `A` is the nearest binding ancestor owner (`ShaperFillResolver.cs:601-616`). **A border uses the identical rule, with the identical ancestor** — the nearest binding ancestor of its *node*, never the node itself.

Not the node itself, because for an Outward strip that would clip the entire border away: the strip is by construction outside the node's coverage.

The ancestor clip is what stops a member's rim appearing in a region a later Subtract member carved out of the bag. Without it, a rim would survive in a hole, which is the fill stage's FC-3.3 problem in a new costume.

## BD-3.5 A border composites OVER its node's finished subtree — it is not part of the exclusivity partition

This is the one structural decision in the task, so here is the argument rather than just the rule.

The fill stage partitions a silhouette: every sample is painted by exactly one owner, and a descendant excludes its ancestor by `paint = max(0, claim − descendantClaim)` (`ShaperFillResolver.cs:634-660`). Two models were available for a border:

**(a) Make the border an owner in that partition.** It would then *replace* the node's fill inside the band, which sounds like the intuitive reading of "recolour the band". But it does not work. To be on top of a bag's members the bag's border would have to be a descendant of every member's fill owner, and a member's owner is a descendant of the bag's — the two requirements are contradictory in a tree. Any tree placement gives the wrong answer for one of the two documented cases in B3, and the case it breaks is the interesting one: an outer outline on a bag would be eaten wherever a member happened to own its own fill.

**(b) Composite the strip Over the node's finished subtree, before that subtree folds into its parent.** Ordering is then correct by construction — a node's outline is above everything inside that node and below anything above the node — the exclusivity machinery is untouched, and the per-owner subtree accumulators T-0106 already built (`ShaperFillBuffers.subtree`) are exactly the buffer this needs. **(b) is the ruling.**

The price of (b) is honest and must be said in the UI: a border with a partially transparent fill lets the fill underneath show through, because it is a stroke on top rather than a substitution. That is what every drawing tool does with a stroke, and it is what Pyre's border does today — but Pyre *documents* the opposite ("the outermost N px of the shape's silhouette are recoloured to the Border fill", `Pyre.cs:518`) while implementing `Over` (`PyreRenderer.Layers.cs:173`). The implementation is the sane one; **the documentation is the half that was wrong**, and it is not carried across.

This also settles FC-2.6a cleanly: Over-versus-Add governs how a finished thing meets what is beneath it, never how one silhouette's exclusive owners meet each other. A border is a finished thing meeting what is beneath it. A border's fill may therefore declare `Add` as well as `Over`, and it means what it means everywhere else.

## BD-3.6 Order among several borders

Borders fold in fold order, like everything else: a member's border folds into the member's subtree, which folds into the bag, and the bag's own border folds in last. **So the bag's outline is above its members' outlines** where they overlap.

No stage may reorder this, for the same reason R1 gives for members. It is also the answer an author predicts: the outline of the thing you drew around everything is on top.

## BD-3.7 A Subtract member may not own a border

Mirrors FC-3.3 exactly, for the same reason and through the same refusal machinery: a Subtract member deposits nothing, and a strip is a deposit. Worse than a fill, in fact — a Subtract member's own edge is not an edge of the finished silhouette at all, so its rim would trace a boundary that is not visible anywhere, wherever a later member happened to put the shape back.

**The way to outline a hole is a border on the bag, and it already works with no extra feature.** The bag's finished field is zero on the hole's boundary just as it is on the outer boundary, so an Inward or Straddling border on the bag traces both at once. That is not a workaround, it is the correct reading of BD-1.1: the hole is part of the bag's edge.

An Intersect member **may** own a border, mirroring FC-3.3, with the caveat that it traces the member's own edge rather than the intersection's — the same caveat the fill stage already carries.

## BD-3.8 The availability gate applies to a border's fill, unchanged

A border's fill goes through FC-4.4 exactly as any other fill: it declares what it requires, the node's published set is unchanged by the presence of a border, and a fill requiring a quantity the node does not publish refuses to bind and records a diagnostic naming the border.

The strip itself never refuses on availability. It needs only the node's distance, which every shape node publishes unconditionally (`ShaperQuantitySet.ShippedShapeEngine = Coverage | EdgeDistance`).

A border whose fill refuses does **not** fall back to the node's fill and does not fall back to nothing. It falls back to the FC-3.2 default Solid, per BD-3.1 — a border that was switched on is always visible, so that a refusal reads as "the outline is the wrong colour, why?" rather than as "the outline vanished, is this a shape bug?". That is the same reasoning FC-4.4 gives for never painting nothing.

---

# Part B4 — the three private rims

The brief requires each to be ported as an ordinary border or explicitly kept and documented, and forbids silently having both. All three are ruled on. Two are kept and one is ported, and in both kept cases the resolution is that **the thing is not a border**, which is the only honest way to keep it without duplication.

## BD-4.1 The lit solids' facet edge lines — KEPT, and renamed so it stops claiming to be a border

**What it is.** `gemLineWidth` / `gemLineFill` (`Pyre.cs:370-371`), drawn at `PyreRenderer.cs:4346-4351` and mirrored for Orb (`:4540`) and Ring (`:4726`). For each visible triangle it measures the distance to the nearest projected 3D edge segment and, within `lineWidth` of one, substitutes a light-scaled line colour: `k = clamp(0.25 + lit, 0, 1.15)`.

**Ruling: keep it. It is not a border and never was.** Three independent reasons, any one of which is sufficient:

1. **It traces a different set.** It draws *every visible facet boundary*, including interior seams where two faces of a gem meet — edges that are nowhere near the silhouette. Those seams are not in the zero set of the 2D field, so no border stage of any design can produce them. It is a 3D feature drawn in 2D, not an outline.
2. **It is lit, and a fill may not be.** The line colour is multiplied by the same per-pixel lighting term as the face it sits on, deliberately overbright at 1.15. A fill emits unlit albedo by construction (FC-2.2 and the whole argument of B4/C6: shine belongs to the lights). Porting it to a border's fill would either lose the light response or smuggle lighting into the fill stage, and the second is the one thing T-0106 was most careful to prevent.
3. **It cannot be excised anyway.** Its distance field `edist` is also consumed by the halo (`PyreRenderer.cs:4366`) and its `isLine` flag gates the inner glow (`:4373-4378`). The painting is separable; the measurement is not.

**What "documented" costs, concretely:** the feature must stop being called a border in the vocabulary, the spec and any future UI. Call it **facet edge lines**. Once it is named for what it does, the public border stage on the same layer is not a duplicate — one traces the silhouette, the other traces the seams — and an author can sensibly have both on at once. Silently having both was only ever a naming failure.

## BD-4.2 The Text generator's outline — PORTED, and it becomes strictly better

**What it is.** `textBorderWidth` / `textBorder` (`Pyre.cs:418-421`), at `PyreRenderer.cs:4020-4025`: a band `0.5 ≤ sd < 0.5 + borderBand` of the glyph SDF, painted with its own `ZuiFill` through the same sampler as the face.

**Ruling: port it as an ordinary border and delete the private one.** It is an inward strip of authored width with its own fill and no lighting — the public stage's exact definition. It is also the most separable of the three: one boolean and one ternary select which `ZuiFill` is handed to the sampler, and deleting them leaves a coherent generator. Nothing is lost and four things are gained: the width becomes animatable (today it is a plain `float`, not a `ZUIValue`), Outward and Straddling become available, the strip becomes antialiased, and the outline stops being a special case only Text has.

**One precondition, named so it is scheduled rather than discovered.** The port is only *complete* once the Text generator publishes a signed distance instead of a hard threshold. Today it does `if (sd < 0.5f) continue;` and sets `alpha = alphaEnv` (`PyreRenderer.cs:4021`, `:4041`) — the SDF is thresholded and thrown away, so glyphs and their rims are fully aliased. A TMP SDF *is* a distance, so this is a conversion rather than a new feature, but it belongs to whichever task ports the Text generator into Shaper. **T-0107 builds no Text generator; there is none in Shaper yet.** This ruling is a standing instruction to that task.

## BD-4.3 Inferno's rim dials — KEPT, and reclassified: they are a heat term, not a rim

**Premise corrected first.** As Part 0 records, `Fire` and `Fireball` have no rim. The generator meant here is Inferno.

**What it is.** `hollowRim` and `outerRim` (`InfernoForm.cs:83-85`), at `PyreInferno.cs:700-712`. Each adds a Gaussian bump to `h` — the **heat scalar** — centred at a normalised radius (`rr = 0.90` for the outer one), with a width that is a fraction of the blast radius rather than a pixel count.

**Ruling: keep it, and stop calling it a rim.** It has no colour of its own and never paints: it raises heat, and colour arrives afterwards from the layer's fill sampled at `1 − heat` (`PyreInferno.cs:41-43`). Under the new model that is not a border by any part of its definition — it is a shape generator publishing more of the `Heat` quantity in an annular region, and the *look* it produces is a `RampByQuantity` fill reading `Heat`. Both halves already exist in the contracts: `ShaperQuantity.Heat` is declared, and `ShaperFillKind.RampByQuantity` reads it.

So there is no duplication to resolve — nothing about it is a strip, a region or a paint. There is a **naming** collision only, and the instruction to whichever task ports Inferno is: rename these dials to something heat-flavoured, because the word "rim" implies a border stage that has nothing to do with them.

**One thing that is genuinely lost by keeping it, said plainly:** because the rim is heat rather than paint, it cannot be given its own fill, cannot be deferred over a matte, and cannot be composited independently — there is no rim buffer and nothing downstream knows a rim happened. Anyone who wants a *paintable* outline on an Inferno wants the public border stage on the Inferno's layer, and that is available, separate, and does something different.

---

# Part B5 — conformance tests

Each is a mechanical, measurable assertion. `BT-13` is deliberately an instrument self-test, following the T-0105/T-0106 finding that a green audit containing a test which cannot fail is worse than no audit.

| # | Test | Assertion |
|---|---|---|
| BT-1 | Zero-width identity | `width = 0` produces no owner, and the painted output is **bitwise identical** to the same document with the border removed. Both directions of the sweep. |
| BT-2 | Shell agreement | For all three alignments over a dense sample of `d` and `w`, the strip field equals `ShaperOps.Shell` bit for bit — proving the border stage added no second copy of the operator. |
| BT-3 | Band placement | The strip's coverage crosses 0.5 exactly at the two `d` values BD-1.3's table predicts, for all three alignments, within a half-pixel band; total thickness matches `width` in all three (the Straddling double-width trap). |
| BT-4 | Inward edge identity | At the shared boundary, strip coverage equals shape coverage to within float rounding; measured as a max absolute difference over the boundary ring, expected 0. |
| BT-5 | Outward complement | At the shared boundary, `strip coverage + shape coverage = 1` to within float rounding, over the same ring. |
| BT-6 | Anchor is the node's (FC-8.4) | A Linear gradient on a border produces the **same colour at the same canvas point** whether the border is 1px or 20px wide. Fails loudly if the anchor were taken from the strip. |
| BT-7 | `edgeDistance` is the strip's | A `ByEdgeDistance` ramp on a border reaches `t = 1` at the strip's centre line and `t = 0` at both faces — the ridge of BD-3.3, not a monotone ramp. |
| BT-8 | The join is a dilation | With `joinsCoverage`, the node's published coverage has **no sample below 0.999 anywhere strictly inside the dilated silhouette** — the direct measurement of the BD-2.2 seam. The naive `min` implementation is run alongside and must fail the same measurement, so the test proves it can fail. |
| BT-9 | Opt-out publishes nothing | With `joinsCoverage = false`, the node's published distance is **bitwise identical** to the borderless document at every sample, while the painted output is not. |
| BT-10 | Anchor box does not dilate | Enabling a joined outward border leaves a Linear gradient on the node's own fill bitwise unchanged (BD-2.4), while the culling box provably grew. |
| BT-11 | Ordering | On a bag with a bordered member, the bag's border wins in the overlap; on a bag whose member owns a fill, the bag's border is **not** eaten by it — the case model (a) of BD-3.5 fails. |
| BT-12 | Subtract refusal | A border on a Subtract member creates no owner and records a diagnostic naming the node and the reason; a border on the bag traces the resulting hole. |
| BT-13 | Instrument self-test | Each measurement above is re-run against a deliberately broken implementation and must report FAIL. A test that cannot fail is reported as a failure of the audit. |
| BT-14 | No allocation | The border paint path allocates zero bytes and causes zero gen-0 collections over a large sample count, matching the fill stage's guarantee. |

---

# Part B6 — what is deliberately NOT in this task

- **No UI, no menu item, no drawer.** Same as `SHAPE-ENGINE-SPEC.md:16` and FC-8.6. The diagnostics are fields on the resolved document; a greyed control is whoever builds the UI reading them. The project rule against unrequested menu items applies with full force.
- **No lighting.** A border's fill emits unlit albedo and optionally a height delta, like any other fill. A bevelled outline is a border whose fill emits height, lit by T-0108's rig — which is why BD-4.1's light-scaled edge lines cannot be a fill today.
- **No Text generator and no Inferno port.** BD-4.2 and BD-4.3 are standing instructions to the tasks that do those, not work done here. There is no Text generator in Shaper.
- **No second border per node.** BD-1.1: that is a bag with one child.
- **No per-side or per-segment borders** (a thick top edge, a dashed strip). Both are expressible later as a border whose *fill* varies with position, which BD-3.2 already supports, so neither needs a contract change.
- **No port of Pyre's `borderOverMatte` deferral.** Pyre defers a rim to the very top of the finished frame (`PyreRenderer.Layers.cs:342-344`), above every later layer, and with no clip applied (`:313-315`). BD-3.5 gives a well-defined position instead. The deferral is a compositing feature about mattes, and it belongs to whichever task builds mattes — not smuggled in as a property of borders.

---

# Part B7 — what this changes about the documents it stands on

1. **`SHAPER_THE_DESIGN.md:76` and `:333`** — "traces that node's coverage" should read "traces that node's edge". Behaviour unchanged, mechanism corrected. BD-1.2. *Flagged, not edited.*
2. **The brief's "the lit solids, Text, Fire"** — Fire has no rim; the generator meant is Inferno, and what it has is not a rim. BD-4.3.
3. **The brief's "gated to six of 29 generators"** — the six is exactly right (`Disc, Crescent, Ring, Streak, Star, Polygon`, `PyreRenderer.cs:950-952`). The twenty-nine could not be reproduced from any list in the code: the verifiable totals are 6 of 17 selectable built-in forms, or 6 of 26 counting the nine plug-in forms. Twenty-nine is only reachable by counting two obsolete enum slots and an abstract base class. The *shape* of the claim — that the great majority of generators have no border and that the UI does not even construct the box for them — is confirmed.
4. **`Pyre.cs:518`'s "recoloured to the Border fill"** — describes substitution, implements `Over`. The implementation is the correct half. BD-3.5.
5. **FC-8.4 is honoured and extended**, not amended: BD-3.2 keeps it verbatim for positional inputs, and BD-3.3 adds the clause it did not need to state — that `edgeDistance` goes the other way.

---

# Part B8 — Pyre defects this stage does not inherit

Recorded because each one is a thing a literal port would have carried across, and because the list is the concrete answer to "much of this exists in today's Pyre already".

| Pyre defect | Why it cannot occur here |
|---|---|
| Rim measured from the alpha-quantisation fringe, so it washes out on any soft edge (`PyreRenderer.cs:962`, `:1035`) | BD-1.2: measured from the exact signed distance. |
| A silhouette running off-canvas loses its rim on the clipped side (the chamfer never seeds at the array boundary) | BD-1.2: no distance transform exists, so there is no seeding and no boundary. |
| Inward only; no outward, no straddling | BD-1.3. |
| Fixed, unauthorable 1px inner feather (`:1025`) | BD-1.6: the same kernel and band as the shape, both edges. |
| Documented as substitution, implemented as `Over` (`Pyre.cs:518`) | BD-3.5: `Over` is the ruling and the document says so. |
| Deferred rims escape their own clip and land above every later layer | Not carried; Part B6. |
| Six of twenty-something generators, by a hard-coded `switch` on a form enum, duplicated in the editor (`PyreRenderer.cs:950`, `PyreWindow.cs:2670`) | BD-1.1: attachment is to a *node*, so there is no per-generator list to gate, and nothing to duplicate. |
| Several full-canvas passes plus two extra full-canvas buffers, per bordered layer per frame | BD-1.3: one extra field evaluation over the tiles the strip touches, and one existing per-owner accumulator. |

---

# Part B9 — what I could not check

- **The "twenty-nine".** Not reproducible from code; see B7.3.
- **Speed.** Nothing here is timed, and nothing in T-0105 or T-0106 is timed either, so there is no baseline to compare against. The cost argument in B8's last row is a count of passes, not a measurement.
- **Determinism across a domain reload.** Same gap the fill stage carries.
- **The lit-solids ruling rests on reading, not on running.** BD-4.1's claim that the facet lines cannot be reproduced by any border stage follows from what the code measures (`DistSeg` to projected 3D edge segments, `PyreRenderer.cs:4329-4331`) rather than from an experiment that tried and failed.
- **Nothing here has been seen animated.** Width is a `ZUIValue` and resolves per frame by construction, but no moving picture has been rendered.
