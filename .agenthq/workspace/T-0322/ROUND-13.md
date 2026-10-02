# T-0322 — the thirteenth full pass (2026-09-09)

Worktree `D:\UNITY\Laubrary Dev - Shaper`, branch `feat/shaper`, HEAD `5e00f8d6` at session start (rounds 11 + 12 already committed), editor on port 7801. `Application.dataPath` confirmed as `D:/UNITY/Laubrary Dev - Shaper/Assets` at the head of the session and again at cleanup. Probes in `workspace/T-0322/probes/` (T-0320's `zlib.cs` / `cap2.cs` / `click.cs` / `crop.cs` and T-0321's `t321-fillsweep.cs` reused; `zbind.cs` added), dumps in `out/`, captures in `shots/`.

**This pass found 7 non-trivial items and fixed 7**, and put 3 owner questions on T-0260. Two of the seven are systemic ZUI defects reaching every tool, and one of those is the sharpest kind this programme can find:

- **The UI Guide states a rule as machine-enforced that does not exist in the code.** "`ZuiLabelAlign` … the rule is now enforced in ZuiLabelAlign itself (a field aligns only when nothing sits to its left on the same line; `zui-align-force` opts a repeating card header back in)". Neither the check nor the class exists anywhere in the tree. Measured consequence: **29 labels across 5 windows padded to a column that is not there**, including Lathe's `X`/`Y`/`Z` at 50pt for 8pt of glyphs, which hung the Z field **70.7px off its own row**.
- **Pyre printed the current frame twice, one of them painted white over the picture.** `PyreWindow.Preview.cs` drew a `frame N/M` overlay inside the canvas while `PyreWindow.cs:770` already put the identical string in the transport 90pt below it. Shaper's stage has never drawn one; Lathe's does not either.

The by-eye channel from round 11 §0 worked unchanged. The Microsoft Store window is still minimised and must stay so.

---

## 0. Methodology notes the next pass should keep

| note | why |
|---|---|
| **`ZBind(win, path)` beats hand-rolled binding.** Every `ZuiAssetWindow` binds through `SetAsset` + `Rebuild` found by walking the base chain; `probes/zbind.cs` wraps that plus a generic `ZOpen`. Six previously-unaudited windows were opened and bound with two probes instead of six. |
| **Measure before believing a capture.** Three things that *looked* like defects in a screenshot and were not: SpriteFx's `Light position` "878×26 pad" is a `ZuiValue2DControl` in its collapsed 120px thumb state; TextSplash's `t` transport "number field" is already a `ZuiMicroSlider` whose fill was at 0; the two `Ease` radios that wrap at different points differ by ~1px of flex slack, not by a layout bug. Each cost one probe and would have been a false finding in the report. |
| **A `Z.Field(label, …, Z.Row(Z.Field("X"…), …))` is the shape that trips label alignment.** The inner fields have the outer label to their left, so they are mid-row by the guide's own definition, and there is no column for them to join. |
| **Cleanup gotcha: an empty folder can be tracked.** Deleting the scratch Level emptied `Assets/Cartographer/Levels`, and `AssetDatabase.DeleteAsset` on the now-empty folder deleted a **committed** `Levels.meta`. Check `git status` for a `D` on a `.meta` after any folder cleanup. |
| **I overwrote round 12's sweep output.** A `sed` meant to redirect `t321-fillsweep.cs`'s hard-coded output path did not match (backslashes), so `workspace/T-0321/out/fill-sweep-t321.tsv` now holds **this** session's 414-row run, not round 12's. That folder was never committed, so the original is gone. The file is a re-derivable measurement and round 12's report quotes its conclusions, but it is a real loss and it is mine. My copy is at `workspace/T-0322/out/fill-sweep-t322.tsv`. |

---

## 1. What was checked

### 1.1 Round 12's landings, by eye

