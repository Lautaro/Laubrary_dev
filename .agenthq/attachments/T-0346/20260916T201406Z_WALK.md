# UC3 walk: a specific order of Pyres in specific positions over a timeline (T-0346)

Walked 2026-09-16 on the live Laubrary Dev editor, right after UC1 and UC2, with the same harness (real elements, the Pyre picker through its own callback, Save Project). Recipe saved as `Assets/Demos/ChunksDemo/UseCases/UC3 Timed Sequence.asset`.

**The owner's sentence:** "Spawn a specific order of Pyres in specific positions over a timeline."

## What was built

Four Pyre Blast cards, each Single. Listed in the final card order (which is also firing order):

| Card | Blast | Delay | Offset |
|---|---|---|---|
| A | Rose Blast Plus | 0 | (-4, 0) |
| B | SparkleBurst Plus | 0.3 | (-2, 2) |
| D | Blob Explosion Plus | 0.6 | (2, 2) |
| C | Perlin Blast Plus | 0.9 | (0, -1) |

The clock runs 1.50 s. D was authored fourth and moved up during the walk (see steps 4–5).

## The walk

1. **New → "UC3 Timed Sequence" → Create** (lands in `UseCases/`).
2. **Four times: Add capability → Pyre Blast → Blast chip → pick → Name → Delay → Offset pad.**
   - The first card has no Delay dial. The Timing section and all Delay dials appear with the second card; the first card grows a Delay field next to its Name when that happens.
   - A first-time user's first idea, Pattern Line with Order "Sequential / Reverse / From centre / Random", gives an order but only evenly spaced positions. So "specific positions" again means one card per Pyre (as in UC1).
3. **Tried to edit the timeline directly** (`02`). A real pointer down / move / up on lane C's band moved the **playhead** to 1.21 s; C's delay stayed 0.6.
   - The lanes' own tooltip says exactly that: "Drag anywhere to move the playhead".
   - A band cannot be dragged to a new time, and clicking a lane does not take you to its card.
4. **Changed the firing order: typed C's delay 0.9 and D's 0.6 on the two cards** (`03`).
   - The lanes do not re-sort, so the ruler now reads A, B, C, D top to bottom while firing A, B, D, C.
5. **Pressed ▲ on D's card to make the stack (and so the lanes) match the firing order** (`04`; before/after montage `05`).
   - The lanes now read A, B, D, C.
   - Lane colours follow position, so D turned from pink to green and C from green to pink.
   - The left pane kept its scroll position.
6. **Scrubbed to 0.10 / 0.45 / 0.80 / 1.25 s** (`06`–`09`, montage `10`).
   - The sequence reads: A (left), then B (top left), then D (top right), then C (below the origin).
   - A blast still to come is a faint outline, and a playing one is filled.
   - Nothing on the stage numbers the order or shows a position.
7. **Save Project**, then a real burst in Play mode (unsaved stage scene, `ChunkEmitter.Burst()`, timeScale 0.25; `20-play-a..f`).
   - The players appeared in the order A (-4, 0), B (-2, 2), D (2, 2), C (0, -1), at the authored times.
   - Their draw orders were **500, 501, 502, 503 in card order**. Moving D's card up in step 5 changed which blast draws in front, as well as the lane order.
   - The editor was taken out of Play mode and ProtoGuyDemo reopened, clean. The window came back on UC3.

**Step count for a human:** about **35 actions**:

| Part | Actions |
|---|---|
| New / name / Create | 3 |
| Card 1 (add ×2, pick ×2, name, pad) | 6 |
| Cards 2–4 (the same plus a delay), ×3 | 21 |
| Two retyped delays + one ▲ | 3 |
| Play + save | 2 |

No action could be spent on the timeline itself.

## Judging points from the task

- **Placing a Pyre at an exact position.**
  - Only by dragging the unlabelled 56 pt Offset pad (range ±8, about 0.3 units per point, no readout, no typed entry).
  - Nothing on the stage can be dragged, and no position is written anywhere.
  - "Specific positions" can be authored only approximately, and cannot be checked without a probe.
- **Reordering in time.**
  - By retyping Delay numbers on each card.
  - The lanes then disagree with the firing order until the cards are moved with ▲/▼, one place per click.
  - That move also changes draw depth.
