# N — The entanglement, and how to split it

Designer N, T-0098. Research and design only: no source changed, no Unity opened, no git mutation. Every load-bearing claim carries a `file:line` from the working tree on `feat/lathe`, 2026-08-30. Code citations are kept in indented footnote lines so the prose reads clean.

Owner's rulings honoured throughout: **drawing outside the shape and outside the frame is allowed**; **saved-asset usage counts are inadmissible** and are used nowhere below.

Path abbreviations: `RT/` = `Assets\Packages\Laubrary\Runtime\`, `ED/` = `Assets\Packages\Laubrary\Editor\`.

---

## Bottom line

- **The eleven generators are not entangled. Not one of them.** I read the pixel-emitting loop of six scene generators and both fire sims line by line. In every single case the opacity decision and the colour decision are **two separate readings of the same thermometer** — neither ever consults the other's answer. The coupling is *structural* (two rules written in one loop) or *authoring* (one gradient object carries a colour column and an opacity column). **Mathematically fused: zero of eleven.** Report 2 §7.1's headline — "the fill decides the shape" — is **flatly false for four of the nine scene generators** (Orb, Torch, Arc Burst, Plasma Bloom), where swapping the palette today leaves the silhouette bit-identical, and is a *product of two independent factors* in the other seven.
- **The seam is not just conceptual — it is already an implemented, named API in Pyre.** Six of the nine scene generators already publish their internal fields through `IPlusFieldPublisher.PublishFields(sink)` under exactly the channel names a shape stage would want: `H` (energy), `T` (soot/second energy), `ramp_t` (normalised transfer), `alpha`/`alpha_f` (the shape's own coverage), `rim_mix` (edge-distance mix), `C` (cooled heat).<sup>1</sup> It exists for the parity-dump harness. **Exposing it is promotion, not construction.** No prior report noticed this.
- **The out-of-frame ruling dissolves most of §7.3, and dissolves it more cheaply than §7.3 proposed.** The fix is not a new "emission" channel — a channel that means "paint here although there is no coverage" *is* coverage under another name. The fix is one sentence: **coverage is a soft field with unbounded extent, not a binary stencil.** Two things still genuinely break, and neither is what §7.3 named: additive-at-the-pixel blending, and the fact that the canvas clip is still real code the ruling does not itself repeal.
- **Recommended design: a hybrid of (c) and (d), which I call *shape keeps the edge, fill gets a veil*.** The shape publishes named channels **and** its own coverage (its edge rule, which is its identity); the fill is a two-part object — a transfer that names which channel it reads, plus a palette returning RGBA — and the fill's alpha **multiplies** coverage rather than replacing it. This reproduces all eleven of today's looks exactly, keeps a shape able to answer "what is my silhouette" with no fill present (which groups, borders and previews all require), **and** preserves the ability of a fill to thin a shape — which report 2's option (a) explicitly gave up, and which five generators already do today.
- **The owner's fireball-wearing-churning-plasma works, with one half to warn about.** The paint churns on its own clock: fully supported, and cheaper than expected. The silhouette keeps boiling on the shape's clock: fully supported. What *half*-works is that a plasma fill which reads only its own noise will read as a flat decal — a good plasma fill must read the shape's `energy` channel and use its noise as a modulation. That is a fill-authoring rule to state up front, not a design flaw.
- **Nothing in Pyre is irreducible. Retract the claim.** Report 2's one nominee, the imported-sprite generator, splits in one line (alpha vs RGB). Its real property is different and worth stating properly: **it is the only generator whose fill half is authored data rather than a rule**, so replacing it discards the artwork. That is "its default fill is a picture", not "it cannot be split".

<sup>1</sup> `RT/Pyre/PyreForm.cs:156-159` (the interface); implemented at `Forms/Kiln/TorchForm.cs:117`, `OrbForm.cs:465`, `ArcBurstForm.cs:422`, `PlasmaBloomForm.cs:397`, `ForkBlastForm.cs:137`, `Jet/JetFormBase.cs:73`. Consumed only by `ED/Pyre/Parity/PyreParityDump.cs:16` via `PyreRenderer.cs:352`. Inferno is the one scene generator that does **not** implement it.

---

## 1. What is actually going on, in plain terms — and then in code

### 1.1 The picture

A Pyre scene generator does not draw a picture and then colour it in. It makes **weather**.

Before a single colour has been chosen, the generator has built an invisible cloud of numbers laid over the canvas. At every pixel it knows things like *how much stuff is here* and *how hot it is*. That cloud is real. It lives in memory as its own float array, and it is complete before any paint exists.

Then, at the very end, in one loop, the generator does two things to that cloud — and this is the only place the alleged trouble lives:

1. **It decides how solid each pixel is.** Not from a colour — from the numbers. "Below this much heat: nothing. Above this much: fully solid. Fade between." Call this the **edge rule**.
2. **It decides what colour each pixel is.** It takes a heat number, turns it into a position from 0 to 1 along a ramp, and looks up the colour there.

Two decisions, one loop, one pile of numbers. **But they never read each other.** The edge rule never asks what colour came out. The colour lookup never asks how solid the pixel is. They are two people reading the same thermometer and writing down different things.

**That is the entire "entanglement".** It is not that the shape is made of the colour. It is that the two rules were written on the same sheet of paper.

There is one real complication, and it affects five of the eleven. In those five, the ramp you author has a **transparency slider on each colour stop**, and the generator multiplies that transparency into the edge rule's answer. So yes — for those five, changing the ramp genuinely can change the silhouette. But look at the *shape* of that: it is a **multiplication of two independent factors**, not a blend of two things that have become one thing. You can pull it apart with scissors and put it back with no loss whatsoever.

### 1.2 Where the decision is made, in code, for eight generators

The question the brief asks — *where is "this pixel is solid / transparent" made, and from what* — has a strikingly uniform answer. Every one of the eleven fits this template:

```
t      = tone(field)                    // shape-side: normalise raw energy to 0..1
rgb    = Palette(t)                     // fill-side: a lookup
alpha  = Edge(field)  ×  Ceiling(t)     // Edge is shape-side; Ceiling is the palette's alpha column, or 1
```

Only `Ceiling` crosses the line. Below, per generator, `Edge` and `Ceiling` named exactly.

| # | Generator | Where the solid/transparent decision is made | What it is made **from** | Does alpha read the palette? |
|---|---|---|---|---|
| 1 | **Orb** | `OrbStyle.Alpha(tone)`, called at `Rasterise`; then a `floor` cut and a wrapped 4-neighbour despeckle | `a = clamp((tone−a0)/(a1−a0))^acurve · amax`, `tone = 1−exp(−E·gain)` — **E and five form dials only** | **NO.** The LUT is read on the next line, from the *same* `tone`, and never touched by the alpha maths.<sup>2</sup> |
| 2 | **Torch** | the shade loop in `TorchForm.Render` | `a = smoothstep(aLo, aHi, _Fp[k])` — **the raw heat plane** | **NO — and not even the same number.** Colour is `_lut.Sample32(_C[k]/top)` from `_C`, a *different plane* (the heat cooled with height). Two planes, two rules, zero contact.<sup>3</sup> |
| 3 | **Arc Burst** | the colorize loop in `ArcBurstForm.Render` | `a = clamp01(A[i])`, where `A` is a **separately deposited plane** (`a = clip(e/aref)^agamma·opa`, max-composited, separately bloomed); then `if (e < Floor) a = 0`, `Floor` a form constant `0.045f` | **NO.** `_lut.Sample32(e)` reads the energy plane; the palette is never consulted for alpha.<sup>4</sup> |
| 4 | **Plasma Bloom** | `PyrePlasmaBloom.Shade` | `a = pow(Smooth(aLo, aHi, e), aGamma)` — **E and three form dials only** | **NO.** The two LUTs `A`/`B` are read four lines later and crossfaded by `rim_mix`; neither contributes to `a`.<sup>5</sup> |
| 5-7 | **Jet / Radial Jet / Explosive Jet** | `JetShade.Shade` | `e = smoothstep((H−lo)/soft)`; `ac = hot.a[idx]` (soot-crossfaded with `soot.a[idx]`, then `ac^opaq`); `a = e · ac` | **YES** — but as a **clean product**. `e` is pure field; `ac` is the ramp's alpha column at index `idx = f(H)`. Two independent lookups into one table.<sup>6</sup> |
| 8 | **Fork Blast** | the shading pass in `PyreForkBlast.Render` | `edge = smoothstep((hv−lo)/soft)`; `aceil = baseColor.a` (the **layer fill's** alpha), `^opaq`; `alpha = edge · aceil` | **YES**, same product form. The header comment says so outright: *"The fill's OWN alpha … doubles as the per-stop alpha ceiling."*<sup>7</sup> |
| 9 | **Inferno** | the shading pass in `PyreInferno.Render` | `lutA[li]` = the layer fill's alpha baked into a 128-entry LUT at `1−heat`; `fireMix`, `materialAlpha`, then `alpha0 = d^0.65 · (0.025 + 0.995·materialAlpha)` | **YES**, and this is the messiest of the eleven — see §1.4. |
| 10-11 | **Fire / Fireball** | `FireSim.Render` | `v = heat + fuel·0.35`; `if (v ≤ threshold) continue`; `t = ((v−threshold)/(1−threshold))^contrast`; `a = c.a · layerAlpha · clamp01(t·2.2)` | **YES**, product form again: `c.a` is the ramp's alpha at `t`.<sup>8</sup> |

<sup>2</sup> `RT/Pyre/Forms/Kiln/PyreOrb.cs:113-122` (`Tone`/`Alpha`), used at `:623-631`; floor cut `:630`; despeckle `:637-651`.
<sup>3</sup> `RT/Pyre/Forms/Kiln/TorchForm.cs:205-215`; the two planes are allocated at `:143`; the file header states the split at `PyreTorch.cs:7-8`.
<sup>4</sup> `RT/Pyre/Forms/Kiln/ArcBurstForm.cs:496-503`; `Floor` at `:420`; the A-plane's own normalisation is documented at `PyreArcBurst.cs:6-9`.
<sup>5</sup> `RT/Pyre/Forms/Kiln/PyrePlasmaBloom.cs:586-593`.
<sup>6</sup> `RT/Pyre/Forms/Kiln/Jet/PyreJetEngine.cs:736-757` — `ac` at `:740`, soot crossfade `:743-751`, `opaq` `:752`, `e` `:753-754`, product `:755`.
<sup>7</sup> `RT/Pyre/Forms/Kiln/PyreForkBlast.cs:385-388`; header comment `:21-23`; dial tooltip `ForkBlastForm.cs:74`.
<sup>8</sup> `RT/SpriteFx/FireSim.cs:295-306` — threshold `:300`, `t` `:301-302`, alpha `:304`.

### 1.3 The distinction the brief asked for, stated exactly

Three categories, and the whole answer turns on which is which:

**(A) Structural only — two numbers computed in one place, no contact whatsoever.** Orb, Torch, Arc Burst, Plasma Bloom. In these four you could, *today*, replace the entire palette with a random one and the silhouette would be **bit-identical**. There is nothing to split; there is only something to *move*.

> **Report 2 §7.1 is wrong about these four.** It says "those generators … compute a smooth energy field and then decide *where that field becomes opaque* — a window, a curve, a floor, a speckle cleanup … It is the shape, and it is currently part of the colouring." The first half is right. The last clause is not: the window, the curve, the floor and the speckle cleanup are all functions of the *field*, live in the *form's* dial block, and never read the palette. They are part of the *shading function*, which is a different claim entirely — and one that is true of any code that emits a pixel.

**(B) Authoring-fused — one authored object carries both columns; the arithmetic is a product.** Jet ×3, Fork Blast, Inferno, Fire, Fireball. `alpha = Edge(field) × PaletteAlpha(t(field))`. Both factors are computed independently and multiplied at the last moment. Splitting is exact scissors work: the palette's alpha column becomes an authored curve that can live on either side of the seam, and the arithmetic is untouched.

**(C) Mathematically fused — the same number is used for both and cannot be separated.** **None. Zero of eleven.** I looked for one specifically and did not find it.

The closest thing to a genuine fusion in the whole library is Inferno's `fireMix`, and it is worth stating precisely because it is the one place someone will reasonably object.

### 1.4 Inferno, the hardest case, examined honestly

Inferno bakes the layer fill into a 128-entry LUT sampled at `1−heat`, so `lutA[]` holds the fill's alpha column. Then:

- `fireMix = smoothstep(0.085, 0.72, hv) · clamp(0.30 + d·1.10) · lerp(0.72, 1.08, cavity) · lerp(0.45, 1.08, fire) · lutA[li]`
- `colourMix = max(smoothstep(0.04, 0.50, hv) · lutA[li], fireMix)` — the crossfade weight between the hardcoded smoke grey and the fill colour
- `materialAlpha = max(fireMix · 0.95, visibleSmoke)`, `alpha0 = d^0.65 · (0.025 + 0.995 · materialAlpha)`

So one number, `fireMix`, does two jobs: it is the **colour** crossfade weight *and* a term in the **alpha**.<sup>9</sup>

Is that fusion? **No — it is double duty, which is a different thing.** `fireMix` is computed entirely from shape-side quantities (`hv` heat, `d` density, `cavity` from the density gradient, plus the `fire` dial) times one fill-side factor (`lutA[li]`). Split `lutA` out and `fireMix` becomes a pure shape channel that the shape can publish by name. The fill then receives it and uses it for the crossfade; the shape keeps using it for coverage. **One number, published once, read twice.** That is exactly what a named channel export is for.

Inferno's genuinely awkward properties are elsewhere and should be named as such:
- The **smoke grey is hardcoded** — `smokeR/G/B` derived from a `darkness` dial, never from the fill. Only the *fire* half goes through the fill at all. So Inferno is not "a shape whose fill decides its shape"; it is "a shape with **one and a half** fills, one of which is not authorable." Splitting it makes the smoke paintable for the first time — a real gain, not a cost.
- Two decorative passes composite **after** shading and never touch the field: the ignition flash and the contained embers, straight-alpha source-over at positions the density field does not cover.<sup>10</sup> Under the out-of-frame ruling those are now legal; they still have to be either promoted into the field as coverage+energy stamps, or declared shape-owned decoration (see §6).

<sup>9</sup> `RT/Pyre/Forms/Kiln/PyreInferno.cs:540-546` (the LUT bake through `fill.Evaluate`), `:776-781` (`fireMix`, `colourMix`), `:792-800` (`materialAlpha`, `alpha0`), `:805-811` (the body dial).
<sup>10</sup> `PyreInferno.cs:877` (flash), `:882-910` (embers), compositor `CompositeGlow` `:505`. Smoke grey `:781-787`.

---

## 2. Does the out-of-frame ruling change the picture?

**Yes — it dissolves most of §7.3, and it dissolves it more cheaply than §7.3 proposed. But two real things survive, and neither is the thing §7.3 named.**

### 2.1 What the ruling dissolves

Report 2 §7.3's argument was: *"If coverage is a stencil and the fill paints inside it, all of that is deleted"* — the lit solids' halo, Inferno's flash and embers, and every generator that blurs its field before deciding opacity.

The premise is the error. **Nothing in the code forces coverage to be a stencil.** Coverage in every one of the eleven is already a smooth float, and in most of them it is smooth precisely *because* the field was blurred first. Orb's field is softened before `Tone`; Arc Burst blooms both its energy and its alpha plane with a 3-pass box blur before colorize; Plasma Bloom's is a continuous smoothstep. The "silhouette" these generators have is *already* a soft, wide, low-valued fringe extending well past anything you would draw with a pencil.

So the answer to the brief's question is **almost yes, but with the framing corrected**: the fix is *not* "coverage must be allowed to exceed 1.0". Coverage is an occupancy fraction; above 1 it is meaningless, and letting it exceed 1 would break every combine rule a group needs. The fix is one sentence:

> **Coverage is a soft field of unbounded spatial extent, not a binary stencil.** It may be 0.02 two hundred pixels from anything you would call the body, and it may be non-zero outside the canvas.

That single sentence covers every one of §7.3's examples:
- **The lit solids' outside halo** is simply low coverage far from the body.<sup>11</sup> No new concept.
- **Blur-before-opacity** is already this and needs nothing.
- **Inferno's flash and embers** are additional deposits, not a different kind of paint — promote them to field stamps and they are inside the model.

### 2.2 Report 2 §7.3's proposed answer is unnecessary, and should be dropped

§7.3 proposed: *"substance must carry **emission** as well as coverage, and a fill must be allowed to paint where emission exists and coverage does not."*

**A channel that means "paint here although there is no coverage" is coverage under a different name.** Adding it buys nothing and costs a second combine rule (what happens when two group members' emissions overlap?), a second thing every border plugin must decide whether to trace, and a permanent "which one do I use?" question in the fill UI. Recommend dropping it. Say plainly in the synthesis that this part of §7.3 is superseded.

### 2.3 What still breaks — two things, honestly

**(a) Additive light at the pixel is not expressible by coverage alone.** A halo is often *added* light, not a translucent skin. Two overlapping 50 %-coverage haloes composited *over* give 75 %; added they give 100 %. Where does this bite? Not in the field — Orb, Arc Burst and Plasma Bloom all accumulate additively or by maximum *inside* the field, upstream of coverage, which is fine and unaffected. It bites at the **layer composite**, and the honest fix is small: **a fill declares its blend mode (over / add)**. Note Inferno is already safe here — its glow composite is explicitly straight-alpha source-over, not additive.<sup>12</sup>

**(b) The canvas clip is real code, and the ruling does not repeal it.** Every form writes into a `W×H` buffer. Permitting a shape's field to extend beyond the frame requires either a padded working buffer or accepting the clip. This is required work, not a free consequence of the ruling — and it is a **precondition for the owner's own red-out-of-frame preview idea**, because the renderer cannot paint out-of-frame pixels red if it never computed them.

<sup>11</sup> `RT/Pyre/PyreRenderer.cs:4396-4408` (facet solids' halo), `:4547-4571` (built-in Orb's halo + inner glow) — both emit colour at pixels where coverage is zero, per F PART 1 rows 8-12.
<sup>12</sup> `PyreInferno.cs:505` — `CompositeGlow` is straight-alpha source-over.

---

## 3. The design — *shape keeps the edge, fill gets a veil*

### 3.1 The recommendation in one line

> **The shape hands over the weather and keeps the edge rule; the fill is a paint recipe with an optional veil.**

This is a hybrid of the brief's candidates **(c)** (a small named channel vector) and **(d)** (a fill is a transfer plus a palette), with one rule added that neither (a) nor (b) has: **the fill's alpha multiplies the shape's coverage; it never replaces it.**

### 3.2 The plain-language explanation

*(This is the section written for the tool's owner, not for an engineer. The synthesis writer should lift it whole.)*

**Three parts. That is the whole design.**

---

**Part one — the shape publishes its weather.**

When a generator finishes, it does not hand over a cut-out. It hands over a small stack of transparent sheets laid over the canvas. Each sheet holds **one number per pixel**, and each sheet has a **name everybody understands**:

- *how much stuff is here* — density
- *how hot it is* — energy
- *how sooty it is* — a second energy, where the generator has one
- *how far this pixel is from the edge*
- *how old this pixel's stuff is* — for swarms, the age of whichever particle owns it
- *which way the surface faces* — for the generators that compute a surface

Not every generator has every sheet. Each one **declares what it has**, and a fill that wants a sheet the shape hasn't got is simply greyed out in the picker with the reason shown — not hidden, not silently broken.

**This part is already built.** Six of the nine big generators already hand out exactly these sheets, under exactly these names — `H` for energy, `T` for soot, `rim_mix` for edge-distance, `alpha` for coverage — because the fidelity-testing harness needed to compare Pyre's numbers against the originals'. The machinery works. It is just that nobody except the test harness is allowed to look at it.

---

**Part two — the shape keeps the edge rule.**

This is the part report 2 got right, and the part it is essential not to give away.

The **edge rule** is the sentence "below this much heat, nothing; above this much, solid; fade between." It sounds like a technicality. It is not — it is what makes a jet read as a **tongue** rather than a smear, and what makes a lightning bolt read as a **bolt** rather than a bright fog. It is tuned against numbers only that generator knows the range of. It is as much that generator's identity as its geometry is.

So the shape keeps it, and the shape can therefore always answer, **all by itself, with no paint anywhere in sight**: *"here is how solid I am at every pixel."* Call that its **coverage**.

Everything downstream needs that answer to exist before paint does:
- a **border** needs an edge to trace
- a **group** needs to know what each member contributes to the union
- a **preview** needs to show you something before you have chosen paint
- a **swarm** needs a rule for how two overlapping copies combine

And coverage is deliberately **soft and unbounded**. It is allowed to be a faint 2 % a long way from the body — that is a halo — and it is allowed to be non-zero off the edge of the frame. It is a *fog*, not a *stencil*.

---

**Part three — the fill is a paint recipe, plus an optional veil.**

A fill says two things:

1. **"Which sheet do I read, and how do I turn its numbers into colour?"** — read the *energy* sheet, put it through this curve, look the answer up in this gradient. Or: read the *edge-distance* sheet and make a rim. Or: ignore all the sheets and churn my own plasma. That is the paint recipe.
2. **"Do I also want to thin the paint anywhere?"** — this is the **veil**. It is a transparency the fill can vary from pixel to pixel, and it **multiplies** the shape's coverage. It never overrides it.

A fill with no veil (fully opaque everywhere) is saying: *"I have no opinion about the silhouette. The shape is in charge."* That is the default, and it is what most fills should do.

A fill *with* a veil can make a solid shape wispy, lacy, or holed — and this matters, because **five of the eleven generators already do exactly that today**, whether anyone intended it or not. The gradient you author for a Jet or a Fireball has a transparency slider on every colour stop, and the generator multiplies it straight into the silhouette. The veil is not a new power. It is the existing power, named and made deliberate.

---

**The one-line picture to remember.**

> The shape is **fabric cut with a soft, frayed edge**. The fraying belongs to the shape and nobody may take it away. The fill is **what the fabric is dyed with**, plus an optional **gauze you view it through**. You can re-dye it any colour and print any churning pattern on it, and the frayed edge is still exactly the edge that generator is famous for.

---

### 3.3 Why this design, and why not the alternatives

**Why not (a) — report 2 §7.1's "opacity rule travels with the shape, full stop"?**

Because §7.1 states its own cost and the cost is worse than it realised: *"a plasma fill cannot make a solid shape wispy. That is a real limitation on the promise."* It is not merely a limitation — **it is a regression.** Five generators let the fill thin them out *today*: Jet ×3, Fork Blast, Inferno, Fire, Fireball, all via the authored ramp's alpha column. Model (a) either takes that away or special-cases it back in on the first day. The veil gives §7.1 everything it wanted (the shape can always speak for itself) at no cost.

Report 2 §7.1's diagnosis is also, as established in §1.3 above, **false for four of the nine** it claims to cover. So it is a proposal whose stated motivation is wrong for 44 % of its own set and whose stated cost is a regression. It should be superseded, not refined.

**Why not (b) — the shape exports a raw field and the fill owns everything, alpha included?**

Two reasons, one architectural and one practical.

*Architectural:* a shape would then have **no silhouette until paint is chosen**. Groups break — a member's contribution to a union is undefined. Borders break — nothing to measure from. The compatibility story becomes "it depends on what you put on it", which is precisely the "silently does nothing" failure mode the whole exercise exists to abolish (report 2 §7.6 makes this point about missing fills; (b) generalises the bug rather than fixing it).

*Practical, and more damning:* the edge rule is **calibrated to numeric ranges only that generator knows.** Torch's alpha smoothsteps between `aLo` and `aHi` on a heat field whose peak is set by a `gain` dial; Jet's between `lo` and `lo+soft`; Orb's through `a0/a1/acurve/amax` on a tone that is `1−exp(−E·gain)`. Hand a generic plasma fill the raw energy of any of them and you will get either a hard-edged blob or a blank canvas, every time, until you hand-tune four numbers you have no intuition for. **(b) makes every fill swap a calibration exercise.** The veil keeps swaps free by default and calibration optional.

**Why not (d) alone — a fill is a (transfer, palette) pair?**

(d) is right and is *included* — it is exactly how the veil and the channel-selection are expressed, and it is what makes Jet's "two ramps crossfaded by soot" expressible at all. But (d) on its own says nothing about who owns coverage, which is the actual question. (d) is the fill's internal structure; the veil rule is the seam.

**Why (c) for the channel set?**

Because the code already votes for it, unanimously and in writing.<sup>1</sup> The channel names are not a design proposal — they are a transcription. And (c)'s "incompatible pairings are greyed out, not forbidden" is the right failure mode: greying out with a printed reason is discoverable; forbidding is a mystery.

### 3.4 The channel vocabulary, derived from the code rather than invented

| Channel | Meaning | Who already computes it |
|---|---|---|
| `coverage` | how solid — the shape's own edge rule, soft, unbounded extent | all eleven (published today by 6 as `alpha`/`alpha_f`) |
| `energy` | the primary field: heat / charge / density | all eleven (published as `H`) |
| `energy2` | the second field: soot, cooled heat, tint | Jet ×3 + Fork Blast (`T`), Torch (`C`) |
| `t` | the normalised transfer output — the ramp coordinate | 5 publish it today as `ramp_t` |
| `edgeDist` | distance from the silhouette edge | Plasma Bloom (`rim`/`rim_mix`); facet solids compute `edist`; Orb computes `rimDist` |
| `normal` | which way the surface faces | Inferno (density-gradient pseudo-normal), built-in Orb (analytic `P/R`), facet solids (per-face) |
| `height` / `depth` | toward the viewer | built-in Orb (`z = √(R²−d²)`), Text (extrusion slab index) |
| `age` | the life of the particle owning this pixel | **does not exist** — new, and the swarm's price (§7.2 of report 2 is right about this) |

Plus **1-2 generator-defined channels**, declared by name with a human-readable label, which is what (c) asks for and what Inferno's `fireMix` and Plasma Bloom's `rim_mix` want to be.

---

### 3.5 Worked example one, end to end: **Torch**

**What Torch does today.** It builds two number sheets at **3× the canvas resolution**: `_Fp`, the raw heat, and `_C`, a copy of the heat cooled with height. Then in one loop: alpha from `_Fp` by a smoothstep, colour from `_C` through a hard 7-band palette, written into premultiplied planes. Only at the very end does it box-filter the whole thing down to canvas size.<sup>13</sup>

**What the shape stage would output.**
- `coverage` = `smoothstep(aLo, aHi, H)` — the edge rule, with `aLo`/`aHi` staying on the shape where they belong
- `energy` = `H` (raw heat)
- `energy2` = `C / rampTop` (cooled heat) — labelled "heat, biased by height", because that is what it is
- a declared **native resolution multiplier of 3**

**What the default fill does to reproduce today's look exactly.** "Read `energy2`; sample this 7-band gradient; veil = 1." Then the pipeline writes premultiplied and does the single downsample at the end. **Result: bit-identical.** Not approximately — identically, because the arithmetic is untouched and the operation order is preserved (see §7 on the resolution handover).

**What happens when you swap in a different fill.** Say a churning-plasma fill reading `energy`:
- **The flame silhouette is exactly unchanged.** Same fraying, same tongues, same licks, same anchored envelope, same curl warp. That is the point of the design.
- **Only what is painted inside changes.**
- **The surprise to warn about:** today's palette is read at `energy2`, the *cooled* heat. A fill that naively reads `energy` (raw) will look subtly hotter toward the top of the flame, because the height-cooling that shaped the original palette's distribution is gone. This is not a bug — it is the fill reading a different sheet — but the picker must show *which sheet each fill reads*, and Torch's default must be labelled as reading `energy2`. Without that label, the first fill swap looks like a regression.

<sup>13</sup> `RT/Pyre/Forms/Kiln/TorchForm.cs:203-216`; SS = 3 at `PyreTorch.cs:384`; downsample at `TorchForm.cs:216` via `PyreSupersample.Downsample(float[]…)` `RT/Pyre/PyreSupersample.cs:72`.

### 3.6 Worked example two: **the owner's headline — a fireball wearing churning plasma**

Take "fireball" to mean any of the blast scenes (Fork Blast, Inferno, Plasma Bloom, Explosive Jet). **Does his example work under this design? Yes for the two halves that matter, partly for the third — and the partial half is a fill-authoring rule, not a design failure.**

**Half one — the paint churns on its own clock. WORKS, fully.**

The shape publishes `coverage`, `energy`, `edgeDist`. The plasma fill reads `energy`, modulates it with its own moving noise, and looks up a plasma palette. The one thing Pyre has never switched on is the **second clock**: today every call site feeds the ramp's animation position a zero. Report 2 §7.7 is correct that this is a field and a call-site change rather than an engine change — the animatable-gradient type already separates "where am I on the ramp" from "what is the ramp doing over time", and `ZuiFill` already carries a `Noise` texture with Stamped/Fixed anchoring, which is precisely a churn.<sup>14</sup> So the plasma churns at its own speed inside the fireball's silhouette, while the fireball's silhouette boils at its own speed. **That is the picture he described, and it is the cheapest thing in this document.**

**Half two — the silhouette keeps its identity. WORKS, fully.**

Because the veil defaults to 1, the fireball's boiling edge — its puffs, its accumulation, its rim falloff — is untouched by the paint. Swapping to plasma does not turn the fireball into a soft ball. This is the guarantee that (b) cannot make.

**Half three — the churn chews the edge. WORKS, but should not be the default.**

If he wants the plasma's turbulence to visibly *eat into* the fireball's silhouette, that is the veil set to the plasma's own density. It is available and it is one checkbox. But the result is a silhouette that is the product of **two** turbulences, and in practice that reads as noisier rather than more alive. Offer it; do not default to it.

**The half that genuinely disappoints, stated plainly.**

A churning-plasma fill that reads **only its own noise** and ignores the shape's `energy` will produce a **fireball-shaped hole with plasma behind it**. Flat. It will read as a decal, not as a burning thing, because all the internal structure — hot core, cooling rim, soot — was in the energy field the fill declined to read.

So the rule, which must be stated on the tin: **a good "material" fill reads the shape's energy channel and uses its own pattern as a modulation, not as the whole signal.** A fill that reads no channels at all is a *sticker*, and the UI should call it that. This is the one place the owner's mental picture and the mechanism can diverge, and it is worth a sentence in the fill picker rather than a discovery in month three.

<sup>14</sup> `ZuiFill.Evaluate(life, u, v) → Color` at `Zui/Scripts/Runtime/ZuiFill.cs:167`; `TextureKind.Noise` `:33`; `FillSpace.Stamped/Fixed` `:48`; `[SerializeReference] ZuiGradient gradientAnim` `:79`.

### 3.7 One rule that must be written on the wall at the start

**A shape always has a fill.** Report 2 §7.6 is exactly right about this and it costs nothing: a shape with no fill assigned defaults to flat white, and a monolithic shape's fill row reads *"this shape paints itself"* rather than showing an empty picker. Otherwise the model recreates the silent-does-nothing bug it exists to kill, relocated from "which generator" to "did anyone put a fill here".

---

## 4. The value test — does the split actually make these eleven more valuable?

Testing the owner's claim generator by generator, skeptically. **Verdict: real for six, modest for two, near-nil for two, and one is not a generator at all.** The claim is broadly true but noticeably uneven, and the unevenness is informative.

| Generator | What becomes possible | Worth having? |
|---|---|---|
| **Explosive Jet** (+ Jet, Radial Jet) | Publishes **two physically meaningful channels — heat `H` and soot `T`** — and Explosive Jet's fracture, chunk, gob, dust and spark populations all deposit into those *same two planes* with a per-piece soot tint. A fill crossfading two materials by soot therefore gets **shrapnel that is molten at the core and cooling at the rim, for free, with no new generator**. Metal, glass, ice, ash — all reachable. | **HIGHEST.** This is the single best argument for the entire exercise. The owner's "a jet's velocity field driving a metal fill" instinct is right, and the channel that makes it work (soot) already exists and is already published. |
| **Arc Burst** | The **only** generator that already deposits coverage and energy as two genuinely separate planes (`A` and `E`, max-composited, separately bloomed). Its "where the bolt is" and "how hot the bolt is" are already independent authored quantities. A bolt-shaped *anything* — chrome bolt, ink bolt, crack-in-glass — is immediately reachable. | **HIGHEST.** It is already the model, built. Splitting it is nearly free (§7) and the geometry (midpoint-displaced polylines, branching trees, jittered arcs) is genuinely reusable. |
| **Plasma Bloom** | Publishes `E`, `rim` **and** `rim_mix` — the only scene generator already crossfading two palettes by a *geometric* channel. A fill reading `rim` gets **edge-aware paint** for free. And `{coverage, distance-from-edge}` is precisely the input pair the Shaper interior-fill library wants, per `PYRE_GUG.md` §9. | **HIGH.** Two projects arrived at the same two channels independently. |
| **Torch** | Its silhouette is genuinely special — anchored envelope, tongues, curl warp, ground cut — and it publishes **two** channels (raw and height-cooled heat), so a fill can choose heat-biased or height-biased paint with no new work. "Flame-shaped anything, anchored to a ground plane" is a real, nameable look. | **HIGH.** |
| **Inferno** | Publishes density, heat, soot, detail **and a pseudo-normal** — the only field generator with a surface orientation. A fill reading the normal can put a genuine *material* on a boiling cloud. **And separately: Inferno's smoke grey is hardcoded today**; only the fire half goes through the fill. Splitting makes the smoke paintable for the first time. | **HIGH**, and it is the generator whose split *fixes something already broken*, not merely opens something new. |
| **Orb** | Five archetypes of energy projectile, one tone channel. Any fill is a straight re-skin — which is useful, but is largely the re-skin you already get by editing its ramp. The genuinely new things are **spatial and textured fills** (the ramp cannot do those) and the **second clock**. | **MEDIUM.** Real but modest. Its geometry (compact-support stamps, loop-exact turbulence, the −x smear) is the valuable part, and it becomes available to any paint. |
| **Fire** | A genuine fluid simulation — advection, buoyancy — with a two-channel physical field (heat + fuel). "Anything that *behaves* like fire": flowing lava, a moving liquid, smoke that is actually water. | **MEDIUM.** Real, but the practical ceiling is cost, not design: reaching frame *f* replays 0→*f*, so a bake is O(f²) and any live preview of the field is expensive. |
| **Fork Blast** | Publishes `H` and `T` — **a strict subset of Explosive Jet's channels**, and the code itself records that Explosive Jet supersedes it. | **LOW.** Honest answer: not much. Anything a fill could do to Fork Blast, it can do better to Explosive Jet. Split it for uniformity, not for value. |
| **Fireball** | Fire with a hardcoded centre source and no position parameter at all. Same heat grid, same channels. | **LOW.** Its value is inherited from Fire, not its own. |
| **Sprite** *(context — not one of the eleven, but the case §5 tests)* | `coverage` from the artwork's alpha; a cheap `edgeDist` computed from it. Becomes Pyre's import-a-silhouette-from-outside shape. | **HIGH as a shape provider, nil as a fill provider.** See §5. |
| **3D Playback** | Nothing. It renders nothing at bake time; `RenderLayer` returns immediately. | **N/A.** It is a viewer, not a generator. |

**The skeptical summary the brief asked for.** Two of the eleven (Fork Blast, Fireball) gain almost nothing, and in both cases the reason is the same and is documented in the code rather than inferred from usage: **a better generator already publishes the same channels.** One (Orb) gains modestly. But five gain substantially and one of those — the Jet family — gains enough on its own to justify the work. The owner's claim that "flexibility would rocket" is **true, but concentrated**: it rockets for the generators with a *second physically meaningful channel* (soot, rim distance, normal, cooled heat) and merely improves for the single-channel ones. That is a useful sorting rule for the build order.

---

## 5. Is anything genuinely irreducible?

**No. Retract the claim.** Report 2 §6 and F PART 1 row 13 both name exactly one — the imported-sprite generator — and both are wrong about *why*, in a way that matters.

**The test.** Sprite's coverage is the texture's alpha channel; its colour is the texture's RGB times a tint.<sup>15</sup> That is a **one-line channel separation** — arguably the most trivially separable generator in the library. It is not entangled by algorithm and F's own row concedes as much ("a trivial channel separation").

**What is actually true about it, stated correctly:**

> Sprite is the only generator whose **fill half is authored data rather than a rule**. Every other generator's colour half is a *function* — a ramp read at a field position — and a function can be replaced by another function. Sprite's colour half is a *picture*, and pictures do not parameterise.

That is a statement about **replaceability**, not about **separability**, and the two were conflated.

**Does it hold under the out-of-frame ruling and design (c)?** No — it gets weaker, in Sprite's favour:
- Under (c), Sprite publishes `coverage` (its alpha) and, at trivial cost, `edgeDist` (a distance transform of that alpha). Two channels. That is exactly the input pair the interior-fill and edge-treatment libraries want. **Sprite becomes a first-class shape provider with real channels**, not a special case.
- It can also publish `luminance` of the artwork as a generator-defined channel — lossy as a re-derivation of the drawing, but perfectly good as a *modulation* input for a fill.
- Its default fill is simply non-parametric: one entry in the picker, named "the artwork". Under §3.7's always-has-a-fill rule that is a completely normal state, not an exception.

So report 2's **conclusion** about Sprite (promote it to shape provider, land Shaper imports there) is right and should be kept. Its **reasoning** ("genuinely inseparable") is wrong and should be dropped, because if it stands, the model has a precedent for "some generators just cannot do this", and that precedent will be cited by the next awkward case.

**The two remaining candidates for irreducibility, both rejected:**
- **3D Playback** — not irreducible; *empty*. `RenderLayer` returns before drawing anything at bake time. It is a viewer. Excluding it is not an exception to the model, because it was never in the model.
- **The stateful sims (Fire, Fireball)** — their difficulty is **cost**, not structure: O(f²) replay to reach frame *f*, and O(N·f²) for a naive swarm. Structurally they are among the cleanest splits in the library — `FireSim.Step` and `FireSim.Render` are already two separate functions, and `Render` is the only place a colour appears.<sup>16</sup>

**Conclusion: zero of the twenty-nine techniques are irreducible.** Some are expensive; one has a non-parametric default fill; one is not a generator. None resist the split.

<sup>15</sup> `RT/Pyre/PyreRenderer.cs:3603` (alpha), `:3604` (RGB × tint), per F PART 1 row 13.
<sup>16</sup> `RT/SpriteFx/FireSim.cs:112` (`Step`), `:295` (`Render`); the replay is `RT/Pyre/PyreRenderer.cs:608-609`.

---

## 6. The escape hatch — should a "monolithic generator" kind exist?

**Yes — but only with a contract, and the contract is one line long. Without that line it is exactly the hole the owner is worried about.**

### 6.1 The judgment

A monolithic kind is a **healthy pressure valve** if and only if it is a *declaration about the fill*, and a **fatal hole** if it is allowed to be a declaration about *everything*.

Here is the failure mode concretely. If a monolithic generator publishes nothing, then every downstream stage — border, group, swarm, effects, preview, compatibility badges — needs a "…unless it's monolithic" branch. Each such branch is a place where the answer to "what will this do?" becomes *"it depends"*. That is not a valve; that is a second, undocumented model growing inside the first. Within a year "monolithic" is where anything hard goes, and the shape/fill/border model describes only the easy half of the library.

Conversely, a monolithic generator that publishes **coverage** costs the rest of the system **nothing at all**, because coverage is the only thing anything downstream actually requires. Border traces it. Group unions/carves it. Swarm composites it. Preview shows it. The fill picker shows one entry instead of many. **Everything works.**

### 6.2 The recommended policy

**The contract — a monolithic generator MUST publish `coverage`, and MAY publish zero channels.**

That single obligation buys, and the policy should state each explicitly so it is not a second-class citizen:

| Question | Answer for a monolithic generator |
|---|---|
| Does it publish coverage? | **Yes — mandatory.** No exceptions. |
| Does it publish channels? | Optional, including none. |
| Can it accept a swappable fill? | **No** — its fill picker shows exactly one entry, named ("the artwork", "its own palette"), never an empty or greyed control. |
| Can it accept **effects**? | **Yes, all of them that run on a buffer** — which is 20 of the 41 today, and would be 33 of 41 once a shape-local coordinate and a shape seed are published. Effects do not care about the shape/fill seam. |
| Can it be a **group member**? | **Yes** — union / carve / soft-union all operate on coverage, which it has. |
| Can it be **swarmed**? | **Yes** — the swarm composites by coverage. (Cost caveats for stateful sims are unchanged and orthogonal.) |
| Can it wear a **border**? | **Yes** — the border measures from coverage. |

**The second half of the policy, which matters as much as the first.** Monolithic must be a *declared reason*, not a *declared exemption*. There are exactly two legitimate reasons:

1. **Its default fill is authored data, not a rule.** (Sprite. A future Shaper import. A baked sheet.) These are permanently monolithic and that is fine.
2. **It has not been split yet.** These are *temporary* monolithic, and the declaration should say so — a `// monolithic: not yet split` marker that a compliance pass can count.

