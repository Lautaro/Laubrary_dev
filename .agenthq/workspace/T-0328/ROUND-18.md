# T-0328 — the eighteenth full pass (2026-09-09)

Worktree `D:\UNITY\Laubrary Dev - Shaper`, branch `feat/shaper`, HEAD `c6a182ce` at session start (rounds 11–17 committed), editor on port 7801, PID **8124** (unchanged from round 17, so `cap.sh`'s hard-coded `-ProcId` is still right). `Application.dataPath` confirmed `D:/UNITY/Laubrary Dev - Shaper/Assets`; `isPlaying=False`, `isCompiling=False`, `scriptCompilationFailed=False`, active scene `Assets/Demos/ShaperDemo/ShaperDemo.unity`, `dirty=False`, `pixelsPerPoint=2.25`. Probes in `workspace/T-0328/probes/`, dumps in `out/`, captures in `shots/`.

**This file is written incrementally — each section is appended as it lands.**

---

## 0. Session log (append-only)

- Read `ShaperHarmony/RULES.md`, `T-0158/PROGRAMME_RULES.md`, `ui-layout-rules.md`, `UNITY_DEV_GUIDE.md`, `T-0325/ROUND-16.md`, `T-0326/ROUND-17.md`.
- Probe library copied from `T-0326/probes` and re-pointed (`T-0326`/`T-0325` → `T-0328` in every path, `T326.`/`T325.` prefs → `T328.`, `AuditT326`/`AuditT325` → `AuditT328`, `fill-sweep-t326` → `fill-sweep-t328`, `t326-fillsweep.cs` → `t328-fillsweep.cs`). The three *restore* lines in `wZ4-final.cs`/`z3-prefs.cs`/`z4-windows.cs` keep their `T-0324` values deliberately — those put the prefs BACK.
- `T0312.out` and `T320.capOut` re-pointed at `T-0328` (to be restored last, per round 14's lesson). Their session-start values are recorded above.
- **No Laubrary tool window was open at session start** — round 17's eight were closed with the editor's own layout since.

---

## 1. The seven windows `ZuiAudit` structurally cannot see — measured, not inherited

Round 17 §4.1 reported that seven windows draw zero UI Toolkit elements. **Re-measured independently at HEAD**: all nine (the six on the `Laubrary/` menu plus the three `Tools/ZUI/*`) were opened from their own menu items and walked with `e.hierarchy`. Every one returns **`elements=1`** — the root, and nothing else. `ZuiAudit` is not merely unhelpful on them; there is literally nothing for it to look at.

| window | menu | base | lines | UITK elements | verdict |
|---|---|---|---|---|---|
| Lazor | `Laubrary/Lazor/Lazor` | `LaubraryAssetWindow` → legacy `ZUIWindow` | **1327** (6 files) | 1 | (a) legacy IMGUI **authoring tool** — too big to port here, **card filed** |
| Zoe Preview | `Laubrary/Zoetrope/Zoe Preview` | legacy `ZUIWindow` | 178 | 1 | (a) → **PORTED this task**, §1.1 |
| Zounds | `Laubrary/Zounds` | legacy `ZUIWindow` | 499 | 1 | (a) legacy IMGUI authoring tool — **card filed** |
| ZUI Playground | `Laubrary/ZUI/Playground` | legacy `ZUIWindow` | 60 | 1 | **(c) DEAD** — §1.3 |
| Dashboard | `Laubrary/Dashboard` | raw `EditorWindow` | 263 | 1 | (b) exempt on control choice (**zero interactive controls**) — but it **leaked a texture per repaint**, §1.2 |
| Notifyer Log | `Laubrary/Notifyer Log` | raw `EditorWindow` | 440 | 1 | (a)/(b) diagnostic viewer **with** settings controls — **card filed** |
| Zhowcase / Zeditor / Texture Editor | `Tools/ZUI/*` | legacy `ZUIWindow` | 748 / **6062** / 715 | 1 each | (b) **exempt**: ZUI's own workshop, on `Tools/`, not on the `Laubrary/` menu and not a Laubrary tool — they exist to author ZUI's own stylesheet and icons, so a window that edits ZUI is not a consumer of it. Named here so the exemption is a decision, not an omission. |

### 1.1 Zoe Preview typed a clip name and had two buttons that did the same thing — PORTED to ZuiWindow

The window that spawns a character "exactly as gameplay does" asked the author to **type** which animation to play (`Clip (all parts)`, a raw `TextField`) — the one thing the project's most-quoted rule forbids ("a name is typed ONCE where it is DECLARED, and PICKED everywhere it is referenced"). Rounds 15–17 removed every typed reference they could see in the Zoe window; this one survived **because no audit could see this window at all**. That is the concrete cost of the blind set, and it is why item 2 of this card exists.

Photographed before the change on ProtoGuy (`shots/zoeprev-proto.png`): two full-pane-width `ObjectField`s stacked, an empty type-in box labelled `Clip (all parts)`, a full-width `View height (world units)` number field, and **two half-pane buttons `⟲ Restart` and `▶ Play` — both of which call `RestartClips()`**, i.e. `▶ Play` does not play, and one of the two labels is a lie. The `⟲` glyph also renders as tofu in this editor's font.

Ported to `ZuiWindow` (the same file; the asmdef already referenced `com.Lautaro-Arino.Laubrary.Zui.Editor`, so nothing else changed):

| was | now |
|---|---|
| `TextField` for the clip name | a **picker** over the clip names the SPAWNED character declares — unioned across its parts' `ZonedAnimationPlayer.version.animations`, which is the true owner here (a composite keeps its animations on the parts). Measured on ProtoGuy: **5 options**, `LegsWalk_N/E/S`, `LegsIdleRotation`, `UpperAimRotation`, plus `(none)` |
| — | the empty case is a **greyed picker reading `None declared`** with the reason, not a text field — the same look the Zoe window's Clip control uses |
| `⟲ Restart` + `▶ Play`, both calling `RestartClips()`, each ~half the pane | one **`Restart`** button, sized to its text, whose tooltip changes when there is nothing to replay |
| `DelayedFloatField` for View height | `Z.MicroSlider("View height", 0.5–20, default 4)` — label and value inside the track |
| two stacked full-width `ObjectField`s | one `Z.HGroup` row, `Zoe` and `Weapon` at 220 px each |
| no tooltip anywhere | every control has one; the stage's says what the red cross and the green outline mean, which nothing on screen ever said |
| a `Label` instruction in the empty state | a `Z.Help` empty state naming the field to fill |

**Verified by probe:** the window went from `elements=1` (invisible to every audit this programme runs) to `elements=34 drawn=30 controls=5`, and `ZuiAudit` reports `captionShort=0 overflowParentX=0 overflowParentY=0 offWindow=0 noTooltip=0 inertNoReason=0` — clean on every counter, empty and bound. **Verified behaviourally:** picking `LegsWalk_N` through the control's own change callback set the clip, started both parts (`Legs`/`Upper` `playing=True`), and the frame advanced `0 → 4` while the readout line tracked it live. **Verified by eye:** `shots/zoeprev-after.png`, `shots/zoeprev-playing.png`.

`Editor/ZoetropeLaunimator/ZoePreviewWindow.cs` — whole file.

### 1.2 The Dashboard allocated a `Texture2D` on every repaint, and nothing ever released one

`DashboardEditorWindow.OnGUI` ends with an unconditional `Repaint()`, so it repaints continuously for as long as the window is open. Inside its per-owner loop it built two `GUIStyle`s and called `MakeTexture(2, 2, Color.gray)` — a fresh `Texture2D`, an engine object, per group per pass.

**Measured**, with one log line pushed into the Dashboard's own static log list and 60 repaints driven through `EditorApplication.update`:

| | textures alive |
|---|---|
| before | 5099 |
| after 60 repaints | 8145 |
| **delta** | **+3046, every one an unnamed 2×2** |

That is ~50 leaked textures per repaint of a window showing a single line, growing without bound for as long as the window is open. It has been invisible for the same reason as everything else in this section: the window draws no UI Toolkit elements, so no audit in this programme has ever looked at it.

Fixed by building the styles and the background texture **once** (`HideFlags.HideAndDontSave`, released in `OnDestroy`), and reusing the entry style rather than allocating one per line.

The same pass gave it the empty state it never had: the window opened as a **completely blank grey pane** (`shots/DashboardEditorWindow.png`) with no text of any kind — the handover-walk rule's dead end. It now says what fills it. No control was added; it is one readout line, the same shape as Zoe's `None declared`.

**Exempt on control choice, and here is why:** the Dashboard draws *zero interactive controls* — it is a scrolled column of labels reporting `[Dashboard]`-marked fields at runtime. There is no enum to make a radio of, no scalar to make a slider of, no reference to pick. Migrating it to UI Toolkit would change nothing a user can touch.

`Editor/Dashboard/DashboardEditorWindow.cs` — `OnGUI`, `DrawHorizontalLine`, `MakeTexture` (removed), new cached styles + `OnDestroy`.

### 1.3 `Laubrary/ZUI/Playground` is a dead hardcoded mock on the shipped menu

60 lines. It draws one row — an empty button, `M`, `S`, `Knight Attack`, a bin icon — in which:

- every control's return value is **discarded** (the two toggles are passed a literal `false` and their result ignored), so nothing it draws does anything when pressed;
- the name button's text is the string literal `"Knight Attack"`;
- the second row is a comment reading `// Row 2: placeholder` and an `EditorGUI.DrawRect`;
- the first button's icon (`ZUI.FindIcon("open-editor")`) does not resolve, so it renders as an **empty button** (`shots/playground.png`);
- the class has **no fields at all** — there is no state to author;
- its own header comment says "Open via Tools > Zounds > ZUI Playground", a menu path that does not exist.

Grepped package-wide: nothing references `ZUIPlayground` except its own `[MenuItem]`. It is a scratchpad left on the `Laubrary/` menu, which the project's menu rule calls out by name ("permanent clutter the user has to notice, question, and hunt down to delete"). **Not deleted** — the card says a dead window goes to the owner rather than being removed — **filed on T-0260**.

### 1.4 What the other blind windows showed, by eye

- **Lazor** (`shots/LazorWindow.png`): the one full authoring tool in the blind set, and it visibly predates every AssetKit landing of the last seven rounds — its header is `[None (Lazor Shape)] [New] [Browse]` with **no `Save` at all** (every `ZuiAssetWindow` shows a greyed Save with a reason), a **`Refresh` button stranded ~1000 px away** at the far right of the library header, and one of its two library cells drawing a **blank thumbnail** for a visual asset. It gets none of the middle-elided cells, the 820 px floor, the honest empty states or any audit.
- **Zounds** (`shots/ZoundsWindow.png`): an honest empty state (`No Zounds Project Loaded`, `Create New` live, `Load`/`Save` greyed) — but `Auto-Save` is a **native checkbox**, which the rulebook bans on every surface, and the `Project JSON` field stretches the full pane.
- **Notifyer Log** (`shots/NotifyerLog.png`): a toolbar of three equal-width stretched buttons over **two titled but completely empty columns** (`Event Types`, `Event Log`) with nothing saying what fills them — the same dead-end empty state the Dashboard had.

---

## 2. Cartographer's two sub-windows, opened for the first time

`PropWindow` and `TilesetBuilderWindow` have no `[MenuItem]` of their own — they are reached from the Cartographer window's Props and Palette boxes — and round 17 enumerated them without opening them. Both were opened through their own `OpenFor(null)` entry point (the empty state), audited, cold-walked New → type → **Create** through the window's own buttons, audited again on the fresh asset, and photographed.

| checked | Prop Editor | Tileset Builder |
|---|---|---|
| declared minimum | 820 × 520 — round 17's `ZuiAssetWindow.MinWindowSize` reaches them, confirmed | 820 × 520, now overridden to 880 × 520 (§2.2) |
| empty state | `[None (Prop)] · Save greyed with a reason · New · Browse · Prop library (1) · Refresh` | same shape, `Tileset library (0)` with *"No Tileset assets yet — hit New to make one."* |
| every leaf control's tooltip | 5 of 5 | 5 of 5 |
| the New row's destination sentence | *"Creates `Assets/Cartographer/Props/<name>.asset`"* | *"Creates `Assets/Cartographer/Tilesets/<name>.asset`"* — both real per-tool folders, round 16's finding 3.7 does not recur |
| the fresh asset, audited | `elements=162 drawn=136 controls=23`, **clean on every counter** | `elements=262 drawn=185 controls=45`, **`captionShort=2`** → §2.2 |
| empty states inside | *"No tiles yet — pick a biome or add a tile above."*, *"Pick a biome above to edit it."*, *"No level in the scene yet."* + a `Create level in scene` button — each names the affordance rather than describing a dead end | *"Step 1: open a sheet above, or drag one onto the canvas."* and a canvas reading *"Drop a sprite sheet here — from the project or straight from Explorer"* |

By eye: `shots/prop-fresh.png` (the 8 × 8 paint grid with its hover cell, the whole left column of boxes), `shots/tileset-fresh.png`.

### 2.1 One word named two different things on the same screen. Fixed, and a new sweep found it

The Prop Editor drew **two sections both titled `Tags`**: the one `ZuiAssetWindow` inserts into every asset window (*"Tags for this asset, shared with the rest of Laubrary"*, at y=68) and Cartographer's own (*"Gameplay labels this prop carries"*, at y=492, holding `TileTag` assets). Different concepts, identical title, one screen, ~420 px apart.

It was found by a check no round has run before: **a sweep for two drawn box/section titles that read the same**. That sweep also had to be widened once — Choreographer's headers use a *third* title class (`zui-text--section` on a `ZuiSectionLabel`) that neither the box nor the section class covers, so the first pass under-counted several windows. With all three classes, package-wide over 21 bound windows: **one collision**, this one. Pyre's `Adjust` × 2 is two per-item ramp cards with identical tooltips — a legitimate repeating card, not a collision.

`CartographerWindow` draws the same collision by construction (`Z.Section("Tags", "Gameplay labels on the ACTIVE layer…")` under the same inserted base section); it could not be demonstrated live because the project contains no `LevelAsset` to bind. Both are renamed to **`Tile tags`**, which is what they hold. The Cartographer section keeps its `"cartographer.tags"` state key, so its fold state is unaffected; its `ZuiSectionToggleBar` unit is renamed with it, so that one chip's saved on/off state defaults once.

`Editor/Cartographer/PropWindow.cs:164`, `Editor/Cartographer/CartographerWindow.cs:324,328`. Re-swept after: **zero duplicate titles package-wide**.

### 2.2 Two Tileset Builder dials printed half a caption. Fixed

`ZuiAudit` on a fresh Tileset: `captionShort=2` — the library header's `Lines` (needs 27.6 px, has 13.3) and `Alpha` (needs 29.8, has 12.9) MicroSliders, each cut roughly in half. Both were declared 70 px wide beside a `Zoom` at 90 px that fits. Now 95 px; re-measured `captionShort=0`.

Separately, **at the shared 820 px floor** the same header pushes its last toggle `Osc frame` **38.7 px out of its own row** (it carries three view dials and four mode buttons on one line). Measured clean at 880. `TilesetBuilderWindow` therefore declares its own `MinWindowSize` of 880 × 520 — which is what the virtual exists for — rather than raising the floor for fifteen other tools.

`Editor/Cartographer/TilesetBuilderWindow.cs` — the three `AddHeaderContent` sliders and a new `MinWindowSize`.

### 2.3 The Rules Editor's empty state (Q20) — left standing, deliberately

Round 17 §4.2 named it: `Laubrary/Rules Editor` opens to three toolbar buttons and a `Z.Help` box telling the reader to go write C# and edit a scene elsewhere. The card asks whether the AssetKit "nothing to press" fix is derivable here without a design decision. **It is not.** The AssetKit fix works because a `ZuiAssetWindow` owns a concrete asset type it can create — `New` writes `Assets/<Tool>/<name>.asset` and binds it. The Rules Editor edits the `RuleSet` of a **`RulesHost` component in the open scene**, and its rules are instances of **`GameRule` subclasses that must exist in project code**. So "nothing to press" has two possible meanings — create a `RulesHost` in the scene, or the project declares no `GameRule` subclass at all — and the honest affordance differs per case, one of them being a scene mutation from a tool window. That is a design decision, not a derivation. **Q20 stands.**

---

## 3. The floor round 17 declared, measured with real content

Round 17 established `ZuiAssetWindow.MinWindowSize` = 820 × 520 and swept nine tools at 400–820 px **wide**. This pass set every one of the **nineteen** concrete `ZuiAssetWindow` subclasses to exact sizes, with each bound to a real asset, and audited at each — width *and* height, across separate probe round trips so the layout actually settles (setting `position` and auditing in the same call returns the pre-resize layout, byte-identically at every size; that trap cost two measurements before it was spotted).

All nineteen report `min=820x520`, so round 17's landing reaches every tool including the two Cartographer sub-windows. At that size, with content:

| window | at 820 × 520 | clean at |
|---|---|---|
| 16 of 19 | clean on every counter | — |
| **Pyre** | **`offWindow=18`, one 166.7 px parent overflow** — §3.1 | 820 × 720 |
| **Tileset Builder** | `Osc frame` 38.7 px out of its row | 880 × 520 (now its own declared minimum) |
| Choreographer | one MicroSlider 4.9 px out of its box vertically | 1000 × 900 — trivia, the control still draws |

### 3.1 At its own declared minimum, Pyre hides a whole box below the window

The number 820 × 520 came from Pyre, whose own comment explains that below it the split's two panes cannot both fit. Measured at exactly that size, bound to a real spec: the transport box overflows its parent by **166.7 px** vertically, and **eighteen** controls and captions sit 20–68 px **below the window** — `▾ Preview backdrop`, `Recall…`, `Save…`, `Colour`, `HDR` and the rest of that box. The right pane has **no scrollbar** (the left pane does), so they are not merely out of view, they are unreachable without resizing the window.

By eye, `shots/pyre-at-820x520.png`: the right column ends at `Delay 0.00 · frame 8/18` flush against the bottom edge, with nothing below it and no scroller.

Bisected: `offWindow` = 18 / 15 / 7 / 4 / 0 at heights 520 / 560 / 600 / 640 / 680, and the residual parent overflow clears at 720. **Pyre is clean at 820 × 720, not at its declared 820 × 520.**

**Not fixed here.** `Editor/Pyre/**` is read-only reference for this programme (PROGRAMME_RULES, "Where you work"), and raising the *shared* floor to 720 pt tall on Pyre's behalf would impose ~80% of this desktop's height on fifteen other tools. Filed as **T-0331**.

---

## 4. Both bakes, cold, twice — and pixel-identical to their previews

| | Shaper | Pyre |
|---|---|---|
| opened from | `Laubrary/Shaper`, then its own `New` → typed name → `Create` | `Laubrary/Pyre`, same |
| where the new asset landed | `Assets/Demos/ShaperDemo/AuditT328ShaperA.asset` — the New row's own sentence says *"beside the Shaper that is currently open"*, and that is where it went | `Assets/Pyre/AuditT328PyreA.asset`, same sentence |
| bake path | the window's **own `Bake` button**, pressed (neither baker raises a file panel — `SaveFilePanel` appears in neither; both derive the destination from the document's asset path) | same |
| walk 1 | sheet + `.anim` + `ShaperClip` written | sheet + `.anim` written |
| walk 2 | `…_1.png`, `…_1.anim`, `… Clip_1.asset` — **versioned, nothing overwritten** | `…_1.png`, `…_1.anim` — same |
| sheet | 768 × 128, 16 frames in 8 × 2 | 512 × 128, 16 frames in 8 × 2 |
| **frame-for-frame vs the live renderer** | `ShaperDocumentRenderer.RenderPhase` at `doc.PhaseOfFrame(f)` for all 16 frames, compared byte-for-byte against the baked sheet's cell: **0 differing pixels of 98 304** | `PyreRenderer.RenderSheet` vs the baked PNG at `PyreRenderer.FrameRect`: **0 differing pixels of 65 536** |

Both tools claim in their own Bake tooltip that the bake uses the same renderer as the preview and is "byte-identical to what you see here". Measured: it is, on every frame of both.

**One probe-method note, recorded because it produced a false finding for ten minutes.** The first two presses of Shaper's Bake button produced nothing at all, while calling the baker directly produced a full sheet — which reads exactly like a broken button. It is not: the button sat at y=904 in a 926 px window, inside a `ScrollView`, and a synthesized pointer press on an element scrolled out of view does nothing. `ScrollTo` first, and the same press bakes. This is round 11's rule and round 16 §4.1's Resample lesson for the third time; **a press that "does nothing" is a scroll question until proven otherwise.**

---

## 5. Temporal, and the regression sweep

**The shipped demo document plays, and the window stays clean while it does.** `Assets/Demos/ShaperDemo/ShaperDemoDoc.asset` played through the transport's own button and sampled across probe round trips ≥1.5 s apart: **frame 1 → 15 → 11** of 16, three distinct frames, `Playing` in the status line and the button reading `❚❚ Pause` at every sample. `ZuiAudit` at each sample **while playing**: `captionShort=0 overflowX=0 offWindow=0 noTooltip=0 inertNoReason=0`. Paused: `currentFrame=10`, readout `frame 11/16`, button back to `▶ Play`, both readouts agreeing, same zeros. Window shape unchanged from rounds 11/16/17.

**The fill sweep is identical, row for row.** Round 17's probe re-run against HEAD plus this round's eight edits (`out/fill-sweep-t328.tsv`, **415 rows**), diffed on `(doc, kind, card, control, state)`:

| baseline | rows | rows differing | rows missing | regressions (was > 0, now 0) |
|---|---|---|---|---|
| `T-0326/out/fill-sweep-t326.tsv` | 415 | **0** | 0 | **0** |
| `T-0325/out/fill-sweep-t325.tsv` | 415 | **0** | 0 | 0 |
| `T-0324/out/fill-sweep-t324.tsv` | 415 | **0** | 0 | 0 |
| `T-0323/out/fill-sweep-t323.tsv` | 415 | **0** | 0 | 0 |
| `T-0277/fill-sweep-after.tsv` | 409 | 54 (all improvements landed in rounds 12–17) | 0 | **0** |

Round 14's trap avoided again: `probes/p1-rfield.cs` creates the 16 × 16 `RFloat` the sweep loads but never makes.

---

## 6. Rounds 16 and 17's landings, re-verified by eye with the corrected capture path

Round 17 replaced round 16's `capwin.ps1` — which silently returned a picture of the wrong window — with `capprint.ps1` + `cap.sh` (`PrintWindow`, matched on the physical rect). Everything below was photographed through that path, plus a new `probes/capmain.ps1` for a window that is docked rather than floating.

| landing | round | what is on screen now |
|---|---|---|
| Choreographer's fourteen native dials became MicroSliders | 17 §2.4 | every dial — Length 1.2, Bend 0.1, Angle 0, Default count 8, Duration 4, Stagger 0.5, Preview count 0, Speed 1 — draws label **and** value inside its own track (`shots/choreo.png`) |
| its clipped phase readout | 17 §2.4 | the transport prints **`0.028`** in its box, not a seven-decimal value cut mid-digit |
| its four-line canvas-gesture paragraph | 17 §2.7 | gone from the left column; the stage carries it as a tooltip |
| both graph toolbars | 17 §2.2 | `No graph loaded · Save · Frame All · **Fade [1.5]** as one embedded MicroSlider`; the instruction paragraph is gone (`shots/braingraph.png`) |
| the graph windows' class-name tab | 17 §2.3 | the tab reads **`Brain Graph`** |
| Tapestry's pane-filling Play button | 17 §2.5 | measured **88.0 px** (was 725) |
| a library cell eliding in the MIDDLE | 16 §3.1 / 17 §2.1 | Pyre's library, 33 cells: **7 elided, `tooWide=0`**, and `Directiona…st 1 Plus` / `Directiona…st 2 Plus` / `Directiona…last Plus` are three distinguishable rows (`shots/pyre-library-crop.png`). The one remaining duplicate visible name is two assets both actually called `New Pyre Plus` |
| `Muzzle Event Name` is a picker | 16 §3.3 | on ProtoGuy, a **greyed** `None declared` dropdown (`choices=1`, `enabled=False`), on both weapon slots — measured through the live control |
| the rig anchor's `MetaLayer Id` is a picker | 16 §3.4 | `Muzzle (Vector)` with 3 choices — `(none) · Muzzle (Vector) · Waist (Point)` — on both sides of the connection |
| a `StringDropdown` shows an unresolved stored value | 16 §3.5 | the Hit reaction reads **`Muzzle (unresolved)`** |
| the point-layer picker lists Waist | 16 §3.2 | yes, in the same 3-choice list above |
| composite clip pickers | 15/16 | `(none) · LegsWalk_N · LegsWalk_E · LegsWalk_S · LegsIdleRotation · UpperAimRotation` — 6 choices, enabled |
| the Zoe window's text inputs | 16 §3.6 | exactly the declarations: the display name, the custom-event id, and a `ListView` size field |
| the 820 × 520 floor on the tools round 17 did not sweep | 17 §2.8 | all **19** `ZuiAssetWindow` subclasses report `min=820x520`; measured at it, §3 |

**Still not seen: the `Add Previewable` popup.** Round 17 measured why (a `PopupWindow` exists only while the editor holds focus, and every probe round trip surrenders it) and the card forbids a sixth attempt. Not attempted.

**Still not seen: the `On Frame` frame picker**, for the same reason as rounds 16 and 17 — reaching it means switching a reaction's trigger on ProtoGuy, a shipped demo asset this card opens read-only.

### 6.1 Two more things the by-eye pass found, and fixed

- **BackSplash still had a native `Z.Slider`.** Round 17 counted the package's 27 `Z.Slider` call sites and fixed the fourteen in Choreographer; BackSplash's one survived. `ZuiAudit` on it bound to a real backdrop: the slider's inline numeric input printed **`10.85311` into a 44 px box**, spilling 9.8 px — the identical defect, in the identical shape. `Zoom` is now a `Z.MicroSlider` (0.05–16, two decimals, label and value in the track), and the `Z.Field` wrapper that repeated its label is gone. In the same row, the position pad's drag wrote seven decimals of mouse noise into the X/Y fields beside it (`-3.372548`, spilling 2.7 px); the drag now rounds at source to three decimals — finer than one pixel of the pad — and the fields are 84 px. Re-measured: `overflowParentX` 2 → **0**. `Editor/BackSplash/BackSplashWindow.cs`.
- **Three Choreographer section titles explained instead of naming.** `Spread (start line → circle)`, `Anchors (Launcher / Target)` and `Preview — shows only what's ticked` — the exact shape the rulebook names ("a box titled *Live preview subject (not baked)* is wrong"). All three already had tooltips; the explanation moved into them and the titles are now `Spread`, `Anchors`, `Preview`. Found by a second new sweep — box/section titles carrying a parenthetical, an em-dash clause or a colon — run over every open window. The other eleven hits are counts (`Weapons (0)`, `Previewables (2)`), library titles (`Level library (0)`) and step numbers (`1 · Sheet`), which are identity, not explanation. `Editor/Choreographer/ChoreographerWindow.cs:263,304,328`.

### 6.2 The one spatial pair in the package still drawn as two 1D fields. Fixed

Swept every X/Y field pair in the package. BackSplash's `Position` and Mirage's world position both pair their numeric fields **with a `Z.Pad`** — the sanctioned shape (the pad is the control, the fields are for exact entry). The rig anchor row in the Zoe window drew `Offset X` and `Offset Y` as two bare `NumField`s with nothing to aim, on both sides of every composite connection — the one place in the package where a spatial pair is two independent 1D fields, and the rulebook's most explicit exclusion from row-packing ("reach for `Z.Value2D`/`Z.Pad`; packing two 1D fields fixes the width but not the aim-a-2D-value ergonomics").

It now draws BackSplash's shape: a 68 px pad over ±3 world units with the two fields beside it, kept in sync both ways. A value beyond the range is still typeable — the dot just sits at the edge — so no authored value becomes unreachable. Verified by eye on ProtoGuy (`shots/zoe-anchor-crop.png`): the parent side's dot sits **above** centre for its authored `Offset Y = 1.3125`, the child side's at centre for `(0, 0)`. `ZuiAudit` on the whole window after: `captionShort=1` (Unity's own `+` glyph, the standing trivia) and zero on every other counter.