| landing | verdict |
|---|---|
| **Pyre's preview clipped at Zoom 6** (T-0321 §2.7) | **Verified by eye.** At Zoom 6 on a 900px window the picture stops at the island's edge and `❚❚ Pause`, `Frame`, `Strip`, `GIF…`, `GIF scale`, `GIF dither`, `Bake`, the Frame scrubber, `Zoom`/`Fit`/`Speed`/`Delay`, the whole `Preview backdrop` box and `Cherry Framing` are all clean and unobscured. No stray rule anywhere. |
| **Laumination Builder on `LegsWalk_N`** (T-0321 §2.8) | **Verified by eye.** `Sheet Legs-N-walk 448×56px`, `2 · Identify Sprites`, `3 · Canvas` with the eight walk frames, `4 · Sprite Palette (8)` populated, `5 · Animation — sequence (8)`. Audit: 387 / 308 / 74, every counter 0. |
| **a folded reflected card keeps its width** (T-0321 §2.5) | **Verified by probe and by eye.** Explosive Jet's folded `Fracture 2` measures **323.1**, identical to all twelve of its open siblings; the capture shows it as a full-width card in the stack, not a stub. |
| **the Views bar with no store** (T-0321 §2.1, T-0310) | **Verified by eye and by probe.** `Apply`/`Update`/`Delete view` greyed with *"No view is saved yet — type a name and press Save as to make one."*, `Rename view` with *"No view is selected to rename."*, `Save as` with *"Type a name for the new view above first."*, and the caption reads **`Rename view`**, not `Rename`. |
| **the Views bar after one Save-as** | **Verified by eye and by probe.** `choices=[AuditT322]`, value set at once; `Apply`/`Update`/`Delete view` live with their own tooltips; `Rename view` and `Save as` correctly re-greyed because Save-as clears the name field. Store + `.meta` and `Shaper.lastView` deleted afterwards. |

### 1.2 The six windows this programme had never audited

Each opened cold, captured empty, bound to a **copy** of a real asset under `Assets/Shaper/Audit*`, every section forced open, walked and captured again.

| window | state | elements / drawn / controls | captions short | overflow-X | off-window | no tooltip | inert w/o reason |
|---|---|---|---|---|---|---|---|
| **Cartographer** | empty (0 Levels exist) | 41 / 19 / 5 | 0 | 0 | 0 | 0 | 0 |
| **Cartographer** | first-run level, 8 sections | 253 / 216 / 52 | 0 | **1** ¹ | 0 | 0 | 0 |
| **Larder** (Ware_02_Crate copy) | 7 sections | 234 / 199 / 58 | 0 | 0 | 0 | 0 | 0 |
| **Lathe** (New Lathe copy) | 7 sections | 423 / 345 / 80 | 0 | **3** ² | 0 | 0 | 0 |
| **SpriteFx stack** (ProtoGuy Fire Flash copy) | 3 sections | 324 / 222 / 59 | 0 | 0 | 0 | 0 | 0 |
| **TextSplash** (Splash Demo copy) | 11 boxes | 707 / 378 / 111 | **2** ³ | 0 | 0 | 0 | 0 |
| **Aseprite launcher** | empty | 10 / 9 / 1 | 0 | 0 | 0 | 0 | 0 |
| **Aseprite launcher** (ProtoGuy / LegsWalk_N) | populated | 24 / 24 / 4 | 0 | 0 | 0 | 0 | **0 — but see 2.5** |

¹ finding 2.4  ² finding 2.2  ³ finding 2.3

Two empty states are exemplary and needed no change: Cartographer's (`Level library (0)` · *"No Level assets yet — hit New to make one."* · Save greyed, New/Browse live) and the Aseprite launcher's (`Lauminary [None]` · *"Pick a lauminary to edit one of its draft animations in Aseprite."*).

**A cold walk of Cartographer's main task, through the window's own affordances only:** press `New` → an inline `Asset name [ ] [Create] [Cancel]` row appears in the toolbar → type a name → `Create` writes `Assets/Cartographer/Levels/AuditT322Level.asset` and binds it → the Level / Generate / Layers / Palette / Props / Decals / Tool sections appear, one layer already present, `Edit in Scene view` + Paint/Erase/Line/Rect/Pick/Stamp/Decal offered. First run shows something and every next step is on screen. That walk is what produced findings 2.4 and 2.5's sibling (2.6).

### 1.3 Zoe and Mirage, DRIVEN

