# A24 — sixth full pass (T-0299), 2026-09-08, worktree HEAD fd37b449 + this task's edits

Every number below was measured in the running Shaper-worktree editor. `Application.dataPath` was confirmed as `D:/UNITY/Laubrary Dev - Shaper/Assets` at the start of every probe, after each recompile, and at the end. Nothing is asserted from a doc or a previous handover.

**This pass found 3 things beyond trivia**, all now either fixed or filed with measurements: opening a Pyre window rewrote the asset on disk (root-caused and fixed), Pyre's own splitter parked its right-hand pane off-screen (fixed), and the Tags section promised a browser filter that does not exist (tooltip corrected, missing affordance filed).

## 1. Opening a window must not write an asset — root-caused and fixed

This was the card's first item and it reproduces exactly, on a clean tree, with no user action at all.

**The chain, measured end to end.**

| step | measurement |
|---|---|
| Baseline, no windows open | all four candidate assets load, `dirty=False`; `Assets/Pyre/New Pyre Plus.asset` line 18 reads **`previewLayerSel: 2147483647`** on disk |
| Opening the Pyre window from `Laubrary/Pyre` with **no asset bound** | 0 assets dirty — the window chrome alone is innocent |
| **Binding an asset** (what opening with a remembered asset does) | on a byte-identical duplicate: **`dirty=True`, `previewLayerSel` 2147483647 → 0** |
| Isolating which write | on a duplicate whose index was **already valid (0)**, so `BuildAsset`'s clamp is a no-op: bind → **`dirty=True`**, value still 0. Clearing dirty and rebuilding → **`dirty=False`**. The dirtier is therefore exclusively the load-time write, not the clamp |
| What then reaches disk | the dirty duplicate was flushed by the **next domain reload** (an ordinary recompile): 216435 bytes → **211993 bytes**, `previewLayerSel: 2147483647` → `0`, the whole `ZuiGradient` block re-serialised. Nobody pressed Save |

**Root cause.** `PyreWindow.OnAssetChanged` (PyreWindow.cs:151) ran `layerSel = int.MaxValue;` on every asset load, commented "a NEW asset defaults to its last layer (BuildAsset clamps)". `layerSel`'s setter writes `spec.previewLayerSel` — a **serialized field on the asset** — and calls `EditorUtility.SetDirty(spec)`. `BuildAsset` (line 375) then clamped it back with a second `SetDirty`. **Net authored change on a healthy asset: zero.** The asset was dirtied only to arrive at the value it started with, and the next domain reload wrote the file. When a flush landed *between* the two writes, the raw sentinel persisted — which is exactly how the shipped `New Pyre Plus.asset` came to carry `2147483647`.

Two smaller writes-on-read in the same block: the `SelLayer` **getter** silently wrote a repaired index back into the asset, and `BuildAsset`'s clamp dirtied on every rebuild.

**The fix** (`Editor/Pyre/PyreWindow.cs`), stated as a rule: *the dirty flag belongs to a user's choice of layer, never to housekeeping.*
- `OnAssetChanged` no longer stomps the stored selection — it calls a new `ClampLayerSel()` that repairs an out-of-range index **without** `SetDirty`. This also restores the behaviour the field's own doc comment promises ("reopening the window … lands on the layer you were working on"), which the stomp had been destroying on every load. A brand-new asset has one layer, so its stored 0 is already its last — the original intent is preserved.
- `SelLayer`'s getter is now read-only; it clamps what it returns and writes nothing.
- `BuildAsset`'s rebuild-time clamp goes through the same silent path.
- The one remaining `layerSel = Mathf.Clamp(...)` (line 931) is inside the layer-delete button — a real user action, correctly still dirtying.

**Verified after the fix**, cold, through the window's own menu item:

| fixture | before bind | after bind | after rebuild |
|---|---|---|---|
| duplicate, index 0 | sel 0, clean | sel 0, **clean** | sel 0, **clean** |
| duplicate, index 0 | sel 0, clean | sel 0, **clean** | sel 0, **clean** |
| **the real `New Pyre Plus.asset`** | sel **2147483647**, clean | sel 0 (repaired in memory), **clean** | sel 0, **clean** |

