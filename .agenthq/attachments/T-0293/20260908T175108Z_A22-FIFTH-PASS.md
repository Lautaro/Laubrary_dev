# A22 — fifth full pass (T-0293), 2026-09-08, worktree HEAD 99807c3e + this task's edits

Every number below was measured in the running Shaper-worktree editor. `Application.dataPath` was confirmed as `D:/UNITY/Laubrary Dev - Shaper/Assets` at the start, after each recompile, and at the end. Nothing is asserted from a doc or a previous handover.

## 1. Every archived probe, re-run unchanged

Run twice: once before this task's edits and once after. Identical both times.

| probe | result | verdict |
|---|---|---|
| T-0271 `roundtrip-probe.cs` | **ROWS 33, DIFFERING 0** | reproduces, before and after |
| T-0283 `fill-state-probe.cs` | 36 of 36 rows identical to T-0284's and T-0288's, including the heightField four (246 / 0 / **310** / 0) | reproduces |
| T-0278 `t0278_saved_probe.cs` | steps 4→32 **0**, bevel 3→16 **0**, Stepped→Dome **9163** (Rect) / **2753** (Star), on all three frames of both documents | reproduces exactly |
| T-0267 `t0267_height.cs` / `t0267_sweep.cs` / `t0267_bagmember.cs` | **3911** / **1042** / **1536 of 6144** | all three reproduce |
| T-0284 `solid-position-probe.cs` | Scale Y Box **1438** / Pyramid **1233** / Can **1528** / Orb, Gem, Ring **0**; Skew X the same shape; Ring rotation **0** | reproduces exactly |
| T-0284 `mask-three-layer-probe.cs` | unmasked 2992 → masked **1304**, invert **1800**, frames 1304/1375/1814/1998/2065/2031/1945/1304, saved+reloaded **1304** | reproduces exactly |
| T-0284 `t0285-duplicate-sentinel-probe.cs` | file byte-identical, mtime unchanged, window switched to the copy, **`dirty=True, inMemoryWidth=199`** | reproduces, and this run read T-0284's ORIGINAL numbers rather than T-0288's degraded ones — T-0288's minimal repro is corroborated by the archived script itself this time |
| T-0284 `t0285-duplicate-graph-probe.cs` | frames 0/4/7 **0 changed px** against 2768 / 2830 / 2768; graph src 4129, dup 4130, **SHARED 0**; 0 null-where-source-had-a-value | reproduces exactly |
| T-0284 `views-reapply-probe.cs` | picked the other view 0/9 → 9/9; rearranged → 0/9; **re-picked the same view → still 0/9**; away and back → 7/9 | reproduces. This is UI Toolkit ChangeEvent semantics; the archived script never presses the Apply button T-0286 added (see §3) |
| T-0284 `overflow-probe.cs` | **0 of 745** | reproduces — and see §5 finding 1 for what this probe structurally cannot see |
| T-0284 `placement-pads-probe.cs` | Translate / Origin / Scale / Skew all go 4 → 19 elements on a real left `PointerDownEvent` | reproduces |
| T-0276 `a12verify.cs` (text / posterise / combine) | all three of T-0276's fixes reproduce with their own sentences intact | reproduces |
| compile | `recompile_status` completed, failed=false, errors=[]; `EditorUtility.scriptCompilationFailed` False; `get_console_logs severity=error` **0 entries** | after every edit |

`t0267_affordances.cs` and `probe-T0280Sweep.cs` were not re-run, for the reasons T-0276 and T-0284 already recorded (the first hand-builds the objects it claims to test; the second is a template needing substitution). Both are covered by other routes here.

## 2. The cold walk, twice, with temporal samples

Opened from `Laubrary/Shaper` with every instance closed first, both times.