Any generator declaring monolithic for a third reason is a design bug, and the review question is always: *"which channel would a fill want that you decided not to publish?"* If there is an answer, it is not monolithic; it is unfinished.

**A last note in its favour.** Publishing coverage is not a burden bolted onto monolithic generators — **all eleven already compute it**, and six already publish it under the name `alpha`. The contract asks for something every candidate already has.

---

## 7. Cost — the eleven ranked, and the order-of-operations trap

### Band 1 — nearly free: the seam is already a function boundary AND alpha never reads the palette

| Generator | Why |
|---|---|
| **Plasma Bloom** | `Shade` is already a standalone static function; its alpha line touches no LUT; it already publishes `H`, `ramp_t`, `alpha`, `rim_mix`. The split is renaming its outputs and moving the two LUT reads out. |
| **Orb** | `Rasterise` is already a separate function; alpha is `OrbStyle.Alpha(tone)` and the LUT is untouched by it. The floor cut and despeckle are pure alpha operations that stay shape-side. |
| **Arc Burst** | Ships `E` and `A` as two independently deposited, independently bloomed planes. The only cross-read is `if (e < Floor) a = 0` with `Floor` a form constant. It is already the model. |
| **Torch** | Two planes already, and alpha and colour read *different* ones. Needs only the resolution-handover rule below. |

