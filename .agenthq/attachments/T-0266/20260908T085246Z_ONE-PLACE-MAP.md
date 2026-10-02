# T-0266 — One place per concept: placement / size / colour / opacity / time / seed

Phase 1 research only (no code edits). Measured against HEAD on `feat/shaper` (worktree
`D:\UNITY\Laubrary Dev - Shaper`) 2026-09-08. Every `file:line` below is a fresh grep/read against that tree,
not carried over from the appendices.

Files inventoried:
- `Editor/Shaper/ShaperWindow.cs`, `.Sections.cs`, `.Lights.cs`, `.Cherry.cs`, `.Bake.cs`, `.Preview.cs`,
  `.OpenFor.cs`, `ShaperCompositeSourceUI.cs`, `ShaperShapePicker.cs`, `ShaperPreviewOverlays.cs`
- `Editor/PyreShaper/PyreFormShaperUI.cs`, `PyreLayerShaperUI.cs`
- `Editor/Pyre/PyreShapeCards.cs` (as hosted inside Shaper)
- Reflected fields (`[ZUILabel]`) over every file under `Runtime/Pyre/Forms/**` (the nine composite
  generators: ArcBurst, ForkBlast, Inferno, Jet/ExplosiveJet/RadialJet, Orb, PlasmaBloom, Torch) and
  `Runtime/PyreShaper/*.cs`

**Two tiers, kept separate throughout this map, because collapsing them into one list is itself a mistake:**

- **Tier 1 — macro/node-level controls.** What every node kind shows for "where is it, how big overall, what
  colour, how see-through, when does it happen, what seeds its randomness" — the concepts the card names.
  These are the ones that can genuinely collide (same node, two controls, same meaning).
