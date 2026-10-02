# T-0314 + T-0315 — the two layout overflows that needed the editor (2026-09-09)

Worktree `D:\UNITY\Laubrary Dev - Shaper`, branch `feat/shaper`, HEAD `5ced8031` at session start, editor on port 7801. `Application.dataPath` was confirmed as `D:/UNITY/Laubrary Dev - Shaper/Assets` at the head of every probe and again at cleanup. Every number below was read out of the running editor through T-0312's shared library (`workspace/T-0312/probes/zlib.cs`); this task wrote no second walker. Probes: `probes/`, dumps: `out/`.

Both findings were the same shape — a control that **could not give a pixel** in a container narrower than its content — and neither could be fixed by picking a smaller number, because in both cases the arithmetic says no number fits. Both are fixed by letting the thing WRAP, which is the only remedy that holds at every width.

---

## 1. T-0314 — an ExplosiveJet blast's last three dials and its remove ×

### What it was

`ZuiReflect.BuildList`'s compact branch drew a list element made only of bounded scalars as ONE `NoWrap` row: `#n` + its dials + a flexible gap + `×`. Its comment said "the dials shrink instead". They cannot: every child of that row resolves `flex-shrink: 0`, and `ZuiMicroSlider` carries a 60px `min-width` besides.

Measured before, Pyre, `ExplosiveJetForm`, dial pane 360 (the width `PyreWindow.leftPaneWidth` defaults to), card body **293.8px** — `out/compactrow-ejet-c1-BEFORE.txt`, reproducing T-0313's numbers exactly:

```
[1] ZuiMicroSlider 'Blast at' w=100.0  x= 63.1..163.1
[2] ZuiMicroSlider 'Violence' w=100.0  x=169.3..269.3
[3] ZuiMicroSlider 'Share'    w=100.0  x=275.6..375.6   << past the box
[4] ZuiMicroSlider 'Seat X'   w=100.0  x=381.8..481.8   << past the box
[5] ZuiMicroSlider 'Seat Y'   w=100.0  x=488.0..588.0   << past the box
[7] Button '×'                w= 21.8  x=603.6..625.3   << 300.9px past the box
```

### What it is now

The row is **three parts, and only the middle one wraps**: `#n` │ a wrapping dial strip │ `×`. The outer row stays `NoWrap`, so the remove button can never be pushed onto a line of its own — the "confusing empty space" the card-layout rule warns about, and the reason the original was written NoWrap in the first place. The dials sit in their own `Z.Row` with `flex-wrap: Wrap`, `flex-grow: 1`, `flex-shrink: 1`, `min-width: 0`; the strip's grow is what still pins the `×` to the right edge now that the flexible spacer is gone, and `align-items` on the outer row moved to `FlexStart` so the index and the `×` sit on the FIRST line of dials rather than floating half way down a three-line block.

`CompactOptions.ControlWidth` went 100 → **105**, which is the whole of the caption fix that was riding along: a MicroSlider's caption ends 56.9px short of the control's own width (a 6px inset plus the 46px value reserve), so at 100 the `Violence` caption had 43.1px for a string needing 44.9 and was clipped. At 105 it has 48.1.

Measured after, same state — `out/compactdials-ejet-c1-AFTER.txt`:

```
row=30.7..324.4   card body=30.7..324.4 (293.8px)   spill=0.0px   rowHeight=65.8
STRIP w=223.6 content=63.1..286.7 wrap=Wrap
  'Blast at' x= 63.1..168.0 line=0   caption need=39.1 have=48.0  ok
  'Violence' x=174.2..279.1 line=0   caption need=44.9 have=48.4  ok
  'Share'    x= 63.1..168.0 line=1   caption need=30.2 have=48.0  ok
  'Seat X'   x=174.2..279.1 line=1   caption need=34.2 have=48.4  ok
  'Seat Y'   x= 63.1..168.0 line=2   caption need=34.2 have=48.0  ok
remove '×' x=296.0..318.2  inside the card
```