### Band 2 — real but mechanical: alpha = shape's edge × the ramp's own alpha column

| Generator | Why |
|---|---|
| **Jet / Radial Jet / Explosive Jet** | `ac = hot.a[idx]` soot-crossfaded with `soot.a[idx]`, then `a = smoothstep(…) · ac^opaq`. Exact scissors work. The real cost is *expressiveness*: the fill type must hold **two palettes and two opacity ceilings crossfaded by a second channel** — which today's `ZuiFill` cannot, and which is exactly why all three declare `UsesFill => false`. **One implementation covers all three.** |
| **Fork Blast** | Same product form, simpler (`aceil = baseColor.a`, one ramp). Already publishes `H` and `T`. |
| **Fire / Fireball** | `a = c.a · layerAlpha · clamp01(t·2.2)` — trivial to factor. The cost is that both sit **outside the `PyreForm` plug-in system entirely**, have no field publisher, and their O(f²) replay makes any live field preview expensive. Structural work, not algorithmic. |

### Band 3 — genuinely more work

| Generator | Why |
|---|---|
| **Inferno** | The only one with a **third** colour source hardcoded beside the fill (the smoke grey), the only one where a single quantity (`fireMix`) does double duty as colour-crossfade weight *and* alpha term, the only scene generator that does **not** implement `IPlusFieldPublisher` (so its channel export is genuinely new code), and the only one with post-shade composited decorations (ignition flash, embers) drawing where density is ~0. Four separate decisions, none hard, all needing an answer. |

