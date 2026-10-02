# T-0318 — the tenth full pass (2026-09-09)

Worktree `D:\UNITY\Laubrary Dev - Shaper`, branch `feat/shaper`, HEAD `455d8c6d` at session start, editor on port 7801. `Application.dataPath` was confirmed as `D:/UNITY/Laubrary Dev - Shaper/Assets` at the head of every probe and again at cleanup. Every number below was read out of the running editor through T-0312's shared library (`workspace/T-0312/probes/zlib.cs`, copied to `workspace/T-0318/probes/zlib.cs` with T-0317's proposed inert tightening appended); this task wrote no second walker. Probes: `probes/`, dumps: `out/`.

**This pass found 6 things: Pyre's divider clamp is 14.2 px short, so a divider dragged right pushes the preview pane and 23 drawn elements off the window at every width below ~1718 (filed as T-0319); the arrow-key nudge T-0316 added to `ZuiMicroMinMax` did nothing at all on an integer-quantised control, which is exactly the one whose tooltip now promises it; T-0317's Chunks fix reached one of the three leaves it named, because a `PropertyField` copies its tooltip onto the ListView it generates; Pyre's matte and delete glyphs were clipped 2.2 and 4.0 px at every width in the Layers card; the Laumination Builder's tools column hung 4.4 px outside its scroll column at the window's own declared minimum; and two `[Unreleased]` lines claimed behaviour the tree did not have. Five of the six are fixed here; the sixth is the card.**

---

## 1. What was re-run, and what it says

### 1.1 Shaper — every archived probe

The committed demo document, every toggle-bar section on, layer 0 and layer 1, at 1 / 2 / 4 `ZuiColumnFlow` columns (pane 400/800/1500 at window 1000/1300/1950).

| state | elements | drawn | text | controls | captions short | overflow-X | off-window | no tooltip |
|---|---|---|---|---|---|---|---|---|
| L0 c1 / c2 / c4 | 1318 / 1319 / 1321 | 898 / 899 / 901 | 324 | 251 | **0** | **0** | 0 | 0 |
| L1 c1 / c2 / c4 | 1335 / 1336 / 1338 | 930 / 931 / 933 | 343 | 228 | **0** | **0** | 0 | 0 |
| Shaper at its own minimum (820), every section open | 1335 | 953 | 349 | 233 | **0** | **0** | 0 | 0 |

**Byte-identical to T-0313's and T-0314's tables**, and identical again after this task's own edits (`shaper18post-*`, six states). Nothing in T-0314/T-0315/T-0316's shared-control changes moved a Shaper pixel.

### 1.2 Pyre — every card, at 1 / 2 / 4 columns

Every concrete `PyreForm` (9 authored) on a one-layer scratch spec, 27 audited states. **All 27 clean: 0 captions short, 0 overflow, 0 off-window, 0 missing tooltips.** Element counts match T-0313's exactly for the eight forms that were already clean.

**ExplosiveJet is the ninth, and T-0314's fix holds live:** where T-0313 measured 1 clipped caption and 4 overflows at every column count, this pass measures **0 and 0 at c1, c2 and c4**.

### 1.3 The four windows at their declared minimum, every section open

`ChunkWindow`, `PyreWindow` and `ShaperWindow` all declare `minSize (820, 520)`; `LauminationBuilderWindow` declares `(900, 640)`.

| window | width | elements | drawn | controls | captions short | overflow-X | off-window | no tooltip |
|---|---|---|---|---|---|---|---|---|
| Chunks (ProbeChunk, 17 sections, all open) | **820** | 496 | 399 | 84 | 0 | **0** | **0** | 0 |
| Chunks | 1000 / 1400 | 496 | 399 | 84 | 0 | 0 | 0 | 0 |
| Pyre (all 9 sections open, default pane) | **820** | 1231 | 841 | 227 | **0 after §2.4** | 0 | 0 | 0 |
| Shaper (all sections open) | **820** | 1335 | 953 | 233 | 0 | 0 | 0 | 0 |
| Laumination Builder (Hero/Idle via `OpenForEdit`) | **900** | 384 | 305 | 71 | **0** | **0 after §2.5** | 0 | 0 |
| Laumination Builder | 1400 | 384 | 299 | 71 | 0 | 0 | 0 | 0 |
| Lauminary Browser | **640** (its own minSize) / 1400 | 69 | 32 | 10 | 0 | **0** | 0 | 0 |
| Sprite Catalog | 820 / 1400 | 41 | 19 | 5 | 0 | 0 | 0 | 0 |

