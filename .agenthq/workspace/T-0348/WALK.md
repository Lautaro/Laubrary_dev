# UC5 walk: fracture and fling a Zoe's sprite (T-0348)

Walked 2026-09-16 on the live Laubrary Dev editor (dev @ becf15cd, which includes 097a3be8, the "Fracture uses a Zoe's live frame" change). The harness was the same as in UC4 (T-0347). The recipe is saved as `Assets/Demos/ChunksDemo/UseCases/UC5 Disc Fracture.asset`. The Zoe-triggered test used the Floating Disc **copy** from UC4 (`UseCases/UC Floating Disc (walk copy).asset`). Its extra Death row was re-pointed at UC5 through the row's own library picker. The owner's Floating Disc was only *referenced* as the recipe's Source, never edited.

**The owner's sentence:** "Fracture and fling a sprite of a zoe."

## What was built

One **Fragment Fracture** card, "Disc pieces":
- Source: the **Floating Disc** Zoe.
- Pieces: 4. Speed: 3–6. Inherit burst direction: on (90).
- Spread: 180 (radial). Gravity: 10. Drag: 0.4. Spin: 30–180. Life: 1.2–2.0.

The clock is 2.00 s long.

## The walk

1. **New → "UC5 Disc Fracture" → Create, then Add capability → Fragment Fracture.** Its menu tooltip, "Cuts a picture into pieces and flings them", matches the owner's words exactly. This is the one kind that is easy to find.
2. **The card, before any source is chosen** (`01`).
   - The card shows no warning.
   - The preview draws four small squares that fly apart as if something would be cut.
   - A standalone burst with no source puts nothing on screen and logs nothing: `FragmentFracture.Fire` returns silently when there is no source.
   - **PM's question answered:** a user never sees a "no fragments" warning, in this state or when a Zoe supplies the frame. The window has no such warning (`HasSource` has no caller in the editor). The only warning is a console line, and it appears only when a cut is *attempted* on an unreadable or empty sprite. So the risk is the opposite one: an empty Source looks fine in the preview and silently does nothing when fired standalone.
3. **Source chip → browser.** The browser lists Zoes, Pyres and an Ammo, 42 entries in all, including `New Pyre Plus§` and similar clutter. I picked **Floating Disc**.
   - The card then reads the Zoe's first frame, `cell_000`.
   - The "Fallback sprite" field stays visible below Source even though it is now unused.
4. **Set Name, Pieces 4, Speed 3–6, Gravity 10.**
5. **Scrubbed the preview to 0.05 / 0.5 / 1.0 / 1.6 s** (`02`).
   - Four blue squares burst into a symmetric cross, rise and fall, spin, and disappear by their life.
   - **The disc's picture never appears**, not whole and not in pieces. The piece layout is a schematic ring; the real cut gives pieces of different shapes.
   - The pattern is the same for every source, so choosing the Zoe did not visibly change anything in the preview.
