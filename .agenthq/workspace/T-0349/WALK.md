# UC6 walk: Floating Disc break-up with interleaved depth (T-0349)

Walked 2026-09-16 on the live Laubrary Dev editor (dev @ fbf3e0a2), using the same harness as UC4/UC5 (T-0347, T-0348).
- **Recipe:** `Assets/Demos/ChunksDemo/UseCases/UC6 Disc Breakup.asset`.
- **Zoe-triggered break-up:** saved on the **duplicate** `UseCases/UC Floating Disc (walk copy).asset`. Its Death reaction's library row now fires UC6, and the original private row is switched off. I did not wire it onto the owner's `Assets/Zoetrope/Floating Disc.asset`, because that would have changed that asset. The owner's Zoe is untouched (git shows it clean).

**The owner's sentence:** "fracture the flying disc zoe, have a pyre fire off behind the fragments. Fragments are flung in different directions. Spray particles in 360 direction. Several smaller pyre fire off in between the fragments. So the layering should be something like: Big pyre / Fragment 1 / Pyre 1 / Fragment 2 / Pyre 2 / Particles / Fragment 3."

**Which end is front.** The owner says the big Pyre fires *behind* the fragments, and Big pyre is at the top of the list. So I read the list as **top = back, bottom = front**: Fragment 3 is the frontmost thing, and the particles sit behind it but in front of everything else. This matches the Layer Plan's own convention, where the first row draws furthest back.

## What was built (the part that can be built today)

| # | Card | Settings | Layer slot |
|---|---|---|---|
| 0 | Layer Plan | slots, back to front: Big pyre, Fragments, Small pyres, Particles | — |
| 1 | Pyre Blast "Big pyre" | Rose Blast Plus, Single, scale 2.2, on screen 1.67 s | Big pyre |
| 2 | Fragment Fracture "Disc pieces" | Source: Floating Disc Zoe, 3 pieces, speed 3–6, radial (spread 180), seed 7 | Fragments |
| 3 | Palette Splash "Particles up" | 20–28 specks, direction 90, spread 180 | Particles |
| 4 | Palette Splash "Particles down" | the same, direction 270 | Particles |
| 5 | Pyre Blast "Small pyres" | SparkleBurst Plus, Ring of 3, radius 0.9, stagger 0.08, scale 0.5, delay 0.1 | Small pyres |

The clock runs 2.00 s. The Timing section appeared with five lanes (`01`).

The "360° spray" is two Palette Splash cards pointing opposite ways. A single Palette Splash tops out at a half circle (UC4 J3).

**What cannot be built:** the interleaving. The best available is a band order: Big pyre < all fragments < all small Pyres < all particles.

## The walk