- **Empty state.** No document bound; 5 live controls, `Save` greyed with "No Shaper is open, so there is nothing to save."; `New`, `Browse`, `▶ Animate all`, `Refresh` live; **0 buttons without a tooltip**. 206 elements on walk 1, 210 on walk 2.
- **New document**, through the window's own two-step New → name → **Create**: one layer, 16 frames, 12 fps, 96×64, one light, **504 lit pixels at frame 0** — identical on both walks and identical to T-0276's, T-0284's and T-0288's number.
- **Play, walk 1** (12 fps, editor focused), 8 samples 1.2 s apart: frames 14, 12, 11, 9, 8, 6, 4, 3 — **8 distinct hashes**, the loop wrapped, the stage's `backgroundImage` was set on every sample (never blank), minimum lit 1120.
- **Play, walk 2**, 8 samples 2.5 s apart: frames 14, 12, 10, 8, 6, 4, 15, 13 — 8 distinct hashes, wrapped through 0, never blank.
- **The aliasing check that makes those two honest.** Both intervals are near-multiples of the 16-frame loop at 12 fps, so a stalled transport and a running one can look alike. A third run at **0.37 s** intervals (12 samples) read 13 → 1 → 6 → 10 → 15 → 3 → 8 → 12 → 1 → 5 → 9 → 14, i.e. **+4.4 frames per sample, forward, wrapping three times** — exactly 12 fps. Playback genuinely advances.
- **Re-entry.** Closed every instance and reopened from the menu: the window comes back on the **empty state**, not on the document it was left on — the shipped behaviour, unchanged. A **second New in the same session** then produced `AuditA22b` with the same one layer and the same 504 lit pixels, so nothing is captured stale across two invocations.
- **Three domain reloads** were survived (two recompiles plus an explicit `RequestScriptReload`): the window rebuilt, the toggle bar restored its section visibility, the saved-view store survived (§6), and every archived probe reproduces afterwards.
- **A final cold open after all cleanup**: menu → empty state, 54 elements, 5 buttons, 0 without a tooltip, title back to "Shaper".

## 3. T-0289 … T-0292, verified live, in BOTH hosts

| card | what its handover claimed | what this pass measured |
|---|---|---|
| **T-0289** — the cherry slot popover greys the dead length dial | greying captured by eye on two separately authored slots; the sync-on-toggle path was "a two-line code trace, not click-tested" | **Click-tested in Pyre, both directions.** Popover open on slot 1: `Length ×` **live**, `Min – Max` **GREY**. A real `NavigationSubmitEvent` on the `Variable length` ZuiToggleButton → `Length ×` **GREY**, `Min – Max` **live**. Clicking it back → the original state. The gap the handover left open is now closed. |
| **T-0290** — the Min/Max row no longer wraps under MultiFrame | verified by eye in both hosts | **One `MicroMinMax` control** in the popover drawing "1 – 1" on a single line, `MultiFrame` on its own line below it, and a full overflow scan inside the popover: **0 of 23 elements** overflowing in either axis. Visible in `shots/a22-pyre-regression-194138.png`. |
| **T-0291** — MicroSlider never emits NaN | self-reviewed, not compiled; the deferred-press path not exercised | Compiles clean. **105 `ZuiMicroSlider`s in Pyre, all 105 laid out with a real content width**; every Shaper slider driven this pass (Frame scrub, Tile size, Fully masked at, Width, Raise) behaved. Separately, this pass hit the same class of pre-layout NaN from the other side: a `zui-menu__item` clicked in the same call that opened its popover has `worldBound = (NaN, NaN)` and the click lands nowhere. That is not reachable by a physical mouse (a real menu is laid out before it can be clicked) and is recorded rather than filed. |
| **T-0292** — one name for Tile size | the caption itself was left in `PyreCherryPanel.cs` for another agent | **Done, and it landed.** Read live in Pyre: `"Tile size" = 1`, `"Tile px" = 0`. `PyreCherryPanel.cs:247` now reads `Z.MicroSlider("Tile size", …)`. |

## 4. Where no pass had been

### Masks — the source picker and every state, by eye and by probe

Walked through the real affordance: the selected layer's **Mask toggle** opens the source menu directly (there is no "on but nothing picked" state), and a real `PointerDown`/`Up`/`Click` on the `Layer 2` menu item set the mask — unmasked 3932 lit → **2960**, and the Mask card appeared.

