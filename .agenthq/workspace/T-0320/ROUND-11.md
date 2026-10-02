# T-0320 — the eleventh full pass (2026-09-09)

Worktree `D:\UNITY\Laubrary Dev - Shaper`, branch `feat/shaper`, HEAD `b9159687` at session start, editor on port 7801. `Application.dataPath` was confirmed as `D:/UNITY/Laubrary Dev - Shaper/Assets` at the head of the session and again at cleanup. Measurements come from T-0312's shared hierarchy-walking library (`workspace/T-0320/probes/zlib.cs`, a copy of T-0318's); probes in `probes/`, dumps in `out/`, screen captures under the session scratchpad's `shots/`.

**The headline is not a finding, it is a channel.** Nine passes have said "not verified by eye"; T-0318 measured that PrintWindow returns a blank client area for these UI-Toolkit windows and that the programme therefore had no eye channel at all. **It does now.** The obstruction was physical: a Microsoft Store window sat on top of the Unity window, so a composited-desktop read returned the Store. Minimising that one window and reading the desktop over the target window's own rect returns real pixels. Everything in §1 below was seen, not inferred — the first time in this programme.

**This pass found 6 non-trivial items, all fixed here**, plus one defect this task introduced in its own fix and caught in its own walk. Four of the six had been invisible to nine passes of probes because they are things you can only see: a control that grows when you Tab onto it, a menu whose columns fall off the window, a header printing a file path, one box saying its own name three times.

---

## 0. The by-eye channel, so the next pass does not have to rediscover it

| step | what to do |
|---|---|
| 1 | Nothing may be on top of the Unity window. `EnumWindows` returns windows in z-order — anything listed **before** the target window is above it. Minimise it (`ShowWindow(hwnd, 6)`). `SetForegroundWindow` from another process is refused by Windows and does not help (measured again this session). |
| 2 | Set the window's title and position in one eval, capture in the **next** one. `EditorWindow.position` is in editor points; multiply by `EditorGUIUtility.pixelsPerPoint` (2.25 here) for desktop pixels. |
| 3 | Capture with `System.Drawing.Graphics.CopyFromScreen` over that rect, reached by reflection (`Assembly.Load("System.Drawing")`) — an `eval_file` body cannot declare a `DllImport`, so P/Invoke is not available, but this needs none. `probes/cap2.cs` is the whole thing, 15 lines. |
| 4 | Keep the window inside the desktop. The desktop is 3840×2160 at 2.25 points/pixel = **1706×960 editor points**; a window taller than ~940 points is cut off, and a screen read cannot see what is not on screen. |
| 5 | Crop before reading the PNG — a 3150×2026 capture reads at half scale, and a 2px focus ring or a 12px glyph does not survive that. |

PrintWindow was re-tested and is still blank for these windows; do not spend time on it again.

**One caveat, stated because it bit this task twice:** a synthetic pointer press only reaches a control that is actually **on screen**. A `Clickable` ignores a press at a point outside the panel, so a button scrolled below the window's bottom edge silently does nothing — twice this session that read as "the affordance is broken" and was the probe. Scroll a control into view before pressing it, and check `worldBound.yMax <= position.height`.

---

## 1. What was checked, and what it shows

### 1.1 The last round's landings, by eye — the thing no pass had done

| landing | verdict |
|---|---|
| **T-0319 Pyre divider clamp** | **Verified by eye and by probe.** The divider was dragged fully right *through the window's own drag path* (pointer captured on `verticalSplitter`, 40 real `PointerMoveEvent`s) at a 1000px window. It clamps at **725.78 = 1000 − 260 − 14.22**, and 14.22 is exactly the measured 8px root padding + 6.22px resolved divider width. **0 of 1476 drawn elements past the window**, `maxRight` exactly 1000.0. The capture shows the preview, transport, GIF, Bake, backdrop and Cherry Framing all inside the window. |
| **T-0315 section bar breaking onto a second row** | **Verified by eye.** Chunks at exactly 820: `Sections \| Toggle Bar` plus 11 sections on row 1, the remaining 6 wrapped onto row 2, left-aligned with the first, all 17 inside the window. It reads as one control, not as two. Shaper's own bar does **not** wrap at 820 — it does not need to (9 entries, ends at 634 of 820). |
| **T-0314 list rows wrapping inside cards** | **Verified by eye** on Explosive Jet's `Schedule (1)` card: `#1 · Blast at · Violence · Share · Seat X` on the first line, `Seat Y` wrapped onto the second, the `×` still pinned top-right, nothing outside the card. |
| **T-0316 keyboard focus ring** | **Verified by eye** (cropped capture): a 2px accent border around the whole control, unmistakable. **0 style-related console entries** after a forced reimport of `ZuiToolkit.uss` with the console cleared first — the sheet parses clean. But the ring *resized* the control: finding 2.1. |
| **the taller blast row in Pyre** | **Seen.** One blast is two lines (~44px). A schedule of several is a stack of two-line cards, which reads fine; this is the judgement T-0314 and T-0318 could not make. |