- **Tier 2 — generator-internal sub-parameters.** A single composite generator (e.g. ArcBurst) declares up to
  ~190 of its own fields, several of which are *locally* about position/size/colour/opacity/time/seed inside
  that generator's own draw (a "Nozzle X", a "Fade start" per layout, a "Tear seed"). These are legitimate,
  are almost always hidden from each other by `[ZUIShowIf]` (only one layout's settings show at once), and
  folding them into the macro concept would either be wrong (they are relative to the shape, not the
  document) or destructive (they are `Runtime/Pyre/Forms/**` fields — RULES.md forbids renaming/removing
  them, only attribute/order changes are allowed). Tier 2 is reported by representative sample per generator
  family, not exhaustively (775 authored fields across 9 generators is not a list to hand-audit line by
  line); the recommendation for Tier 2 is a naming/tooltip convention, not a fold.

---

## PLACEMENT (translate / centre / offset / nose / spawn centre)

### Tier 1 — node-level

| Control | File:line | Card | Engine effect |
|---|---|---|---|
| **Position** box (X / Y / Z) | `Editor/Pyre/PyreShapeCards.cs:238` (Pyre's own, hosted), `Editor/Shaper/ShaperWindow.cs:1117` (Shape card's own, ex-"Transform") | Shape card | Node's translate — the placement every node kind is meant to share, per T-0265 |
| **Position Z** | `Editor/Shaper/ShaperWindow.Lights.cs:203` | Lights card | A *light's* Z depth, not a shape's placement — different subject, correctly separate |
| **Centre X / Centre Y** (Solid) | `Editor/Shaper/ShaperWindow.Sections.cs:309-311` | Solid's own Size/Centre/Aspect/Depth row | `s.centreX` / `s.centreY` on `ShaperSolidDial` — a Solid places **itself** with its own dials (comment at `ShaperWindow.cs:974`: "A Solid node places, turns and sizes itself with its own dials (Centre, Size, Yaw/Tilt/Roll)…") |
| **Centre X / Centre Y** (Gradient fill) | `Editor/Shaper/ShaperWindow.Sections.cs:598-601` | Fill card, Radial/Angular gradient | `f.gradientCentreX/Y` — normalised to the shape's own half-extent (fraction, `-2..2`), NOT canvas pixels. Different subject entirely (where a gradient radiates from inside the shape), already correctly named and scoped (T-0257 tooltip: "as a fraction of the shape's own half-width") |
| **Anchor** | `Editor/Pyre/PyreShapeCards.cs:516` | Legacy ShapeForm (Streak?) | Need confirmation from T-0265 (file is mid-edit); likely a streak's anchor end, not node placement |
| **Emitter offset (px)** | `Editor/Pyre/PyreShapeCards.cs:675` | Legacy ShapeForm (Fireball?) | Already correctly qualified as an *offset from the node's own placement*, not a second placement |
| **Offset U / V, Offset** | `Editor/Shaper/ShaperWindow.Sections.cs:696-697, 732, 846-848` | Fill card (Texture / Strip fills) | UV offset into a texture/strip — a texel-space concept, not a placement concept; false positive from the word "offset", no action |

### Decision — PLACEMENT

**ONE home = the Shape card's "Position" box**, already in progress under T-0265 ("make the Shape card's
Position box the one placement for every node kind, Solids included; hide Pyre's own Position box on hosted
cards"). This map does not contradict that — it adds the one gap T-0265's card doesn't already name:

- **FOLD: Solid's own `Centre X` / `Centre Y` (`ShaperWindow.Sections.cs:309-311`) into Position.** Once
  T-0265 wires a Solid's placement through the shared Position box, these two `SolidVal` dials become a
  second, differently-named placement control for the same node — exactly the "two controls, same meaning,
  different name" case RULES.md is watching for. `Size` (radius), `Aspect`, `Depth` on the same row are NOT
  placement (they size/shape the Solid) and stay.
- **NO action for Gradient-fill Centre X/Y** — different subject (in-shape gradient origin, unit-fraction),
  already tooltip-qualified. Keep the "Centre" word (it reads correctly in context) but do not let it drift
  toward being read as a second node placement — no change needed, just noted so nobody "fixes" it into the
  Position box by mistake.
- **Tier 2 generator placement fields** (`JetFormBase.cs:27,30` "Nozzle X/Y", `OrbForm.cs:61` "Nose X",
  `ForkBlastForm.cs:73` "Fill centre") are *emission-point-within-the-shape* dials — relative to the node's
  own Position, one level down. Convention to apply (not a fold, since these are `Runtime/Pyre/Forms/**`
  fields — attribute-only edits allowed): every such dial's tooltip should say explicitly that it is relative
  to this shape's own placement, not a second Position — `OrbForm.cs:61`'s tooltip is truncated in this
  extraction ("Full name: \"") and should be checked/completed in phase 2.

---

## SIZE (radius / half-width / size / scale / reach)

### Tier 1 — node-level

The bulk of SIZE hits (44 in Editor UI files, most of the 83 in Tier-2 reflected fields) are legitimate:
**one dial set per primitive/solid/form kind**, switched mutually exclusively by `s.form`/`s.kind`/`p.kind`,
so only one is ever on screen for a given node. Representative rows:

| Control | File:line | Card |
|---|---|---|
| Half width, Radius X/Y, Half length, Radius, Length, Base width | `Editor/Shaper/ShaperWindow.cs:1007-1093` | Primitive dials (per shape kind, e.g. Rect/Ellipse/Capsule/Star/Diamond) |
| Size | `Editor/Shaper/ShaperWindow.Sections.cs:307` | Solid's own Size (radius) |
| Size (px) | `Editor/Pyre/PyreShapeCards.cs:151, 476` | Legacy ShapeForm particle radius — the "scale driver for every form" (comment at :151) |
| Length (px) / Width (px) | `Editor/Pyre/PyreShapeCards.cs:508,514` | Streak's own Length/Width (explicitly replaces the shared Size row for Streak — comment says so) |
| Line width | `Editor/Pyre/PyreShapeCards.cs:391`, `Editor/Shaper/ShaperWindow.Sections.cs:340,870` | Border/edge line width — a different axis (stroke thickness), not the shape's own size |
| Reach | `Editor/Pyre/PyreShapeCards.cs:715,784`, `ShaperWindow.Sections.cs:733` | Fire/Fireball sim's bounding radius ("whole-layer sims bounded by their Reach radius, not a particle radius" — comment at PyreShapeCards.cs:151) |
| GIF scale, Strip tile size | `ShaperWindow.Bake.cs:124`, `ShaperWindow.cs:1461` | Export/UI-only sizes, unrelated to any shape concept — false positives, no action |

### Decision — SIZE

**No macro-level duplication found.** Every SIZE control found is scoped to exactly one node/fill/form kind
and hidden from its siblings by the same kind-switch that already governs Position/Fill/Border. This matches
the pattern T-0257/T-0258 already established elsewhere (Depth, Loop gap). **No fold recommended.**

One naming gap worth fixing in phase 2 for consistency (not a duplicate, a *inconsistency*): `Scale` appears
in `ShaperWindow.Sections.cs:797` (HeightFieldFill's height multiplier) and `:845` (a second fill kind's own
multiplier) — both correctly scoped, but neither tooltip cross-references that "Scale" here means "multiplies
the sampled value", not "resizes the shape" the way `PyreShapeCards.cs:701`'s "Curl scale" does. Recommend
tightening each tooltip to open with what it scales (already partly done — `:797` "Multiplies the sampled
height" is fine; verify `:845` matches).

---

## COLOUR (fill colour / tint / ramp / generator palette / line / glow)

### Tier 1 — node-level

| Control | File:line | Card | Note |
|---|---|---|---|
| **Fill** (colour source) | `Editor/Pyre/PyreShapeCards.cs` Fill row (near :137), `Editor/PyreShaper/PyreFormShaperUI.cs:74-76` | Legacy ShapeForm / hosted PyreForm generator | Two different types (`ZuiFill` vs the generator's `src.shapeFill`) but same *role*; `PyreFormShaperUI.cs:73` already states the absence rule: "only Inferno and Fork Blast read it" — Fill is **absent** (not drawn) for the other 7 generators that carry their own ramps, which is exactly the CLAUDE.md "absence rule, like Fill on a composite" pattern working as intended |
| Colour (native primitive/Solid fill) | `Editor/Shaper/ShaperWindow.Sections.cs:548-549` | Fill card, Solid-kind fill | Flat colour for `ShaperFillDef` |
| Ramp | `ShaperWindow.Sections.cs:576,634,828,861` | Fill card, one per fill kind (RampByQuantity, second ramp mode, Grain, Rust) | Each behind its own `f.kind` switch — mutually exclusive on screen |
| Tint | `ShaperWindow.Sections.cs:586-587,652-653,700,798-799,831-832,863-864`, `PyreShapeCards.cs:493` | Fill card, per fill/form kind | Every instance's tooltip already says what it multiplies ("Multiplies the sampled ramp colour", "Multiplies the resulting colour") — consistent pattern, correctly per-kind |
| Line colour, Edge glow (colour), Inner glow (colour) | `ShaperWindow.Sections.cs:343-354`, `PyreShapeCards.cs:399-411` | Border/edge card | Distinct subjects (stroke colour vs two different glow layers), already individually qualified |
| Spec tint | `ShaperWindow.Sections.cs:1599` | Lighting card | Tints the *specular highlight* the light rig adds — explicitly NOT a second light colour (comment at `ShaperWindow.Lights.cs:157-159`: "There is no second specular colour on a light — the highlight's tint lives on the receiving layer's Spec tint instead") — this is the resolved form of a colour concept that could have collided and was deliberately kept apart |
| Light colour / ambient colour | `ShaperWindow.Lights.cs:51,157,160` | Lights card | Different subject (a light source's own colour) — correctly separate from a shape's fill colour |

### Decision — COLOUR

**No macro-level duplication found.** The "one Fill per node kind, absent where the generator supplies its
own ramp" rule is already in force and is the correct one home. Tint/Ramp repetition across fill *kinds* is
not a violation — it's the same word reused for the same role behind mutually-exclusive `f.kind`/`s.form`
switches, which RULES.md's own wording ("no two controls **on screen**") permits. **No fold recommended.**

Tier 2 generator palette controls (`ArcBurstForm.cs:100` "Colour bands" — the five-band cel palette) are each
a generator's *own* internal ramp, drawn only when that generator/layout is hosted and its Fill is absent —
consistent with the absence rule above, no conflict.

---

## OPACITY (fade / veil / alpha / opacity / intensity)

### Tier 1 — node-level

| Control | File:line | Card | Engine effect |
|---|---|---|---|
| **Alpha** | `PyreShapeCards.cs:142` | Legacy ShapeForm | "Opacity over the particle's own life (multiplies the final output alpha)" — a life-driven `ZUIValue` envelope |
| **Alpha** | `PyreFormShaperUI.cs:81` | Hosted PyreForm generator | "Overall opacity across this generator's life — multiplied into the picture it paints" — same role, same name, as the row above; the two are mutually exclusive by node kind (Legacy ShapeForm vs hosted PyreForm), so this is the SAME concept correctly reusing the same label, not a duplicate |
| **Fade** (`f.veil`) | `ShaperWindow.Sections.cs:539` | Fill card, every native fill kind | "Multiplies this fill's own transparency — the fade the palette applies on top of whatever edge rule the fill computed" — a DIFFERENT pipeline stage from Alpha above (outer-fill veil vs generator/particle life-alpha); never shown on the same node as Alpha (native primitives/Solids use `ShaperFillDef`, Pyre-hosted forms/generators use `ZuiFill`/generator Alpha instead) |
| **Intensity ×** | `ShaperWindow.Sections.cs:1591` | Lighting response card | Scales how much the LIGHT RIG affects this layer — a lighting-response magnitude, not the shape's own transparency. Tooltip already disambiguates ("Scales the rig's effect on this layer") |
| Ambient intensity, light Intensity | `ShaperWindow.Lights.cs:54, +implicit` | Lights card | Light brightness — different subject from shape opacity, correctly separate |
| GIF "50% opacity" mention | `ShaperWindow.Bake.cs:132` | Bake card | Comment/tooltip prose only, not a control |

### Decision — OPACITY

**No macro-level duplication found; the three real concepts (generator/particle life-Alpha, outer-fill
Fade/veil, lighting-response Intensity) are already at three different pipeline stages, correctly named, and
never co-resident on one node.** The card's five words (fade/veil/alpha/opacity/intensity) map onto exactly
these three homes plus one internal field-name synonym (`veil` is the serialized field, `Fade` is the label —
that's intentional per a prior wave, not a second control). **No fold recommended** for Tier 1.

Tier 2 generator sub-opacity fields (ArcBurstForm has ~20 fade/opacity fields, one to two per `[ZUIGroup]`
"Timing"/"Body & energy" block per layout, e.g. `ghostDeepP`/`dieHi`/`veilP`/`swapEnd` all labelled "Fade
start") were checked for an on-screen collision: **none exists** — each "Fade start" belongs to a different
nested `*Settings` class (`CoreSettings`, `WeaveSettings`, `BoltSettings`, …) shown behind `[ZUIShowIf("layout",
"…")]`, so exactly one is visible for a given `layout` selection at a time. This is a deliberate, correct
reuse of one label across mutually-exclusive layouts (confirmed by reading `ArcBurstForm.cs:104-113` and the
per-layout Timing groups) — not a violation. **No action.**

---

## TIME (frames / lifetime / loop gap / spawn timing / generator frames / cherry)

### Tier 1 — node/document-level

| Control | File:line | Card | Note |
|---|---|---|---|
| **Frame** (transport scrubber) | `ShaperWindow.cs:1379`, `ShaperWindow.Preview.cs:76` | Transport | Playback position — the one place answering "where is playback" |
| **Lifetime** (layer's frame window) | `ShaperWindow.cs:706-710` | Layer card | `Z.MicroMinMax` — "the SELECTED layer's… frame lifetime window — the frames it contributes to" |
| **Loop gap** (transport) | `ShaperWindow.cs:1358-1362` | Transport | `document.loopDelaySeconds` — gap between passes through the FRAMES. Already resolved: comment at `ShaperWindow.Cherry.cs:269` records that this and Cherry's own gap used to collide under the same label and were split (T-0257) |
| **Cherry loop gap** | `ShaperWindow.Cherry.cs:272,336` (comment at 269-271) | Cherry panel | `document.cherryLoopDelaySeconds` — gap between passes through the CHERRY SEQUENCE, explicitly disambiguated in its own tooltip from the transport's Loop gap |
| **Spawn timing** box / **Timing** field | `ShaperWindow.Sections.cs:1316,1323` | Swarm card | When each swarm instance is born/dies — a third, clearly different timing concept (per-instance birth/death model, not playback or layer window) |
| **"Generator frames" — REMOVED** | `PyreFormShaperUI.cs:60-68` (comment) | — | T-0254 already deleted a hosted generator's own second frame-count axis; `src.frames` now silently mirrors `ctx.FrameCount` every time, "so a hosted generator has exactly one lifetime." This is exactly the fold this concept needed, and it is already done — no further action |

### Decision — TIME

**Already resolved to one home per sub-concept**, confirmed by re-reading the code (not just trusting the
comments): playback position = Frame/scrubber; a layer's window on the document clock = Lifetime; the
document's own loop gap vs the cherry sequence's own loop gap are two different fields, both correctly
labelled and cross-referenced; a swarm's birth/death model = Spawn timing/Timing; a hosted generator's frame
axis was removed entirely and now silently tracks the document (T-0254). **No fold recommended.**

Tier 2 generator lifetime fields (`ForkBlastForm.cs:84,121` "Puff lifetime"/"Gob lifetime",
`PyreJetEngine.cs:152,201,224` "Puff/Shed/Ring lifetime", `OrbForm.cs:410` "Mote lifetime") are each a
sub-effect's own life **as a fraction of the generator's own clock** (tooltips already say so: "as a fraction
of the blast's own clock", "as a multiple of Puff life") — internally consistent, no collision with the
document/layer Lifetime above. **No action.**

---

## SEED (document / swarm / cherry / generator)

### Tier 1 — document-level, already fully resolved (T-0257)

| Control | File:line | Scope |
|---|---|---|
| **Document seed** | `ShaperWindow.cs:603-612` | "The seed every deterministic draw in this document derives from" — the base seed |
| **Swarm seed** | `ShaperWindow.Sections.cs:1159-1163` | Per-swarm instance jitter, explicitly "Varies the jitter without changing its character" |
| **Cherry seed** | `ShaperWindow.Cherry.cs:336-339` | Per cherry-frame slot's own MultiFrame candidate re-roll, explicitly "independent of the document seed, so one slot can be re-rolled without disturbing the others" |
| **Grain seed** | `ShaperWindow.Sections.cs:808` | `f.steelSeed` — the Steel/Rust fill kind's own pattern variation, scoped to that one fill kind |

A comment already left at `ShaperWindow.Cherry.cs:335` records this exact cleanup: "one of three unrelated
'Seed's (document, swarm, this one), now each qualified" (Grain seed is a fourth, fill-kind-scoped one, not
part of that trio).

### The "generator" seed named on the card

Searched `Editor/PyreShaper/PyreFormShaperUI.cs` and `PyreLayerShaperUI.cs` for any generator-level seed
control: **none exists**. `Pyre.cs:1187`'s `public int seed = 1234` is a *Pyre asset's own* top-level seed
(a different, non-Shaper asset type) and is not surfaced anywhere in the Shaper window. A hosted composite
generator's own randomness (`ArcBurstForm`'s `Re-strike` re-roll, `OrbForm`'s Tear/Boil/Veil/Granule/Break-up/
Charge seeds) is entirely Tier 2 — each is a *named variation offset* relative to the generator's own draw
(e.g. "Boil seed": "A different random draw of the same boiling"), not a second base seed. This means
"generator" seed, as named on the card, is **not a missing/duplicate control** — a hosted generator's
determinism is inherited from Document seed (BC-1.3: `UnityEngine.Random`/`System.Random` are banned in
generator paths; the deterministic hash chain runs off `seed`/`frame`), with each Tier 2 sub-seed offsetting
that same chain for one named part of the effect.

### Decision — SEED

**No fold needed — already one home per legitimate scope (document / swarm / cherry / grain), and "generator"
seed is correctly NOT a fifth authored dial but an inheritance from Document seed.** The only phase-2 action
is verification, not code: confirm (by reading, not assuming) that every Tier 2 generator sub-seed's tooltip
makes clear it is a variation OFFSET on top of the document seed, not an independent seed — `OrbForm.cs`'s
seven seed tooltips already do this correctly ("A different random draw of the same X"); spot-check the other
eight generator families' seed fields the same way in phase 2 if any exist (Jet/Torch/PlasmaBloom/Inferno
were not enumerated for seed fields in this pass — do that check in phase 2, it's a read, not an edit).

---

## Phase 2 — done (2026-09-08)

Restriction honoured: `Editor/Shaper/**` and `Editor/PyreShaper/**` were NOT touched (T-0265 already landed the
placement fold there — Position box on every kind, Solid Centre/Roll and Pyre Offset/Spin gone, Scale
tooltip, "Spawn centre"). Phase 2 here is `Runtime/Pyre/Forms/**` attribute-only work, permitted under
RULES.md rule 2 (Tooltip changes only; no field rename, no default change, no render code touched).

1. **`OrbForm.cs:60`** ("Nose X") — tooltip now ends: "This is an offset WITHIN the orb's own drawing frame,
   relative to the node's own Position — it is not a second placement; the node's Position box still moves
   the whole orb."
2. **`Jet/JetFormBase.cs:25`** ("Nozzle X") and **`:29`** ("Nozzle Y") — same clarification added to both.
3. **Seed spot-check, Jet / Torch / Plasma Bloom / Inferno:** grepped every `[ZUILabel(...Seed...)]` and every
   plain `public ... seed` field across `Jet/*.cs`, `PyreTorch.cs`/`TorchForm.cs`, `PlasmaBloomForm.cs`/
   `PyrePlasmaBloom.cs`, `InfernoForm.cs`/`PyreInferno.cs`. **Result: none of these four families exposes an
   authored/reflected seed field at all** — unlike Orb (which has 7 named variation-offset seeds: Tear/Boil/
   Veil/Granule/Break-up/Charge), Jet/Torch/PlasmaBloom/Inferno's `seed` occurrences are all internal plumbing
   parameters (`PyreJetEngine.cs:309`, `PyreTorch.cs:68`, `PyreInferno.cs:253`, etc.), never `[ZUILabel]`'d,
   so there is nothing on screen to mislabel. Traced the actual value at runtime:
   `PyreLayerCompositeSource.cs:116` (`spec.seed = unchecked((int)seed)`) and `PyreFormCompositeSource.cs:121`
   (`int sd = unchecked((int)seed)`) both take `seed` from the caller — the Shaper document's own seed chain,
   the same one `Document seed` (`ShaperWindow.cs:603`) authors. **No independent seed found in any of the
   four families.** No tooltip change was possible or needed (no exposed field exists to annotate); nothing
   left to do here — reporting the negative result is the deliverable.

## Summary of phase-2 actions (pending PM sequencing / "go") — SUPERSEDED, see "Phase 2 — done" above

1. **PLACEMENT — the only real fold in this map:** remove/hide the Solid's own `Centre X`/`Centre Y` dials
   (`ShaperWindow.Sections.cs:309-311`) once T-0265's shared Position box drives a Solid's placement, so the
   Solid's Size/Aspect/Depth row keeps only shape-defining dials. **Depends on T-0265 landing first** — do
   not touch these lines until T-0265's handover confirms Position is wired for Solids.
2. Complete/verify the truncated tooltip on `OrbForm.cs:61` "Nose X" (and similarly `JetFormBase.cs:27,30`
   "Nozzle X/Y") to state explicitly it is relative to the node's own Position, not a second placement.
   Attribute-only edit, permitted under RULES.md rule 2.
3. Spot-check Jet/Torch/PlasmaBloom/Inferno generator families for any seed field not yet confirmed to read
   as a variation-offset-on-Document-seed (read-only check; likely no code change).
4. No other concept in this pass needs a fold, rename, or removal — SIZE, COLOUR, OPACITY, TIME and SEED are
   already each at one home per node kind, correctly disambiguated by prior T-0257/T-0258/T-0254 waves. This
   audit's marginal value is (a) confirming that with fresh line numbers rather than trusting the comments,
   and (b) the one Solid-placement fold above.