Every state driven by its own control on a two-layer fixture (layer 3 disabled), measured as changed pixels:

| control | result |
|---|---|
| Mode **Keep inside** | lit 2816 |
| Mode **Cut away** | lit 3932, **2856 changed** |
| Mode **Keep overlap** | lit 2816, 2856 changed from Cut away — and **0 changed from Keep inside**, which is correct: the two differ only where the mask is soft, exactly as the control's own tooltip says |
| **Invert** on / off | 3932 / 2816, 2856 changed each way |
| Quantity **Distance from edge** | 52 changed (a soft ramp near the edge) |
| Quantity **Brightness** | 1740 changed |
| Quantity **Height**, source NOT extruded | option **greyed**, click inert (0 px), and the card grows a "Falling back to Opacity." line; the render is identical to Opacity |
| Quantity **Height**, source extruded through its own Height toggle | option goes **LIVE**, the fallback line disappears |
| **Fully masked at** 1 → 8 on Height | 1625 changed |
| source layer deleted through its own `×` | card reads **"Missing layer"** with the full explanation tooltip, offers **Clear**; the reference is kept (`sourceLayerId` still 1) so Undo can restore it |
| **Clear** pressed | mask unset and the whole card disappears |

By eye: `shots/a22-mask-sheet.png` renders the same fixture under six states at 3× — no mask (full ellipse), Keep inside (right half), Cut away (left half), Keep overlap (identical to Keep inside), Invert + Keep inside (identical to Cut away), and Distance from edge at Fully-masked-at 8, which shows a **visible soft gradient ramping in from the stencil's edge**. Card by eye: `shots/a22-mask-clip-coverage-190921.png` (before) and `shots/a22-mask-fixed-192634.png` (after §5 finding 1).

**A note the owner may want, recorded not filed:** on an extruded source, Quantity **Height** at the default `Fully masked at` of 1 renders **identically to Opacity** (0 changed px) — a raised source is at least 1 canvas pixel tall almost everywhere it covers, so the ramp degenerates to the silhouette. Raising the dial to 8 moves 1625 px. That sentence is now on the dial's own tooltip.

### Height — every profile and every bevel, by eye

`shots/a22-height-sheet.png`, 64×64 slab, `normalKind = Profile`, Raise 8. Row 1 is the seven extrusion profiles, row 2 the six bevels on a Dome profile.

- **Six of seven profiles draw distinctly** — Flat, Linear, Dome, Round, Taper (a visible frustum plateau), Pyramid.
- **Stepped is pixel-identical to Flat.** Filed as **T-0298** with the full matrix (3 shapes × 3 Raises × 4 Step counts = 36 comparisons, 0 of 4096 px every time; and Flat, Linear and Stepped can each be identical to having no height stage at all). The Profile radio's tooltip now says so — §5 finding 2.
- **All six bevels draw distinctly**, Stepped bevel included (faint concentric micro-terraces, visible in the sheet). The bevel catalogue is sound.

### Texture fills — a real sprite and an animated sheet, by eye

`shots/a22-texture-sheet.png`, using the real project sprite sheet `Assets/Demos/ProtoGuyDemo/Sprites/LegsWalk/Legs-E-walk.png` (448×56, 8 frames, readable).

- Row 1: **no texture assigned** → the documented flat `textureTint` fallback (white, 3136 lit); **Fitted** → the whole strip mapped once into the shape (427 lit); **Tiled 3×3** → repeats (381); **Fitted at 30°** → rotated (397).
- Row 2: **animated sheet on, Columns 8 × Rows 1**, sampled at document frames 0, 2, 4, 6 → **four visibly different walk poses**, four distinct hashes (487 / 341 / 449 / 342 lit). The sheet steps by the node's own phase, over time, as designed.

### The Edge box's nested fill

