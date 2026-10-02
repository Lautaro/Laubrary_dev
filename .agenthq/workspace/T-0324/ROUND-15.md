# Round 15 (T-0324) — report written by the PM

The worker (general-purpose, claude-opus-5, sole editor driver) completed every measurement and fix in the brief and died during cleanup when the Claude Code login expired (2026-09-09 ~07:40Z), before writing this file or posting its handover. This report is reconstructed by the PM from the agent's progress notes on the board and the outputs it left under `out/`, `probes/` and `shots/`. Nothing here is claimed beyond what those notes and files state.

## What was fixed (all compiled clean, verified by the PM at HEAD before commit)

1. **Mirage has a route to its own preview stage** — `Editor/Mirage/MirageWindow.cs` `BuildStageRow`: one fixed row in three states (already open / openable / no stage in the project), greyed with the reason, copying Chunks' `EnsureMirageStageOpen`. The section tooltip no longer claims previewables animate in Edit mode (measured: three renders of the rig camera 3.4 s apart returned one frame hash; they only animate in Play).
2. **MicroSlider numeric input grows to the value it prints** — `Zui/Toolkit/ZuiMicroSlider.cs` `FitNumField`: Pyre's shipped `Proper Blast` Size 1.366666 (field 45.8 → 63.6 px, text 53.3 px in a 57.3 px box; was 12 px past the slider edge). Returns to the 46 px resting width when the value fits, so T-0314's row arithmetic is untouched at rest. The display is never rounded (an isDelayed editable field would commit the rounded number). `ZuiAudit.cs` gains a clipped-input check.
3. **A Zoe reaction Clip was a raw text field for a laumination name** — on every fresh Zoe (empty option list degraded to a text input) AND on every composite character (`GetClipNameOptions` had no branch for `CompositeLauminaryView`, so ProtoGuy showed a blank type-in box). `Editor/Zoetrope/ZoetropeWindows.cs`: options are the union of the parts' declared animations; the empty state is a greyed picker reading "None declared" with the reason; the "pick one above" hint only says so when there is something to pick.
4. **Zoe's ▶ preview buttons grey with a reason** when the character declares no animation (measured: pressing ▶ on a new Zoe changed nothing, wrote nothing, logged nothing).
5. **Asset grid names elide** — `Editor/AssetKit/LauAssetGridGUI.cs`: the `Add Previewable` popup (first pass ever to see it open; `out/popup-audit`) had 16 of 47 names cut mid-word, three to the identical visible string. Names now get the whole cell and an ellipsis; the full name stays on the tooltip.
6. **Three pane-filling buttons in ZoeWindow** — Preview in Mirage (873 px) and two Paint Muzzle… (859 px) filled the whole pane; sized to 170/130 px.
7. Mirage's Previewables section tooltip corrected (see 1).

## Verified by eye (agent captures under `shots/`)
Round 14's landings: Pyre Size prints 1.366666 in full; SpriteFx/Lathe/TextSplash libraries are name chips with no empty squares; Zoe keeps its 6 thumbnails including the one honest hole; TextSplash Baked font elides with a real ellipsis. Cold walks done twice: Larder (a Ware from empty shows intact/dmg 1/dmg 2 on the first screen after Create), Zoetrope (Clip and preview affordances honest in the empty state, identical on the second run). Temporal: the Shaper demo document plays (frames 11 and 3 visibly different in real captures, three samples > 1.5 s apart), then paused with every counter 0.

## Verified by probe
Fill sweep against HEAD (`out/fill-sweep-t324.tsv`): 414 rows, ZERO regressions against rounds 13, 14 and T-0277. All eight windows report ZERO ZuiAudit findings and clipped-input = 0 (`out/audit-*`, `out/allsweep-final.txt`).

## Not verified / PM cleanup
- No human has operated any of it.
- The agent's cleanup did not run: the PM removed the empty `Assets/Larder/` folder and its meta (scratch Ware deleted by the agent, folder left), and reverted `Assets/Mirage/Test 2.asset` (only its baked preview texture blob had been re-rendered by probing; no authored value changed). The demo scene was left dirty by probes and was not saved.
- Floating tool windows share one screen rect, so only the topmost is capturable; the agent added a solo-window probe (`probes/`).

## Verdict
**FOUND: 7 non-trivial items, next pass needed** (PM-derived from the agent's notes; every fix above landed).