`Editor/Zoetrope/ZoetropeWindows.cs` — `BuildAnchorRow`.

---

## 7. Files touched

| file | what |
|---|---|
| `Editor/ZoetropeLaunimator/ZoePreviewWindow.cs` | ported off the legacy IMGUI `ZUIWindow` onto `ZuiWindow`: the typed clip name becomes a picker over the clip names the spawned character declares (greyed `None declared` when it declares none), the two buttons that both restarted become one `Restart`, `View height` becomes a MicroSlider, the two stacked pane-wide object fields share one row, every control gains a tooltip, and the stage says what its red cross and green outline mean. The window went from invisible to `ZuiAudit` to clean on every counter |
| `Editor/Dashboard/DashboardEditorWindow.cs` | the per-repaint `Texture2D` allocation (measured: **+3046 leaked textures over 60 repaints** of a single log line) becomes one cached `HideAndDontSave` texture released in `OnDestroy`, with the two per-pass `GUIStyle`s cached alongside; the blank grey empty pane gains one line saying what fills it |
| `Editor/Cartographer/PropWindow.cs` | the box titled `Tags` becomes `Tile tags`, so the word stops naming two different things on one screen |
| `Editor/Cartographer/CartographerWindow.cs` | the same rename on its own `Tags` section and its toggle-bar unit |
| `Editor/Cartographer/TilesetBuilderWindow.cs` | the library header's `Lines` and `Alpha` dials 70 → 95 px, so their captions stop being cut in half; a declared `MinWindowSize` of 880 × 520, the measured width at which the header's last toggle stops being pushed 38.7 px out of its row |
| `Editor/BackSplash/BackSplashWindow.cs` | the last native `Z.Slider` outside Choreographer becomes a MicroSlider (it was printing `10.85311` into a 44 px box); the position pad's drag rounds at source, and its two numeric fields are 84 px |
| `Editor/Choreographer/ChoreographerWindow.cs` | three section titles stop explaining — `Spread`, `Anchors`, `Preview` — with the explanations moved into the tooltips they already had |
| `Editor/Zoetrope/ZoetropeWindows.cs` | the rig anchor's `Offset X` / `Offset Y` become a `Z.Pad` with the two fields beside it, the shape BackSplash and Mirage already use for a spatial pair |

