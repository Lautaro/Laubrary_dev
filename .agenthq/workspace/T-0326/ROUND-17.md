# T-0326 — the seventeenth full pass (2026-09-09)

Worktree `D:\UNITY\Laubrary Dev - Shaper`, branch `feat/shaper`, HEAD `50d724d9` at session start (rounds 11–16 committed), editor on port 7801, PID 8124. `Application.dataPath` confirmed `D:/UNITY/Laubrary Dev - Shaper/Assets`; `isPlaying=False`, `scriptCompilationFailed=False`, open scene `Assets/Demos/ShaperDemo/ShaperDemo.unity`, `dirty=False`. Probes in `workspace/T-0326/probes/` (copies of round 16's with every hard-coded output path re-pointed at `T-0326` before the first run), dumps in `out/`, captures in `shots/`.

**This file is written incrementally — each section is appended as it lands.**

---

## 0. Session log (append-only)

- Read `ShaperHarmony/RULES.md`, `T-0158/PROGRAMME_RULES.md`, `ui-layout-rules.md`, `UNITY_DEV_GUIDE.md`, `T-0324/ROUND-15.md`, `T-0325/ROUND-16.md`.
- Probe library copied and re-pointed (`T-0325` → `T-0326` in every path, `T325.`/`T324.` prefs → `T326.`, `AuditT325`/`AuditT324` → `AuditT326`, `fill-sweep-t325` → `fill-sweep-t326`, the `Cap325`/`T325` P/Invoke class names).
- Editor state at session start recorded above.

---

## 1. The never-audited window set — computed, not inherited

Round 16 §8.6 listed "eleven" windows never audited. That list was written before this programme's audited set was itemised, and it both over- and under-counts. Enumerated in the live editor (`EditorWindow` subclasses across every `Laubrary*`/`ZUI*` assembly, with the `[MenuItem]` that opens each), then subtracting the fourteen the card names as audited (Shaper, Pyre, Chunks, Laumination Builder, Lauminary Browser, Sprite Catalog, Zoe, Mirage, Larder, Lathe, SpriteFx, TextSplash, Cartographer, Aseprite), the real never-audited set is **seventeen windows reachable from a menu**, plus two sub-windows Cartographer opens itself:

| window | menu | base | drawn UITK elements |
|---|---|---|---|
| BackSplash | `Laubrary/BackSplash` | `ZuiAssetWindow` | 32 |
| Choreographer | `Laubrary/Choreographer` | `ZuiAssetWindow` | 36 |
| Tapestry | `Laubrary/Tapestry` | `ZuiAssetWindow` | 22 |
| Lathe Mold | `Laubrary/Lathe Mold` | `ZuiAssetWindow` | 22 |
| Weapons | `Laubrary/Zoetrope/Weapons` | `ZuiAssetWindow` | 38 |
| Ammo | `Laubrary/Zoetrope/Ammo` | `ZuiAssetWindow` | 27 |
| Palette Health | `Laubrary/Zoetrope/Palette Health` | `ZuiWindow` | 20 |
| Rules Editor | `Laubrary/Rules Editor` | `ZuiWindow` | 9 |
| ZUI Control Gallery | `Laubrary/ZUI/Control Gallery` | `ZuiWindow` | 267 |
| Brain Graph | `Laubrary/Brain Graph` | raw `EditorWindow` + GraphView | 16 |
| Story Graph | `Laubrary/Story Graph` | raw `EditorWindow` + GraphView | 16 |
| **Lazor** | `Laubrary/Lazor/Lazor` | `LaubraryAssetWindow` → **legacy `ZUIWindow`** | **0** |
| Zoe Preview | `Laubrary/Zoetrope/Zoe Preview` | legacy `ZUIWindow` | **0** |
| Zounds | `Laubrary/Zounds` | legacy `ZUIWindow` | **0** |
| ZUI Playground | `Laubrary/ZUI/Playground` | legacy `ZUIWindow` | **0** |
| Dashboard | `Laubrary/Dashboard` | raw `EditorWindow` | **0** |
| Notifyer Log | `Laubrary/Notifyer Log` | raw `EditorWindow` | **0** |
| Prop / Tileset Builder | (opened by Cartographer) | `ZuiAssetWindow` | not opened this pass |

Plus three legacy ZUI dev windows on `Tools/ZUI/` (Zhowcase, Zeditor, Texture Editor), which are ZUI's own workshop rather than Laubrary tools.

**Seven of those seventeen draw ZERO UI Toolkit elements** — they are immediate-mode `OnGUI` windows (four on the legacy `ZUIWindow` base, three raw). That is the single most consequential structural fact this pass turned up and it is in §3.3.

---

## 2. The findings

### 2.1 Round 16's picker fix landed on one of the two grids — the one users see least. Fixed.

Round 16 finding 3.1 established that a tail ellipsis collapses this project's asset names onto each other, because names are family-first with the variant at the END, and moved `LauAssetGridGUI.Elide` to a middle ellipsis. That is the **immediate-mode** grid — the one behind Mirage's `Add Previewable` popup.

Every ZUI asset tool also carries a **second, retained-mode grid**: the library browser inside the window itself (`ZuiAssetWindow.BuildCell`), which is what an author actually browses in Pyre, Zoe, Chunks, SpriteFx, Lathe, TextSplash, BackSplash, Choreographer, Tapestry, Lathe Mold, Weapons, Ammo and Cartographer. It elides through the stylesheet (`.zui-cell__name { text-overflow: ellipsis }`), and UITK's ellipsis goes at the **tail**. Measured in Pyre's own library at HEAD:

| Pyre library (32 cells) | before | after |
|---|---|---|
| names too wide for the 96.9 px cell | 7 | 0 (all elided, none hard-cut) |
| cells whose visible text duplicates another cell | 3 | **1** |
| of those, **caused by the truncation** | **2** | **0** |

`Directional Grenade Blast 1 Plus`, `Directional Grenade Blast 2 Plus` and `Directional Grenade Side Blast Plus` all read **`Directional Grenad…`** in Pyre's library — the identical defect round 16 fixed one control away. They now read `Directiona…st 1 Plus` / `Directiona…st 2 Plus` / `Directiona…last Plus`. The one remaining duplicate is two assets that are both genuinely named `New Pyre Plus`, which no truncation can fix.

The fix needed a second, smaller correction that is worth recording because it is a general UITK trap: **`.zui-cell` centres its children, so a name label with a `max-width` sizes to its OWN text, not to the cell.** Eliding against that box makes every name "too wide for itself" — the first attempt shortened `Curl Plus` to `Curl…lus`. The label now takes a FIXED width and centres the text internally, which gives a stable budget that shortening cannot feed back into. Measured after: short names untouched, `tooWide=0` on all five libraries swept.

`Editor/AssetKit/ZuiAssetWindow.cs` — `BuildCell` + new `ElideMiddleWhenLaidOut`/`ElideMiddle`/`Measure`. Compiled clean.

### 2.2 Both graph windows drew a native slider, three untooltipped controls and a permanent instruction line. Fixed.

`Laubrary/Brain Graph` and `Laubrary/Story Graph` are the same window (`GraphWindowBase`, `Editor/Loom/GraphEditor.cs`); neither had ever been audited. `ZuiAudit` on both: **`tooltip-missing=3`** — every control in the toolbar. What they were:

- **`Save`** and **`Frame All`**: raw `new Button(...)`, no tooltip at all.
- **`Fade`**: a raw UITK `Slider` with a separate `Label` printing `1.5s` beside it — the exact shape the layout rules name as the pre-MicroSlider look ("never a plain `Z.Slider` + an external value field"), also with no tooltip.
- A permanent `Label` reading `"  Add nodes: right-click the canvas.  Edit field values in-node or the Inspector."` stretched across the toolbar — an on-screen instruction paragraph, which the rules put in a tooltip on the thing it describes.

Now: `Z.Button` with tooltips, one `Z.MicroSlider("Fade", …, 0.1f, 5f, …, decimals: 1)` carrying label and value inside its own track, and the instruction moved onto the canvas as `_view.tooltip`. The header label gained a tooltip saying how a graph gets loaded.

Two things about the ZUI attachment are deliberate and worth keeping: this window is **not** a `ZuiWindow`, so `Z.Attach` had to be called by hand or every `Z.*` control would render unstyled (a MicroSlider collapses to zero height — the guide's first rule); and it is attached to the **toolbar**, not to `rootVisualElement`, because the sheet's `.zui-root` descendant rules would otherwise reach into the GraphView canvas and restyle every node's fields, which is not this pass's to change.

`Editor/Loom/GraphEditor.cs` — `OnEnable`; `Editor/Loom/Loom.Editor.asmdef` gains `com.Lautaro-Arino.Laubrary.Zui.Editor`. Compiled clean.

### 2.3 Both graph windows put their CLASS NAME on the tab. Fixed.

`GraphWindowBase` set `titleContent` inside `OpenAsset`. Opening `Laubrary/Brain Graph` or `Laubrary/Story Graph` from the menu with nothing selected goes `GetWindow<T>()` → `Show()` and never touches `OpenAsset`, so the tab read **`BrainGraphWindow`** / **`StoryGraphWindow`** — measured live on both. The tab is how a user tells one floating window from another; every other Laubrary window names itself properly. The title is now set in `OnEnable`, which both routes go through.

`Editor/Loom/GraphEditor.cs` — `OnEnable` / `OpenAsset`.

### 2.4 Choreographer was the one window where every dial is a native slider. Fixed.

`Z.Slider` builds a UITK `Slider` with an inline numeric input — the pre-MicroSlider look the layout rules name explicitly ("never a plain `Z.Slider` + an external value field"). Counting the whole package: **27 `Z.Slider`/`Z.SliderInt` call sites against 179 `Z.MicroSlider` ones, and 15 of the 27 are in `ChoreographerWindow` alone** — Pyre has 2, Lathe 1, BackSplash 1, Tapestry 1, Laumination Builder 4. Every other tool's bounded scalar is an embedded MicroSlider; Choreographer's every bounded scalar was a native track with a separate number beside it, each wrapped in a `Z.Field` that repeated the tooltip.

That is not only a look. `ZuiAudit` on a fresh Choreography found a **`clipped-input`**: the preview scrubber printed `0.6388855` into a 43.6 px box needing 62.2 px, and the slider spilled **23.1 px horizontally and 4.0 px vertically out of its own box** — a number cut mid-digit on every frame of playback. Root cause, measured: `Z.Slider` rounds to 5 decimals in its change callback, and the ticker feeds the phase through `SetValueWithoutNotify`, which skips it.

Fourteen dials — Length, Bend, Angle/Angle offset, Default count, Duration, Stagger, Launch blend, Release, Target blend, Preview count, Onion frames, Trail length, Sprite size, Speed — are now `Z.MicroSlider`, with the three integer ones on `decimals: 0` + `Mathf.RoundToInt`, which is Pyre's own pattern for an integer dial. The transport scrubber stays a `Z.Slider`: it is the same deliberate exception as Pyre's transport `SliderInt` (open as T-0260 Q12), and its readout is now rounded to 3 decimals at source — finer than one pixel of its own 150 px track.

`Editor/Choreographer/ChoreographerWindow.cs` — `BuildDials`, `BuildPreviewSection`, `BuildTransport`, `Phase()`.

### 2.5 Tapestry's ▶ Play filled the pane; Lathe Mold's name field stretched without saying so. Both fixed.

- **Tapestry**: `▶ Play` was a `Z.Button` with no width inside a column, so it resolved **725 px wide** (ZuiAudit `over-width`, cap 600) — the identical defect round 15 fixed for ZoeWindow's three pane-filling buttons. Now 88 px. `Editor/Tapestry/TapestryWindow.cs` — `RebuildTransport`.
- **Lathe Mold**: the node **name** field carries `flex-grow: 1` on purpose (the rulebook's own exception: a name field may take a row's slack rather than truncate). It was flagged as an unexplained `stretch` because nothing declared that. It now carries `zui-audit-allow-stretch`, the sanctioned opt-out, so the exception is stated rather than re-litigated every pass. `Editor/Lathe/LatheMoldWindow.cs` — the node row.

### 2.6 What the six cold walks cleared

`Laubrary/BackSplash`, `Laubrary/Choreographer`, `Laubrary/Tapestry`, `Laubrary/Lathe Mold`, `Laubrary/Zoetrope/Weapons`, `Laubrary/Zoetrope/Ammo`, each opened from its own menu item, walked New → type → **Create** through the window's own buttons, twice, on a scratch asset:

| checked | result |
|---|---|
| empty state | all six show `[None (X)] [Save greyed] [New] [Browse]` + their library with a count, `Save` greyed **with a reason**, Duplicate/Rename/Delete correctly absent |
| the New row's destination sentence | all six name a real per-tool folder — `Assets/BackSplash`, `Assets/Choreographer`, `Assets/Tapestry`, `Assets/Lathe/Molds`, `Assets/Zoetrope` ×2. Round 16's finding 3.7 (a new asset landing in the project root) does not recur anywhere: **every one of the eighteen asset windows now declares its own folder** |
| the fresh asset's screen, audited | after the fixes above: `captionShort=0 overflowX=0 offWindow=0 noTooltip=0 inertNoReason=0`, `ZuiAudit=0` on all six |
| **conditional UI** | the audit is bounded by what DRAWS (round 16's not-verified #7), so every `display:None` subtree was forced visible and the audit re-run: 10/26/12/4/4/5 hidden subtrees revealed, **still zero findings on every counter, `foldedSkipped=0`** on five of six |

### 2.7 Choreographer taught the user its canvas gestures in four lines of body text. Fixed, and swept.

`Drag a handle to move it · click the curve to add a point · right-click a handle to remove. Marquee-drag empty space to select many · shift-click to add · drag any selected handle to move them together.` — a permanent four-line paragraph in the left column, found by looking at the window rather than by any audit. It describes the **stage's** gestures, so it is now the stage's tooltip.

Swept mechanically afterwards over every drawn text element in every open Laubrary window (eighteen, each bound to a real asset): **two** long strings remain package-wide, and both are legitimate — Palette Health's `"Nothing observed yet…"` readout, and the Rules Editor's `Z.Help` empty-state box (§4.2). No other tool puts instructions on screen.

`Editor/Choreographer/ChoreographerWindow.cs` — `BuildStage`/`BuildControls`.

### 2.8 Twenty-three windows had no minimum size at all, and one of them collapses at its own minimum. Fixed.

The card asked for a walk "at each window's declared minSize". Reading the declarations first: **only Shaper and Pyre declare one** (820 × 520, set in their static `Open()`, with Pyre's comment explaining that below it the split's two panes cannot both fit). Every other Laubrary window — Zoe, Mirage, Chunks, Lathe, Larder, SpriteFx, TextSplash, Cartographer, Prop, Tileset Builder, BackSplash, Choreographer, Tapestry, Lathe Mold, Weapons, Ammo, and the non-asset windows — sits at Unity's **50 × 50** default. Only Zounds declares its own (414 × 151).

Walked at the declared minimum, the difference is not theoretical:

| window | at its declared minimum | result |
|---|---|---|
| Shaper | 820 × 520 | `captionShort=0 overflowX=0 offWindow=0 noTooltip=0`, ZuiAudit 0 |
| Pyre | 820 × 520 | same, 0 on every counter |
| **Zoe** | 50 × 50 (Unity clamps the tab to 124 px) | **31 clipped captions, 102 controls spilling out of their parent, 20 elements off the window, 18 `off-screen` findings** |

So the floor was measured rather than guessed — nine asset tools swept at 400 / 520 / 620 / 720 / 820 px:

| width | tools clean on every counter |
|---|---|
| 400 | 3 of 9 (Tapestry 21 elements off-window, TextSplash 18, Zoe 3) |
| 520 | 3 of 9 |
| 620 | 6 of 9 |
| 720 | 8 of 9 (TextSplash still 1 off-window) |
| **820** | **9 of 9** |

**820 is the first width at which every asset tool is clean** — which is exactly the number Shaper and Pyre already carry. `ZuiAssetWindow` now declares `MinWindowSize` (virtual, so a tool whose content genuinely needs less can lower it) and applies it in `OnEnable`, which every open route goes through including a domain reload. Verified after the change: all fifteen asset tools report `min=820x520`; the small panels that are legitimately narrow (Palette Health at 321 px, Rules Editor, Dashboard, Notifyer Log, the graph windows) are untouched.

`Editor/AssetKit/ZuiAssetWindow.cs` — `MinWindowSize` + `OnEnable`.


---

## 3. Round 16's landings, by eye

The by-eye channel had to be rebuilt **again**, and this time round 16's own probe was found to be lying rather than failing. `capwin.ps1` raises a window and reads the DESKTOP over its rect. Asked for `Choreographer` it returned a picture of **`Zoes`** — because a background editor gives its floating tool windows an **empty OS title**, the title lookup missed, and the probe's `FindOther` fallback ("the one visible window that is not the main window") silently grabbed a different window at a different rect. A capture that returns the wrong window without saying so is worse than one that fails.

The replacement is `probes/capprint.ps1` + `probes/cap.sh`, and it is what the next pass should use:

- **`PrintWindow(hwnd, hdc, 2)`** — the window renders ITSELF, so z-order is irrelevant. Nothing has to be closed, moved, minimised or raised, which matters on a desktop shared with another agent's editor and the user's own terminal (`PW_RENDERFULLCONTENT`, flag 2, is required — flag 0 returns black on Unity's GPU windows).
- **The window is found by RECT, not by title**, because the titles are empty. This editor's mapping, fitted from three windows that did have titles and verified against five more: `physical = 2.25 x logical - (5, 45)` (`pixelsPerPoint` 2.25). `cap.sh <WindowTypeName> <out.png>` asks the editor for the window's logical position and does the arithmetic.

| round 16's landing | what is on screen |
|---|---|
| **Muzzle Event Name is a picker** (16 §3.3) | ProtoGuy's weapon card reads `ProtoGuy Gun ×` / `Attach To Part [Upper]` / `Muzzle Layer Id [Muzzle (Vector)]` / `Muzzle Event Name [None declared]` **greyed** — three dropdowns in a column where one used to be a type-in box (`shots/zoe-weapon-crop.png`) |
| **a StringDropdown shows an unresolved stored value** (16 §3.5) | ProtoGuy's Hit reaction card reads `At [Meta Point]` and `Layer [Muzzle (unresolved)]` — the authored value on screen with its marker, not replaced by the first option (`shots/zoe-unres-crop.png`) |
| **the point-layer picker lists Waist** (16 §3.2) | the same card's layer picker offers `(none)`, `Muzzle (Vector)`, `Waist (Point)` — measured through the window's own control, 3 choices |
| **a library cell elides in the middle** (16 §3.1, and §2.1 above) | the Zoe library draws `Hit Event…tte Demo`, not `Hit Event Palette D…` (`shots/choreo.png`, the accidental Zoes capture that exposed the probe bug above) |
| **the Choreographer/MicroSlider work** (§2.4) | every dial renders as an embedded MicroSlider with its label and value inside the track (`shots/choreo3.png`) |

**Composite derivation, re-measured at HEAD** through the window's own helpers on ProtoGuy (`CompositeLauminaryView`): 5 clips; `LegsWalk_N/E/S` **8 frames** each, `LegsIdleRotation` and `UpperAimRotation` **16**; `UpperAimRotation` yields point layer **`Waist`**; `AllMetaLayers` = `(Muzzle, Vector), (Waist, Point)`. Round 16's 0 to 8 and 0 to 1 hold.

**Not seen: the `On Frame` frame picker itself.** Reaching it means switching a reaction's trigger to On Frame on **ProtoGuy**, a shipped demo asset, which this card forbids editing. The numbers it would be bounded by (8 and 16) are measured above through the same helper the picker calls.

**Not seen, third pass running: the `Add Previewable` popup — and now with a measured cause.** Round 16 tried four times and reported it as a race. It is not a race. Pressing the button with a synthesized pointer does not raise it at all (no `PopupWindow` appears in `Resources.FindObjectsOfTypeAll`), and calling `LauAssetBrowser.Show` directly DOES create one — `UnityEditor.PopupWindow` at 456 x 510 — but it is **gone by the next probe round trip**, and it never becomes an enumerable OS window in between. A `PopupWindow` lives only while the editor holds focus, and every probe round trip surrenders it. Nothing short of a human moving the mouse will photograph this control; that should be written down rather than re-attempted a fourth time.

## 4. What else the seventeen windows showed

### 4.1 Seven of them are not on the mandated toolkit at all

`Laubrary/Lazor/Lazor`, `Laubrary/Zoetrope/Zoe Preview`, `Laubrary/Zounds`, `Laubrary/ZUI/Playground`, `Laubrary/Dashboard` and `Laubrary/Notifyer Log` draw **zero** UI Toolkit elements — their `rootVisualElement` holds one element, the root. Four sit on the legacy `ZUIWindow` (immediate-mode `OnGUI`) base and two are raw `EditorWindow`s; the three `Tools/ZUI/*` windows are the same. Consequences, stated plainly because they bound everything else in this report:

- **`ZuiAudit` cannot see them.** Every "clean" this programme has ever reported excludes them by construction, not by measurement.
- The project rule is "ZUI is the mandatory toolkit for all UI... Legacy 'no ZUI' comments in older tools are exactly that — legacy — and should be migrated **when you touch them**." Nobody has touched them.
- **Lazor is the sharpest case**: it is a full authoring tool with a library, a canvas and layers, sitting on `LaubraryAssetWindow` → `ZUIWindow`, so it gets none of the AssetKit/ZuiAssetWindow work of the last seventeen rounds — not the middle-elided cells, not the honest empty states, not the 820 px floor, not any audit.

Migrating them is a real project, not a harmony fix, and programme rule 6 forbids inventing it here. It is the largest single item left and belongs on the owner's desk.

### 4.2 The Rules Editor's empty state is a dead end

`Laubrary/Rules Editor` opens to three toolbar buttons and one `Z.Help` box: *"No rules found. In Play mode this shows the live RulesHost; in edit mode it shows the authored RulesHost's RuleSet asset. Open a scene with a RulesHost (and define some GameRule subclasses) to edit rules."* There is no New, no Create, nothing to press — the window's answer to its own empty state is to send the user to write C# and edit a scene elsewhere. By the handover-walk rule that is a missing affordance ("a blank list with no create affordance is a dead end, not a neutral state"). Building one is a **new control**, which programme rule 6 forbids, so it is named and handed over rather than built.

### 4.3 What the other never-audited windows cleared

`Laubrary/Zoetrope/Weapons` and `Laubrary/Zoetrope/Ammo` audit clean empty, clean on a fresh asset, and clean with every conditional subtree forced visible — `foldedSkipped=0`, zero findings on every counter. `Laubrary/Zoetrope/Palette Health` (20 drawn elements, 2 controls) and `Laubrary/ZUI/Control Gallery` (267 drawn, 61 controls) likewise report zero. The Gallery's `foldedSkipped=17` is its own collapsed demo sections, not hidden defects.

## 5. Temporal, Play mode and regression

### 5.1 The Shaper demo document plays, and the window stays clean while it does

`Assets/Demos/ShaperDemo/ShaperDemoDoc.asset`, played through the transport's own Play button and sampled at least 1.5 s apart: **frame 9 → 13 → 11** of 16, three distinct frames, `playing=True` and the button reading `Pause` at every sample. `ZuiAudit` at each sample **while playing**: `captionShort=0 overflowX=0 offWindow=0 noTooltip=0 inertNoReason=0`. Paused: `currentFrame=7`, readout `frame 8/16`, button back to `Play`, same zeros. Window shape `elements=1320 drawn=374 controls=87` at 1100 px — matching rounds 11 and 16, so nothing this round changed Shaper.

### 5.2 A Chunks burst, in Play mode, seen flying

Round 16's not-verified #3 closed. Entered Play **once** (`EditorApplication.EnterPlaymode`, polled to `isPlaying=True`), built the burst through Chunks' own preview component — `Laubrary.Mirage.MirageChunkBurst` with the shipped `Floating Disc Blowup` spec, the same component Mirage's rig realises for a ChunkSpec previewable — and sampled from inside `EditorApplication.update`, which ticks between MCP calls where a probe round trip (10 s apart in practice) cannot:

| t (s) | chunks alive | mean distance from the burst centre |
|---|---|---|
| 0.05 | 6 | 0.249 |
| 0.74 | 6 | 1.138 |
| 1.53 | 6 | 1.922 |
| 2.31 | 1 (the container) | returned to the pool |

**By eye**, three renders of the demo scene's own Main Camera taken inside the same tick loop (`shots/burst-t0/1/2.png`, `shots/burst-strip.png`): a dense orange flash with six debris pieces clustered at the origin (t=0.25, 6 alive); the fireball broken into separate lobes with two pieces visibly fallen below it (t=1.02, 6 alive); the last star-shaped flash alone (t=1.90, 3 alive). It flies.

Play mode was **exited** and confirmed: `isPlaying=False`, `isCompiling=False`, active scene `Assets/Demos/ShaperDemo/ShaperDemo.unity`, `dirty=False`, the burst object gone with the play session. Nothing was recompiled while playing; the Test Runner was never run.

### 5.3 The fill sweep is identical, row for row

Round 15's probe re-run against HEAD (`out/fill-sweep-t326.tsv`, **414 rows**), diffed on `(doc, kind, card, control, state)`:

| baseline | rows | regressions (was > 0, now 0) | rows missing |
|---|---|---|---|
| `T-0325/out/fill-sweep-t325.tsv` | 414 | **0** | 0 |
| `T-0324/out/fill-sweep-t324.tsv` | 414 | **0** | 0 |
| `T-0323/out/fill-sweep-t323.tsv` | 414 | **0** | 0 |
| `T-0277/fill-sweep-after.tsv` | 408 | **0** | 0 |

Round 14's trap avoided again: `probes/p1-rfield.cs` creates the 16x16 `RFloat` the sweep loads but never makes, without which `Pyramid / Solid / Fill / heightFieldScale` reports a false regression.

---

## 6. Files touched

| file | what |
|---|---|
| `Editor/AssetKit/ZuiAssetWindow.cs` | a library cell name elides from the MIDDLE, against a fixed budget rather than its own box (Pyre's library: names too wide 7 → 0, truncation-caused duplicate rows 2 → 0); and every asset tool now declares `MinWindowSize` = 820 × 520, the measured width at which all nine swept tools are clean |
| `Editor/Loom/GraphEditor.cs` | Brain/Story Graph: `Z.Button` + tooltips on Save and Frame All, one `Z.MicroSlider` in place of a native `Slider` + external value label for Fade, the toolbar's instruction paragraph moved onto the canvas as a tooltip, `Z.Attach` on the toolbar (not the root), and the tab named in `OnEnable` so it stops reading the class name |
| `Editor/Loom/Loom.Editor.asmdef` | references `com.Lautaro-Arino.Laubrary.Zui.Editor`, so the graph windows can use ZUI at all |
| `Editor/Choreographer/ChoreographerWindow.cs` | fourteen native `Z.Slider` dials become `Z.MicroSlider` (the only window in the package where every dial was a native track + separate number); the transport's phase readout rounds at source, so it stops printing a 7-decimal value into a 44 px box; the four-line canvas-gesture paragraph becomes the stage's tooltip |
| `Editor/Tapestry/TapestryWindow.cs` | `▶ Play` sized to 88 px instead of resolving 725 px wide and filling the pane |
| `Editor/Lathe/LatheMoldWindow.cs` | the node name field declares `zui-audit-allow-stretch`, the sanctioned opt-out for the rulebook's own name-field exception |

**No Pyre runtime or form file was touched, no `CHANGELOG.md`, nothing committed** (ShaperHarmony rule 3). Six compiles this session, all `completed, failed=false, errors=[]`; the last is the state the tree is in. `scriptCompilationFailed=False`, `isPlaying=False`.

## 7. State left behind

`git status` shows exactly the six source files above, plus what was already modified before this session (`.mcp.json`, `Assets/Pyre/Green Lantern.asset`, the TextSplash demo border font, the untracked `Assets/Shaper/`, `Assets/_Recovery/` and the `Samples~` block). Nothing else, and no `Assets/Mirage/Test 2.asset` this time — rounds 15 and 16 both dirtied it by probing; this pass did not.

Everything created was deleted through `AssetDatabase.DeleteAsset` (which removes the `.meta`): `AuditT326Back`, `AuditT326Choreo`, `AuditT326Tapestry`, `AuditT326Mold`, `AuditT326Weapon`, `AuditT326Ammo`, `AuditT326Splash` **and its generated `Border Fonts/AuditT326Splash (LiberationSans SDF) Border Font.asset`**, `Assets/Mirage/AuditT326View.asset`, `Assets/Shaper/Audit0277/rfield0277.asset`, and the folders `Assets/TextSplash/Border Fonts`, `Assets/TextSplash`, `Assets/Shaper/Audit0277`, `Assets/Choreographer` and `Assets/Screenshots`. A project-wide scan at cleanup found **0 dirty ScriptableObjects** and `AuditT326` matches **0**.

**One generated sibling this round adds to the family** (after round 14's `.states.cs` and round 16's `Border Fonts/`): `mcp__unity-mcp__capture_game_view`'s `save_path` is resolved **relative to `Assets/`**, not to the project root, so asking for `Screenshots/burst-1.png` silently created `Assets/Screenshots/` with two imported PNGs and their `.meta` files. Both, and the folder, are gone.

The demo document was never saved; the demo scene is the open scene, `dirty=False`, and was never saved. Play mode was entered once and exited, confirmed `isPlaying=False`. No tag was created; the Test Runner was never run; the editor was never relaunched.

Prefs: `T0312.out` and `T320.capOut` restored to their T-0324 values (`T0312.out` **last**, after the final dumping probe, per round 14's lesson); `T320.capWin`, `ZuiSectionToggleBar.ShaperWindow.userSel` and `ZuiSectionToggleBar.ZoeWindow.userSel` were measured unchanged and left alone; every `T326.*` key deleted. `Undo.ClearAll()`, console cleared.

The open window set is exactly the eight this session found (Pyre, Lathe, Larder, SpriteFx, TextSplash, Zoe, Mirage, Shaper) — the seventeen opened for the audit were closed. **Nothing is left pinned over the user's desktop**: a final sweep confirms no `WS_EX_TOPMOST` window belongs to this editor's process, and — unlike rounds 11 and 14 — no other application was minimised, moved or closed at any point, because `PrintWindow` never needed the foreground.

## 8. Verified how

**By probe, in the live editor:** the seventeen-window enumeration with each window's base class and menu item; the empty-state and fresh-asset audits of all six never-audited asset tools, twice, plus the same audits with every `display:None` subtree forced visible; the Pyre-library elide measurement before (7 too wide, 2 truncation-caused duplicates) and after (0 and 0) across five libraries; the graph windows' three missing tooltips before and `noTooltip=0` after; the package-wide long-prose sweep (2 remaining, both legitimate); the six New → Create walks and their destination folders; the `minSize` census (2 declared of 25) and the nine-tool width sweep at 400/520/620/720/820; Zoe at its own declared minimum (31 clipped captions, 102 parent overflows, 20 off-window); the composite derivation on ProtoGuy; the Chunks burst's chunk count and spread over 2.3 s of Play; three play samples and the paused audit of the Shaper demo document; the 414-row fill sweep against four baselines; six compiles; the asset/pref/window/topmost cleanup and the project-wide dirty scan.

**By eye, in real captures of the running editor:** Choreographer's dials as embedded MicroSliders on a real fresh asset, with its stage, path handles and transport; ProtoGuy's weapon card with three pickers where one was a text field; ProtoGuy's Hit reaction reading `Muzzle (unresolved)`; the Zoe library's middle-elided `Hit Event…tte Demo`; and the Chunks burst at three moments of one `Fire()` — flash, break-up with debris falling, last star — rendered from the demo scene's own camera inside the play loop.

**Not verified:**

1. **No *human* has operated any of this.** Every press was a synthesized pointer or key event.
2. **The `Add Previewable` popup is still unphotographed**, now with a measured cause rather than a suspicion (§3): a `PopupWindow` exists only while the editor holds focus, and every probe round trip surrenders it. It will take a person, not a better probe.
3. **The `On Frame` frame picker was not seen**, because reaching it means editing a shipped demo asset (§3). Its bounds were measured through the helper it calls.
4. **The seven immediate-mode windows were not audited, only opened and read** (§4.1). `ZuiAudit` structurally cannot see them; no geometry check, no control-choice check and no cold walk was performed on Lazor, Zoe Preview, Zounds, ZUI Playground, Dashboard, Notifyer Log or the three `Tools/ZUI/*` windows.
5. **Cartographer's two sub-windows** (Prop, Tileset Builder) were enumerated but not opened — they have no menu item of their own and are reached through Cartographer.
6. **The 820 px floor is measured on nine tools, not all fifteen.** Cartographer, Prop, Tileset Builder, Chunks, Sprite Catalog, Weapons and Ammo inherit it without having been swept at 400–720.
7. **`ExecuteAlways` behaviour is still measured with the editor in the background**, where it does not tick. This round worked around it by driving `EditorApplication.update` by hand (which is how the Play-mode burst was sampled at all), so "how long a thing takes to appear for a real user" is still not measured.
8. **The Rules Editor's empty-state dead end and the seven un-migrated windows are named, not fixed** — both would need new controls or a migration project, which programme rule 6 forbids inventing here.

### Trivia (named, not fixed)

- Unity's own `+` glyph on the `ListView` footer inside the sanctioned reorderable-list island measures 2.7 px short (`need 12.4 / have 9.8`) — the same one glyph rounds 14–16 reported.
- Shaper's two frame readouts can disagree by one frame **mid-playback** (`frame 14/16` beside `Playing — frame 13/16`): the status line is written when the frame advances, the scrubber label on the next repaint. Paused they always agree (`currentFrame=7` / `frame 8/16`). A one-frame lag at ~6 fps, not a wrong number.
- `Laubrary/Lazor/Lazor` puts a tool's own main window inside a submenu named after it, which the project's menu rule reserves for secondary actions (`Laubrary/Zoetrope/` holds five items including four windows, against the "~3, secondary actions only" cap). Renaming a menu path changes muscle memory and the owner owns the menu, so it is reported, not changed.
- Choreographer's transport scrubber is still a native `Z.Slider`, deliberately: it is the same case as Pyre's transport `SliderInt`, which is already open as **T-0260 Q12**.

## 9. Verdict

Seventeen passes in, the pass that finally looked at the windows nobody had looked at found the most structural items yet. Two of them are one-line consequences of an assumption nobody had tested: a library cell elides at the tail in every asset tool because round 16 fixed the *other* grid, and twenty-three of twenty-five windows have no minimum size at all — at its own declared minimum, Zoe puts 102 controls outside their parent. Beside those, one whole window's dials were native sliders while every other tool's are MicroSliders, both graph windows shipped with the class name on the tab and no tooltip on any control, two tools drew instruction paragraphs the rulebook puts in tooltips, and one button resolved 725 px wide. The by-eye channel itself was found returning the wrong window silently, which means some of round 16's captures were of whatever happened to be on top.

What is left is not more of the same. Seven windows — Lazor above all — are not on the mandated toolkit, so no audit this programme runs can see them, and the Rules Editor's empty state has nowhere to go. Both are owner-sized decisions, not harmony fixes.

Counted: **eight fixed** — the tail-eliding library cell, the graph windows' native slider + three untooltipped controls, their class-name tab, Choreographer's fourteen native dials and its clipped phase readout, Tapestry's pane-filling Play button, Lathe Mold's undeclared stretch, Choreographer's instruction paragraph, and the missing window minimum — plus **two handed to the owner** (the seven un-migrated immediate-mode windows; the Rules Editor's dead-end empty state). A ninth, methodological: the by-eye probe this programme has used since round 16 returns the wrong window silently, which is now replaced by `probes/capprint.ps1` + `probes/cap.sh`.

**FOUND: 10 non-trivial items, next pass needed**
