# T-0199 — Cherry Framing function inventory (Pyre vs Shaper, before the merge)

Read off source, not docs. Pyre: `Editor/Pyre/PyreWindow.CherryFraming.cs` (pre-T-0199, 461 lines) plus its playback hooks in `PyreWindow.cs:139-280,387-389,634-638`. Shaper: `Editor/Shaper/ShaperWindow.Cherry.cs` (pre-T-0199, 554 lines) plus `ShaperWindow.cs:1061,1196-1231,1287-1305,1350-1365` and `ShaperWindow.Preview.cs:47-52,99-180,231-281`.

**32 panel functions in Pyre. Shaper had 22. The 10 it lacked are the entire source-frame half plus the tile size.**

| # | Function | Pyre (file:line, pre-merge) | Shaper before | After T-0199 |
|---|---|---|---|---|
| 1 | "Cherry Framing" section, header enable toggle | CherryFraming.cs:81-87 | had | shared |
| 2 | Absence rule — no grids built while cherry is off | :91-96 | had | shared |
| 3 | "Source frames" box (`BoxKeyed`, "stack" icon) | :98-101 | **LACKED** | shared |
| 4 | Source thumbnail grid, one tile per frame, flex-wrap | :166-179 | **LACKED** | shared |
| 5 | Source tile frame-number badge (absolute, top-left) | :196-199 | **LACKED** | shared |
| 6 | Source tile click → select single | :207 | **LACKED** | shared |
| 7 | Source Shift-click → range select | :205 | **LACKED** | shared |
| 8 | Source Ctrl/Cmd-click → toggle select | :206 | **LACKED** | shared |
| 9 | Source double-click → append slot | :204,213-224 | **LACKED** | shared |
| 10 | "+ Add selected" → append every selected source frame in index order | :110-111,226-233 | **LACKED** | shared |
| 11 | "Tile px" dial 32–256, persisted | :105-109 (`Pyre.cs:1249`) | **LACKED** (hardcoded `CherryTileSize = 64f`) | shared; Shaper stores in EditorPrefs |
| 12 | Source selection restyled in place, no rebuild mid-gesture | :151-156 | **LACKED** | shared |
| 13 | Source thumbnail cache outliving preview refills | :36-57 | had (lazy, keyed by source index) | each host keeps its own |
| 14 | "Cherry slots" box | :114-118 | had | shared |
| 15 | Slot card play-order badge | :269 | had | shared |
| 16 | Slot card "M" MultiFrame badge | :270-271 | had | shared |
| 17 | Slot card × → delete slot or whole selection | :272-276,396-402 | had | shared |
| 18 | Slot card thumbnail body | :279-284 | had | shared |
| 19 | Slot card length badge (`1.5×` / `1–3×`) | :286-290 | had | shared |
| 20 | Slot click / Shift-range / Ctrl-toggle select | :311-320 | had | shared |
| 21 | Drag-reorder, whole multi-selection moves, NO PointerCapture | :292-307,322-335 | had | shared |
| 22 | Right-click → popover anchored to the card | :294,337-345 | had | shared |
| 23 | Popover title (`Slot N` / `N slots selected`) | :359 | had | shared |
| 24 | Popover "Source frame" dial (single selection only) | :361-365 | had | shared |
| 25 | Popover "Variable length" toggle | :367-370 | had (labelled "Randomise length") | shared, Pyre's label |
| 26 | Popover "Length ×" dial | :372-375 | had | shared |
| 27 | Popover Min / Max dials on one row | :377-381 | had ("Min ×"/"Max ×") | shared, Pyre's labels |
| 28 | Popover "MultiFrame" toggle | :383-386 | had | shared |
| 29 | Popover Duplicate (deep copy, inserted after the block) | :389-390,414-444 | had | shared |
| 30 | Popover Delete | :391-392,404-412 | had | shared |
| 31 | Delete / Backspace removes the selection | :449-453 | had | shared |
| 32 | Ctrl/Cmd+D duplicates the selection | :454-458 | had | shared |

## Shaper extras — no Pyre equivalent, all kept

| Extra | Where it was | Where it is now |
|---|---|---|
| Loop gap dial in the panel (Pyre's `previewDelay` sits in its transport row, `PyreWindow.cs:634-638`) | Cherry.cs:148-151 | host hook `BuildExtraCherrySlotBoxRows` |
| Preview-only Zound cue: "Fires on slot" dial + picker button, right-click auditions | Cherry.cs:497-552 | host hook `BuildExtraCherrySectionRows` |
| ♪ badge on the slot the cue names | Cherry.cs:224-225 | host hook `DecorateCherrySlotHeader` |
| MultiFrame candidate-frame list + "+ Add current frame" + per-slot Seed (Pyre only ever READ `multiFrameSources`; it has no editor for it) | Cherry.cs:363-416 | host hook `BuildExtraCherryPopoverRows` |
| "+ Add slot" from the frame on screen | Cherry.cs:129-147 | host hook `BuildExtraCherrySlotBoxRows` |
| Cherry playhead ("Cherry slot" scrubber), T-0190 | ShaperWindow.cs:1210-1231,1287-1305 | untouched |
| Status line ("Cherry slot 2/5 → frame 7", "Cherry gap — blank between loops"), T-0188/T-0190 | ShaperWindow.Preview.cs:162-180 | untouched |
| Deterministic draws (`ShaperCherry.Draw`, lowbias32) instead of Pyre's `Random` — BC-1.3 | Runtime/Shaper/ShaperCherry.cs | untouched; the reason the two slot types were NOT unified |

## Judgment calls made without asking

1. **Adapted, did not unify, the slot type.** `CherryFrame.PickSourceFrame`/`ResolveLength` use `UnityEngine.Random` and `System.Random`; both are banned from a Shaper generator path (BC-1.3, `ShaperValue`'s own doc). The panel exchanges the six authored scalars (`PyreCherrySlotView`) instead, so neither runtime asmdef gains a dependency and no serialized field changes type.
2. **The builder is an object, not a static class** like `PyreShapeCards`. Selection and drag state are per panel; two open windows must not share them.
3. **Tile px now repaints the grids in BOTH windows.** Pyre's dial wrote the value and never rebuilt anything, so it did nothing until an unrelated rebuild. Pyre's pixels are unchanged; a previously dead control now works.
4. **A source-frame edit resets playback** (Shaper already did; Pyre did not), so the preview shows the change immediately.
5. **Shaper's tile size is `EditorPrefs`, not a document field.** View state, no Undo (ui-layout-rules), and it avoids editing `ShaperDocument.cs` while siblings are in it. Default 96 = Pyre's default.
6. **`com.Lautaro-Arino.Laubrary.Shaper.Editor` now references `…Pyre.Editor`** — required for a shared builder living in Pyre's editor asmdef, and acyclic (Pyre.Editor references nothing Shaper).
