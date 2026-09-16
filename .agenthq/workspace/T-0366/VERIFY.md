# Merge 1 verification in the live editor (T-0366)

Walked 2026-09-16 on the live Laubrary Dev editor (PID 38044, dev @ 7b6ab52c). The checklist is `ChunksUseCases/MERGE1-VERIFY.md`.

**How the window was driven**
- Synthesized pointer and navigation events on the real elements, one gesture per CLI eval, so each move landed in its own editor frame, with `Undo.IncrementCurrentGroup()` before each action.
- The preview stage is an IMGUI island, so its drags went through `EditorWindow.SendEvent`.
- Undo was checked by reading Unity's own undo stack (`Undo.GetRecords`), not only by reading values back.
- Captures are PrintWindow shots at 2.2× scale. Files named `*-crop` or `40-*` onward are crops of the full shot.

**What was used**
- Every test ran on copies or new assets in `Assets/Demos/ChunksDemo/UseCases/Scratch/` (listed at the end).
- No owner asset was edited through the window. See the incident note at the end: a Create saved two tracked assets as a side effect, and I restored them with git.

## Summary

| Task | # | Check | Result |
|---|---|---|---|
| T-0355 | 1 | Band follows the pointer, outlined, start shown on the ruler; Delay field and stage update live; ruler scale does not jump | **pass** |
| T-0355 | 2 | One Ctrl+Z restores the delay; redo works | **pass** |
| T-0355 | 3 | Ruler and empty-lane drags scrub and leave bands alone; a click on a band jumps the playhead | **pass** |
| T-0355 | 4 | Dragging a band left past 0 stops at 0.00 | **pass** |
| T-0355 | 5 | A switched-off band can't be dragged; pressing it scrubs | **pass** |
| T-0355 | 6 | Hover shows the slide cursor and the tooltip ending | **pass** (by probe) |
| T-0355 | 7 | Name column is as wide as the longest name; 0.00s under the bars; long name capped at 120 pt with ellipsis | **pass** |
| T-0355 | 8 | Dragging past the end of the clock lengthens the ruler on release | **pass** |
| T-0358 | 1 | Offset pad + X/Y fields; scrub and type; pad clamps at ±8 | **FAIL** |
| T-0358 | 2 | Stage disc/ring drag updates X/Y live; one Ctrl+Z undoes the drag | **FAIL** (the live update passes; undo fails) |
| T-0358 | 3 | The other `Z.Pad` users unchanged | **pass** for BackSplash (capture); others **untested by eye**, code untouched |
| T-0361 | 1 | Folder label middle-elided, full path on hover, row doesn't wrap or overflow | **FAIL** at a narrow window (passes at 900 pt) |
| T-0361 | 2 | Folder… → pick → Create uses it | **pass** (the dialog was bypassed, see below) |
| T-0361 | 3 | A folder outside Assets is rejected | **could not test** (native modal); code-read note |
| T-0361 | 4 | Close/reopen → New still shows the folder | **pass** |
| T-0361 | 5 | PM question: which folder is offered? | answered below |
| T-0361 | 6 | Undo of Create; ZuiAudit on the create row | **pass** (with the known AssetKit ghost); audit 0 findings at 900 pt, 3 at 560 pt |
| T-0354 | 1 | Coloured edge + number chip; lane and stage outline share the colour | **pass** |
| T-0354 | 2 | Grip reorder keeps colours; one Ctrl+Z; the grip never folds the card | **pass** |
| T-0354 | 3 | Duplicate: copy placed after, new colour, same values; one Ctrl+Z; modifiers independent | **pass** |
| T-0354 | 4 | UC2: every blast numbered, chips fade with discs, no stray numbers, not stacked at 0.05 s | **pass** |
| T-0354 | 5 | Clicking a lane name scrolls to, unfolds and highlights the card; scrubbing and band drag still work | **pass** |
| T-0354 | 6 | Adding a second blast doesn't move card 1's Name row | **pass** |
| T-0354 | 7 | ZuiAudit on the fully expanded window: no card header wraps | **pass** (900 pt and 560 pt) |
| T-0357 | 1 | Add menu says "Fling", greyed with a reason, real tooltip once there is a blast | **pass** |
| T-0357 | 2 | Burst → Fixed → Outward; Burst shows a disabled live row; Outward flies from the ring's own centre | **FAIL** (the Burst row is not live); Fixed and Outward pass |
| T-0357 | 3 | Spin with Face velocity on and off: a tick rotates over time | probe **pass**, by eye **FAIL** (the tick is hidden) |
| T-0357 | 4 | UC2 still loads and plays (old flag became Direction = Burst) | **pass** |
| T-0357 | 5 | Play-mode burst (Outward + Spin) agrees with the preview | **FAIL** (the mode is lost on reload); **pass** once the mode survives |
| Also | – | Console clean through the walk | **pass**: only Unity's generic "custom toolbar elements" warning on each domain reload |

