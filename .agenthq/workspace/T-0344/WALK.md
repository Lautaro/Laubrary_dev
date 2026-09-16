# UC1 walk: several Pyres, each with its own spawn time, position, scale, alpha and tint (T-0344)

Walked 2026-09-16 on the live Laubrary Dev editor (dev @ b6376425), starting from a cold `Laubrary/Chunks` window. Recipe saved as `Assets/Demos/ChunksDemo/UseCases/UC1 Several Pyres.asset`.

**The owner's sentence:** "Spawn several Pyres, and for each one change spawn parameters like spawn time, position, perhaps scale, alpha or tint."

**How the window was driven.** A throwaway probe drove the real window's real elements:
- Buttons got a submit event.
- Add-menu rows got a pointer down/up.
- Sliders and range sliders were moved through their own notifying setter.
- The Offset pad went through its own drag handler (`SetFromLocal`).
- Text, number and colour fields had their values set, which fires their change events.

Two gestures could not be synthesised:
- **Pyre picker.** Picks went through the picker's own pick callback. The browser was opened through the chip's own activate handler, but it was not seen on screen (see "Not verified").
- **Colour picker.** The colour field's value was set directly; the OS colour dialog was never opened.

Before each action the probe started a new undo group. Captures are PrintWindow shots of the real window: `01`–`13` are the window, `20-*` are Play-mode camera renders.

## What was built

Four Pyre Blast cards, each with Pattern set to Single:

| Card | Blast | Delay | Offset | Scale | Alpha | Tint |
|---|---|---|---|---|---|---|
| Left | HollowBlast Plus | 0 | (-3, 0) | 1 | 1 | white |
| Small | HollowBlast Plus | 0.25 | (-1, 1.5) | 0.6 | 0.8 | orange |
| Big | HollowBlast Plus | 0.5 | (1, -0.5) | 1.4 | 0.6 | cyan |
| Right | Directional Grenade Side Blast Plus | 0.75 | (3, 1) | 1 | 0.4 | green |

The clock runs 1.35 s.

## The walk, step by step (what I saw, what I clicked, how I knew)

1. **`Laubrary/Chunks`.** The library grid showed 7 recipes, plus New and Browse (`01`). *How I knew:* the menu.
2. **New, type "UC1 Several Pyres", Create.** An empty recipe appeared: one `Add capability…` button, a stage with only the origin cross, and no Timing section (`02`). The asset was created in `Assets/Chunks/`; I moved it to `UseCases/` myself. *How I knew:* the button label.
3. **Add capability….** Nine kinds were listed; Trajectory, Trail and Hits were greyed, each with a reason (`03`). "Pyre Blast — Spawns an effect — one, or a whole pattern of them" is the obvious pick. *How I knew:* the name and the tooltip.
4. **Pick Pyre Blast.** One card appeared, with no Delay dial and no Timing section. The lone-capability rule held (`04`).
5. **Blast chip, pick HollowBlast Plus.** The browser offers 32 Pyres (`05`).
6. **Detour a first-time user will take: Pattern → Line, Count 4, Length 6, Stagger 0.25** (`06`). Four numbered points appeared, fired in order.
   - Every "per instance" control here is a random **band** (Scale min–max, Alpha min–max) or a **single shared** value (Tint, Rotation).
   - Positions are evenly spaced, and times are evenly staggered.
   - There is no way to say "the second one is small and orange".
   - So I went back to Single.
7. **The route that works: one Pyre Blast card per instance.** On card 1: Name "Left", Offset pad to (-3, 0).
8. **Add capability → Pyre Blast → pick HollowBlast again.** The Timing section appeared with two lanes, and a Delay dial appeared on **both** cards. The stage did not move (its top edge sits at the same place in `05` and `07`).
9. **Card 2.** Delay 0.25, Offset (-1, 1.5), Scale 0.6–0.6, Alpha 0.8–0.8, Tint orange, Name "Small".
10. **Cards 3 and 4.** Same routine as card 2: Add, pick, set six values.
11. **Scrubbed to 0.10 / 0.40 / 0.65 / 0.95 s** (`08`–`11`).
    - Each blast is a faint outline before its delay, then a filled disc in its tint while it plays, then gone.
    - The four lanes stagger as authored, and the playhead moves on the ruler.
    - Scale reads as disc size, and alpha as fill strength.