### 1.2 The cold walk, twice, temporally

Everything below went through the window's real affordances — the toolbar's own buttons, the shape picker's own menu, the section toggles — never through a data write.

| step | walk 1 | walk 2 |
|---|---|---|
| close every Shaper window, open from `Laubrary/Shaper` | binds nothing; the empty state is the library | identical |
| **the empty state, by eye** | `[None (Shaper)] [Save greyed] [New] [Browse]`, `Shaper library (3)` with **all three thumbnails resolving**, `▶ Animate all` + `Refresh`. Duplicate/Rename/Delete correctly absent. | identical |
| press **New** | opens an inline `Asset name [ ] [Create] [Cancel]` row in the toolbar | identical (the second invocation in one session behaves the same) |
| type a name, press **Create** | `Assets/Shaper/AuditT0320W1.asset`, bound | `Assets/Shaper/AuditT0320W2.asset`, bound |
| the fresh document, by eye | one layer, a Rectangle, a Solid fill, a light, a grey rectangle in the preview — first run shows something | identical |
| **pick each shape family once**, through the picker's own menu | Ellipse, Solids›Orb, Bag›Combine children, Explosions›Inferno, Kiln›Energy Explosion›Arc Burst, Kiln›Energy Projectile›Orb, Kiln›Flame›Torch, Simulations›Fire, Pyre›Disc — **9 of 9 took, the caption followed each time** | Primitives›Star |
| add a fill / an edge / a light / height / a mask / a swarm | the fresh document already carries a fill; **Add edge** → the box turns into Width·Sits·Joins coverage + Remove edge; **+ Add light** → 2 light cards; **Height**, **Mask**, and the Swarm header toggle all latch | same |
| **Play 3s, sampling 1.5s apart** | all **16 frames reached** in 3.0s at 12fps; samples landed on frames 12, 15, 0 | — |
| the picture over time | frames 0 / 4 / 8 / 12 hash `F4101DC5` / `90B3C1A5` / `AB33F8C5` / `F9B45C25` with **0 / 788 / 1296 / 960 lit pixels** | frames 0/4/8/12 all distinct |
| **Save → force-reimport off disk → same pixels** | `f0=F4101DC5/0 f4=90B3C1A5/788 f8=AB33F8C5/1296 f12=F9B45C25/960` before and after — **identical** | `f0=D2172050/5032 f4=73B5DBD6/5034 f8=73B5DBD6/5034 f12=9E2F5A16/5034` — **identical** |

Two things worth naming from that table. **Frame 0 of a hosted generator is legitimately empty** (lit = 0): a check that samples only frame 0 sees every generator produce the same blank picture and reads as "nothing draws". That is what a frame-0-only sweep would have concluded here, and it would have been wrong. And **the round-trip test was run with the document dirty before the save**, so the on-disk form reproduces the in-memory picture, not merely itself.

### 1.3 Every audited state, before and after this task's edits