## Failures (each posted as a todo message on its owning task)

1. **T-0358/1: X/Y fields don't clamp.** (`10-offset-typed-20.png`)
   - Typing X = 20 stores `offset.x = 20` while the pad dot pins at 8.
   - Typing Y = −30 then writes x = 8 (taken from the pad) while the X field still reads 20.
   - Scrubbing X past the end does the same: the field and the data read 26, the pad reads 8. The disc leaves the stage.
2. **T-0358/2: a stage drag leaves no Undo record.** (`11-stage-drag-mid.png`, `12-ring-dragged.png`)
   - `Undo.RecordObject` runs only on MouseDown, and nothing changes in that frame, so Unity drops the record. The MouseDrag frames then change the asset with nothing recorded.
   - The undo stack was unchanged after a full drag (measured for a disc and for a ring).
   - Ctrl+Z after the drag undid the previous, unrelated band drag instead, and the dragged offset stayed.
   - `BackSplashWindow.HandleDrag` has the same shape and likely the same bug.
   - Also: `draggingBlastCap` is serialized, so after a domain reload it comes back as a default PyreBlast (empty id) instead of null. Seen live. A press-drag on empty stage would then drag that phantom and dirty the asset.
   - Minor: stage-dragged values aren't snapped (the X field shows 3.000001).
3. **T-0361/1: the create row overflows in a narrow window.** (`22-new-row-narrow.png`)
   - At 560 pt, the folder label keeps its 252 pt and pushes Folder…, Create and Cancel past the window edge. ZuiAudit reports 3 off-screen findings; Create is then reachable only with Enter.
   - At 900 pt the row fits (`21-new-row-long.png`).
4. **T-0357/2: the "Burst direction" mirror on a Fling card is not live.** (`52-burst-mirror-stale.png`)
   - I moved the stage pane's Burst direction dial from 90 to 30. The recipe and the dial both read 30; the card's mirror still read 90 in the next frame.
   - Minor: the disabled mirror's tooltip still says "Drag to set; Shift = fine."
5. **T-0357/3 (with T-0354): the orientation tick can't be seen.** (`55-spin-*`)
   - The probe shows the ticks turning, for example blast 1 at 48°, 95° and 143° at 0.15, 0.30 and 0.45 s, with Face velocity both off and on.
   - On screen, the tick runs from the disc centre to the rim in the card colour. The firing-number chip (at least 14×13 pt, same colour, alpha 0.9) is drawn over the disc centre and covers it.
6. **T-0357/5: data loss — a new Fling's Fixed/Outward reverts to Burst on the next load.** (`60-play-lostmode-*.png`)
   - A Fling added from the menu has `directionModeMigrated = false` and `inheritBurstDirection = true`. Picking a mode on the card never marks it migrated.
   - Entering Play mode (domain reload) ran `MigrateLegacyCurves()`. The runtime spec then had `directionMode = 0` (Burst), the burst flew along the 30° burst direction instead of outward, and the card showed Burst after exiting Play.
   - Every other Fling field survived.
   - After I set Outward again (now marked migrated), the Play burst agreed with the preview:
     - directions 0 / 60 / 120 / 180 / −120 / −60° from the ring centre (2, 1), in both;
     - rotation growing about 340°/s at runtime against 180–360°/s authored (`61-play-*.png`, `54-outward-*`, `55-spin-faceon-*`).
     - Speeds differ between runs because seed 0 rerolls.

## Notes per check (passes)

**T-0355: band drag** (captures `00`–`07`)
- **Check 1: live drag.**
  - Pressing band C at 1.2 s: a 1 px move stays a press (threshold 3 px); a 0.15 s move becomes a drag and opens one undo group ("Edit Delay").
  - Mid-drag (`01-band-mid-drag-crop`): band C is outlined white, "1.20s" is printed in pink on the ruler, and the ruler still ends at 1.50s although the recipe is now 1.80 s. The card's Delay field reads 1.20.
  - The stage is live mid-drag. At t = 1.0, dragging C to 0.8 made disc 4 fire (`04-stage-live-mid-drag`).
- **Check 8: ruler on release.** The ruler became 0–1.80 s on release (`02-band-released-crop`).
- **Check 2: undo/redo.** One `PerformUndo` gave delay 0.9, field 0.90 and ruler 1.50. Redo gave 1.2, 1.20 and 1.80.
- **Check 3: scrubbing.**
  - Ruler drag 0.3 → 1.0, empty lane A at 1.2 → 1.5, and empty lane B at 0.1 all moved only the playhead; all delays were unchanged.
  - A press/release on band B at 0.7 jumped the playhead to 0.70 and opened no undo group.
