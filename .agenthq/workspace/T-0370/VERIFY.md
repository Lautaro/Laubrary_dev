# Merge 2 verification in the live editor (T-0370)

Walked 2026-09-16 on the live Laubrary Dev editor (PID 38044, dev @ 3e0859f7). The checklist is `ChunksUseCases/MERGE2-VERIFY.md`.

**How the window was driven**
- The same approach as T-0366: one gesture per CLI eval, and `Undo.IncrementCurrentGroup()` before every scripted action. Undo was read from Unity's own stack (`Undo.GetRecords`), then exercised with `PerformUndo`/`PerformRedo`.
- Sliders, MicroMinMax, right-clicks, double-clicks and stage drags went through `EditorWindow.SendEvent` with IMGUI MouseDown/MouseDrag/MouseUp events. That is the path real mouse input takes into UI Toolkit, and it matters for the T-0359 failure below.
- Buttons were driven with NavigationSubmit, pickers through the chip's own `OnDrop` (the drag-and-drop path). The Splash Source picker popup was opened by tapping its chip, filtered through its own search field, and picked through its own `_onPick`.
- Captures are PrintWindow shots at about 2.26× (screen-captured for the picker popup), or camera renders from an unsaved Play-mode stage scene (`4x`, `5x`). Montages show the stage at several playhead times.

**What was used**
- Every Chunks test ran on copies or new assets in `Assets/Demos/ChunksDemo/UseCases/Scratch/`.
- The Zoe tests ran on `Scratch/W Disc Zoe B.asset`, a fresh duplicate of `UC Floating Disc (walk copy)`.
- The owner's `Floating Disc Blowup`, `Sparks` and `Ring Blast` were only fired in Play mode and never edited or saved.

## Summary

| Task | # | Check | Result |
|---|---|---|---|
| T-0359 | 1 | Red Pyre + cyan tint shows darkened on the stage, matching a Play burst | **pass** |
| T-0359 | 2 | Cleared Blast draws nothing; a lone Single blast takes about a third of the stage | **pass** (see the zoom caveat under failure 1) |
| T-0359 | 3 | Fracture: real pieces fly apart; no source leaves the stage empty and shows the card line; unreadable sprite shows Read/Write | **pass** |
| T-0359 | 4 | Splash: sprite colours from its footprint, a sprite swap changes them; no sprite gives white from the origin plus the card line | **pass** |
| T-0359 | 5 | Seed-0 tooltips (Fling/Fracture/Splash/Debris), "Pins…" at 5, and the stage tooltip | **pass** |
| T-0359 | 6 | Ring + Fling arc peaks stay inside; no zoom-in while dragging On screen, glide after release; a disc drag holds the zoom | **FAIL** (the glide after release). Arc peaks and both holds pass |
| T-0359 | 7 | Regressions: disc drag sets Offset, rims + numbers, Fling ticks, no console errors after close/reload | **pass** (the known T-0358 missing undo for stage drags is still there) |
| T-0360 | 1 | Single card reads Blast → Pattern → Offset → Rotation → Scale/On screen → Tint/Alpha → Seed, with no Add alternate | **pass** |
| T-0360 | 2 | Line/Ring: Blast and Pattern don't move, pattern rows appear below; Single with a pool keeps Alternates | **pass** |
| T-0360 | 3 | Right-click Fixed/Range on Scale/Alpha/Spin; both modes drag with one undo each; double-click reset; 1–1 opens as Fixed | **FAIL (minor)**: a double-click in Fixed goes to the track minimum, and Range at the maximum gives an empty band. Everything else passes |
| T-0360 | 4 | Other MicroMinMax (Debris/Fracture/Splash/Fling) unchanged | **pass** |
| T-0363 | 1 | Zoe-triggered splash with empty Source/Sprite uses the Zoe's live colours from its footprint | **pass** |
| T-0363 | 2 | Source picker offers the Floating Disc Zoe by name; a standalone burst uses its first frame | **pass** |
| T-0363 | 3 | Spread 180 standalone gives a full 360° spray, runtime and preview alike | **pass** |
| T-0363 | 4 | One burst, one frame: splash colours match the cut frame | **pass** |
| T-0363 | 5 | Owner recipes before vs now | **reported**: `Floating Disc Blowup`'s splash went from an upward half circle to a full circle. Sparks and Ring Blast are unaffected |
| T-0364 | 1 | Private → Public creates a library asset with the same tuning and logs it | **pass** |
| T-0364 | 2 | One Ctrl+Z after step 1 | **FAIL**: the row points at a destroyed sub-asset and the new library asset becomes unloadable |
| T-0364 | 3 | Public ↔ Private toggled several times: no errors, no broken references | **pass** (observation: each Public click mints another library asset) |
| Also | – | Console clean through the walk | **pass**: 0 errors all along. Warnings are compiler warnings plus Unity's automated-mode and toolbar notices |