Walked through the real controls. With no edge the box offers one row, `Edge — Add edge`. Pressing **Add edge** authored the strip (`ShaperBorderDef.IsAuthored` True) and **changed 520 pixels**. The box then carries `Width`, `Sits` (Centred|Inward|Outward), `Joins coverage`, a header checkbox, `Remove edge`, and a properly **nested `Edge fill` box** drawn by the same `BuildFillBody` as the node's own fill — all nine fill Kinds, Blending, Space, Fit, Fade, Height change, Colour, Posterise. By eye in `shots/a22-edge-nested-fill-192244.png`: the nested box reads as nested, its 9-option Kind radio wraps cleanly onto three lines inside the column, nothing overlaps.

### The window at its 820×520 minimum, and at four columns

- `minSize` is `(820, 520)`, confirmed live.
- **At the minimum, with a normally-placed divider:** left pane 360.0, right pane at x=364.9 width 451.1, the Play button inside the window. A full-tree scan with **every section and every box open** returns **X overflow 0**, and the only Y hits are three `unity-slider__input` +20 px entries, which are `ScrollerSlider` thumb internals — UITK chrome, present at every window size. The one visible horizontal scroller belongs to `ShaperFilmstripElement`'s own strip (16 tiles × 40 px = 675.6 px of content in a 234.2 px pane), which is what that control is for and what the Tile size slider shrinks.
- **At the minimum with the divider left where a user legitimately dragged it at a wide window, the whole right pane is off-screen and unreachable.** Filed as **T-0296** with the table.
- **Four columns:** dragging the divider to 1520 gives `ZuiColumnFlow` four columns of 375.6 / 375.1 / 375.6 / 375.1 px at x = 4.0 / 385.8 / 767.1 / 1148.9 — **no column overlaps its neighbour**, X overflow 0. By eye in `shots/a22-four-columns-192845.png`: Views+Canvas+Lights, Layers+Mask+Lighting, Shape+Position+Sweep+Shell, Fill+Edge+Swarm+SpriteFX, with the preview island, transport, filmstrip, backdrop, Cherry and Bake in the right pane. Nothing stretches; every control keeps a sane width.

### Views across a domain reload

- The store is an asset (`Assets/Shaper/ShaperViews.asset`) and **survives a real domain reload**: after `RequestScriptReload` the dropdown still read `A19View2 | A22Reload` with `A22Reload` selected.
- **Apply restores the boxes it captured**, after the reload: all boxes opened by hand (10/10) → Apply → 2/10 for `A22Reload`, 9/10 for `A19View2`, with no dropdown value change.
- **A box-by-box round trip found that two boxes are never captured at all.** Filed as **T-0295**.
- **Sections are correctly NOT part of a view** — 11/11 before and after Apply. That matches `ZuiViewBar`'s own T-0284 comment; it is not a defect.

### Pyre's own window, for every ZUI change this programme made

Driven on a **duplicate** (`Assets/Shaper/AuditA22Pyre.asset`), never a user asset, per the programme rule. Window 1600×1150, every section and box open, 1602 elements.

| programme change | measured in Pyre |
|---|---|
| T-0288 field height follows its control | **Y overflow 0** across the whole window; 24 of 24 `.zui-field`s carry an explicit stamped height |
| T-0287 wrapped radio reaches its flow container | Torch `Flame type`: radio 247.1 px in a 347.1 px box, **2 lines**, ending **22.7 px inside** the box. All five of Pyre's wrapping radios measured inside their boxes |
| T-0286 Views bar Apply | exactly **1 Apply button**, in the Views bar; visible in the capture as `Views [dropdown] [Apply] [Update]` |
| T-0289 / T-0290 cherry slot popover | see §3 — greying click-tested both ways, one MicroMinMax, 0 overflow |
| T-0291 MicroSlider NaN guard | 105 MicroSliders, all laid out with a real width |
| T-0292 Tile size | caption reads "Tile size" (1 occurrence, 0 of "Tile px") |
| header checkboxes | 2 present and rendering |
| overflow, ZUI containers only | **X overflow 0**. The 22 raw X hits over the whole tree are all native UITK internals — `unity-min-max-slider__dragger` (+7.1 px, a dragger overhanging its own track) and `FloatInput` text-field inputs (+12–15 px) — none of them ZUI or Pyre layout |

**Pyre is clean.** Nothing this programme changed regressed it.