| state | elements | drawn | controls | captions short | overflow-X | off-window | no tooltip | inert w/o reason |
|---|---|---|---|---|---|---|---|---|
| Shaper L0, pane 400 / 800 / 1500 | 1319 / 1320 / 1322 | 905 / 906 / 908 | 252 | **0** | **0** | **0** | **0** | **0** |
| Shaper L1, pane 400 / 800 / 1500 | 1336 / 1337 / 1339 | 937 / 938 / 940 | 229 | **0** | **0** | **0** | **0** | **0** |
| Pyre, all 9 forms at 2 columns | 753–1661 | — | 104–245 | **0** | **0** | **0** | **0** | 0 |
| Pyre Explosive Jet at 1 / 2 / 4 columns | 1660 / 1661 / 1663 | — | 245 | **0** | **0** | **0** | **0** | 0 |
| Pyre Explosive Jet, every box open, **820** | 1660 | 1064 | 284 | **0** | **0** | **0** | **0** | 0 |
| Chunks (WallDebris, 17 sections open) 820 / 1400 | 496 | 399 | 84 | **0** | **0** | **0** | **0** | 0 |
| Laumination Builder, empty state, 900 | 35 | 32 | 10 | **0** | **0** | **0** | **0** | 0 |

Shaper's counts are **+1 element** against T-0318's in every state. That is not this task: `Zui/Toolkit/ZuiViewBar.cs` is modified in the working tree by the parallel T-0310 rename, and the Views row now carries a **Rename** button it did not have. This task did not touch that file.

### 1.4 Two "clipped captions" that are not defects — a methodology result

The first Pyre sweep reported 1 clipped caption on `InfernoForm` and 1 on `JetForm`: a `zui-microslider__value` reading `0.89` with 5.3px of room where it needed 22.7. Both vanish with the preview **paused**, and re-measuring the same control seconds later showed `1` fitting in exactly its 5.3px. The value label is content-sized and absolutely positioned; while the preview plays, an animated dial's text changes between the layout pass and the read. It is a one-frame artefact of measuring a running preview, not a clip a user can see. **Every sweep in this programme is sensitive to whether the preview was playing**, which no previous round states.

### 1.5 Bake, swept with every output on

On a scratch document, destination `Assets/Shaper`, PPU 16:

| output | result |
|---|---|
| Sprite sheet PNG | `768×128` — 16 frames of a 96×64 canvas, 8×2. **`spritePixelsPerUnit = 16`, exactly what the box says.** `filterMode = Point`, `spriteImportMode = Multiple`, **16 sliced sprites**. |
| AnimationClip | 16 frames, `length 1.3333s`, `frameRate 12` — the document's own rate. |
| ShaperClip | created, `ShaperClip 'AuditT0320W2 Clip'`. |
| GIF | 28,548 bytes. |
| **a second Bake** | wrote `..._1.png`, `..._1.anim`, `..._1.gif`, `... Clip_1.asset`. **Nothing was overwritten — versioned paths confirmed.** |

`Sprite sheet PNG` is correctly greyed with its reason (always produced); `GIF scale` and `GIF dither` grey until GIF is on and enable the moment it is.

### 1.6 Keyboard

All **12** leaf controls of the Layers card are focusable (0 not focusable) — T-0316's guarantee holds a round later. Per control kind, driven live:

- **`ZuiToggleButton`** activates from the keyboard: `Lighting` went on → off on a navigation submit (what Enter/Space raise), and the layer's `✔` mute did the same and visibly changed the picture.
- **`ZuiMicroMinMax`** nudges by one whole frame per press: `Lifetime` `_low` 4 → 5 → 6 → 7 on three Rights. T-0318's integer fix holds.
- **`ZuiValue2DControl`** takes focus and shows its ring (§2.1).

A card-**wide** keyboard sweep is not measurable in one pass: each activation rebuilds the card, so every element reference after the first is stale. That is a probe limit, stated as one, not a finding.

### 1.7 The shape picker menu, seen for the first time

Nine columns — Primitives, Solids, Bag, Explosions, Kiln›Energy Explosion, Kiln›Energy Projectile, Kiln›Flame, Simulations, Pyre. Two things were checked and are **not** defects:

- **Eleven names appear twice and `Orb` three times** (Box, Pyramid, Can, Orb, Gem, Ring, Star, Polygon, Text, Fire, Fireball). Every duplicate carries a distinguishing tooltip — *"Draw a shaded pseudo-3D orb…"* vs *"Orb: bake this generator's own picture into the node…"* vs *"Draw Pyre's orb — the real Pyre layer, with its own life envelopes…"*. The only on-screen differentiator is the column header. Deliberate and explained; left alone.
- Only **one** raw native `Toggle` exists across Shaper, Pyre and Chunks — the `Foldout` header of the `ListView` a `PropertyField` generates, which is the sanctioned reorderable-list island.

