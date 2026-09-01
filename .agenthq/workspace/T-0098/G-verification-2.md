# T-0098 — Verifier G: adversarial check of `PYRE_SHAPE_FILL_BORDER.md`

Research only. No source file changed. `PYRE_SHAPE_FILL_BORDER.md` not modified. No Unity run, no Coplay, no subagents. Every load-bearing counter-claim below was re-derived from the working tree on `feat/lathe` (2026-08-30) or from an independent asset census I ran myself — not taken on trust from `E-spritefx-triage.md` or `F-decomposability.md`, both of which turn out to contain errors the draft inherited.

Documents under test / cited:
- Draft: `D:\UNITY\Laubrary Dev\PYRE_SHAPE_FILL_BORDER.md`
- First report: `D:\UNITY\Laubrary Dev\PYRE_GUG.md`
- Evidence: `D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0098\{A-generator-inventory,B-compatibility,C-shaper3d,D-verification,E-spritefx-triage,F-decomposability}.md`

**Headline.** 12 ERRORs, 9 OVERSTATEMENTs, 9 OMISSIONs, 5 QUIBBLEs. The most serious is **finding 1**: §7.1's "seven of the nine scene generators" is wrong in both directions at once — *all nine* derive coverage from their shade pass, and the "seven" the draft reuses in §7.4 is a **different set** (it excludes Inferno, which §7.1 depends on). The draft's hardest objection is materially understated, which matters because §14 claims §7 "was written to be as hostile to the proposal as the evidence allows".

**Two errors came in from the evidence files, not from the compression** (findings 1 and 6): `F-decomposability.md` says "seven of the nine plug-in scenes" while naming eight engines, and says "three of Shaper's four fuse modes" when two of four is correct. Fixing the draft requires correcting F as well, or the next reader will re-import them.

---

# ERRORS

## 1. ERROR — "seven of the nine scene generators" is the wrong count *and* silently means two different sets in two sections

**Draft, §7.1 heading and body:** *"For seven of the nine scene generators, the fill decides the shape"* / *"(1) For **seven of the nine big scene generators the fill decides the silhouette**"* (§1).
**Draft, §7.4:** *"**Seven of the nine scene generators explicitly declare that they do not use the layer's fill at all.**"*

**What is actually true.** These are two different sevens, and one of them is not seven.

*(a) The `UsesFill => false` seven is correct.* Verified: five declarations — `Runtime/Pyre/Forms/Kiln/ArcBurstForm.cs:50`, `OrbForm.cs:51`, `PlasmaBloomForm.cs:121`, `TorchForm.cs:55`, `Jet/JetFormBase.cs:23` — with `JetFormBase` covering Jet / Radial Jet / Explosive Jet = **7 of 9**. Inferno and ForkBlast are the two that *do* use the layer fill. §7.4 is right.

*(b) The "coverage decided by the shade pass" set is NOT that seven — and it is not seven at all.* `F-decomposability.md` Part 4 case 1 says "seven" and then names **eight** engines (Orb, Jet×3, Torch, Arc Burst, Inferno, Plasma Bloom). I checked the ninth. **ForkBlast also derives alpha from its energy field**: `float alpha = Mathf.Clamp01(edge * aceil) * layerAlpha;` (`Runtime/Pyre/Forms/Kiln/PyreForkBlast.cs:390`). And Inferno derives it from density: `float alpha = alpha0; … alpha = Mathf.Lerp(alpha, Mathf.Max(alpha, dense), p.body); alpha = Mathf.Min(1f, alpha * (1f + 0.55f * p.body * dense));` (`PyreInferno.cs:805-810`, inside the pass its own header labels *"shading pass"*, `:749`).

So: **all nine plug-in scenes decide coverage in their shade pass.** The set that declines the *layer* fill is seven; the set where *colour and coverage are decided together* is nine. The draft merges them, which lets §7.1's problem read as "seven, and here's why they voted that way in §7.4" — a causal link that does not exist, since Inferno is in one set and not the other.

**Why this matters.** §7.1 is the draft's own "hardest one". Making it universal across the plug-in family removes the last "…but two of them are fine" escape hatch, and it means the opacity-rule/colour-rule split in §7.1's answer is **mandatory for every plug-in scene**, not for a majority of them.

**Recommended replacement, §7.1 heading + first line:**
> ### 7.1 For every one of the nine scene generators, the fill decides the shape
>
> This is the hardest one, and it is universal, not a majority case. None of those nine computes a silhouette and then colours it. Each computes a smooth energy field and then decides *where that field becomes opaque* — a window, a curve, a floor, a speckle cleanup — inside the same pass that picks the colour.

**Recommended replacement, §1 bullet (1):**
> (1) **For all nine of the big scene generators the fill decides the silhouette**: they turn an energy field into a shape by choosing where it becomes opaque. Swap the fill and the shape changes. Seven of those nine separately declare that today's fill is not expressive enough for them — a different and equally damning count.

**Recommended replacement, §7.4 first line** (to break the false identity):
> Seven of the nine scene generators — a *different* seven from §7.1's nine, since Inferno and Fork Blast do use the layer fill — explicitly declare that they do not use it at all.

**Also fix in `F-decomposability.md` Part 4 case 1**, which is the source of the bad number.

---

## 2. ERROR — the substance value means **five** things, not four; the draft dropped one

**Draft, §4:** *"that value currently means **four different things** depending on where you are — distance across the shape in one place, distance across the whole canvas in another, *the heat value* inside one of the scene generators, and a hardcoded zero in a third host… **Formalising it means fixing all four.**"* Repeated in §12 slice 3 (*"the four places it currently means four different things"*) and §14 (*"the four different meanings of the substance value"*, filed under **Measured and solid**).

**What is actually true.** Five. I enumerated every `PixelInfo` construction site repo-wide (`grep -rn "new PixelInfo("`), which is the only place `crossFrac` is bound:

| Site | What `crossFrac` is |
|---|---|
| `Runtime/Pyre/PyreRenderer.cs:1346` (fed by ~15 raster sites, e.g. `:3090`, `:3773`, `:4762`) | shape-local centre→edge (or back→tip / hole fraction) |
| `Runtime/SpriteFx/SpriteFxBurst.cs:117` (value at `:115`) | **canvas**-local, `unit = min(halfW, halfH)` |
| `Runtime/Pyre/Forms/Kiln/PyreInferno.cs:820` | `hv` — **the heat value** (its own comment at `:815` says so) |
| `Runtime/Pyre/Forms/Kiln/PyreForkBlast.cs:394` | `t` — **a fork/puff parameter** ← *dropped by the draft* |
| `Runtime/SpriteFx/SpriteFxRecolor.cs:167` | hardcoded `0f` |

`E-spritefx-triage.md` §4 B3 lists all five and says "**fixing all five**". The compression lost the ForkBlast one.