Round 12 captured these two but never drove them. Every drivable leaf (ZUI MicroSliders and ToggleButtons as well as the native fields round 12's Chunks driver covered) was perturbed through its own control API, with the bound asset's whole `SerializedObject` diffed after each change and the list re-queried every iteration.

| window | drivable leaves | wrote to the asset | did not | what the "did not" is |
|---|---|---|---|---|
| **Zoe** (`PreviewShooterZoe` copy) | 10 | **9** | 1 | the `Foldout` header `Toggle` of the loadout `ListView` — Unity's own chrome in the sanctioned reorderable-list island, correctly writes nothing |
| **Mirage** (`MirageDemo` copy) | 4 | **3** | 1 | the backdrop `Zoom` MicroSlider — window view state, not asset data (its own value moved and held) |

Neither window contains a radio group (a separate sweep clicked one non-selected option in every `zui-radio` and found 0 groups in both), so their surface is pickers, buttons and lists. **No authored asset was dirtied** — a project-wide dirty scan during the pass found only the two `Audit*` copies.

### 1.4 Temporal, on the real demo document

`Assets/Demos/ShaperDemo/ShaperDemoDoc.asset`, bound and played through the transport's own button, sampled three times ≥1.5 s apart:

| t | readout | lit pixels | frame hash |
|---|---|---|---|
| 3.7 s | `frame 6/16` | 4217 | `0DD6620F` |
| 12.1 s | `frame 4/16` | 3154 | `B785DFF7` |
| 15.4 s | `frame 5/16` | 3630 | `98346F0B` |

Three distinct frames, three distinct pictures — the preview animates. `ZAudit` ran at each sample **while playing**: `captionShort` 0 / **1** / 0. **Paused** and re-audited, Shaper and Pyre are both `captionShort=0, overflowParentX=0, overflowParentY=0, offWindow=0, noTooltip=0, inertNoReason=0`. That is round 11 §1.4's one-frame artefact of measuring a running preview, reproduced and confirmed — it is not a clip a user can see. The demo document finished `dirty=False` and was never saved.

### 1.5 The regression sweep

T-0321's `t321-fillsweep.cs` re-run verbatim against HEAD (414 rows) and diffed against `T-0277/fill-sweep-after.tsv` (408 rows) on (doc, kind, card, control, state):

- **REGRESSIONS (pixels > 0 then, 0 now): 0.**
- Newly alive (0 then, > 0 now): **51** — the whole `Bag (2 members)` Fill card, exactly as round 12 reported.
- Rows missing from the new run: **0**. Rows only in the new run: **6** — the `authored` flags T-0271 added.

Nothing rounds 11, 12 or 13 changed killed a dial.

### 1.6 The MicroSlider caption cap, measured rather than asserted

The programme's ≤13-character cap on a MicroSlider caption (T-0263, T-0269) was swept across every open window: **226 MicroSliders, 4 captions over 13**. All four measure `captionShort=0` — none clips — and three of them are T-0257's own deliberate remedy, which widens the control rather than shortening the name back into jargon: Shaper's `Depth between layers` (width 190) and `Pixels per unit` (175), Lathe's `Turntable frames` (170). The fourth, SpriteFx's reflected `Edge Threshold` (14), fits at its default. **Not changed** — the cap is a heuristic for the 150px default, and T-0257's principle governs the exceptions. Logged as trivia.

---

## 2. The findings, all fixed here

### 2.1 The alignment rule the guide says is enforced does not exist — fixed, systemically

`ui-layout-rules.md` states: *"The rule is now enforced in `ZuiLabelAlign` itself (a field aligns only when nothing sits to its left on the same line; `zui-align-force` opts a repeating card header back in)."* Grepped across the whole package: **there is no such check and no such class anywhere.** `Apply()` padded every `.zui-field__label` in a scope to the widest one, unconditionally.

Measured before, across the open windows: **29 labels sat mid-row and were padded past their own text** — Lathe `X`/`Y`/`Z` **50pt for 8pt of glyphs** (×6), Laumination Builder `Cols` 38/25, `Rows` 38/31, `Pad` 38/22, `Pivot` 38/28, `α >` 38/19, `± tol` 38/26 (13 in that window alone), SpriteFx `Mode` **91/32** and `Light Color` 91/62, TextSplash `Axis` 31/24 (×2), Pyre `Tint` 40/22. The guide's own words for why this is wrong: *"the padding just shoves the control away from its label … so a label read as belonging to the control before it."*

Its visible consequence was finding 2.2.

Fixed by adding the test the guide describes, over **laid-out geometry** — the guide is explicit that `resolvedStyle.flexDirection` cannot be trusted here — walking each label's ancestor chain up to the scope and looking only at each level's earlier hierarchy siblings, which is exactly "what came before me on this row" at depth × siblings cost rather than a full scan. A label that is packed mid-row is released to its natural width and excluded from the widest-label measurement; the release is unconditional so a label that moves between the two states cannot keep a stale width.

Measured after: **29 → 0** padded mid-row labels, every window still `captionShort=0` / `overflowParentX=0`. Labels only ever get NARROWER under this rule, so nothing can start overflowing because of it. Verified by eye in Lathe (`Position X 0 Y 0 Z 0` now tight and wholly inside the pane) and Larder.

`Zui/Toolkit/ZuiLabelAlign.cs`.

### 2.2 Lathe's Z field hung 70.7px off its own row — fixed by 2.1

`LatheWindow.Vector3Row` (`Editor/Lathe/LatheWindow.cs:298-305`) wraps three `Z.Field("X"/"Y"/"Z", …, Z.Float(…, 60f))` in a `Z.Row` inside an outer `Z.Field(label, …)`. With every one-glyph label padded to 50pt, each component measured 116.89 and the three needed 350.7 of a **292.89pt** row: `Position`, `Rotation` and `Scale` each spilled their Z component **70.7px** out over the divider, three times, at the 820px window.

No separate edit was needed — 2.1 alone took Lathe's `overflowParentX` from **3 to 0** and the row now fits. Verified by probe and by eye.

### 2.3 A reference field that could not show which asset it referenced — fixed, systemically

`Z.Object<T>` set a **fixed** `style.width` (default 200) and left it there. Measured in TextSplash: the `Baked font` field showed `Splash Demo (LiberationSans SDF) Border Font` in a 191.6px slot that needed **372** — two thirds of the name unreadable, on the one control whose entire job is to say WHICH asset is bound. The layout rules already say this in as many words: *"A fixed width is a starting point, not a check that content fits … when a field's content length varies at runtime, measure and size to fit rather than guessing."*

`width` is now a MINIMUM: the field measures the name actually drawn and grows to fit it, **never past the room its parent gives**, and carries `flex-shrink: 1` so a row that runs out of space shrinks it back instead of spilling. Census after, over all 17 drawn ObjectFields in eleven windows: **2 truncated → 1**, and the survivor grew 230 → 302 of the 372 it wants (52% of the name visible → 81%). No window gained an overflow — all eleven re-audited clean.

**Residual, stated rather than hidden:** that one 43-character generated name still does not fit, because the row it sits in is genuinely narrower than the name. The cap is the parent's own content box, deliberately, since a wider cap can push a field past its siblings. Left as measured.

`Zui/Toolkit/Zui.cs` (`Object<T>`).

### 2.4 Cartographer's layer row hung the Opacity slider over the divider — fixed

Measured on a first-run level at a 900px window: `Default tile · Sort · Opacity` needed **438.7px of a 396.4px** row, and the `Opacity` MicroSlider ran **42.2px** past the row's right edge, out over the split divider. The tile picker's group alone is 221.3 of the 396.4.

The card-layout rule already decides this: *"Only a WIDE control earns its own row … A LauAsset picker is wide by nature — give it a line rather than squeezing it beside pickers."* The tile picker (label + type icon + a name that varies with whatever is bound) takes the line; `Sort` and `Opacity` share the next. Measured after: `overflowParentX` **1 → 0**.

`Editor/Cartographer/CartographerWindow.cs:533`.

### 2.5 A greyed button that described its effect instead of its reason — fixed

The Aseprite launcher's `Sync from Aseprite` is disabled until the animation has been promoted, and its tooltip read *"Pull the edited .aseprite back into the lauminary's own source and re-bake."* — what it would do, never why it cannot. This is the exact failure zlib's own T-0318 note describes ("a tooltip that merely restates the control's normal effect scores identically to one that says why"), and the reason the mechanical `inertNoReason` counter read 0 on a window that had one. It now reads *"Nothing to sync yet — this animation has no owned .aseprite. Press \"Edit in Aseprite\" first; that promotes it and makes this live."*, and reverts to the action description once promoted.

`Editor/Launimator/AnimationAsepriteWindow.cs:70`.

### 2.6 A permanent instruction paragraph under two ordinary buttons — fixed

The same window ended with a `Z.Help` paragraph carrying the whole round-trip contract (*"Pixels only — authored events and meta-layers are preserved … Keep each sprite at its position on the Aseprite canvas …"*), re-read on every visit. The layout rules ban this outright, and round 12's reason for *sparing* the two Laumination Builder paragraphs does not apply here: these sit under two plain ZUI buttons that hover perfectly well, not under a bespoke IMGUI canvas with undiscoverable gestures. The sentence is now composed onto both buttons, which is what it is about.

`Editor/Launimator/AnimationAsepriteWindow.cs:62,70,87`.

### 2.7 The New row promised a folder "beside the one currently open" with nothing open — fixed

Round 12's §2.4 made the Create row name its destination, with a fixed sentence: *"Creates `<folder>/<name>.asset` — beside the `<T>` that is currently open."* `FolderForNew()` only puts it beside the open asset when there IS one and otherwise returns `DefaultFolder`, so on the **empty state** — which is exactly where `New` gets pressed most — the sentence was simply false. Caught on Cartographer's cold walk with 0 Levels in the project. A conditional tooltip has to read for the state it is in.

Now composed: with nothing open it reads *"Creates Assets/Cartographer/Levels/&lt;name&gt;.asset — this tool's default folder, since no Level is open."* Verified live in both states.

`Editor/AssetKit/ZuiAssetWindow.cs:333`.

### 2.8 Pyre printed the current frame twice, one of them over the picture — fixed

`PyreWindow.Preview.cs:138` painted `frame N/M` in white directly onto the preview canvas, bottom-left, on every repaint. `PyreWindow.cs:770` puts the identical string in the transport's own `frameReadout` **90pt below it**. Both were on screen at once — seen in a 1.5× crop — so the same value was stated twice, and one of the two covered the artwork the tool exists to let you judge, at every zoom, with no way to turn it off. Shaper's stage has never drawn one (it has exactly one readout, `RefreshFrameReadout`); Lathe's does not either (`LatheWindow.cs:377`), so Pyre was the odd one out of three sibling tools — the "be consistent across the codebase" rule.

The in-canvas overlay is gone; the transport keeps the readout. The **strip** view's own label is untouched — it says more than the transport does (`strip — N frames · frame X`). Verified by eye: one readout, clean picture.

`Editor/Pyre/PyreWindow.Preview.cs:138`.

### 2.9 A tooltip naming a button by a name it no longer has — fixed

The Views bar's name field said *"…or **Rename** to give the SELECTED view this name"*, while round 12 renamed that button to **Rename view** (its §2.3, to stop it colliding with the asset toolbar's own Rename). A tooltip that sends the reader to a control spelled differently is the same class of defect as the one round 12 fixed. Now *"…or Rename view to…"*. Trivial, and included because it is the tail of round 12's own landing.

