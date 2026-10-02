# T-0313 — the ninth full pass, with the corrected hierarchy-walking probes (2026-09-09)

Worktree `D:\UNITY\Laubrary Dev - Shaper`, branch `feat/shaper`, HEAD `ae66d31c`, editor on port 7801. `Application.dataPath` was confirmed as `D:/UNITY/Laubrary Dev - Shaper/Assets` at the head of every probe and again at cleanup. Every number below was read out of the running editor through T-0312's shared library (`workspace/T-0312/probes/zlib.cs`) — this task wrote no second walker. Probes: `probes/`, raw dumps: `out/`.

**This pass found 5 things: an ExplosiveJet blast row whose last three dials and remove button sit 300.9 px outside the card — invisible and unclickable at the pane width Pyre opens at; a Chunks section bar that runs 206.2 px off the window at the window's own declared minimum, putting four sections out of reach; three ZUI controls that cannot take keyboard focus at all, nine of them dials T-0312 introduced; 21 greyed controls in Chunks and Launimator that say what they do instead of why they are greyed; and four pieces of trivia, fixed here.**

---

## 1. What was re-run, and what it says

### 1.1 Shaper — every archived probe, corrected walk

The committed demo document `Assets/Demos/ShaperDemo/ShaperDemoDoc.asset`, every toggle-bar section on, layer 0 and layer 1, at 1 / 2 / 4 `ZuiColumnFlow` columns (pane 400/800/1500 at window 1000/1300/1950), plus two states T-0312 never audited: the **empty state** (window closed and reopened with nothing bound) and the **right pane at its 260 px minimum**.

| state | elements | drawn | text | controls | captions short | overflow-X | off-window | no tooltip | inert w/o reason |
|---|---|---|---|---|---|---|---|---|---|
| L0 c1 / c2 / c4 | 1318 / 1319 / 1321 | 898 / 899 / 901 | 324 | 251 | **0** | **0** | 0 | 0 | 0 |
| L1 c1 / c2 / c4 | 1335 / 1336 / 1338 | 930 / 931 / 933 | 343 | 228 | **0** | **0** | 0 | 0 | 0 |
| empty state (browser) | 53 | 33 | 11 | 6 | 0 | 0 | 0 | 0 | 0 |
| right pane at its minimum (window 820, split intent 1500) | 1335 | 936 | 343 | 228 | 0 | 0 | 0 | 0 | 0 |

Identical after this task's own edits (`shaper13post-*`, six states, same numbers), and identical to T-0312's "after" table — so T-0312's three fixes hold live. 23–30 controls are greyed in these states and every one of them carries a sentence saying why; the empty state's single greyed control is `Save` → *"No Shaper is open, so there is nothing to save."*

### 1.2 Pyre — every card, at 1 / 2 / 4 columns

**Correcting T-0312's README:** it records that "Pyre has no split at all — its dial pane is a fixed 360 px ScrollView, so Pyre is always one column". Half of that is right (`ZUI.Split.pyre.window.split.v1` really is inert) and half is not: `PyreWindow.leftPaneWidth` is a `[SerializeField]` the user drags, `ClampedLeftPaneWidth()` allows up to `min(1458, window.width − 260)`, and `Z.ColumnFlow(360f)` reads it. Driven through that field, Pyre reaches **2 columns at pane 760 (flow 747)** and **4 at pane 1458 (flow 1445, window 1900)** — measured. T-0312 therefore audited Pyre at one column only; this pass audited every card at all three.

Every concrete `PyreForm` (9 authored + the test form skipped) on a one-layer scratch spec, at 1 / 2 / 4 columns — 27 audited states:

| form | elements (c1) | controls | captions short | overflow-X | no tooltip | inert w/o reason |
|---|---|---|---|---|---|---|
| Torch | 1231 | 162 | 0 | 0 | 0 | 0 |
| ArcBurst | 802 | 114 | 0 | 0 | 0 | 0 |
| ForkBlast | 765 | 106 | 0 | 0 | 0 | 0 |
| Inferno | 836 | 120 | 0 | 0 | 0 | 0 |
| Jet | 1070 | 159 | 0 | 0 | 0 | 0 |
| RadialJet | 1156 | 177 | 0 | 0 | 0 | 0 |
| Orb | 860 | 113 | 0 | 0 | 0 | 0 |
| PlasmaBloom | 1574 | 246 | 0 | 0 | 0 | 0 |
| **ExplosiveJet** | 1715 | 247 | **1** | **4** | 0 | 0 |