### The order-of-operations trap — report 2 §7.8 undercounts

Report 2 §7.8 says *"two of the scene generators anti-alias after colouring, on purpose."*

**Verified: it is three, not two.** The missed one is Arc Burst, and it is the one that matters most for the exact failure §7.8 describes:

| Generator | Multiplier | Colour written at | Downsampled at |
|---|---|---|---|
| **Torch** | `SS = 3` (fixed) | `TorchForm.cs:207-214` | `TorchForm.cs:216` |
| **Plasma Bloom** | `SS = 2` (fixed) | `PyrePlasmaBloom.cs:588-592` | `PlasmaBloomForm.cs:469` |
| **Arc Burst** | `k = clamp(ceil(128 / min(W,H)), 1, 4)` — **2 at Pyre's typical 64 px canvas** | `ArcBurstForm.cs:498-503` | `ArcBurstForm.cs:520` |

§7.8's stated hazard is band-boundary crispness: colour a *downsampled* field and a hard-banded palette gets soft band edges. Torch has **7 hard bands**; **Arc Burst has 5 hard bands and was the one omitted**. Plasma Bloom, by contrast, blends two continuous LUTs and is the *least* sensitive of the three — so §7.8 named the two generators of which one is the least affected, and missed one of the two most affected.

**How the design accommodates them — completely, and at a known price.**