## Failures (each posted as a todo message on its owning task)

1. **T-0359/6: the zoom never glides in after a slider or button release.** (`14a`, `14b`, `14c`)
   - While "On screen" was dragged from 0.60 to 0.15, the zoom held at reach 10.21 (pass).
   - The slider captures the pointer, so UI Toolkit hands the release only to the slider. The window's TrickleDown `OnAnyPointerUp` on the root saw 0 ups. `pointerHeld` stayed true, and `shownReach` stayed 10.21 for 1.5 s against a measured 6.00, logged every editor update.
   - This reproduced through `EditorWindow.SendEvent`, the real input path: capture was true on down and false after up.
   - Pressing Replay (a Button) leaves `pointerHeld` stuck the same way.
   - A later release on a non-capturing element (a plain label, or the IMGUI stage) clears it. The glide itself then works: 10.21 → 6.00 in about 0.5 s.
   - **Second path to the same frozen zoom:** `draggingBlastCap` comes back non-null after every domain reload (seen after a script reload and after leaving Play mode). `EaseZoom` returns early, so a freshly opened lone blast sat at reach 0.5, filling the stage (`01`), instead of 1.25 (`02`) until such a click.
   - Posted on T-0359 todo 4 (the framing todo) rather than todo 1.
2. **T-0360/3 (minor): the Fixed-mode double-click resets to the track minimum.** (`24`)
   - The Pyre Blast card gives Scale and Alpha no defaults, so a double-click on Scale took 3.06 to 0.01 and the blast practically vanishes.
   - Range double-click still resets to the full span (0.01–8; −720–720 for Spin), as before.
3. **T-0360/3 (minor): Range at the track maximum shows an empty band.** (`25`)
   - Switching Alpha 1.00 from Fixed to Range nudges high to min(max, low + 5%) = 1.00. The control then shows "1.00 – 1.00" with a zero-width band and an empty track, although the value is 1.
   - The next card rebuild reopens it as Fixed.
4. **T-0364/2: one Ctrl+Z after Private → Public breaks the row.** (`30`, `31`)
   - The whole collapsed group undid in one step (undo list 32 → 28). Afterwards:
     - **The row points at the destroyed private sub-asset.** The field still holds its old instance ID (−30314), `objectReferenceValue` is null, and the row reads "Public · none ·". The sub-asset is gone from the Zoe file, and the Zoe is left dirty with that missing reference.
     - **The new library asset exists only on disk.** `Assets/Chunks/W Disc Zoe — Chunks.asset` still held the correct YAML, but `LoadMainAssetAtPath` returned null and the main type read DefaultAsset, even after `ImportAsset(ForceUpdate)`. So the data survived, but only on disk.
   - Redo re-pointed the row at an in-memory "W Disc Zoe — Chunks" with no asset path (not persistent), and the file stayed unloadable.
   - Cause: `RemoveObjectFromAsset` and `DestroyImmediate` are not undoable, while the field write and `RegisterCreatedObjectUndo` are.
   - Also flagged: `MakeChunksPublic` (like `MakeChunksPrivate`) calls `AssetDatabase.SaveAssets()`.
   - The broken duplicate and the ghost file were deleted afterwards. Check 3 ran on a fresh duplicate (`W Disc Zoe B`).

## Notes per check

