# PyrePlus — CherryFraming feature

## Goal

Add an authoring tool to PyrePlus that lets the user cherry-pick frames from the spec's own baked Pyre animation (the existing `frameCount` rendered frames), producing a sub-selection that **takes over the preview**. Below the preview the user gets two stacked grids — the source animation on top, the cherry result on the bottom — plus per-cherry-frame right-click context menus (Variable Length, MultiFrame), plus preview playback options (Delay static/MinMax, Zound with optional frame offset).

## Scope & decisions (confirmed with user)

- **Source = the spec's own baked Pyre frames.** No external Lauminary reference. The grid reuses the same `PyrePlusRenderer.RenderFrameTexture(s, i)` call that the existing filmstrip uses — the cherry grid is just two stacked contact sheets.
- **Cherry list drives the preview when enabled.** Empty cherry list ⇒ blank preview (per user).
- **Preview loop:** full cherry sequence → Delay → loop forever.
- **MultiFrame:** at each playback entry into a multiframe, randomly pick ONE of its source frames and treat it as the frame for that slot. Replays can pick a different one (preview sees randomness; runtime will too once shipped).
- **Variable Length:** a cherry frame's duration multiplier (1.0 = normal). Static OR MinMax.
- **Right-click context menu on a cherry tile** opens: Variable Length (Static / MinMax), MultiFrame (add/remove source frames from this slot's group).
- **Preview options (live preview only — do NOT trigger a sound elsewhere):**
  - **Delay**: how long nothing plays between preview iterations. Static OR MinMax range slider.
  - **Zound**: optional sound + optional frame offset (play on frame N instead of frame 0).
- **Existing `previewStrip` (filmstrip) stays.** CherryFraming is a new parallel mode that lives below the right pane's chrome. They can coexist — when cherry is enabled, the main preview plays the cherry result; the source grid (top) and cherry grid (bottom) below always show.

## Files touched (5 files)

1. **NEW** `Assets/Packages/Laubrary/Runtime/PyrePlus/CherryFrame.cs`
   - Serializable `CherryFrame` class — one cherry slot.
   - Fields: `int sourceIndex` (default frame in this slot), `float lengthMultiplier = 1f`, `bool useMinMaxLength`, `float minLengthMultiplier = 1f`, `float maxLengthMultiplier = 1f`, `bool multiFrame`, `List<int> multiFrameSources`, `int multiFrameRandomSeed`.
   - Helper: `int PickSourceFrame(Random.State)`, `float GetLengthMultiplier(Random.State)`.

2. **EDIT** `Assets/Packages/Laubrary/Runtime/PyrePlus/PrebuiltAssetScripts/PyrePlusSpec.cs` (≈line 1212 in the existing `PyrePlusSpec` class)
   - Add fields to `PyrePlusSpec`:
     - `bool cherryEnabled = false;` — master switch.
     - `List<CherryFrame> cherryFrames = new();` — the cherry sequence (empty ⇒ blank preview when enabled).
     - `ZUIValue previewDelay = new ZUIValue(0f);` — ZUIValue (Static / MinMax) so it matches the rest of the codebase.
     - `string previewZoundName = "";` — by-name Zound (matches the codebase convention).
     - `int previewZoundFrame = 0;` — frame index to trigger on; 0 = on first frame of the cherry sequence.

3. **NEW** `Assets/Packages/Laubrary/Editor/PyrePlus/PyrePlusWindow.CherryFraming.cs`
   - Owns the entire CherryFraming UI build + interaction.
   - **BuildCherryPanel(host)** — adds a Z.Section "CherryFraming" to the right pane below the chrome (passed by BuildAsset in step 4). Holds a "Cherry enabled" toggle, the Delay + Zound controls, then a fixed-height IMGUI island that draws the source + cherry grids.
   - **DrawCherryGrid(s, view)** — IMGUI: two stacked contact sheets (source grid top, cherry result grid bottom). Reuses the layout math from `DrawFilmstrip` (PyrePlusWindow.Preview.cs:141-152) — same tile size, same wrap-by-cols. Source tiles show `stripCache[i]`; cherry tiles are a SECOND contact sheet where each tile shows the chosen source frame's sprite. Click a source tile to **add** it as a new CherryFrame at the end. Click a cherry tile to **remove** it. Drag = reorder. Right-click a cherry tile → ZuiMenu-style GenericMenu context (Variable Length Static/MinMax, MultiFrame toggle + sub-menu to add/remove source frames).
   - **ShowCherryContextMenu(tileIdx, mousePos, s)** — uses `UnityEditor.GenericMenu` (matches the codebase convention) for Variable Length / MultiFrame actions. The length and multi-frame picks route through `Undo.RecordObject` + `EditorUtility.SetDirty`.
   - **OnCherryChanged(s)** — bumps `previewDirty` + `preview?.MarkDirtyRepaint()` so the main preview redraws.
   - **DrawBackdropForCherry(...)** — fills the cherry strip cache when needed, mirroring `EnsureStripCache` (`PyrePlusWindow.Preview.cs:196`).

4. **EDIT** `Assets/Packages/Laubrary/Editor/PyrePlus/PyrePlusWindow.cs` (3 surgical edits)
   - In `BuildAsset` (≈line 230): after `rightPane.Add(chrome)`, add `BuildCherryPanel(rightPane, s)`.
   - In `Tick` (line 137): change the frame advance to drive the cherry sequence when `cherryEnabled` and `cherryFrames.Count > 0`. Specifically:
     - Maintain a private `int _cherryPlaybackIdx = 0` and `float _cherryDelayRemaining = 0` (window-scoped, not persisted).
     - When `cherryEnabled && cherryFrames.Count > 0`:
       - During `_cherryDelayRemaining > 0`, decrement by `(dt * previewFps)`. No frame advance.
       - Otherwise, advance one step in the cherry sequence. If the current CherryFrame is `multiFrame`, randomly pick one of `multiFrameSources` (or fall back to `sourceIndex` if empty), then `frame = pickedIndex` (the source frame index).
       - If Variable Length, weight the accumulator so the frame stays longer (multiply acc-spend by `lengthMultiplier` — already implemented via direct counter).
       - On sequence end, reset to 0, set `_cherryDelayRemaining = EvalDelay()`, and if `previewZoundName != ""`, play it (once per loop start, see Delay handler below).
     - The main preview at line 97 (`int cur = Mathf.Clamp(frame, …)`) keeps reading `frame`, so the preview displays whatever source frame is current.
   - In `DrawPreview` (line 97-98): when `cherryEnabled && cherryFrames.Count == 0`, the preview draws a blank backdrop (one extra `EditorGUI.DrawRect(view, Color.clear)` branch). When `cherryEnabled && cherryFrames.Count > 0`, the preview behaves identically (just `frame` is now driven by the cherry sequencer). The transport scrubber becomes inactive in cherry mode (we won't fight it — clicking the scrubber still works, it just jumps to that source frame).
   - The `RefreshTransportReadout` already reads `frame` — its label can keep "frame N/M" (where M = source frameCount), or we can show "cherry N/M" with M = cherryFrames.Count when in cherry mode. Will add a tiny conditional.

5. **EDIT** `Assets/Packages/Laubrary/Editor/PyrePlus/PyrePlusWindow.Preview.cs` (1 small edit)
   - The IMGUI preview island already uses the source frame cache; cherry mode piggybacks on it. The **only** preview.cs change is: skip the existing `DrawFilmstrip` branch when cherry is enabled (or coexist — let the user decide). Easiest: when `cherryEnabled` is on, the existing single-frame preview path runs (it shows the current source frame, which is exactly what the cherry sequencer is driving). The two stacked grids in the new Cherry panel below remain the only grid visualisation.

## Implementation order

1. **Data model** — `CherryFrame.cs`, add the 5 fields to `PyrePlusSpec`. Pure additions — zero risk to existing assets (new fields default to false/empty, byte-identical to before).
2. **Preview playback changes** — modify `Tick` to drive cherry sequence + delay + zound on loop. Test by creating an empty asset and confirming it's byte-identical to before (cherryEnabled defaults false → no behavioural change).
3. **Cherry grid UI** — `PyrePlusWindow.CherryFraming.cs`. Build the source + cherry grids in a fixed-height IMGUI island below the chrome. Click-add / click-remove / drag-reorder. Reuse `DrawFilmstrip`'s layout math.
4. **Right-click context menu** — `GenericMenu` on a cherry tile: Variable Length (Static / MinMax), MultiFrame (toggle + add/remove sources).
5. **Preview options controls** — the `Z.Section` panel above the grid holds the Cherry toggle, the Delay ZUIValue, the Zound picker, the Zound frame offset.

## Trade-offs / things that are NOT in scope

- **No runtime playback changes.** MultiFrame random pick happens in the editor `Tick` only. Runtime playback will get a parallel implementation in a future slice (the data model is already shaped for it).
- **No save-data migration.** All new fields default to off/empty. Existing assets deserialise unchanged (byte-identical).
- **The existing `previewStrip` mode (filmstrip toggle) is untouched.** It's a separate visualisation that can run alongside CherryFraming — when both are on, the main preview shows the cherry-driven source frame, the Strip shows the source contact sheet, the new Cherry panel shows source + cherry stacked. Three views, all in sync via `frame`.
- **No batching / multiselect.** The user said "common and logical edit tools for browsing, selecting and previewing" — click adds one, click on cherry removes one, right-click configures the current one. Drag-reorder is included. Shift-click range-select and box-select are NOT in scope; can be added later if requested.

## Verification plan

Per the Handover Walk rule, every requirement must trace to code before declaring done. After implementation I'll trace each:

1. Source animation shows in a grid → `DrawCherryGrid` upper half calls `GUI.DrawTexture(tileR, stripCache[i])`.
2. Final animation shows in a grid → `DrawCherryGrid` lower half draws each `cherryFrames[i]`'s source sprite.
3. Select / unselect frames → click a source tile appends a `CherryFrame`; click a cherry tile removes it.
4. Variable Length right-click option → `GenericMenu` item opens a sub-menu (Static / MinMax).
5. MultiFrame right-click option → `GenericMenu` item toggles + adds source indices.
6. Preview always shows final result when enabled → `Tick` drives `frame` from cherry sequencer.
7. Delay control → `previewDelay` ZUIValue, applied between iterations.
8. Zound + frame offset → `previewZoundName`, `previewZoundFrame`, played via `ZoundEngine.PlayZound` at the offset.
9. Preview-only sound (does NOT trigger elsewhere) → only the cherry `Tick` calls PlayZound.

After implementation, the user opens Laubrary Dev in Unity, I'll provide compile-error list (zero), then walk through the editor with them.