> **A shape declares a native resolution multiplier. The fill is evaluated at that resolution. The single premultiplied downsample is the last thing the pipeline does.**

Under that rule all three are **bit-identical** after the split, because the only thing that changes is *who* writes the colour into the premultiplied planes — the arithmetic, the resolution and the order are unchanged. This is not a hopeful claim: all three already exit through the *same shared function*, `PyreSupersample.Downsample(float[] pr, pg, pb, pa, …)`, which was written specifically to defer quantisation to a single point.<sup>17</sup> It stops being each form's last line and becomes the pipeline's last line.

**The honest price:** the substance buffers must be allocated at k×, i.e. **4× to 9× the memory** of a canvas-resolution handover. That is a memory cost, not a fidelity cost, and it is paid only by the three generators that ask for it. Every other generator declares a multiplier of 1 and is unaffected.

---

## What I could not determine

- I read the pixel-emitting loop of **six** of the nine scene generators (Orb, Torch, Arc Burst, Plasma Bloom, Fork Blast, Inferno) plus the shared Jet engine (covering the remaining three) and both fire sims. I did **not** re-read the built-in enum forms' loops (Disc, Star, the lit solids, Text) — F PART 1 covers them and G confirmed that part held, so I cite F rather than re-deriving.
- The claim that a resolution-preserving handover is *bit*-identical is derived from reading `PyreSupersample.Downsample` and each form's write loop; it is not measured. It is falsifiable by a parity dump and should be checked before the split is called done, because the whole Kiln port's value is bit-exact fidelity.
- I have not costed the fill type system itself. Report 2 §7.4 is right that a pluggable fill is a new `[SerializeReference]` type hierarchy rather than a rename; the Jet two-ramp requirement is the concrete forcing case, and the plug-in generator system is the in-repo precedent for how to do it.
- The per-pixel `age` channel (the swarm's price, report 2 §7.2) does not exist anywhere today and I have not designed it. It is the one channel in §3.4's table that is genuinely new construction.