**T-0315's fix holds live:** Chunks at exactly 820 — where T-0313 measured 1 overflow and **5 elements off the window**, four of them section buttons that could not be clicked — is now 0 and 0, at 820, 1000 and 1400 alike.

**T-0313's own trivia holds live:** the Laumination Builder's transport pause glyph (1 clipped caption at both widths) is 0 at both, and the Lauminary Browser's Rename row (1 overflow) is 0 at 640 and 1400.

### 1.4 Zoetrope and Mirage — the shared ZUI changes reach them too

Neither window declares a `minSize`, so both were audited at 1400 and at the package's 820 floor, before and after this task's edits.

| window | width | elements | drawn | controls | captions short | overflow-X | off-window | no tooltip |
|---|---|---|---|---|---|---|---|---|
| Zoetrope (Zoes, `PreviewShooterZoe`, 8 sections, 7 folds opened) | 1400 / 820 | 285 | 208 / 214 | 51 | 0 | 0 | 0 | 0 |
| Mirage (`MirageDemo`, 3 sections) | 1400 / 820 | 166 | 120 | 35 | 0 | 0 | 0 | 0 |

Clean at both widths, unchanged after this task's edits. No regression from the wrapping segmented control, the wrapping list row or the new focus ring.

### 1.5 The cold walk, twice, with temporal samples

Close every tool window → open Shaper from its own menu item → sample at t≈0 / 1 / 4 s → bind the demo document → sample again at t≈0 / 1 / 4 s. Run twice.

| step | walk 1 | walk 2 |
|---|---|---|
| open, t0 | 57 elements, **0 laid out**, 0 errors | identical |
| open, t1 / t4 | 37 drawn, 6 controls, 0 without a tooltip, 0 errors | identical |
| bind, t0 | 1318 elements, 1 laid out | identical |
| bind, t1 / t4 | 1319 elements, 905 drawn, 251 controls, 0 errors, 0 warnings | identical |

The second walk is byte-identical to the first: no drift, no growth, no stale captured state. The empty state is the browser — Save (greyed, with its reason), New, Browse, Refresh, "▶ Animate all" — and the first frame after the click is still empty (0 laid out at t0, 37 by t1), which is why every probe in this programme reads geometry in a later eval than the one that built it.

### 1.6 T-0316 verified live: Tab reaches every dial in the Layers card, and the arrows nudge

**Static + live traversal of the Layers card** (Shaper, demo document, layer 0, window 1400), the same probe T-0313 ran:

| | T-0313 | T-0318 |
|---|---|---|
| leaf controls in the card | 20 | 20 |
| `focusable == false` | **2** (`Lifetime` ZuiMicroMinMax, `Direction XY` ZuiPad) | **0** |
| reached by `NavigationMoveEvent(Next)` | **18 of 20** | **20 of 20** |

The live order runs `✔ → ● → Star → Dup → × → ✔ → ○ → ArcBurst → Dup → × → + Add layer → Lifetime → Height → Mask → Lighting → Draws into the picture → HDR → Flat → Follow the surface → Direction XY`, then leaves the card. Both former misses are now steps 11 and 19.

**The arrow nudge, per control**, driven with real `KeyDownEvent`s on a scratch COPY of the demo document (never the committed one):

| control | nudge | one undo restores it |
|---|---|---|
| `ZuiPad` "Direction XY" | 0.00 → 0.08 over two presses | **yes** — two undos, read in a later eval with no `Rebuild`, back to 0.00 |
| `ZuiValue2DControl` "Direction" | (−50.00, 40.00) → (0.00, 40.00) | **yes** |
| `ZuiMicroMinMax` "Lifetime" | **nothing at all** | — (finding 2.2) |

Each press opens exactly one `Edit Shaper Document` undo group, so a key nudge undoes like a drag.

### 1.7 T-0317's inert rule, tightened and actually run

T-0317 proposed that a disabled control's tooltip only counts as explained if it is **state-dependent** — different from what the same control shows enabled. That rule is implemented in this task's copy of the library (`ZTipSample` in `probes/zlib.cs`, comparison in `out/twostate-A.txt` vs `out/twostate-B.txt`) and run against the Lauminary Browser with nothing selected and with `ProtoGuy` selected. Of the controls whose enabled state actually flipped between the two samples, **0 carried an identical tooltip across the flip**:

```
TextField  <rename field>  DISABLED -> ENABLED
    disabled: No lauminary is selected, so there is nothing to rename.
    enabled : New name for the selected lauminary.
```

**Scope of that claim, stated because it bounds it:** the comparison keys a control by type + caption + class, so a caption that exists twice in one state collapses; only two of the four gated controls produced a clean pair. The rule is applied, not proven exhaustive.

The 21 controls themselves were re-read live: **Lauminary Browser 4 of 4** and **Laumination Builder 14 of 14** say why they are greyed. Chunks' three were 1 of 3 — finding 2.3.

### 1.8 Stability

T-0304's layout-struggle detector, re-run after every edit in this task: Shaper, dial pane 360 → 1400 in steps of 260 at window 1700, 2 s idle at each width — **0 errors, 0 warnings at every width**. Pyre 360 → 1360 — same. Chunks at 820 / 900 / 1026 / 1400 with a 3 s idle — same.

### 1.9 Every `[Unreleased]` line added since T-0308, read against the tree

Four bullets have been added to `CHANGELOG.md` since `bb3ada3b`.

| bullet | verdict |
|---|---|
| T-0314/T-0315/T-0316 — list row wraps, section bar breaks onto a second row, "MicroMinMax, Pad and Value2D take keyboard focus and nudge with the arrow keys" | **wrap and bar verified; focus verified (20/20); "nudge with the arrow keys" was FALSE for a whole class of MicroMinMax** — finding 2.2, fixed here, which is what makes the line true |
| T-0317 — "every greyed control says why it is greyed (21 controls)" | **19 of 21 verified; 2 were still saying what they do** — finding 2.3, fixed here, which is what makes the line true |
| T-0313 — Bake Destination ellipsises with the folder in its tooltip; Launimator's pause glyph and Rename row fit | **verified**: `ShaperWindow.Bake.cs:84` sets `TextOverflow.Ellipsis` and `:89-90` names the destination in the tooltip; the two Launimator measurements are in §1.3 |
| T-0309/T-0311/T-0312 — "0 missing tooltips, 0 overflows, 0 clipped captions, 0 greyed controls without a reason in Shaper and Pyre" | **the first three verified**; the fourth is the claim T-0313 and T-0317 both corrected and nobody has reworded — it means *"every greyed control has a tooltip"*, which is not the same sentence. It also said "0 clipped captions in Pyre" while Pyre's Layers card carried two — no audited state had that card open until this pass (finding 2.4) |

The CHANGELOG line for the fourth bullet's rewording is in this task's handover under `CHANGELOG:`; programme rule 3 forbids editing `CHANGELOG.md` here.

---

## 2. The findings

### 2.1 Pyre's divider clamp is 14.2 px short — filed as T-0319

`ClampedLeftPaneWidth()` (`Editor/Pyre/PyreWindow.cs:465-469`) caps the dial pane at `min(1458, max(360, position.width − 260))`, subtracting only the right pane's own 260 px `minWidth`. It does not subtract the root row's horizontal padding (4 + 4) nor the 6.2 px divider — 14.2 px, which is exactly what spills.

| persisted `leftPaneWidth` | window | preview pane | root content ends | spill | drawn elements past the window |
|---|---|---|---|---|---|
| 360 (default) | 820 / 1400 / 1800 | fits | — | 0.0 | 0 |
| 1200 | 1400 | 1150.2..1410.2 | 1396.0 | **14.2 px** | **23** |
| 9999 | 820 | 570.2..830.2 | 816.0 | **14.2 px** | **23** |
| 9999 | 1800 | fits | — | 0.0 | 0 |

It bites at any window narrower than about 1718 px once the divider has been dragged to the cap, and `leftPaneWidth` is a persisted `[SerializeField]`, so a divider dragged wide in a big window keeps pushing the preview off afterwards. T-0313 established that dragging that divider is how a user reaches Pyre's 2- and 4-column dial stack. **Not fixed here** — arithmetic in a Pyre window file with a drag path behind it, duplicated in two places (`:467` and `:483`), is not the caption trivia this round fixes.