---

## 2. The findings

### 2.1 The focus ring resized the control it was on — fixed

`.zui-kbd-focus:focus` set `border-width: 2px`, and a border is part of the box. `ZuiPad` and `ZuiMicroMinMax` declare a 1px border and an explicit size, so their outer box does not move. **`ZuiValue2DControl` declares neither**: measured live, focusing one grew it **184.44×21.78 → 188.89×26.22** and pushed the control below it **down 4.44px**. Tab across a card and the card shuffles under the cursor — the stable-workspace rule ("reserve the space up front, never reflow") inverted by the very control that is meant to say "you are here".

Fixed by reserving the 2px transparently at all times on that control alone (`zui-value2d`), so `:focus` only colours it. Measured after: focused and unfocused geometry are **identical** (188.89×26.22 both ways, the next sibling stays at y 558.22), and the ring still draws — seen in the capture.

`Zui/Toolkit/ZuiToolkit.uss` (new `.zui-value2d` rule beside the focus rule), `Zui/Toolkit/ZuiValue2DControl.cs:277`.

### 2.2 Pyre's "Life (frames)" was the one native-looking slider left on the card — fixed

`Z.MinMax(…, 130f, isInt: true)` inside a `Z.Field` drew a grey two-handle slider flanked by two numeric boxes, directly above `Alpha`, `Root spread 2.3 – 14.6`, `Tongue climb 9 – 42` and every other range in the window, all of which are embedded `ZuiMicroMinMax`es. The layout rules prefer `Z.MicroMinMax` and keep `Z.MinMax` for pairs whose typed precision matters; a whole-frame index over 0..frameCount−1 has none to lose, and Shaper's own equivalent (`Lifetime`) has been a MicroMinMax all along.

Now one embedded control, `decimals: 0`, which also gives it T-0318's whole-number arrow nudge. Verified by eye: `Life (frames)  0 – 15`, indistinguishable in style from every dial under it.

`Editor/Pyre/PyreShapeCards.cs:110-137`.

### 2.3 Six boxes titled X containing a box titled X — fixed, systemically

Explosive Jet drew **`Fracture` inside `Fracture`, `Fracture 2` inside `Fracture 2`, `Flash` inside `Flash`, `Chunks` inside `Chunks`, `Gobs` inside `Gobs`, `Dust` inside `Dust`.** `Fracture 2`'s inner box is `Advanced` and therefore folded, so the outer box shrank to a **94px stub holding nothing but a repeat of its own name** — visible in the capture as a small orphan card among full-width ones.

The cause is general, not Jet's: a nested `[Serializable]` settings field is drawn as a titled box from its `[ZUILabel]`, and its members name the same concern in their `[ZUIGroup]`, so `ZuiReflect` made a second box with the identical title inside the first. Fixed in `ZuiReflect.EmitGroup`: when a group's name matches the title of the box it is already inside, that box **becomes** the group's box — the fields flow straight in and the group's `Advanced` fold transfers, so nothing is lost. Needed one new read-only accessor, `ZuiBox.TitleText`.

Measured after: Pyre's titled boxes 43 → 37, **duplicate-title nestings 6 → 0**, Shaper and Chunks unchanged at 0. **No dial was lost** — each flattened group draws exactly the number of fields it declares: Fracture 12/12, Fracture 2 8/8, Flash 5/5, Chunks 9/9, Gobs 11/11, Dust 14/14. Verified by eye.

`Zui/Toolkit/ZuiReflect.cs` (EmitGroup + `EnclosingBox`), `Zui/Toolkit/ZuiBox.cs`.

### 2.4 A box titled "Edge" holding a field labelled "Edge" holding "Add edge" — fixed

Three "Edge"s in one 24px row on every node without an edge strip. This is the case the layout rules name explicitly ("never title a box that holds exactly one field … says nothing twice"). Measured mechanically across three windows: **exactly one** such echo exists (Shaper), box body child count 1. The field's sentence moved onto the button's tooltip and the wrapper is gone.

`Editor/Shaper/ShaperWindow.Sections.cs:1199`.