12. **Replay.** The clock advanced 0.08 → 0.42 s between two probes; the button read `❚❚ Pause` (`12-playing-a/b`). Pause held the time.
13. **Saved with File → Save Project.** The file has four `PyreBlast` managed references with their rids, names and delays intact. Git showed no other file written by the save.
14. **Real burst in Play mode.** This used an unsaved stage scene: an orthographic camera plus a `ChunkEmitter` holding UC1. `Burst()` was called at timeScale 0.25 (`20-play-burst-a..e`). The spawned Pyre players matched the recipe exactly:
    - **Order and timing:** they appeared one by one in order.
    - **Position and scale:** (-3, 0) at scale 1.00, (-1, 1.5) at 0.60, (1, -0.5) at 1.40, (3, 1) at 1.00.
    - **Colour × alpha:** (1, 1, 1, 1), (1, .6, .2, .8), (.3, .9, 1, .6), (.4, 1, .4, .4).
    - **Draw order:** 500, 501, 502, 503.
    - The render shows four blasts in the authored places (`20-play-burst-d`). The editor was then taken out of Play mode and ProtoGuyDemo reopened, clean.
15. **Re-entry: closed and reopened the window.** It came back on the library, not on UC1 (`13`); the library tile shows only the first Pyre.
16. **Undo.** Scale 1 → 0.5, then Undo gave back 1, Redo gave 0.5, Undo gave 1 again.

**Step count for a human:** about **54 actions**, plus 4 wasted on the Line detour. The breakdown:

| Part | Actions |
|---|---|
| Menu | 1 |
| New / name / Create | 3 |
| Add capability + pick kind, ×4 | 8 |
| Blast chip + pick, ×4 | 8 |
| Card 1 (name, pad) | 2 |
| Cards 2–4 (name, delay, pad, 2 drags for Scale, 2 drags for Alpha, ~3 for the colour dialog), ×3 | 30 |
| Play + save | 2 |

About 30 of the 54 are repetition that one "duplicate this card" action would remove.

## Judging points from the task

- **Can each INSTANCE differ?**
  - Not inside one Pyre Blast: a pattern's points share one tint, draw scale and alpha from a random band, and are spaced and staggered evenly.
  - Per-instance values need one card per instance. That works, and runs correctly in Play mode, but nothing in the window tells you so.
- **Is "several" obvious?**
  - The obvious answer (Pattern → Line/Ring, Count) is the wrong one for this use case.
  - The right answer (several cards) is found only by trying the first and seeing it cannot do it.
- **Is the timing surface shown only when it earns its place?**
  - Yes. It was absent with one card and appeared with the second, as did the Delay dials. The preview stage did not move.
  - The lanes are read-only: a delay can only be typed on the card (see UC3, T-0346).

## Friction log