**T-0359: preview honesty** (captures `01`–`15`, `40`–`41`, `46`)
- **Check 1: tint.**
  - Stage: `HollowBlast Plus` (average colour 0.99/0.13/0.01) with tint (0, 1, 1) draws its real frame with picture colour (0, 1, 1). The rim and path use average × tint (0, 0.12, 0.04), so the blast reads dark green over time (`03-cyan-over-time`).
  - Play: the spawned `PyreBlastPlayer` has SpriteRenderer colour (0, 1, 1) and renders the same dark green (`41`).
  - A stray dark shape at the lower left of the t = 0.5 frame is part of the Pyre's own sprite (checked against the exported frame), not a drawing bug.
- **Check 2: clearing and framing.**
  - An empty Blast card adds no guide. Clearing card 1's picker too left 0 guides and an empty stage (`04`).
  - Once the zoom was allowed to ease, the lone blast (radius 0.5) framed at reach 1.25 = 2.5 radii: the disc is 115 px of a 330 px stage height, about 35 % (`02`).
- **Check 3: Fracture.**
  - With the Floating Disc Zoe as Source, 4 textured pieces (`ChunkPreviewPiece`) fly apart over 0.05 / 0.5 / 1.0 s (`05`).
  - With both sources cleared: 0 guides and the card line "Nothing to cut: no source. A Zoe that fires this cuts its own frame." (`06`). The stage tooltip gained "Fragment Fracture — Nothing to cut…".
  - With `Background1` (not readable) as the fallback: "Can't cut 'Background1': its texture isn't Read/Write enabled." on the card and in the stage tooltip (`07`).
- **Check 4: Splash.**
  - `cell_001`: 36 dots in 10 disc colours from a ±0.4 footprint (`08`).
  - Swapped to `Book_5302_dmg0`: 5 teal/white/orange colours from a tall 0.4 × 0.88 footprint (`09`).
  - Sprite cleared: all dots white from (0, 0), with the card line "No sprite: sprays plain white from the origin, unless a Zoe fires it with its own colours." and the same line in the stage tooltip (`10`, `10b`).
  - Observation: "From footprint" stays on and shown when there is no sprite, where it does nothing.
- **Check 5: seed tooltips.**
  - Fracture, Splash, Debris and Fling all read "0 = a new roll on every burst: <what> change each time it plays…" at 0.
  - At 5 they read "Pins <what>, so every play comes out the same…", on both the field and its label.
  - The stage tooltip adds "A Seed of 0 rolls afresh on every real burst…" while an unseeded card is drawn.
- **Check 6: framing.**
  - V Fling (Ring × 6, Fling Outward) with gravity 10 and upward bias 10: across 41 sampled instants the worst path/disc extent was (6.25, 9.05), against a shown stage half-extent of (21.5, 12.0) at reach 10.2. Every peak is inside (`13`).
  - The stage disc drag (W Preview) held the zoom at 1.25 through three drag steps while the offset followed to (1.07, 0.71). On release the zoom widened with a glide to 1.79 (`15a`, `15b`).
  - Observation: at this zoom the firing-number chips cover the small discs.
- **Check 7: regressions.**
  - The disc drag set Offset, and the card's X/Y fields followed (1.070565 / 0.7137097, unsnapped as T-0366 noted). The stage drag still leaves no undo record (T-0358's known failure).
  - Rims carry the card colour, and the numbers show.
  - Fling discs carry `showAngle` ticks.
  - Closing and reopening the window (titled "Chunks"), then a script reload, gave 0 console errors.
- Observation: at 900 pt the Ring pattern's Count/Radius/Arc row wraps Arc onto a second line (`21`).

**T-0360: compact card + Fixed/Range** (captures `20`–`26`)
- **Check 1: row order.** Row y positions on a Single card: Blast 338, Pattern 362, Offset 386, Rotation 450, Scale/On screen 474, Tint/Alpha 498, Seed 522. There is no Add-alternate row (`20`).
- **Check 2: pattern switch.**
  - Ring and Line leave Blast at 338 and Pattern at 362. Add alternate, the placement rows, Stagger, Order and Pattern seed appear between Pattern and Offset (`21`).
  - After adding an alternate (`Blob Explosion Plus`) and switching back to Single, the Alternates box stays (`22`).
  - Observation: the Blast picker above doesn't say it is being ignored while the pool holds anything.