`SelLayer` still returns a valid layer throughout. At the end of the pass `git status` shows `New Pyre Plus.asset` **unmodified**, and a byte-hash comparison against the session-start baseline shows every Pyre and Shaper asset **identical**.

**Shaper does not have this defect.** `ShaperWindow.OnAssetChanged` (ShaperWindow.cs:124-130) writes only `selectedLayer`/`currentFrame`/`playing`, which are `[SerializeField]`s on the **window**, not on the document. Verified live: `OpenFor` on a duplicate and on both real scratch documents left all three `dirty=False`, mtime and length unchanged, 0 dirty assets project-wide.

The corrupt value still sitting in the committed file is filed as **T-0300** (serialized data is the owner's, not a task agent's).

## 2. Every archived probe, re-run unchanged

Run twice, before and after this task's edits. Identical both times.

| probe | result | verdict |
|---|---|---|
| T-0271 `roundtrip-probe.cs` | **ROWS 33, DIFFERING 0** | reproduces |
| T-0283 `fill-state-probe.cs` | 36 of 36 rows identical to T-0284/T-0288/T-0293, **including the heightField four (246 / 0 / 310 / 0)** | reproduces |
| T-0278 `t0278_saved_probe.cs` | steps 4→32 **0**, bevel 3→16 **0**, Stepped→Dome **9163** (Rect) / **2753** (Star), all three frames of both documents | reproduces exactly |
| T-0267 `t0267_height` / `_sweep` / `_bagmember` | **3911** / **1042** / **1536 of 6144** | all three reproduce |
| T-0284 `solid-position-probe.cs` | Scale Y Box **1438** / Pyramid **1233** / Can **1528** / Orb, Gem, Ring **0**; Ring rotation **0** | reproduces exactly |
| T-0284 `mask-three-layer-probe.cs` | 2992 → **1304**, invert **1800**, frames 1304/1375/1814/1998/2065/2031/1945/1304, saved+reloaded **1304** | reproduces exactly |
| T-0284 `t0285-duplicate-graph-probe.cs` | frames 0/4/7 **0 changed**, 2768/2830/2768; graph src 4129, dup 4130, **SHARED 0**; 0 nulls | reproduces exactly |
| T-0284 `t0285-duplicate-sentinel-probe.cs` | `fileUnchanged=True, mtimeUnchanged=True` in **every** run; `dirty=True, inMemoryWidth=199` on a warm run | reproduces — see the archive note below |
| T-0284 `views-reapply-probe.cs` | picked → 9/9, rearranged → 0/9, re-picked same → 0/9, away and back → **9/9** | reproduces; away-and-back is 9/9 where T-0293 read 7/9, which is T-0295's fix restoring more boxes |
| T-0284 `overflow-probe.cs` | **0 of 388** (0 of 839 on the wider run) | reproduces |
| T-0284 `placement-pads-probe.cs` | Translate / Origin / Scale / Skew all 4 → 19 on a real `PointerDownEvent` | reproduces exactly |
| T-0276 `a12verify.cs` (text / posterise / combine) | all three fixes reproduce with their sentences intact | reproduces |
| compile | `recompile_status` completed, failed=false, errors=[]; `scriptCompilationFailed` False | after every edit |

**Three archive-hygiene notes that cost this pass real time and will cost the next one the same.**

1. **`T-0277/fill-state-probe.cs` is broken and must not be used.** Line 61 loads `dir + "/rfield0277.asset"` — a path the probe never creates (it writes `hfield0277.png`). `f.heightField` is therefore null and the three `heightFieldScale` rows silently read **0** instead of 246/0/310. The **`T-0283/` copy is the corrected one** and is what T-0293's table actually names. Running the T-0277 copy looks exactly like a rendering regression and is not one.
2. **The `t0285` sentinel probe's `dirty`/`inMemoryWidth` columns are only meaningful on a WARM run.** On the first run after `Assets/Shaper/AuditA19/` has been recreated, the folder-creation import discards the sentinel's unsaved in-memory edit and the probe reads `dirty=False, inMemoryWidth=32`; runs 2 and 3 read the archived `dirty=True, inMemoryWidth=199`. Reproduced both ways twice this pass. This is the T-0284-vs-T-0288-vs-T-0293 disagreement that has now confused three passes. **The load-bearing assertion — `fileUnchanged=True`, `mtimeUnchanged=True`, i.e. Duplicate does not write the project — held in every single run, degraded ones included.** T-0285's property is intact.
3. **`t0285-duplicate-graph-probe.cs` must run immediately after the sentinel probe**, which writes the `A19.dupPath` EditorPref it reads. Out of order it throws `Index was outside the bounds of the array`.

