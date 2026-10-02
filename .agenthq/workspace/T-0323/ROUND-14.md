# T-0323 — the fourteenth full pass (2026-09-09)

Worktree `D:\UNITY\Laubrary Dev - Shaper`, branch `feat/shaper`, HEAD `e76b5040` at session start (rounds 11–13 already committed), editor on port 7801. `Application.dataPath` confirmed as `D:/UNITY/Laubrary Dev - Shaper/Assets` at the head of the session and again at cleanup. Probes in `workspace/T-0323/probes/` (T-0320's `zlib.cs` / `cap2.cs` / `click.cs` / `crop.cs`, T-0322's `zbind.cs`, T-0321's fill sweep — **every hard-coded output path re-pointed at `T-0323` before running, and checked afterwards**), dumps in `out/`, captures in `shots/`.

**This pass found 7 non-trivial items and fixed 6**, and the seventh is a missing affordance the programme's own rules forbid me to build. The two that matter most were found the same way as rounds 11–13's headline items — by operating the thing rather than by walking its element tree:

- **Four of the eight asset browsers reserve a thumbnail square that never fills.** Lathe 4 of 4, SpriteFx 5 of 5, TextSplash 2 of 2, Zoe 1 of 7 — measured through each window's own `Thumb()`. The LauAsset rule names this exact case: *"an empty thumbnail slot is pure cost — it takes the width, adds nothing, and tells the reader 'this thing has a picture and it failed to load'."* Thirteen passes had never opened four of those browsers.
- **A reflected dial's caption is a field name, and nobody sized the control for it.** SpriteFx's Outline modifier drew `Inner Softness` and `Inner Softness Curve` as two dials whose captions both elided to `Inner Softness …` — the word that told them apart was the one that got cut. Nine such captions across seven modifiers, none of them reachable until you add that modifier, which nobody had.

---

## 0. Methodology notes the next pass should keep

| note | why |
|---|---|
| **`EditorApplication.delayCall` does NOT fire in this editor. `EditorApplication.update` does.** Measured directly: a `delayCall` registered in one eval was still `pending` several evals and minutes later, while TextSplash's `_scrub` (an `update` hook) advanced 22.15 → 0.97 → 4.68 across the same interval. **Any window that refreshes through `delayCall` therefore looks frozen to a probe.** `MirageWindow.RebuildBody` (`MirageWindow.cs:177`) does exactly that, so adding a previewable left the window reading `Previewables (0)` — which is a probe artefact, not a defect, and would have been reported as one. Drive such a window by re-binding it (`SetAsset` → `Rebuild`) or by invoking the deferred body directly. |
| **A press only reaches a control that is on screen — and `ScrollTo` is the fix.** Round 11 stated the rule; `probes/g3-scrollpress.cs` implements it (walk up to the enclosing `ScrollView`, `ScrollTo(button)`, then press in the NEXT eval). Two Lathe buttons read as dead until this was added. |
| **A button that "does nothing" may be opening a separate `EditorWindow`, not a `ZuiPopover`.** Mirage's `Add Previewable`, TextSplash's `Recall…` and the BackSplash pickers all open a `PopupWindow`, which a panel-scoped popover count cannot see. Count `Resources.FindObjectsOfTypeAll<EditorWindow>()` before and after. Three would-be findings died here. |
| **Copying a `Zoe` asset generates a sibling `.states.cs`.** `Assets/Shaper/AuditT323Zoe.states.cs` appeared beside the copy and had to be deleted with it. Check for generated siblings when cleaning up scratch assets, not just `.meta`. |
| **`AssetDatabase.SaveAssets()` inside a bake writes EVERY dirty asset in the project.** `WareBaker.Bake` calls it, so pressing Larder's `Bake this Ware` silently persisted the unsaved dial edits its own toolbar was still offering a `Save` button for. Harmless here (only scratch copies were dirty) but worth knowing before pressing a bake with real edits in flight. |
| **T-0277's fill-sweep probe still LOADS an RFloat height field it never creates.** Round 12 said so and it is still true: create `Assets/Shaper/Audit0277/rfield0277.asset` (16×16 `TextureFormat.RFloat`) first or the sweep reports a false regression on `Pyramid / Solid / Fill / heightFieldScale`. It did, again, on the first run this session. |

---

## 1. What was checked

### 1.1 Round 13's landings, by eye

| landing | verdict |
|---|---|
| **the label-column rule on Lathe** (T-0322 §2.1/2.2) | **Verified by eye and by probe.** At 820: `Position X 0 Y 0 Z 0`, `Rotation …`, `Scale …` — three rows, X/Y/Z on one line each, the Z field wholly inside the pane. The one-glyph labels measure **11.1pt for 8.0pt of glyphs** (they were 50pt), the Z component starts at x=227.1 in a 292.9pt row, and Lathe's `overflowParentX` is **3 → 0**. |
| **Larder's MicroSliders and labelled radios** (T-0322 §2.10) | **Verified by eye.** Eight MicroSliders — `Width 0.92`, `Height 0.85`, `Palette 2`, `Label width 0.7`, `Band count 1`, `Resolution 48`, `Damage stages 3`, `Pixels/unit 48` — and seven labelled radio rows: `Kind`, `Shape`, `Fill`, `Label`, `Corners`, `Bands`, `Spots`. Nothing in the window still draws slider-plus-field. Audit clean on every counter. |
| **Pyre's preview with no frame readout over the picture** (T-0322 §2.8) | **Verified by eye** at 900px on a real blast: the canvas carries the artwork and nothing else; the only readout is `frame 21/26` in the transport below, beside `Zoom`/`Fit`/`Speed`/`Delay`. |
| **the Aseprite window's greyed Sync and relocated instruction** (T-0322 §2.5/2.6) | **Verified by eye and by probe.** `Sync from Aseprite` is disabled and says *"Nothing to sync yet — this animation has no owned .aseprite. Press \"Edit in Aseprite\" first; that promotes it and makes this live."*; the round-trip contract now lives on `Edit in Aseprite`'s tooltip. The populated window draws **7 text elements** and not one of them is an instruction paragraph. |
| **the AssetKit New row with nothing open** (T-0322 §2.7) | **Verified live, on a different window than round 13 used** — Lathe, unbound: *"Creates Assets/Lathe/&lt;name&gt;.asset — this tool's default folder, since no Lathe is open."* on the label, the field and `Create`. So it is composed from the state, not special-cased for Cartographer. |
| **the one asset name that still truncates** (T-0322 §2.3) | **Measured and half-closed — see 2.3.** Still 1 of 17 drawn ObjectFields. It cannot be closed by sizing: the field already takes **every pixel its row has** (301.8 of a 372.9pt content box, the rest being its own label), the name needs 372, and TextSplash's left column is a fixed 387pt that does **not** grow with the window (measured at 900 and at 1500 — identical). What WAS wrong and is now fixed is that the overflow was a silent hard cut. |

### 1.2 Live buttons, pressed

The gap every pass has declared. Every live button in the six named windows was pressed through the window's own path, on scratch assets, with the bound asset's serialized JSON, the project's asset count and the window's own state sampled before and after.

| window | action buttons pressed | wrote / did something | dead |
|---|---|---|---|
| **Larder** | `Randomize seed`, `Randomize whole Ware`, `Bake this Ware`, `Bake variation grid` | **4 of 4** — seed and full randomize both changed the asset; Bake wrote 3 PNGs beside the spec; the grid wrote 18 more into `Variations/` | 0 |
| **Lathe** | `+ Add solid`, solid `Duplicate`, solid `×`, `Show Move Gizmo`, `Animate Texture`, `Second Texture Layer`, `Emit Light`, `Cap Ends`, `+ Add modifier` → `Twist`, `Change…`, `❚❚ Pause`, toolbar `Duplicate`, toolbar `Rename` | **13 of 13** — every one changed the asset, the window, or a window field (`playing` False, `showGizmo` True) | 0 |
| **SpriteFx** | `New`, `Create`, `Cancel`, `Play`, `+ Add SpriteFx` → `Outline` / `Dissolve` | **all** — Play sets `_previewPlaying`, installs an update hook and flips its own caption to `Stop`; the picker adds a real modifier (json 704 → 3436) | 0 |
| **TextSplash** | `▶ Play`, `Reset`, `▲`, `Recall…`, `Save…` | **5 of 5** — Play advances `_scrub` and becomes `⏸ Pause`; `▲` collapses the 2D editor (707 → 692 elements); `Recall…`/`Save…` open a `PopupWindow` | 0 |
| **Zoe** | `+ New event`, `+ Add Weapon`, `+ New cue`, `+`, `+ Add effect ▾` → `Zound` | **5 of 5** — every one grew the asset's JSON | 0 |
| **Mirage** | `Add Previewable`, `Browse Sprite...`, `Ping`, `Remove`, `Add clip` → `Shoot` | **5 of 5** — `Add Previewable` opens a browser offering 48 assets; `Remove` deleted an entry; `Add clip` opened the Zoe's own declared-state list | 0 |

**Not pressed, deliberately:** `Bake Sprite Strip…` (Lathe) opens a modal `EditorUtility.SaveFilePanel`, which wedges the editor — instead the method behind the dialog was called directly and returns a real **1536×64** strip (24 frames of a 64px canvas). `Delete` in every toolbar opens a confirm dialog and was not pressed at all.

**No live button in any of the six was inert.** The three that first read as dead were probe artefacts, each named in §0.

### 1.3 The two cold walks, twice each

**SpriteFx stack — empty to a visible effect on a sprite.**

| step | walk 1 | walk 2 (after a domain reload) |
|---|---|---|
| close every stack window, open from `Laubrary/SpriteFx Stacks` | binds nothing; the empty state is the library | identical |
| the empty state, by eye | `[None (SpriteFx Stack)] [Save greyed] [New] [Browse]` + `SpriteFx Stack library (5)` — **and five blank squares: finding 2.1** | after the fix: **five name chips, full names, no squares** |
| `New` → type → `Create` | `Assets/SpriteFx/AuditT323Walk1.asset`, bound; the fresh stack shows Preview / timeline / Stack | `AuditT323Walk3.asset`, identical |
| the fresh document, by eye | `Preview on [None (Sprite)]`, a stage placeholder, `Play`/`Reverse`, an Active/Gap timeline, `Seed 12345`, `+ Add SpriteFx` / `Paste` greyed | identical |
| pick a sprite through the window's own field | `Legs-E`; the status line changes to *"A picked sprite — this stack is not on any Zoe event yet."* | identical |
| `+ Add SpriteFx` → the menu | 3 groups, ~43 effects, panel **490×457 inside a 900×880 window** — round 11's popover clamp holds here too | identical |
| pick one | `Outline` — **the leg sprite gains a white outline, seen in a capture** | `Dissolve` — **the leg sprite comes apart, seen in a capture** |

**Mirage — spawn a Zoe, raise one declared state.**

| step | walk 1 | walk 2 |
|---|---|---|
| close, open from `Laubrary/Mirage` | empty state: `Mirage View library (3)`, three thumbnails resolving | identical |
| `New` → `Create` | `Assets/Mirage/AuditT323Walk2.asset` — and the Create tooltip named that folder before the press | `AuditT323Walk4.asset`, identical |
| the fresh view, by eye | `Display PPU 16`, `Add Previewable`, `Browse Sprite...`, a Backsplash box, `Previewables (0)` + *"No previewables yet — Add Previewable above."* | identical |
| `Add Previewable` | opens a browser popup offering **48** assets | identical |
| add a Zoe | `PreviewShooterZoe @ (0, 0)` appears in the list | — |
| select it | an `Entry` section: Content, Position, Scale, Zoe options, Clips, Fire direction, Choreography | — |
| **raise a declared state** | `Add clip` opens a menu of the Zoe's OWN declared lauminations — `Idle`, `Shoot` — and picking `Shoot` writes step `1. Shoot` with `Fire Weapon` / `Trigger` / `Layer: Muzzle` | — |
| **see it play** | **could not** — see finding 2.6 | — |

### 1.4 Temporal, on the real demo document

`Assets/Demos/ShaperDemo/ShaperDemoDoc.asset`, played through the transport's own button, sampled three times ≥1.5 s apart, then paused:

| t | readout | lit pixels | frame hash |
|---|---|---|---|
| 40.6 s | `frame 14/16` | 1773 | `4E697BCF` |
| 43.8 s | `frame 15/16` | 249 | `21E231A1` |
| 45.4 s | `frame 12/16` | 3295 | `2C12AF6B` |

Three distinct frames, three distinct pictures. `ZAudit` ran at every sample **while playing** — `captionShort=0`, `overflowParentX=0` each time — and again **paused**: Shaper `elements=1320 drawn=818 controls=233`, every counter **0**. Pyre paused shows `captionShort=1` (a `0` value label 1.8px short — round 11 §1.4's known one-frame artefact) and `overflowParentX=1` (finding 2.7). The demo document finished `dirty=False` and was never saved.

### 1.5 The regression sweep

T-0321's fill sweep re-run verbatim against HEAD (`out/fill-sweep-t323.tsv`, 414 rows) and diffed on `(doc, kind, card, control, state)`:

| baseline | regressions (was > 0, now 0) | newly alive | rows missing |
|---|---|---|---|
| `T-0277/fill-sweep-after.tsv` (408 rows) | **0** | 51 (the `Bag (2 members)` Fill card, exactly as rounds 12 and 13 report) | 0 |
| `T-0322/out/fill-sweep-t322.tsv` (414 rows, round 13's own) | **0** | 0 | 0 |

Nothing rounds 11–14 changed killed a dial. Round 13's TSV and this one are identical row-for-row.

### 1.6 Audits after the edits

Nine windows re-audited with every section and box forced open after all six fixes and three clean compiles:

| window | elements / drawn / controls | captionShort | overflow-X | off-window | no tooltip | inert w/o reason |
|---|---|---|---|---|---|---|
| Shaper (demo doc, paused) | 1320 / 818 / 233 | 0 | 0 | 0 | 0 | 0 |
| Pyre (Proper Blast copy) | 889 / 657 / 195 | 1 ¹ | 1 ² | 0 | 0 | 0 |
| Lathe (New Lathe) | 424 / 340 / 80 | 0 | 0 | 0 | 0 | 0 |
| Larder (Ware copy, randomised) | 208 / 163 / 52 | 0 | 2 ² | 0 | 0 | 0 |
| SpriteFx (**every one of the 42 modifier kinds on one stack**) | 2787 / 1969 / 618 | **9 → 1** ³ | 1 ⁴ | 0 | 0 | 0 |
| TextSplash (Splash Demo copy) | 707 / 380 / 111 | 1 ⁵ | 0 | 0 | 0 | 0 |
| Zoe (PreviewShooterZoe copy) | — browser | 1 ⁶ | 0 | 0 | 0 | 0 |
| Mirage (fresh view) | 121 / 87 / 23 | 0 | 0 | 0 | 0 | 0 |
| Aseprite launcher (populated) | 20 / 20 / 4 | 0 | 0 | 0 | 0 | 0 |

¹ round 11 §1.4's one-frame value-label artefact, 1.8px on a single digit.  ² finding 2.7.  ³ finding 2.2; the survivor is explained there.  ⁴ the SpriteFx preview's deliberate, documented margin overhang — see "not verified" item 4.  ⁵ finding 2.3's residual, now elided rather than cut.  ⁶ a browser cell name at 96.9 of 109.8, already ellipsised by `.zui-cell__name`.

---

## 2. The findings

### 2.1 Four of eight browsers reserve a thumbnail square that never fills — fixed, systemically

Measured through each window's own `Thumb()` over every asset of its type:

| browser | items | resolve a thumbnail | blank |
|---|---|---|---|
| **Lathe** | 4 | **0** | 4 |
| **SpriteFx stack** | 5 | **0** | 5 |
| **TextSplash** | 2 | **0** | 2 |
| **Zoe** | 7 | 6 | 1 (`Hit Event Palette Demo` — a Zoe with no view, so `PreviewSprites()` is empty) |
| Shaper / Pyre / Larder / Mirage | 3 / 10 / 10 / 3 | all | 0 |

`SpriteFxSpec` and `LatheSpec` implement no preview path at all and their windows override no `RenderThumbnail`; `TextSplash` DOES declare `IVisualPreview` and its `RenderPreviewTexture()` is `return null;` with a comment saying a real thumbnail is "a later slice" (`Runtime/TextSplash/TextSplash.cs:994`). In every case `ZuiAssetWindow.BuildCell` added the `zui-cell__thumb` box regardless, so the library drew a row of empty grey squares with names under them — the picture in `shots/fx-empty.png`.

The LauAsset rule decides this and its two halves are a pair: a visual asset must always resolve, **a non-visual one must not reserve the slot at all**, "so a list of them reads as a list of names rather than a column of empty boxes". Whether a TYPE has a picture is a property of the type, not of one asset, so the decision is made **once per browser**: if nothing in the library resolves, the grid is name chips; if some do, every cell keeps its slot so the one that failed still reads as a failure. That keeps Zoe honest (it has six real thumbnails and one hole) and turns the other three into readable lists.

Measured after: Shaper 3 cells / 3 thumb boxes and Zoe 7 / 7 unchanged; Lathe, SpriteFx and TextSplash now 0 thumb boxes and 4 / 6 / 2 name chips. Verified by eye — `shots/fx-empty2.png` shows six full names, no squares, and the names no longer clipped to the 104px cell width they only had to line thumbnails up.

`Editor/AssetKit/ZuiAssetWindow.cs` (the browser build loop + `BuildCell`), `Zui/Toolkit/ZuiToolkit.uss` (`.zui-cell--nothumb`, and the selected state moved onto it).

### 2.2 Two dials in one card whose captions both elided to the same words — fixed, systemically

SpriteFx's `Outline` modifier draws `Inner Softness` and `Inner Softness Curve` side by side, and at the 150px default a MicroSlider's caption box is 93.3px, so the second one elided to `Inner Softness …` — **the word that distinguished the two dials was the one that got cut**. Same for `Outer Softness` / `Outer Softness Curve`. Swept properly by putting **one instance of all 42 SpriteFx modifier kinds on a single stack**: **9 clipped captions across 7 modifiers** — `Inner Softness Curve` ×2 (109.8 of 93.3), `Outer Softness Curve` ×2 (112.4), `Replacement Smoothing` (127.1), `Cell Shade Strength` (104.0), `Vortex Persistence` (98.2), `Child Shed Chance` (99.6), `Spread Progress` (86.2 of 72.0).

These are REFLECTED field names: nobody chose a width for them, and the ≤13-character caption heuristic cannot govern a name the drawer reads off a field. T-0257's principle already answers it — widen the control rather than shorten the name back into jargon (Shaper's `Depth between layers` is 190 by hand, `Pixels per unit` 175, Lathe's `Turntable frames` 170). This makes that automatic: `ZuiMicroSlider.FitCaption(floor)` measures the drawn caption after layout and grows the dial to hold it, never past the room its parent gives, never shrinking one that fits. It is wired into the two paths where a caption is a field name — `ZuiValueControl`'s Static body and `ZuiReflect`'s three direct MicroSlider branches — and deliberately NOT into hand-written call sites, whose widths someone chose.

One subtlety cost a compile and is worth recording: **the room a dial has is not its immediate parent's width.** `ZuiValueControl` wraps its slider in a `Z.Row` that shrink-wraps to the slider, so the first version measured `room == own` and grew nothing (captionShort stayed 9). The fix walks up past shrink-wrapping ancestors — one that follows its child imposes no constraint — to the first that is genuinely wider.

Measured after: `captionShort` **9 → 1** on the 42-modifier stack; `Inner Softness Curve` now 168.0 wide with 111.6px of caption box for 109.8px of text. Every other window re-audited unchanged. The survivor is `Spread Progress` on a **folded (animated)** value, whose track is deliberately narrower than its static twin (`controlWidth − BadgeSize − BadgeGap`) so it reads as one of the sliders beside it; that comment is explicit and was left alone.

`Zui/Toolkit/ZuiMicroSlider.cs` (`FitCaption`), `Zui/Toolkit/ZuiValueControl.cs:277`, `Zui/Toolkit/ZuiReflect.cs` (3 call sites).

### 2.3 An asset name too long for its field was cut mid-word with nothing to say so — fixed

Round 13 made `Z.Object<T>` grow to the name it is showing. One field still does not fit: TextSplash's `Baked font`, showing `Splash Demo (LiberationSans SDF) Border Font (TMP_Font Asset)`, needs 372px and has 263.1.

The card asked to fix it or say why not. **Why not, measured:** the field is already at its maximum. Its row's content box is 372.89pt, of which the `Baked font` label takes 68; the field resolves to 301.8 and its display label to 263.1. Widening the window does not help — TextSplash's left column is a fixed 387pt and measured **identical at 900 and at 1500** — and moving the label onto its own line would win ~70px of the 109 needed while breaking the label column. No arrangement inside this pane shows a 43-character generated name.

What WAS fixable is that UITK's default `text-overflow: clip` hard-cut it: the name simply stopped mid-word, with nothing on screen saying it continued. It now elides, which is the same promise `.zui-chip__label` already makes for the same content ("an asset name is arbitrarily long and it TRUNCATES rather than pushing the rest of the row off screen"). Measured after: `textOverflow` `Clip` → `Ellipsis`, geometry unchanged, all eleven windows re-audited clean.

`Zui/Toolkit/ZuiToolkit.uss` (`.unity-object-field-display__label`).

### 2.4 Mirage says "This Zoe has no weapons configured" three times on one screen — fixed

On a Zoe with no weapons, the entry drew: a body paragraph in `Zoe options` (*"…(add one to Zoe.weapons, not here)."*), a second body paragraph in `Clips` (*"…— Fire Weapon toggles below are disabled."*), and the disabled `Fire Weapon` toggle's own tooltip, measured live: *"This Zoe has no weapons configured."* Round 11 found a box saying its own name three times; this is the sentence version.

Only one of the three is sanctioned. The labelling rule sends an explanation to the tooltip of the control it describes, and that tooltip is already there and already correct — so the `Clips` banner is body text the reader re-reads on every visit to say what the greyed control beneath it says on hover. Removed. The `Zoe options` line is kept: it explains a control that is **absent** (the Weapon picker), and an absent control has no tooltip to carry it.

`Editor/Mirage/MirageWindow.cs:836`.

### 2.5 Lathe's transport spent a whole row restating the frame — fixed

`Frame [———●———] [14]` on one row and `frame 14/24` on the row below it — the same scalar, twice, 20px apart, in a transport that is only three rows tall. Shaper does this in one row and says why in its own comment (`ShaperWindow.cs:1507`: *"the 'frame N/M' readout goes LAST in the row (variable-width content last), and is the one place that answers 'where is playback'"*), and Pyre packs its readout into the transport's wrap row. Lathe was the odd one of three sibling tools, and the layout rules' first pre-flight line is that vertical space is the scarce resource.

The readout now trails the scrubber in its own row. Verified by eye: `Frame ——●—— [8]  frame 8/24`, one line, and Lathe's transport is two rows instead of three.

**Deliberately NOT changed:** the scrubber is still a thumbed `Z.SliderInt` inside a `Z.Field`, which is the pre-MicroSlider look the control-choice rule names — but that is exactly round 12's **Q12** on T-0260 about Pyre's identical scrubber, and answering it here for Lathe would pre-empt the owner for all three tools. The packing fix stands on its own.

`Editor/Lathe/LatheWindow.cs:345`.

### 2.6 Mirage has no route to its own preview — NOT fixed, the owner's call

The cold walk went: open Mirage, `New`, `Create`, `Add Previewable`, pick a Zoe, select it, `Add clip`, pick the declared state `Shoot`. Every step was reachable and every one worked. Then **nothing appears anywhere**, because Mirage renders through a `MirageRig` that lives in a scene — measured: 0 `MirageSubject` and 0 `MirageHud` in the open scene, and the rig lives in `Assets/Mirage/MirageStage.unity`, which exists in this project and which the window neither opens nor mentions on screen. The only place that says so is a section *tooltip*: *"A MirageRig in the open scene shows them live…"*.

This is the Handover Walk's own definition of a missing feature: *"a step that requires hunting…, knowing a magic string, or being TOLD by you, is a missing feature, not a workflow."* And the answer already exists one folder over — **`Editor/Chunks/ChunkWindow.MiragePreview.cs:69`, `EnsureMirageStageOpen()`**, which finds `MirageStage t:Scene`, offers to save the current scene and opens it, for exactly this reason ("the live preview needs a MirageRig, which lives in the stage scene. Without it the button would drop you into a…"). Mirage's own window, the one whose entire job this is, has no equivalent.

**Not built here**: ShaperHarmony rule 6 says no new controls, surface it instead. The shape is settled by the precedent — the same `EnsureMirageStageOpen` behaviour on Mirage's own window, wired to opening a view rather than to a new button, would need no new control at all. It needs an owner's yes.

### 2.7 A MicroSlider's numeric input clips an authored value — NOT fixed, reason stated

`ZuiMicroSlider`'s optional numeric input is a fixed **46px** `FloatField`; its text element measures 39.56px of content box and clips at about six characters. Round 12 measured the same thing in Chunks and logged it as trivia because *"no authored value in the project reaches that length"*. That is no longer true, and it took pressing things to find out:

- **Pyre, shipped data**: `Assets/Pyre/Imported/Proper Blast.asset`'s `Size` is `1.366666` — 57.78px of text in a 39.56px box, **18.2px over**, measured on the paused window.
- **Larder, its own button**: `Randomize whole Ware` writes full-precision floats, so `Width` came back `0.5712263` (24.9px over) and `Height` `0.774174` (15.6px over) the moment its own action button was pressed.

**Why not fixed:** the 46px is load-bearing beyond this control. `.zui-microslider__caption--reserve` reserves exactly `right: 46px` for it, and T-0314's row arithmetic for every reflected card is written against "a MicroSlider's caption ends 56.9px short of its own width (6px inset plus the 46px value reserve)". Widening the input, or rounding the displayed value, changes a number three separate layout rules depend on across every tool — that is its own task with its own before/after sweep, not a side effect of this one. Measured and handed over rather than half-done.

---

## 3. Files touched

| file | what |
|---|---|
| `Editor/AssetKit/ZuiAssetWindow.cs` | a browser reserves a thumbnail slot only when its type actually has a picture; a library without one is a list of names |
| `Zui/Toolkit/ZuiToolkit.uss` | `.zui-cell--nothumb` (name chip + selected state); an ObjectField's asset name elides instead of hard-cutting |
| `Zui/Toolkit/ZuiMicroSlider.cs` | `FitCaption` — a dial grows to hold a caption nobody sized it for, capped by the first ancestor that is genuinely wider |
| `Zui/Toolkit/ZuiValueControl.cs` | the Static body's label-inside slider fits its caption |
| `Zui/Toolkit/ZuiReflect.cs` | the three reflected MicroSlider branches do the same |
| `Editor/Mirage/MirageWindow.cs` | the third copy of "this Zoe has no weapons configured" is gone; the greyed toggle's own tooltip keeps it |
| `Editor/Lathe/LatheWindow.cs` | the frame readout trails the scrubber in its row instead of taking one of its own |

**No Pyre runtime or form file, no `CHANGELOG.md`, not committed** (ShaperHarmony rule 3). Three compiles this session, all `completed, failed=false, errors=[]`; the last one is the state the tree is in. `scriptCompilationFailed=False`, `isPlaying=False`. Play mode was never entered; the Test Runner was never run.

## 4. State left behind

Every asset this task created was deleted through `AssetDatabase.DeleteAsset` (which removes the `.meta`): `Assets/Shaper/AuditT323{Ware,Lathe,Lathe 1,Fx,Splash,Zoe,Mirage,Pyre}.asset`, the generated `AuditT323Zoe.states.cs`, the three `Can_*_dmg*.png` that `Bake this Ware` wrote, `Assets/Shaper/Variations/` (36 files from `Bake variation grid`), `Assets/Shaper/Audit0277/` with its `rfield0277.asset`, `Assets/SpriteFx/AuditT323Walk1.asset` and `AuditT323Walk3.asset`, `Assets/Mirage/AuditT323Walk2.asset` and `AuditT323Walk4.asset`. `Assets/Shaper` holds only the two untracked `New Shaper*.asset` files that were there at session start — **not this task's, and not deleted**, same as rounds 11–13. No folder was left empty and no tracked `.meta` was deleted (checked against `git status`).

`git status` shows the seven source files above and nothing else of this task's. `Assets/Pyre/Green Lantern.asset` and the TextSplash border-font asset were already modified in the tree before this session and were not touched. **No sprite `.meta` was rewritten** — the Laumination Builder was never opened and `Edit in Aseprite` was never pressed. A project-wide dirty scan at cleanup found **0** dirty ScriptableObjects; `ShaperDemoDoc` finished `dirty=False` and was never saved. No tag was created.

`ZuiSectionToggleBar.ShaperWindow.userSel` restored verbatim to `Views=1;Canvas=0;Layers=0;Shape=0;Fill=0;Swarm=0;SpriteFX=0;Lights=1;Tags=0` with `barMode` true. Every `T323.*` pref deleted, plus the stale `T320.cap*` / `T321.*` / `T322.*` keys and `Shaper.lastView`; `T0312.out` put back to `workspace/T-0312/out`. Larder, Lathe, SpriteFx, TextSplash, Zoe, Mirage, Pyre and the Aseprite launcher closed; the Shaper window's title restored to `Shaper` and its preview paused. `Undo.ClearAll()`, console cleared. The open window set is exactly what it was at session start.

**One cross-round file was written and put back.** A final Lathe audit ran after the `T0312.out` pref had already been restored, so it wrote `workspace/T-0312/out/audit-LatheWindow`. T-0312's own dumps all carry a `.txt` extension and none was overwritten; the stray file was **moved** to `workspace/T-0323/out/audit-LatheWindow-after` and T-0312's folder is byte-for-byte what it was. Round 13 lost round 12's sweep this way; this is the same trap one pref away, and the lesson is that the output-path pref must be restored **last**, after the final probe, not before it.

**The Microsoft Store window is still minimised.** That is what keeps the by-eye channel working; leave it.

## 5. Verified how

**By probe, in the live editor:** every number in §1 and §2 — Lathe's X/Y/Z label widths and its 3 → 0 overflow; Larder's eight MicroSliders and seven radios; the Aseprite window's two button tooltips in both states and its 7-text-element census; the Create-row tooltip on an unbound Lathe; the 17-ObjectField census and the Border Font field measured at 900 and 1500; the per-window `Thumb()` sweep (4 of 8 browsers blank) and the cell/thumb-box counts before and after; the 42-modifier stack's 9 → 1 clipped captions and the widened dial's geometry; the caption-fit room walk; the 39 live-button presses with their JSON hashes, asset counts and window fields; `LatheBaker.BakeStrip` returning 1536×64; the `EditorWindow` count before and after each browser-opening button; the `delayCall` starvation test against the advancing `update` hook; both cold walks end to end; the three play samples with frame hashes and lit counts; the playing-and-paused audits; the 414-row fill sweep diffed against two baselines; three compiles; the asset/pref/window cleanup and the project-wide dirty scan.

**By eye, in real captures of the running editor:** Lathe at 820 with `Position X 0 Y 0 Z 0` tight inside the pane, and its transport before and after the readout move; Larder's eight MicroSliders and seven labelled radios; Pyre at 900 with a clean canvas and one transport readout; the Aseprite launcher with `Sync from Aseprite` greyed and no instruction paragraph; the SpriteFx library's five blank squares and then its six name chips; a fresh SpriteFx stack; the same stack with `Outline` and then `Dissolve` visibly changed on a real sprite; Mirage's empty state and its fresh view; a full-desktop capture at cleanup.

**Not verified:**

1. **No *human* has operated any of this.** Every press was a synthesized pointer or key event — the same code path a real one takes, not the same hand.
2. **The browser popup that `Add Previewable` opens was never seen.** A `PopupWindow` is created (window count 16 → 17) and it reports 48 assets, but a full-desktop capture taken with it open showed nothing, and its `position` reads `(0,0,456,510)`. The most likely reading is that a `PopupWindow` closes on lost focus and my capture eval is what closed it — but that is a guess, and "the picker draws where you expect" is **not** verified.
3. **Mirage's preview was never seen playing** — finding 2.6 is exactly why. The walk got as far as a declared state raised by name; nothing rendered because no rig exists in the open scene, and opening `MirageStage.unity` would have swapped the demo scene out from under the session.
4. **SpriteFx's preview image deliberately hangs outside its stage** (a 294×294 buffer in a 194×194 frame box, 50.7px each side) and the audit flags it. Read the code before treating it as a defect: it is documented as the honest picture ("the border IS the sprite's own boundary… the overflow simply extends past the stage"), unlike Pyre's undocumented case that round 12 fixed. Left alone. Whether a large margin could reach the `Play`/`Reverse` buttons ~90px to its right was **not** tested.
5. **`ZuiAssetWindow.Thumb` has no blank-texture guard**, while the older `LauAssetGridGUI.GetThumbnail` does (it drops a fully transparent preview and seeks a visible frame, because "the Pyre blast Proper Blast renders 0 opaque pixels out of 4096 at its first frame"). Measured: no asset currently returns a transparent-but-non-null thumbnail to a `ZuiAssetWindow`, so nothing is wrong today — but the two renderers disagree, and the new "does this type have a picture at all" test would be fooled by one.
6. **Q12–Q16 on T-0260 were not re-opened**, and 2.5 deliberately stops short of Q12.
7. **The four windows this pass never touched** — Cartographer, Chunks, the Laumination Builder, the Lauminary Browser — were not re-audited, and Lazor, Tapestry, Choreographer, BackSplash, Zounds, the Rules Editor, the Brain/Story graphs, Dashboard and the Sprite Catalog have still never been audited at all.
8. **No window was walked below 820px wide.** Round 12 said this and it is still true.

---

## 6. Verdict

Fourteen passes in, the first pass that actually pressed things found that four of the eight asset browsers have been drawing rows of empty squares for a rule the guide states in two mandatory halves, that a whole family of reflected dials cannot be told apart because their captions elide to the same words, and that the one window this programme was asked to cold-walk cannot show a preview at all without a scene nobody is told to open. None of those is visible in an element tree, and none of the six windows' live buttons had ever been pressed before today. Against that, every live button named in §1.2 works, the fill sweep is identical to round 13's row for row, and every window audits clean.

**FOUND: 7 non-trivial items, next pass needed**