### 2.5 Three shape-picker column headers printed a file path — fixed

`Kiln/Energy Explosion`, `Kiln/Energy Projectile`, `Kiln/Flame` beside six plain words. `[PyreFormInfo(group: …)]` is a path that Pyre's own picker nests; Shaper draws one flat column per group and printed the raw string. Now shown the way this codebase already writes a path in its own prose — `Kiln › Energy Explosion`. The grouping key itself is untouched. Verified by eye.

`Editor/Shaper/ShaperShapePicker.cs:121-131`.

### 2.6 The shape picker menu spilled a third of itself off the window — fixed, systemically

At the window's own declared minimum (820), the menu asks for a 1060px minimum width, resolved **1163 wide, and spilled 349px**: **three of its nine columns — Kiln › Flame, Simulations and Pyre — sat entirely off-window**, with no scroller and no way to reach them. Nine passes measured element overflow inside windows and never opened a menu.

`ZuiPopover.Place()` clamped the panel's **position** and never its **width**, so a panel wider than the window simply hung off the right edge. The menu's own column row already declares `flex-wrap` for exactly this case; the fixed minimum was what stopped it wrapping. Fixed in `ZuiPopover` — the window's width now wins over a requested minimum, and the maximum is capped to it — so **every** ZUI menu and flyout gets it, not just this one. Measured after: **808 wide inside an 820 window**, columns wrapped onto a second row, spill −5.8px horizontally and −6.2px vertically, all nine columns reachable. Verified by eye.

`Zui/Toolkit/ZuiPopover.cs`.

### 2.7 A defect this task introduced, and the walk caught — fixed

The first version of 2.6's fix returned early whenever it applied a cap, expecting the re-layout to call `Place()` again. A cap that **changes nothing** (a panel that already fits) fires no `GeometryChangedEvent`, so `Place()` never ran again and the panel stayed at its pre-placement `visibility: Hidden` — **every menu in a window wide enough to hold it opened invisible and unclickable.** It was found because the cold walk kept picking shapes and the picker stopped responding, not because any audit flagged it (an audit sees the elements; it does not see that they are hidden). Fixed by only deferring when the cap actually narrows the panel, and clamping the width locally for the placement maths. Both cases re-measured: 820 → 808 wide, wrapped, `visibility=Visible`; 1400 → 1163 wide, placed at (7.11, 215.56), `visibility=Visible`, and picking works again.

This is the strongest argument in this report for the walk: a probe suite that was clean at every width would have shipped it.

---

## 3. Files touched

| file | what |
|---|---|
| `Zui/Toolkit/ZuiToolkit.uss` | `.zui-value2d` reserves the focus ring's 2px transparently, so `:focus` only colours it |
| `Zui/Toolkit/ZuiValue2DControl.cs` | carries that class |
| `Zui/Toolkit/ZuiReflect.cs` | a group whose name matches the box it is inside uses that box instead of drawing a second one |
| `Zui/Toolkit/ZuiBox.cs` | `TitleText`, a read-only accessor the above needs |
| `Zui/Toolkit/ZuiPopover.cs` | a popover is never wider than its window; a cap that changes nothing no longer leaves the panel hidden |
| `Editor/Pyre/PyreShapeCards.cs` | "Life (frames)" is one embedded MicroMinMax |
| `Editor/Shaper/ShaperShapePicker.cs` | a category's column header shows path separators as "›" |
| `Editor/Shaper/ShaperWindow.Sections.cs` | the Edge box's redundant one-field wrapper is gone |

**No Pyre runtime or form file, no `CHANGELOG.md`, not committed** (ShaperHarmony rule 3). `Zui/Toolkit/ZuiViewBar.cs` is also modified in the tree — that is the parallel T-0310 rename and was not touched here. Three compiles this session, all `completed, failed=false, errors=[]`; the last one is the state the tree is in.

## 4. State left behind