## 3. The cold walk, twice, with temporal samples

Opened from `Laubrary/Shaper` with every instance closed first, both times.

- **Empty state.** No document bound; **5 buttons**, `Save` greyed with "No Shaper is open, so there is nothing to save."; `New`, `Browse`, `▶ Animate all`, `Refresh` live; **0 buttons without a tooltip**. 197 elements on walk 1, 201 on walk 2, 33 on the final cold open.
- **New document** through the window's own two-step New → name → **Create**: one layer, 16 frames, 12 fps, 96×64, one light, **504 lit pixels at frame 0** — identical to T-0276's, T-0284's, T-0288's and T-0293's number. `dirty=False` immediately after Create (the create path flushes), which matters for §1.
- **Play, temporal samples at 0.37 s** (the aliasing-honest interval T-0293 established), 12 samples driven off `EditorApplication.update`: frames **4 → 8 → 13 → 1 → 6 → 10 → 15 → 3 → 7 → 12 → 0 → 5**, i.e. **+4.4 frames per sample, forward, wrapping three times** — exactly 12 fps over a 16-frame loop. Playback genuinely advances and loops.
- **The transport's own label flips**: reads `▶ Play` at rest, `❚❚ Pause` while playing, back to `▶ Play` when pressed again.
- **The preview never blanks.** All 16 frames rendered: **0 blank frames, 16 distinct hashes**, lit per frame 504 / 704 / 864 / 1120 / 1320 / 1536 / 1872 / 2128 / 2124 / 1944 / 1944 / 1872 / 1768 / 1700 / 1700 / 1536. Playing left the document `dirty=False`.
- **Re-entry.** Closed every instance and reopened from the menu: back to the **empty state**, not the document it was left on — the shipped behaviour, unchanged. 5 buttons, 0 without a tooltip.
- **A second New in the same session** produced `AuditA24b` with the same one layer and the same **504** lit pixels — nothing captured stale across two invocations.
- **Two domain reloads** (both recompiles) were survived; every archived probe reproduces afterwards.
- **A final cold open after all cleanup**: menu → empty state, title back to "Shaper", 5 buttons, 0 without a tooltip, **0 dirty assets project-wide**, compile clean.

## 4. T-0295, T-0296, T-0297 — verified live and independently

**T-0296 — Z.Split now clamps, and regrows.** The exact T-0293 repro: divider pref forced to 1400, cold-opened at the documented minimum.

| window | left pane | right pane | Play button | persisted pref |
|---|---|---|---|---|
| **820×520** (`minSize` confirmed 820×520 live) | clamped to **492.0** | x **496.9 … 816.0** (319.1 px), fully inside | worldBound (500, 501, 53×20), **fully inside** | still **1400**, uncorrupted |
| widened to **2100×1150** | regrew to **1400** | x 1404.9 … 2096.0 | fully inside | still **1400** |

The right pane is reachable at the minimum and the user's real intent survives the round trip. Matches T-0296's own numbers exactly.

**T-0295 — a saved view now captures the right pane.** Rather than trust the code, the live capture closure was invoked. `ZuiViewBar._capture` and `._apply` both hold a `paneRoot` with **7 ZuiBoxes** under it, and calling the real capture returns **8 entries** including `Preview backdrop…/fold` and `Bake…/fold` alongside the six left-pane keys. Before the fix `paneRoot` was the left `flow` and those two were structurally invisible to it. Confirmed against the tree: the left pane holds 5 boxes, the right pane holds exactly `Preview backdrop` and `Bake`.

**T-0297 — a wrapping MiniRadio inside a Z.HGroup wraps.** Built the exact trap shape from scratch (4-option `Z.MiniRadio(wrap:true)` → `Z.Field` → `Z.HGroup` → 350 px box) and measured it live:

