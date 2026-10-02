# A25 — seventh full pass (T-0303), 2026-09-08, worktree HEAD 3b1aa8d6 + this task's edit

Every number below was measured in the running Shaper-worktree editor. `Application.dataPath` was confirmed as `D:/UNITY/Laubrary Dev - Shaper/Assets` at the start of every probe, after the recompile, and at the end. Nothing is asserted from a doc or a previous handover.

**This pass found 2 things beyond trivia**, both filed with measurements: Pyre's dial pane puts the layout engine into a permanent recursion storm at a narrow band of pane widths — a band the new 820 px minimum window makes easy to reach — and the Swarm section's "Appearance order" dial is hidden in the one timing state where it does the most work while being drawn live in states where it does nothing. One piece of trivia was fixed: Pyre's window still called itself "Pyre Plus" in fifteen user-visible strings.

## 1. Pyre's dial pane never finishes laying out at pane widths 540–620 px

Found by accident — the console was full of it, which is how it hid: it drowns everything else.

**The message**, repeated tens of times per second, forever: `Layout update is struggling to process current layout (consider simplifying to avoid recursive layout): EditorPanelRootElement unity-panel-container (x:0, y:0, width:820.00, height:546.22)`.

**Measured, with only the Pyre window open** (the Shaper window closed, so attribution is unambiguous), counting `LogEntries.GetCountsByType` errors after a `LogEntries.Clear()` and then leaving the editor completely idle:

| state | errors in 5 s idle |
|---|---|
| Pyre at 820×700, dial pane 560 | **464** |
| Pyre at 900×700, same asset | **0** |
| Shaper alone at 820×520 (its own declared minimum) | **0** |
| Shaper alone at 900×700 / 1200×900 | **0** |

**It is the PANE width, not the window width.** Holding the window at 1600×900 and rebuilding at a series of pane widths:

| dial pane width | 520 | 530 | **540** | **550** | **560** | **570** | **580** | **600** | **620** | 640 | 660 | 760 | 900 |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| errors in 4 s idle | 0 | 0 | **390** | **380** | **362** | **356** | **380** | **340** | **410** | 0 | 0 | 0 | 0 |

**It needs a rebuild, and then it never stops.** Toggling sections open on an already-built tree at pane 560 is silent (all 28 sections open, scrollbar present: 0 errors). A `Rebuild()` at pane 560 produces 404 errors per 4 s, and the storm was still running **20 seconds later** (410 per 5 s) with nobody touching anything. Resizing the window does not clear it (516 per 5 s after widening the window with the pane still at 560); dragging the divider out of the band does (pane 640 + rebuild → 0).

**It is not T-0299's re-clamp.** With `leftPaneWidth` at its 360 default the clamp never fires and 820×700 is silent; with the intent above the cap but no relayout the callback also never fires. The storm follows the pane's *resolved* width, and the clamp is what lands it in the band: at the new 820 px minimum window, `ClampedLeftPaneWidth()` returns `820 − 260 = 560`, the middle of the broken band, for every author who has ever dragged the divider wide.

**No single card reproduces it.** With all 28 sections folded: 0. With exactly one open (each of the 28 tried in turn, three seconds apiece): 0 every time. It needs the aggregate content, which is why a bisection over sections cannot name a culprit.

Filed as a card on `ShaperHarmony-2026-09-07`, not fixed: the mechanism is inside the pane's layout as a whole, not in one control, and rule 6 forbids inventing a new one here.

## 2. The Swarm section's conditional drawing, by eye and by pixel

The Swarm section builds nothing until its header checkbox is ticked (`BuildSwarmSection` returns after the header when `!s.enabled` — ShaperWindow.Sections.cs:1438), which is why a first look at it appears empty. With the swarm on, every state was read out of the live tree — labels drawn, controls greyed, tooltips missing:

