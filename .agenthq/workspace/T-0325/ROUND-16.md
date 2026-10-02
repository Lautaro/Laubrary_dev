# T-0325 — the sixteenth full pass (2026-09-09)

Worktree `D:\UNITY\Laubrary Dev - Shaper`, branch `feat/shaper`, HEAD `b0323a9f` at session start (rounds 11–15 committed), editor on port 7801. `Application.dataPath` confirmed `D:/UNITY/Laubrary Dev - Shaper/Assets` at the head of the session. Probes in `workspace/T-0325/probes/` (copies of round 15's, every hard-coded output path re-pointed at `T-0325` before the first run and re-checked afterwards), dumps in `out/`, captures in `shots/`.

**This file is written incrementally — each section is appended as it lands.**

---

## 0. Session log (append-only)

- Read `ShaperHarmony/RULES.md`, `ui-layout-rules.md`, `UNITY_DEV_GUIDE.md`, `T-0320/ROUND-11.md`, `T-0323/ROUND-14.md`, `T-0324/ROUND-15.md`.
- Probe library copied and re-pointed: `e2-cappop.cs` → `T-0325/shots/popup.png`, `f5-render.cs` → `T-0325/shots/mirage-rig.png`, `t325-fillsweep.cs` → `T-0325/out/fill-sweep-t325.tsv`, `zrun.sh` → `T-0325/probes`. `T0312.out` pref repointed at `T-0325/out` (restored LAST, per round 14's lesson).

---

## 1. Methodology — the by-eye channel had to be rebuilt again, for a new reason

Round 11 established the channel and round 14 kept it: minimise whatever sits above the Unity window, then read the composited desktop over the window's own rect from inside the editor (`cap2.cs`). **That recipe does not work this session, and the reason is worth recording because it will recur.**

| what was measured | consequence |
|---|---|
| The desktop is occupied by a **second agent driving the Cupcakehour editor** (a full-screen Unity 2021 editor) and by a Windows Terminal running a live session the user is typing into. Both take the foreground repeatedly, seconds apart. | Minimising them — round 11's fix — is not acceptable here: one is another agent's work surface, the other is the user's own keyboard focus. |
| **The Shaper editor's floating tool windows disappear from `EnumWindows` entirely whenever that editor is not the active application.** Measured directly: `MirageWindow` exists in `Resources.FindObjectsOfTypeAll` at a real `position` while no visible top-level window of that title exists. | A capture issued as a separate `unity.exe command` round trip is 1–2 s after any raise, which is long enough to lose the window again. Round 11's two-eval "place, then capture in the NEXT one" is exactly the losing shape under contention. |
| `SetWindowPos(HWND_TOPMOST)` on the tool window **does not survive** that hide/show cycle. | Pinning ahead of time does not help either. |

**The fix, and the probe the next pass should reuse: `probes/capwin.ps1`.** It raises the target window and reads the desktop over it **in one process**, so there is no gap to lose: `SetProcessDPIAware` (without it the shell is DPI-virtualised and a 900pt window comes back 1163px wide instead of 2035 — a 12px glyph does not survive that), `EnumWindows` by exact window title within the editor's PID, `SW_RESTORE` (not `SW_SHOW` — the main editor window was found minimised at −32000,−32000 and `SW_SHOW` leaves it there), `AttachThreadInput` + `BringWindowToTop` + `SetForegroundWindow`, then `CopyFromScreen` over `GetWindowRect`, then `HWND_NOTOPMOST` again so nothing is left pinned over the user's desktop. `probes/cropimg.ps1` is the matching crop. Both are plain PowerShell, so neither costs an editor round trip.

Second methodology result, and it invalidates a measurement round 15 reported: **`ExecuteAlways` `Update` does not run in this editor while the application is in the background, even with `set_autotick` on at 16 ms.** `MirageSubject` spawns its Zoe on a guarded FIRST `Update()` tick (deliberately, and its own doc comment explains why it cannot be `Start()` or `OnEnable()`), so with the editor unfocused the subject exists as a bare `Transform` + `MirageSubject` with **no renderer, no children, nothing to draw** — for minutes. Round 15's "three renders of the rig camera 3.4 s apart returned one frame hash, so they only animate in Play" is consistent with having measured an empty picture rather than a still one. Two reflection-driven `Update()` calls spawned `Preview Shooter` immediately. **A probe that concludes "nothing moves" from a background editor is measuring the editor, not the tool.**

---

## 2. Round 15's landings, by eye — nobody had seen any of them after commit

### 2.1 Mirage's "Open preview stage" row, all three states — verified by eye

One fixed row, `Display PPU 16 · Add Previewable · Browse Sprite… · Open preview stage`, in the same place in every state; only the button's enabled state and the row tooltip change, so nothing moves under the cursor.

| state | how it was reached | what is on screen |
|---|---|---|
| **openable** (stage exists, no rig in the open scene) | demo scene open, window bound to a scratch view | button **enabled**; tooltip *"Opens Assets/Mirage/MirageStage.unity, the scene whose MirageRig renders this view…"* — `shots/mirage-openable.png` |
| **already open** | the window's **own button pressed** | button **greyed**; tooltip *"Already open: the scene "MirageStage" carries the MirageRig…"* — `shots/mirage-stageopen.png` |
| **no stage in the project** | `AssetDatabase.MoveAsset` renamed `MirageStage.unity` out of the way (GUID and `.meta` preserved, reversed immediately after, `git status` clean) | button **greyed**; tooltip *"No preview stage in this project…"* — `shots/mirage-nostage.png` |

**The button itself was pressed**, not simulated — but only after reloading the demo scene from disk first. That matters: `OpenStageScene` calls `SaveCurrentModifiedScenesIfUserWantsTo()`, and the demo scene was dirty from earlier rounds' probes, so pressing it cold would have raised the modal save prompt this card forbids. Reloading the scene from disk discards that dirt (saving nothing) and makes the real button press dialog-free. After the press: active scene `Assets/Mirage/MirageStage.unity`, `MirageRig` found, window rebuilt.

### 2.2 The preview actually renders after the button opens the stage — verified by eye and by probe

Added `PreviewShooterZoe` as a previewable, handed the view to the rig, and looked:

- **By probe:** the rig's own preview camera renders **1024 lit pixels of 163840**, hash `C0F839C5` (`shots/mirage-rig.png` — a blue figure on the rig's clear colour). Before the subject spawned it was `lit=0`, hash `E1671DC5`.
- **By eye:** the editor's **Game view** shows the previewable — `shots/mirage-gameview.png`, cropped out of a full capture of the main window, with the Mirage HUD panel drawn beside it, and the Mirage window in front reading `Previewables (1) · PreviewShooterZoe @ (0, 0) · Ping · Remove` with `Open preview stage` correctly greyed (`shots/c4.png`).

This closes round 14's finding 2.6 end to end: the route is reachable from the window, the press works, and something appears. **No human has done it.**

### 2.3 The Zoe Clip picker, fresh and composite — verified by eye

| character | what is on screen |
|---|---|
| **a fresh Zoe** (`Assets/Zoetrope/AuditT325Zoe.asset`, created for this) | Hit and Death each show `Clip [None declared ▾]` — a **greyed dropdown, not a text field**; `choices=1`, `enabled=False`, tooltip *"No animations to choose from: this character has no View yet…"*. Both **▶ preview buttons greyed**, tooltip *"Nothing to preview yet: this character declares no animations, so the throwaway spawn would be invisible…"*. `Custom events (0)` reads *"None declared — this character plays only Hit and Death."* — `shots/zoe-fresh.png` |
| **ProtoGuy** (a `CompositeLauminaryView`, the case that used to fall through to a typed string) | `Clip [(none) ▾]` **enabled, 6 choices** (5 declared names + `(none)`); both ▶ **enabled** with real tooltips; the duration line reads *"No clip — pick one above…"*, which is the wording that is only correct when there IS something to pick — `shots/zoe-protoguy.png` |

`ZuiAudit` on the fresh Zoe: `elements=298 drawn=144 controls=44 captionShort=1 overflowParentX=0 offWindow=0 noTooltip=0 inertNoReason=0`. The one short caption is Unity's own `+` glyph on the `ListView` footer inside the sanctioned reorderable-list island (2.7 px) — trivia, not ours.

### 2.4 Pyre's Proper Blast Size input, long value and at rest — verified by eye and by probe

On a copy of the shipped `Proper Blast`:

| state | measurement | seen |
|---|---|---|
| `Size 1.366666` | numfield **63.56 px**, text needs 53.3 in a 53.3 box — fits exactly | `shots/pyre-size.png`: the value sits inside the slider, nothing over the edge |
| typed down to `1.5` through the field's own value setter | numfield back to **45.78 px** — the 46 px resting width T-0314's row arithmetic is written against | `shots/pyre-size-after.png` |

Two things checked while there and found NOT to be defects: the whole Pyre window contains exactly **one** raw `SliderInt` (the transport scrubber, deliberately left by round 14 as T-0260 Q12) and **zero** raw `Slider`/`MinMaxSlider`/`EnumField`/`PopupField`/`DropdownField`/`Toggle`; and the `Centre` control that reads as a thin horizontal slider is the **collapsed thumbnail** of a `ZuiValue2DControl` (120×18, click to expand), whose keyboard-nudge tooltip is honest in both states because `OnKeyDown` is on the outer control, not the plot.

### 2.5 The Add Previewable popup's elided names — verified by probe, NOT by eye, and it left one real defect (§3.1)

The popup could not be photographed this round: a `PopupWindow` closes or hides the instant anything else activates, and this session's desktop changes hands every few seconds (§1). Four attempts, including arming the press on an `EditorApplication.update` hook so it fired while the editor was foreground, produced captures of the Cupcakehour editor, of a browser, and of the Mirage window with the button visibly focus-ringed but no popup drawn. **Stated as not verified by eye rather than narrated around.**

Measured instead through the shipped code path (`MirageAssetPicker.FindAll` → `LauAssetGridGUI.Elide` at the browser's real 104 px cell): 48 items, **16 names wider than the old 92 px label, 5 wider than the cell, all 5 now elided with a real ellipsis, none hard-cut**. Round 15's landing holds. What it did **not** fix is finding 3.1.

---

## 3. The findings

### 3.1 A picker showed three different assets under one identical name — fixed

Round 15 found that 16 of the 47 names in the `Add Previewable` browser were cut mid-word, "three to the identical visible string", and fixed the cutting. Measuring the result at HEAD shows the second half of that sentence survived the fix: **`Directional Grenade Blast 1 Plus`, `Directional Grenade Blast 2 Plus` and `Directional Grenade Side Blast Plus` all draw as `Directional Grenade…`** — three rows of a picker that a reader cannot tell apart, in the one control whose entire job is "choose which of these". Round 15's own comment names it and sends the reader to the tooltip; a tooltip you must hover three times to disambiguate a list is not the picker working.

Swept over **every ScriptableObject name in the project** (421 names, 29 too wide for the 104 px cell), because the grid is shared by every LauAsset browser and picker:

| where the ellipsis goes | names elided | names colliding with another row |
|---|---|---|
| at the **tail** (round 15) | 29 | **15**, of which 6 are assets that genuinely share a full name → **9 caused by the truncation** |
| in the **middle** | 29 | **8**, the same 6 genuine duplicates → **2 caused by the truncation** |

Both cut exactly the same 29 names, so nothing is lost; keeping both ends recovers 7 of the 9. It works because this project names assets family-first with the variant at the END (`… Blast 1 Plus`, `… Side Blast Plus`, `Old School Explo 2 Plus`, `UniversalRenderPipelineGlobalSettings`), which is also why the tail was the worst half to throw away. Measured after, on the Add Previewable list: `Directiona…ast 1 Plus` / `Directiona…ast 2 Plus` / `Directiona…Blast Plus` — **truncation-caused collisions 3 → 0**; the one collision left is two assets both actually named `New Pyre Plus`, which no truncation can fix.

`Editor/AssetKit/LauAssetGridGUI.cs:207-227` (`Elide`). Compiled clean.

### 3.2 The composite fix stopped one level short: every picker BELOW the clip list was empty on ProtoGuy — fixed

Round 15 taught `GetClipNameOptions` to union a composite character's parts. Everything that resolves a **named clip** still went through `FindAnimationByName`, which reads `view.version` — and a composite has none. Measured through the window's own helpers, on the character the demos are built around:

| ProtoGuy (`CompositeLauminaryView`) | before | after | what the parts actually declare |
|---|---|---|---|
| clip list | 5 | 5 | `LegsWalk_N/E/S`, `LegsIdleRotation`, `UpperAimRotation` |
| `GetFrameCount(first clip)` | **0** | **8** | 8 and 16 frames per clip, on both parts |
| `GetPointLayerIds` | **0** | **1 — `Waist`** | a Point layer `Waist` and a Vector layer `Muzzle` on `UpperAimRotation` |
| `AllFrameEventNames` | 0 | 0 | genuinely none authored |

So on a composite character the **On Frame** trigger picker was bounded to zero frames — the exact thing its own comment says it exists to prevent ("an author picks a frame that EXISTS instead of typing a number into the dark") — and the FX **Layer** picker and the whole **Cues** section offered nothing, while the data was sitting on the parts. Nothing in the window said so; the clip picker worked, so everything downstream looked merely un-authored.

Fixed by teaching the lookup the same trick the clip list already knew: `FindAnimationByName` → `FindAnimationsByName`, which yields matches from the view AND recursively from every part, and the three callers union what they find (`GetFrameCount` takes the largest, the other two de-duplicate) — necessary because ProtoGuy's Legs and Upper both declare all five clip names.

`Editor/Zoetrope/ZoetropeWindows.cs` — `FindAnimationsByName`, `GetFrameCount`, `GetEventNames`, `GetPointLayerIds`.

### 3.3 `Muzzle Event Name` was the last typed reference on the weapon card — fixed

A FrameEvent name, typed into a raw `Z.TextInput`, sitting **directly under** `Muzzle Layer Id`, which is a proper picker built from the same character's own animations. The owner is not merely knowable, the code four lines above already enumerates it, and `AllFrameEventNames(view)` exists in the same file. A misspelling here compiles, saves, looks authored and fires nothing — the rule's own failure mode.

Now the same three-part picker as the layer field beside it: `(none)`, the declared names, and an authored value that no longer resolves kept and marked `(unresolved)` rather than silently dropped. When the character declares no FrameEvents at all it is a **greyed picker reading "None declared"** with the reason and what would fill it — not a text field, because "degrading to a text field when the option list is empty is the failure mode, not the graceful fallback".

Verified by eye on ProtoGuy (`shots/c16.png`): `Attach To Part [Upper ▾]`, `Muzzle Layer Id [Muzzle (Vector) ▾]`, `Muzzle Event Name [None declared ▾]` greyed — three pickers in one column where one used to be a type-in box.

`Editor/Zoetrope/ZoetropeWindows.cs` — `BuildWeaponSlots`.

### 3.4 The rig anchor's `MetaLayer Id` was typed — fixed

`BuildAnchorRow` drew `MetaLayer Id` as a text field whose tooltip *taught the user the syntax* — "The painted point's layer id (e.g. \"Waist\")". A tooltip giving an example of what to type is the tell: the value is a reference, and the guide's own worked example for it is a laumination's meta-layers.

Each side of a composite connection resolves against a **different** part's animation (parent side = the parent part's, child side = this part's), so each side now gets its own picker derived from that part's view — all modes offered and labelled with theirs (`Muzzle (Vector)`, `Waist (Point)`), because the anchor resolver reads a layer's painted frames whatever mode it was painted in. Measured on ProtoGuy: 2 candidates per part. Empty state is the same greyed "None declared" with its reason, and it names the two modes that need no painting at all (Pivot, Edge).

`Editor/Zoetrope/ZoetropeWindows.cs` — `BuildRig`, `BuildAnchorRow`, new `AllMetaLayers`.

### 3.5 A picker drew a value the asset does not hold — fixed, and it was live on a shipped demo

`StringDropdown` did `Mathf.Max(choices.IndexOf(prop.stringValue), 0)`. `IndexOf` returns −1 for a stored value the option list does not contain, and clamping that to 0 draws **the first option instead** — a control whose entire job is to say which value is stored, quietly showing a different one. With an empty list it drew the plain text `(none authored)`, which hides the stored value completely.

It is reachable by ordinary authoring, not a corner case: a reaction's layer list is scoped to its **current clip**, so changing the clip strands whatever was picked under the old one. And it was live: ProtoGuy's Hit reaction stores `metaLayerId = "Muzzle"` with no clip picked, so the `Layer` field read `(none authored)` — the authored value simply not on screen. It now reads **`Muzzle (unresolved)`** (measured; `shots/zoe-weapons.png`).

Fixed by keeping an unmatched value in the list and marking it, and by giving the empty case the same greyed "None declared" picker the Clip control uses — one concept, one look, instead of a third rendering of "there is nothing here".

`Editor/Zoetrope/ZoetropeWindows.cs` — `StringDropdown`.

### 3.6 What the sweep cleared

Every drawn text input in **eighteen** open tool windows was enumerated with its caption and effective tooltip (`out/textsweep-*.txt`), each window bound to a real asset of its own type, sections forced on. Everything else that draws a text box is a **declaration** and correctly typed: a Lathe node/solid name, a Pyre/Shaper/Tapestry/Lazor layer name, a Shaper light name, a Chunks blast label and Code Event hook name (whose own comment states the rule), a Cartographer new-layer name beside a `MiniRadio` of the existing ones, a Zoe display name, a Zoe custom-event id, a TextSplash line, a new-asset/rename name, a view name, a download URL — plus `ListView` size fields and the numeric sub-fields of native sliders, which are not names at all. **The Zoe window now contains exactly three text inputs and all three are declarations.**

One typed reference is left in the package and is **not** mine to fix: SpriteFx's Relight modifier types a MetaLayer id (`value='Muzzle'`, *"Which MetaLayer (Point or Vector mode) supplies this light's live position"*). A SpriteFx stack is authored on its own, with no owner to derive the list from — the genuinely ownerless case the card names, already open as **Q14 on T-0260**. Not re-asked.

### 3.7 A new TextSplash landed loose in `Assets/` — fixed

Seventeen of the eighteen asset windows declare their own folder (`Assets/Chunks`, `Assets/Lathe`, `Assets/Mirage`, `Assets/Cartographer/Levels`…). `TextSplashWindow.DefaultFolder` was `"Assets"`, so the New row's own sentence — composed from that value — read *"Creates `Assets/<name>.asset` — this tool's default folder, since no TextSplash is open"*, calling the project root a tool folder. Caught by walking it: `AuditT325SplashA.asset` was written into `Assets/` beside the render-pipeline settings.

Now `Assets/TextSplash`, which `AssetLibrary.Create` makes on demand; no existing asset moves. Verified by re-walking it cold: *"Creates `Assets/TextSplash/<name>.asset`"*, and the asset landed there.

`Editor/TextSplash/TextSplashWindow.cs:30`.

### 3.8 Two findings handed to the owner rather than built — posted on T-0260 as Q17 and Q18

- **Q17, Chunks:** only **88 of the project's 788 sprites** are Read/Write enabled, and the Cut Preview's Subject picker offers all 788. Pick one of the other 700 and the stage answers *"Enable Read/Write on this sprite's import settings to preview."* with nothing to press — the user leaves the tool, finds the importer, comes back. The window knows the sprite and its importer, so a one-click fix is derivable, but that is a **new control**, which programme rule 6 forbids me to build. Measured live; three options offered on the card.
- **Q18, Chunks control choice:** 18 range dials are `Z.MinMax` (a slider flanked by two typed fields) while the dials beside them in the same card are embedded MicroSliders — one card, two looks. The layout rules prefer `Z.MicroMinMax`, round 11 already migrated Pyre's identical `Life (frames)`, and Chunks' own comments justify `Z.MinMax` as "a short control" — written before MicroMinMax existed. But `Size 0.01–2` and `Life 0.05–8` are exactly the typed-precision case the rule carves out, so it is a per-dial judgement across a whole tool. Not touched.

---

## 4. The two cold walks, twice each

### 4.1 Chunks — a spec from empty to a visible burst source

Opened from its own menu item (`Laubrary/Chunks`) with every tool window closed first.

| step | walk 1 | walk 2 |
|---|---|---|
| the empty state | `[None (Chunk)] [Save greyed] [New] [Browse]`, `Chunk library (7)` with **all 7 thumbnails resolving**, Duplicate/Rename/Delete correctly absent — `shots/chunks-empty.png` | identical |
| **New** | opens the inline `Asset name [ ] [Create] [Cancel]` row; its tooltip names the destination: *"Creates Assets/Chunks/<name>.asset — this tool's default folder, since no Chunk is open."* | identical |
| type + **Create** | `Assets/Chunks/AuditT325ChunkA.asset`, bound | `…ChunkB.asset` |
| the fresh spec | 496 elements / 399 drawn / 84 controls, **audit clean on every counter**; Tags, Slicer(off), Blasts, Movement(off), Formation(off), Layer Stack, Splash(off), Timeline(off), Emission, Physics, Life/Look, Floor, Sampled, Cut Preview, Animated, Hits, Trail — `shots/chunks-fresh.png` | byte-identical counts |
| Cut Preview, empty | `Subject [None (Sprite)] [Resample]` and a stage that says *"Pick a Subject sprite (or set a Sample Source) to preview slicing."* — the affordance is named, not hunted | identical |
| pick a Subject through the window's own field | first try hit **finding Q17** (an unreadable sprite: *"Enable Read/Write…"*) | — |
| pick a readable one, press **Resample** | **the book is sliced — yellow cut outlines over the artwork, `19 × 46 px · zoom 2 · 8 screen px each`, and six real cut pieces fill the strip** (`shots/c22.png`) | same, a different random cut (`shots/c24.png`) |
| **Preview in Mirage**, pressed for real | creates a view, adds the spec as a previewable (`Previewables (1) · AuditT325ChunkA @ (0,0)`) and opens the stage scene | — |
| the burst itself | **does not fly in Edit mode, and that is documented and deliberate**: `MirageChunkBurst` declines outside Play mode because `Chunk`, `ChunkModuleRunner` and `PyreBlastPlayer` are not `ExecuteAlways`, and firing would strew frozen debris that never returns to the pool. The HUD says so in words. Not a defect; the walk correctly ends here. | — |

One probe-method note worth keeping: on walk 2 the Resample button sat at y=1431 in a 900pt window and the press did nothing. Round 11's rule again — **scroll it into view first**; after `ScrollTo` the same press worked.

### 4.2 TextSplash — a text from empty to a played-through fly-in

| step | walk 1 | walk 2 (after the 3.7 fix and a recompile) |
|---|---|---|
| open from `Laubrary/Text Splash`, empty | `[None (TextSplash)] [Save greyed] [New] [Browse]` + `TextSplash library (1)` drawn as a **name chip with no empty thumbnail square** — round 14's landing 2.1, re-verified after commit (`shots/splash-empty.png`) | identical |
| New → type → Create | `Assets/AuditT325SplashA.asset` — **finding 3.7** | `Assets/TextSplash/AuditT325SplashB.asset` |
| the fresh splash | 707 elements / 378 drawn / 111 controls, **audit clean on every counter** | identical |
| type a line, press **▶ Play** | the button becomes `⏸ Pause`, `_playing=True`, and `_scrub` runs **0.94 → 2.62 → (wraps) 0.85 → 3.24** over ~5.5 s — it plays through and repeats | `1.10 → 0.38`, same behaviour |
| **the picture** | `ROUND SIXTEEN` drawn in the stage mid-flight, white with a black border, transport reading `t 1.70 / total 3.40s` (`shots/c27.png`) | `WALK TWO` mid-flight with the letters staggered — the per-letter fly-in visibly in progress (`shots/c28.png`) |

---

## 5. Temporal and regression

**The shipped demo document plays.** `Assets/Demos/ShaperDemo/ShaperDemoDoc.asset`, played through the transport's own button, sampled ≥1.5 s apart: `frame 9/16` → `frame 5/16` → `frame 2/16`, three distinct frames, then paused at `frame 1/16` with the button back to `▶ Play`. `ZuiAudit` at every sample **while playing** and again **paused**: `captionShort=0 overflowX=0 offWindow=0 noTooltip=0 inertNoReason=0`. With every section forced on the window is `elements=1320 drawn=906 controls=252`, matching round 11's 1319/905/252 — nothing this round changed Shaper's shape.

**The fill sweep is identical row for row.** Round 15's probe re-run against HEAD (`out/fill-sweep-t325.tsv`, 414 rows), diffed on `(doc, kind, card, control, state)`:

| baseline | regressions (was > 0, now 0) | rows missing |
|---|---|---|
| `T-0324/out/fill-sweep-t324.tsv` (414) | **0** | 0 |
| `T-0323/out/fill-sweep-t323.tsv` (414) | **0** | 0 |
| `T-0277/fill-sweep-after.tsv` (408) | **0** | 0 |

Round 14's §0 trap was live again and was avoided: the sweep LOADS `Assets/Shaper/Audit0277/rfield0277.asset` and never creates it, so `probes/p1-rfield.cs` makes the 16×16 `RFloat` first. Without it the sweep reports a false regression on `Pyramid / Solid / Fill / heightFieldScale`, as it did in rounds 12 and 14.

---

## 6. Files touched

| file | what |
|---|---|
| `Editor/AssetKit/LauAssetGridGUI.cs` | a cell name elides from the MIDDLE, so a picker's rows stay distinguishable (measured: truncation-caused collisions 9 → 2 across the project, 3 → 0 in the Add Previewable browser) |
| `Editor/Zoetrope/ZoetropeWindows.cs` | `FindAnimationsByName` walks a composite's parts, so frame counts, point layers and frame events resolve on a composite at all; `Muzzle Event Name` and the rig anchor's `MetaLayer Id` are pickers instead of type-in boxes; `AllMetaLayers` added; `StringDropdown` keeps and marks a stored value its list does not contain, and uses the same greyed "None declared" empty state as the Clip control |
| `Editor/TextSplash/TextSplashWindow.cs` | `DefaultFolder` is `Assets/TextSplash`, like all seventeen sibling windows, instead of the project root |

**No Pyre runtime or form file, no `CHANGELOG.md`, not committed** (ShaperHarmony rule 3). Four compiles this session, all `completed, failed=false, errors=[]`; the last one is the state the tree is in. `scriptCompilationFailed=False`, `isPlaying=False`. Play mode was never entered; the Test Runner was never run; no tag was created.

## 7. State left behind

`git status` shows exactly the three source files above plus what was already modified before this session (`.mcp.json`, `Assets/Pyre/Green Lantern.asset`, the TextSplash demo border font, the untracked `Assets/Shaper/`, `Assets/_Recovery/` and the `Samples~` block). Nothing else.

Everything this task created was deleted through `AssetDatabase.DeleteAsset` (which removes the `.meta`): `Assets/Mirage/AuditT325View.asset`, `Assets/Pyre/AuditT325Pyre.asset`, `Assets/Zoetrope/AuditT325Zoe.asset` **and its generated `AuditT325Zoe.states.cs`**, `Assets/Chunks/AuditT325ChunkA.asset` and `…ChunkB.asset`, `Assets/AuditT325SplashA.asset`, `Assets/TextSplash/AuditT325SplashB.asset`, `Assets/Shaper/Audit0277/rfield0277.asset`, and the folders `Assets/Shaper/Audit0277`, `Assets/TextSplash/Border Fonts`, `Assets/TextSplash` and `Assets/Border Fonts`.

**A generated sibling worth recording for the next pass, in the same family as round 14's `.states.cs` note: authoring a TextSplash writes `Border Fonts/<name> (<font>) Border Font.asset` beside it.** Walk 1's splash therefore created `Assets/Border Fonts/` at the project ROOT (finding 3.7's second cost), and walk 2's created `Assets/TextSplash/Border Fonts/`. Both are gone.

`Assets/Mirage/Test 2.asset` came out of the session marked modified — content-identical, only line endings (`git diff --numstat` empty) — and was restored with `git checkout --`. Round 15 hit the same file the same way. `Assets/Shaper` holds only the two untracked `New Shaper*.asset` files that were there at session start: **not this task's, and not deleted.**

A project-wide scan at cleanup found **0 dirty ScriptableObjects under `Assets/`**. The demo document ends `dirty=False` and was never saved; the demo scene is the open scene, `dirty=False`, and was never saved. Prefs: `ZuiSectionToggleBar.ShaperWindow.userSel` restored verbatim to `Views=1;Canvas=0;Layers=0;Shape=0;Fill=0;Swarm=0;SpriteFX=0;Lights=1;Tags=0` with `barMode` true; `ZuiSectionToggleBar.ZoeWindow.*` deleted (measured `<unset>` at session start); `T320.capWin`/`T320.capOut`/`T324.win` put back; every `T325.*` and the stale `T324.find`/`T324.t0`/`T324.rigOut` deleted; **`T0312.out` restored LAST**, after the final dumping probe. `Undo.ClearAll()`, console cleared. The open window set is exactly what this session found (Mirage, Zoe, TextSplash, SpriteFx, Larder, Lathe, Pyre, Shaper).

**Nothing is left pinned over the user's desktop.** The by-eye channel worked by making one window temporarily `HWND_TOPMOST`; every capture cleared it again and a final sweep confirms **no topmost window belongs to this editor's process**. No other application was minimised, moved or closed — which is the difference from rounds 11 and 14, and deliberate: this session's obstructions were another agent's editor and a terminal the user was typing in.

## 8. Verified how

**By probe, in the live editor:** every number in §2–§5 — the stage row's three states and their tooltips; the rig camera's 0 → 1024 lit pixels and its two frame hashes; the fresh Zoe's disabled dropdowns and disabled ▶ with their reasons, and ProtoGuy's 6-choice populated ones; Pyre's Size numfield at 63.56 px and back to 45.78 px, and the native-control census that found exactly one raw `SliderInt` in the whole window; the 48-item elide measurement and the 421-name tail-vs-middle comparison; the composite derivation before and after (frameCount 0 → 8, point layers 0 → 1) and ProtoGuy's parts' own declared frames/layers; the text-input census over eighteen bound windows; both cold walks end to end with their audits; the three play samples and the paused audit; the 414-row fill sweep against three baselines; four compiles; the asset/pref/window cleanup and the project-wide dirty scan.

**By eye, in real captures of the running editor:** Mirage's stage row enabled, greyed-because-open and greyed-because-absent; the previewable rendering in the Game view; the fresh Zoe's `Clip [None declared]` and greyed ▶, and ProtoGuy's populated `Clip [(none)]`; ProtoGuy's weapon card showing three pickers where one was a text field; Pyre's `Size 1.366666` inside its slider and `1.5` at rest; Chunks' empty library with seven resolving thumbnails, a fresh spec, the Cut Preview's honest empty state, the Read/Write dead end, and the sliced book with six real cut pieces on both walks; TextSplash's library as a name chip with no empty square, and `ROUND SIXTEEN` / `WALK TWO` mid-flight in the stage.

**Not verified:**

1. **No *human* has operated any of this.** Every press was a synthesized pointer or key event — the same code path a real one takes, not the same hand.
2. **The `Add Previewable` popup was still never seen** (§2.5). Four attempts; a `PopupWindow` hides the instant anything else activates and this desktop changes hands every few seconds. The elision is measured through the shipped code path instead.
3. **A Chunks burst was never seen flying.** It is documented Play-mode-only and this session did not enter Play mode.
4. **`ExecuteAlways` behaviour was measured with the editor in the background**, where it does not tick. Everything that depends on it (a Mirage previewable spawning) had to be driven by hand, so "how long it takes to appear for a real user" is not measured.
5. **The two owner questions (Q17, Q18) are measured but unanswered**, and Q12–Q16 were not re-opened.
6. **The windows never audited at all** are unchanged from round 14's list: Cartographer, the Lauminary Browser, Lazor, Tapestry, Choreographer, BackSplash, Zounds, the Rules Editor, the Brain/Story graphs, Dashboard, the Sprite Catalog. This round OPENED most of them for the text-input census, but only that census — no geometry audit, no walk.
7. **The reference sweep is bounded by what DRAWS.** A control behind a collapsed card, an unauthored list or a mode the demo assets do not use is invisible to it; the rig anchor's `MetaLayer Id` (finding 3.4) is exactly that case and was found by reading, not by the sweep.
8. **No window was walked below 820 px wide.** Rounds 12–15 said this and it is still true.

### Trivia (named, not fixed)

- Unity's own `+` glyph on a `ListView` footer measures 2.7 px short (`need 12.4 / have 9.8`) inside the sanctioned reorderable-list island — not ours, and one glyph.
- Chunks' Cut Preview reserves six cut-piece slots that are empty until Resample runs. They fill on the first press and the stage beside them says what to do, so the LauAsset blank-thumbnail rule does not bite.
- The Zoe reaction's duration line reads as advice in the empty state ("No clip — this character's View declares no animations yet…"). It is a permanently-reserved single line whose TEXT changes and whose job is to answer "how long is this event", so it is a readout, not an instruction paragraph; left alone.

---

## 9. Verdict

Sixteen passes in, a full pass still turns up things that matter. The largest is that round 15's own composite fix stopped one level short: the clip picker learned to read a composite character's parts, and everything that resolves a named clip did not — so on ProtoGuy, the character the demos are built around, the On Frame picker was bounded to zero frames and the point-layer picker said nothing was authored while a `Waist` layer sat on the parts. Beside it, two more typed references survived in the same window round 15 was cleaning, a picker was drawing a value the asset does not hold on a shipped demo asset, a picker collapsed three different assets onto one visible name, and a new TextSplash landed loose in `Assets/`. Two further items are measured and handed to the owner rather than built.

**FOUND: 8 non-trivial items, next pass needed**