1. **New → "UC6 Disc Breakup" → Create, then Add capability → Layer Plan.** The card opens with a Name field (it is unclear what naming a plan does) and **Add layer**.
2. **Add layer ×4, then renamed each row** to Big pyre / Fragments / Small pyres / Particles.
   - The row name fields have no label.
   - Every Add layer rebuilds the whole stack (walker A's harness note held: one action per call).
3. **Pyre Blast "Big pyre".** Picked Rose Blast Plus, set Scale 2.2–2.2 (two handles) and On screen 1.67 (found by measuring the Pyre; UC2 G2).
   - Layer: a radio row `(stack order) | Big pyre | Fragments | Small pyres | Particles` appeared on the card, because a Layer Plan exists. I picked Big pyre.
4. **Fragment Fracture.** Source = Floating Disc, Pieces 3, Speed 3–6, Seed 7, Layer = Fragments.
   - **At this point the owner's list needed "Fragment 1 / Fragment 2 / Fragment 3" to be separate. There is no control for a single piece:** the Layer choice is per card.
5. **Palette Splash ×2** (Particles up at 90°, Particles down at 270°), Layer = Particles.
   - Both cards read their colours from the fracture's *authored* source, `cell_000`, even when a Zoe triggers the burst (UC4 J2).
6. **Pyre Blast "Small pyres".** Pattern Ring, Count 3, Radius 0.9, Stagger 0.08, Scale 0.5, Delay 0.1, Layer = Small pyres.
   - **The same wall:** Layer is per card, so "Pyre 1" and "Pyre 2" cannot sit at different depths.
   - "In between the fragments" cannot be aimed either. The ring's points are at fixed angles, while the pieces fly along their own cut directions.
7. **Scrubbed to 0.05 / 0.3 / 0.7 / 1.2 s** (`02`).
   - The ring points (numbered 1–3), the fracture squares (orange), the specks (all pink, although the two lanes are green and pink) and a faint big-pyre disc all read.
   - **Nothing in the preview shows depth:** which item is in front cannot be told from the stage.
   - The picture is framed small: a large ring guide sets the zoom.
8. **Save Project.** Clean.
9. **Standalone burst in Play mode** (`20`). I captured the sorting order of every renderer the burst spawned (below).
10. **Turned the Layer Plan off with its own On toggle, fired again, then turned it back on** (`22`). This shows the depth each item gets with no plan (below).
11. **Wired UC6 to the Zoe copy.** On the copy's Death row, the library picker was set to UC6 (the UC4 J5 route). Spawned the copy and killed it (`21`).
    - The fragments were cut from the **live** frame `cell_001`.
    - The big Pyre went off behind them, the small ring popped around them, and the specks sprayed.
    - The Zoe's own body renders at order 0, **the same order as the Big pyre** (see D3).

**Step count for a human:** about **74 actions**, plus about 6 to attach the recipe to a Zoe.

| Part | Actions |
|---|---|
| New | 3 |
| Layer Plan (add, 4 × add + rename) | 14 |
| Big pyre | 9 |
| Fracture | 10 |
| Two splashes | 22 |
| Small pyres | 14 |
| Play + save | 2 |

## What depth control exists today — measured

Every spawned renderer is on sorting layer **Default**. Below is `sortingOrder` from a real burst.

**With the Layer Plan** (standalone and Zoe-triggered agree):

| Output | sortingOrder |
|---|---|
| Big pyre (1 Pyre player) | **0** |
| Fragment 1 / 2 / 3 | **10 / 11 / 12** |
| Small pyres, ring points 1 / 2 / 3 | **20 / 21 / 22** |
| Particles (both splash cards) | **30 … 39**, with every particle past the 9th pinned at 39, and the two cards' particles sharing the same numbers |
| The Zoe's own body (copy) | 0 |

**Without the Layer Plan** (same recipe, plan switched off):

| Output | sortingOrder |
|---|---|
| Big pyre (stack position 1) | 501 |
| Fragments | 502 / 503 / 504 |
| Particles up | 503 + i (503 … 531) |
| Particles down | 504 + i |
| Small pyres | 505 / 506 / 507 |

The rules behind those numbers, confirmed in code:
- **A slotted output** gets `baseOrder (0) + slotIndex × 10 + clamp(instance index, 0..9)` (`LayerSpec.OrderAt`). So a Layer Plan gives **one band per slot**, and inside a band the instances of one card are sub-ordered by their spawn index.
- **An unslotted output** gets `emitter order (500) + stack position + instance index`, **not clamped** (`ChunkModuleContext.ResolveOrder`). Instances therefore spill into the numbers of the cards after them. The no-plan burst above does interleave, but only by accident: particles tie with fragments and Pyres. Moving a card (walker A's H4) changes all of it.
- **Both slots and unslotted outputs are chosen per card.** No control addresses "fragment 2" or "ring point 1". Fragments are consecutive integers (10, 11, 12), so nothing can ever sit strictly between two of them.
- **The preview** resolves one order per card (`ChunkPreviewSim` line ~760) and does not draw per-instance depth.

**So the owner's order cannot be expressed today.** The closest is a band order, which is what UC6 uses. The owner's own sequence needs single fragments and single Pyres in alternating depth positions.

## Design proposal: a Depth list that addresses single pieces and single blasts

For the owner to approve. No code was written.

### In five lines
1. The Layer Plan becomes a **Depth list**: one row per thing the recipe draws. The rows fill in from the cards on their own, and dragging a row changes what is in front. **Card order stops affecting depth** once a plan exists (this fixes H4).
2. A Fragment Fracture row and a patterned Pyre Blast row get a **Split** toggle. It replaces the row with one row per piece or point ("Disc pieces 1/2/3", "Small pyres 1/2/3"), so the owner's list can be dragged into shape exactly.
3. **Pieces get stable numbers:** the cutter numbers them clockwise from 12 o'clock by where they sat. The preview writes those numbers, and pattern points keep their stagger numbers.
4. **The runtime asks the plan "what order is card X, instance k?"** instead of looking up a slot name. The plan answers with the burst's own base order (500) plus row × step. Particles and debris stay one row each.
5. **Existing named slots migrate** to rows in their current order, so no existing recipe changes its look. The per-card Layer radio disappears, which leaves one control fewer on every producer card and no typed names at all.

### The Layer Plan card (Depth list)
- A drag-to-reorder list (the grip handle the Debris Scatter modifier list already uses), top = back, as today.
- Each row shows the card's colour chip, the card title, the instance number when split, and a small thumbnail where one exists (the Pyre, or the piece cut from the Source). Clicking a row scrolls to its card (walker A's H1 asks the same of lanes).
- A newly added producer appears as a new row at the **front**, which matches today's "unslotted draws in front".
- Removing a card removes its rows. There are no names to type, so none to rename or mistype.
- **Split / Join** is a toggle on rows that can split:
  - Fragment Fracture, one row per piece up to its Pieces count;
  - Pyre Blast with Line/Ring, one row per point.

  Splitting inserts the instance rows where the joined row was, so the picture does not change until the user drags something.
- Rows that can never split: Palette Splash and Debris Scatter. Their counts vary per burst, and "particle 7" means nothing.
- The owner's list becomes exactly seven rows: `Big pyre · Disc pieces 1 · Small pyres 1 · Disc pieces 2 · Small pyres 2 · Particles up+down · Disc pieces 3`. The two splash cards stay two rows next to each other, and Small pyres 3 goes wherever the owner wants it.

### Producer cards
- The **Layer** radio row is removed. Depth lives in one place.
- A one-line read-only hint on a card is enough: "Depth: row 3 of 7", with a click that jumps to the plan. It shows only when a plan exists.

### Preview
- Draws in the plan's order, computed by the same function the runtime uses (design §16 already requires this).
- Fracture squares and pattern points carry their instance numbers.
- Hovering a Depth row highlights what it draws on the stage.
- When two things overlap on the stage, the one in front is visibly on top. Today the stage draws per card, so this needs the stage to draw per instance in plan order. It already draws per dot, so this is an ordering change, not a new renderer.

### What the runtime must do
- `LayerPlan` stores `List<DepthRow { string capabilityId; int instance /* -1 = whole card */ }>` and the step (keep 10).
- It answers `OrderFor(capability, instance, sub)`:
  - the row of `(id, instance)`, else the row of `(id, -1)`, else "front";
  - result = **the burst's own sortingOrder** + rowIndex × step + clamp(sub, 0, step−1).
  - Using the emitter's order instead of `LayerSpec.baseOrder = 0` also fixes D3.
- Producer call sites change from `ctx.OrderFor(layerName, i)` to `ctx.OrderFor(this, instance, sub)`:
  - Fracture: instance = piece number, sub = 0;
  - Pyre Blast: instance = point number;
  - Splash / Debris: instance = −1, sub = particle index.

  That is 4 call sites plus Trail's puff order.
- `FragmentCutter.Cut` sorts its pieces clockwise by offset angle before returning them. This is deterministic per seed, and it gives "piece 1" a meaning that survives a different live frame: "the piece that was at the top".
- **Migration, once, through the editor:** each named slot in order becomes one `(id, -1)` row for every card that referenced it, in stack order. Cards with no slot are appended at the end, which is the front.
  - Today's cross-card order is reproduced.
  - Same-slot cards that used to tie now resolve by stack order, which is the only visible change and is strictly less random.
- `LayerSpec` itself stays as it is: `SpawnPyreFx` and `PyreChunksFx` use it. Chunks just stops exposing slot names.
- **Without a plan**, behaviour is unchanged (stack order). The ▲▼ tooltips should say "also changes what draws in front" (H4's cheap half).

### Open points for the owner
1. Confirm the reading **top = back**.
2. Confirm that numbering pieces clockwise from the top is a good enough identity for "Fragment 1/2/3". The alternatives are largest-first, or a per-piece picker on a thumbnail strip.
3. Should particles ever split in two ("half behind, half in front")? This proposal says no.
4. The follow-on for "small Pyres in between the fragments" (spatial, not depth): a Pyre Blast pattern option "Between pieces of <Fracture>", which places ring points on the angles midway between the numbered pieces. It is not part of this proposal.

**Rough size:** runtime about 5 files, small. Editor: the Depth-list card (reuses the drag list), the preview ordering and labels, and removing the Layer radio. One migration pass.

## Friction log (new only; earlier ids cited)

| # | Trying to | Had to | Should have | Severity | Rule |
|---|---|---|---|---|---|
| D1 | put Fragment 1, Pyre 1, Fragment 2… at alternating depths | settle for bands: every piece of a card shares one slot, every point of a pattern shares one slot | per-instance depth rows (the proposal above) | **BLOCKER** (for the owner's layering) | owner use case 6 |
| D2 | see which thing is in front | guess; the preview has no depth cue and colours specks by slot while lanes colour by card | the preview draws in runtime order, per instance, with hover-highlight from the plan | **MAJOR** | design §16; ui-rules §9 |
| D3 | keep the break-up in front of the scene | discover that adding a Layer Plan drops every output from order 500 to 0–39; the Big pyre ties the Zoe body at 0 and would draw behind any game sprite above 0 | slotted orders start from the burst's own sortingOrder (500) | **MAJOR** | design §16 (the preview assumes 500 as well) |
| D4 | spray 30+ particles in one slot | accept that every particle past the 9th is pinned at the band's last number, and that two cards in one slot share numbers | a particle row uses one order for all its specks (Debris Scatter already does), so nothing depends on the clamp | MINOR | — |
| D5 | build "Pyre 1 / Pyre 2 between fragments" spatially | accept that a ring's points and the fracture's piece directions are unrelated | a "Between pieces of <Fracture>" pattern option (proposal, open point 4) | **MAJOR** | owner use case 6 |
| D6 | spray in 360° | add a second Palette Splash aimed the other way (UC4 J3) | Spread that reaches a full circle | **MAJOR** (repeat of J3) | — |
| D7 | name the layers | type four names into unlabelled fields, each Add layer rebuilding the whole stack; then pick the same name again on every card | no names at all: rows come from the cards (proposal) | **MAJOR** | ui-rules §1 "typed once where declared, picked everywhere" (today it is typed and picked, twice the work) |
| D8 | understand the Layer Plan card's own Name field | nothing; a coordinator's name shows nowhere that matters | hide Name on the Layer Plan card | MINOR | ui-rules §7 |
| D9 | keep particles in the fragments' colours when a Zoe triggers | accept that the splash samples the fracture's authored first frame while the fracture cuts the live one (`cell_000` specks around `cell_001` pieces) | Palette Splash's fracture fallback uses the same live-aware source (UC4 J2) | **MAJOR** | consistency |
| D10 | read the stage | see everything framed small by a wide guide ring | framing per design §8.17 (walker A's G7) | MINOR | — |

Repeats that also bit here:
- UC1: F2 (no duplicate card: the second splash was built from scratch), F3 (numberless offset pad), F4 (lane vs stage colours).
- UC2: G2 (Pyre length typed by hand, twice).
- UC3: H4 (card order drives depth: in this recipe it drives it only when the plan is off, which is exactly the D1/D3 trap).
- UC4: J5 (Zoe row Public/Private).

## Verdict: not buildable (the layering); the rest buildable with workarounds

Everything except the owner's interleaved depth is in UC6 and runs, standalone and from the Zoe copy, with the fragments cut from the live frame. The interleaving has no control at all (D1). The proposal above is the smallest change that makes it expressible and fixes H4 on the way.

## Verified by probe
- Card values were read back after every edit (listing in "What was built").
- **Runtime orders with the plan, standalone and Zoe-triggered:** Big pyre 0; fragments 10/11/12; ring points 20/21/22; particles 30–39, clamped; all on sorting layer Default.
- **Without the plan:** 501; 502–504; 503+i and 504+i; 505–507.
- **Zoe-triggered:** pieces `cell_001_Fragment1..3` (live frame); the Zoe body at order 0.
- **Code:** `LayerSpec.OrderAt` clamp, `ChunkModuleContext.ResolveOrder` unclamped fallback, per-card `LayerName`, preview order per card.
- The owner's Floating Disc is unchanged. The copy's second Death row holds the UC6 guid.

## Verified by eye
- `01`: the Layer Plan with four rows, the Big pyre card with its Layer radio, the five Timing lanes.
- `02`: the stage at four times.
- `20`: standalone burst (whole disc at t0, then the big Pyre behind the flying pieces and the small ring).
- `21`: Zoe copy alive, the hurt frame, then the break-up with pieces in front of the big Pyre.
- `22`: the same burst with the plan switched off.

## Not verified
- No human mouse (the Add-layer and rename flow, the Layer radios, and drags were synthesised).
- The proposal is not prototyped.
- The Zoe copy was killed from code in an unsaved scene; Mirage was not run.
- Sorting was read from `sortingOrder` values and one camera render per moment. Tie-breaking between equal orders was not studied frame by frame.