6. **Save Project.** The file saved clean.
7. **Zoe row:** on the copy's Death reaction, the library picker on the second Spawn Chunks row → **UC5** (`03`).
   - Its "Live sample preview" still reads "This recipe has no Sampled-visual Debris Scatter — nothing else to preview".
   - So it does not mention that this fracture will now cut the live frame.
   - The recipe chip shows no thumbnail (UC4's chip did).
8. **Standalone burst in Play mode** (`20`).
   - Four `Fragment` renderers, cut from `cell_000` (the Zoe's first frame), at orders 500–503.
   - They start tiled into the whole disc and then fly apart radially, spinning and falling.
9. **Zoe-triggered burst** (`21`): spawned the copy, then `Health.Kill()`.
   - The four pieces were cut from **`cell_001`**, not from `cell_000`. `cell_001` is the frame on screen at the moment of death: `Kill()` fires the Hit reaction first, and the Hurt clip starts on `cell_001`. The Death clip is `cell_000` alone.
   - I also forced the renderer to `cell_005` in the same call just before `Kill()`. The Hurt clip replaced it synchronously, and the pieces again came from `cell_001`, the frame actually showing.
   - A second kill without forcing gave the same result.
   - **The live-frame fracture works:** pieces come from whatever the Zoe is showing, not from the authored first frame. In `21` the Zoe-triggered pieces visibly carry the checkered hurt-frame look, while the standalone pieces in `20` carry the idle look.
   - For the first frames, the Zoe's intact body is drawn at order 0 underneath its own pieces (`21`, middle panel). Nothing in the recipe or on the Zoe row relates "hide the body" to the fracture.

**Step count for a human:** about **15 actions**:

| Part | Actions |
|---|---|
| New / name / Create | 3 |
| Add + kind | 2 |
| Source chip + pick | 2 |
| Name, Pieces, Speed ×2, Gravity | 5 |
| Play + save | 2 |
| Optional reading of Inherit / Spread | 1 |

Attaching UC5 to a Zoe adds about 6 more, the same route as UC4's J5.

## Judging points from the task

- **Standalone:** it works. Pieces cut from the Zoe's first frame tile the disc and fling outward, as expected.
- **Zoe-triggered:** it works, and since 097a3be8 the pieces come from the **live** frame.
  - The PM's pre-walk note ("Fracture takes the FIRST frame from an authored field") describes the standalone case only.
  - The card's Source tooltip says so correctly ("When a Zoe triggers this burst, its live current sprite outranks both…").
  - Nothing else in either window shows it (K3).
- **Difference as a user sees it:** none in the editor. Both windows preview the same thing whether or not a Zoe supplies the frame. Only Play mode shows that the Zoe-triggered pieces are cut from a different frame.

## Friction log

| # | Trying to | Had to | Should have | Severity | Rule |
|---|---|---|---|---|---|
| K1 | see my Zoe come apart in the preview | read four blue squares in a fixed cross that look the same for every source; the picture never appears | the preview cuts the resolved source once (cached per source/seed/pieces, the runtime already caches seeded cuts) and draws the real pieces on their arcs | **MAJOR** | design §6 "the preview shows what happens"; ui-rules §5 |
| K2 | notice that no source is chosen | see a preview that pretends to fracture something, and get a silent no-op when fired standalone | the card says "(nothing to cut — pick a Source, or trigger it from a Zoe)" as a subtle line on the empty Source, and the preview draws no pieces while nothing is resolvable | **MAJOR** | ui-rules §2/§5; Handover-walk empty state |
| K3 | know which frame a Zoe-triggered fracture uses | read one tooltip; the Zoe row's live-sample preview talks only about Debris Scatter | the Zoe row's live-sample preview also shows "pieces this Fracture would cut" from the representative frame (the same block it shows for Sampled debris) | MINOR | ui-rules §5 |
| K4 | fracture the Zoe cleanly | watch the intact body drawn under its own pieces at first; hiding it is a Zoe death-disposal matter that neither window links to the fracture | a Spawn Chunks row option "hide the body while the burst plays" (or the doc says to use Death disposal) | MINOR | owner principle "minimal cognitive load" |
| K5 | read the card with a Zoe as Source | see "Fallback sprite" still offered, although it can no longer be used | dim it with the tooltip "used only if the Source yields no frame" | MINOR | ui-rules §7 |
| K6 | find the Zoe in the Source browser | scan 42 mixed entries (Zoes, Pyres, Ammo) with no type grouping, including clutter | type sections or a sort-by-type, and hygiene (UC1 F13) | MINOR | ui-rules §4 (sort options) |
| K7 | recognise UC5 on the Zoe row | see a chip with no thumbnail (UC4's chip has one) | the recipe thumbnail uses the Source's frame | MINOR | ui-rules §5 |

UC4's J5 (Public on a Private Zoe row does nothing) and J8 (empty "Alpha over life" box on a fresh card, which is also true here) apply unchanged, as do UC1's F9, F10 and F12.

## Verdict: buildable as-is

The recipe is five dials and one pick. It flings the Zoe's pieces standalone and, from a Zoe, cuts the frame actually on screen. What is missing is the preview and the empty-state honesty (K1, K2), not the ability to build the effect.

## Verified by probe
- Every card edit was read back from the asset.
- `ResolveSource()` returns `cell_000` for the Floating Disc source (`HasSource` = true). No editor code calls `HasSource`.
- **Standalone Play burst:** 4 `FragmentN` renderers, sprites `cell_000_Fragment1..4`, sorting layer Default, orders 500–503, positions changing over time.
- **Zoe-triggered (copy, Death row → UC5, `Kill()`), run twice, once with the renderer forced to `cell_005`:** pieces `cell_001_Fragment1..4` both times. `cell_001` is the Hurt clip's first frame (clip keys read from `Hurt.anim` against the texture's sprite ids); `Death.anim` holds only `cell_000`.
- In code, `FragmentFracture.Fire` returns silently on a null source.

## Verified by eye
- `01`: the card with no source and no warning, the preview still drawing pieces.
- `02`: the schematic over four times.
- `03`: the Zoe row with UC5 picked, a blank chip thumbnail, and a live-sample text that does not mention the fracture.
- `20`: standalone pieces of the idle disc.
- `21`: Zoe alive; the moment of death with the hurt frame and pieces; pieces of the hurt frame flying.

## Not verified
- No human mouse; the Source browser was driven through its pick callback, not seen on screen.
- The Zoe was killed from code in an unsaved scene. No weapon hit was used, and no death without a preceding Hit reaction was tested, so the frame used for a pure death (without `Kill()`'s Hit) was not observed.
- A Zoe whose frames are not Read/Write was not tried (Floating Disc's texture is readable).
- Mirage handoff was not run.