### 2.2 T-0316's arrow nudge did nothing on an integer-quantised MinMax — fixed

`ZuiMicroMinMax.OnKeyDown` stepped by 1 % of the range and handed the result to `SetValues`, which rounds to the control's own `_decimals`. Shaper's `Lifetime` is a frame-index range: `_min 0, _max 15, _decimals 0`, so the step was 0.15 and `Math.Round(0.15, 0)` is 0. Left and Right were **dead keys on the one control whose tooltip now says "Left/Right to nudge the last-touched handle"** — measured `_low=0 _high=15` unchanged across three presses, with `_lastHandle` never updating, i.e. the handler ran and produced nothing.

Pyre's nine `[Range] Vector2` dials (the ones T-0312 created) all have `_decimals = -1` and all move — `Root spread` 8 → 8.4, `Tongue width` 1.2 → 1.257, and so on — which is why the gap survived T-0316's own check.

Fixed by never stepping less than one of the control's own quantums (`Zui/Toolkit/ZuiMicroMinMax.cs`, one line inside `OnKeyDown`). Measured after: `Lifetime` `_low` 0 → 1 → 2, one press each. The demo document was restored to `0..15` and its dirty flag cleared; the file on disk was never written.

### 2.3 T-0317's Chunks fix reached one of its three leaves — fixed

T-0317 set `listField.tooltip` from the array size, which reached the ListView's size field and **not** the foldout header or its Toggle: those two resolved a nearer tooltip — the one Unity's `PropertyField` copies onto the `ListView` it generates when it binds, seeded from `ChunkSpec.sprites`'s own `[Tooltip]` (`Runtime/Chunks/ChunkSpec.cs:71`). Measured live: two of the three inert leaves still read *"Chunk sprites to pick from at random…"*.

Fixed by telling the generated tree too. The inner `ListView` does not exist when the field is constructed, so the update also runs on the field's first layout, not only at construction and on a value change (`Editor/Chunks/ChunkWindow.cs`). Measured after: **all three** now read *"The list is empty, so there is nothing here to fold or resize by typing — use the + below to add a sprite."*

### 2.4 Pyre's matte and delete glyphs were clipped at every width — fixed

The Layers card's `□`/`▦` matte toggle (24 px) and `✕` delete button (22 px) left **9.8 px and 8.0 px of content for glyphs that measure 12.0** — the button's own 6+6 padding and 1+1 border, not the row. Clipped at 820 and at 1400 alike; the width does not matter and never did. No audited state in this programme had Pyre's Layers card open before, which is why nine passes missed it.

Fixed by zeroing horizontal padding rather than widening the slot — the same call the Library star got in T-0311 and the transport's pause glyph in T-0313 (`Editor/Pyre/PyreWindow.cs`). Measured after: Pyre at 820 with every section open, **captionShort 0**.

### 2.5 The Laumination Builder's tools column hung 4.4 px outside its scroll column — fixed

`toolsCol` is a fixed 380 px with `flex-shrink: 0`; at the window's own declared minimum (900) the scroll column gives it 375.56. T-0313 measured this overflow and tabled it but never filed it. Fixed by letting the column shrink instead of picking a smaller fixed number — the call the ramp field got in T-0312 and the Rename row in T-0313 (`Editor/Launimator/LauminationBuilderWindow.cs`). Measured after: **overflow-X 0 at 900 and 1400.**

### 2.6 Two `[Unreleased]` lines described behaviour the tree did not have

Both are §1.9's first two rows, and both are now true because 2.2 and 2.3 are fixed. The third correction — "0 greyed controls without a reason" should read "every greyed control has a tooltip" until the inert rule itself is tightened in `ZuiAudit`/`zlib` — is still outstanding and is the one thing here that needs a CHANGELOG edit rather than code.

---

## 3. Files touched

| file | what |
|---|---|
| `Zui/Toolkit/ZuiMicroMinMax.cs` | the arrow step never falls below one of the control's own quantums |
| `Editor/Chunks/ChunkWindow.cs` | the greyed-reason tooltip reaches the generated `ListView`, and is applied once that tree exists |
| `Editor/Pyre/PyreWindow.cs` | matte-toggle and delete buttons: horizontal padding zeroed so the glyph fits |
| `Editor/Launimator/LauminationBuilderWindow.cs` | the tools column may shrink to its scroll column |