`Zui/Toolkit/ZuiViewBar.cs:151`.

### 2.10 Larder was drawing the pre-MicroSlider look, and seven unlabelled radios — fixed

The first audit of this window found the flagship control-choice rule broken eight times and the labelling rule seven times:

- **Eight bounded scalars** (`Width`, `Height`, `Palette`, `Label width`, `Band count`, `Resolution`, `Damage stages`, `Pixels/unit`) were `Z.Field(label, …, Z.Slider(…))` — *"never a plain `Z.Slider` + an external value field (that's the pre-MicroSlider look; MicroSlider is the standard)"*. All eight are now `Z.MicroSlider`, the four whole-number ones with `decimals: 0` (which also gives them the whole-step arrow nudge T-0318 added). Every caption is ≤13 characters.
- **Seven `Z.MiniRadio` groups had no label at all** — three of them consecutively inside `Decoration`, so the reader saw `None | Triangle | Rounded | Cut` over `None | Horiz | Vert | Diag` with nothing saying what either one selects. The rest of the package wraps a radio in `Z.Field("Kind", …)` (Shaper's own fill kind is the pattern). They are now `Kind`, `Shape`, `Fill`, `Label`, `Corners`, `Bands`, `Spots`.

Measured after: 234 → 208 elements (the eight `Z.Field` wrappers collapsed into the sliders), audit clean on every counter. Verified by eye — the window now reads like the rest of the package.

`Editor/Larder/LarderWindow.cs:121-190`.

---

## 3. Files touched

| file | what |
|---|---|
| `Zui/Toolkit/ZuiLabelAlign.cs` | a label joins the column only when nothing sits to its left on the same line — the rule the guide already documents |
| `Zui/Toolkit/Zui.cs` | `Z.Object<T>` sizes to the asset name it is showing, capped by its parent, shrinkable |
| `Zui/Toolkit/ZuiViewBar.cs` | the name field's tooltip calls the button by its current name |
| `Editor/AssetKit/ZuiAssetWindow.cs` | the Create row's destination sentence reads for the state it is in |
| `Editor/Pyre/PyreWindow.Preview.cs` | no second frame readout painted over the picture |
| `Editor/Cartographer/CartographerWindow.cs` | the tile picker takes its own line; the two short dials share the next |
| `Editor/Larder/LarderWindow.cs` | eight MicroSliders instead of slider+field; every radio labelled |
| `Editor/Launimator/AnimationAsepriteWindow.cs` | the greyed Sync button says why; the round-trip paragraph moved onto the buttons it describes |

**No Pyre runtime or form file, no `CHANGELOG.md`, not committed** (ShaperHarmony rule 3). Four compiles this session, all `completed, failed=false, errors=[]`; the last poll reads `up_to_date`. `scriptCompilationFailed=False`, `isPlaying=False`. Play mode was never entered; the Test Runner was never run.

## 4. State left behind

Every asset this task created was deleted through `AssetDatabase.DeleteAsset` (which removes the `.meta`): `Assets/Shaper/AuditT322Pyre/Doc/Ware/Lathe/Mold/Fx/Splash/Zoe/Mirage.asset`, `Assets/Shaper/ShaperViews.asset`, `Assets/Shaper/Audit0277/` with its `rfield0277.asset`, and `Assets/Cartographer/Levels/AuditT322Level.asset`. `Assets/Shaper` holds only the two untracked `New Shaper*.asset` files that were there at session start — not this task's, and not deleted, same as rounds 11 and 12.

`Assets/Cartographer/Levels` and its **tracked** `Levels.meta` were restored after the cleanup briefly deleted the emptied folder (see §0). `Assets/Demos/ProtoGuyDemo/Sprites/LegsWalk/Legs-N-walk.png.meta` was rewritten by the Laumination Builder's `CrispenTextureImport` when `LegsWalk_N` was opened, exactly as round 12 predicted a user opening it would, and was **reverted with `git checkout`**. `Assets/Pyre/Green Lantern.asset` and the TextSplash border-font asset were already modified in the tree before this session and were not touched.

`git status` shows the eight source files above and nothing else of this task's. The Shaper demo document finished `dirty=False`; a project-wide dirty scan during the driving pass found only this task's own copies.

`ZuiSectionToggleBar.ShaperWindow.userSel` restored verbatim to `Views=1;Canvas=0;Layers=0;Shape=0;Fill=0;Swarm=0;SpriteFX=0;Lights=1;Tags=0`. `Shaper.lastView` and every `T322.*` / `T320.cap*` / `T321.*` / `T0312.*` pref deleted. Cartographer, Larder, Lathe, SpriteFx, TextSplash, the Aseprite launcher, Zoe, Mirage, the Laumination Builder and Pyre closed; the Shaper window's title restored to `Shaper`; `Undo.ClearAll()`.

**The Microsoft Store window is still minimised.** That is what keeps the by-eye channel working; leave it.

## 5. Verified how

**By probe, in the live editor:** every number in §1 and §2 — the folded-box widths (`Fracture 2` at 323.1 against twelve siblings), the Views-bar enabled/tooltip states before and after Save-as, the six first-time window audits in both empty and populated states, the Lathe spill (three fields, 70.7px each, against a 292.89pt row) and its 3 → 0, the 29 → 0 mid-row padded labels across five windows, the 17-ObjectField census and its 2 → 1, the Cartographer row arithmetic (438.7 of 396.4) and its 1 → 0 overflow, the Aseprite button tooltips in both states, the Create-row tooltip in both states, the 226-MicroSlider caption census, the Zoe and Mirage driving passes with the whole-`SerializedObject` diff after every control, the radio-group sweep, the three play samples with their frame hashes and lit counts, the playing-vs-paused audits, the 414-row fill sweep and its diff against T-0277, the project-wide dirty scan, four compiles, and the asset/pref/window cleanup.

**By eye, in real captures of the running editor:** Pyre at Zoom 6 with a clipped picture and a clean transport, and again after the overlay removal showing one readout instead of two; the 1.5× crop showing both readouts before it; Explosive Jet's card stack with `Fracture 2` folded at full width; the Views bar greyed and then live; the Laumination Builder fully populated on `LegsWalk_N`; Cartographer's empty state and its first-run level; Larder before (eight slider+field pairs, seven unlabelled radios) and after (eight MicroSliders, seven labelled radios); Lathe with its X/Y/Z rows tight inside the pane; the SpriteFx stack; TextSplash; the Aseprite launcher empty.

**Not verified:**

1. **No *human* has operated any of this.** Every press was a synthesized pointer or key event — the same code path a real one takes, not the same hand.
2. **Live buttons were not pressed in Zoe, Mirage, Larder, Lathe, SpriteFx or TextSplash.** Their greyed controls are covered by `inertNoReason=0`, but a *live* button that silently does nothing — round 12's Views-bar finding — can only be found by pressing, and these windows' buttons include Bake, Delete and Randomize, which are destructive or slow. The one window whose buttons were pressed end to end is Cartographer (New / Create / Cancel).
3. **The residual truncation in 2.3.** `Splash Demo (LiberationSans SDF) Border Font` still shows 302 of the 372px it wants. Improved, not closed.
4. **`Assets/Cartographer/Levels` is restored as an empty tracked folder**, which is what it was — but its `.meta` briefly went through a delete/restore and only `git status` (clean) attests to it, not a fresh import.
5. **Round 12's `fill-sweep-t321.tsv` was overwritten** by this session's run (§0). Not recoverable; not committed at the time.
6. **Three items were deliberately NOT changed and are on T-0260** (Q14/Q15/Q16 below) because each needs a decision, not a fix.
7. **The `Ease` radio wrapping in TextSplash** — the same nine options in two identical boxes wrap after `Back` in one and after `Elastic` in the other, decided by about 1px of flex slack. Measured, judged trivia, left alone.
8. **Round 12's own unverified list (its items 2, 3, 4, 5, 8) was not re-opened**, other than where this pass overlapped it.

## 6. Owner questions posted to T-0260

- **Q14 — SpriteFx's Fake light asks you to TYPE a MetaLayer id.** `SpriteFxRelight.metaLayerId` is a reflected `string`, so `ZuiReflect` draws a free-text field pre-filled with `Muzzle`. That is the exact case the fundamental rule names by name, and Zoetrope already draws the same field as a picker (`ZoetropeWindows.cs:2267`). Not changed here because a SpriteFx stack is authored standalone and has no owner to derive the option list from — which is a design decision, not a mechanical fix.
- **Q15 — a reflected `Vector2` that really is a position still draws as two float fields.** `ZuiReflect.Vector2Row` is deliberately not the 2D pad, and `[ZUIPair2D]` only covers two separate fields. Live case: Lathe's radial-fill `centre`. Closing it needs a new way to declare "this Vector2 is spatial", which rule 6 says to surface rather than build.
- **Q16 — the Lauminary Browser has no thumbnail path at all**, measured: rows are plain `Z.Button`s built from `$"{name}   (latest v{n})"`, no Image, no PreviewTex, no `IVisualPreview`, and `Lauminary` is a plain `ScriptableObject`, **not** a LauAsset — so the LauAsset thumbnail rule does not literally bind it even though a lauminary is unambiguously a visual asset. The browser does animate the selected animation in its right-hand panel. That answers round 12's open item 7 as a measurement; whether to add a picture is the owner's.

---

## 7. Verdict

Thirteen passes in, the first audit of six windows nobody had ever looked at turned up a window drawing the pre-MicroSlider look eight times with seven unlabelled enum rows, a field hanging 70.7px off its row, a greyed button describing its effect instead of its reason, and a permanent instruction paragraph — and behind two of those sat a systemic defect worth more than any of them: **the canonical rulebook states a rule as machine-enforced that has never existed in the code**, and 29 labels across five windows were wrong because of it. That is a new failure mode for this programme to remember: a rule can be written, believed, cited in three reports, and still not be running.

The surface is still not exhausted. Chunks was not re-audited this round; Lazor, Tapestry, Choreographer, BackSplash, Zounds, the Rules Editor, the Brain/Story graphs, Dashboard and the Sprite Catalog have never been audited at all; no window has been walked below 820px; and live buttons in six windows have never been pressed.

**FOUND: 7 non-trivial items, next pass needed**