Two dials per line at the width Pyre opens at, three lines, everything inside the card, nothing clipped. The row costs 65.8px of height where it cost 22 — that is the price of five dials in a 293.8px column, and it is the only price on offer: the compact branch's arithmetic (`26 + N × width + gaps + 22`) admits **N = 2** at that width and `IsCompactRowElement` admits N = 2..5, so any element with three or more dials had to wrap or overflow.

### Both hosts, three column counts each

| state | captions short | overflow-X | off-window | no tooltip | inert w/o reason |
|---|---|---|---|---|---|
| Pyre ExplosiveJet c1 / c2 / c4 — **before** (T-0313) | **1** | **4** | 0 | 0 | 0 |
| Pyre ExplosiveJet c1 / c2 / c4 — after | **0** | **0** | 0 | 0 | 0 |
| Shaper-hosted ExplosiveJet c1 / c2 / c4 — after | **0** | **0** | 0 | 0 | 0 |

The Shaper half is the same form drawn by `Editor/PyreShaper/PyreFormShaperUI.cs`, which reproduces the window's reflected dump; the card body there is 324.0 / 327.6 / 304.0px and the strip lays out 2 / 2 / 1 the same way. Dumps: `out/audit-pyre14-ExplosiveJet-c{1,2,4}.txt`, `out/audit-shaper14-ejet-c{1,2,4}.txt`, `out/compactdials-*.txt`.

**One thing this fix does NOT change, deliberately:** `IsCompactRowElement` still admits 2..5 fields, and a reflection scan over the whole package still finds exactly one type it admits (`ExplosiveJetSettings.blasts` → `ExplosiveBlast`). Narrowing the rule would have been a second change with nothing to fix; wrapping makes the rule safe at any count.

---

## 2. T-0315 — Chunks' section bar, 206.2px off an 820px window

### What it was

`ChunkWindow.minSize` is (820, 520). At exactly 820 the bar's per-section `ZuiSegmented` measured **1008.0px** and ended at x=1026.2 against a window root ending at 820.0, putting `Cut Preview`, `Animated`, `Hits` and `Trail` outside it — `out/togglebar-chunks-bar820-BEFORE.txt`, again reproducing T-0313's numbers. Four of seventeen sections could not be shown or hidden at all until the window was dragged past 1026px.

The outer `ZuiSectionToggleBar` was `flex-wrap: Wrap`, which sounds like it should have saved it and could not: the bar is ONE child, so the only break that container could ever make is "mode switch on line 1, the whole bar on line 2" — it could never break the bar's own buttons, which is where the width goes. And the `ZuiSegmented` itself was `NoWrap` with `flex-shrink: 0`.

### What it is now

Three changes, all in the shared control so every tool's bar is fixed at once rather than Chunks' roster being trimmed:

1. **`ZuiSegmented.Wrapping()`** — an opt-in that adds `.zui-segmented--wrap`. A short 2–3 option radio keeps the one-line joined strip and can never re-flow under the user; only a control whose roster is as long as a tool's section list opts in.
2. **`.zui-segmented--wrap` in `ZuiToolkit.uss`** — `flex-wrap: wrap` **and `flex-shrink: 1`**. The shrink is not optional: a wrap container's own preferred width is still the single-line sum, so with `flex-shrink: 0` the wrap could never engage no matter how narrow the host got. The look is carried by the same block: every segment gains its own `border-left-width: 1px` so the first segment of a broken row is not left hanging open, and the 1px comes back out of that segment's `padding-left` (8 → 7) so the swap is width-neutral by construction.
3. **`ZuiSectionToggleBar`'s own container went `Wrap` → `NoWrap`, `alignItems` `Center` → `FlexStart`** — the bar now shrinks beside the mode switch and breaks inside itself, which costs one row fewer than dropping the whole bar to line 2, and the mode switch sits beside the bar's FIRST row rather than floating half way down it.

Measured after at 820 — `out/togglebar-chunks-bar820-AFTER.txt`: the segmented is **645.3px**, ends at 809.8 against a root ending at 820.0, and lays out 11 buttons on row 0 (to `Physics`, x=776.4) and 6 on row 1 (`Life` … `Trail`, x=517.3..559.6). **Nothing off the window.**