- field carries `zui-field--wrap`, radio carries `zui-radio--wrap`
- radio **274.7 × 40.0 px** — 40 px is two lines, so it genuinely wrapped
- radio xMax **336.9** vs box xMax **350.2** → **13.3 px inside**, where T-0293 measured this same shape **24.0 px outside**
- all four options inside the box; `Height` sits on the second line at y=72.9

## 5. Where no pass had been

### The Bag card, end to end

Driven through its own controls on a real bag.

- **Add members.** `+ Add member` pressed three times → 1, 2, 3 children, each row offering `Open`, `×` and a name field.
- **Three combine modes, by eye**, on two members with the second offset +10 x: **Add 1856 lit**, **Cut out 320**, **Keep overlap 1216**, each 1536 changed pixels from the last. The arithmetic is exactly consistent — 1856 = 1536 + 1536 − 1216, and 320 = 1536 − 1216 — so the three modes are genuinely doing union, difference and intersection.
- **Drill in and out via the breadcrumb.** `Open` on the second row set `drillPath = [1]`, the breadcrumb rendered `[layer] › [Member 2]` with the documented tooltips ("Back to this layer's root node." / "Back to this member."), and the member's own combine radio `[Add][Cut out][Keep overlap]` appeared. Pressing the layer crumb clears the path.
- **Undo and Redo after each edit type** — five edit types, ten assertions, **all correct**:

| edit | after | undo restores | redo reapplies |
|---|---|---|---|
| `+ Add member` | 3 members | ✔ 2 | ✔ 3 |
| change a member's combine mode | Member 2/Subtract | ✔ Add | ✔ Subtract |
| reorder members | Member 2, Member 1 | ✔ original order | ✔ swapped |
| rename a member | Renamed | ✔ Member 1 | ✔ Renamed |
| remove a member (row `×`) | 1 member | ✔ 2 | ✔ 1 |

**Recorded, not filed:** `+ Add member` places each new member exactly on top of the previous one, so lit pixels stayed **1536 for one, two and three members** — pressing it twice changes the picture not at all, and the only feedback is the new row. That is arguably correct (identical shapes union to the same area), but it is a "nothing happened" moment on the tool's own primary Bag affordance.

### Keyboard: Ctrl+Z / Ctrl+Y in the window

`Edit/Undo` and `Edit/Redo` — the commands those keystrokes fire — both execute with the Shaper window focused and round-trip correctly (`EditOne` → undo → `Layer 1` → redo → `EditOne`, document alive and loadable throughout). A raw ctrl+Z `KeyDownEvent` sent at the window's root is **not** stopped by the window, so the editor's own Undo still receives it.

**A probe trap worth recording, because it looks exactly like catastrophic data loss and is not.** Doing Create and an edit in back-to-back `eval_file` calls leaves both in the **same undo group** (no real input event happens in between to increment it), so a single undo unwinds the Create too: the window's document goes `NULL` and the `.asset` file, still on disk, becomes unloadable. Measured with group numbers: Create → group 88 "Create Shaper"; with an explicit `Undo.IncrementCurrentGroup()` the edit lands in group 89 "Edit Shaper Document" and one undo correctly restores just the edit. This confirms the archived `editor-walk-probe-gotchas` note with numbers. **Shaper's Undo is sound; the harness needs a group increment between staged operations.**

### The Swarm section, every dial, by eye

Reflection-driven over `ShaperSwarmDef`'s public fields rather than a hand list, measured as changed pixels, and re-measured in each dial's own enabling state and across all 16 frames.

**Every swarm dial is live.** In the default state most read 0 — that is conditionality, not deadness, and each one comes alive in its proper state:

- `count` 375, `seed` 1904, `positionJitterX/Y` 1834 / 1331, `scaleJitterDial` 1288, `shape` 1626, `timing` 680.
- With `shape = Circle`: `spawnerRadius` 1235, `spawnerOffsetX/Y` 1385 / 1111, `spawnerRotationDegrees` 777, `spawnerPitchDegrees` 218, `spawnerYawDegrees` 247, `distribution` 1472, `spawnOrderChaos` 557, `scaleByIndex` 5455.
- With `spawnMode = Path`: `pathProgress` 1, `evenSpacing` 1304, `pathSpread` 1296.
- With `timing = Window`: `spawnTiming` 3864, `instanceLife` 961, `dieTogether` 961. With `timing = FrameStep`: `firstSpawnPhase` 4991, `spawnPhaseStep` 4869, `instanceLife` 4869, `dieTogether` 2742.
- `orient` (1532) and `rotationJitterDegreesDial` (1311) read 0 on an **Ellipse** — a circle is rotationally symmetric, so a per-instance rotation cannot show — and come alive immediately on a **Star**.
- `lifetimeStagger` read 0 in every timing mode until the node's own content was made genuinely phase-driven: with an **Oscillation** dial on the star's length it moves **4766** px at 1→0 and **3260** at 1→0.5. It reads 0 under `MinMax` and `Steps`, which are not phase-driven in the way the instance clock feeds — consistent with `ShaperCompiler.cs:523-528` and `:655`, and with the field's own doc comment.

**Not verified:** whether the Swarm section's UI *greys* the dials that are inert in the current state. The engine side is clean; the declaration side was not measured for this section.

### Tags

- **Add and remove round-trip cleanly.** Added `A24AuditTag` to a document → 1 tag on the asset; removed → 0; deleted the tag → library back to its original **6** entries, no residue.
- **The filter the section promised does not exist here** — see §6.

### The Browse grid

- **42 documents found, 42 thumbnails rendered, 43 Image elements all carrying a real texture.** No blank cell anywhere in the grid.
- **Opening from the grid works**, via the cell's `PointerDownEvent` (not `ClickEvent` — worth knowing for the next pass): a single click on a cell bound `AuditA24Doc` to the window; a double-click bound `New Shaper` **and** closed the browser (`browsing` → False).
- That measurement also produced a trivia fix — see §6.

## 6. Fixed here

| # | finding | measurement | fix |
|---|---|---|---|
| 1 | **Opening a Pyre window rewrote the asset on disk.** | §1 in full | `OnAssetChanged` no longer stomps the persisted selection; `SelLayer`'s getter no longer writes; the rebuild clamp no longer dirties. Verified: bind the real corrupt asset → repaired in memory, **stays clean**. |
| 2 | **Pyre's own splitter never clamped to the window**, so a divider legitimately dragged wide parked the whole preview/transport/bake pane off-screen. | At **900×700** with `leftPaneWidth` 1458 (a width the drag handler itself permits at a large window): left pane stayed **1458 px** and **11 of 27 buttons — the entire transport (`❚❚ Pause`, `Frame`, `Strip`), `GIF…`, `GIF dither`, `Bake`, `+ Add modifier`, `+ Add simulation` — sat entirely right of the window edge**, with no scroller and no reachable drag anchor. Root cause: the **drag** clamped against `position.width - 260` (PyreWindow.cs:447) but the **build** clamped only against the four-column cap `[360, 1458]` (line 343). | A single `ClampedLeftPaneWidth()` now owns the cap, used by build, drag and a new `GeometryChangedEvent` re-clamp so a resize corrects the pane without needing a rebuild. The persisted intent is never overwritten. Verified: **900×700 → pane 640, 0 of 65 buttons off-screen; widened to 1900 with no rebuild → regrew to 1458, 0 of 27 off-screen; `leftPaneWidth` still 1458 throughout.** Same class as T-0296's Z.Split defect, as T-0296 flagged. |
| 3 | **The browse cell's tooltip misdescribed both of its clicks.** It read "click to select, double-click to open", but `BuildCell`'s `PointerDownEvent` handler calls `SetAsset(item)` on a **single** click — there is no select-without-open state — and the only thing the second click adds is `browsing = false`. | Measured live: single click → window bound to `AuditA24Doc`; double-click → bound `New Shaper` **and** `browsing` False. | Now reads "click to open it here, double-click to open it and close the browser." Shared AssetKit, so Pyre and every other `ZuiAssetWindow` gains it. |
| 4 | **The Tags section promised a browser filter this window does not have** ("Tags for this asset — filterable in the browser."). | `LauTagFilter`'s only callers are `LauAssetBrowser.cs:332`/`:338` — the *other*, IMGUI browser. `ZuiAssetWindow.BuildBrowser()` offers a header, an Animate-all toggle, Refresh and the grid; `RefreshBrowse()` is a bare `Enumerate()`. A live scan of every control in the window for "tag"/"filter" found nothing outside the Tags section itself. | The tooltip now says the tags are shared with the rest of Laubrary and that this window's browser lists every asset and does not filter by them. The missing affordance is filed, not built (rule 6). |

