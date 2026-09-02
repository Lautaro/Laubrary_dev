# T-0199 — the walk, both windows

Editor: `D:\UNITY\Laubrary Dev - Shaper` (`Application.dataPath` verified). Compile clean before and after. Walked on throwaway duplicates (`Assets/_T0199/T0199WalkDoc.asset` copied from the demo doc, `T0199PyreSpec.asset` created fresh), both deleted afterwards. **`ShaperDemoDoc.asset` was never opened or saved by a probe.**

Gestures are real UITK events built through the dispatcher's own `GetPooled`: `PointerDownEvent`/`PointerUpEvent` (with `shiftKey`/`ctrlKey`/`button 1`/`clickCount 2`), `NavigationSubmitEvent` on Buttons, `KeyDownEvent` for Delete and Ctrl+D. Dials are driven through `ZuiMicroSlider.SetValue(v, notify:true)` — the path a drag takes; the public `value` setter is deliberately non-notifying and does **not** exercise the wiring.

## Shaper — 32/32 functions plus 6 extras

| Check | Result |
|---|---|
| F1 section + header enable toggle | PASS |
| F2 absence rule: cherry off removes both grids; back on rebuilds them | PASS |
| F3 Source frames box | PASS |
| F4 one source tile per frame | PASS (16 tiles / 16 frames) |
| F5 frame-number badge | PASS |
| F6 click selects | PASS |
| F7 Shift = range | PASS (tiles 2–5 → 4 selected) |
| F8 Ctrl = toggle | PASS (4 deselected → 3 selected) |
| F9 double-click appends | PASS (slots 5→6, last sourceIndex 7) |
| F10 "+ Add selected" in index order | PASS (slots 2→5, added [2,3,5]) |
| F11 Tile px resizes BOTH grids + persists | PASS (96→140 source and slot; EditorPrefs 140) |
| F12 selection restyles in place, element survives the press | PASS |
| F13 every source tile carries a thumbnail | PASS (16/16) |
| F14 Cherry slots box | PASS |
| F15 play-order badge | PASS |
| F16 "M" MultiFrame badge | PASS |
| F17 slot × removes | PASS (6→5) |
| F19 length badge shows the range | PASS ("1.5–4×") |
| F20 slot click / Shift range / Ctrl toggle | PASS |
| F21 drag-reorder, press A release B, no capture | PASS ([6,9,2,3,5,7] → [9,2,6,3,5,7]) |
| F22–F30 right-click popover + its 8 controls | PASS |
| F24b–F28b each popover control WRITES the slot | PASS (sourceIndex 9→11, length 2.5, useMinMax, min 1.5 / max 4, multiFrame) |
| F29b Duplicate deep-copies (length + multiFrame carried) | PASS |
| F31 Delete key | PASS (5→4) |
| F32 Ctrl+D | PASS (4→5) |
| X1 extras present: Zound cue, Loop gap, "+ Add slot" | PASS |
| X2 MultiFrame candidate list + seed + "+ Add current frame" | PASS **after a fix** — see below |
| X3 ♪ badge follows the Zound cue slot | PASS |
| X4 Zound cue clamps when its slot is deleted | PASS (cue 2 → 1 as the list shrank) |
| S-temporal playback walks the slot list | PASS — slots [6,11,0] played as frames [6,11,11,11,0,−1,−1,6,…]: slot 1's 1.5–4× hold occupies three beats, then the loop gap's two blank beats, then back to slot 0 |

## Pyre — unchanged, and its own extras absent

| Check | Result |
|---|---|
| P1/P3/P4 section, Source frames box, 16 tiles / 16 frames | PASS |
| P2 absence rule | PASS |
| P5 badge "1" absolute at (2,2) | PASS (inline style) |
| P6/P7 click + Shift range | PASS |
| P10 "+ Add selected" in index order | PASS ([1,2,3]) |
| P11/P11b Tile px reads AND writes `previewCherryStripSize`, resizes the grid | PASS (96 → 128 on both) |
| P14 a card per slot | PASS |
| P21 drag-reorder | PASS ([1,2,3] → [2,1,3]) |
| P22–P30 popover, Pyre's own eight controls | PASS |
| P31 Delete key | PASS (4→3) |
| P32 Ctrl+D | PASS (3→4) |
| P-temporal playback walks the slot list | PASS (slots [2,2,1,3], frames seen [2,2,1,3,2,2,1]) |
| P-extras Pyre draws NO Shaper extras (no "+ Add slot", no Loop gap, no Zound cue) | PASS |
| P-extras2 Pyre's popover shows no candidate list | PASS |

### Pyre pixel-compare

Inline style values read out of the live panel, compared against the literals in the pre-extraction `PyreWindow.CherryFraming.cs` (which the commit diff shows verbatim):

```
source tile : w=96px h=96px mr=2px mb=2px bt=2 bl=2     (96 = previewCherryStripSize)
badge       : pos=Absolute left=2px top=2px
slot card   : w=96px h=Null mr=2px mb=2px bt=2 bl=2     (height deliberately unset, as before)
× button    : w=16px h=16px mt=0 mb=0 ml=0 mr=0
```

Every value matches the old literal. `resolvedStyle` was tried first and is **not** usable as evidence here — it reports DPI-scaled points (2px → 2.222) and `NaN` for an element that has not been laid out since its last rebuild, which is what produced three spurious failures in the first pass.

Screenshot: `pyre-cherry-panel.png` — Cherry Framing with its header checkbox, Source frames (16 numbered 96px tiles), `Tile px 96` + `+ Add selected`, Cherry slots with four cards, × buttons and the blue selection border on card 2. No Shaper rows anywhere. Layout is clean.

## The one real defect the walk found (fixed, `861bd324`)

Toggling **MultiFrame** inside the right-click slot editor did not reveal Shaper's candidate-frame list or its seed — they only appeared if the popover was closed and reopened on a slot that already had MultiFrame on. The host's extra rows were built once from the value captured when the popover opened, so the gesture that makes them relevant was the one gesture that could not show them. This was pre-existing behaviour carried through the extraction, not a regression, but it made the candidate list undiscoverable. The extras now live in their own container refilled in place when a value they depend on changes (refill, not rebuild — a rebuild closes the popover under the cursor). Re-verified: hidden while off → appears the moment MultiFrame goes on, with the seed and "+ Add current frame" → "+ Add current frame" writes `multiFrameSources` → hides again when MultiFrame goes off.

## Observed, NOT mine, for whoever owns the preview bar

`shaper-cherry-panel.png` shows the Shaper right pane's chrome band between the preview and the Cherry Framing section rendering **overlapped**: the Frame/Zoom row, the Play/Rate/Loop gap/Strip row, the cherry status line, the Preview backdrop box (Recall/Save/Colour/Image) and the filmstrip's own `Tile px` are all drawn on top of one another in roughly one row's height. Pyre's capture of the same chrome is clean, so this is not a PrintWindow artefact. It is above the cherry section and outside every file this task touched (`ShaperWindow.cs` preview chrome — T-0195's area, which was mid-flight during this walk). Flagging it rather than touching it.