Eight of the nine are clean at every column count. The ninth is finding 2.1 below, and it is clean at no column count.

### 1.3 The cold walk, twice, with temporal samples

Close every tool window → open Shaper from its own menu item → sample at t≈0 / 1 s / 4 s → bind the demo document → sample again at t≈0 / 1 s / 4 s. Run twice.

| step | walk 1 | walk 2 |
|---|---|---|
| open, t0 | 53 elements, **0 laid out**, 0 errors | identical |
| open, t1 / t4 | 33 drawn, 6 controls, 0 without a tooltip, 0 errors | identical |
| bind, t0 | 1318 elements, 1 laid out | identical |
| bind, t1 / t4 | 1320 elements, 374 drawn, 86 controls, 0 errors | identical |

Two things this says that a single steady-state audit cannot. **The first frame after the click is empty** (0 laid out at t0, 374 by t1) — normal for UI Toolkit, but it means every probe in this programme must read geometry in a later eval than the one that built it, which the library already does. And **the second walk is byte-identical to the first**: no stale captured state, no drift, no growth in element count, and the window comes back on the browser (its `asset` is a `[SerializeField]`, so a closed window genuinely reopens empty — the empty state is reachable and was audited).

The empty state is a real screen, not a dead end: Save (greyed, with its reason), New, Browse, Refresh, "▶ Animate all", and the IMGUI asset grid. **Probe limitation, stated because it bounds the claim:** that grid is painted by `LauAssetGridGUI` inside an `IMGUIContainer`, so the hierarchy walk cannot see the cards in it — no audit in this programme, this one included, has ever measured them.

### 1.4 T-0308 / T-0309 / T-0311 / T-0312 verified live

- **T-0309** — the toolbar at window 1400 with the demo document bound: field 200.0 px, name needs 96.0, has 161.8, drawn whole, no `(Type)` suffix, rightmost child 606.2 with 793.8 px of slack. Verified.
- **T-0311** — `STAR w=22.2 content=20.4 need=12.9 fits`. Verified.
- **T-0312** — §3.1 (ramp/gradient field shrinks) and §3.3 (BackSplash row folds) verified by the right-pane-at-minimum audit above coming back with 0 overflow; §3.2 (`[Range] Vector2` → `Z.MicroMinMax`) verified by nine `ZuiMicroMinMax` controls at 150 px, two per row, 0 overflow, in every Torch state — **and by finding 2.3, which is the cost of that swap.**
- **T-0308** — the `[Unreleased]` block carries 2 remaining `PyrePlus` mentions and 0 of `Bestiarium` / `CharacterDef` / `ReelHitFilter` / `ClumpStage`. Both survivors are inside CHANGELOG.md:158, the self-annotated *"SUPERSEDED, never shipped — reverted in `450de881` … kept here only so the attempt is not re-made"* entry, which is exactly the exception T-0308's handover reserved. Verified.

### 1.5 Chunks and Launimator

| window | width | elements | drawn | controls | captions short | overflow-X | off-window | no tooltip |
|---|---|---|---|---|---|---|---|---|
| Chunks (ProbeChunk bound, 17 sections, all open) | 1400 | 496 | 399 | 84 | 0 | 0 | 0 | 0 |
| Chunks | 1000 | 496 | 399 | 84 | 0 | 1 | 2 | 0 |
| Chunks | **820 (its own minSize)** | 496 | 399 | 84 | 0 | **1** | **5** | 0 |
| Laumination Builder (Hero/Idle via `OpenForEdit`) | 1400 | 384 | 299 | 71 | 1 | 0 | 0 | 0 |
| Laumination Builder | **900 (its own minSize)** | 384 | 311 | 71 | 1 | 1 | 0 | 0 |
| Lauminary Browser | 1400 / 820 | 69 | 32 | 10 | 0 | 1 → **0 after the fix** | 0 | 0 |
| Sprite Catalog | 1400 / 820 | 41 | 19 | 5 | 0 | 0 | 0 | 0 |

Two notes on scope. The Laumination Builder's **menu item opens an empty shell** — the door an author actually uses is `LauminationBuilderWindow.OpenForEdit(lauminary, animName)` from the Lauminary Browser, so the audit went in the same door, on `Hero`'s draft laumination `Idle`, read out of the draft `LauminaryVersion` asset directly (never `LauminaryRepo.EnsureDraft`, which creates). And the **Lauminary Browser and Sprite Catalog are largely IMGUI** — 32 and 19 drawn elements — so the tree walk measures their ZUI chrome and nothing of their grids.

