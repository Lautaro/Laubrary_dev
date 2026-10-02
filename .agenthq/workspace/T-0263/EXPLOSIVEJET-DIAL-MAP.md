# EXPLOSIVEJET-DIAL-MAP — T-0263

Explosive Jet's own ~90 dials (detonation / fracture / chunks / gobs / dust mechanics), the ones T-0255 skipped.
Files: `Runtime/Pyre/Forms/Kiln/Jet/ExplosiveJetForm.cs` (unchanged except the pre-existing `variant` caption from an
earlier pass) and `Runtime/Pyre/Forms/Kiln/Jet/ExplosiveJetProgram.cs` (all the settings classes below). Method:
read every render line in `ExplosiveJetProgram.cs` end to end plus the family's `JetFormBase.cs` and the sibling
`RadialJetForm.cs` / `RadialJetProgram.cs` (T-0255's accepted work on the same shared "arc" fields) to keep naming
consistent across the jet family. No serialized field renamed, no default/range/render line touched — attributes
and field order only, per RULES.md section 2. Every `[Tooltip]` already carried the full mechanism in longer
wording (an earlier pass); each new `[ZUILabel]`'s full name is prepended to that Tooltip as `Full name: "…".` so
nothing is lost.

## How grouping works (same two attributes as T-0235/T-0255/T-0262)

- `[ZUILabel("Short name")]` — the ≤13-character caption a 150 px MicroSlider can show beside its value.
- `[ZUIGroup("Box name", Tooltip = "…", Advanced = true)]` — the titled sub-card the dial lands in; `Advanced`
  folds the box (or, set per-dial, folds just that dial into the box's tail) to the end of the card on first sight.
  A box's Tooltip is set once, at the group's first member.

Six of Explosive Jet's settings classes are already their own titled sub-card structurally (one C# class = one box,
per the file's header comment: "Blasts … Fracture, Fracture 2, Flash, Chunks, Gobs, Dust"), so each field inside them
carries `[ZUIGroup("<that box name>")]` explicitly (never relying on the class alone) so the grouping does not
depend on unverified implicit behaviour. Explosive Jet's own *flat* dials (not already boxed by a nested class) are
split by concern into three new boxes: **Arc shape** (mirrors Radial Jet's identical shared fields, same names),
**Blasts** (the detonation timing dials, alongside the schedule list itself), and **Death** (how the body dies).

## Arc shape (`ExplosiveJetSettings`, gen 3 fields — identical fields to Radial Jet, same captions for family consistency)

| serialized | old label | new label | group | effect |
|---|---|---|---|---|
| `bias` | Bias | Angle bias | Arc shape | Angle-distribution power across the arc: 1 = uniform; > 1 biases towards the aim. Wide arcs (Spread ≥ 60°) stratify per blast. |
| `srcR` | Src R | Burn radius | Arc shape | Birth radius, canvas widths: > 0 = the gas leaves a ring rather than a point. |
| `swirl` | Swirl | Swirl | Arc shape | Degrees a puff is carried around the seat over its life (a vortex, lash's hooked arms). |
| `spin` | Spin | Pattern spin | Arc shape | Whole turns per loop the emission pattern rotates. |
| `lobes` | Lobes | Tongue count | Arc shape | Gathers the arc into N tongues with real gaps. 0 = an even sheet. |
| `lobeDepth` | Lobe Depth | Tongue clump | Arc shape | How hard the tongues clump, 0..0.95. |
| `lobeKick` | Lobe Kick | Tongue kick | Arc shape | Extra travel on a lobe axis against between them. |
| `rootK` | Root K | Lump count | Arc shape **(advanced)** | With Src R > 0 and no schedule: lumps round the birth circle. |
| `ringFlat` | Ring Flat | Rings face-on | Arc shape | Rings seen face on instead of the edge-on O. |
| `ringArc` | Ring Arc | Ring arc | Arc shape | Half-angle of a flat ring, degrees — a directional blast's leading front. (Radial Jet has no equivalent field.) |
| `warpSpin` | Warp Spin | Turb. spin | Arc shape **(advanced)** | Whole turns per loop the polar noise texture rotates. |

## Blasts (the schedule + detonation timing, `ExplosiveJetSettings` + `ExplosiveBlast`)

| serialized | old label | new label | group | effect |
|---|---|---|---|---|
| `blasts` (list) | Blasts | Schedule | Blasts | The authored list of detonations; empty = a steady stream. |
| `blastSpan` | Blast Span | Blast span | Blasts | Birth times of a blast's slots spread over this much of the loop. |
| `blastSkew` | Blast Skew | Blast skew | Blasts | Birth offset = span·u^skew: > 1 piles births at the front (hard attack, ragged tail). |
| `blastFront` | Blast Front | Blast front | Blasts | Extra speed for the first gas out — a front that runs away from the body. |
| `velSpread` | Vel Spread | Vel spread | Blasts | > 0 gives the fireball a long velocity tail so the slow gas fills the middle instead of leaving a hole. |
| `swell` | Swell | Swell | Blasts | Radius gained per unit age — closes the centre of a fireball filled by Vel Spread. |
| `ExplosiveBlast.at` | At | Blast at | Blasts | Loop phase this detonation fires at. |
| `ExplosiveBlast.pow` | Pow | Violence | Blasts | This blast's violence — scales speed, amplitude, flash, ring, chunks, gobs, dust and sparks. |
| `ExplosiveBlast.share` | Share | Share | Blasts | This blast's share of the slots, relative to the others. |
| `ExplosiveBlast.offX` | Off X | Seat X | Blasts | Seat offset across the source frame. |
| `ExplosiveBlast.offY` | Off Y | Seat Y | Blasts | Seat offset down the source frame. |

## Death — how the body dies (`ExplosiveJetSettings`, gen 5 fields)

| serialized | old label | new label | group | effect |
|---|---|---|---|---|
| `hold` | Hold | Hold | Death | Holds the amplitude up and drops it late — the delay that leaves the body solid before it contracts. |
| `shrink` | Shrink | Shrink | Death | Fraction of radius lost by end of life, weighted by lead — collapses outside-in. |
| `shrinkAt` | Shrink At | Shrink at | Death | The age the contraction starts at. |
| `leadDie` | Lead Die | Lead die | Death | The fast gas dies first — the outer shell expires while the slow middle still burns. |
| `opaq` | Opaq | Solidity | Death | Exponent on the ramp's opacity ceiling — pushes mid-ramp towards solid without losing the soft edge. |
| `shedSwell` | Shed Swell | Shed swell | Death | Shed gas dies the other way: it thins and spreads while the body contracts. |

## Fracture — the first crack (`ExplosiveFracture`)

| serialized | old label | new label | group | effect |
|---|---|---|---|---|
| `chance` | Chance | Crack chance | Fracture | Probability a detonation cracks, rolled per blast. 0 = never. |
| `pieces` | Pieces | Pieces | Fracture | Pieces the body is cut into (uneven widths). |
| `at` | At | Crack at | Fracture | Age the crack opens at. |
| `open` | Open | Crack open | Fracture | How far each piece closes towards its own bisector — the wedge-shaped half of the gap. |
| `kick` | Kick | Piece kick | Fracture | Per-piece extra travel, zero-mean — pieces separate from each other. |
| `spin` | Spin | Piece spin | Fracture | Degrees a piece turns as it goes. |
| `grip` | Grip | Gas grip | Fracture | Share of gas that follows its piece; the rest bridges the crack as wisps. |
| `rot` | Rot | Crack rotate | Fracture **(advanced)** | Turns the crack pattern round the arc so every draw does not split the same axis. |
| `drift` | Drift | Piece drift | Fracture | Per-piece translation in its own random direction. |
| `stagger` | Stagger | Stagger | Fracture | A piece's crack is delayed by up to this much life — the break runs through the mass. |
| `cut` | Cut | Crack cut | Fracture | The constant-width half of the gap — reaches the middle, not just an angle. |
| `body` | Body | Piece body | Fracture | 0 = only the shell separates; 1 = the whole piece leaves, gas and all. |

## Fracture 2 — the second crack, whole box folded Advanced (`ExplosiveFracture2`)

Expert-only refinement on top of Fracture (splitting already-broken pieces again); folded entirely, matching how
T-0262 folded Orb's whole "Cleanup" box rather than dial-by-dial.

| serialized | old label | new label | group | effect |
|---|---|---|---|---|
| `chance` | Chance | Crack2 chance | Fracture 2 **(advanced)** | Probability a piece breaks again, rolled per piece. 0 = one crack only. |
| `at` | At | 2nd crack at | Fracture 2 **(advanced)** | Age the second crack opens at. |
| `open` | Open | 2nd open | Fracture 2 **(advanced)** | The second crack's angular closing. |
| `cut` | Cut | 2nd cut | Fracture 2 **(advanced)** | The second crack's constant-width cut. |
| `kick` | Kick | 2nd kick | Fracture 2 **(advanced)** | Per-sub-piece extra travel. |
| `drift` | Drift | 2nd drift | Fracture 2 **(advanced)** | Per-sub-piece translation. |
| `spin` | Spin | 2nd spin | Fracture 2 **(advanced)** | Degrees a sub-piece turns. |
| `stagger` | Stagger | 2nd stagger | Fracture 2 **(advanced)** | Per-sub-piece delay of the second crack. |

## Flash — the instant (`ExplosiveFlash`)

| serialized | old label | new label | group | effect |
|---|---|---|---|---|
| `radius` | Radius | Flash radius | Flash | Radius of the detonation core. 0 = none. |
| `amp` | Amp | Heat | Flash | Heat of the flash, × the blast's violence. |
| `life` | Life | Duration | Flash | Life of the flash as a fraction of the LOOP (brief). |
| `grow` | Grow | Growth | Flash | Growth of the core over its life. |
| `elong` | Elong | Elongate | Flash **(advanced)** | > 1 stretches the flash along the aim — a muzzle flash. |

## Chunks — burning fragments (`ExplosiveChunks`)

| serialized | old label | new label | group | effect |
|---|---|---|---|---|
| `count` | Count | Count | Chunks | Fragments per blast. |
| `radius` | Radius | Radius | Chunks | Fragment radius. |
| `reach` | Reach | Reach | Chunks | Travel as a multiple of Reach — they outrun the gas. |
| `drag` | Drag | Drag | Chunks | Exponential drag; lower than the gas's. |
| `life` | Life | Life | Chunks | Life as a multiple of Life. |
| `sag` | Sag | Sag | Chunks | Fall by end of life. |
| `trail` | Trail | Trail | Chunks **(advanced)** | Blobs of trail drawn behind each fragment. |
| `amp` | Amp | Heat | Chunks | Heat of a fragment. |
| `wide` | Wide | Arc width | Chunks | Its arc as a multiple of the gas arc. |

## Gobs — shed burning mass (`ExplosiveGobs`)

| serialized | old label | new label | group | effect |
|---|---|---|---|---|
| `count` | Count | Count | Gobs | Gobs per loop. |
| `radius` | Radius | Radius | Gobs | Gob radius at birth. |
| `reach` | Reach | Reach | Gobs | Travel as a multiple of Reach. |
| `drag` | Drag | Drag | Gobs | Exponential drag; decelerates hard. |
| `life` | Life | Life | Gobs | Life as a multiple of Life. |
| `amp` | Amp | Heat | Gobs | Heat of a gob. |
| `swell` | Swell | Swell | Gobs | It dissipates: radius grows while heat falls. |
| `sag` | Sag | Sag | Gobs | Fall by end of life. |
| `wide` | Wide | Arc width | Gobs | Its arc as a multiple of the gas arc. |
| `early` | Early | Early birth | Gobs | Gobs born inside this fraction of the blast span, piled at the front. |
| `trail` | Trail | Trail | Gobs **(advanced)** | Blobs of trail behind each gob. |

## Dust — shed off the body's own skin (`ExplosiveDust`)

| serialized | old label | new label | group | effect |
|---|---|---|---|---|
| `count` | Count | Count | Dust | Particles per loop, born on the body's own surface. |
| `radius` | Radius | Radius | Dust | Particle radius. |
| `from` | From | Born from | Dust | Earliest body age a particle comes off at. |
| `to` | To | Born to | Dust | Latest body age a particle comes off at. |
| `bias` | Bias | Birth bias | Dust **(advanced)** | > 1 piles the births at From. |
| `where` | Where | Skin depth | Dust **(advanced)** | The body radius they come off, as a share of the front's travel. |
| `reach` | Reach | Reach | Dust | A particle's own travel after leaving. |
| `drag` | Drag | Drag | Dust | Exponential drag. |
| `life` | Life | Life | Dust | Life as a multiple of Life. |
| `amp` | Amp | Heat | Dust | Heat of a particle. |
| `sag` | Sag | Sag | Dust | Fall by end of life. |
| `scatter` | Scatter | Scatter | Dust | Radians of spray either side of straight out. |
| `wide` | Wide | Arc width | Dust | Its arc as a multiple of the gas arc. |
| `trail` | Trail | Trail | Dust **(advanced)** | Blobs of trail behind each particle. |

## Container fields left without their own ZUILabel/ZUIGroup

`fracture`, `fracture2`, `flash`, `chunks`, `gobs`, `dust` on `ExplosiveJetSettings` are references to the nested
settings classes above; each one's own box title comes from the `[ZUIGroup]` names on its *inner* fields (as in
T-0235's Orb precedent), so the container field itself keeps only its original `[Tooltip]` — adding a label/group
to the container too would either fight the inner grouping or create a redundant nested box.

## Naming rules applied (kept consistent with T-0235/T-0255/T-0262)

- **`…amp` → "Heat"** in every debris box (Chunks/Gobs/Dust/Flash) — each box is its own titled card so "Heat"
  alone is unambiguous per box, matching Orb's "…Amp → …brightness" convention adapted to a fire/heat vocabulary.
- **`…trail` → "Trail", folded Advanced** in Chunks/Gobs/Dust — a fine-tuning count of trailing blobs, not a
  primary creative dial.
- **The Arc shape fields reuse Radial Jet's own new captions verbatim** (Angle bias, Burn radius, Swirl, Pattern
  spin, Tongue count/clump/kick, Lump count, Rings face-on, Turb. spin) since they are the identical serialized
  fields shared by the whole jet family — a caption should not read differently in two variants of the same knob.
- **Fracture 2 folded whole**, not dial-by-dial, since every one of its 8 dials is equally a refinement nobody
  touches before Fracture itself; folding piecemeal would have left half a folded box open for no reason.