## 7. Filed rather than fixed

- **T-0300** — `New Pyre Plus.asset` still carries the corrupt `previewLayerSel: 2147483647` written by the bug above. Harmless now (repaired in memory, never written back), but it is committed serialized data and rule 7 makes it the owner's to change.
- **T-0301** — `ZuiAssetWindow`'s browser has no tag filter at all, in any tool. The filter logic already exists (`LauTagFilter`) and only lacks a control; adding one is a new control, which rule 6 forbids here.
- **T-0302** — `PyreWindow` sets no `minSize` (measured **50×50**, Unity's default floor; a real 124.4×50 window put 22 of 27 buttons off-screen). `ShaperWindow` declares 820×520. Picking Pyre's number is an authoring decision.

## 8. Looked at and found CLEAN

- **Shaper never writes its document on open** — `OpenFor` on a duplicate and on both real scratch documents left every one `dirty=False`, file mtime and length unchanged, 0 dirty assets project-wide.
- **Undo/Redo across all five Bag edit types**, 10 of 10 assertions correct, plus the keyboard path.
- **The Browse grid**: 42/42 thumbnails, 43/43 Images with a texture, open-from-grid works on both click counts.
- **Tags** add/remove/delete round-trip with no residue.
- **Every swarm dial is live** in its own state; none is dead.
- **Playback over time**: 12 fps confirmed against an aliasing-proof interval, loop wraps, 16 distinct frames, never blank.
- **Overflow**: 0 of 388 elements overflowing their own box.

## 9. Cleanup

`Assets/Shaper/` is back to exactly the two pre-existing scratch documents (`New Shaper.asset`, `New Shaper 1.asset`). Every asset and folder this pass created is deleted with its `.meta`: `AuditA24a/b/g/u/v`, `AuditA24Doc`, `AuditA24Pyre`, `AuditA24Pyre2`, `ShaperViews.asset`, and the `Audit0271/`, `Audit0277/`, `AuditA19/` folders. Both windows were unbound and closed, `Undo.ClearAll()` run, and Pyre's `leftPaneWidth` put back to its declared 360 default.

EditorPrefs written by this pass are removed (`A24.savedSel`, `ZUI.Split.shaper.window.split.v1`, `Shaper.lastView`, `A19.dupPath`, `A19.srcPath`), the `A24.*` SessionState keys erased, and the section toggle bar's `userSel` **restored byte-for-byte** to what it was at session start (`Views=1;Canvas=0;Layers=0;Shape=0;Fill=0;Swarm=0;SpriteFX=0;Lights=1;Tags=0`).

**No asset was written.** A SHA-256 comparison against the session-start baseline shows `New Pyre Plus.asset`, `New Pyre Plus 1.asset`, `Green Lantern.asset`, `New Shaper.asset` and `New Shaper 1.asset` all **byte-identical**. `git status` lists only the two files this task changed, plus the modifications that were already present when it began (`.mcp.json`, `CLAUDE.md`, `Green Lantern.asset`, the TextSplash border font, the `Samples~` tree, and untracked `Assets/Shaper/` and `Assets/_Recovery/`).

A final cold open from the menu: empty state, title "Shaper", 5 buttons, 0 without a tooltip, **0 dirty assets**, `scriptCompilationFailed` False.

## 10. Files changed

- `Assets/Packages/Laubrary/Editor/Pyre/PyreWindow.cs` — the write-on-open fix (`OnAssetChanged`, `layerSel`/`SelLayer`, `ClampLayerSel`) and the splitter clamp (`ClampedLeftPaneWidth`, the build site, the drag handler, the resize re-clamp). **This is the only Pyre file touched.** No serialized field renamed, no default changed, no render code touched.
- `Assets/Packages/Laubrary/Editor/AssetKit/ZuiAssetWindow.cs` — two tooltip corrections (browse cell, Tags section).

No new menu item, no new control, no new concept, no commit (rule 3), `CHANGELOG.md` untouched (the line is at the top of the handover).