## 5. What this pass found and fixed

| # | finding | measurement | fix |
|---|---|---|---|
| 1 | **The Mask card clipped an option label mid-word, and no archived probe could ever have seen it.** A wrapping `Z.MiniRadio` only wraps where its width is bounded; `.zui-field` is `flex-shrink: 0`, so inside a `Z.HGroup` the field keeps its full content width and the radio never wraps. | `ZuiHGroup` 336.0 px, its `.zui-field` child **367.1** px, the radio 312.0 px, in a Mask box of 350.2 px — the `Brightness` button ran to xMax 378.2 against the box's 354.2, **24.0 px outside**, drawn on screen as "Brightne". T-0284's `overflow-probe.cs` compares only `yMax`, so it reported "0 of 745" on that same window. | The Quantity field is added to the **box body**, like every other wrapping radio in this window (Fill Kind, Ramp Quantity, Swarm Shape, Height Profile, Height Bevel), and `Fully masked at` gets its own `Z.HGroup` below it. Re-measured: radio 280.9 px, **two lines**, `Brightness` fully legible on its own second line at y=398.7, `Fully masked at` on the row below at y=424.0, **0 overflowing children**. An X-axis companion to the archived Y-only probe is at `a22-hov.cs`; the ZUI-wide trap is filed as **T-0297**. |
| 2 | **The Height card's Profile tooltip promised something Stepped does not do.** It read "Flat leaves it unraised; the others differ in how the surface climbs from edge to centre" — but Stepped paints exactly what Flat paints. | 36 comparisons (Rect/Ellipse/Star × Raise 4/16/48 × Steps 2/4/16/32): **0 of 4096 changed pixels, every one**. Against a no-height-stage control at Raise 4/8/24/48: Flat 0, Stepped 0, while Dome/Round/Taper/Pyramid change 1890–2304. | The tooltip now names Stepped as the exception, says its terraces have no wall for the light to catch, quotes the measurement, and points at the same engine gap the Steps dial below already names. Declared, not hidden — the engine gap itself is **T-0298**. |
| 3 | **"Fully masked at" did not explain why Height looks like Opacity at its default.** | On an extruded source, Quantity Height vs Opacity at `fullAt = 1`: **0 changed pixels**. `fullAt` 1 → 8: **1625 changed**. | The dial's tooltip now says a raised source is at least 1 pixel tall almost everywhere it covers, so the default reads as the silhouette, and to raise the dial to the source's own Raise to see the ramp. |

## 6. Filed rather than fixed