### 1.6 Tab order through one card

`NavigationMoveEvent(Next)` — the event UI Toolkit turns Tab into — dispatched into the live panel, plus a static read of `focusable` / `canGrabFocus` / `tabIndex` on every leaf control.

**Canvas card (Shaper, 1400, 2 columns):** 5 leaf controls, 5 focusable, **5 of 5 reached by Tab**, in reading order (`96` → `152` → `16` → `424242` → `HDR`), then focus leaves the card. Nothing unreachable.

**Layers card (same state), the richer case:** 20 leaf controls, **18 reached**, and the two misses are not an ordering problem — they are finding 2.3.

### 1.7 The Bake box's Destination at the minimum width

The Bake box lives in Shaper's **right** pane (`ShaperWindow.cs:1400`), whose `minWidth` is 260 (`ShaperWindow.cs:321`), so the width that matters is that pane's minimum, not the dial pane's.

| document | folder shown | pane | needs | has | result |
|---|---|---|---|---|---|
| `ShaperDemoDoc` | `Assets/Demos/ShaperDemo` (23 ch) | 304.9 | 160.0 | 160.0 | fits |
| `ShaperDemoDoc` | same | 1116.9 (window 1700) | 160.0 | 160.0 | fits |
| scratch doc five folders down | `Assets/Shaper/AuditT0313/Characters/Bosses/Final Boss` (53 ch) | 292.0 | 325.8 | 288.9 | **cut by 36.9 px**, `text-overflow: Clip` |

Fixed below (3.4). The path is not shortenable and the pane is at its floor, so the fix is to make the cut visible and the whole path readable, not to invent a width.

### 1.8 Stability

T-0304's layout-struggle sweep, re-run after this task's edits: Shaper, dial pane 360 → 1400 in steps of 260 at window 1700, 2 s idle at each width — **0 errors, 0 warnings at every width.**

---

## 2. The four findings filed as cards

### 2.1 An ExplosiveJet blast's last three dials and its remove button are 300.9 px outside the card

`ZuiReflect.BuildList`'s **compact** branch (`ZuiReflect.cs:706-763`) draws a list element that is nothing but bounded scalars as ONE `NoWrap` row: `#n` + its dials + a flexible gap + `×`. Its own comment says *"the dials shrink instead"*. They do not — every child resolves `flex-shrink: 0` — and even if they did, `ZuiMicroSlider`'s `min-width` is 60.

Measured, Pyre, `ExplosiveJetForm`, dial pane 360 (the width the window opens at), card body **293.8 px**:

```
[0] Label '#1'              w= 26.2  x= 30.7.. 56.9
[1] ZuiMicroSlider 'Blast at'  w=100.0  x= 63.1..163.1
[2] ZuiMicroSlider 'Violence'  w=100.0  x=169.3..269.3
[3] ZuiMicroSlider 'Share'     w=100.0  x=275.6..375.6   << past the box
[4] ZuiMicroSlider 'Seat X'    w=100.0  x=381.8..481.8   << past the box
[5] ZuiMicroSlider 'Seat Y'    w=100.0  x=488.0..588.0   << past the box
[7] Button '×'                w= 21.8  x=603.6..625.3   << past the box, 300.9 px out
```

The dial pane's clipping viewport ends at **351.1**, so at one column `Share`, `Seat X`, `Seat Y` and the `×` are **outside it: not drawn, not clickable**. A blast cannot be removed and three of its five dials cannot be touched at the width Pyre opens at. At 2 and 4 columns they are drawn (the pane is wider) but still overhang their card by 277.3 and 291.1 px; they overlap no other leaf control, so the effect there is a row sprawling across the column gap.

Arithmetic, so the remedy needs no invented number: a 360 px column gives a card body 293.8 px; `26 + N×100 + gaps + 22` fits **N = 2** (258.6) and does not fit N = 3 (364.8). `IsCompactRowElement` admits **N = 2..5**. A reflection scan of every `List<T>` field in every Laubrary runtime type finds **exactly one** type the branch admits — `ExplosiveJetSettings.blasts` → `ExplosiveBlast`, with 5 — so the compact branch as written has never produced a row that fits. Riding along: the `Violence` caption is 1.8 px short (needs 44.9, has 43.1) because `CompactOptions` sets `ControlWidth = 100` where a MicroSlider's caption reserve is sized for 150.

### 2.2 Chunks' section bar runs 206.2 px off the window at the window's own minimum

`ChunkWindow.minSize` is **(820, 520)**. At exactly 820 the toggle bar's section `ZuiSegmented` is **1008.0 px** wide and ends at x=1026.2 against a window that ends at 820:

```
btn 'Sampled'      x=716.0.. 784.0
btn 'Cut Preview'  x=784.0.. 871.1   << off the window
btn 'Animated'     x=871.1.. 944.0   << off the window
btn 'Hits'         x=944.0.. 984.0   << off the window
btn 'Trail'        x=984.0..1026.2   << off the window
```

Four of the seventeen sections cannot be shown or hidden at all until the window is dragged to ≥ 1026 px; at 1000 px `Trail` is still 26.2 px off. The outer `ZuiSectionToggleBar` is `flex-wrap: Wrap`, but the seventeen buttons are ONE `ZuiSegmented` with `NoWrap` and `flex-shrink: 0`, so the wrap can never engage. Shaper's own bar is the same control with nine sections and measures 636.0 px at 820 — it fits, which is why this has never shown up in a Shaper-only sweep. Pyre was not measured for this (its bar was not drawn in the state to hand).

### 2.3 Three ZUI controls cannot take keyboard focus at all

Measured as `focusable == false` on drawn leaf controls (so it is not the disabled-controls case):

| window / state | leaf controls | not focusable | which |
|---|---|---|---|
| Shaper, demo doc, layer 0, window 1400 | 144 | **10** | 8 × `ZuiValue2DControl`, 1 × `ZuiMicroMinMax`, 1 × `ZuiPad` |
| Pyre, Torch, 1 column | 54 | **9** | 9 × `ZuiMicroMinMax` |

The live Tab traversal confirms it: in the Layers card, focus jumps `+ Add layer` → `Height`, straight past `Lifetime`, and never lands on `Direction XY`.

The Pyre nine are the dials **T-0312 created** — `Root spread`, `Root height`, `Tongue climb / peel / width / length / life`, `Ember rise`, `Ember size` were `Z.MinMax` (a `MinMaxSlider` plus two `FloatField`s: focusable, and typeable) and are now `Z.MicroMinMax`, which has no field of any kind. That swap fixed a real 8.4–27.6 px overflow and should not simply be reverted — but it cost typed entry and keyboard access on 13 reflected `[Range] Vector2` fields across `Runtime/**`, and nobody measured that. The scalar sibling shows the shape of the answer: `ZuiMicroSlider` carries a `FloatField _numField` (`ZuiMicroSlider.cs:33`) and is reachable; `ZuiMicroMinMax` carries none.

### 2.4 Twenty-one greyed controls in Chunks and Launimator say what they do, not why they are greyed

Shaper and Pyre set the standard this programme has been holding them to — *"No Shaper is open, so there is nothing to save"*, *"Position and range only apply to a Point light…"*. The other two tools do not:

- **Lauminary Browser, 4 of 4** — `Duplicate` → "Create a copy of the selected lauminary (draft only)."; the name field → "New name for the selected lauminary."; `Rename` → "Rename the selected lauminary."; `Delete…` → "Delete the selected lauminary and every one of its versions (asks first)." They are greyed because nothing is selected. None of them says so.
- **Laumination Builder, 14 of 14** — `Restore`, `Clear`, `Download`, the transparent-colour and tolerance fields, the four marquee fields, `Clear Box`, `Add Region (0)`, `Edit in Aseprite`, `Sync edits`, `Reverse`. Greyed because no sheet is loaded / no marquee is drawn / nothing is selected. None of them says so.
- **Chunks, 3 of 4** — the two `Sprites` pickers and their count. (Chunks' `Save` is the one that does it right: *"This Chunk matches what is on disk — nothing to save."*)

**This also corrects a claim the probe library makes.** `ZAudit`'s inert rule counts a control as explained if it has ANY effective tooltip, so all 21 score as `inertWithReason` and none as `inertNoReason`. Every "0 disabled without a reason" in this programme — T-0312's included — therefore means *"every greyed control has a tooltip"*, not *"every greyed control says why"*. On Shaper and Pyre the two happen to coincide; on these windows they do not.

---

## 3. Fixed here (trivia)

Three files, all editor, **no Pyre runtime or form file, no `CHANGELOG.md`, not committed** (programme rule 3).

1. **`Editor/Launimator/LauminationBuilderWindow.cs`** — the transport's pause glyph was clipped. The 36 px button's default 6+6 padding and 1+1 border left **21.8 px of content for a "❚❚" that measures 24.0**; the caption audit caught it at both 1400 and 900. Horizontal padding zeroed rather than the slot widened — the same call the Library star got in T-0311 and the unsaved-edits dot in T-0307. After: **content 34.2 px for a 24.0 px glyph, measured with the button in its ❚❚ state.**
2. **`Editor/Launimator/LauminaryBrowserWindow.cs`** — the identical 36 px transport button, same two lines. Its preview was not drawn in the state to hand, so this one is fixed by arithmetic (the same widths, the same call) rather than re-measured live.
3. **`Editor/Launimator/LauminaryBrowserWindow.cs`** — the `Rename` row: a 170 px name field + 6.2 px gap + a 64 px button in a 240 px column put the button's right edge **3.6 px past the row's content box at every window width**. The field is now allowed to shrink (the same default T-0312 gave a ramp field) instead of a hand-picked width. After: **overflow-X 0** in the window's audit.
4. **`Editor/Shaper/ShaperWindow.Bake.cs`** — the Bake box's Destination readout (1.7 above). The cut is now signalled (`text-overflow: Ellipsis` + `overflow: Hidden`) instead of the path being sliced mid-word, and the **field's own tooltip now names the actual destination** ("Where a bake lands — currently \"Assets/…/Final Boss\": …"). Ellipsis also buys the hover for free: an elided `TextElement` returns its full text as its tooltip (`displayTooltipWhenElided = True`, confirmed live), so the readout itself now hands over the whole path — verified with a sentinel string that never appeared, which is how the mechanism was identified rather than assumed.

Compile after the last edit: `recompile_status` **completed, `failed=false`, `errors=[]`**. The six-state Shaper sweep re-run after the edits is identical to the sweep before them.

---

## 4. State left behind

`Assets/Shaper` and `Assets/Pyre` are **byte-identical to this task's session-start baseline** — 36 files, every SHA-256 prefix in `out/z-clean.txt` matching `out/p0-base.txt`, nothing added, removed or changed. The scratch assets created here (`Assets/Pyre/AuditT0313.asset`, the `Assets/Shaper/AuditT0313/Characters/Bosses/Final Boss` tree with its document) and the one T-0312's own sweep re-created (`Assets/Pyre/AuditT0312.asset`) were deleted with their `.meta`s through `AssetDatabase.DeleteAsset`. Every `T313.*` and `T0312.*` pref deleted; both `ZUI.Split.*` prefs deleted (neither had a value at session start); `ZuiSectionToggleBar.ShaperWindow.userSel` restored verbatim to `Views=1;Canvas=0;Layers=0;Shape=0;Fill=0;Swarm=0;SpriteFX=0;Lights=1;Tags=0` with `barMode` true; `Shaper.lastView` deleted; `Undo.ClearAll()`; console cleared; all six tool windows closed (none was open at session start — the baseline's window list is the seven stock editor windows). The editor reports the same **10 pre-existing dirty shaders and nothing else**, and `scriptCompilationFailed = False`. The demo document was `dirty=False` before binding and after every rebuild; nothing was ever saved to it, to `ProbeChunk`, to `Hero`, or to any other authored asset. Play mode was never entered and the Test Runner was never run.

## 5. Verified how

**By probe, in the live editor:** every table and every measurement above — the 6 Shaper states before and after the edits, the empty state, the right pane at its minimum, the 27 Pyre form × column states, the two cold walks with their temporal samples, the four Chunks/Launimator windows at two widths each, the Tab traversals, the Destination readout at three widths and two path lengths, the four fixes re-measured (three of them), the compile, the idle sweep, and the byte-identical asset baseline.

**By eye:** nothing. No human and no screenshot has looked at any of these windows this pass. T-0307's archive note still holds — `PrintWindow` returns a blank client area for these UI-Toolkit windows and a `CopyFromScreen` catches whatever the owner has on top — so editor-window screenshots remain a non-working verification channel here, and forcing a window to the foreground would have stolen focus on the owner's live desktop.

**Not verified:** (a) nobody has dragged Pyre's divider by hand to reach 2 or 4 columns — the pane width was set through `leftPaneWidth`, which is what the drag handler writes, but the drag itself was not exercised; (b) the Lauminary Browser's pause button is fixed by arithmetic, not re-measured (its preview was not drawn); (c) the IMGUI halves of the Lauminary Browser, the Sprite Catalog and every asset-browser grid are invisible to the hierarchy walk and have never been audited by this programme; (d) Chunks' 17 sections were audited with every section open, but no section's own *contents* were driven (no chunk was cut, no layer added); (e) no mouse has clicked, dragged or typed into anything this pass — every interaction was an event dispatched into the panel.