| state | dials drawn | greyed | no tooltip |
|---|---|---|---|
| shape None, timing Stagger (the default) | Count, Swarm seed, Rotation jitter, Scale jitter, Position jitter, Shape, Timing, Lifetime stagger, Merge width/sharpness | 2 | 0 |
| shape Circle, Area | + Spawn, Radius, Turn, Tilt, Yaw, Spawn centre, Distribution, Face, Size by index | 2 | 0 |
| shape Circle, Path | Progress replaces Distribution | 2 | 0 |
| … with Even spacing on | + Spread | 2 | 0 |
| shape Line | Half length instead of Radius; no Spawn, no Distribution | 2 | 0 |
| timing Window | + Births, Lifetime, Die together, Appearance order | 2 | 0 |
| timing FrameStep | + First frame, Frame step, Lifetime, Die together, Appearance order | 2 | 0 |

The two greyed controls are the same two in every state, each with its reason in its own tooltip: **Merge carve** ("Does nothing on a swarm: instances are unioned, and carve strength is read only where a node subtracts") and **Instances mix** ("Does nothing on this shape: instances are evaluated independently and unioned"). Nothing in the section is drawn without a tooltip in any state.

**Hidden dials were then measured for liveness in the state that hides them** (changed pixels, summed over frames 0/4/8/12, swarm count 8, radius 24):

| dial | state that hides it | pixels it moves there |
|---|---|---|
| spawnerRadius, distribution, spawnerOffsetX, orient, scaleByIndex | shape = None | **0** each |
| pathSpread, pathProgress | Area spawn | **0** each |
| instanceLife, dieTogether, firstSpawnPhase | timing = Stagger | **0** each |
| **spawnOrderChaos ("Appearance order")** | **timing = Stagger** | **4224** |

So the hiding rule is right everywhere except one dial, and there it is inverted on both sides:

| | shape = None | shape = Circle |
|---|---|---|
| timing Stagger | 0 px — **not drawn** ✔ | **4224 px — NOT DRAWN** ✘ |
| timing Window | 0 px — **drawn, live** ✘ | 6774 px — drawn ✔ |
| timing FrameStep | 0 px — **drawn, live** ✘ | 6354 px — drawn ✔ |

The permutation it controls is built whenever a spawn shape exists (`ShaperCompiler.cs:465`, `placed || spawnOrderChaos > 0`), so with a shape authored it re-assigns which instance sits where regardless of timing. The field's own doc comment ("Observable only when timing gives instances distinct birth moments", ShaperSwarmDef.cs:188-190) understates it, and the card's branch placement follows that comment. Filed, not moved: which box a control belongs in is an authoring decision.

## 3. Every card by eye at 1, 2 and 4 columns

Driven through the window's own splitter pref and `ZuiColumnFlow`'s real column count (read through `hierarchy`, not `flow[i]` — the flow's `contentContainer` is its RIGHTMOST column, so plain indexing reports that column's children and lied about the column count until this was corrected).

| window | dial pane | columns | column widths | overflow | controls off the right edge | truncated labels |
|---|---|---|---|---|---|---|
| 900×1300 | 387 | **1** | 387 | **0 of 529** | 0 of 127 | 2 |
| 1250×1300 | 760 | **2** | 377 / 377 | **0 of 314** | 0 of 94 | 2 |
| 1900×1300 | 1480 | **4** | 365 × 4 | **0 of 224** | 0 of 81 | 2 |

Captures: `shots/shaper-cols1-222601.png`, `shaper-cols2-222405.png`, `shaper-cols4-222449.png` (every section switched on, so every card is on screen at once).

**By eye, all three:** cards stay whole — no card is split across a column boundary; label columns align inside each card; the greying reads correctly (Fill's `Fit` pair greyed on a Solid, the Lighting box's rim dials greyed on Flat normals); the Tags strip spans the window above the columns at every width; the right-hand pane keeps Preview, its backdrop, Cherry Framing and Bake at every width. At 1 column the left pane gains a vertical scrollbar and nothing is clipped by it.

**The same two label truncations at every width**, both width-independent and both cosmetic: the header's `ObjectField` shows `AuditA25a (Shaper Documen` (needs 173 px, has 162) and the dirty dot `●` is given 9 px for an 11 px glyph. Neither is a layout failure at any column count, and neither changes with the pane width.

A stricter overflow rule than the archived probe's (element rect leaving its nearest box horizontally) reported 5 hits at first: all five were filmstrip thumbnails inside the preview's own horizontal `ScrollView`, i.e. a scrollable strip doing its job. Excluding scroll viewports, every column count is 0.

