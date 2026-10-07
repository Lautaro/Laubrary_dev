# Discrete frame lanes

The multi-lane control used by Chunks also supports discrete frames. Seconds remain the default until the host calls `SetFrames`. Frame mode reuses the gutter, clipped bar, ruler, pointer capture and layout; the host owns animation data and undo.

Create the control with `Z.Lanes(...)`, then call `SetFrames(IReadOnlyList<ZuiFramePicture>, IReadOnlyList<ZuiFrameLane>)`. A picture supplies its texture, normalized UV and hover text. A lane contains spans with inclusive zero-based `First` and `Last` indices, label, color, tooltip and optional resizable endpoints. The ruler displays one-based frame numbers.

| Member | Host responsibility |
| --- | --- |
| `SetFrameSelection(IEnumerable<int>)` | Update highlighted frames without changing the document. |
| `SetFrameWithoutNotify(int)` | Update the preview playhead without re-entering selection. |
| `OnFrameSelected(frame, additive, range)` | Apply click, Ctrl and Shift selection semantics. |
| `OnFrameMoved(from, to)` | Reorder the frame and all its attached payloads once, on pointer release. |
| `OnFrameContext(frame)` | Open the appropriate context menu. |
| `OnFrameLaneSelected(lane)` | Select that lane's inspector. |
| `OnFrameRangeChanged(lane, span, first, last)` | Record one undo and apply the inclusive boundary change on release. |

Place the control in a horizontal scroll view with a minimum width per frame when needed. Pictures, lanes and ruler then share one scroll and identical column geometry. Preview-only lead-ins belong outside the real frame columns. Playback updates must not replace authored selection.

The Builder displays four stable rows: timing, events, the selected spatial layer and the selected phase. Its typed clipboard rejects incompatible kinds and deep-copies spatial and event content. Pasting between different frame dimensions resamples shapes and maps a point to exactly one destination cell.

`ZuiLanes.VerifyFrameContracts()` checks frame coordinate/gesture dispatch and the existing seconds behavior. Builder document and preview checks separately cover payload attachment, clipboard copies, undo, fitting and phase transitions. Editor validation also exercised thumbnail reorder, both phase edges and a Chunks seconds-band drag through Unity input events.
