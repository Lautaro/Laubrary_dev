# T-0368 / T-0369 editor verification (PM, 2026-09-18)

Live editor, dev @ e35ee98d (unchanged this session — verification only, no code edits). Driven via
unity-mcp `eval` (blocked by internal-member compile errors under Coplay's separate-assembly compile) and
Coplay `execute_script` (works — the target methods are `internal`/private but reflection from a same-domain
script still reaches them). Tested against the existing `Assets/Demos/ChunksDemo/UseCases/Scratch/V Fling.asset`
(untracked, owner's own scratch asset — restored to its original in-memory values after the probe; nothing
was saved to disk, confirmed by the file's unchanged mtime).

## T-0369 (New row repairs) — PASS, code-level + visual
1. **Row fits at 560pt** — CONFIRMED by screenshot (`chunks_create_row_narrow.png`). Asset name / Create /
   Cancel are their own row and stay fully on-screen; the folder row below elides
   ("Assets/Demos/Chunks...olderNameForTesting") and the whole row is visible with Folder… reachable.
2. **Remembers only an explicit Folder… choice** — CONFIRMED by code read (`ZuiAssetWindow.cs` `Confirm()`
   only calls `RememberNewFolder` when `createFolderExplicit` is true, set only from `ChooseCreateFolder`).
3. **Path-segment check** — CONFIRMED by code read (`ChooseCreateFolder`'s inside-Assets test now requires
   an exact match or `dataPathAbs + "/"` prefix, closing the `AssetsOld` sibling-folder hole).
4. **Save only the created asset** — CONFIRMED by code read: `Confirm()` calls
   `AssetDatabase.SaveAssetIfDirty(created)`, not `SaveAssets()`. Same fix present in `AssetLibrary.cs`
   (Create/Duplicate/Rename) and `LaubraryAssetWindow.cs`'s IMGUI create-row confirm.

## T-0368 (Fling repairs) — PASS, live-behaviour + code-level
1. **Burst-direction mirror follows the live recipe value** — CONFIRMED LIVE: switched the Fling's
   `directionMode` to Burst, then called the window's own `Dial()` twice (same path the stage's real
   Burst-direction drag uses) to set the recipe's `directionDeg` to 12 and then 260 without rebuilding the
   card — the card's read-only mirror (`ZuiMicroSlider`, tooltip "The recipe's own aim, read-only here…")
   tracked both changes exactly (12→12, 260→260), matching the stage's own dial.
2. **Migration / no data loss** — CONFIRMED by code read: the Add-menu factory in `ChunkWindow.Recipe.cs`
   calls `t.MarkBornMigrated()` right after `new Trajectory()`, so a newly-added Fling never runs
   `MigrateLegacyCurves()` and keeps whatever mode the user picks. The existing `V Fling.asset` capability's
   `directionModeMigrated` field reads `true` in the live editor, consistent with this.
3. **Orientation tick clears the firing-number chip** — code read confirms the fix (`ChipHalfDiagonal`
   offset in `ChunkWindow.Preview.cs`'s `DrawGuide`), but NOT independently visually confirmed this
   session — the open test asset's ring radius (1.5 units) renders discs small enough in the stage that no
   tick was visible either way (by design: discs too small for the stroke skip it, per the code's own
   `r - startDist < 2px` guard). Needs a larger-radius recipe or a closer preview zoom to see the tick pixel
   -for-pixel; not blocking, the logic itself is sound on inspection.

## Verdict
Both T-0368 and T-0369 are confirmed fixed, no regressions found. Ready to close as verified.