### Reachable, not merely drawn

"Inside the window" is not "a click lands on it", so `probes/k-barreach.cs` asks the editor's own hit test: `panel.Pick` at each button's centre, plus a real `PointerDownEvent`+`PointerUpEvent` (the path a user's left click takes — `Clickable` tracks only the left button) on the last button, checking a `ZuiSection.IsOpen` actually flips.

| window at 820 | buttons | pickable | off-window | click on the last button |
|---|---|---|---|---|
| Chunks | 17 | **17** | 0 | `Trail` → exactly 1 section flipped; clicked back, 0 still differing |
| Pyre | 8 | 8 | 0 | 0 sections flipped (see below) |
| Shaper | 9 | 9 | 0 | `Tags` → exactly 1 section flipped; clicked back, 0 still differing |

`Trail` is one of the four that used to be outside the window entirely.

**The Pyre row is an observation, not a regression:** its bar is drawn and every button picks, but the click flipped nothing. `ZuiSectionToggleBar` deliberately swallows a left-click while any section is soloed (T-0076), and solo state is persisted in EditorPrefs, so the likeliest reading is that a solo was left engaged in this editor before this task started. Nothing in this task touches that code path, and Pyre's bar fits at 820 either way (473.8px). It was not investigated further because doing so would have meant changing persisted state this task is supposed to leave as found.

### Every bar, at each window's own minimum

All three windows declare `minSize` (820, 520).

| bar | sections | before, at 820 | after, at 820 |
|---|---|---|---|
| Chunks | 17 | **1008.0px, ends 1026.2, 4 buttons off-window** | 645.3px over 2 rows, ends 809.8, 0 off |
| Pyre | 8 | 475.6px, fits | 473.8px, 1 row, fits |
| Shaper | 9 | 471.6px, fits | 477.3px, 1 row, fits |

Pyre −1.8px and Shaper +5.7px across 8 and 9 segments is sub-pixel rounding of the border/padding swap at this DPI (≈±0.9 per segment), not real growth — and a bar that can wrap absorbs a pixel by definition. The USS comment records those two measurements rather than the "exactly identical" claim the arithmetic alone would have supported.

### The whole Chunks window, three widths

| Chunks | captions short | overflow-X | off-window | no tooltip | inert w/o reason |
|---|---|---|---|---|---|
| 820 — before (T-0313) | 0 | **1** | **5** | 0 | 0 |
| 1000 — before (T-0313) | 0 | **1** | **2** | 0 | 0 |
| 820 / 1000 / 1400 — after | 0 | **0** | **0** | 0 | 0 |

Dumps: `out/audit-chunks14-w{820,1000,1400}.txt`.

---

## 3. Files touched

| file | what |
|---|---|
| `Zui/Toolkit/ZuiReflect.cs` | the compact list row's three-part layout; `CompactOptions.ControlWidth` 100 → 105 |
| `Zui/Toolkit/ZuiSegmented.cs` | new `Wrapping()` opt-in (+ its doc comment) — no change to any existing behaviour |
| `Zui/Toolkit/ZuiSectionToggleBar.cs` | calls `Wrapping()` on the per-section bar; container `Wrap` → `NoWrap`, `Center` → `FlexStart` |
| `Zui/Toolkit/ZuiToolkit.uss` | one new block, `.zui-segmented--wrap` (+ its segment rule) |

**No Pyre runtime or form file, no `Editor/PyreShaper/PyreFormShaperUI.cs`, no `ChunkWindow.cs`, no `CHANGELOG.md`, not committed** (ShaperHarmony rule 3). Neither card's scope needed a form attribute or a window edit in the end: both were the shared control's problem, which is why one fix each covers every tool.

`ZuiToolkit.uss` is a **shared file** — T-0316 appended its own `.zui-kbd-focus:focus` block to it in the same window. The two blocks are disjoint and sit in different parts of the sheet; both are present in the working tree.

## 4. Stability, and state left behind

- **T-0304's idle layout sweep**, re-run after the edits: Shaper, dial pane 360 → 1400 in steps of 260 at window 1700, 2s idle at each width — **0 errors, 0 warnings at every width**. Same for Pyre. And, because a bar that wraps is exactly the shape of control that could re-lay-out forever, the same detector was pointed at **Chunks at 820 / 900 / 1026 / 1400 with a 3s idle** (`probes/w-idle-any.cs`) — **0 errors, 0 warnings at every width**.
- **Shaper's six-state audit on the committed demo document** (layer 0 and 1 × 1/2/4 columns) is **byte-identical to T-0313's**: 1318/1319/1321 and 1335/1336/1338 elements, 898/899/901 and 930/931/933 drawn, and 0 in every finding column. Nothing regressed for the tool that does not host a blast.
- **Compile**: `recompile_status` **completed, `failed=false`, `errors=[]`**. One intermediate compile failed on `ZuiMicroMinMax.cs(84,44): CS0103 'OnKeyDown'` — a sibling task's file mid-edit, reported rather than touched (PROGRAMME_RULES line 19); it cleared on its own and the final compile is clean.
- **Assets**: `Assets/Shaper` + `Assets/Pyre` are **36 files, every SHA-256 prefix identical to T-0313's end-of-task baseline** (`out/z-clean314.txt` vs `T-0313/out/z-clean.txt`, diffed: no difference). The two scratch assets (`Assets/Pyre/AuditT0313.asset`, `Assets/Shaper/AuditT0314.asset`, the latter a one-layer document hosting an `ExplosiveJetForm` so the card could be measured in its second host) were deleted with their `.meta`s through `AssetDatabase.DeleteAsset`. Nothing was ever saved to a committed asset; the demo document read `dirtyBefore=False dirtyAfter=False` at every bind.
- **Prefs**: every `T313.*` and `T0312.*` key deleted; both `ZUI.Split.*` deleted; `ZuiSectionToggleBar.ShaperWindow.userSel` restored verbatim to `Views=1;Canvas=0;Layers=0;Shape=0;Fill=0;Swarm=0;SpriteFX=0;Lights=1;Tags=0` with `barMode` true, and the solo key deleted, exactly as T-0313 left them. `Undo.ClearAll()`, console cleared, all three tool windows closed (none was open at session start). The editor reports the same **10 pre-existing dirty shaders and nothing else**, `scriptCompilationFailed = False`, `isPlaying = False`.
- The reach probe's clicks each toggled a section and toggled it straight back, so the Chunks/Shaper bars' saved selections end where they started.

## 5. Verified how

**By probe, in the live editor:** every table and every number above — the before and after compact-row geometry in both hosts at 1/2/4 columns, every caption's need-vs-have, the three toggle bars at 820 before and after, the 17/8/9 `panel.Pick` hit tests, the three real pointer clicks and their section flips, the Chunks window audit at three widths, Shaper's six-state regression audit, the three idle sweeps, the compile, and the asset/pref baseline.

**By eye:** nothing. No human and no screenshot has looked at any of these windows. T-0307's archive note still holds — `PrintWindow` returns a blank client area for these UI-Toolkit windows — so editor-window screenshots remain a non-working channel here, and forcing a window to the foreground would have stolen focus on the owner's live desktop.

**Not verified:** (a) **nobody has seen the wrapped Chunks bar**, so how a two-row section bar READS — whether row 1 and row 2 look like one control or two — is a judgment only the owner's eye can make; the geometry is right, the aesthetics are unreviewed; (b) nobody has seen the three-line blast row either, and 65.8px per blast means a Swarm of several blasts is a much taller card than before; (c) no mouse has dragged a window edge — every width was set through `EditorWindow.position`, which is what the drag handler writes, but the drag itself was not exercised; (d) the wrapped bar's SOLO and quick-view gestures (right-click) were not driven, only the left-click path; (e) Pyre's bar swallowing a left-click is unexplained (§2, almost certainly a pre-existing persisted solo) and was deliberately left alone; (f) no other window in the package was checked for the same threshold — the section-count threshold is ~810px of labels, and only Chunks crosses it among the three bars that exist.