- **Check 3: Fixed/Range.**
  - Scale 1–1 and Alpha 1–1 opened as Fixed (value label "1.00"; tooltip "Drag to set one value… Right-click for Fixed/Range mode.").
  - Right-click shows a Fixed | Range switch (`23`). Range made Scale 1.00–1.40.
  - A high-handle drag moved it to 1.00–2.78 as **one** undo record. Undo gave 1.00–1.40, redo gave 2.78.
  - Fixed collapsed it to 1.00. A Fixed drag moved it to 3.06 in one record, with both ends equal (`24`).
  - Spin (Fling) opened as Range 180–360. Fixed gave 180, and a drag gave −176 in one record. Range then gave −176 to −104, and a double-click gave −720 to 720 (undone).
- **Check 4: other MicroMinMax.**
  - Every MicroMinMax on the Fracture, Splash and Debris cards opened as Range with "a – b" labels.
  - A middle-pan drag on Debris Speed moved 2.62–5.12 to 6.66–9.16 with the span held, in one record (undone afterwards) (`26`).

**T-0363: Palette Splash** (captures `11`, `12`, `42`–`46`, `50`–`52`)
- **Check 1: live colours.**
  - A fresh spawn of the Zoe copy idles on `cell_000`. `cell_004` shares only 1 of its 13 colours with `cell_000`, so the renderer was set to `cell_004` just before `Health.Kill()`, making the test tell live from authored. The renderer read `cell_000` again right after Kill (the death clip), but the burst had already taken `cell_004`.
  - The death row pointed at `W Splash Live` (a single Palette Splash with empty Source and Sprite): 34/34 particles are `cell_004` colours, 10 of them absent from `cell_000`. They spawned inside a ±0.47 footprint (`45`). Standalone, the same recipe sprays white from one point (`43`).
- **Check 2: picker and first frame.**
  - The Source picker's search "Floating Disc" lists `Floating Disc`, `Floating Disc Hit Plus` and `UC Floating Disc (walk copy)` with thumbnails (`11`).
  - Picking the walk copy set `sourceVisual`, and the preview resolved `cell_000` (`12`). A standalone Play burst gave 35/35 particles in `cell_000` colours (`42`).
- **Check 3: Spread 180.**
  - `W Splash Live` (gravity 0): runtime launch angles span −177…158, with 16 up / 15 down (`43`).
  - The preview gives angles −177…174 and a cone of half-angle 180 (`46`).