**Recommended replacement, §4:**
> …that value currently means **five different things** depending on where you are — distance across the shape in one place, distance across the whole canvas in another, *the heat value* inside one scene generator, *a puff-index parameter* inside a second, and a hardcoded zero in a third host, which silently kills one effect there. Formalising it means fixing all five.

**And §12 slice 3:** *"…and fix the five places it currently means five different things."*
**And §14:** *"…the five different meanings of the substance value."*

---

## 3. ERROR — "all six lit solids": there are five, and the §6 row does not add up to its own count

**Draft, §6 table, MECHANICAL row:** *"**13** | All six flat stamps, all **six lit solids**, the ring, and text"*.

**What is actually true.** Six + six + one + one = **14**, against a stated count of 13. There are **five** lit solids: Gem, Box, Pyramid, Can (all four drawn by one routine, `DrawFacetSolid`, `Runtime/Pyre/PyreRenderer.cs:4152`) plus the built-in Orb (`DrawOrb`, `:4418`). `ShapeForm` (`Runtime/Pyre/Pyre.cs:112-121`) confirms the enum membership; Ring is a separate tilted-annulus routine (`DrawRing`, `:4609`) and is already counted separately in the draft's own sentence. `F-decomposability.md`'s Part 1 table lists them as rows 8-11 (Gem/Box/Pyramid/Can), 12 (Orb built-in) and 7 (Ring) — 6 flat stamps + 5 solids + Ring + Text = **13**. ✅

**Recommended replacement:**
> | **Mechanical** — interleaved but not entangled; routine work | **13** | All six flat stamps, all five lit solids, the ring, and text |

---

## 4. ERROR — the kaleidoscope's documentation does not say it was put in "the wrong stage"; and "half the generators" is invented

**Draft, §5:** *"**The kaleidoscope effect's own documentation says it was put in the wrong stage on purpose.** It states, in as many words, that it was made a whole-frame post effect *because that was the only stage that was universal* — the tool already had radial repetition, but it lived inside the particle path where **half the generators** could never reach it."*

**What the comment actually says** (`Runtime/SpriteFx/SpriteFxModifiers.cs:3466-3472`, read verbatim):
> *"Deliberately a POST modifier, working on the layer's finished pixels, because that is the only place that is universal: Pyre's existing `star`/`spreadCount` does radial repeat too, but it lives inside the SCATTER path, so MetaBlob, Height balls and Fire never reach it. Operating on pixels means every shape gets this, including ones not written yet."*

Two problems:

1. **The doc says the opposite framing.** It presents Post as the *right* stage given the constraint — *"the only place that is universal"*, *"every shape gets this, including ones not written yet"*. It never says or implies "wrong stage". `E-spritefx-triage.md` §3.4 calls it *"a workaround"* — that is E's interpretation, correctly flagged as such ("That is not a design statement about kaleidoscopes; it is a workaround"). The draft promotes E's interpretation into a quotation of the documentation. That is the exact failure mode the brief asked me to hunt: attributing to the code something the source labelled as a reading.
2. **"half the generators" is unsupported.** The comment names **three** things that cannot reach the scatter path (MetaBlob, Height balls, Fire). Nothing in E, F, or the code says half. (For scale: `D-verification.md` §3's re-measured table shows 8 of 17 built-ins skip the geometry hook — a different mechanism entirely.)

**Recommended replacement, §5:**
> **The kaleidoscope effect's own documentation is a written record of the gap you are proposing to close.** It states, in as many words, that it was made a whole-frame post effect *because that was the only stage that was universal* — the tool already had radial repetition, but it lives inside the particle path, so three named generators can never reach it. The author picked the one stage that reached every shape and wrote down why. **That is not a design decision about kaleidoscopes; it is the shape of the hole**, described by the person who fell in it.

---

## 5. ERROR — per-particle lifetime colour fades are on 42% of authored layers, not "the overwhelming majority"

**Draft, §7.2:** *"A colour ramp that runs white → orange → smoke over a particle's life is the default look of the tool, and **the overwhelming majority of authored layers are exactly that**."* Also §1: *"per-particle colour-fade-over-lifetime — **the default on the overwhelming majority of authored layers**"*.

**What is actually true.** I measured it directly across all 123 spec files / 305 layers under `D:\UNITY` (same census method as `D-verification.md`, re-run independently; my run reproduces 123 specs and 305 layers exactly):

| Property | Layers | Share |
|---|---|---|
| `shapeForm: 0` (Disc) | 238 | 78.0% |
| `swarmEnabled: 1` | 196 | 64.3% |
| `shapeFill.mode: 1` (OverLife) | 192 | 63.0% |
| **both swarmed AND OverLife** | **127** | **41.6%** |

A per-particle lifetime fade requires both. 41.6% is a plurality, not an overwhelming majority.

The likely origin of the error: `F-decomposability.md` Part 4 case 3 writes *"the default `OverLife` fire ramp on a Disc swarm — 78% of all authored layers are Discs"*. The 78% is the **Disc** figure (`D-verification.md` §3, 237/305; I get 238/305), not the ramp figure. The draft folded the adjacent statistic onto the wrong noun.

**Recommended replacement, §7.2:**
> A colour ramp that runs white → orange → smoke over a particle's life is the default look of the tool, and **127 of the 305 saved layers — 42%, the single largest group — are exactly that** (swarm on, fill in over-life mode). Nothing else in the corpus is close.

**And §1:** *"…and per-particle colour-fade-over-lifetime — the largest single group of authored layers, about two in five — simply ceases to exist…"*

---

## 6. ERROR — "three of 3D Shaper's four combine modes are already there" — it is two of four, and the draft contradicts itself four sections later

**Draft, §4:** *"**Three of 3D Shaper's four combine modes are already there — only soft blending is missing**, and the maths for soft blending is also already in the codebase, used elsewhere."*

**What is actually true.** Shaper's four fuse modes are **Add** (union), **Add soft**, **Subtract**, **Subtract soft** (`C-shaper3d.md` §2, sourced from Shaper's `PRODUCT_CONTRACT.md` layer-mode registry and confirmed live against `T-0058_layer-fusion-radios.png`). Pyre's `MatteCombine` is `{ Max, Add, Subtract }` (`Runtime/Pyre/Pyre.cs:164`). Mapping honestly:

- Shaper **Add** (union) ≈ Pyre **Max** ✅
- Shaper **Subtract** ≈ Pyre **Subtract** ✅
- Shaper **Add soft** ❌ missing
- Shaper **Subtract soft** ❌ missing
- Pyre **Add** (arithmetic accumulate) — has **no** Shaper counterpart; it is a Pyre extra, not a third match.