- **Check 4: past 0.** Dragging C 3 bar-widths left gave delay 0 and field 0.00 (`05`).
- **Check 5: switched-off band.** With C off, pressing its band set no band index. The drag scrubbed to 0.40 and the delay stayed 1.2 (`06-dim-band`).
- **Check 6: hover.** The hit overlays of enabled bands carry cursor id 5 (SlideArrow). Tooltips end "…Drag the band sideways to change when it starts."; the off band has no cursor and no drag sentence.
  - Minor: the tooltip says the timing twice ("A — 0.00s → 0.60s — Pyre Blast — fires at 0.00s and is gone by 0.60s.").
- **Check 7: name column.**
  - Gutter 13.8 pt for one-letter names; the ruler pad matches, and the bar and ruler both start at x = 478.7.
  - The "0.00s" tick box starts at the bar's zero, and its text is centred about 22 pt right of it.
  - Renaming D to a 60-character name widened the gutter to exactly 120 pt with an ellipsis (`07-long-name`).

**T-0358/3: other pads**
- `Z.Pad` and `ZuiPad` are untouched by 1709ec6f; it only adds `Z.PadRow`.
- BackSplash (Castle) renders its Position pad and X/Y as before (`13-backsplash-pad`).
- Mirage (it would load the owner's modified MirageStage), Pyre's fire-emitter offset and Zoetrope's muzzle offset were not opened.

**T-0361: New folder** (captures `20`–`24`)
- **Check 1: the label.**
  - At 900 pt the label shows "Assets/Demos/ChunksDemo/UseCases/Scratch" (exactly 40 characters, so not elided).
  - With a longer remembered folder it shows "Assets/Demos/Chunks… a rather long name", with the full path as tooltip, and the row stays on one line.
- **Check 2: picking a folder.** The native folder panel wasn't opened (it wedges the editor). I set `createFolder` to `…/Scratch/Picked` and rebuilt, which is what `ChooseCreateFolder` does after the dialog returns. Create then wrote `Scratch/Picked/V New Check.asset` and remembered the folder.
- **Check 3: outside Assets.** Not driven.
  - Code read: the inside-Assets test is a plain `StartsWith(dataPath)`, so a sibling folder like `<project>/AssetsOld` would pass as `AssetsOld/…`.
- **Check 4: reopen.** After closing the window and `GetWindow` again, New offered `Scratch/Picked`.
  - Side note: a window reopened with `GetWindow<ChunkWindow>()` is titled "ChunkWindow" in its OS title bar until the title is set.
- **Check 6: undo.**
  - One Ctrl+Z after Create destroyed the new object. The window showed "Missing (Chunk Spec)" and the .asset stayed on disk. This is the known AssetKit ghost from the memory notes, unchanged by T-0361 (`23-after-undo-create`); I deleted the ghost file.
  - ZuiAudit on the open create row: 0 findings at 900 pt, 3 off-screen at 560 pt (failure 3).

**T-0361/5, the PM question.** After any successful New+Create, that folder is written to EditorPrefs, even if Folder… was never pressed. From then on, New always offers it and ignores the open asset.
- Measured: remembered = `UseCases/Scratch/Picked`. I opened UC3, which lives in `UseCases`, and pressed New: it offered `Scratch/Picked`, while `FolderForNew()` would have given `UseCases` (`24-new-with-uc3-open`).
- The label makes this visible. Still, a user who has opened an asset elsewhere most likely expects "next to this one", and one who never touched Folder… doesn't expect a sticky choice.
- Suggestion: remember only an explicit Folder… choice, or offer the open asset's folder whenever an asset is open.

**T-0354: card identity** (captures `31`–`33`, `40`–`45`)
- **Check 1: colours.** Card edge, chip background, lane colour and stage outline were identical RGBA for all four cards; chips read 1–4.
- **Check 2: grip reorder.** Dragging C's grip onto D's top edge gave the order A B C D (UC3's stack was already A B D C).
  - Each card kept its colour on card, lane and stage outline; the chip numbers follow firing order (C stays 4).
  - One undo gave A B D C.
  - A grip press/release without moving neither folded the card nor made an undo record.
- **Check 3: duplicate.**
  - Duplicating B gave "B copy" right after it, colour slot 4 (yellow), same Pyre/offset/delay, a new id and a separate object; one undo removed it.
  - Debris Scatter with a Posterize modifier (scratch `V Debris Mods`): after duplicating, the modifier instances are separate objects. Levels 5 → 2 on the copy left the original at 5.
  - Observations:
    - A duplicate sits exactly on its source on the stage.
    - The two copies fire at the same instant, so both chips say "2" and the next cards jump to 4 and 5.
    - An unnamed Debris Scatter's copy gets no " copy" suffix, so the two cards read identically except for colour.
- **Check 4: UC2 numbers.**
  - At 0.05 s, chips 1 and 2 are nudged apart, not stacked.
  - At 0.3, 0.7 and 1.2 s all six are numbered, and the chips fade with their discs.
  - At 1.45 s only 4–6 remain, and at 1.58 s no number is drawn (one bare arc remains) (`40-uc2-*`).
  - The Pyre Blast card chip reads "1–6".
- **Check 5: lane name click.** With C folded and the pane at the top, clicking the "C" gutter label unfolded C, scrolled the pane (790 pt) and added the highlight class; the playhead didn't move (`41`).
  - Ruler scrubbing and band drag were re-exercised on the same build (T-0355 checks).
- **Check 6: second blast.** Name row at y = 311.1 before and after adding a second Pyre Blast. The Delay row was reserved (hidden) and became visible in the same slot (`42`, `43`).
  - Observation: with no Pyre picked, the two coincident discs fill the whole stage.
- **Check 7: headers.** On V3 with every box open, every card header row is one line (22.7 pt, children's centres within 0.2 pt, NoWrap) at both 900 and 560 pt; ZuiAudit 0 findings.
  - Its "folded = 25" counts `display:none` elements, not closed boxes.
  - Observation: at 560 pt the right-hand Preview/Timing pane is cut off at the window edge (`45-audit-560`), and ZuiAudit doesn't report it.

**T-0357: Fling** (captures `50`–`61`)
- **Check 1: Add menu.** With an empty recipe the menu row reads "Fling", disabled, with the tooltip "Nothing to fling yet — a fling moves what a Pyre Blast spawned, so add a Pyre Blast first." After adding a Pyre Blast it is enabled: "Flings what a Pyre Blast spawned along an arc."
- **Check 2: direction modes.**
  - Burst shows the disabled "Burst direction" row (but see failure 4).
  - Fixed shows an enabled "Direction" slider; at 0° all six fly right (`53`).
  - Outward (gravity 0, spread 0, ring centre at offset (2, 1)): at 0.30 and 0.60 s each blast keeps its spawn angle (0, 60, 120, 180, −120, −60) and its distance from (2, 1) grows (`54`).
- **Check 4: UC2.** UC2's committed YAML has only `inheritBurstDirection: 1`; its copy loads as Direction = Burst, and the arcs rise along 90° as before (`40-uc2-*`).

## Incident: two tracked assets were saved as a side effect

At 22:59:54 local, `Assets/Demos/ChunksDemo/Ring Blast.asset` (the owner's) and `UseCases/UC2 Flung Pyres.asset` were re-written on disk.
- The diffs are schema only: `colorSlot: -1`, `directionModeMigrated: 1`, `directionMode: 0`, spin 0/0. Load-time migration had dirtied them in memory.
- The most likely writer is AssetKit's Create, which calls `AssetDatabase.SaveAssets()` and so saves every dirty asset in the project; a New+Create ran in this walk around then. The same second also shows a domain reload in the console, so I can't pin it down more tightly.
- I restored both files with `git checkout -- <those two paths>` and refreshed. This was noted on T-0361, with `SaveAssetIfDirty(created)` suggested.

## Editor state left

- Out of Play mode, timeScale 1. ProtoGuyDemo is open and not dirty; no popups are open.
- The Chunks window is open on `Scratch/V Fling`, 900×900.
- The New-folder EditorPrefs key is deleted (it was unset before the walk).
- Scratch assets, for the PM to keep or delete (not committed):
  - `Assets/Demos/ChunksDemo/UseCases/Scratch/V3 Timed Sequence.asset` (copy of UC3)
  - `…/V2 Flung Pyres.asset` (copy of UC2)
  - `…/V Debris Mods.asset` (copy of SampledDebris + cell_000 source + Posterize, with a duplicated card)
  - `…/V Name Row.asset` (two empty Pyre Blasts)
  - `…/V Fling.asset` (Ring ×6 + Fling Outward, spin 180–360, face on)
  - `Scratch.meta`
  - Some of these may carry unsaved in-memory edits.

## Three buckets

- **Verified by probe:** every pass and fail in the table except the by-eye items. Values came from the model, the UI fields and the Undo stack, across separate editor frames.
- **Verified by eye:**
  - band outline, ruler tick and scale freeze;
  - dim band, gutter ellipsis, stage liveness;
  - create-row eliding and overflow;
  - card colours, duplicate, lane-click reveal, header lines;
  - UC2 numbering over time;
  - Fling Burst mirror staleness, Outward stage, the tick hidden under the chip;
  - Play render of the outward ring.
- **Not verified:**
  - a real mouse and real cursor hover (the cursor was checked as a style);
  - the native folder dialog and its outside-Assets rejection;
  - Mirage, Pyre and Zoetrope pads by eye;
  - Play-mode rotation by eye (round sprites; transforms only);
  - a byte-for-byte comparison of UC2's flight before and after T-0357 (no pre-change baseline).