- **T-0295** — a saved view never captures the right pane. `BuildViewsSection(flow)` passes the left pane's `ZuiColumnFlow` as the view bar's `paneRoot`, so `Preview backdrop` and `Bake` are neither stored nor restored while all 8 left-pane boxes round-trip exactly. Not fixed: it changes what the already-committed `ShaperViews.asset` does, and wants the two boxes keyed at the same time.
- **T-0296** — `Z.Split` never clamps the restored divider, so at the 820×520 minimum the right pane starts 704.9 px outside the window and the entire preview/transport/bake half is unreachable, with no scroller and no reachable drag anchor. Not fixed: shared ZUI, needs its own cross-host pass.
- **T-0297** — the ZUI-wide version of finding 1: a wrapping MiniRadio inside a `Z.HGroup` can never wrap. Shaper's one call site is fixed; the trap remains for every other tool, and `ZuiAudit` cannot see it.
- **T-0298** — the Stepped extrusion profile paints exactly what Flat paints (and Flat/Linear/Stepped can each paint what no height stage paints), with the full matrix and the Linear intermittency. Extends T-0278, which greyed the Steps dial but never checked the profile option itself. Needs an owner decision: build riser normals (new rendering capability, which RULES.md #6 forbids here), hide the option, or leave it declared.

## 7. Looked at and found CLEAN

- **The four-column layout** at a 1520-px dial pane: four equal columns, no overlap, no stretch, X overflow 0.
- **The 820×520 minimum** with a normal divider: X overflow 0 with every section and box open; the only horizontal scroller is the filmstrip's own, by design.
- **Sections are not part of a view**, before or after a domain reload — matches the documented model, not a defect.
- **Pyre**, on every ZUI change this programme made (§4).
- **The Edge box's nested fill**: the nested box reads as nested and its own 9-option Kind radio wraps to three lines inside the column without clipping.
- **Deleting a layer** through its own `×` does not prompt a dialog and leaves a mask that named it recoverable ("Missing layer" + Clear), rather than silently dropping the reference.

## 8. Files changed

`Assets/Packages/Laubrary/Editor/Shaper/ShaperWindow.Sections.cs` — three edits, all in `BuildMaskSection` and `BuildHeightSection`: the Quantity field moved out of its `Z.HGroup`, the `Fully masked at` dial given its own row and a fuller sentence, and the Profile radio's tooltip corrected. **No Pyre file touched.** No serialized field renamed, no default changed, no render code touched, no new menu item, no new control, no new concept. No commit (programme rule 3).

## 9. Cleanup

`Assets/Shaper` is back to the two pre-existing scratch documents (`New Shaper.asset`, `New Shaper 1.asset`) and nothing else: `AuditA22a`, `AuditA22b`, `AuditA22Pyre`, `AuditT0278Rect/Star`, `ShaperViews.asset` and the `Audit0271/`, `Audit0277/`, `AuditA19/` folders are all deleted with their `.meta` files. Every `A22.*` and `A19.*` EditorPref is removed, `Shaper.lastView` is cleared, the `A22.samples` SessionState is erased, both window titles are back to "Shaper" and "Pyre", and both windows were closed and the Shaper one cold-reopened from the menu to confirm the empty state.

**One asset outside the remit was written and has been reverted, the same one and in the same way as in T-0288.** `Assets/Pyre/New Pyre Plus.asset` was flushed while this task had Pyre open (only ever on a duplicate; the original was never bound to the window). The diff was a pure re-serialisation against the current `ZuiGradient` shape (`gradient` → `legacyGradient` plus the new `stops`/`space`/`stopsAuthored` fields) with `previewLayerSel: 2147483647 → 0` — nothing authored changed. The Pyre window was closed, the object's dirty flag cleared and the object unloaded, the file restored with `git checkout` and force-reimported; `git status` now shows it unmodified. **This is the second consecutive pass in which merely opening Pyre republishes that asset, and it is worth its own card if a third pass hits it.**

`git status` at the end lists only `Assets/Packages/Laubrary/Editor/Shaper/ShaperWindow.Sections.cs` plus the modifications that were already present when this task began (`.mcp.json`, `CLAUDE.md`, `Assets/Pyre/Green Lantern.asset`, the TextSplash border font, the `Samples~` tree, and the untracked `Assets/Shaper/` and `Assets/_Recovery/`).

## 10. Captures

| file | shows |
|---|---|
| `shots/a22-mask-clip-coverage-190921.png` | the Mask card before the fix — `Brightness` clipped to "Brightne" at the card's edge |
| `shots/a22-mask-fixed-192634.png` | the same card after — `Brightness` wrapped onto its own line, `Fully masked at` below it |
| `shots/a22-mask-sheet.png` | all six mask states rendered at 3×, including the soft Distance-from-edge ramp |
| `shots/a22-height-sheet.png` | the seven extrusion profiles and the six bevels; Stepped visibly identical to Flat |
| `shots/a22-texture-sheet.png` | the Texture fill with a real sprite sheet: tint fallback, Fitted, Tiled 3×3, rotated, and four animated-sheet frames |
| `shots/a22-edge-nested-fill-192244.png` | the Edge box with its nested Edge fill box |
| `shots/a22-four-columns-192845.png` | the window at four columns, nothing overlapping or stretching |
| `shots/a22-pyre-regression-194138.png` | Pyre: Views Apply, the wrapped Flame-type radio, the slot popover with a greyed Min–Max, "Tile size" |

Probe scripts are the `a22-*.cs` files beside this report; `a22-lib.txt` is the shared helper block they are all built from, and `a22-hov.cs` is the X-axis overflow scan the archive was missing.
