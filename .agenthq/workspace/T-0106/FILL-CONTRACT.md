# Shaper — the fill contract

**T-0106, todo T1, 2026-08-31. Wave 2. Written before any fill code exists, which is the only moment at which it is cheap.**

This document is normative. Where it says MUST, an implementation that violates it is wrong even if it works. Where it says MAY, the choice is genuinely free. Every factual claim about existing code carries a `file:line`; where I did not open the file, I say so instead of asserting.

**Authority, in precedence order.** `T-0098\SHAPER_THE_DESIGN.md` (B4, B5, B6, C4, C5, C6) is the design and outranks everything below it. `T-0102\BUFFER_CONTRACT.md` (BC-*) binds the shape stage and states what a fill is handed. `T-0103\SHAPE-TREE-RULES.md` (R*) binds the tree. `T-0105\SHAPE-ENGINE-SPEC.md` is the spec the shipped shape stage was built from, and the shipped code in `D:\UNITY\Laubrary Dev - Shaper\Assets\Packages\Laubrary\Runtime\Shaper\` is the API this contract must actually plug into. Where a later document and the shipped code disagree, the shipped code wins and the disagreement is recorded in §F9 rather than smoothed over.

**Scope.** This contract binds the *fill stage*: what a fill is handed, what it emits, how it attaches to the shape tree, how availability is gated, how it is invoked, and the four fill kinds T-0106 ships. It does not bind lights (T-0108), extrusion or bevel (T-0109), the indexed strip (T-0110), borders (T-0107) or Tapestry (T-0111) — §F8 says what each of those takes away and what hook this contract leaves for it. It builds no UI, exactly as `SHAPE-ENGINE-SPEC.md:16` builds none for the shape stage.

**Rule IDs.** Every normative clause has an ID (`FC-1.3`, `FC-4.2`, …). Cite the ID in task text and in code comments. A rule nobody can name is a rule nobody can defend — the argument `BUFFER_CONTRACT.md:12` makes, applied to this stage.

**Decisions taken here.** Nineteen clauses below are marked **[D-T0106]**. Those are decisions this task took because no earlier document ruled, and each carries the reason it was taken so it can be overruled by a sentence rather than re-argued from scratch. Nothing is built yet, so every one of them is cheap to reverse today and expensive to reverse after T2–T5 land.

---

# Part 0 — the four things that changed while checking

Stated up front rather than buried, because two of them change what the fill stage can do on day one and one of them is a live contradiction between two signed-off documents.

**1. The shipped shape engine publishes exactly two of the nine quantities, not nine.** `ShaperEvaluator.FillTile` writes into precisely two host arrays, `distance` and `coverage` (`ShaperEvaluator.cs:148-149`), and the whole of `Runtime\Shaper\` contains no height, heat, density, soot, depth, age or surface-direction sheet — verified by grep over all eleven runtime files, whose only hits on those words are the word "height" used as a tile-row count (`ShaperEvaluator.cs:140`), the triangle's own dial (`ShaperPrimitives.cs:75`) and doc comments. So of BC-3.3's nine, the fill stage can read **`coverage` and `edgeDistance` and nothing else** in T-0106. That is not a defect in T-0105 — its brief was the shape *engine*, and BC-3.5 already records that `depth` is identically zero until extrusion exists (`BUFFER_CONTRACT.md:222`) — but it means the availability gate of §F4 is exercised hard from the first build rather than being a facility waiting for a future generator. Seven of the nine are unavailable everywhere, today.

**2. `coverage` clamped versus `coverage` unbounded is a live contradiction, and the fill stage is where it bites.** BC-3.3 #1 says coverage is "≥ 0, nominally 0..1, **unbounded above** — a fog, not a stencil… Consumers clamp at use, the publisher never clamps" (`BUFFER_CONTRACT.md:188`). `SHAPE-ENGINE-SPEC.md:39` says the opposite — "it is clamped to [0,1] at this stage" — and the shipped `ShaperField.Coverage` does clamp, returning 1 below the band and 0 above it (`ShaperField.cs:54-61`), with the reason in its own comment at `:51-52`. Both are defensible in their own terms: a coverage *derived from a signed distance* genuinely cannot exceed 1, and a coverage *accumulated by a fog generator* genuinely can. They are not reconcilable as stated. **Resolution for this contract (FC-1.6): the fill stage treats incoming coverage as unbounded above and clamps at use, per BC-3.3, even though today's only producer never exceeds 1.** Writing the fill stage to assume [0,1] would make it silently wrong the day the first fog generator lands, and the clamp costs one instruction.

**3. `CONTRACT-EXTRACT.md:15` is stale on the coverage kernel and should not be re-inherited.** It flags "none of the three specifies the actual smoothing kernel/width used to turn a single primitive's own final signed distance into its 0..1 coverage value" as an open gap for the implementer to propose or ask about. That gap was closed by `SHAPE-ENGINE-SPEC.md:30-40` and is shipped: `halfBand = max(edgeSoftness, 0.5·pixelSize)` (`ShaperField.cs:44-45`) and `coverage = 1 − smoothstep(−halfBand, +halfBand, distance)` (`ShaperField.cs:54-61`). The fill stage's veil arithmetic sits directly on top of that kernel, so it matters that it is settled.

**4. The design's own claim about `ZuiFill` was refuted a document ago, and this is the document that discusses `ZuiFill`, so the refutation must be carried rather than the claim.** `SHAPER_THE_DESIGN.md:120` says "every call site in Pyre passes zero for its spatial point". `BUFFER_CONTRACT.md:37` refutes that as stated — 23 `EvalFill` call sites in the built-in rasterisers are fed real per-pixel coordinates, and the 20 zero-passing sites are a deliberate hoist guarded by `IsSpatialFill`. What survives, and is the half this contract needs, is that `ZuiFill.Evaluate`'s entire input is `(life, u, v)` — verified directly at `ZuiFill.cs:167`. §F0 below states the consequence.

---

# Part F0 — the existing fill abstraction: what it is, what it cannot do, and what to take by value

The task text says the existing library-wide fill abstraction is **NOT to be extended in place**. That is `ZuiFill`, at `D:\UNITY\Laubrary Dev - Shaper\Assets\Packages\Laubrary\Zui\Scripts\Runtime\ZuiFill.cs`, 663 lines. I read it. Here is what it actually is.

## F0.1 What it does

`ZuiFill` is a `[Serializable]` paint-data class in the global namespace (`ZuiFill.cs:23-24`) with one pure entry point, `public Color Evaluate(float life, float u, float v)` (`ZuiFill.cs:167`). It carries four fill modes — `Solid, OverLife, Linear, Radial` (`ZuiFill.cs:29`) — and a separate texture group, `None, Sprite, Noise, Grid, Dots` (`ZuiFill.cs:34`), where a non-`None` texture **replaces** the mode entirely rather than being a kind of it, which is what keeps evaluation strictly non-recursive (`ZuiFill.cs:31-34`, dispatch at `:169`). It has a genuine animated-dial layer: the gradient is a `ZuiGradient` companion with non-destructive transforms (`ZuiFill.cs:77`, sampled at `:235`, `ZuiGradient.Evaluate(float t, float phase, float life)` at `ZuiGradient.cs:85`), and zoom and centre are `ZUIValue` companions (`ZuiFill.cs:105-107`). It is deterministic by construction — the noise is an internal FNV hash, never `UnityEngine.Random` (`ZuiFill.cs:10-11`).

It is used in **20 files** across the library (verified by `grep -rl ZuiFill Assets --include=*.cs`): Pyre runtime and editor, TextSplash runtime and editor, six ZUI files, and a test. That is the blast radius the task text is protecting, and it is real.

## F0.2 What it cannot do, precisely

- **Its entire input is `(life, u, v)`** — `ZuiFill.cs:167`. It takes no coverage, no height, no edge distance and none of BC-3.3's named quantities. Every one of the seven things B4's table says a fill decides with, beyond the position and the clock, is unreachable through this signature. This is the load-bearing gap and it cannot be closed without changing the signature, which is exactly what "used library-wide" forbids.
- **It emits one `Color` and nothing else.** No height delta, no veil separable from that colour's alpha, no composite declaration. B4's albedo-plus-height ruling (`SHAPER_THE_DESIGN.md:116`) is not expressible.
- **It is a managed object dereferenced per sample.** `BUFFER_CONTRACT.md:67` names "per-pixel dereference of a managed `ZuiFill` holding a `Gradient`" as a BC-1.3 offender by name, citing `ZuiFill.cs:24` and the call at `PyreRenderer.cs:4990-4991`. `Gradient.Evaluate` inside a per-sample loop is precisely what BC-1.1 forbids and what Burst cannot compile.
- **Its sprite path reads pixels through a managed cache per sample** (`ZuiFill.cs:260`, which caches the null so it "never re-throws per pixel" — an honest mitigation of a shape that should not exist in a block-fill contract at all).
- **Alpha is overloaded.** `color` is "alpha-capable like every mode" (`ZuiFill.cs:68`) *and* Grid and Dots carry their mask in alpha ("off-line pixels are transparent (alpha carries the mask)", `ZuiFill.cs:143`; "the gaps are transparent", `:151`). So one channel means both "this paint is translucent" and "this pattern does not cover here". §F2 refuses to inherit that.

## F0.3 What to reuse by value — and the difference between reusing a type and extending the abstraction

Reusing a **type** is fine and already precedent: the shipped shape stage stores `ZUIValue` dials directly on an authored primitive (`ShaperPrimitives.cs:96-100`) and samples them through its own `ShaperValue.Sample(v, phase01, seed)` (`ShaperValue.cs:26`) rather than through ZUI's own `Evaluate`, and the Shaper asmdef references `com.Lautaro-Arino.Laubrary.ZuiRuntime` and nothing else (`com.Lautaro-Arino.Laubrary.Shaper.asmdef`). Extending `ZuiFill` — adding a field, adding an overload, widening `Evaluate` — is what is forbidden.

**FC-0.1 — the fill stage MUST NOT reference `ZuiFill`, subclass it, wrap it, or add a member to it.** It MAY reference `ZUIValue`, `ZuiGradient` and `Color` as authored dial types, on the same terms the shape stage already does.

Five things are worth taking **by value** — the semantics, re-implemented in `Laubrary.Shaper`, with the provenance in a comment:

1. **`FillSpace { Stamped, Fixed }`** (`ZuiFill.cs:47`, field at `:111`). Its own documentation is an exact statement of design B5's swim problem: "Stamped (the default) = the shape's OWN local coords, so the pattern rotates / spins / travels WITH the shape (stamped onto it). Fixed = canvas-anchored coords, so the shape moves THROUGH a stationary pattern that stays put on the canvas" (`ZuiFill.cs:40-46`). B5 says the same thing from the other side and calls it "a control every texture fill needs and it is easy to forget until it looks wrong" (`SHAPER_THE_DESIGN.md:134`). Take the concept, the two names and the default. §F1.4 makes it concrete.
2. **`FillFit { Uniform, Stretch }`** (`ZuiFill.cs:64`, field at `:114`) — how a non-square anchor box normalises into ±1, with `Uniform` using one divisor so a radial fill stays a circle and `Stretch` using per-axis divisors so a ramp always runs end to end. The comment at `ZuiFill.cs:49-63` is the best statement of that trade anywhere in the project, including the worked failure ("on a text line 5.6× wider than it is tall, a vertical gradient shows the middle 18% of the ramp and nothing else"), and it names the exact defect that made the dial necessary: "TextSplash divided by the larger half-extent, Pyre's background divided per axis, and neither was a choice anyone could see or make" (`ZuiFill.cs:119-121`).
3. **Zoom-is-a-SIZE, not a frequency** (`ZuiFill.cs:83-88`). That field was migrated because "the maths used to multiply by it, so raising 'zoom' made the pattern SMALLER". The new contract starts on the fixed side: the authored dial is a size, bigger means bigger, and the reciprocal is taken once at compile time.
4. **Gradient evaluation semantics** — a 0..1 parameter into a `ZuiGradient`, clamped (`ZuiFill.cs:194`, `:198`, `:212`, `:217`), with the linear projection through an authored centre and the radial distance measured from that centre. §F6.2 restates the two expressions as formulae so the port is by value and not by call.
5. **"A half-configured fill never renders empty"** (`ZuiFill.cs:163-166`): a null gradient falls back to the flat colour, a null or unreadable sprite falls back to the flat colour. §F6.4 keeps that rule and adds the half `ZuiFill` cannot have — a recorded diagnostic, so the fallback is visible rather than merely graceful.

Two things are worth taking as **warnings** rather than as behaviour: the alpha overload of §F0.2, refused in §F2.2; and `Mode.OverLife`, a gradient sampled by the lifetime clock (`ZuiFill.cs:177`), which in the new model is not a fill mode at all — every dial is a `ZUIValue` sampled at compile time per frame (§F5.3), so "animate the colour over life" is a property of the gradient dial, not a fifth mode.

---

# Part F1 — what a fill is handed

## FC-1.1 The rule

> **A fill is handed exactly three things: a per-sample read of the sheets its owning node published, a blittable per-invocation parameter block of scalars resolved before the call, and the sampling point in the shape-local frame. It reads nothing else. It reads no managed object inside its loop, and it never reads a neighbouring sample.**

The three categories are B4's table (`SHAPER_THE_DESIGN.md:96-104`) split along BC-3.2's line (`BUFFER_CONTRACT.md:176-180`), which the design's own table does not draw — B4 puts "How old is this pixel, and which instance am I" in one row, and those are a sheet and a scalar respectively. Follow BC-3.2.

## FC-1.2 Category one — the per-sample sheet inputs

The nine of BC-3.3, and only those nine. A fill reads a sheet by index at its own sample; it never reads `[i−1]` or `[i+width]`, because BC-2.1 forbids screen-space neighbour reads before the resolve (`BUFFER_CONTRACT.md:100`) and because a neighbour read would seam under BC-1.6's tile-independence test on the build it was written.

| # | Quantity | Type | Units / range | Available in T-0106? |
|---|---|---|---|---|
| 1 | `coverage` | float | ≥ 0, nominally 0..1, **unbounded above**; consumer clamps at use (FC-1.6) | **Yes** — `ShaperEvaluator.cs:149` |
| 2 | `height` | float | layer-local units along the layer's up axis, 0 = base plane | No — no producer exists |
| 3 | `edgeDistance` | float | canvas pixels, **signed, negative inside** (`ShaperField.cs:9-10`) | **Yes** — `ShaperEvaluator.cs:148` |
| 4 | `heat` | float | 0..1 | No |
| 5 | `density` | float | 0..1 | No |
| 6 | `soot` | float | 0..1 | No |
| 7 | `depth` | float | layer-local units, entry-to-exit along the ray (BC-3.5) | No — identically zero until T-0109 |
| 8 | `age` | float | 0..1 normalised lifetime of the material at this sample | No |
| 9 | `surfaceDirection` | float×3 | unit vector, layer-local | No |

**FC-1.3 — the sign of `edgeDistance` is a stated trap.** It is negative inside. `BUFFER_CONTRACT.md:190` records that Pyre's existing `BorderInsideDistance` runs the *opposite* direction — unsigned and growing inward — and that the band expression it feeds "inverts if it is ported literally onto a negative-inside quantity". Any fill reading edge distance MUST state its polarity in a comment at the expression. §F6.2's `ByEdgeDistance` mode does.

## FC-1.3b Category two — the per-invocation scalars

Resolved into a blittable parameter block **before** the tile call, never read from a managed object inside it (BC-1.2, `BUFFER_CONTRACT.md:58`). These are not sheets and MUST NOT be given sheet-shaped storage:

- `instanceIndex` (int) — which swarm instance this invocation is painting. Per BC-3.2 (`BUFFER_CONTRACT.md:180`) and R4: a swarmed node's fill runs **once, on the node's own clock, over the single resolved plane** (`SHAPE-TREE-RULES.md:130`), so within one fill invocation `instanceIndex` is constant, and per-instance variation reaches a fill *only* through the `age` sheet. Present in the block so a future non-coalesced path has somewhere to put it; constant and equal to 0 in T-0106.
- `instanceLife` (float, 0..1) — the instance's own normalised life. Same status.
- `phase01` (float, 0..1) — the node's clock, per R3: "a fill runs on the clock of the node it is attached to" (`SHAPE-TREE-RULES.md:85`), which is the parent's clock remapped through the node's own window (`:91`). This is the same number `ShaperCompiler.Compile(root, phase01, seed)` already takes (`ShaperCompiler.cs:88`) and it MUST be the same value for the shape and the fill of one node in one frame.
- `seed` (uint) — for the `MinMax` dial mode, drawn by hash and never by `System.Random`, exactly as `ShaperValue.Sample` already does (`ShaperValue.cs:33`, hash at `:43-49`). BC-1.3 names `new System.Random(...)` at `PyreRenderer.cs:5034` as an offender.

**FC-1.4 [D-T0106] — there is no separate "document clock" input.** B4's table lists "What time is it" as its own row. R3 already routes every clock through the node chain and is explicit that "a member cannot shift the bag's fill" (`SHAPE-TREE-RULES.md:93`). Handing a fill a raw document clock *as well as* its node clock would give it two answers to "when is it" and let a fill quietly ignore its node's window — reintroducing the exact class of bug R3 exists to close. So the document clock reaches a fill only after remapping, as `phase01`. Reversible in one line if a future fill genuinely needs wall time; nothing in the first four does.

## FC-1.5 Category three — the sampling point, and what normalises it

This is the single most consequential unstated thing in the task, so it gets its own argument.

BC-3.2 is precise that the shape-local coordinate "is not published because it *is* the sampling point and comes back from the resolve query" (`BUFFER_CONTRACT.md:180`). B4 asks for it as "where am I inside this shape (a **normalised** coordinate)" (`SHAPER_THE_DESIGN.md:98`). Nothing anywhere says what normalises it. Three candidates were on the table.

**Rejected — the node's canvas-space support box.** `ShaperProgram.supportCx/supportCy/supportHalfW/supportHalfH` (`ShaperProgram.cs:80`) exists and is tempting. It is the wrong number, and the reason is visible in the shipped compiler: the box is built by mapping the primitive's *local* half-extents through the forward matrix with an absolute-value corner sum — `hw = |m00|·halfExtentX + |m01|·halfExtentY` (`ShaperCompiler.cs:178-179`). That is an axis-aligned box in canvas space, so it **grows and shrinks as the node rotates**. Normalising by it would make a gradient on a spinning shape breathe once per quarter turn, with nothing authored changing. That is the swim problem of B5 wearing a different hat, and it would be reported as a bug.

**Rejected — the layer canvas.** Normalising by the canvas means a small shape receives a small slice of the ramp and *changes colour when it moves*. That is not wrong — it is precisely and exactly the `Fixed` behaviour, which §F1.6 makes available as a setting. Making it the only behaviour would throw away B5's switch and give the Stamped case no home at all.

**Rejected for now — an authored rect.** It is the most flexible and it is a fourth thing to author on every fill on every node, when the node already declares a support extent that R7's cost model requires anyway (`SHAPE-TREE-RULES.md:71`). Recorded as a cheap later addition (§F8.7), not built.

> **FC-1.5 [D-T0106] — the anchor. In `Stamped` space, the fill's normalised coordinate is the canvas sample point mapped through the fill-owning node's accumulated root→node inverse transform, then divided by that node's own LOCAL support half-extents, honouring `FillFit`.**

In formula, for a node `N` whose accumulated forward matrix is `M`, with local support half-extents `(hx, hy)`:

```
(lx, ly) = M⁻¹ · (x, y)                                  // the same 2×3 the compiler already bakes per leaf
Uniform:  (u, v) = (lx, ly) / max(hx, hy)
Stretch:  (u, v) = (lx / hx, ly / hy)
```

Four reasons, in order of how much they matter.

**It is rotation-invariant and scale-stable, which the canvas box is not.** The local half-extents are the primitive's own declared numbers (`ShaperBakedPrimitive.halfExtentX/halfExtentY`, `ShaperPrimitives.cs:41`), fixed before any transform touches them. A node that rotates carries its pattern round with it and the pattern does not breathe. That is what "stamped onto the shape" means.

**The inverse matrix already exists and is already accumulated.** `ShaperCompiler.EmitLeaf` bakes the full root→leaf inverse into `m00…m12` (`ShaperCompiler.cs:171-172`, struct at `ShaperProgram.cs:47`) precisely so a leaf is reached with one 2×3 and no point stack. The fill stage wants exactly the same number for its own node. Nothing new is computed; one number is exposed.

**It composes correctly with R2 without a second rule.** R2's `σ_min` rescale exists so *distances* come back up in canvas pixels (`SHAPE-TREE-RULES.md:61`). The fill's coordinate travels the other way — down, in local space — which is R2's own "only the sample point travels down in local space" (`:61`) applied verbatim. So the fill inherits the transform model rather than inventing a parallel one.

**It gives `FillFit` something real to mean.** `Uniform` divides both axes by `max(hx, hy)`, so a radial gradient on a wide rectangle stays a circle; `Stretch` divides per axis, so the ramp runs end to end. That is `ZuiFill.cs:49-63` taken by value, and it is only expressible because the anchor is a box rather than a scalar.

**FC-1.5a — the compiler MUST publish a node-local support box, not only the canvas one.** This is the one addition T-0106 requires of T-0105's compiled program. Today `Emitted.box` is canvas-space only (`ShaperCompiler.cs:24-65`, produced at `:180`) and the local half-extents survive only inside `ShaperBakedPrimitive` before the transform is applied (`ShaperPrimitives.cs:41`, consumed at `ShaperCompiler.cs:178`). Carrying the box in local space alongside it is the same fold with the transform omitted, and it is needed for bags as well as leaves, because a bag's fill needs a bag-local box. Do **not** obtain it by mapping the canvas box back down through the inverse: the shipped longitudinal-sweep path does exactly that (`ShaperCompiler.cs:326-328`) and it is conservative-but-inflating under rotation, for the same absolute-value-corner-sum reason that rejected the canvas box above. It is correct there because a sweep only needs a conservative span; it is wrong here because a fill needs a stable one.

**FC-1.5b — degenerate anchor.** If `max(hx, hy) ≤ 1e-6` — an empty bag, a node whose every member was skipped, a `Box.Invalid` (`ShaperCompiler.cs:29`) — the normalised coordinate is `(0, 0)` and the fill still evaluates. A Solid still paints; a Gradient reads its ramp at `t` for `(0,0)`. It MUST NOT divide, produce NaN, or refuse to bind. This mirrors the shipped engine's own singular-transform rule, which publishes the empty field and flags rather than dividing (`ShaperCompiler.cs:131-142`).

## FC-1.6 The two coordinate spaces

> **FC-1.6 — `Stamped` (the default) is FC-1.5. `Fixed` is the canvas sample point normalised by the layer canvas half-extents, with the node's inverse transform NOT applied.**

`Stamped` anchors the pattern to the shape, so it travels, spins and scales with it. `Fixed` anchors it to the canvas, so the shape becomes a window onto a stationary backdrop. That is design B5's "pattern anchored to the world versus anchored to the shape" (`SHAPER_THE_DESIGN.md:134`) and `ZuiFill.cs:40-46` word for word.

**A non-positional fill ignores `space` entirely.** Solid never reads the coordinate; Ramp-by-quantity never reads it. The dial exists on Gradient and Texture only, which is `ZuiFill.cs:45-46` taken by value.

**`Fixed` is the case tile independence will catch if it is implemented lazily.** A `Fixed` fill reads the *absolute* canvas position, so it MUST be computed from the absolute sample index the way `ShaperSampleGrid.X(ix)` does (`ShaperEvaluator.cs:31-32`), not from a per-tile local origin. Computing it from a tile-local origin passes every visual check on a whole-grid render and seams on the first decomposed one — which is exactly what BC-1.6 exists to catch (`BUFFER_CONTRACT.md:88`) and is FT-11 in §F7.

## FC-1.7 What binds from the buffer contract, clause by clause

The task asks for this explicitly. Every row is a BC clause and what it does to the fill stage.

| BC clause | Binds the fill stage how |
|---|---|
| **BC-1.1** — no per-sample managed callback, virtual method or delegate | Directly. A fill's entry point is one call per tile. §F5.1. |
| **BC-1.2** — one call per tile per quantity-set; `(x0,y0,w,h)` plus stride; host-supplied Burst-addressable arrays; everything resolved before the call; no delegate crosses the boundary | Directly, all five bullets. §F5.1, §F5.2, §F5.3. The fill's signature mirrors `ShaperEvaluator.FillTile` (`ShaperEvaluator.cs:133-136`) including `dstOffset`/`dstStride`. |
| **BC-1.3** — the named illegal patterns | Two apply by name. "Per-pixel dereference of a managed `ZuiFill` holding a `Gradient`" (`BUFFER_CONTRACT.md:67`) is the exact thing §F5.4's LUT bake exists to prevent. `new System.Random(...)` (`:69`) is prevented by routing every dial through the hash-based `ShaperValue.Sample` (`ShaperValue.cs:33`). |
| **BC-1.6** — tile independence, bit-identical | Directly, and it is FT-11. A fill that reads only its own sample and the parameter block cannot seam; `Fixed` space and any future neighbour read can. |
| **BC-2.1** — no screen-space neighbour reads before the resolve | Directly. FC-1.2. A fill MUST NOT blur, dilate, sample a gradient of a sheet, or read `[i±1]`. If a fill wants a gradient of height it is asking for `surfaceDirection`, which BC-3.6 says the shape publishes and the consumer never differences (`BUFFER_CONTRACT.md:226`). |
| **BC-3.1** — vocabulary closed at nine; a consumer needing an unpublished quantity is greyed out with the reason shown, never hidden, never silently inert | Directly. This is the whole of §F4. |
| **BC-3.2** — sheets versus scalars versus the sampling point | Directly. FC-1.1's three categories are this clause. |
| **BC-3.3** — the nine, their types, units and frame; layer-local, +Y up, one frame for everything | Directly. FC-1.2's table. The frame is the shipped one (`ShaperField.cs:13`). |
| **BC-3.3 (closure)** — "no standard fill may depend on" a private non-vocabulary value, and such values "may not be given vocabulary-looking names" (`BUFFER_CONTRACT.md:214`) | Directly, and it is the reason Ramp-by-quantity picks from an enum of exactly nine rather than from a string. `ramp_t` in particular is called out as "a palette lookup position, which is a fill concern rather than a property of the shape" — so the fill computes it and never reads it. |
| **BC-3.4** — heat and density are two quantities, never the same array under both names | Indirectly: Ramp-by-quantity offers both and they are distinct picker entries. |
| **BC-3.5** — `depth` is entry-to-exit thickness, single-valued only while the ray enters and leaves once, identically zero until extrusion | Directly on availability: `depth` is declared unpublished in T-0106 and Ramp-by-`depth` greys out (§F4). |
| **BC-3.7a** — declared, not discovered | Directly, both ways: the shape declares what it publishes, and the *fill* declares what it requires. §F4.1. |
| **BC-3.7b** — never debug-gated | Directly. A fill's required-set check runs in normal operation, not behind a flag. |
| **BC-3.7c** — non-destructive; a sheet is readable any number of times | Directly. Two fills on two nodes read the same coverage sheet. A fill MUST NOT write to an input sheet. |
| **BC-3.7d** — one coordinate frame | Directly. FC-1.5's local frame is the layer frame composed with the node transforms; there is no second convention and no flip. |
| **BC-3.7e** — one definition per name | Directly on `coverage`: it means occupancy at the sample, before any layer-alpha multiply. A fill's veil is applied *after* and is a different quantity. |
| **BC-3.7f** — host-allocated, host-owned; the generator never allocates, replaces, resizes or frees a sheet | Directly, and it extends to the fill's *outputs*: the albedo, veil and height-delta arrays are host-allocated too. §F5.2. |
| **BC-3.8** — the conformance-test shape: declared set == written set, full tile coverage, range, frame | Mirrored in §F7 for the fill stage. |
| **BC-4.2** — the bound, the safe march step | Does **not** bind the fill stage. A fill emits no distance and nothing marches on its output. Recorded so nobody adds a bound to a fill by analogy. |

---

# Part F2 — what a fill emits

## FC-2.1 The three outputs

> **A fill emits albedo, always; a veil, always; and a height delta, only if it declares that it does. It emits nothing else. In particular it emits no shine, no specular, no rim and no lit colour — that is B4's load-bearing simplification and it belongs to T-0108.**

`SHAPER_THE_DESIGN.md:116`: "A fill emits **albedo** — flat, unlit colour — and optionally a **height delta**. The document's light rig turns albedo plus height into the final pixel. Shine belongs to the lights, not to the paint."

## FC-2.2 Albedo — three floats, no alpha

> **FC-2.2 [D-T0106] — albedo is three channels. It has no alpha channel. The veil is the one and only transparency authority.**

The reason is the strongest single argument in this document and it is not mine — it is the shipped code's. `ShaperBlend` was deliberately split into `width` and `carveStrength` because the reference app's single `viscosity` was "a band half-width in canvas units for a soft add and a dimensionless fraction of the cut for a soft subtract — the same slider showing two different quantities" (`ShaperNode.cs:11-15`). `ShaperSweep` was split into four fields for the identical reason (`ShaperNode.cs:30-37`). A fill emitting *both* an albedo alpha and a veil, with no rule saying which wins or how they combine, is the same defect a third time, and it is exactly the defect `ZuiFill` has today (§F0.2: `color`'s alpha means translucency in Solid mode and pattern-mask in Grid and Dots mode, `ZuiFill.cs:68` versus `:143`).

Consequences, stated so they are not discovered:

- **A ported `ZuiFill` Grid or Dots mask routes into the veil, not into albedo.** Those are not among the first four fills, but they are the obvious fifth and sixth, and this is where their mask goes.
- **A texture's alpha channel multiplies into the veil** (§F6.4). It does not tint the albedo.
- **An authored `Color` dial's alpha is not silently dropped — it is not authored.** A fill's colour dials are three-channel in the UI. If a colour dial were left four-channel, an author would set alpha, see nothing happen, and file a bug. (The dial is stored as `Color` because that is what `ZuiGradient` returns and what Unity's picker gives; the alpha channel is read at exactly one place, the texture's, and everywhere else it is documented as unused. That is the honest version — a three-float struct would be cleaner and would break gradient reuse.)

## FC-2.3 Albedo colour state

> **FC-2.3 [D-T0106] — albedo is LINEAR, non-premultiplied, float per channel. sRGB decode happens once at the authored-colour boundary at compile time; sRGB encode happens once at the document's `Color32` write.**

The project is in **Gamma** colour space — `m_ActiveColorSpace: 0` at `ProjectSettings/ProjectSettings.asset:50` — so Unity performs no conversion for us and this is a choice the fill stage makes for itself, not one the engine imposes.

The reason to choose linear: **everything the fill stage and the light stage do to albedo is a multiplication.** Veil multiplies it, coverage multiplies it, the light rig multiplies it (T-0108), and additive compositing sums it. Every one of those is wrong in an sRGB-encoded space — coverage-weighted compositing in sRGB is the classic dark-fringe artefact, and albedo × light in sRGB is the classic muddy-shading artefact. Since B4's whole architecture is "a fill emits albedo, the lights multiply it", getting the space wrong makes the central ruling produce visibly worse pictures than the thing it replaces.

The costs, stated honestly:

- **Two conversions.** Neither is per-sample. The decode is per authored colour at compile time — 256 entries of a gradient LUT (§F5.4), or one colour for a Solid. The encode is once per output pixel at the document's `Color32` write, which happens exactly once regardless.
- **Round-trip fidelity.** `byte → float → linear → float → byte` with the standard sRGB transfer function in `float32` round-trips exactly for all 256 values. That is a claim, not a measurement — **I did not run it** — so it is FT-2 in §F7 and MUST be measured before the decision is treated as settled.
- **Divergence from Pyre.** The same authored gradient will not produce the same bytes in Shaper as in Pyre, because Pyre composites in sRGB. That is a real difference and it will be noticed when the two are compared side by side. It is a difference in Shaper's favour but it should be expected rather than reported.

**The escape hatch, named rather than built.** Both conversions live at exactly two named boundaries, one function each. If the owner would rather have byte-for-byte Pyre parity than correct compositing, making both functions the identity is a two-line change at two sites and no other code moves. **Do not add a dial for this.** A colour-space mode is a document-wide correctness property, not a per-fill preference, and adding a switch would mean every conformance test in §F7 has to run twice.

**FC-2.3a [T-0106 fix pass, 2026-08-31] — the `Color32` write is STRAIGHT ALPHA, and there is exactly ONE of it.** FC-2.3 says albedo is non-premultiplied and that the encode happens once at the document's `Color32` write; it did not say which alpha convention those bytes carry, and the first implementation shipped two encoders and made the wrong one the default.

*The superseded state:* `EncodePremultiplied` was the default. It premultiplied in LINEAR and then sRGB-encoded the premultiplied value — `encode(rgb · α)` — which is neither straight alpha nor the conventional premultiplied-sRGB `encode(rgb) · α`, so neither kind of consumer could read it back. *Measured, linear-white fill, byte per alpha:* α = 0.75 gives 225 where straight wants 255 and conventional premultiplied wants 191; α = 0.5 gives 188 against 255 and 128; α = 0.25 gives 137 against 255 and 64; α = 0.1 gives 89 against 255 and 26. At α = 0.5 a premultiplied consumer reconstructs 1.47× too bright and a straight-alpha consumer 26% too dark; over all 256 codes at α = 0.5 the worst straight-alpha misread was **67 codes**, and it is now **0**.

*What now holds:* one function, `ShaperFillResolver.Encode`, un-premultiplying in linear and encoding once, producing straight-alpha bytes. That is what FC-2.3 specifies, what a PNG requires (the format has no premultiplied mode to declare), and what Unity's `SrcAlpha OneMinusSrcAlpha` sprite blend consumes. The premultiplied encoder was **deleted**, not repaired, and the reason is worth keeping: its justification was that only premultiplication can represent an additive glow over nothing (α = 0, RGB > 0, FC-2.6b), and that is true of premultiplication and false of any correct 8-bit encoding of it — the conventional premultiplied byte for that sample is `encode(rgb) · 0 = 0`, which deletes the glow just as thoroughly. Keeping a second encoder would have traded a documented limitation for an undocumented misencoding, and it had no caller.

*Where the additive case lives instead — both answers are real and both are in use:* read the float destination directly, which is linear premultiplied and carries the glow exactly, and is what a light stage or a further composite should consume; or composite over an opaque backdrop BEFORE the encode, at which point the glow is ordinary brightness and every byte is meaningful (the contact sheet does exactly this, which is the only reason an additive cell can be shown at all). `Encode`'s own `α ≤ 1e-6` branch does not divide, so an additive sample keeps its colour bytes and honestly reports α = 0; a straight-alpha blender will discard it, and that is the limitation rather than a third answer.

## FC-2.4 The veil

> **FC-2.4 — the veil is a scalar in [0,1] that MULTIPLIES the shape's published coverage. It can never REPLACE the shape's edge. It is clamped to [0,1] by the fill stage at publish, and the clamp is load-bearing.**

Effective coverage at a sample is:

```
coverageEff = clamp01(coverage) * clamp01(veil)
```

**What enforces "can never replace the edge" is two things, not one, and the one-line rule only names the first.** Multiplication gives you the easy half for free: `veil × 0 = 0`, so no veil value whatsoever can create paint where the shape published no coverage. The shape's outer edge is inviolable by construction. But the *soft* edge is not, and this is where the one-line rule is insufficient: at a half-covered antialiasing sample, `coverage = 0.5`, and a veil of `2.0` would give `coverageEff = 1.0` — a fill silently thickening the shape's edge by half a pixel and hardening its antialiasing. That is replacing the shape's edge, achieved entirely through multiplication. **The [0,1] clamp on the veil is what actually enforces C3's "The shape always owns its own edge. A fill can veil it but never replace it" (`SHAPER_THE_DESIGN.md:321`).** Write the clamp with that sentence as its comment.

**FC-2.4a [D-T0106] — the veil may not exceed 1, and this is deliberately asymmetric with coverage.** FC-1.6 says incoming coverage is treated as unbounded above, per BC-3.3. The veil is *not*, because coverage's unboundedness belongs to the shape (a fog accumulating past 1 is meaningful) and the veil belongs to the fill (a fill claiming more than total opacity is not). A fill that wants to make something brighter than opaque uses `Add` compositing, which is what additive compositing is for (FC-2.6). Note that `coverageEff` therefore *can* still exceed 1 when a fog generator lands — the clamp is on the veil, and the final clamp on `coverageEff` happens at the composite (FC-2.6), which is BC-3.3's "consumers clamp at use" applied at the last possible moment.

**FC-2.4b — the veil is separate from coverage in storage as well as in meaning.** The fill writes a veil sheet; it MUST NOT write back into the shape's coverage sheet. BC-3.7c makes coverage readable by any number of consumers (`BUFFER_CONTRACT.md:255`), and a fill that mutated it would break the next consumer — the border stage (T-0107) reads the same coverage, and an outward border joins the node's *published* coverage per C5 (`SHAPER_THE_DESIGN.md:333`), which is the shape's number and not the fill's.

## FC-2.5 The height delta

> **FC-2.5 — the height delta is a scalar in layer-local height units (BC-3.3 #2), ADDED to the shape's own height, weighted by `coverageEff`. It is optional in the strict BC-3.7a sense: a fill DECLARES at compile time whether it emits height, and the host allocates the height-delta sheet only if some fill in the layer declares it.**

"Optional" mechanically means declared-then-allocated, never discovered-then-allocated. `BUFFER_CONTRACT.md:253` (BC-3.7a) is explicit that today's set "is discovered from whatever string literals a form happens to pass" and that this is the defect. A fill kind that can emit height (all four of the first four can, via a `heightDelta` dial) declares `emitsHeight = (heightDialResolvesNonZero)` at compile time — so a Solid with `heightDelta = 0` costs no sheet, and a Solid with `heightDelta = 3` does.

The composition against the shape's own height is `height_final = height_shape + heightDelta · coverageEff`. Three notes:

- **The shape's own height does not exist in T-0106** (Part 0, finding 1) — it arrives with T-0109's extrusion, where C3 says it is "worked out by a fixed recipe from its own published edge distance" (`SHAPER_THE_DESIGN.md:323`). So in T-0106 the height delta is produced, is testable, and is consumed by nothing. That is correct and it is not a reason to defer it: it is the hook that makes T-0110 nearly free (§F8.3).
- **The weighting by `coverageEff` is what makes a veiled fill not punch a hole in the relief.** A fill at half veil raises the surface half as much, which is the intuitive reading and the only one that degrades continuously to zero.
- **Height is NOT affected by the Over/Add switch** (FC-2.6c).

## FC-2.6 The composite declaration

> **FC-2.6 [D-T0106] — Over versus Add is a per-fill AUTHORED field with a per-kind DEFAULT, resolved into the compiled form at compile time. It is not a per-fill-kind static declaration, and it is not a per-sample decision.**

B4 says "A fill also declares **how it composites** — over, or added. Additive light is not expressible by coverage and colour alone, and leaving it out is how a glow ends up looking like paint" (`SHAPER_THE_DESIGN.md:109-110`). "Declares" is satisfied by an authored field baked at compile time: the declaration is static per invocation, which is all BC-1.2 needs.

**Why not per-kind.** The same Gradient is genuinely wanted both ways — a painted gradient and an additive glow gradient are one fill kind with one dial different. A static per-kind declaration would force every fill kind to be duplicated into an additive twin, which is four kinds becoming eight before a single new capability exists. That is the taxonomy failure B4 explicitly dissolves ("the list you gave stops being a taxonomy and becomes a list of plug-ins", `SHAPER_THE_DESIGN.md:112`).

**FC-2.6a — what `Over` means, exactly.** Against an accumulated destination `dst` (linear, non-premultiplied, with its own accumulated alpha `dstA`):

```
outA   = coverageEff + dstA * (1 − coverageEff)
outRGB = (albedo * coverageEff + dst * dstA * (1 − coverageEff)) / max(outA, ε)
```

with `coverageEff` clamped to [0,1] at this point and only at this point (FC-2.4a). An implementation MAY carry the destination premultiplied internally to avoid the divide — that is a representation choice inside the compositor and it does not change the fill's contract, which hands over non-premultiplied albedo.

**FC-2.6a-AMENDED [T-0106 fix pass, 2026-08-31] — `Over` describes how a FINISHED layer meets what is beneath it, and never how one silhouette's own exclusive owners meet each other.** The formula above is unchanged and remains correct for what it actually governs. What is amended is its SCOPE.

*The superseded reading, recorded so it is not re-derived by accident:* FC-2.6a was read — by the T-0106 implementation, and defensibly, since nothing here said otherwise — as the rule for compositing EVERY fill against the accumulated destination, including two fills that own disjoint parts of the same silhouette by FC-3.5. *Why that reading is wrong:* `outA = coverageEff + dstA · (1 − coverageEff)` is the accumulation of two INDEPENDENT coverages. Exclusive owners do not hold independent coverages; by FC-3.5 they hold a PARTITION of one pixel's area, and the accumulation of a partition is the sum. Asking `Over` about a partition under-reports, and the under-report is not a rounding residue: **measured on the FT-21 fixture, 244 of 16384 samples came out below the shape's own published coverage, total alpha deficit 29.288, worst 0.2484** — a one-pixel ring up to 25% transparent wherever one fill's region met another's inside a single silhouette. The arithmetic is the plainest statement of it: an ancestor at 0.5 and a descendant at 0.5 give `0.5 + 0.5 · 0.5 = 0.75` where the shape's own coverage is exactly 1.0.

*What now holds:* within one silhouette's paint pass the owners accumulate by claim-weighted sum (FC-3.5a); `Over` then describes the relationship of a finished subtree to what is under it — to a sibling subtree, and to the destination. Both halves are FC-2.6a's formula applied where it is true. Re-measured after the change, same instrument and same fixture: **0 of 16384 samples, total deficit 0.000, worst 0.0000**, with the sibling half unmoved — two overlapping siblings at veil 0.5 still read alpha 0.75000, which is `Over` and not a sum.

**FC-2.6b — what `Add` means, exactly.**

```
outRGB = dst * dstA + albedo * coverageEff       (accumulated premultiplied; the destination's own alpha unchanged)
outA   = dstA
```

Two properties that matter and are easy to get wrong. **Add does not increase alpha** — an additive glow over nothing stays transparent-but-bright, which is what makes it read as light rather than as paint; adding to alpha is exactly how a glow ends up looking like paint, which is B4's own phrasing for the failure. And **the veil still applies in Add mode**: `coverageEff` gates the additive contribution, so veil 0 contributes nothing. A veil is not an opacity in Add mode, it is a strength.

**FC-2.6c — the switch governs colour only. Height is always added.** `heightDelta · coverageEff` accumulates by summation regardless of mode. There is no "over" meaning for a height *delta* — a delta by definition adds — and giving the Over/Add dial a second meaning on a second output is the double-meaning FC-2.2 refused. State it in the code comment, because "Add mode" reads like it should change everything.

**FC-2.6d — the per-kind defaults.** Solid: `Over`. Gradient: `Over`. Ramp-by-quantity: `Over`. Texture: `Over`. All four default to `Over` because paint is the common case; the heat and soot ramps that B4 identifies as the fire and explosion palettes (`SHAPER_THE_DESIGN.md:112`) are usually authored to `Add`, and that is one dial the author sets, visibly, once.

---

# Part F3 — attachment and inheritance

## FC-3.1 The rule, stated so it cannot be got wrong

C4 (`SHAPER_THE_DESIGN.md:327`): "A fill is attached to a shape node. The **nearest ancestor that owns a fill paints the whole subtree**; a child that owns its own fill wins inside its own coverage. The default for a new bag is that the bag owns the fill and the children own none."

Restated as a resolution procedure, which is what an implementer needs:

> **FC-3.1 — For each fill-owning node `N` in the tree, `N`'s paint region is `min(coverage_N, coverage_A)`, where `coverage_N` is the coverage of `N`'s own subtree evaluated standalone and `coverage_A` is the finished coverage of the nearest ancestor that also owns a fill (or of the layer root, at the top). Regions are painted in FOLD ORDER — bottom-most member first — each compositing by its own declared mode. Ownership is exclusive per pixel: an ancestor's fill does NOT paint underneath a descendant's.**

## FC-3.2 The root always owns a fill, and that is what makes the rule total

> **FC-3.2 [D-T0106] — a shape layer's root node ALWAYS owns a fill. The root fill cannot be removed, only edited. A brand-new layer's root fill is a Solid, opaque, white, with zero height delta and `Over` compositing.**

Design B2 settles this without appearing to: "Add a layer. You get a disc in one flat colour. Nothing about bags, combine modes or sub-shapes is on screen" (`SHAPER_THE_DESIGN.md:65`). A new layer *paints* with nothing authored, so "nothing/transparent" contradicts the design directly.

The structural payoff is worth more than the aesthetic one: **making the root's fill non-removable makes the nearest-ancestor search total.** It can never fail, so there is no "no fill found" branch anywhere, no null owner, no undefined pixel inside a covered silhouette — an entire error class removed by one non-nullable field. It is also the fallback target for §F4's availability gate, which needs a fill that is *guaranteed* to bind, and a Solid requires nothing (§F6.1).

## FC-3.3 The hard case the one-line rule does not answer: a child whose mode is Subtract or Intersect

C4 says a child "wins inside its own coverage". For an `Add` member that is obvious. For the other two modes it is not, and FC-3.1's `min(coverage_N, coverage_A)` formula resolves all three uniformly — with one consequence that must be surfaced rather than computed silently.

**Add member.** `coverage_N` is the member's own silhouette; `coverage_A` is the finished bag. Their minimum is the member's silhouette clipped by whatever the bag ended up as — so a member that a *later* member carved a hole in does not paint into that hole. Intuitive, and it is the case the design was thinking about.

**Intersect member.** An Intersect member restricts the accumulator, so its own coverage is a *superset* of the result in the region that survives; `min(coverage_N, coverage_A) = coverage_A` there. So an Intersect member owning a fill paints the entire intersected result. That is well-defined and genuinely useful — the intersecting member is the cookie cutter and painting its surface across the cut region is the natural read — but it is *indistinguishable in output* from putting the same fill on the bag, and an author who does not know that will think one of the two did nothing. **Allowed, and the UI states the equivalence.**

**Subtract member.** This is the one that needs a ruling, and the ruling is inherited rather than invented. R3 is explicit: "**A Subtracted member contributes no values at all.** It removes coverage; it does not deposit heat" (`SHAPE-TREE-RULES.md:106`). Paint is a deposit. So:

> **FC-3.3 [D-T0106] — a Subtract member MAY NOT own a fill. The slot is greyed out with the reason, through §F4's mechanism, and the reason is "a subtracted member removes coverage; it deposits nothing".**

The alternative — allowing it and letting `min(coverage_N, coverage_A)` compute — is *not* identically zero, and that is precisely why it must be forbidden rather than left to the arithmetic. Where a Subtract member cut, the bag's coverage is zero and the product is zero. But where a *later* member re-added over the hole, the bag's coverage is non-zero and the product is not. So a fill on a Subtract member would paint exactly the parts of the subtractor that something else put back — a region nobody authored, nobody can predict, and which changes shape when an unrelated member above it is edited. That is a silent trap, and greying it out with the reason is BC-3.1's own prescription.

## FC-3.4 Overlapping siblings that both own fills

> **FC-3.4 — paint resolution order IS fold order. No stage may reorder it, for the same reason R1 gives for the fold: "No stage may reorder a bag's members. Not to batch the additive ones, not to group by generator, not to improve cache locality, not to skip ahead" (`SHAPE-TREE-RULES.md:31`).**

The bottom-most member (index 0) paints first; each member above composites onto the accumulated paint by its own declared mode. This is not merely consistent with R1, it is required by R1's stated reason: "B2's whole reason for having one nesting system is that two lists which look identical must not disagree about which end is which" (`SHAPE-TREE-RULES.md:33`). If paint resolved top-down while shape folded bottom-up, one authored list would mean two opposite things.

**Note carefully what "wins" means in C4.** C4's "a child that owns its own fill *wins* inside its own coverage" resolves **ownership** — whose fill applies — not **z-order between siblings**. Two sibling fills do not fight for ownership; they own disjoint claims and paint in order. Where both cover, the later one composites over (or adds to) the earlier one, which is more capable than replacement and is what `Over` means. Reading "wins" as "replaces" would make a translucent veil on the upper sibling produce a hole rather than a blend.

## FC-3.5 Ownership is exclusive: the ancestor does not paint underneath

> **FC-3.5 [D-T0106] — the nearest-ancestor rule assigns exactly ONE owner per pixel. An ancestor's fill does not paint beneath a descendant's.**

The alternative — the ancestor painting a backdrop that a translucent descendant shows through — is superficially attractive and wrong for two reasons. It makes one fill do two jobs (surface *and* backdrop) with no way to author "descendant over transparent", which is the double-meaning FC-2.2 refused. And it makes resolution O(depth) per pixel instead of O(1), because every ancestor in the chain must be evaluated at every pixel of the deepest descendant. If layered paint is wanted, that is what a second node is for — and it is expressible today, by adding a member that duplicates the silhouette and carries the upper fill.

**FC-3.5a [T-0106 fix pass, 2026-08-31] — exclusive owners ACCUMULATE BY CLAIM-WEIGHTED SUM, not by `Over`.** This is the clause FC-3.5 always implied and never said, and its absence is what let the seam through.

> An owner's paint region is `max(0, claim − descendantClaim)`, which by construction is DISJOINT from every one of its descendants' regions. Disjoint sub-areas of one pixel are parts of one whole, so their alphas ADD and their colours average by claim: `alpha = Σ claim_o` and `rgb = Σ claim_o · albedo_o` (premultiplied). SIBLING claims, by contrast, genuinely overlap and are NOT a partition, so siblings still composite `Over` in fold order (FC-3.4), and a finished subtree still meets the destination with `Over` (FC-2.6a as amended).

*The superseded reading:* that "exactly ONE owner per pixel" was fully discharged by making the claims disjoint, and that the compositor could then treat every owner uniformly. It cannot — making the regions disjoint and then compositing them as if they were independent throws the disjointness away again at the last step. *The measurement that forced the amendment:* 244/16384 samples, deficit 29.288, worst 0.2484, now 0/16384, deficit 0.000, worst 0.0000; the numbers in FC-2.6a-AMENDED, from the same instrument on the same fixture.

*What it costs, stated rather than hidden:* one 4-float-per-sample accumulator per owner (`ShaperFillBuffers.subtree`), because a subtree has to be finished before it can meet a sibling and a single flat destination cannot tell an ancestor from a sibling once both are mixed into it. Host-allocated once, on the same BC-3.7f terms as every other array; nothing is allocated inside a paint call and FT-9's IL scan is unchanged.

*One consequence that is a limitation and not a bug:* `descendantClaim` is a MAX over the descendants rather than a sum, because siblings may overlap and a sum would double-count them. Two SPATIALLY DISJOINT descendants at fractional coverage can therefore leave their ancestor more claim than the pixel has left, and the accumulated alpha is clamped at 1 at use (FC-2.4a's argument — alpha past total opacity has no meaning). RGB is deliberately not rescaled to match, because an additive result legitimately carries more light than its alpha.

**FC-3.5b [T-0106 fix pass, 2026-08-31] — the mixed case, defined rather than left to fall out.** Where one owner of a pixel declares `Over` and another declares `Add`, **the pixel's alpha is the sum of the `Over` owners' claims alone, and its RGB is the claim-weighted sum of EVERY owner's albedo.** An `Add` owner contributes light and never opacity — whether it meets the destination, a sibling, or an ancestor that shares the pixel with it. This is FC-2.6b's "Add does not increase alpha" carried unchanged into the partition, and it is written down because it is the one combination a reader would otherwise have to derive. Consequence worth naming: a silhouette whose only fill is additive stays fully transparent and fully bright, which is exactly what FC-2.6b asks for, and FT-17 still measures `alpha == 0` across all 16384 samples.

## FC-3.6 A disabled node needs no rule

`ShaperNode.enabled` (`ShaperNode.cs:81`) is honoured by the compiler: a disabled child is skipped outright (`ShaperCompiler.cs:208`) and a disabled root emits the empty field (`ShaperCompiler.cs:94-98`). So a disabled node contributes no coverage, its own coverage is empty, `min(coverage_N, coverage_A) = 0`, and its fill paints nothing — with no fill-stage rule at all. And a disabled node cannot orphan a subtree's fill, because disabling it removes the whole subtree from the shape. **State that this falls out rather than adding a check**; adding a redundant check is how the two allow-lists in `PyreRenderer.cs:950-952` and `PyreWindow.cs:2670-2672` came to be hand-synced (`BUFFER_CONTRACT.md:29`).

## FC-3.7 The default for a new bag

C4: "The default for a new bag is that the bag owns the fill and the children own none — which is the 'fuse several shapes, then texture as one' case, made the default rather than a mode" (`SHAPER_THE_DESIGN.md:327`). Implemented as: **a newly created member node's fill slot is empty, and drilling into a bag never creates one.** B2's UI rule is the same thing from the UI side: "A sub-shape's own fill row only exists once the bag's fill is not what you want" (`SHAPER_THE_DESIGN.md:70`).

R3 then makes the clock fall out with no second rule: "a fill runs on the clock of the node it is attached to… Combine the two and the default answer is the bag's clock, without a second rule to remember" (`SHAPE-TREE-RULES.md:85-87`).

## FC-3.8 The worked example, with the resolved per-pixel owner for each region

The tree — written bottom-up in fold order, exactly as R1 says the list reads:

```
Layer root  ......................  Bag "Body"        [fill: Gradient G_body]
  [0] Disc "Torso"    Add        ..  primitive        (no fill)
  [1] Disc "Head"     Add        ..  primitive        [fill: Solid S_head]
  [2] Rect "Belt"     Intersect  ..  primitive        [fill: Texture T_belt]
  [3] Disc "Eye"      Subtract   ..  primitive        (fill FORBIDDEN — FC-3.3)
  [4] Disc "Iris"     Add        ..  primitive        [fill: Ramp-by-coverage R_iris]
```

Geometry, for the worked answer: Torso and Head overlap; Belt is a horizontal band crossing the lower half of Torso only; Eye is a small disc inside Head; Iris is a smaller disc concentric with Eye.

Folding per R1 (`SHAPE-TREE-RULES.md:33`): accumulator starts empty; `Add Torso` → Torso; `Add Head` → Torso ∪ Head; `Intersect Belt` → **(Torso ∪ Head) ∩ Belt** — note this is the whole point of R7's ordered box fold (`SHAPE-TREE-RULES.md:205`) and it discards everything outside Belt, including all of Head; `Subtract Eye` → that, minus Eye, which is empty here since Eye lies in Head and Head is already gone; `Add Iris` → that ∪ Iris.

So `coverage_Body` = `((Torso ∪ Head) ∩ Belt) ∪ Iris`.

Resolved owner per region:

| Region | `coverage_Body` | Owner | Why |
|---|---|---|---|
| Torso ∩ Belt, not Head, not Iris | non-zero | **G_body** (the bag) | Torso owns no fill; nearest ancestor owning one is Body. |
| Torso ∩ Head ∩ Belt, not Iris | non-zero | **T_belt**, over **S_head**, over **G_body**'s claim | Fold order: Head (index 1) paints before Belt (index 2). Both claims are non-empty here, so both paint, Belt last. Body's claim `min(cov_Body, cov_Body)` also covers it — but ownership is exclusive (FC-3.5), and the deepest-claim-wins reading is wrong: FC-3.1 gives the bag a claim only where **no descendant claims**, so here Body does not paint. |
| Head ∩ Belt, not Torso, not Iris | non-zero | **S_head**, then **T_belt** over it | Same, without Torso. |
| Head, not Belt | **zero** | nobody | Intersect discarded it. Note S_head's *claim* is non-empty here (`coverage_Head` ≠ 0) but `min` with the bag's zero coverage kills it. This is the case that proves the `min` is load-bearing: without it, S_head would paint outside the silhouette. |
| Eye, inside Head, not Belt | zero | nobody | Already zero before the subtract. |
| Iris | non-zero | **R_iris** | Added last, after everything; its own claim, clipped by the bag's, which is itself here. |
| Iris ∩ Belt ∩ Torso | non-zero | **R_iris**, over T_belt, over S_head | Fold order again: index 4 paints last. |
| Anywhere outside `coverage_Body` | zero | nobody | Structural: `coverageEff = 0`. No veil value can change this (FC-2.4). |

Two things the example is deliberately built to show. **A member's fill claim can be entirely annihilated by a later member** (S_head outside Belt), which is why the resolver must evaluate `min(coverage_N, coverage_A)` and not `coverage_N` alone. And **the bag's own fill paints only where no descendant claims**, which is what makes the "fuse several shapes, texture as one" default (C4) actually produce one surface rather than a surface with holes in it.

## FC-3.9 What the resolver costs, and the one API addition it needs

Resolving `coverage_N` for a fill-owning node means evaluating that node's subtree standalone. The shipped `ShaperEvaluator.Distance` returns only `stack[0]` — the root's value (`ShaperEvaluator.cs:113`) — and `ShaperOp` carries no tap (`ShaperProgram.cs:34-61`), so an intermediate node's coverage cannot be extracted from a whole-tree program. Two routes:

- **(a) Compile one program per fill-owning node.** `ShaperCompiler.Compile(ShaperNode root, float phase01, uint seed)` already accepts *any* `ShaperNode` as a root (`ShaperCompiler.cs:88`), so this is nearly free. The catch: `Compile` seeds `ShaperMatrix.Identity` (`ShaperCompiler.cs:101`), so a subtree compiled standalone lands in its own local frame rather than the layer frame. **This contract therefore requires one addition to T-0105's API: an overload taking a starting forward matrix, which `EmitNode` already accepts as `parentForward` (`ShaperCompiler.cs:123`).** One parameter, one line, exact.
- **(b) Tap the RPN stack at named ops.** Cheaper at evaluation time — one walk instead of `k` — and it costs a per-op tap index, a tap output array, and a rule about what the stack holds at each op. It also couples the fill stage to the RPN layout.

> **FC-3.9 [D-T0106] — take route (a): one compiled program per fill-owning node, plus the `parentForward` overload. Route (b) is the optimisation to reach for if measurement says so.**

Reasons: it needs one line of new shape-engine code instead of a mechanism; each program is independently cacheable under R7's rules, which is worth more than the walk it saves ("A time-invariant subtree is evaluated once for the whole animation", `SHAPE-TREE-RULES.md:233`); and each program carries its own support box, so the fill's paint pass is bounded by R7's rule — "a node costs one pass over the pixels it can affect" (`SHAPE-TREE-RULES.md:202`) — rather than by the canvas. The honest cost: `k` fill-owning nodes means `k+1` field evaluations at a sample covered by all of them, and R7 already names node count as the *least* dangerous of its four multipliers (`SHAPE-TREE-RULES.md:223`). This is the one place this contract adds work to the shape stage, and it is named rather than hidden.

---

# Part F4 — the availability gate

## FC-4.1 What each side declares

> **FC-4.1 — a fill declares, at compile time, the set of quantities it REQUIRES and the set it OPTIONALLY reads. A shape node declares, statically before any render, the set it PUBLISHES (BC-3.7a). Both sets are over the closed nine of BC-3.3, expressed as a bit mask over an enum, never as strings.**

Strings are forbidden by name: BC-3.3 says "**Extending the vocabulary is a design decision requiring a new task, not a new string literal** — the free-form-string mechanism at `PyreForm.cs:156-159` is precisely how a vocabulary stops being one" (`BUFFER_CONTRACT.md:214`).

**FC-4.1a — the required set is a FUNCTION of the fill's authored settings, not a per-kind constant.** Solid and Texture require nothing regardless of settings. Gradient requires `edgeDistance` in one of its four modes and nothing in the other three. Ramp-by-quantity requires whichever quantity is picked. A per-kind constant cannot express any of those three, so the declaration is `RequiredSet(settings) → mask`, evaluated once at compile time — which keeps it out of the loop and satisfies BC-1.2.

**FC-4.1b — `coverage` is never in a required-but-missing set.** C3 and R5 both make coverage universal: "Every node publishes coverage, and a *consumer* names the node it reads" (`SHAPE-TREE-RULES.md:144`). A fill may require it and the requirement always holds. **`edgeDistance` is NOT universal** and MUST NOT be assumed — every shipped primitive is an exact SDF so today every node publishes it (`ShaperEvaluator.cs:148`), but R6 declares a coverage-only domain for generators that publish "a fog rather than an edge" (`SHAPE-TREE-RULES.md:190-192`), and those are coming with the simulation and Tapestry ports. Hard-coding "edge distance is always there" is exactly the day-one rule that quietly closes.

## FC-4.2 How a bag's published set is computed — intersection, and why

> **FC-4.2 [D-T0106] — `published(bag) = ⋂ { published(M) : M is an enabled member whose mode is Add or Intersect }`. A bag with no such member publishes the empty set. Subtract members are excluded from the intersection entirely.**

**Why intersection and not union.** Take a bag with member A publishing `heat` and member B publishing nothing. Under a union declaration, a Ramp-by-heat on that bag binds successfully and then reads *something* at every pixel B contributed — a zero, a stale value, whatever the sheet was initialised to. R3 already tells us what the value would be: continuous quantities "combine as a **coverage-weighted average** of the contributing members at that pixel" (`SHAPE-TREE-RULES.md:103`), and where only B contributes there are no contributors, so the average is undefined. A union declaration therefore produces a fill that binds and is wrong across part of its own shape — which is BC-3.1's "silently inert" failure in its more damaging variant, because it is not inert, it is confidently incorrect. Intersection means the quantity is meaningful *everywhere the bag has coverage*, which is the only guarantee a fill can actually use.

**Why Subtract members are excluded.** They deposit nothing (R3, `SHAPE-TREE-RULES.md:106`), so their published set is irrelevant to what can be read at any pixel. Including them in the intersection would let a subtractor that publishes nothing disable a quantity for a bag it only ever removed from — a pure false negative.

**The honest cost of intersection, and what makes it acceptable.** It is a cliff, not a slope: adding one member that does not publish `heat` disables every heat-driven fill on the whole bag and on every bag above it. That is genuinely annoying, and the mitigation is the entire reason FC-4.3's reason string names the *member*: the author is told exactly which node to fix, so the cliff has a visible cause and a one-click remedy. Without the named member, intersection would be indefensible.

**Union is the reversal if this proves too aggressive in practice**, and it is one line — but it must not be taken without also deciding what a fill reads in a non-contributing member's region, which is a real design question and not a default.

## FC-4.3 The reason string

> **FC-4.3 — the reason is a complete sentence naming the fill kind, the node the fill is on, the missing quantity, and where it went missing.**

Two shapes, and no others:

- Leaf: `"Ramp-by-quantity on 'Torso' needs heat, which this shape does not publish."`
- Bag: `"Ramp-by-quantity on 'Body' needs heat, which member 'Belt' does not publish."`

**FC-4.3a — the count is recorded as well as the first name.** This follows the shipped precedent *including its correction*: `ShaperProgram` records `leadingNonAddNode` for the first offender and `leadingNonAddCount` for all of them, with the comment "a tree with three offending bags would have surfaced one and silently hidden two — and the flag exists precisely so the UI can point at the problem" (`ShaperProgram.cs:90-95`, set at `ShaperCompiler.cs:217-222`). The same shape applies here and for the identical reason. Do not repeat the mistake that correction fixed.

## FC-4.4 What the RUNTIME does — because "greyed out" is a UI word and this task builds no UI

> **FC-4.4 [D-T0106] — the fill resolver REFUSES TO BIND an unavailable fill, records a diagnostic on the compiled fill program, and paints that node's region with the nearest ancestor fill that DOES bind — which is at worst the layer root's Solid, which always binds (FC-3.2). It never falls back to a different quantity, never substitutes a default value for the missing one, and never paints nothing.**

The diagnostic mirrors `ShaperProgram`'s flags exactly (`ShaperProgram.cs:82-102`), which is the established precedent for "record a UI flag rather than silently fixing it":

- a boolean `hasUnavailableFill`;
- `unavailableFillReason` — the first offender's full FC-4.3 sentence;
- `unavailableFillNode` — the first offender's node name;
- `unavailableFillCount` — how many there are in the whole program.

**Why fall back rather than paint nothing.** Painting nothing is *silently inert* — the thing BC-3.1 names as "the single most-reported confusion in the existing tool" (`BUFFER_CONTRACT.md:174`) — because a hole inside a silhouette looks like a shape bug, not a fill bug. Falling back paints something visible while the diagnostic carries the reason, which is the closest runtime analogue of a greyed control: the control is present, its reason is stated, and the thing it would have done does not happen.

**Why fall back to the nearest *binding* ancestor rather than straight to the root.** It is the same rule as everything else in §F3, with unbindable owners skipped. A special-case jump to the root would be a second resolution rule, and two rules that mostly agree is how the two hand-synced border allow-lists happened.

## FC-4.5 The day-one availability table — the gate is exercised immediately

Given Part 0's finding, this is the actual state on the first build of T-0106, not a hypothetical:

| Quantity | Published by the shipped shape engine? | Consequence for the first four fills |
|---|---|---|
| `coverage` | **Yes** (`ShaperEvaluator.cs:149`) | Ramp-by-`coverage` binds. It is therefore the default pick (§F6.3). |
| `edgeDistance` | **Yes** (`ShaperEvaluator.cs:148`) | Gradient's `ByEdgeDistance` mode binds; Ramp-by-`edgeDistance` binds. |
| `height`, `heat`, `density`, `soot`, `age` | **No** | Ramp-by-those greys out with the reason. Five of the eight picker entries. |
| `depth` | **No**, and identically zero until T-0109 (BC-3.5, `BUFFER_CONTRACT.md:222`) | Greys out. Its reason should say "until extrusion exists" rather than a bare "not published", because that one has a scheduled answer. |
| `surfaceDirection` | **No**, and it is a `float×3` | Not offered by Ramp at all — §F6.3, a type refusal rather than an availability one. |

So the gate is not a facility waiting for a future generator: **six of Ramp-by-quantity's eight offerable entries are unavailable on the day it ships.** That is the right way round — it means FT-8 in §F7 is a real test of a real path rather than a mock.

---

# Part F5 — the evaluation shape

## FC-5.1 The call

> **FC-5.1 — a fill fills a rectangular tile in ONE call, writing into host-supplied flat arrays, with the same tile parameters the shape stage already uses. Its inner loop contains no virtual method, no interface call, no delegate, no allocation, no boxing, no LINQ and no `System.Random`.**

The signature mirrors `ShaperEvaluator.FillTile(program, grid, x0, y0, width, height, distance, coverage, dstOffset, dstStride, stack)` (`ShaperEvaluator.cs:133-136`) including `dstOffset` and `dstStride`, so the fill stage and the shape stage tile identically and a host can drive both from one loop.

Inputs to the call: the compiled fill form; the `ShaperSampleGrid` (`ShaperEvaluator.cs:11-33`); the tile rectangle; the *input* sheets the fill declared it reads, as host arrays; the parameter block of FC-1.3b. Outputs: the albedo array (3 floats per sample, or one `float3`-shaped container), the veil array, and the height-delta array if declared. All host-allocated, host-owned, never allocated or replaced or freed by the fill — BC-3.7f (`BUFFER_CONTRACT.md:258`), extended to outputs.

## FC-5.2 Flat compiled form, not one object per tile — and BC-1.1 does not settle it, so here is the actual argument

The task rightly asks whether a per-tile virtual call is legal. **Read literally, BC-1.1 permits it.** It forbids invocation "once per sample through a managed callback, virtual method or delegate" (`BUFFER_CONTRACT.md:49`); once per *tile* is not once per sample, and a virtual call amortised over a few thousand samples is free. So BC-1.1 alone does not decide this.

What decides it is **BC-1.2's third bullet and BC-1.4**. BC-1.2 requires destination arrays "in a container a Burst kernel can actually address" and says explicitly "Which container type is a build decision; that it must be one Burst can take is not" (`BUFFER_CONTRACT.md:57`). BC-1.4 then promotes Burst from an optimisation to a precondition: "If BC-1 is broken, BC-2 is decorative" (`BUFFER_CONTRACT.md:76`). A virtual method on a managed class cannot be Burst-compiled at all, so an object-per-tile design satisfies BC-1.1's letter and forfeits BC-1.4's purpose. BC-4.2's own correction says the same thing from the enforcement side: the blittable signature "closes one door… The thing that turns those into compile errors is actually compiling the kernel with Burst" (`BUFFER_CONTRACT.md:288`).

> **FC-5.2 [D-T0106] — a fill compiles to a flat, blittable op form and is invoked through a static entry point that switches on the op kind, exactly as `ShaperEvaluator.Distance` switches on `ShaperOp.kind` (`ShaperEvaluator.cs:57`).**

And the trade is genuinely favourable here, which is why this is not merely dogma: **a fill does not nest.** The shape stage went flat-RPN because a bag holds arbitrarily deep children and a recursive evaluator would need a stack walk per sample. A fill is one object on one node with no children, so its "program" is a *single op struct*. The flat form is therefore a struct and a switch — strictly *simpler* than an abstract class with four subclasses, not a concession. It also makes dial resolution identical across both stages, so there is one rule about when a `ZUIValue` is read rather than two.

The one honest cost: a fill kind cannot carry arbitrary managed state on the op. Texture needs pixel data and Tapestry (T-0111) will need a cached surface. FC-5.5 handles that.

## FC-5.3 Dials are resolved at compile time, through `ShaperValue.Sample`

> **FC-5.3 — every authored `ZUIValue` on a fill is sampled once per compile, into the op, via `ShaperValue.Sample(v, phase01, seed)` (`ShaperValue.cs:26`). No value funnel is called from inside the tile loop.**

BC-1.2: "the value funnel must be evaluated *into* the parameter block up front, not called *from* inside it" (`BUFFER_CONTRACT.md:59`). Using the shipped `ShaperValue` rather than `ZUIValue.Evaluate` directly is not a style preference — `ShaperValue`'s own header records that `ZUIValue.Evaluate(t)` divides by the value's `duration` (default 4 s) and would sweep only the curve's first quarter (`ShaperValue.cs:9-13`). Same trap, same fix, one code path.

## FC-5.4 The gradient LUT — and the distinction that is easy to conflate

> **FC-5.4 [D-T0106] — a gradient dial is baked at compile time into a host-owned LUT of 256 linear RGB triples, and is read per sample by POINT lookup with no interpolation between entries.**

`BUFFER_CONTRACT.md:67` names "per-pixel dereference of a managed `ZuiFill` holding a `Gradient`" (`ZuiFill.cs:24`, called at `PyreRenderer.cs:4990-4991`) as a BC-1.3 offender. Baking removes it entirely.

**256 entries, justified rather than assumed.** The LUT quantises the ramp's *position*, not its colour, so a hard stop lands within 1/256 of its true position. B9's canvas is authorable 32–256 wide and capped at 68 000 cells (`SHAPER_THE_DESIGN.md:204`, verified in the reference app at `BUFFER_CONTRACT.md:307`), so 1/256 of the anchor span is at or below one output pixel on any canvas the tool allows. More entries cannot be seen; fewer can.

**Point lookup, not lerp, and this one is a real decision.** Interpolating between LUT entries re-softens exactly the hard stops that a pixel-art gradient exists to create — B5 already names "a palette-quantise step so the output stays pixel art rather than photographic" as one of three things a ported surface needs (`SHAPER_THE_DESIGN.md:135`). Since the LUT is already at or above output resolution, interpolation buys nothing and costs the hard stop. Reversible in one line if a smooth ramp on an oversized canvas ever looks stepped.

**The conflation to avoid.** `BUFFER_CONTRACT.md:37` criticises the Kiln composite forms for baking "a 1-D LUT by sampling at `(0.5, 0.5)` — not `(0,0)` — once per ramp entry" (`PyreShade.cs:209`, `PyreForkBlast.cs:373`, `PyreInferno.cs:544`). That is **not** a criticism of LUTs. It is a criticism of collapsing a *spatial* fill to a single sample point: those forms bake a whole `ZuiFill` — including its `Linear`/`Radial` spatial modes — down to one 1-D table, so every spatial mode silently becomes a constant. Baking a *gradient*, which is 1-D by definition, loses nothing. FC-5.4 does the second and MUST NOT be extended to do the first: a Gradient fill's spatial parameter `t` is computed per sample from the anchor coordinate and only the colour lookup comes from the table.

## FC-5.5 Bulk data — the one slot for things that are not floats

> **FC-5.5 — a compiled fill op MAY reference host-owned bulk data by index: an offset, a width, a height and a stride into a flat host array. It MUST NOT hold a `Texture2D`, `Sprite`, `Gradient` or any other managed reference.**

Texture uses it for pixels, decoded to linear at compile time. Gradient uses it for its LUT. T-0111's Tapestry surfaces will use it for a cached surface with no contract change (§F8.5). The array is host-allocated and host-owned on exactly the terms BC-3.7f sets for sheets, and it is the shape a `NativeArray<T>` takes, which is what BC-1.2's Burst-addressability clause asks for (`BUFFER_CONTRACT.md:57`).

## FC-5.6 Order of operations within one sample

Fixed, so two implementations cannot disagree:

1. Compute the anchor coordinate `(u,v)` per FC-1.5 / FC-1.6 (positional fills only).
2. Read the declared input sheets at this sample.
3. Compute the fill's own parameter `t` (Gradient's projection, Ramp's remap) — clamped or wrapped per kind.
4. Look up or compute albedo, in linear (FC-2.3).
5. Compute the fill's own veil term; multiply in any kind-specific mask (a texture's alpha, FC-6.4c); clamp to [0,1] (FC-2.4).
6. Compute the height delta, if declared.
7. Write albedo, veil and height delta to the host arrays. **The fill does not composite.** Compositing against the destination (FC-2.6) is the host's, after the ownership resolution of §F3 has decided who paints here.

Step 7's last sentence matters: keeping the composite out of the fill is what lets the resolver decide ownership once per pixel and lets `Over` and `Add` be a two-line branch in one place rather than duplicated into every fill kind.

---

# Part F6 — the first four fills, specified

Common to all four, and not repeated per kind: `veil` (`ZUIValue`, default 1, clamped [0,1] at bake per FC-2.4), `heightDelta` (`ZUIValue`, default 0, layer-local height units per FC-2.5), `composite` (`Over | Add`, default `Over` per FC-2.6d).

## FC-6.1 Solid

| Dial | Type | Default | Notes |
|---|---|---|---|
| `color` | `Color` | white, opaque | The alpha channel is **not authored and not read** (FC-2.2). |

**Requires:** nothing. **Optionally reads:** nothing. **Emits height:** iff `heightDelta ≠ 0`. **Composite default:** `Over`. **Space/fit dials:** none — it is non-positional (FC-1.6).

Per-sample: `albedo = decodeSrgb(color)`, `veil = veilDial`, `heightDelta = hDial`. All three are constants over the tile, resolved at compile time; the loop is three stores.

**Identity / no-op:** white, veil 1, height 0, `Over`. Output is the node's coverage in white. **This fill always binds** — it requires nothing — which is what makes it the mandatory root fill (FC-3.2) and the fallback target of FC-4.4.

## FC-6.2 Gradient

| Dial | Type | Default | Notes |
|---|---|---|---|
| `mode` | `Linear \| Radial \| Angular \| ByEdgeDistance` | `Linear` | B4's four (`SHAPER_THE_DESIGN.md:112`). |
| `gradient` | `ZuiGradient` | a sensible two-stop | Baked to a 256-entry linear LUT at compile time (FC-5.4). Null → falls back to Solid-of-`tint` and records a diagnostic (FC-6.5). |
| `angleDegrees` | `ZUIValue` | 0 | `Linear`: the axis direction. `Angular`: the phase where `t = 0`. Unused by the other two. |
| `centre` | two `ZUIValue`s | (0, 0) | In the normalised anchor space. `Linear`: the axis passes through it. `Radial`/`Angular`: the origin. |
| `size` | `ZUIValue` | 1 | A **SIZE, not a frequency** — bigger means the pattern spreads further (`ZuiFill.cs:83-88` taken by value). The reciprocal is taken once at compile time. Unused by `ByEdgeDistance`. |
| `depthPixels` | `ZUIValue` | 8 | `ByEdgeDistance` only: how far in from the edge the ramp reaches, in **canvas pixels**. |
| `space` | `Stamped \| Fixed` | `Stamped` | FC-1.6. Ignored by `ByEdgeDistance`, which is not positional. |
| `fit` | `Uniform \| Stretch` | `Uniform` | FC-1.5. Ignored by `ByEdgeDistance`. |

Per-sample, with `(u,v)` the anchor coordinate, `(cx,cy)` the centre, `z = 1/size`, `θ = angleDegrees·π/180`:

- **Linear** — `t = clamp01((((u−cx)·cosθ + (v−cy)·sinθ) · z + 1) · 0.5)`. This is `ZuiFill.cs:197-198` by value.
- **Radial** — `t = clamp01(hypot(u−cx, v−cy) · z)`. This is `ZuiFill.cs:215-217` by value; the distance is measured *from* the centre, so an off-centre gradient drifts toward a border rather than squashing.
- **Angular** — `t = frac((atan2(v−cy, u−cx) − θ) / 2π)`. **New**: `ZuiFill` has no angular mode (`ZuiFill.cs:29`). Note `frac`, not `clamp01`: an angular ramp is cyclic, and clamping would pin a hard band at the wrap. The wrap seam is visible unless the gradient's first and last stops match — that is inherent, and the UI should say so rather than the fill trying to hide it.
- **ByEdgeDistance** — `t = clamp01(−edgeDistance / max(depthPixels, ε))`. **The negation is the polarity of FC-1.3**: the shipped field is negative inside (`ShaperField.cs:9-10`), so `−edgeDistance` is depth-into-the-shape in canvas pixels, `t = 0` at the boundary and `t = 1` at `depthPixels` deep. `BUFFER_CONTRACT.md:190` records that porting Pyre's opposite-polarity band expression literally inverts it; that sentence belongs in this expression's comment.

**Requires:** `edgeDistance` in `ByEdgeDistance` mode; **nothing** in the other three. This is FC-4.1a's per-settings requirement in its simplest form. **Optionally reads:** nothing. **Emits height:** iff `heightDelta ≠ 0`. **Composite default:** `Over`.

**Identity / no-op:** a gradient whose stops are all one colour bakes to a constant LUT, so Gradient degenerates to Solid of that colour bit-identically in every mode. That is FT-3 and it is worth having because it exercises the LUT path against the trivial path.

**A note on which mode is which.** `ByEdgeDistance` is the one B4 singles out as "the last is the one that reads a quantity" (`SHAPER_THE_DESIGN.md:112`), and it is also the one that makes rims, inner glows and contour bands free — the four uses B4's own table lists against the edge-distance row (`SHAPER_THE_DESIGN.md:99`).

## FC-6.3 Ramp-by-quantity

This is the worked example of §F4 and its specification is mostly about the gate.

| Dial | Type | Default | Notes |
|---|---|---|---|
| `quantity` | one of eight | `coverage` | **The pick drives the availability gate.** Default `coverage` because it is the one quantity guaranteed available on every node (FC-4.1b), so a freshly created Ramp never greys out on creation. |
| `gradient` | `ZuiGradient` | two-stop | Baked to a LUT, as Gradient. |
| `inputLow` | `ZUIValue` | 0 | The value of the quantity that maps to `t = 0`. |
| `inputHigh` | `ZUIValue` | 1 | The value that maps to `t = 1`. |

Per-sample: `t = clamp01((q − inputLow) / (inputHigh − inputLow))`, `albedo = LUT[t]`.

**FC-6.3a — the degenerate window.** If `|inputHigh − inputLow| < 1e-6` the expression MUST NOT divide. It becomes a hard step: `t = (q ≥ inputLow) ? 1 : 0`. That is the meaningful limit of an infinitely narrow window and it produces a two-colour threshold, which is a useful thing to be able to author on purpose.

**FC-6.3b — inverted windows are legal.** `inputLow > inputHigh` gives a negative denominator and a reversed ramp. That is the natural way to invert without a second dial and it needs no special case.

**FC-6.3c [D-T0106] — the picker offers EIGHT of the nine. `surfaceDirection` is not offerable at all.** It is a `float×3` (BC-3.3 #9), and a direction cannot drive a scalar ramp without first choosing a projection axis — which is a different control with its own dials, not a value in this picker. So it is refused **on type**, not on availability, and its reason string says so: `"surface direction is a direction, not a number"`. This is deliberately a *different* message from the availability one, because the two failures have different remedies: one is fixed by adding a member that publishes the quantity, the other cannot be fixed at all from this fill. Conflating them would send an author looking for a shape change that would never help. (It also happens to close a loop with §F8.1: a fill that ramped on surface direction would be shading, which belongs to the lights.)

**Requires:** `{ quantity }` — dynamic, evaluated at compile time. **Optionally reads:** nothing. **Emits height:** iff `heightDelta ≠ 0`. **Composite default:** `Over`, with the note that heat and soot ramps are usually authored to `Add` (FC-2.6d).

**Identity / no-op:** with `quantity = coverage`, a black-to-white ramp and window 0..1, the albedo equals coverage in greyscale — trivially checkable against the coverage sheet, which makes this the cheapest end-to-end test of the whole pipeline (FT-4).

**What B4 says this fill IS.** "Ramp by quantity (heat or density or height into a palette — this is what the fire and explosion effects are already doing internally)" (`SHAPER_THE_DESIGN.md:112`). So its long-term job is to be the fill that replaces the nine imported effects' private palettes. It cannot do that job until `heat`, `density` and `soot` have publishers — which is exactly why the gate must be honest rather than permissive today.

## FC-6.4 Texture

| Dial | Type | Default | Notes |
|---|---|---|---|
| `texture` | `Texture2D` | none | Pixels are copied into host-owned bulk data at compile time (FC-5.5), decoded to linear. |
| `mapping` | `Fitted \| Tiled` | `Fitted` | Below. |
| `tilesPerUnit` | two `ZUIValue`s | (1, 1) | `Tiled` only. |
| `offset` | two `ZUIValue`s | (0, 0) | In UV units; animatable, which is how a texture scrolls. |
| `angleDegrees` | `ZUIValue` | 0 | Rotation of the UV frame about the anchor centre. |
| `tint` | `Color` | white | **Multiplies** the sampled albedo. The alpha channel is not authored (FC-2.2). |
| `space` | `Stamped \| Fixed` | `Stamped` | FC-1.6. This is the dial that fixes B5's swim. |
| `fit` | `Uniform \| Stretch` | `Uniform` | FC-1.5. |

**FC-6.4a — `Fitted` versus `Tiled`.** `Fitted`: the normalised anchor box maps to the texture's full 0..1 UV exactly once, so the image stretches to the node and never repeats. `Tiled`: `uv = frac(rotate(p) · tilesPerUnit + offset)`, so the image repeats at an authored density independent of the node's size. `Fitted` is the default because a first-time author dropping a texture on a shape expects to see the whole image.

**FC-6.4b [D-T0106] — POINT filtering only. There is no filter dial.** This is a pixel-art tool; bilinear on a pixel-art texture is the defect rather than the feature, and B5's own list of what a ported surface needs includes "a palette-quantise step so the output stays pixel art rather than photographic" (`SHAPER_THE_DESIGN.md:135`) — softening the source would work against that. A dial that is always set to one value is clutter, and the project rule against adding what was not asked for applies to dials as much as to menu items. If smoothing is ever wanted it is a new dial then, on evidence.

**FC-6.4c — the texture's alpha multiplies into the VEIL, not into the albedo.** FC-2.2 gives albedo no alpha, so there is nowhere else for it to go, and the veil is exactly the right place: a transparent texel means "this pattern does not cover here", which is what a veil says. `veil = veilDial × texelAlpha`, then clamped.

**FC-6.4d — a null, missing or non-readable texture falls back to `tint` as a Solid, AND records a diagnostic.** The fallback is `ZuiFill`'s own rule taken by value ("a null/unreadable sprite falls back to `color`", `ZuiFill.cs:164-165`, `:260`) and it is the right rule — "a half-configured fill never renders empty" (`ZuiFill.cs:165`). The diagnostic is the half `ZuiFill` cannot have. **The non-readable case is the one that will actually happen**, because it depends on the texture's import setting rather than on the asset being present, so the asset looks fine in the inspector and renders as a flat colour. Its reason string must name the import setting: `"Texture on 'Belt' is not marked Read/Write Enabled; painting flat tint instead."`

Note that the new contract makes `ZuiFill`'s per-sample null-cache (`ZuiFill.cs:260`) structurally unnecessary: pixels are copied at compile time or the fill does not bind, so there is no per-pixel path that can throw.

**Requires:** nothing. **Optionally reads:** nothing. **Emits height:** iff `heightDelta ≠ 0`. **Composite default:** `Over`.

**Identity / no-op:** a 1×1 opaque white texture, `Fitted`, tint white, veil 1 → bit-identical to Solid white. FT-3.

**FC-6.4e — an animated texture is NOT a fifth fill.** B4 lists "*Animated texture* (the same plus time)" (`SHAPER_THE_DESIGN.md:112`), and the task's first four do not include it. Because every dial here is a `ZUIValue` sampled per frame at compile time (FC-5.3), scrolling, rotating and zooming a texture over time is already available in T-0106. What is *not* here is a frame-sequence texture (a different image per frame), which needs a sequence asset and belongs with T-0111's surface work. Say this in the handover so it does not look like an oversight.

## FC-6.5 One fallback rule for all four

> **FC-6.5 — a half-configured fill NEVER renders empty and NEVER renders silently. Every fallback (null gradient, null texture, unreadable texture, degenerate anchor, unavailable quantity) paints something visible and records a diagnostic with a reason.**

The first half is `ZuiFill.cs:163-166` by value. The second half is BC-3.1 (`BUFFER_CONTRACT.md:172`). Together they are the whole of what "greyed out with the reason shown, never silently inert" means in a stage that has no UI.

---

# Part F7 — conformance tests

Mirrors BC-3.8's shape (`BUFFER_CONTRACT.md:262`) and T-0105's V1–V9 (`SHAPE-ENGINE-SPEC.md:246-268`). Every row is a measurable assertion with an expected value, not an intention. The audit is plain static methods returning a report string, **no `[MenuItem]`**, invoked through the Unity CLI against the Shaper editor on port 7801 — the same arrangement `SHAPE-ENGINE-SPEC.md:248` sets for the shape audit, for the same reason.

| ID | What it asserts | Expected |
|---|---|---|
| **FT-1** | **Solid identity.** Solid white, veil 1, height 0, `Over`, on a disc. Compare the albedo array against a constant array and the veil array against ones. | Bitwise equal, every sample in the tile. |
| **FT-2** | **sRGB round-trip (FC-2.3).** For all 256 byte values per channel: `byte → float → linear → float → byte`. | Exactly the input byte, all 256, all three channels. **A failure here reverses FC-2.3 rather than being patched.** |
| **FT-3** | **Cross-kind identity.** (a) A single-colour Gradient in each of the four modes; (b) a 1×1 white texture, `Fitted`, tint white; (c) Ramp with a single-colour ramp. Each compared bitwise against Solid of that colour. | Bit-identical in all six cases. Exercises the LUT path, the bulk-data path and the trivial path against one another. |
| **FT-4** | **Ramp-by-coverage passthrough.** `quantity = coverage`, black→white ramp, window 0..1. Compare albedo luminance against the coverage sheet. | Equal to within the 256-entry LUT quantisation (≤ 1/255 per channel) — and the deviation is reported, not merely passed, so a regression in the LUT shows as a number. |
| **FT-5** | **The veil cannot create coverage.** On a disc, sweep veil over {0, 0.5, 1, and an attempt to author 2}. At every sample where `coverage == 0`, assert `coverageEff == 0`. At a known half-covered edge sample (`coverage == 0.5`), assert `coverageEff ≤ 0.5` for every veil setting including the attempted 2. | Zero outside, ≤ coverage at the soft edge. **The attempted veil of 2 is the load-bearing half** (FC-2.4) — without the clamp it produces 1.0 at the edge sample and this test fails, which is exactly the silent edge-thickening the clamp exists to prevent. |
| **FT-6** | **Veil zero kills, height and all.** veil = 0 with a non-zero `heightDelta`. | `coverageEff == 0` and the height-delta contribution `== 0` at every sample (FC-2.5's `coverageEff` weighting). |
| **FT-7** | **Nearest-ancestor resolution on FC-3.8's worked tree.** Build the exact five-member tree of §F3.8 and assert the owner at one probe sample per row of that table (8 probes). | The owner named in FC-3.8's table, for all 8. Assert `Head ∩ ¬Belt` paints **nothing** — the case that proves the `min` against the ancestor's coverage is applied. |
| **FT-7b** | **Fold order governs paint order.** In the same tree, swap members [1] and [2] and assert the composited result at a sample both cover **differs**. | Different bytes. A pass would mean paint order was not following fold order (FC-3.4). |
| **FT-8** | **The gate produces a reason, not silence.** Ramp-by-`heat` on a node that publishes only coverage and edgeDistance. | `hasUnavailableFill == true`; `unavailableFillCount == 1`; `unavailableFillReason` is a non-empty string containing the fill kind, the node name and the word `heat`; and the region is painted by the ancestor fill (**not** left transparent). |
| **FT-8b** | **The count is not just the first.** Three unavailable fills in one tree. | `unavailableFillCount == 3`, `unavailableFillNode` names the first. This is the `leadingNonAddCount` correction (`ShaperProgram.cs:90-95`) applied here, and it exists because that exact mistake was already made once. |
| **FT-8c** | **A Subtract member's fill slot is refused (FC-3.3).** | Refused with a reason mentioning subtraction, not silently painting, and not silently doing nothing. |
| **FT-8d** | **`surfaceDirection` is refused on TYPE, with a different message (FC-6.3c).** | The reason string differs from FT-8's availability message. |
| **FT-9** | **Zero allocation in the tile loop.** `GC.GetTotalAllocatedBytes(precise: true)` (or `GC.GetTotalMemory` bracketing, if the former is unavailable in this Unity version) around a 10 000-sample tile fill for each of the four kinds, after one warm-up call. | **Exactly 0 bytes** for all four. |
| **FT-10** | **Determinism.** Compile and fill the same document twice in one session, and once more after a domain reload, for each kind including Texture and a `MinMax` dial. | Bit-identical bytes all three times. The `MinMax` case is the one that catches a stray `System.Random` (BC-1.3, `PyreRenderer.cs:5034`). |
| **FT-11** | **Tile independence (BC-1.6).** Fill the whole grid as one tile, then as a 7×5 prime decomposition, for each of the four kinds, in **both** `Stamped` and `Fixed` space. | Bit-identical. `Fixed` is the case that fails if the absolute canvas position is computed from a tile-local origin (FC-1.6). |
| **FT-12** | **Declared set == read set (BC-3.8's shape).** For each of the four kinds under a spread of settings, instrument the input-sheet reads and compare against `RequiredSet(settings) ∪ OptionalSet(settings)`. | Equal. A fill that reads a sheet it did not declare fails the build; so does one that declares a sheet it never reads. |
| **FT-13** | **Every output sample is written.** For each kind, pre-fill the albedo, veil and height arrays with a sentinel NaN and assert no sentinel survives inside the tile. | No sentinel. Mirrors BC-3.8's "no unwritten samples". |
| **FT-14** | **Ranges hold.** Over a dense parameter sweep: `veil ∈ [0,1]`; albedo finite and `≥ 0` in every channel; height delta finite. | No NaN, no infinity, no negative veil, no veil above 1. |
| **FT-15** | **Degenerate inputs do not NaN.** Zero-extent anchor box (FC-1.5b); `inputHigh == inputLow` (FC-6.3a); `size == 0`; null gradient; null texture; unreadable texture; a fill on an empty bag. | Every case produces finite output and, where FC-6.5 requires one, a diagnostic. |
| **FT-16** | **A fill never mutates an input sheet (BC-3.7c/e).** Hash the coverage and distance arrays before and after every fill call. | Unchanged. |
| **FT-17** | **`Add` does not raise alpha (FC-2.6b).** An additive fill over a transparent destination. | Destination alpha unchanged; destination RGB increased. |
| **FT-18** | **`Over` versus `Add` do not differ in height (FC-2.6c).** Same fill, same `heightDelta`, two composite modes. | Height output bit-identical. |
| **FT-19** | **The edge-distance polarity is the shipped one (FC-1.3).** Gradient in `ByEdgeDistance` on a disc of known radius, `depthPixels = 8`. | `t == 0` at the boundary, `t == 1` at 8 px inside, `t == 0` outside. A reversed ramp means the Pyre polarity trap (`BUFFER_CONTRACT.md:190`) was inherited. |
| **FT-20** | **The visual check.** A contact sheet PNG covering all four kinds, Gradient's four modes, both spaces, both fits, both mappings, both composites, and the FC-3.8 tree. | Rendered, kept, and **looked at by a human**. A passing table is not a picture; T-0105 shipped a contact sheet (`T-0105\shaper-contact-sheet.png`) for exactly this reason. |

**Report every result as measured-number versus expected-number.** Do not report a rule as verified on the strength of the code compiling — `SHAPE-ENGINE-SPEC.md:268`, and it applies here unchanged.

---

# Part F8 — what is deliberately NOT in this task

Recorded so they are answered rather than discovered, which is the same reason design C9 and BC Part 5 exist.

## F8.1 Lighting — T-0108

**Not here:** no shine, no specular, no sheen, no rim, no Fresnel, no ambient, no light direction, no shadow, no per-layer light-response block (`SHAPER_THE_DESIGN.md:192`), and **no fill that reads `surfaceDirection` to shade with**. B4's load-bearing simplification is exactly this: "A fill emits **albedo** — flat, unlit colour — and optionally a **height delta**. The document's light rig turns albedo plus height into the final pixel. Shine belongs to the lights, not to the paint" (`SHAPER_THE_DESIGN.md:116`).

**The hook left open:** the height delta (FC-2.5). That is the entire interface — T-0108 reads albedo and the summed height, and needs no fill-stage change. FC-6.3c's type refusal of `surfaceDirection` in the ramp picker keeps the boundary clean from the other side.

**Explicitly refused:** a "reflective" or "metallic" dial on any fill. B4: "**Reflective, shiny, bevelled and lit are not fills.** They are what happens *after* a fill" (`SHAPER_THE_DESIGN.md:114`). Kiln reached the same rule independently — "a fill never authors its own bevel" (`SHAPER_THE_DESIGN.md:140`).

## F8.2 Extrusion, bevel and Z — T-0109

**Not here:** no extrusion thickness, no bevel profile, no Z offset, no side walls, no height *profile*. A fill emits a scalar height *delta*; the shape's own height is worked out by T-0109 "by a fixed recipe from its own published edge distance" (`SHAPER_THE_DESIGN.md:323`).

**The open question T-0109 must answer before this contract is complete:** the side-wall parameterisation. `BUFFER_CONTRACT.md:304` records it — a side wall has no "how far in from the outline" value, "yet `edgeDistance` is what the bevel band, the border region and the indexed-strip fill are all defined against". **Gradient's `ByEdgeDistance` mode (FC-6.2) is directly affected**: once walls are visible, that mode has no defined `t` on a wall until one of the three candidate rules is picked. Flag it in the handover as a fill-stage dependency on T-0109, not merely an extrusion detail.

**Also T-0109's:** `depth` becoming non-zero (BC-3.5), which un-greys one Ramp picker entry with no fill-stage change.

## F8.3 The indexed strip — T-0110

**Not here:** no per-slot palette, no arc-length or projection parameterisation of the shape, no edge-reach control, no per-slot height.

**The hook left open, and it is the whole point:** FC-2.5's height delta. B6 calls the indexed strip "the existence proof for B4's central claim" and says restoring per-slot height "should be an early task, not a late one, because it validates the fill contract cheaply and visibly" (`SHAPER_THE_DESIGN.md:154`). Because this contract already gives every fill an albedo output *and* a height-delta output, T-0110 is **one new fill kind and zero contract changes** — its palette entries carry a colour and a height, and both outputs already exist. That is the single best argument that FC-2.5 belongs in T-0106 rather than being deferred until something consumes it.

## F8.4 Borders — T-0107

**Not here:** no strip derivation, no inward/outward/straddling width, no "an outward strip joins the node's published coverage" (`SHAPER_THE_DESIGN.md:333`).

**The hook left open, and it costs nothing now:** C5 says a border strip "is a region; a fill paints it", so T-0107 will hand this contract a strip and ask for paint. Two clauses make that work without a change: FC-5.1's tile call is agnostic about what the region *means*, and — the one that has to be got right today —

> **FC-8.4 — the anchor box (FC-1.5) is always the NODE's box, never the painted region's box.**

Otherwise a gradient painting a border would normalise across the *ring* rather than across the shape, so a linear ramp on an outline would run across the outline's own thickness instead of across the shape it traces. That is almost never what anyone wants, and it is nearly impossible to diagnose after the fact.

**Also relevant:** FC-2.4b forbids a fill writing back into coverage, which is what keeps the border stage's read of the shape's published coverage honest.

## F8.5 Tapestry and the procedural family — T-0111

**Not here:** no plasma, no painted steel, no machined alloy, no height-field presets, no noise fill, and no port of Tapestry's blend modes.

**The hook left open:** FC-5.5's bulk-data slot. A Tapestry surface is cached in memory keyed by its settings (`SHAPER_THE_DESIGN.md:136`) and read per sample as a flat array — which is the same slot Texture already uses, so T-0111 adds a fill kind and no contract change. B5's three prerequisites — a time input, a palette-quantise step, and a coordinate-space choice (`SHAPER_THE_DESIGN.md:135`) — are all already satisfied by this contract: `ZUIValue` dials give the time input (FC-5.3), point LUT lookup gives the quantise (FC-5.4), and `FillSpace` gives the coordinate space (FC-1.6).

**One thing T-0111 must NOT assume:** that Tapestry's plasma is a fill. B5 measured it and it is not — "given a flat field renders **nothing at all**: its transparency *is* its silhouette" (`SHAPER_THE_DESIGN.md:128`), so it is a shape-coupled composite generator. Do not let it into the fill picker.

## F8.6 No UI, no menu item, no drawer

Same as `SHAPE-ENGINE-SPEC.md:16`. The availability gate is a diagnostic on the compiled program (FC-4.4), not a greyed control — the greyed control is whoever builds the UI reading that diagnostic. The project rule against adding unrequested menu items applies with full force.

## F8.7 Deferred additions, named so they read as decisions rather than gaps

- **An authored anchor rect override** (rejected in FC-1.5). Cheap to add later precisely because FC-1.5 routes the anchor through one function.
- **Route (b) stack tapping** for the resolver (FC-3.9), if measurement says the per-node programs cost too much.
- **A union-instead-of-intersection published set** (FC-4.2), which requires first deciding what a fill reads in a non-contributing member's region.
- **LUT interpolation** (FC-5.4) and **a texture filter dial** (FC-6.4b), both one line, both on evidence only.
- **A frame-sequence animated texture** (FC-6.4e).

---

# Part F9 — what this contract changes about the documents it stands on

Not a plan — a list of what an implementer would otherwise inherit wrong.

| Document | Claim | Status |
|---|---|---|
| `CONTRACT-EXTRACT.md:15` | The primitive→coverage kernel is an unclosed Wave-1 gap for the implementer to propose or ask about | **Stale.** Closed by `SHAPE-ENGINE-SPEC.md:30-40`, shipped at `ShaperField.cs:44-61`. |
| `SHAPE-ENGINE-SPEC.md:39` vs `BUFFER_CONTRACT.md:188` | Coverage is clamped to [0,1] / coverage is unbounded above and the publisher never clamps | **Live contradiction.** Both defensible; resolved for the fill stage by FC-1.6 (treat as unbounded, clamp at use, per BC-3.3). Neither document is edited by this one; the resolution is scoped to consumers. |
| `SHAPER_THE_DESIGN.md:120` | "Every call site in Pyre passes zero for its spatial point" | **Already refuted** by `BUFFER_CONTRACT.md:37`; the surviving half — that `ZuiFill.Evaluate`'s entire input is `(life, u, v)`, `ZuiFill.cs:167` — is verified here and is what §F0 relies on. Do not re-quote the refuted version. |
| `SHAPER_THE_DESIGN.md:96-104` (B4's table) | Bundles "How old is this pixel, and which instance am I" into one row | **Split by BC-3.2** (`BUFFER_CONTRACT.md:180`): `age` is a sheet, `instanceIndex` is a parameter-block scalar. FC-1.1 follows BC-3.2. |
| `SHAPER_THE_DESIGN.md:329` (C4) | "It reads only the numbers in B4's table" | **True in principle, misleading in practice on day one:** seven of the nine have no publisher in the shipped engine (Part 0, finding 1), so the fill stage can read two. Not a correction to C4 — a correction to how C4 will be read by someone who has not opened `ShaperEvaluator.cs`. |
| `T-0106` task text | "a child owning its own fill wins inside its own coverage" | **Under-specified for two of the three combine modes.** FC-3.1 gives the uniform rule and FC-3.3 rules that a Subtract member may not own a fill at all. |
| `T-0106` task text | "a veil that multiplies the shape's coverage but can never replace the shape's edge" | **The multiplication alone does not achieve this.** It stops a fill creating coverage from nothing; it does not stop a veil above 1 hardening a soft edge. The [0,1] clamp is the load-bearing half — FC-2.4, tested by FT-5. |

---

# Part F10 — what I could not check

Stated because a contract that claims verification is worth exactly as much as the verification.

- **The sRGB round-trip of FC-2.3 is asserted, not measured.** It is FT-2 and a failure reverses the decision rather than patching it.
- **Nothing in this document has been run.** No fill exists; every expected value in §F7 is derived from a formula or from the shipped shape stage's behaviour, not observed. In particular FT-4's "≤ 1/255" and FT-5's edge-sample arithmetic are predictions.
- **I did not open `T-0105\VERIFICATION.md` or `AUDIT.txt`** (52 KB and 27 KB). I read the spec and the shipped code and treated the code as authoritative, so anything those two record about *measured* bound values or audit results is not reflected here. Nothing in this contract depends on a bound.
- **I did not open `T-0098\P1-digest.md`.** Its eighteen catalogued bugs may contain a fill-relevant one this document has not accounted for.
- **I did not open `REF-MATHS.md`.** The reference app's own fill maths is therefore not cross-checked; the gradient expressions in FC-6.2 are taken from `ZuiFill.cs` (Unity, verified) rather than from the reference app.
- **`ZuiGradient.Evaluate(float t, float phase, float life)`** is cited from its signature at `ZuiGradient.cs:85`; I did not read its body, so the claim that its transforms can be baked into a LUT at a fixed `phase`/`life` (FC-5.4) is an inference from `ZuiFill.cs:235`'s usage, not a reading. **If any transform on `ZuiGradient` is position-dependent rather than parameter-dependent, FC-5.4's bake is invalid** and must become a per-frame bake at each animated `life` instead — which is still a compile-time bake and still satisfies BC-1.2, but it changes the cache key. Check this first in T5.
- **The Burst claim is architectural, not demonstrated.** No Shaper code is `[BurstCompile]` today — `BUFFER_CONTRACT.md:84` records that the project's one real Burst kernel is `ArcRaster.cs:37` and that it is not wired in. FC-5.2 keeps the door open; it does not prove anyone will walk through it.