So **two of four**, with **two** soft modes missing. `F-decomposability.md` §2.4 is the source of the bad arithmetic ("three of Shaper's four fuse modes, missing only soft-blend"), and the draft imported it — then contradicted it in **§9**, which correctly lists **five** member combine modes *"(union / add / carve / soft-union / soft-carve)"* and in **§9 point 6** discusses soft-union as a two-knob problem. §4 says one soft mode is missing; §9 implies two.

**Recommended replacement, §4:**
> **Two of 3D Shaper's four combine modes are already there** — union and carve — plus one Pyre extra (arithmetic accumulate) that Shaper has no equivalent of. **Both of Shaper's soft modes are missing.** A one-knob smooth-max primitive exists in the codebase for another purpose and is a plausible starting point, but see §9 point 6: it is not the same control.

---

## 7. ERROR — the fake-depth effect *can* move the silhouette; the invariant holds only at default settings

**Draft, §5:** *"the **fake-depth effect is filed as a geometry warp and it provably never moves the silhouette at all** — a comment in its own implementation says so."*

**What is actually true.** The comment (`Runtime/SpriteFx/SpriteFxModifiers.cs:3140-3143`) reads: *"r=1 (the shape's own edge) always has ratio=1 on EITHER branch (asin(1)/(π/2)=1, sin(π/2)=1), so the edge never moves, for any Strength, either direction."* But `r` is normalised to the **lens** radius, not the shape's: `float effRadius = radiusOverride > 0.001f ? radiusOverride : ctx.radius; … float r = d.magnitude / effRadius;` (`:3119-3123`), and the lens centre is offset by two dials: `Vector2 effCenter = ctx.center + new Vector2(originXPx, originYPx);` (`:3121`).

The parenthetical "(the shape's own edge)" is therefore only true when `radius == 0` (auto) **and** `originX == originY == 0`. The modifier's *own tooltip* advertises the opposite case explicitly: *"0 (default) = auto, matching the shape's own current radius (ctx.radius)… **a larger one lets the effect extend past the shape's own edge**"* (`:3100-3104`). With a lens radius larger than the shape, or the lens centre offset, the shape's edge sits at r < 1 where ratio ≠ 1 — and the silhouette moves.

The *reclassification* still stands (Sphere is a fill op at its default configuration, and "fake depth = material" is the right reading). The word **"provably"**, and "at all", do not.

**Recommended replacement, §5:**
> The most striking: the **fake-depth effect is filed as a geometry warp, and at its default settings it does not move the silhouette at all** — a comment in its own implementation works through why. It only becomes a real warp if you push its lens wider than the shape or off-centre, which two of its own dials exist to let you do. Its normal use is a lens on the interior, i.e. a fill operation, sitting in the warping menu.

---

## 8. ERROR — §12's "none of them breaks a saved effect" is contradicted by slice 3

**Draft, §12 preamble:** *"Seven slices. **Each one ships value on its own, and none of them breaks a saved effect.** That property is what makes 'replace' unnecessary."*
**Draft, §12 slice 3:** *"**Name the substance.** … and fix the four places it currently means four different things. **Nothing user-facing**; everything downstream depends on it."*

**What is actually true.** Two of the five sites (finding 2) are *load-bearing for what saved assets render*, and "fixing" them changes those renders:

- `Runtime/SpriteFx/SpriteFxRecolor.cs:167` passes a hardcoded `0f`. `E-spritefx-triage.md` §4 B3 states this *"silently kills Tint's cross gradient in that host"* — the draft repeats this in §4 (*"a hardcoded zero in a third host, which silently kills one effect there"*). Giving it a real value **switches a currently-inert effect on** in every asset that uses Tint through Recolor.
- `Runtime/SpriteFx/SpriteFxBurst.cs:115` computes it **canvas**-local (`unit = min(halfW, halfH)`). Changing it to shape-local re-tunes Tint's cross gradient and Voronoi crack's spread mask in every SpriteFx Stack asset that uses them.

So slice 3 is neither "nothing user-facing" nor free of saved-asset breakage. It is the one slice with an unavoidable behaviour change, and the draft currently files it as the safe one.

**Recommended replacement, §12 preamble:**
> Seven slices. **Six of the seven ship value on their own without changing any saved effect; slice 3 is the one exception and is flagged as such.** That property is what makes "replace" unnecessary.

**Recommended replacement, §12 slice 3:**
> 3. **Name the substance.** Promote the per-pixel value that already exists into a documented, named, per-generator promise, and fix the five places it currently means five different things. **This is the one slice that changes what an existing asset renders** — two of the five sites are wrong in ways that today silently suppress or mis-scale a real effect, and correcting them un-suppresses it. Small, contained, and worth doing early precisely because everything downstream depends on it — but it needs a before/after render pass over the affected stacks, not a "nothing user-facing" label.

---

## 9. ERROR — "never look at the colour underneath" is false for at least three of the 22

**Draft, §1:** *"**Twenty-two of the 41 operate on geometry or coverage and never look at the colour underneath.**"*

**What is actually true.** `E-spritefx-triage.md` §0 says *"22 of 41 operate on geometry or coverage and **never needed** the colour underneath"* — a claim about what they are *for*. The draft hardened "never needed" into "never look at", which is a claim about what they *read*, and it is false for at least three members of that same 22:

- **Fuse (blob melt)** box-blurs the **premultiplied RGBA frame** and lerps colour by `bleed` (`Runtime/SpriteFx/SpriteFxModifiers.cs:2522-2528`) — it reads and writes colour.
- **Kaleidoscope** is a `PostModifier` that repeats *the layer's finished pixels* (`:3468`) — colour included.
- **Dissolve** is filed as Post *"specifically so smoothness can see real NEIGHBOUR pixels"* (E's own row 30).

**Recommended replacement, §1:**
> Twenty-two of the 41 operate on geometry or coverage and **never need the colour underneath to decide what they do** — three of them read the finished frame only because that is the only buffer the current architecture hands them.

---

## 10. ERROR — the "41" was never miscounted; the sub-counts were. And the draft says "twice" in one place and "three ways" in another

**Draft, §5:** *"Counted against the code — 41 effects, and **the count was independently verified twice** because earlier drafts got it wrong."*
**Draft, §14:** *"…and **the 41 count was independently reconciled three ways** after earlier drafts of the first report got it wrong."*

**What is actually true.** `D-verification.md` §1.2 is explicit: *"The total (41) is right, so the three sub-counts must be 17/12/10."* What earlier drafts got wrong were the **stage sub-counts** — `PYRE_GUG.md` said "fifteen warping effects" and "11 recolour effects" against actual 17 and 12, and `B-compatibility.md`'s headings said "(15)/(11)/(12)" while listing 17/12/10 names. The 41 total was correct in `B` and correct everywhere it appeared. The draft credits the wrong number with the correction.

Separately, "verified twice" (§5) and "reconciled three ways" (§14) are two different claims about the same fact in one document.

**Recommended replacement, §5:**
> Counted against the code — 41 effects. The total was never in doubt; what earlier drafts got wrong were the per-stage sub-counts, which have now been re-derived from the class hierarchy twice, independently.

**Recommended replacement, §14:** *"…and the per-stage sub-counts behind it were independently re-derived twice, after earlier drafts of the first report published the wrong ones (the 41 total was always right)."*

---

## 11. ERROR — the melt-together effect's colour-bleed dial does not become meaningless

**Draft, §5:** *"It also makes one of that effect's own dials — the one for bleeding colour between the merged shapes — **meaningless, because there would only be one fill**."*

**What is actually true.** `colorBleed` is not an inter-shape control. It is a per-pixel lerp toward the blurred colour applied across the whole affected region: `float mix = origA > 0.02f ? bleed : 1f; Color fc = Color.Lerp(ownColor, blurColor, mix);` (`Runtime/SpriteFx/SpriteFxModifiers.cs:2526-2527`). Its own tooltip: *"How much colour blends across the fused seam. 0 = each pixel keeps its own colour (only the silhouette fuses); **1 = colour is fully blurred too (a smooth blended melt)**"* (`:2473-2474`).

With one fill it still does something whenever that fill is spatially or lifetime-varying: it blurs the fill's *own* internal variation. And the modifier survives as a post effect over ungrouped layers regardless — the class comment says it works *"as a global modifier — several different layers melted together after they all composite"* (`:2454-2455`). `E-spritefx-triage.md` §3.5's parenthetical ("there would be one fill, so there is nothing to bleed between") is an inference not supported by the implementation, and the draft states it more flatly than E did.

**Recommended replacement, §5:**
> It also narrows the job of one of that effect's own dials — the one that blends colour across the fused seam. With a single fill over the group there is no longer a seam *between two different fills* to blend, though the dial keeps a smaller meaning: softening a spatially-varying fill's own internal variation.

---

## 12. ERROR (internal) — the draft uses two different denominators for the same zero, and never states the clone caveat

**Draft, §1:** *"**It has been used on zero of the 79 authored layers on this machine.**"*
**Draft, §4:** *"**Zero of the 305 saved layers use it.** Not 'rarely' — zero."*
**Draft, §10:** *"Channel-based grouping across layers … **Zero of 305.**"*

**What is actually true.** Both are zero, so no conclusion moves — but the document states the same measurement against two denominators three sections apart, and never explains why. The reason exists and is important: `D-verification.md` §2.2 established that **three of the four projects are clones of the fourth** (123 spec files but only 32 distinct filenames and 75 distinct content hashes), and `F-decomposability.md` §11 says explicitly *"'zero uses of grouping' is zero out of 79 real layers, **not** out of 305"*. My own independent census reproduces 123/305.

Additionally, "**on this machine**" in §1 is wrong: all four project copies are on this machine; 79 is one project's layer count, standing in for the de-duplicated corpus.

**Recommended replacement, §1:**
> **It has been used on zero of the 305 saved layers — and because three of the four project copies are clones, that is zero out of ≈79 genuinely distinct authored layers.**

**Recommended replacement, §4 and §10:** keep "zero of 305" but append once, at first use in §4:
> (305 is the raw count across four project copies, three of which are clones of the fourth; the distinct authored corpus is ≈32 effects / ≈79 layers. Zero either way.)

---

# OVERSTATEMENTS

## 13. OVERSTATEMENT — "exactly two" fill generators is presented as measured; the evidence calls it decision-dependent and nine effects contested

**Draft, §1:** *"Counted against the code: of the 41 effects, **exactly two** could generate a picture on their own."* **§14:** *"**Measured and solid:** the 41 effects and **their classification**."*

**What the evidence says.** `E-spritefx-triage.md` §0: *"**Nine of the 41 are genuinely contested** … Their placement above is my **primary call, not a clean one**."* And E §7.2 names the pivot: *"Whether 'fill' in the owner's model means per-sample or per-buffer. The whole (b)-vs-(c) distinction shifts… **This is the single question that most changes my table**, and it is a decision, not a fact I can read out of the code."* Two of the contested nine are near-misses for the fill-generator bucket in their own right: Tint's cross-gradient half *"IS a radial fill"* (E §3.1) and Colour tint at amount 1 *"is a flat-colour fill generator"* (E §3.2).

The draft handles this honestly in **§13 Q1** — but §1, §5 and §14 all present the number as settled, and a reader who stops at §1 gets a firmer number than exists.

**Recommended:** in §1, after "exactly two", add: *"— under the reading that a fill is a per-pixel function. Nine of the 41 are genuine judgement calls, and one open decision (§13, question 1) moves the number: if a fill may be a whole-buffer pass, four qualify, not two."*
In §14, change *"the 41 effects and their classification"* → *"the 41 effects, their stage assignment, and the bucket boundaries — with nine placements flagged in the working notes as judgement calls rather than measurements."*

## 14. OVERSTATEMENT — "Every energy-field generator blurs its field… so the visible extent is always larger"

**Draft, §7.3:** *"**Every energy-field generator** blurs its field *before* deciding where it is opaque, so the visible extent is **always** larger than any silhouette you could name."*

`F-decomposability.md` Part 4 case 4 cites exactly **two** (`PyreArcBurst.cs:7-8`, `PyreOrb.cs:6-7`) and hedges its own generalisation. Neither F nor I verified the other seven. And "always larger" does not follow from "blurred then thresholded" — a blur followed by a *high* threshold can shrink the opaque region.

**Recommended:** *"At least two of the energy-field generators blur their field before deciding where it is opaque, so their visible extent need not match any silhouette you could name; the pattern looks general across the family but only two were checked."*

## 15. OVERSTATEMENT — "the maths for soft blending is also already in the codebase" glosses over a direct disagreement between two evidence files

**Draft, §4** and **§9**: *"Soft blending is the one combine mode missing, and the maths for it is already in the codebase for another purpose."*

`F-decomposability.md` §2.4 says `PyreField.SmoothMax(a, b, k)` (`PyreField.cs:139`) is that maths. `C-shaper3d.md` §7 reached the opposite conclusion: *"Soft-add/soft-subtract's viscosity blend … has **no matching primitive in Pyre's matte model at all** — it would need new renderer code regardless."* Both are partly right: the smooth-max exists in the *field pass*, not in `MatteCombine`, and it is one-knob against Shaper's two (which the draft does say, but only in §9 point 6, seven pages later).

**Recommended, §4:** *"Both of Shaper's soft modes are missing. A one-knob smooth-max primitive exists in the field-pass code and is the obvious basis, but it is not wired into the combiner and it is not the same control as Shaper's two-knob viscosity (§9, point 6) — the Shaper study concluded this needs new renderer code either way."*

## 16. OVERSTATEMENT — "for no reason anyone wrote down" / "nothing in the tool explains why"

**Draft, §1 and §4** on the border gate.

`F-decomposability.md` §2.2 does supply a reason: *"The gate is policy, not capability — it exists because the solids have their own edge lines, Text has its own SDF border, and nobody wanted a rim on Fire."* That is F's inference and is not written in the source — so "nothing in the tool explains why" is literally true — but presenting it as *unexplained* rather than *explained-by-inspection* is what lets §12 slice 1 read as risk-free. See finding 22.

**Recommended, §4:** *"…and off for the other 23. Nothing in the tool says why, but the reason is legible from the code: the solids draw their own edge lines, Text has its own SDF border band, and a rim on Fire would be nonsense. That is policy, not capability — `BuildBorderBuffer` never reads the generator, only a colour buffer."*

## 17. OVERSTATEMENT / possible contradiction with `PYRE_GUG.md` §4 — "the special slot disappears"

**Draft, §11 table:** *"| 'Simulation' as its own special slot | A shape op that happens to keep state — the special slot disappears |"*

The evidence (`E-spritefx-triage.md` §3.9) is about the **SpriteFx modifier stage** — `SimulationModifier`, whose only member is Pixel fluid, referenced at `Pyre.cs:766`. That claim is sound. But §11's table is otherwise entirely about *generators* and *effect lists*, and `PYRE_GUG.md` §4 reaches the opposite verdict about the thing a reader will assume is meant: *"**Verdict: keep both, label them 'Sequential' on the badge strip**"* for Fire and Fireball. Nothing in E or F disturbs that. As written the row reads as a reversal of the first report that the draft does not acknowledge and does not intend.

**Recommended:** *"| The 'simulation' **effect** slot — one stage holding one effect | A shape op that happens to keep state; the special effect stage disappears. (This is not the Sims *generator* family — Fire and Fireball keep their own badge, per the first report.) |"*

## 18. OVERSTATEMENT — §11's "one Shape picker with everything in it" vs the first report and the draft's own §6

**Draft, §11:** *"One **Shape** picker with everything in it, including the currently hidden ones."*

`PYRE_GUG.md` §10 recommends the opposite for one entry: *"Recommendation: take [3D Playback] out of the generator picker and make it an honest reference tool, or cut it. Leaving a picker entry that draws nothing is the one thing not to do."* And the draft's own §6 files it as **"Not a shape at all"**. So "everything" is either a reversal or a slip, and 3D Playback's fate under the new model is stated nowhere in the draft.

**Recommended, §11 row 1:** *"One **Shape** picker with everything that is a shape in it, including the four currently hidden mechanisms — and with 3D Playback out of it, as the first report already recommended, because §6 finds it is not a shape at all."*

## 19. OVERSTATEMENT — "the lit solids are not the expensive case"

**Draft, §1:** *"The first report guessed the lit 3D solids would be the expensive case; **they are not**."*

`F-decomposability.md` says only that they are not **entangled**: *"That is true line-by-line and misleading architecturally."* It then files all five as **MECHANICAL — "routine work"**, i.e. *more* work than the 14 CLEAN ones, and adds *"What must move with it is the lighting model… That is the one real design decision."* So the first report's *ranking* was right (solids cost more than the field generators); what was wrong was the *diagnosis* (entanglement).

To the draft's credit **§6 states the correction properly and attributes it** (*"The first report assumed they were, because their lighting is computed and consumed in the same breath"*). It is only §1's compression that overshoots.

**Recommended, §1:** *"The first report guessed the lit 3D solids would be the hard case; they are not the *entangled* case, which is what 'hard' was taken to mean. They already carry **five separate fills each**, and the code itself already calls one of them the material — so a material stage for them is not something to invent, it is something to move (which is still real work)."*

## 20. OVERSTATEMENT — "Each one ships value on its own" contradicted twice inside §12 itself

**Draft, §12 preamble:** *"**Each one ships value on its own.**"*
**Draft, §12 slice 6:** *"**Only meaningful after slice 5; do them as one project.**"*
**Draft, §12 slice 4:** *"Migration risk lives here; **nothing else waits on it being finished.**"*

Slice 6 is by the draft's own sentence not independently shippable. And slice 5 *does* wait on slice 4 in one specific way: `F-decomposability.md` §2.1 lists what is new for FILL as *"(a) a polymorphic fill/material base with `[SerializeReference]`; **(b) a substance-aware `Evaluate` signature**"*. Today's signature is `Color Evaluate(float life, float u, float v)` (`Zui/Scripts/Runtime/ZuiFill.cs:167`) — it takes no substance. A generator cannot "opt into the new handover" (slice 5) until something on the fill side can receive it. Slices 4 and 5 are two halves of one contract change.

**Recommended, §12 preamble:** *"Seven slices, of which slices 1-4 ship independently. Slices 5 and 6 are one project (the draft says so at slice 6), and slice 5 needs slice 4's new fill signature to have somewhere to hand the substance to."*
**And slice 4:** *"Migration risk lives here. Slice 5 waits on its *signature*, though not on its migration being finished."*

## 21. OVERSTATEMENT — §14's "it does not soften the case against"

**Draft, §14:** *"**One thing this document deliberately does not do:** it does not soften the case against. §7 is the most valuable section here and it was written to be as hostile to the proposal as the evidence allows."*

Findings 1, 9, 14 and 25 are each a place where §7 or §1 states an objection *more weakly* than the evidence does, and finding 25 is an objection E raised that §7 omits entirely. The claim is not sustainable as written.

**Recommended:** *"**One thing this document tries to do:** not soften the case against. §7 was written to be as hostile to the proposal as the evidence allows — and it should be re-read against the working notes before it is trusted, since the compression from those notes to this page lost at least one objection and understated another."*

---

# OMISSIONS

## 22. OMISSION — slice 1 does not mention that four generator families already draw their own border

**Draft, §12 slice 1:** *"**Ungate the border stage.** Turn the existing, working border on for all generators instead of six. Days, not weeks. Immediately visible. **Start here.**"*

**What is missing.** `F-decomposability.md` §2.2 inventories four *other* edge treatments that would now co-exist with an ungated border stage:
- the lit solids' `isLine` hard edge (`edist ≤ gemLineWidth`, `Runtime/Pyre/PyreRenderer.cs:4345-4351`) plus `gemEdgeGlowFill` and `gemInnerGlowFill` (`:4345-4380`, `:4552-4571`);
- Text's own SDF border band, which switches `textFill` → `textBorder` (`:3994-3999`, `:4025-4027`);
- `edgeSoftness`, honoured by the six flat forms and ignored by the solids, Sparkle, Sprite, Text, Fire, Fireball, Playback3D (`Editor/Pyre/PyreWindow.cs:1320-1322`);
- and, for Fire/Fireball, an alpha derived from energy rather than coverage (`FireSim.cs:303`), so a chamfer "inside distance" is measured off a soft energy edge, not a silhouette.

Ungating therefore gives Text and the solids **two independent border systems on the same rim**, and gives Fire a rim on something that has no rim. That is not a reason not to do slice 1 — it is a reason slice 1 is "days" only if it also ships a per-form default (off for the forms that already have one).

**Also missing:** the gate is **two** clauses, not one — `p.hasBorder = layer.form == null && layer.borderEnabled && IsFlat2DBorderForm(layer.shapeForm)` (`Runtime/Pyre/PyreRenderer.Layers.cs:92`). The `form == null` clause excludes all nine plug-ins independently of the hardcoded list. "Ungate" is two changes.

**Recommended, §12 slice 1:**
> 1. **Ungate the border stage.** Turn the existing, working border on for all generators instead of six. Two clauses to remove, not one: a hardcoded six-form list, and a separate test that excludes every plug-in scene. Budget for one extra thing the gate was quietly doing: the solids and Text already draw their own rim, and Fire's alpha is an energy threshold rather than a silhouette — so ship it with a per-form default (on for the flat stamps, off where a rim already exists) rather than on for everything. **Verified safe against the saved corpus: exactly one of the 305 saved layers has the border switched on, and it is a flat non-plug-in form, so no existing effect changes.** Days, not weeks. Immediately visible. **Start here.**

## 23. OMISSION — §7.5 says "Pyre already sorts effects into buckets, so nothing is lost there". Pyre's *post* bucket is order-sensitive, and the four-way split cuts it in half

**Draft, §7.5:** *"**Splitting one ordered list into four destroys ordering — in one place.** Pyre already sorts effects into buckets internally, **so nothing is lost there.**"*

**What is missing.** `E-spritefx-triage.md` B2 was arguing about a **three**-way split; the draft's model is **four**-way, and the extra cut lands inside a bucket Pyre *does* order. Verified: `CollectMods` skips post entirely (`if (m is PostModifier) continue;`, `Runtime/Pyre/PyreRenderer.cs:1284`) — but `ApplyLayerPost` then **walks the authored list in order** and applies every `PostModifier` in that order (`:1354-1368`). Three of the ten post effects go to the new Border bucket (Outline, Bloom, Edge smooth) and two stay in Post (Chromatic aberration, Drop shadow). Today a user can author "drop shadow, then outline" or "outline, then drop shadow" **in Pyre** and get different pictures. Under a Border-stage-then-Post-stage model, one of those orderings stops being expressible in Pyre too — not just in the standalone stack.

**Recommended, §7.5:**
> **Splitting one ordered list into four destroys ordering — in two places, not one.** Pyre already buckets warps and colour work separately, so cutting *those* apart loses nothing. But Pyre runs its whole-frame effects **in the order you authored them**, and the four-way split cuts that run in half: three of them become borders and two stay post. Any Pyre layer whose look depends on a drop shadow sitting before or after an outline is affected. The **standalone effect stack** is worse: it walks the whole authored list in order and flushes deliberately so later effects see what earlier ones produced, so any saved stack that interleaves a warp between two colour operations **stops being expressible**. Two real migration hazards, in two real hosts.

## 24. OMISSION — the draft reverses `PYRE_GUG.md` §9 on what the Mass generators become, without saying so

**`PYRE_GUG.md` §9:** *"It absorbs three of the hidden generators. Height-relief, the mask-fed height field, and the melt-into-a-blob shading all stop being separate techniques and **become materials that read height**. Family 3 mostly dissolves into Family 1 plus a material choice."*
**Draft, §6:** files all three (Coalesce = Fuse, Coalesce = Ramp, Height consumer) as **CLEAN shape generators**.
**Draft, §11:** *"| Melt-together, and a channel-wired grouping nobody found | One **Group** shape, visible, ordered, transformable |"*

The first report said they become **materials**; this draft says they become **a shape**. That is a real reversal and the right one (F's Part 1 shows all three compute a field and then colour it — they are shape stages with a trivial fill, not fills). But the brief asked whether §11 and the GUG verdict table agree about the Mass generators, and they do not, silently.

**Recommended:** add one line to §11, after the table:
> *One correction to the first report, stated so it is not a silent reversal:* it proposed that the three hidden Mass generators become **materials that read height**. On inspection they are the opposite — each computes a field as a distinct step and then colours it, so under this model they are **shape** generators (a Group, and a height source), whose fill happens to be a ramp read off the field. Same three techniques absorbed, different side of the seam.

Note also that `F-decomposability.md` §11 warns that `PYRE_GUG.md` §10's proposed fusion — *"Height relief + mask-fed height field → one Height field"* — **merges a used technique (8 layers) with a never-used one (0 layers)**. That warning is nowhere in the draft and belongs in §11's "what is not replaced" paragraph.

## 25. OMISSION — E's "what the new model would make *worse*" is not in the draft at all

`E-spritefx-triage.md` §4.3 is a section with exactly one item, and it argues against the proposal:

> *"**Fill ops become 9 controls hunting for a fill that may not exist.** … In a shape/fill/border model, a shape layer with no fill assigned (or a shape group whose fill is applied at group level) gives them nothing to act on, and **the 'silently does nothing' failure the model is meant to abolish reappears in a new place — the same bug, relocated from the generator axis to the stage-population axis.** The mitigation is trivial (a shape always has a fill, defaulting to flat white) but it must be a rule, not an accident."*

This is the model's own headline benefit turning back on itself, and §7 — which §14 claims was written to be maximally hostile — does not contain it. It is also *the* argument a sceptical reader will reach for.

**Recommended:** add to §7.5 as a fourth smaller item:
> **The bug the model abolishes can reappear one axis over.** Nine of the effects are colour operations that need a finished pixel underneath. Today one always exists. Under shape/fill/border, a shape layer with no fill — or a group member whose fill is applied at group level — gives them nothing to act on, and "this effect silently did nothing" comes back, relocated from the generator axis to the stage-population axis. The fix is trivial and must be a **rule**, not an accident: a shape always has a fill, defaulting to flat white.

## 26. OMISSION — slice 5's "the 14 clean ones opt in cheaply" ignores the codebase's own counterexample

**Draft, §12 slice 5:** *"The 14 clean ones opt in cheaply — several already have the seam as a function boundary."*

`E-spritefx-triage.md` §4.1 F1 attaches a caveat the draft drops entirely:

> *"whole-field resampling is precisely what `PyreFormWarp.BuildMap/Apply` already does for plug-in forms (`PyreRenderer.cs:349-350`), and **`InfernoForm` opts out of it** (`HandlesGeometry => true`, `InfernoForm.cs:19`) — evidence from inside the codebase that a post-hoc resample is **not** always visually equivalent to a per-sample warp. **The no-op is traded for an aliasing/quality risk, not eliminated for free.**"*

Both E (§7.3) and F ("what I could not determine" #3) list this as open. Inferno is one of the 14 CLEAN generators, so it is a counterexample sitting inside the exact set the draft calls cheap.

**Recommended, §12 slice 5:** append: *"— with one warning from inside the code: the existing whole-buffer resample is opted **out** of by one plug-in precisely because it is not visually equivalent to a per-sample warp. Expect the universal shape stage to trade 'silently does nothing' for an aliasing question, not to eliminate it for free. That is worth one rendered A/B before slice 5 commits."*

## 27. OMISSION — the coverage-from-fill problem is not confined to the scene generators; Fire has it too

**Draft, §7.1** presents this as a plug-in-scene problem. **§6** presents both fire simulations as **CLEAN**, with the seam already a function boundary.

`F-decomposability.md` Part 1, row 15 (Fire), records the same defect inside a CLEAN verdict: *"Only real loss: alpha is derived from the energy, not the coverage — `a = c.a · layerAlpha · clamp01(t·2.2)` (`FireSim.cs:303`). So the material must be allowed to *set* alpha from energy, not merely tint a given coverage."*

So the opacity-rule/colour-rule split §7.1 proposes is required by **11 of the 29** (nine plug-in scenes + both fire sims), not by seven — and "CLEAN" does not mean "free of §7.1".

**Recommended, §7.1**, after the answer paragraph: *"And it is not only the scene generators. Both fire simulations derive their alpha from the heat value in the same line that picks the colour — so eleven of the 29 need the opacity rule split out, including two the decomposability table calls clean."*

## 28. OMISSION — no generator today has two clocks, and §9 point 3 depends on one existing

**Draft, §9 point 3:** *"**Whose clock does the fill run on?** Decide explicitly that **the fill runs on the group's clock**… it is literally your fireball-with-plasma example: the shape changes over time while the surface churns on its own schedule."*

`F-decomposability.md` Part 4 case 5 is a whole hard-case entry the draft has no trace of: *"there is no clock today on which a fill could animate independently of its shape… No generator today has two clocks. There is no per-layer time-remap, no sub-clock, no phase offset field."* It also carries the good news the draft would want: `ZuiFill` already separates the ramp-position axis from the animation clock (`EvalGrad(t, life)`, `Zui/Scripts/Runtime/ZuiFill.cs:235`), and the `phase` argument exists but **every Pyre call site passes `0f`**, so the palette-cycle driver has never been engaged in Pyre. So the material clock is *"a field plus a call-site change, not an engine change"*.

That is both a missing obstacle and a missing piece of supporting evidence for the owner's own headline example, and it is absent from §4, §7 and §9 alike.

**Recommended:** add to §4 as a fifth half-built piece, or to §9 point 3: *"One thing the sketch assumes and the code does not have: a second clock. No generator today animates its colour independently of its shape. The good news is that the fill type already separates 'where on the ramp' from 'the ramp's own animation clock', and its phase input exists and is passed zero at every Pyre call site — so a material clock is a field and a call-site change, not an engine change."*

## 29. OMISSION — a fifth body of authored Pyre content is never counted

`D-verification.md` §4.2: each of the four projects also holds an `Editor.PyreLayerLibrary` asset (`…\Assets\Pyre\PyreLayerLibrary.asset`) with **13 saved layers on the pre-rename schema** (`shape:`, the Pyre1 enum, not `shapeForm:`) — correctly outside the 123/305 census, but *authored Pyre content that any generator removal or fill migration would affect*. Also relevant to §9's *"Pyre has no reusable-shape concept"*: a layer **library** is the closest thing that exists, and it is on a schema nothing in this proposal has considered.

`D` filed this as something `PYRE_GUG.md` should not have dropped. The addendum drops it too.

**Recommended, §14 under "Confidence and limits":** *"One body of authored content is outside every number here: each project also holds a layer library of 13 layers on the pre-rename schema. Nothing in this document has considered how those migrate."*

## 30. OMISSION — one effect legitimately belongs in two buckets, and a one-home-per-effect model loses one of them

`E-spritefx-triage.md` §3.3: *"Ordered dither … dithers **alpha** specifically to turn soft edges into stipple … That is a shape/coverage op. But it is *also* the classic last-pass pixel-art treatment, and the same class would be wanted on the composited frame. **It genuinely belongs in two places**, and a model with one home per effect will lose one of them."*

The draft files Ordered dither under Shape ops (§5, as "stipple") and never mentions the loss. Small, but it is a direct cost of the four-list split and belongs in §7.5.

---

# QUIBBLES

## 31. QUIBBLE — "extrude" listed as an example of "today's 41 effects"

**Draft, §2 table:** the Shape-ops row is headed *"How many of today's 41 effects: **22**"* and its Examples cell reads *"Bend, skew, erode, dissolve, wipe, melt-together, **extrude**"*. Extrusion is not one of the 41 — the draft's own §8 says there is *"exactly one worked instance… welded into the text generator"* and calls it *"essentially new construction"*. Suggest italicising it as a *proposed* member, e.g. *"…melt-together, and — new — extrude"*.

## 32. QUIBBLE — "melt-together" names two different mechanisms

§2, §5 and §7.5 use "melt-together" for the **post effect** (`FuseModifier`, a blur-and-rethreshold on finished RGBA, one of the 41). §11's table row *"Melt-together, and a channel-wired grouping nobody found → One Group shape"* appears to mean the **Coalesce radio** (a per-layer swarm mode, which §1 and §10 call "the melt-into-one-blob mode"). Given §3 of this very draft warns *"the word already means three different things in the tool… Adding grouping under that name makes it four"*, the draft should not itself use one informal name for two of them. Suggest §11 read *"The melt-into-one-blob swarm mode, the melt-together effect, and a channel-wired grouping nobody found"*.

## 33. QUIBBLE — "a border stage available to 6 of 29 generators" is a shapeForm predicate, not a technique predicate

The gate keys on `layer.shapeForm` and `layer.form` (`PyreRenderer.Layers.cs:92`), not on which of the 29 techniques is active. A Coalesce = Fuse layer or a height-consumer layer still carries a `shapeForm`, so one set to Disc/Ring/etc. **does** get a border today. "6 of 29" is a good headline but is approximate; worth one parenthetical if this document is used to scope the work.

## 34. QUIBBLE — "four generators reach past instead of through" (§14) — three generators, four call sites

The draft's own preceding sentence says *"a fire, fireball or text layer"* (three) and *"Those four places"* (four sites — `PyreRenderer.cs:442`, `:616`, `:798`, `:4084`). The closing line then says *"four generators"*. Inherited verbatim from `F-decomposability.md` Part 4 case 9, which makes the same slip. Read *"three generators, at four call sites, reach past the fill instead of through it."*

## 35. QUIBBLE — "at positions its density field does not cover" (§7.3)

`F-decomposability.md` Part 4 case 4 says *"positions the density field **may not** cover"*. The draft hardened it. One word: *"may not cover"*.

---

# WHAT SURVIVED

These I attacked and could not break. Several I re-measured from scratch rather than accepting the evidence files.

**Counts and classifications.**
- **41 effects**, and the stage split **17 Geometry / 12 Pixel / 10 Post / 1 Edge / 1 Simulation**. Independently confirmed against the class hierarchy by `D-verification.md` §1.2 and re-walked by `E`; the two agree exactly.
- The **bucket counts 22 shape / 9 fill op / 4 border / 2 post / 2 fill generator / 2 cull = 41**. Arithmetic closes; every member is individually justified in E's §2 table with a `file:line`.
- **"Ten of the 41 are filed in a category that does not predict what they are."** E's cross-tabulation is internally consistent (rows sum 17/12/10/1/1, columns sum to the bucket counts, off-diagonal cells sum to 10). Solid.
- **29 generators, split 14 clean / 13 mechanical / 1 entangled / 1 N/A.** The 29 arithmetic (17 live enum + 9 plug-ins + 3 field-pass modes) is confirmed in `D` §3 and re-confirmed against `ShapeForm` at `Runtime/Pyre/Pyre.cs:112-121` (19 cases, two `[Obsolete]`). Only the *prose label* on the MECHANICAL row is wrong (finding 3); the count is right.
- **"Seven of nine scene generators decline the layer fill."** Verified directly: five `UsesFill => false` declarations with `JetFormBase` covering three. Exact.
- **"About 32 authored effects / 79 layers."** Confirmed via `D` §2.2 and my own census.
- **"78% of all authored layers are a plain disc."** Re-measured independently: **238 of 305 = 78.0%** (`D` got 237/305 = 77.7%; either rounds to 78%).
- **"Grouping used on zero layers."** Re-measured: `matteEnabled: 1` = 0, `heightFromChannel >= 0` = 0, `clipByChannel >= 0` = 0, across all 305. Zero is zero. Only the denominator is presented inconsistently (finding 12).
- **"Melt-into-one-blob used on 20 layers, never the mask-fed variant."** Confirmed in `D` §3 (coalesce:1 = 12, coalesce:2 = 8, mask-fed = 0).
- **~10,600 lines behind the plug-in contract.** `F` case 7. Not re-counted, but "roughly ten thousand" is safely inside any plausible margin.

**The "already exists" claims — the load-bearing half of the draft.** All four verified in source:
- **Border stage**: `borderEnabled` / `borderWidth` (a `ZUIValue`, so animatable) / `borderFill` (a full fill) / `borderOverMatte` on the layer; a chamfer inside-distance transform; a **separate returned buffer**; the design comment stating the separate-buffers principle in the model's own words; and the six-form gate at `Runtime/Pyre/PyreRenderer.cs:950-952` — Disc, Crescent, Ring, Streak, Star, Polygon. Every element the draft claims is there. ✅
- **Grouping**: `MatteCombine { Max, Add, Subtract }` into four float planes, plus a consumer that discards its own shape and colours the combined plane through one fill with relief lighting. Present and unused. ✅
- **Fill abstraction, closed**: `ZuiFill` is a `[Serializable] class`, not `[SerializeReference]`; `Evaluate(float life, float u, float v)` takes no substance argument. ✅ Its use outside Pyre (`Runtime/Lathe/LatheSurfaceFill.cs`, TextSplash, backgrounds) makes the draft's blast-radius warning correct.
- **Substance already exists, unnamed**: `PixelInfo` carries `wx/wy`, `crossFrac`, `life`; two effects read `crossFrac` (Tint, Voronoi crack). ✅ Only the *count of its meanings* is wrong (finding 2).
- **Extrusion is the genuinely new one**: one worked instance (Text's 8-slab march) plus two unrelated height mechanisms. ✅

**Slice 1's safety, which I expected to break and could not.** I censused every saved layer for `borderEnabled: 1`: **exactly one of 305**, and it is on a flat, non-plug-in form — i.e. a layer that *already* renders its border today. **Ungating the border stage changes zero saved renders.** The draft's claim is stronger than it knew; it should say so (see finding 22's replacement wording). The separate concern is not saved assets but the double-rim on Text/solids/Fire.

**The lit-solids correction of `PYRE_GUG.md` §9 is properly justified, not papered over.** §6 names the first report, states what it assumed and why, and gives the evidence that overturns it (five existing fills, the code's own `matFill` / *"material fill"* naming, and a full authored light rig). This is the right way to do a correction. Only §1's compression overshoots (finding 19).

**The four hard cases in §7 are real and correctly identified**, and I could not find a fifth of comparable weight that E or F flags and the draft ignores — except E §4.3 (finding 25). §7.2 (the swarm), §7.3 (glows), §7.4 (fill must become a new type), §7.6 (antialias-after-colouring) all check out against the cited code and against E and F, and none is softened.

**The two named counterexamples survive.** Chromatic aberration and Drop shadow genuinely have no shape/fill/border role, so the post stage genuinely must survive. Edge warp is genuinely dead (`EdgeOffset`/`EdgeSoftness` have no callers repo-wide, re-confirmed by `D` §3) and a real border stage genuinely revives it.

**The live-bug claim is faithfully compressed.** The draft carries `F`'s confidence level intact — *"the code path is confirmed by reading; it has not been reproduced in the editor, and it should be before it is fixed"* — matching `F` case 9's *"Reproduce before fixing"* and its own "could not determine" #2. Only the generator count is off by one (finding 34).

**The verdict itself survives.** "Frame, not replacement", the seven-slice additive route, and the discoverability warning in §10 all hold up. I could not construct a case for a rewrite from anything in E, F, or the code: the ~10,600 lines of bit-exact ports, the order-of-operations dependencies in §7.6, the ≈32-effect authored corpus, and the fact that the plug-in contract can be extended additively (`F` case 7's `virtual bool ProducesSubstance => false`) all point the same way. **The route's *ordering* is the part that needs correcting, not the route** — see findings 8, 20, 22 and 26, which together say: slice 1 needs a per-form default, slice 3 is not the free one, and slices 4-5-6 are more coupled than the list implies.

**The word choices survive.** "Fill" over material/texture, "Group" rather than a fourth "fuse", "Substance" as the internal name, and extrusion as a shape op rather than a fourth kind — each is consistent with the evidence and with `C-shaper3d.md`'s warning about the "layer" collision, which §9 correctly reuses one level down.