## 4. T-0299's two fixes and T-0302, verified live

**Opening a window must not write an asset.** Pyre opened from `Laubrary/Pyre` on a clean tree; `Assets/Pyre/New Pyre Plus.asset` still carries `previewLayerSel: 2147483647` on disk (line 18, T-0300's subject) and the window repaired it **to 0 in memory while leaving the asset clean**: bind → `dirty=False`, `SelLayer` returns a real `PyreLayer`, rebuild → `dirty=False`. A forced domain reload (`RequestScriptReload`) afterwards left the file at **216435 bytes, SHA 32AEDD46…, mtime unchanged** — byte-identical to the session-start baseline. `git status` on `Assets/Pyre` stayed clean throughout.

**The splitter clamps and regrows.** With the persisted intent at 1458:

| window | pane | buttons entirely right of the window edge |
|---|---|---|
| 820×520 (the declared minimum) | clamped to **560** | **0 of 76** |
| 900×700 | clamped to **640** | **0 of 76** |
| 1900×1100 (no rebuild) | regrew to **1458** | **0 of 27** |

The persisted intent stayed 1458 throughout. (Reading the pane's resolved width in the *same* eval call that resized the window returns the pre-layout value — the "1 of 76 off-screen" and "did not regrow" readings that appear if you do that are staleness, not a defect. Measure in a second call.)

**T-0302's minimum is real.** `PyreWindow.minSize` reads **820×520**, and asking for `position = 200×120` produced a **820.0×520.2** window. The floor is enforced by Unity, not merely declared.

## 5. Pyre's own cold walk, on a scratch spec

Opened from `Laubrary/Pyre` with every instance closed first.

- **Empty state**: 5 buttons (`Save` greyed, `New`, `Browse`, `▶ Animate all`, `Refresh`), **0 without a tooltip**, `minSize` 820×520 — the same shape as Shaper's empty state.
- **New → Create** through the window's own two-step: `Assets/Pyre/AuditA25Pyre.asset`, one layer, `dirty=False` immediately after Create. The name field offered **"New Pyre Plus"** — see §6.
- **Picking a shape**: the caret on the "Shape" title opens the form menu; it listed **26 forms** in four columns (3D / 2D / Special / plug-in forms) with the current one (`Disc`) checked. Synthetic pointer events do not reach a `Clickable` inside that popup panel, so the pick itself was made through the window's own `Dirty(…)` edit path with the same three statements the menu item runs; the menu's own affordance is verified as far as opening and listing.
- **What a new spec draws**: 16 frames, **2 blank (the first and the last)**, 15 distinct frames, lit 0 / 50 / 110 / 184 / 284 / 406 / 544 / 518 / 462 / 442 / 418 / 392 / 372 / 326 / 302 / 0 — a real birth-and-death curve. Worth knowing that a brand-new Pyre spec shows **nothing at frame 0**, where a brand-new Shaper document shows 504 lit pixels; Pyre auto-plays on open, so in practice the author sees motion immediately.
- **Play, sampled every 0.37 s off `EditorApplication.update`**: frames **9 → 14 → 2 → 6 → 11 → 15 → 4 → 8 → 13 → 1 → 6 → 10**, i.e. +4.4 frames per sample, forward, wrapping — 12 fps over a 16-frame loop, the same behaviour Shaper matches.
- **Bake**, pressed as a user presses it, on the spec moved into `Assets/Pyre/AuditA25/`: produced `AuditA25Pyre.png` (10 076 bytes) and `AuditA25Pyre.anim` (4 785 bytes) beside the spec. The whole folder was deleted afterwards with its `.meta`s.

## 6. Fixed here — Pyre's window still called itself "Pyre Plus"

`PyreWindow.TypeLabel` was `"Pyre Plus"` and `NewAssetName` `"New Pyre Plus"`. `TypeLabel` is threaded through **fifteen** user-visible strings in `ZuiAssetWindow` (`:197-361`): the Save tooltip and its three states, the dirty dot's tooltip, the object field, New / Browse / Duplicate / Delete wording, the browser header and its empty-state line, and the `Undo` entries `Create …` / `Duplicate …`. Thirteen further literals in `PyreWindow.cs`, `PyreWindow.Forms.cs` and `PyreWindow.Modifiers.cs` named the same retired product in Undo entries (`Edit Pyre Plus`, `Edit Pyre Plus form`, `Edit Pyre Plus modifier`, `Create Pyre Plus Views`) and one on-screen notification (`A Pyre Plus asset needs at least one layer.`).

The project retired that name on 2026-08-23 and CLAUDE.md says to write `Pyre` everywhere. All fifteen now do. **Verified live after a clean recompile**, cold-opening the window from its menu item: `Save` reads "No **Pyre** is open, so there is nothing to save.", `New` reads "Create a brand new **Pyre** asset (undoable).", the browser header reads "**Pyre** library (33)", and **0** elements in the window carry "Pyre Plus" in their text or tooltip **except** the names of assets already saved on disk (`New Pyre Plus.asset`, `New Pyre Plus 1.asset`, `New Pyre Plus§.asset`, `Metafield Pyre Plus`). Those are authored data — rule 7 — and renaming them is the owner's call; it belongs with T-0300, which is about one of those same files.

Files touched: `Editor/Pyre/PyreWindow.cs`, `Editor/Pyre/PyreWindow.Forms.cs`, `Editor/Pyre/PyreWindow.Modifiers.cs`. No serialized field renamed, no default value on an asset changed, no render code touched.

## 7. Every archived probe, re-run unchanged — twice, before and after the edit

| probe | result | verdict |
|---|---|---|
| T-0271 `roundtrip-probe.cs` | **ROWS 33, DIFFERING 0** | reproduces (both runs) |
| T-0283 `fill-state-probe.cs` | 36 rows identical, **heightField four = 246 / 0 / 310 / 0** | reproduces |
| T-0278 `t0278_saved_probe.cs` | steps 4→32 **0**, bevel 3→16 **0**, Stepped→Dome **9163** (Rect) / **2753** (Star), all three frames of both documents | reproduces exactly |
| T-0267 `t0267_height` / `_sweep` / `_bagmember` | **3911** / **1042** / **1536 of 6144** | reproduce |
| T-0284 `solid-position-probe.cs` | Scale Y Box **1438** / Pyramid **1233** / Can **1528** / Orb, Gem, Ring **0**; Ring rotation **0** | reproduces exactly |
| T-0284 `mask-three-layer-probe.cs` | 2992 → **1304**, invert **1800**, frames 1304/1375/1814/1998/2065/2031/1945/1304, saved+reloaded **1304** | reproduces exactly |
| T-0284 `t0285-duplicate-sentinel-probe.cs` | `fileUnchanged=True, mtimeUnchanged=True` in **every** run (warm runs also `dirty=True, inMemoryWidth=199`; the first run after the folder is recreated reads the degraded `dirty=False, inMemoryWidth=32`, exactly as T-0299 documented) | reproduces |
| T-0284 `t0285-duplicate-graph-probe.cs` | frames 0/4/7 **0 changed**, 2768/2830/2768; src 4129, dup 4130, **SHARED 0**; 0 nulls | reproduces exactly |
| T-0284 `views-reapply-probe.cs` | picked → 9/9, rearranged → 0/9, re-picked same → 0/9, away and back → **9/9** | reproduces |
| T-0284 `overflow-probe.cs` | **0 of 388** at session start and **0 of 33** at the end | reproduces |
| T-0284 `placement-pads-probe.cs` | Translate / Origin / Scale / Skew all 4 → 19 on a real `PointerDownEvent` | reproduces exactly |
| T-0276 `a12verify.cs` (text / posterise / combine) | all three fixes reproduce with their sentences intact | reproduces |
| compile | `recompile_status` completed, failed=false, errors=[]; `scriptCompilationFailed` False | after the edit |

Two archive notes for the next pass, on top of T-0299's three:

1. **`a12verify.cs` is a Coplay `execute_script` file, not a `unity eval_file` one** — it declares `public static string Execute(JObject)` and needs `arguments` (`{"mode":"text"|"posterise"|"combine"}`). Fed to `eval_file` it produces a wall of bogus "does not exist in the current context" errors that look like a broken tree and are not.
2. **The archived `overflow-probe.cs` reading depends on the section toggle bar's saved selection.** With every section switched on (as the column pass needs) it reports the left pane's own ScrollView as a 1509 px overflow — that is the scroller doing its job, not a regression. Restore `ZuiSectionToggleBar.ShaperWindow.userSel` before quoting the archived 0.

## 8. Chunks and Launimator — the shared ZUI changes did not break them

Both opened once from their own menu items, at 1500×1100, with the same overflow rule used above.

| window | elements | buttons | without tooltip | overflowing their own box | controls off the right edge | truncated labels |
|---|---|---|---|---|---|---|
| `ChunkWindow` (`Laubrary/Chunks`) | 48 | 4 | **0** | **0** | 0 of 4 | 2 (the `●` dot; a thumbnail caption ellipsised by 2 px) |
| `LauminationBuilderWindow` (`Laubrary/Laumination Builder`) | 35 | 7 | **0** | **0** | 0 of 7 | **0** |

Captures: `shots/a25-chunks-223606.png`, `shots/a25-launimator-223644.png`.

**By eye:** Chunks opens on its empty state — an unbound `None (Chunk Spec)` picker with `Save` greyed, `New`, `Browse`, and a "Chunk library (7)" grid whose **seven thumbnails all render**. Launimator's builder opens on its two banner lines, the sheet loader row (`None (Texture 2D)`, Load, Recent ▾) and the URL row, with Save / Restore / Clear correctly greyed on an empty sheet. Neither window shows a clipped caption, a control pushed out of its box, or a folded dial drawn wrong.

**`ChunkWindow.minSize` reads 50×50** — Unity's default floor, i.e. it sets none, which is the same observation T-0302 filed against Pyre before Pyre was given 820×520. Noted on the low-priority card, not changed: picking the number is an authoring decision.

## 9. Looked at and found clean

- Playback over time in **both** tools: 12 fps against an aliasing-honest 0.37 s interval, forward, wrapping, never blank in Shaper (16 distinct frames, 0 blank), a real birth/death curve in Pyre.
- Two cold walks of Shaper, one before and one after a domain reload: identical empty state (5 buttons, 0 without a tooltip), identical New (one layer, 16 frames, 12 fps, 96×64, **504 lit pixels at frame 0**, `dirty=False` on create), identical playback samples, identical 16-frame lit table.
- Re-entry: closing every instance and reopening from the menu returns to the **empty state**, not the last document — the shipped behaviour, unchanged.
- Every swarm dial's tooltip present in every state; the two genuinely inert ones greyed with their reason.

## 10. Cleanup

`Assets/Shaper/` is back to exactly the two pre-existing scratch documents, and `Assets/Pyre/` to its four pre-existing specs plus the Green Lantern bake and `Imported/`. Deleted with their `.meta`s: `AuditA25a.asset`, `AuditA25b.asset`, `ShaperViews.asset`, the `Audit0271/`, `Audit0277/`, `AuditA19/` folders (recreated by the archived probes) and `Assets/Pyre/AuditA25/` (the scratch spec plus its bake).

A SHA-256 comparison against the session-start baseline shows `Green Lantern.asset`, `New Pyre Plus.asset`, `New Pyre Plus 1.asset`, `New Pyre Plus§.asset`, `New Shaper.asset` and `New Shaper 1.asset` all **byte-identical**, and the editor reports **0 dirty assets** project-wide. The section toggle bar's `userSel` is restored byte-for-byte to its session-start value; every `A25.*` pref and SessionState key this pass wrote is deleted; Pyre's `leftPaneWidth` is back at its 360 default; `Undo.ClearAll()` was run and every tool window closed. `git status` lists only the three Pyre files this task changed, plus the modifications that were already present when it began.

**One thing this pass could not put back**: Pyre's per-section fold states. Bisecting §1 folded all 28 sections and opened them one at a time; the persisted state is whatever the last rebuild restored (Shape open, the rest shut). No asset is affected — these are window prefs — but the owner may find Pyre's cards folded differently than they left them.