- **Check 4: one frame per burst.**
  - The UC6 copy (Layer Plan, Big pyre, Fracture with the owner's Floating Disc Zoe as Source, two Splashes with empty sprites, Small pyres) fired from the Zoe showing `cell_004`.
  - 46/46 splash particles are `cell_004` colours (11 of 12 absent from `cell_000`). Fragment1–3 contain only `cell_004` colours (at most 1 shared with `cell_000`). The hit row's debris chunks also came from `cell_004` (`44`).
- **Check 5: owner recipes.**
  - `Floating Disc Blowup` has one Palette Splash (Spread 180, Direction 90, gravity 6, no sprite). The old code threw ±90° (upward only). In a timeScale 0.05 Play burst, 8 of 15 particles launched downward (angles −159…−37), so this owner splash visibly doubled, from a half circle to a full circle (`50`; its dots are small next to the fragments, so the angles are the evidence).
  - The checklist's "Spread < 180" rule undercounts: 180 doubles too.
  - `Sparks` (Debris Scatter) and `Ring Blast` (Pyre Blast + Trajectory) have no Palette Splash and are unaffected (`51`, `52`, records only).
  - Nothing was saved, and the owner's assets are clean in git and in memory.
  - Note: the first attempt at these three renders failed because my own cleanup destroyed the pooled objects. That error was mine; Play mode was restarted and the renders re-taken.

**T-0364: Private → Public** (captures `30`, `31`)
- **Check 1: making it public.**
  - The death row pointed at `UC6 Disc Breakup`. Private embedded "W Disc Zoe — Chunks (Private)", JSON-identical to UC6.
  - Public created `Assets/Chunks/W Disc Zoe — Chunks.asset` (6 capabilities, JSON-identical), and the row showed its chip (`30`).
  - The console logged "[Zoetrope] Chunks made public: Assets/Chunks/W Disc Zoe — Chunks.asset". The owner's console has all three level filters off, so the log is invisible in the window until they are turned on.
- **Check 2: undo.** See failure 4.
- **Check 3: toggling.**
  - Three Private/Public rounds on `W Disc Zoe B`: each Private made the file hold 4 ChunkSpecs, each Public brought it back to 3. The row always resolved to a main asset with UC6's exact tuning. 0 errors.
  - Each Public minted a new asset: `W Disc Zoe B — Chunks`, `… Chunks 1`, `… Chunks 2`. Two were left unreferenced, which clutters the shared library (noted on T-0364).

## Owner assets

- No owner asset was edited, saved or restored. `git status` on `Assets/Demos/ChunksDemo/*.asset` and `Assets/Zoetrope/` was clean before and after every step that saves (CopyAsset, Private, Public).
- An in-memory dirty check over every loaded persistent asset showed only my Scratch assets and ten importer shaders (which were already dirty).
- `AssetDatabase.CopyAsset` itself saved my dirty Scratch recipes (their mtimes jumped at the copy). This is worth knowing next to the SaveAssets hazard.
- No `git checkout` was needed.

## Editor state left

- Out of Play mode, timeScale 1. `ProtoGuyDemo` is open and not dirty.
- The Chunks window is open on `Scratch/V Fling`, and the phantom drag has been cleared. The Zoes window I opened is closed.
- The console's level filters are left exactly as found (flags 7170); they were switched on only for the moment of each read.
- Scratch assets, all under `Assets/Demos/ChunksDemo/UseCases/Scratch/`, not committed, for the PM to keep or delete:
  - `W Preview.asset`: two blasts, HollowBlast with cyan tint at offset (1.07, 0.71), scale 3.06.
  - `W Fracture.asset`: UC5 copy; Source cleared, `Background1` as fallback, seed 5.
  - `W Splash.asset`: UC4 copy; Source = walk-copy Zoe, seed 5.
  - `W Splash Live.asset`: UC4 copy; no sprite, gravity 0.
  - `W Disc Zoe B.asset` and its generated `W Disc Zoe B.states.cs`. Its death row points at `W Splash Live` in memory, unsaved; on disk it points at `… Chunks 2`.
  - `W Disc Zoe B — Chunks.asset`, `… Chunks 1.asset`, `… Chunks 2.asset`: made by the Public button in `Assets/Chunks`, then moved here with their GUIDs kept. `Assets/Chunks` is empty again.
  - Some of these carry unsaved in-memory edits.
- T-0366's `V Fling.asset` and `V Debris Mods.asset` were edited and then saved by CopyAsset:
  - V Fling: gravity 10, upward bias 10, On screen 0.15, Spin −176 to −104.
  - V Debris Mods: seed 5.

## Three buckets

- **Verified by probe:** every pass and fail in the table. Values came from the model, the UI controls, the preview frames and Unity's undo stack, across separate editor frames. Runtime colours and angles came from the spawned SpriteRenderers.
- **Verified by eye:**
  - the darkened tinted blast on the stage and in Play;
  - the lone-blast framing, and the frozen versus eased zoom;
  - fracture pieces flying, and the source-state lines;
  - splash colours, footprint and sprite swap;
  - the picker listing the Floating Disc Zoe;
  - the full-circle spray in the stage and in Play;
  - Fixed/Range labels and the right-click switch;
  - the empty Range band at the maximum;
  - the Zoe row before and after undo;
  - the Zoe-triggered break-up render.
- **Not verified:**
  - a real hand on the mouse (all gestures were synthesized through the window's event path) and real hover tooltips;
  - the owner-splash doubling by eye: the dots are too small next to the fragments, so it rests on measured angles;
  - Sparks/Ring Blast before-vs-now comparison (no Palette Splash, so only renders were taken);
  - Mirage.