`Assets/Shaper` holds only the two untracked `New Shaper*.asset` files that were there at session start (leftovers from earlier rounds — **not this task's, and not deleted**, since nothing says they are disposable). `Assets/Pyre` is back to its committed contents. Everything this task created was deleted with its `.meta` through `AssetDatabase.DeleteAsset`: `AuditT0320W1.asset`, `AuditT0320W2.asset`, the four bake outputs and their four `_1` versions, `Assets/Pyre/AuditT0320.asset`, and `Assets/Pyre/AuditT0313.asset` (recreated by the shared setup probe this session). Every `T320.*`, `T313.*`, `T0312.*` and `ZUI.Split.*` pref deleted; `ZuiSectionToggleBar.ShaperWindow.userSel` restored verbatim to `Views=1;Canvas=0;Layers=0;Shape=0;Fill=0;Swarm=0;SpriteFX=0;Lights=1;Tags=0` with `barMode` true; `Shaper.lastView` deleted. Pyre's `leftPaneWidth` put back to 360 before its window was closed; Chunks, Pyre and the Laumination Builder closed. `Undo.ClearAll()`, console cleared. The demo document reads `dirty=False`, was never saved and never edited; `git status` shows `Assets/Demos/ShaperDemo/` untouched. `scriptCompilationFailed=False`, `isPlaying=False`. Play mode was never entered; the Test Runner was never run.

**One deliberate change outside the project:** the Microsoft Store window that was covering the Unity window is **minimised**, not closed. It is one click away on the taskbar. Leaving it minimised is what keeps the by-eye channel working for the next pass.

## 5. Verified how

**By probe, in the live editor:** every table in §1 and every number in §2 — the divider drag and its 0-of-1476 spill, the clamp arithmetic against the measured 8+6.22px, the six Shaper states and nine Pyre forms before and after the edits, Explosive Jet at 1/2/4 columns and at 820 with every box open, Chunks at 820 and 1400, the Laumination Builder at 900, the focus-geometry measurement before and after the fix, the duplicate-title count 6→0 and the per-group dial counts, the field-label echo count, the popover width and spill at 820 and 1400, the two cold walks with their frame hashes and lit-pixel counts, the 16 frames reached in 3s, both save/reload round-trips, the bake outputs' dimensions/PPU/filter/sprite count/clip length/versioning, the keyboard nudges and submits, the raw-Toggle census, the USS reimport with a cleared console, three compiles, and the asset/pref cleanup.

**By eye, in real captures of the running editor** (a first for this programme): the empty state and its three resolving thumbnails; a fresh document; the Views and Lights sections; the Layers card; the focus ring at 4× crop; Shaper's section bar at 820; Chunks' two-row section bar at 820; Pyre after the divider was dragged fully right; Explosive Jet's wrapped blast row, its doubled `Fracture` boxes before and its single one after; `Life (frames)` before and after; the shape picker at 1400 and its wrapped nine columns at 820 with the corrected headers.

**Not verified:** (a) no *human* has operated any of this — every press was a synthesized pointer or key event, which is the same code path a real one takes but not the same hand; (b) the Laumination Builder was audited in its **empty** state only (35 elements) — T-0318's populated state was not re-run; (c) Zoetrope and Mirage were not re-audited this round; (d) Chunks' 17 sections were opened but no section's contents were driven; (e) the IMGUI halves (Lauminary Browser, Sprite Catalog, the Tags island) remain invisible to the hierarchy walk and were not audited; (f) the Tags section was read but no tag was created or removed — that writes the shared `LauTagLibrary` side table and would have left an orphan entry behind a deleted scratch asset; (g) the shape picker's eleven duplicate names are *explained* by their tooltips but nobody has confirmed with the owner that having both a "Solids › Orb" and a "Pyre › Orb" in one menu is intended; (h) the Views bar was inspected but not exercised — `Zui/Toolkit/ZuiViewBar.cs` is owned by T-0310 this session. Its saved-view list is a native `DropdownField` and its name field carries no label; both are defensible (a dynamic list is not an enum, and the field's tooltip explains it) and are flagged for the owner of that file rather than changed here.

---

## 6. Verdict

The programme ends only when a full pass reports nothing beyond trivia. This one found six non-trivial items, two of them systemic ZUI defects that reach every tool, and four of the six were things only an eye can see — which is exactly why nine passes missed them. Large parts of the surface have still never been looked at: Chunks' and Launimator's bodies, Zoetrope, Mirage, Pyre's right pane, and every Shaper section that did not happen to be captured here.

**FOUND: 6 non-trivial items, next pass needed**