| # | Trying to | Had to | Should have | Severity | Rule |
|---|---|---|---|---|---|
| F1 | give each of several Pyres its own time/position/scale/alpha/tint | abandon Pattern and build one card per instance; a pattern only offers random bands and one shared tint | a pattern whose points can each carry their own offset/delay/scale/alpha/tint (a small per-point table, or a "Custom" pattern where each point is a row) — or the card saying plainly that per-instance values mean one card each | **MAJOR** | Handover walk step 3 (reachability); owner principle "minimal cognitive load" |
| F2 | add the 2nd, 3rd, 4th instance | Add capability → Pyre Blast → re-open the picker → re-pick the same Pyre → re-set every value, each time (~30 of 54 actions) | a Duplicate action on the card header (copy with a fresh id, placed right after) | **MAJOR** | ui-rules §3 space/effort; owner "minimal cognitive load" |
| F3 | place a blast at x = -3 | drag an unlabelled 56 pt pad. It shows no numbers, its ±8 range is invisible, and one point of drag is about 0.3 world units, so an exact value cannot be read or entered | the pad shows its value and accepts a typed/scrubbed value (e.g. Z.Pad with a readout plus two scrub fields, or `Z.Value2D`) | **MAJOR** | ui-rules §1 "a numeric input should be drag-scrubbable, never keyboard-only" (here: neither); §3 legible |
| F4 | tell which disc on the stage is which card | guess. Single blasts carry no label (only pattern points are numbered). The stage colours a disc by its tint, the lane colours by stack position, and the card shows neither (card "Big" = cyan disc, green lane) | one identity per card: its number/name next to its disc, and the same colour on the card header, its lane and its stage outline | **MAJOR** | ui-rules §1 "same kind of value, same control/look everywhere"; §4 "every mention shows the picture" |
| F5 | preview a cyan-tinted Pyre | trust a cyan disc; in Play mode the tint MULTIPLIES the red Pyre and it comes out nearly black (`20-play-burst-d`, "Big") | the preview predicts the result: the Pyre's own colour × tint, or the tinted Pyre thumbnail | **MAJOR** | design §6 "preview shows what happens" |
| F6 | see the explosion I picked | read an outline disc. The stage never shows the Pyre itself; a lone blast at the origin is zoomed to fill the whole stage (`04`, `05`); an unpicked blast still draws a full disc | a tinted thumbnail or first frame inside the footprint; no disc while nothing is picked | MINOR | ui-rules §5 (a thumbnail is a promise) |
| F7 | work on four cards | scroll a stack of ~280 pt cards, each repeating Add alternate / Pattern / Rotation / Seed rows this use never touches | a compact form of a Single blast card (picker, delay, offset, scale, alpha, tint on 2–3 rows), or default-folded extras | MINOR | ui-rules §3 vertical space is scarce |
| F8 | set one fixed scale / alpha per instance | drag both handles of a min–max slider to the same spot (two drags each); it then reads "1.40 – 1.40" | a range that collapses to one handle when both ends meet (or a Fixed/Range right-click mode like Z.Value) | MINOR | ui-rules §1 |
| F9 | save the recipe where I want it | accept `Assets/Chunks/` (or the folder of whatever recipe was open) and move it by hand | New asks, or shows, where it will create the file | MINOR | — |
| F10 | get to the recipe | scroll past Tags and Views (~150 pt, both unused) on every recipe; they are on by default in the toggle bar | these two off by default, or collapsed into one line | MINOR | ui-rules §7 only expose what is in use |
| F11 | read the card | watch the label column jump: identical cards align labels at different x (`07`: card 1 at 143 px, card 2 at 113 px), and switching Pattern moves every field (`06`) | one label column per card kind, stable across modes | MINOR | ui-rules §6 stable workspace |
| F12 | find Replay | read `≣ Replay`: the ⟲ glyph renders as a list icon (`02`) | a replay icon | MINOR | — |
| F13 | pick a Pyre | scan 32 entries including `New Pyre Plus` ×2, `New Pyre Plus 1`, `New Pyre Plus§`, and a "Plus" suffix plus typos (`Driectional`, `Explotion`, `Abbility`) on most names | a clean library (owner's asset hygiene; not a Chunks defect) | MINOR | owner principle "no clutter in the shared browser" |
| F14 | come back to UC1 | reopen the window and find myself on the library; the tile shows only the first Pyre | reopen on the last recipe; the thumbnail shows the composition | MINOR | known AssetKit gap (T-0217 finding 1) |
| F15 | read the Line pattern rows | see Angle alone on one row and Jitter alone on the next; Count carries an embedded number box that no other slider has | Angle + Jitter share a row | MINOR | ui-rules §3 short fields share a row |
| F16 | nothing (empty recipe) | see a `Burst direction` dial on an empty recipe | shown once a card can inherit it | MINOR | ui-rules §7 |

## Verdict: buildable with workarounds

The result is exactly what was asked for, and it runs correctly in Play mode. The workarounds:
- one card per instance (F1, F2);
- reading positions I could not see (F3).

## Verified by probe
- Every edit was read back from the asset after it was made: names, delays, offsets, scale and alpha bands, tints, sources.
- The clock length was 0.25 → 0.60 → 0.85 → 1.10 → 1.35 s as cards were added. The Timing surface was absent at one card and present from two.
- In Play mode, `ChunkEmitter.Burst()` spawned four Pyre players with the authored positions, scales, colour × alpha and draw orders 500–503, one after another in authored order.
- Undo/redo of a Scale edit.
- Save Project wrote only the UC1 file, with its managed-reference ids intact.
- Reopening the window forgets the recipe.

## Verified by eye
- **Window captures** `01`–`13`:
  - cold library;
  - empty recipe;
  - Add menu with greyed modifiers;
  - one-card and two-card states (Timing appears, the stage stays put);
  - the Line detour;
  - four cards at four playhead times with the lanes and playhead agreeing;
  - Pause/Play label;
  - library after reopen.
- **Play-mode camera render** `20-play-burst-d`: four blasts in the authored places, with the tint multiply visible.

## Not verified
- **No human mouse.** No real drag on the Offset pad, the range sliders, or the lanes. The pad's precision (F3) is worked out from its geometry, not felt.
- **The Pyre browser popup was never seen.** Opened from a probe it placed itself at screen (0, 0) and closed on focus loss. The pick went through its own callback.
- **The OS colour picker was never opened.** The colour field's value was set.
- **Mirage handoff was not run.** It opens `MirageStage.unity`, which carries uncommitted owner edits and is off limits for this programme.
- **The Play-mode burst was fired from code** (`ChunkEmitter.Burst()`), at timeScale 0.25, in an unsaved scene. No game input was involved.