**No Pyre runtime or form file, no `CHANGELOG.md`, not committed** (ShaperHarmony rule 3). Compile after the last edit: `recompile_status` **completed, `failed=false`, `errors=[]`** (three separate compiles this session, all clean).

---

## 4. State left behind

`Assets/Shaper` and `Assets/Pyre` are **byte-identical to this task's session-start baseline** — 29 files, every SHA-256 prefix in `out/z-clean318.txt` matching `out/p0-base.txt`, nothing added, removed or changed. The scratch assets created here (`Assets/Pyre/AuditT0313.asset` from the shared Pyre setup probe, and `Assets/Shaper/AuditT0318/NudgeDoc.asset`, a copy of the demo document so the key-nudge test never touched the committed one) were deleted with their `.meta`s through `AssetDatabase.DeleteAsset`. Every `T313.*`, `T318.*` and `T0312.*` pref deleted; both `ZUI.Split.*` deleted; `ZuiSectionToggleBar.ShaperWindow.userSel` restored verbatim to `Views=1;Canvas=0;Layers=0;Shape=0;Fill=0;Swarm=0;SpriteFX=0;Lights=1;Tags=0` with `barMode` true; `Shaper.lastView` deleted; `Undo.ClearAll()`; console cleared; all eight tool windows closed (none was open at session start). Pyre's `leftPaneWidth` was put back to its 360 default before the window was closed. The editor reports the same **10 pre-existing dirty shaders and nothing else**, `scriptCompilationFailed = False`, `isPlaying = False`. The demo document read `dirty=False` at every bind, was edited only through the key-nudge test, was undone back to `0..15` and had its dirty flag cleared; `git status` shows `Assets/Demos/ShaperDemo/` untouched. Play mode was never entered and the Test Runner was never run.

## 5. Verified how

**By probe, in the live editor:** every table and every measurement above — the six Shaper states before and after the edits, the 27 Pyre form × column states, the four windows at their own declared minimums with every section open, Zoetrope and Mirage at two widths each before and after, the two cold walks with their temporal samples, the Tab traversal and the three arrow-nudge/undo tests, the two-state inert comparison, the Pyre pane-spill matrix, the four fixes re-measured, three compiles, three idle sweeps, and the byte-identical asset baseline.

**By eye: nothing, and this pass established why, which is a result in itself.** Two channels were built and measured:
- **`PrintWindow(hwnd, hdc, 2)`**, DPI-aware, into a `System.Drawing` DC *and* into a GDI DIB (T-0265's route). Both return a **blank client area** for these UI-Toolkit windows — measured, not assumed: one distinct colour across the whole client band of an 1855×2134 capture. The window frame and title bar come through; nothing Unity draws does. Tried on Chunks (armed, focused, 4 s idle, separate calls) and on a freshly `ShowUtility`'d Shaper.
- **`InternalEditorUtility.ReadScreenPixel`**, which reads the composited desktop instead. It returns real pixels — and they were the **Microsoft Store window the owner had on top**, then a blank patch of desktop, because the call takes screen coordinates and cannot be made to mean "this window". Raising the window to `HWND_TOPMOST` and calling `SetForegroundWindow` did not take the foreground (`foreground == target: False`); the window was returned to `HWND_NOTOPMOST` and its title restored immediately.

So **nobody has yet seen the two-row Chunks section bar or the three-line ExplosiveJet blast row** — T-0314 and T-0315 each said so, and this pass could not discharge it. Both are geometrically right (measured again here); whether two rows read as one control, and whether a 65.8 px blast row makes a Swarm of blasts too tall, remain judgements only the owner's eye can make. **The programme has no working by-eye channel for an editor window**, and that is now measured rather than assumed.

**Not verified:** (a) the two eye judgements above; (b) no mouse has dragged Pyre's divider, dragged a window edge, or clicked anything this pass — every width and every key press was an event or a field write, which is what the drag/key handlers themselves write; (c) the IMGUI halves of the Lauminary Browser, the Sprite Catalog and every asset-browser grid remain invisible to the hierarchy walk and have never been audited by this programme; (d) Chunks' 17 sections were audited with every section open but no section's contents were driven; (e) the tightened inert rule was run on one window pair, not everywhere, and its identity key collapses duplicate captions; (f) the Pyre pane spill (2.1) is measured but its fix is not written or tested.