- **Does the ONE timeline appear, and can it be edited?**
  - It appears exactly once, below the preview, as soon as there are two timed cards, and it stays put.
  - It is **read-only**: dragging anywhere on it moves the playhead only.

## Friction log

| # | Trying to | Had to | Should have | Severity | Rule |
|---|---|---|---|---|---|
| H1 | move a Pyre later in time on the timeline | find its card and retype its Delay; dragging the band only moves the playhead, and a lane gives no route to its card | drag a band's body to change that card's delay (one undo per drag, playhead drag kept on the ruler); click a lane label to scroll to its card | **MAJOR** | owner principle "exactly ONE timeline and only when it earns its place" (a timeline you cannot author on half-earns it); handover walk step 3 |
| H2 | put a Pyre at an exact spot | drag an unlabelled pad with no numbers (UC1 F3); nothing on the stage can be dragged | typed/scrubbed X and Y beside the pad, and draggable blast discs on the stage | **MAJOR** | ui-rules §1 (numeric entry must exist and scrub); §3 legible |
| H3 | see the order the Pyres go off in, where they are | read the lanes; single blasts on the stage carry no number (pattern points do) | number every blast on the stage by firing order, the same way pattern points are numbered | **MAJOR** | design §6 (the schematic shows order) |
| H4 | make the timeline read in firing order | reorder cards, which also changes draw depth (sortingOrder 500 + card index, confirmed in Play mode); nothing says so | lanes that can sort by start time independent of the stack, or the reorder/depth link made visible (the card showing its draw position) | **MAJOR** | ui-rules §2 label = action (▲ says "earlier in the recipe", does not mention depth) |
| H5 | reorder four cards | press ▲/▼ once per place | drag-and-drop cards (and lanes) | **MAJOR** | ui-rules §4 "a reorderable list is drag-and-drop, never up/down arrow buttons" |
| H6 | read how long each Pyre plays and how they overlap | trust 0.6 s bands; the real Pyres run 1.33–1.67 s (Rose 1.67, Sparkle/Perlin/Blob 1.33), so the timeline shows gaps and overlaps that do not happen | bands sized from each Pyre's own length by default (UC2 G2) | **MAJOR** | design §6 "time is real" |
| H7 | follow a card through a reorder | notice its lane and stage colour changed (D pink → green, `05`) | a colour that belongs to the card, not to its position | MINOR | ui-rules §8 key view state per instance, not per position |
| H8 | read the lanes | read one-letter names in a ~90 pt gutter far from their bands; the bar gets ~295 pt of a ~390 pt pane | a gutter sized to its longest name | MINOR | ui-rules §3 size to content |
| H9 | keep working on card 1 | see card 1 grow a Delay field beside its Name when card 2 is added | reserve the Delay slot, or accept it, but do not reflow a card the user is not touching | MINOR | ui-rules §6 stable workspace |
| H10 | pick "Proper Blast" | nothing: it renders no pixels (frame check: 0 lit pixels), like the two Ring Blast Pyres noted before | asset hygiene (owner) | MINOR | ui-rules §5 |

UC1's F2 (no duplicate card), F4 (card↔disc identity), F6, F9–F14 apply here unchanged.

## Verdict: buildable with workarounds

The order and positions are right in Play mode. The workarounds:
- positions set on a numberless pad;
- time edited only as typed numbers on the cards;
- the timeline brought into line by reordering cards, which also changes depth.

## Verified by probe
- Delays, offsets, sources and card order read back after every edit.
- A real pointer drag on a band changed the playhead (0 → 1.21 s) and left the delay at 0.6.
- The left pane's scroll was held at its maximum across the reorder.
- In Play mode the four players appeared in the order A, B, D, C at the authored positions, with draw orders 500–503 following card order.
- Rose Blast Plus measured 1.67 s; SparkleBurst, Perlin and Blob 1.33 s. Proper Blast renders 0 lit pixels.
- Save wrote UC3 clean.

## Verified by eye
- `02` band drag moving the playhead.
- `03` lanes out of firing order after retyping delays.
- `04`/`05` lanes in order after ▲, with the colours swapped.
- `10` stage sequence over four times.
- `20-play-d` real blasts at their places.

## Not verified
- No human mouse on the pad, the lanes or the ▲ buttons (pointer events were synthesised on the real elements).
- Mirage handoff not run (MirageStage is off limits).
- The Play-mode burst was fired from code at timeScale 0.25 in an unsaved scene.