**No Pyre runtime or form file was touched, no `Editor/Pyre/**` file, no `CHANGELOG.md`, nothing committed** (ShaperHarmony rule 3). Five compiles this session, all `scriptCompilationFailed=False`; the last is the state the tree is in.

## 8. State left behind

`git status` shows **exactly the eight source files above**, plus what was already modified before this session (`.mcp.json`, `Assets/Pyre/Green Lantern.asset`, the TextSplash demo border font, the `Samples~` block) and the untracked `Assets/Shaper/` and `Assets/_Recovery/` that were there at session start. `Assets/Mirage/Test 2.asset` did **not** come out modified this time (rounds 15 and 16 both dirtied it by probing).

Everything created was deleted through `AssetDatabase.DeleteAsset` (which removes the `.meta`) — 18 assets and 3 folders:

- `Assets/Demos/ShaperDemo/AuditT328ShaperA.asset` and its full bake output across three bakes: `.png`, `.anim`, ` Clip.asset`, `_1.png`, `_1.anim`, ` Clip_1.asset`, `_2.png`, `_2.anim`, ` Clip_2.asset`
- `Assets/Pyre/AuditT328PyreA.asset` + `.png`, `.anim`, `_1.png`, `_1.anim`
- `Assets/Cartographer/Props/AuditT328PropA.asset`, `Assets/Cartographer/Tilesets/AuditT328TilesetA.asset`
- `Assets/Shaper/Audit0277/rfield0277.asset`
- the folders `Assets/Cartographer/Props`, `Assets/Cartographer/Tilesets` (both created by this session's cold walks) and `Assets/Shaper/Audit0277`

A project-wide scan at cleanup found **0 dirty ScriptableObjects** and `AuditT328` matches **0**. **A generated-sibling note for the next pass, in the family of round 14's `.states.cs` and round 16's `Border Fonts/`: a Shaper bake writes THREE siblings, not one** — `<name>.png`, `<name>.anim` and `<name> Clip.asset` — and a repeat bake versions all three, so one asset plus two bakes is nine files to clean up, not three.

The demo document was never saved. The demo scene is the open scene, `dirty=False`, and was never saved. **Play mode was never entered.** No tag was created; the Test Runner was never run; the editor was never relaunched; no confirm dialog or native file panel was opened.

Prefs: `T0312.out` and `T320.capOut` restored to their T-0324 values (`T0312.out` **last**, after the final dumping probe, per round 14's lesson); `T320.capWin` was measured unchanged and left alone; every `T328.*` key deleted. `Undo.ClearAll()`, console cleared.

**The open window set is back to the editor's own seven** (Toolbar, Inspector, Console, Project, Hierarchy, Scene, Game) — no Laubrary tool window was open at session start, and none is open now. A final sweep confirms **no `WS_EX_TOPMOST` window belongs to this editor's process**, and no other application was minimised, moved or closed at any point: `PrintWindow` never needs the foreground.

## 9. Verified how

**By probe, in the live editor:** all nine audit-blind windows opened from their own menu items and measured at `elements=1`; the Dashboard's texture count before and after 60 driven repaints (5099 → 8145, +3046 unnamed 2×2); the Zoe Preview port measured empty and bound (`elements=1` → `34`, `ZuiAudit` zero on every counter) and behaviourally (a clip picked through the control's own callback started both parts and the frame advanced 0 → 4); both Cartographer sub-windows' empty-state and fresh-asset audits with every leaf control's tooltip; the two cold walks New → Create and their destination folders; the duplicate-title sweep over 21 bound windows with all three title classes, before and after; the explaining-title sweep, before and after; the X/Y-pair sweep over the whole package; the nineteen-subclass `minSize` census and the size sweep that found Pyre's 18 off-window controls and bisected its floor to 720 height; the Tileset Builder's two clipped captions before (27.6/13.3, 29.8/12.9) and `captionShort=0` after; BackSplash's `overflowParentX` 2 → 0; Pyre's library elide (33 cells, 7 elided, `tooWide=0`, one genuine duplicate name); ProtoGuy's five pickers read through the live controls; both bakes walked twice and compared frame for frame to their renderers (0 differing of 98 304 and 65 536); three play samples and the paused audit of the demo document; the 415-row fill sweep against five baselines; five compiles; the asset/pref/window/topmost cleanup and the project-wide dirty scan.

**By eye, in real captures of the running editor** (`shots/`): all six blind `Laubrary/` windows as they open — Lazor's Save-less header and stranded Refresh, Zounds' native Auto-Save checkbox, the Notifyer Log's two empty titled columns, the Dashboard's completely blank pane, the ZUI Playground's hardcoded row with an empty first button; Zoe Preview before (a type-in `Clip` box and two half-pane buttons) and after (a picker, one Restart, a MicroSlider, both fields on one row, the character rendering with its hurtbox); the Prop Editor's fresh asset with its 8 × 8 paint grid; **Pyre at 820 × 520 with a whole box below the window and no scrollbar**; Choreographer's dials as embedded MicroSliders with a 3-decimal phase readout and no instruction paragraph; the Brain Graph toolbar with its Fade MicroSlider and a properly named tab; Pyre's library reading `Directiona…st 1 Plus` / `…st 2 Plus` / `…last Plus`; the rig anchor's new offset pad with its dot above centre for the authored 1.3125.

**Not verified:**

1. **No *human* has operated any of this.** Every press was a synthesized pointer or key event.
2. **The `Add Previewable` popup was not attempted** — round 17 measured why it cannot be photographed by a probe, and the card forbids a sixth try.
3. **The `On Frame` frame picker was not seen**, because reaching it means editing ProtoGuy, which this card opens read-only.
4. **The three `Tools/ZUI/*` windows were opened and measured but not walked.** They are exempted as ZUI's own workshop, on the `Tools/` menu; that is a judgement, not a measurement of their contents.
5. **Lazor, Zounds and the Notifyer Log were opened, photographed and read — not audited or walked.** `ZuiAudit` structurally cannot see them; their findings above come from the pictures and the source.
6. **Nothing was measured below 820 px wide or 520 px tall.** Round 17's floor is the bottom of the range this pass swept.
7. **Cartographer's own window could not be bound** — the project contains no `LevelAsset` — so its `Tags` collision was found in source and fixed unseen, and its window has still never been audited with content.
8. **The Choreographer 4.9 px overflow, the SpriteFx 1.8 px image overflow and the `Z.Object` label clipping were measured and not fixed** (the first two are sub-glyph trivia; the third is a shared-control change, filed as T-0332, §10).
9. **`ExecuteAlways` behaviour is still measured with the editor in the background**, where it does not tick; the Zoe Preview animation had to be driven through `EditorApplication.update` by hand, so "how long a thing takes to appear for a real user" is still not measured.

### Trivia (named, not fixed)

- Unity's own `+` glyph on the `ListView` footer inside the sanctioned reorderable-list island measures 2.7 px short — the same one glyph rounds 14–18 have reported.
- Shaper's two frame readouts can disagree by one frame **mid-playback** (`frame 1/16` beside `Playing — frame 16/16`, `frame 11/16` beside `Playing — frame 10/16`). Paused they always agree. A one-frame lag at ~6 fps, not a wrong number. Round 17 named it.
- SpriteFx draws one `Image` 1.8 px outside its parent on both axes — 0.3 px past the audit's own 1.5 px device tolerance, on a 190 px element.
- Choreographer's `Speed` MicroSlider sits 4.9 px outside its box vertically at 820 px wide; it clears at 1000 and the control draws correctly at both.
- A shipped Pyre asset is named `Driectional…` — a typo the new middle elision makes visible in the library (`Driectiona…last Plus`).
- The Zoe window's `Preview (schematic)` section title carries a parenthetical, but it distinguishes that preview from the `Preview in Mirage` button beside it, so it is identity rather than explanation. Left alone.
- Choreographer's transport scrubber is still a native `Z.Slider`, deliberately — the same case as Pyre's transport `SliderInt`, open as **T-0260 Q12**.

## 10. Filed this round

Five cards on `ShaperHarmony-2026-09-07`:

| id | what |
|---|---|
| **T-0329** | Lazor is a full authoring tool still on the legacy IMGUI base (1327 lines across 6 files) — port or retire |
| **T-0330** | Zounds (499) and Notifyer Log (440) are immediate-mode windows `ZuiAudit` cannot see, with a native checkbox and two empty titled columns respectively |
| **T-0331** | Pyre puts 18 controls off the window at its **own** declared 820 × 520 minimum, with no scroll on the right pane |
| **T-0332** | `Z.Object`'s grow-to-fit still clips two asset-name fields (Lathe 24 px, TextSplash 109 px) even at 1500 px wide |
| **T-0333** | Owner decision: `Laubrary/ZUI/Playground` is a dead hardcoded mock on the shipped menu |

**T-0260 Q20 (the Rules Editor's empty state) stands** — §2.3 explains why it is a design decision rather than a derivation, so it is not answered here.

---

## 11. Verdict

Eighteen passes in, the pass that finally *opened* the windows nobody could audit found the sharpest items of the programme. The one that matters most is not a layout fault: **the window that spawns a character "exactly as gameplay does" asked the author to type the animation's name.** Rounds 15, 16 and 17 removed every typed reference they could find, and this one survived all three because no audit in this programme could see the window it lives in — which is exactly what "seven windows draw zero UI Toolkit elements" costs, stated as a bug rather than as a statistic. Beside it, an editor window has been leaking a texture on every repaint for as long as it has existed (3046 in sixty repaints, measured); one word named two different things on the same screen in two Cartographer windows; the last native slider outside Choreographer was still printing a seven-digit number into a 44 px box; and the only spatial pair in the package still drawn as two typed fields was on the rig row rounds 15–16 had already been editing.

The single most uncomfortable measurement is Pyre's. The programme adopted 820 × 520 as its shared floor last round because Pyre declares it; measured at that exact size with real content, **Pyre hides a whole box of controls below its own window with no way to scroll to them.** It is clean at 720 tall. That is not a small drift, and it is out of this programme's reach to fix, so it is on the owner's desk as T-0331.

Two new mechanical checks were added this round and both found something on their first run — duplicate box titles, and titles that explain instead of naming. That is the honest signal about whether the programme is finished: a pass that invents two cheap checks and both hit is not a pass that is running out of things to find.

Counted: **nine fixed** — the typed clip name and the two duplicate buttons in Zoe Preview (with the whole window ported off the legacy base), the Dashboard's per-repaint texture leak, the Dashboard's blank empty state, the `Tags`/`Tags` collision in two Cartographer windows, the Tileset Builder's two half-cut captions, its too-narrow declared minimum, BackSplash's native slider and its noisy drag, Choreographer's three explaining titles, and the rig anchor's two-field spatial pair — plus **five handed to the owner** (T-0329 to T-0333) and one question left standing (T-0260 Q20).

**FOUND: 9 non-trivial items, next pass needed**
