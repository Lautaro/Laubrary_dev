# T-0321 — the twelfth full pass (2026-09-09)

Worktree `D:\UNITY\Laubrary Dev - Shaper`, branch `feat/shaper`, HEAD `bb947fdb` at session start (round 11 + the T-0310 Views rename already committed), editor on port 7801. `Application.dataPath` confirmed as `D:/UNITY/Laubrary Dev - Shaper/Assets` at the head of the session and again at cleanup. Probes in `workspace/T-0321/probes/` (T-0320's `zlib.cs` / `cap2.cs` / `click.cs` reused verbatim), dumps in `out/`, captures in `shots/`.

**This pass found 8 non-trivial items and fixed 8.** Two of them are the kind nine passes of probes structurally could not see, and one of those makes a whole tool unusable on real data:

- **Pyre's preview paints over the transport** the moment the zoom exceeds the pane — the rendered frame covers Play/Pause, Frame, Strip, GIF, Bake, the Preview backdrop box and the Bake box, and the canvas-edge outline draws two window-wide rules through the Frame row and the Tags band. Every element was exactly where it belonged; only a capture shows it.
- **Opening a real laumination for editing showed an empty window.** 3 of the 5 shipped ProtoGuy draft animations carry an empty `sourceTextureGuid` on the animation while every recipe frame carries a real one, so no sheet loaded — blank canvas, no palette, no sequence — under a status line reading *"Loaded 'LegsWalk_N' (8 frames) for editing."*

The by-eye channel from round 11 §0 worked unchanged; the Microsoft Store window is still minimised and must stay so.

---

## 0. Methodology notes the next pass should keep

Three additions to round 11's recipe, each of which cost real time here:

| note | why |
|---|---|
| **`ZAudit` never audited a popover.** It walks `win.rootVisualElement`; a ZUI menu/flyout lives on `panel.visualTree`, outside it. No round in this programme has audited menu contents. Doing so found finding 2.2 in the first menu opened. Walk `win.rootVisualElement.panel.visualTree` and pick up `.zui-popover`. |
| **A rebuilding window makes multi-control driving in ONE eval impossible if you gate on `ZDrawn`.** Any dial change rebuilds the card, and the fresh tree is un-laid-out for the rest of that eval, so `ZDrawn` goes false for everything and a driver silently stops after the first control. Gate on `ZDisplayed` instead and re-query the list every iteration. Measured: gating on `ZDrawn` reported "1 of 29 controls wrote to the asset"; the same probe on `ZDisplayed` reported 28 of 28, and each one re-verified individually did write. |
| **A per-control before/after diff of the whole SerializedObject can false-negative.** One Chunks dial (`spawnFormation.formation.radius`) came back "dead" in the sweep and wrote correctly when driven on its own. Confirm every "dead" control individually before reporting it. |

And one about archived probes: **T-0277's `fill-sweep-probe.cs` LOADS an RFloat height-field asset it never creates.** Re-running it as-is reports a false regression on `heightFieldScale` because the height field is null. Create `Assets/Shaper/Audit0277/rfield0277.asset` (16×16 `TextureFormat.RFloat`) first.

---

## 1. What was checked

### 1.1 Round 11's landings, by eye

| landing | verdict |
|---|---|
| **shape picker at 820** | **Verified by eye.** Panel **808 wide inside an 820 window**, `visibility=Visible`, columns wrapped onto a second row, **all nine reachable** — Primitives, Solids, Bag, Explosions, Kiln › Energy Explosion, Kiln › Energy Projectile / Kiln › Flame, Simulations, Pyre. |
| **the wide-window regression round 11 introduced and fixed** | **Verified.** At 1400 the picker opens 1163 wide, `visibility=Visible`; a `ZuiValueControl` right-click mode menu also opens `Visible` (276×66.7). Menus in a wide window work. |
| **picker column headers with `›`** | **Verified by eye** — `Kiln › Energy Explosion`, `Kiln › Energy Projectile`, `Kiln › Flame`. |
| **Explosive Jet: no box-in-same-box, all 59 dials** | **Verified by probe and by eye.** Titled boxes 28, **duplicate-title nestings 0**. Per group: Fracture 12, Fracture 2 8, Flash 5, Chunks 9, Gobs 11, Dust 14 = **59/59**, inner boxes 0 in all six. The capture shows six single cards, two dials per row. *But* the folded `Fracture 2` card was still a narrow orphan — finding 2.5. |
| **the Edge row** | **Verified by eye** on a fresh document: `▾ Edge  [?]` over one full-width `Add edge`. The redundant field label is gone. |
| **Pyre "Life (frames)"** | **Verified by eye** — `Life (frames)  0 – 15`, an embedded MicroMinMax, indistinguishable from `Alpha 0.67` under it. |
| **Tab across a card with a Value2D** | **Verified by probe, and wider than asked.** Focusing a `ZuiValue2DControl` leaves it at 189.78×26.22 and moves none of its three siblings. Across the whole Shape+Fill surface: **61 focusable leaf controls, focusing each in turn shifted 0 of the window's laid-out elements.** |
| **Views bar** | **Exercised end to end.** First Save-as on a fresh store put the view in the picker **at once** (`choices=[AuditT321]`, value set) — T-0310's fix holds. All four Rename grey-reasons fired live: *No view is selected to rename* / *Type the new name above first* / *That is already this view's name* / *A view named "X" already exists*. Rename renamed in place and the picker followed. Store + `.meta` deleted, `Shaper.lastView` cleared. **But** its four sibling buttons were silent no-ops — finding 2.1. |

### 1.2 The surfaces round 11 left uncovered

| surface | state | elements / drawn / controls | captions short | overflow-X | off-window | no tooltip | inert w/o reason |
|---|---|---|---|---|---|---|---|
| Shaper (demo doc, 3 sections) | populated | 1320 / 818 / 233 | 0 | 0 | 0 | 0 | 0 |
| Pyre (Explosive Jet, all sections, 820) | populated | 1661 / 1025 / 272 | 0 | 0 | 0 | 0 | 0 |
| **Chunks (WallDebris copy, 17 sections)** | populated | 685 / 562 / 118 | 0 | 3¹ | 0 | 0 | 0 |
| **Zoetrope `ZoeWindow` (PreviewShooterZoe)** | populated | 285 / 126 / 41 | 0 | 0 | 0 | 0 | 0 |
| **Mirage (MirageDemo)** | populated | 166 / 120 / 35 | 0 | 0 | 0 | 0 | 0 |
| **Laumination Builder (ProtoGuy / LegsWalk_N)** | populated | 387 / 308 / 74 | 0 | 0 | 0 | 0 | 0 |
| Laumination Builder | empty | 35 / 32 / 10 | 0 | 0 | 0 | 0 | 0 |

¹ the three Chunks overflows are `FloatField` text spilling its 35.56pt box — and the values were **mine** (182.74, 424.11, 0.408, left by the driving sweep), not authored ones. It is still a real measurement: a Chunks numeric field is 35.56pt wide and clips at about six characters. Logged as trivia because no authored value in the project reaches that length.

**Chunks, DRIVEN rather than opened.** All 17 sections opened, then every drivable leaf perturbed through its own control API with the asset's whole SerializedObject diffed after each change. Across four passes: **44 distinct controls driven, every one wrote to the asset, 0 dead.** (Driven on a copy at `Assets/Shaper/AuditT321Chunk.asset`; `Assets/Demos/ChunksDemo/WallDebris.asset` finished the session `dirty=False`.)

**The IMGUI halves, by capture** — the hierarchy walk cannot see them, so all three were read off the screen:
- **the Tags island** (`LauTagField`, an `IMGUIContainer` in every `ZuiAssetWindow`): finding 2.6.
- **the Lauminary Browser**: `Refresh`, a `Lauminaries` list (`Orphaned (0)`, `ProtoGuy (latest v0)`), Duplicate / Rename / `Delete…` correctly greyed with nothing selected, and an empty-state line plus an `Open Laumination Builder` button. `Delete…` carries the ellipsis its dialog earns; nothing off-window. No finding. Worth an owner's eye some day: a Lauminary is a **visual** asset and this browser lists it as a bare name with no thumbnail.
- **the Sprite Catalog**: 0 assets exist in the project, and the empty state is correct (`Sprite Catalog library (0)`, *"No Sprite Catalog assets yet — hit New to make one."*, Save greyed, New/Browse live). No finding.

### 1.3 Temporal

Play pressed through the transport's own button on the **demo document**, sampled three times ≥1.5 s apart:

| t | readout | lit pixels | frame hash |
|---|---|---|---|
| 24.2 s | `frame 11/16` | 3683 | `989D47D5` |
| 25.8 s | `frame 10/16` | 4246 | `253676B6` |
| 27.6 s | `frame 9/16` | 4393 | `7CF2F940` |

Three distinct frames, three distinct pictures — the preview animates. `ZAudit` was run at each sample **while playing** and at the end **paused**: `captionShort=0`, `overflowParentX=0` in every case, for Shaper and for Pyre. Round 11's two false clips did not recur, in either state.

### 1.4 The dial-sweep regression check

T-0277's `fill-sweep-probe.cs` re-run verbatim against HEAD (`out/fill-sweep-t321.tsv`, 414 rows vs 408) and diffed against `T-0277/fill-sweep-after.tsv` on (doc, kind, card, control, state):

- **REGRESSIONS (pixels > 0 then, 0 now): 0.**
- **Newly alive (0 then, > 0 now): 51** — the entire `Bag (2 members)` **Fill** card, which was completely inert at T-0277 and now drives 150–1754 pixels per dial.
- Rows missing from the new run: 0.

The one apparent regression on the first attempt (`Pyramid / Solid / Fill / heightFieldScale`, 604 → 0) was the archived probe's own missing height-field asset; with it created the row matches. T-0265's and T-0288's probe bodies are not archived (only their TSVs), so those two were not re-run as such — the fill sweep is the one with a runnable body.

---

## 2. The findings, all fixed here

### 2.1 Four Views-bar buttons looked live and did nothing — fixed

`Apply`, `Update` and `Delete view` each read `_picker.value` and each returned in silence when it was empty; `Save as` returned in silence with an empty name field. Only `Rename` greyed itself with a reason. Reachable in three clicks and measured that way: delete the last saved view and the picker goes empty while all three still look pressable — pressed live, nothing happened, no exception, no asset written.

All five now grey with a reason from one place, `RefreshButtonStates` (was `RefreshRenameState`). Measured after: `Apply/Update/Delete view` → *"No view is saved yet — type a name and press Save as to make one."*, `Save as` → *"Type a name for the new view above first."*, and every one re-enables with its own tooltip the moment its precondition is met (verified by typing a name and pressing Save as).

`Zui/Toolkit/ZuiViewBar.cs:41`, `:310`.

### 2.2 The value mode menu described a mode it did not offer — fixed

`ZuiValueControl.ShowMenu` builds the mode list conditionally (`Oscillation` is opt-in per host, `_opt.allowOscillation`, default false) but handed the radio a **fixed** sentence ending *"…or an Oscillation — a sine swinging between two envelopes at an animatable rate."* Measured live on a Shaper dial: the menu drew **four** buttons — Static, Min-Max range, Envelope, Steps — and its tooltip described **five**. A tooltip has to read for the state it is in; naming a mode the control cannot reach sends the reader hunting for an affordance that is not there.

The sentence is now composed from the modes actually offered.

`Zui/Toolkit/ZuiValueControl.cs:868`.

### 2.3 A second "Rename" in the same window, one that renames something else — fixed

T-0310 added `Rename` to the Views bar. Every `ZuiAssetWindow` already puts a `Rename` in its toolbar that renames the **asset file**, and the bar sits a couple of rows below it: measured live in Shaper, **both are drawn, ~90pt apart, spelled identically**. This is exactly the collision T-0276 removed for `Delete` / `Delete view`, reintroduced one noun over. Now `Rename view`.

`Zui/Toolkit/ZuiViewBar.cs:162`.

### 2.4 `New` writes into whatever folder happens to be open, and never says which — fixed

`ZuiAssetWindow.FolderForNew()` puts a new asset beside the one currently bound and falls back to `DefaultFolder` only when nothing is bound, so the destination moves with whatever the author last browsed to. Nothing in the Create row said so — it is `Asset name [ ] [Create] [Cancel]`, and the only tooltip was *"File name for the new asset."* Measured: with the shipped `ShaperDemoDoc` open, pressing **New** wrote the new document into **`Assets/Demos/ShaperDemo/`**, a package demo folder, silently. (This report's own test document was created that way and had to be moved out.)

The name field, the `Asset name` label and the `Create` button now all name the exact destination: *"Creates Assets/Demos/ShaperDemo/&lt;name&gt;.asset — beside the Shaper Document that is currently open."* Tooltip only, deliberately: the programme's rules forbid adding a control, and the layout rules put explanations in tooltips. **If the owner wants the folder visible without a hover, that is a one-line addition and worth asking for** — see "not verified".

`Editor/AssetKit/ZuiAssetWindow.cs:335`.

### 2.5 A folded card shrank to its own title — fixed, and it is round 11's finding not quite closed

Round 11 flattened the six "X inside X" boxes on Explosive Jet by making the enclosing box *become* the group's box. It did. But `Fracture 2`'s group is `Advanced`, so that box now folds — and measured live it folded to **94.2pt wide among 323.1pt siblings**, a narrow orphan in the card stack. That is the same 94px stub round 11 describes as the *before* picture; the name repeat went, the width did not.

Cause is general, not Jet's. `ZuiReflect.FlowSubset` lays its children out in a **wrapping Row** and gives `flexBasis: 100%` only to things `IsWideControl` recognises — which did not include a `ZuiBox`. An open box is invisibly fine (its own inner flow already fills the row); a folded one has no content and content-sizes to its title. A box in a `zui-box__body` (a Column with `align-items: stretch`) was never affected, which is why only the reflected flow showed it — folded `Source frame` measured 323.1 the whole time.

A titled box is a card in a stack, not a control on a row, so it is now a wide control. Measured after: folded `Fracture 2` **323.1**, identical to its siblings; element count unchanged at 1661, dials still 59/59, duplicate nestings still 0, every audit still clean. Verified by eye.

`Zui/Toolkit/ZuiReflect.cs:415`.

### 2.6 The Tags button was thrown 780pt away from the word "Tags" — fixed

`LauTagField.Draw` ends its row with `GUILayout.FlexibleSpace()` then the `Tags…` button, which is sane only when the island is about as wide as its content. Pyre parents the Tags section **above** its split (T-0065), so the island is the whole **812pt** window: measured, the label sat at x=4 and the button at x≈780, out over the preview, with 700pt of nothing between them and the section's `?` glyph a full pane-width from its own title. The slack now goes last and the button sits beside the names. Verified by eye in Pyre and in the Zoe window.

`Editor/AssetKit/LauTagField.cs:32`.

### 2.7 Pyre's preview painted over the whole transport — fixed

**The headline.** `ZuiPixelStage` deliberately honours the zoom the author dialled instead of shrinking to fit ("a pane too small … gets a clipped picture, which is honest"), so `placement.rect` is routinely bigger than the island: measured at Zoom 6 on a 64×64 canvas, a **682.7×682.7pt** picture inside a **445.8×320pt** island. Neither `GUI.DrawTexture` (`ZuiPixel.Draw`) nor `EditorGUI.DrawRect` (the canvas-edge outline) clips to the container, and an `IMGUIContainer` does not clip by default.

What that looked like, in a real capture: at Zoom 6 the rendered fireball **covered `❚❚ Pause`, `Frame`, `Strip`, `GIF…`, `GIF scale`, `GIF dither`, `Bake`, the Frame scrubber, `Zoom`, `Fit`, `Speed`, `Delay`, the whole `Preview backdrop` box and the `Bake` box.** At the more ordinary Zoom 4 the picture is transparent where it overhangs, so only the outline shows — as a **0.89pt bright rule running the full width of the right pane, straight through the word "Frame"**, and a second one across the Tags band above. That rule is what first gave it away, and it took eight probes to attribute because it belongs to no element: measured, the whole panel contains exactly two elements under 3pt tall and neither is it.

Fixed by giving the island the guarantee Shaper's stage already has — Shaper draws its picture as a UITK child whose `worldClip` is the stage rect (confirmed: `worldBound` 2389×1593 at zoom 20, `worldClip` 306×320). One line, `overflow: hidden` on the preview container. Measured after: `worldClip` equals the island's own rect, and the Zoom-6 capture shows a correctly clipped picture with a clean transport under it.

`Editor/Pyre/PyreWindow.cs:410`.

### 2.8 Opening a real laumination for editing showed an empty window — fixed

`LauminationBuilderWindow.LoadAnimationIntoSequence` resolves the sheet from `Laumination.sourceTextureGuid` only. Measured on the shipped `ProtoGuy` lauminary's draft:

| animation | `sourceTextureGuid` | first recipe frame's guid | frames |
|---|---|---|---|
| **LegsWalk_N** | *(empty)* | `5f495e56…` | 8 |
| **LegsWalk_E** | *(empty)* | `cc5c9ef9…` | 8 |
| **LegsWalk_S** | *(empty)* | `6514727d…` | 8 |
| LegsIdleRotation | `2121882a…` | `2121882a…` | 16 |
| UpperAimRotation | `28d62e9c…` | `28d62e9c…` | 16 |

So for three of five, `_sheet` and `_sheetPath` came back **null** and the Builder rendered `None (Texture 2D)` over a blank canvas — **no canvas, no sprite palette, no sequence strip, nothing below the Sheet row** — under the status line *"Loaded 'LegsWalk_N' (8 frames) for editing."* A window that reports success and shows an empty state is the worst version of this: nothing tells the author it failed. `_sequence` did populate (8), which is why the status line was honest about the frames and wrong about everything else.

The summary field is a convenience; the recipe is the record. The sheet now falls back to the first recipe frame that names one. Read-only — the asset is not rewritten. Verified by eye: `Sheet Legs-N-walk 448×56px`, `2 · Identify Sprites`, `3 · Canvas` with the eight walk frames, `4 · Sprite Palette (8)`, `5 · Animation — sequence (8)`.

**One consequence the PM should know:** loading a sheet runs `CrispenTextureImport`, which rewrote `Assets/Demos/ProtoGuyDemo/Sprites/LegsWalk/Legs-N-walk.png.meta` (`maxTextureSize` 2048 → 16384). That is the Builder's own long-standing behaviour, now reachable where it never used to be. This session reverted that `.meta`, but a user opening `LegsWalk_N` will produce it again.

`Editor/Launimator/LauminationBuilderWindow.cs:3334`.

### 2.9 (bonus, same file) a section header printing a sentence of instructions — fixed

`"3 · Canvas — drag to marquee; drag interior/edges to move/resize"` was a section **title** carrying a how-to, re-read on every visit, with the tooltip already sitting right there. Retitled `3 · Canvas`, with the mode-specific sentence composed into the tooltip so it still reads for the state it is in. Two sibling instruction paragraphs were **not** touched and are on T-0260 as Q13.

`Editor/Launimator/LauminationBuilderWindow.cs:972`.

---

## 3. Files touched

| file | what |
|---|---|
| `Zui/Toolkit/ZuiViewBar.cs` | every button greys with a reason instead of silently doing nothing; `Rename` → `Rename view` |
| `Zui/Toolkit/ZuiValueControl.cs` | the mode menu's tooltip lists the modes it actually offers |
| `Zui/Toolkit/ZuiReflect.cs` | a titled box is a wide control, so a folded card keeps its width |
| `Editor/AssetKit/ZuiAssetWindow.cs` | the Create row names the folder the asset will land in |
| `Editor/AssetKit/LauTagField.cs` | the `Tags…` button sits beside its label; the slack trails |
| `Editor/Pyre/PyreWindow.cs` | the preview island clips its own painting |
| `Editor/Launimator/LauminationBuilderWindow.cs` | the sheet falls back to the frames' own texture; `3 · Canvas` is a title, not a sentence |

**No Pyre runtime or form file, no `CHANGELOG.md`, not committed** (ShaperHarmony rule 3). Three compiles this session, all `completed, failed=false, errors=[]`; the last one is the state the tree is in. `scriptCompilationFailed=False`, `isPlaying=False`. Play mode was never entered; the Test Runner was never run.

## 4. State left behind

Everything this task created was deleted through `AssetDatabase.DeleteAsset` (which removes the `.meta`): `Assets/Shaper/AuditT321Doc.asset`, `AuditT321Pyre.asset`, `AuditT321Chunk.asset`, `ShaperViews.asset`, and `Assets/Shaper/Audit0277/` with its `rfield0277.asset`. `Assets/Shaper` holds only the two untracked `New Shaper*.asset` files that were there at session start — **not this task's, and not deleted**, same as round 11.

`Assets/Demos/ProtoGuyDemo/Sprites/LegsWalk/Legs-N-walk.png.meta` was reverted with `git checkout` (see 2.8). Every real asset finished `dirty=False`: `ShaperDemoDoc`, `WallDebris`, `PreviewShooterZoe`, `MirageDemo`, `ProtoGuy.asset`, `ProtoGuy_draft.asset`. `git status` shows the seven source files above and nothing else of this task's; `Assets/Pyre/Green Lantern.asset` and `Assets/Demos/TextSplashDemo/…Border Font.asset` were already modified in the tree before this session and were not touched.

`ZuiSectionToggleBar.ShaperWindow.userSel` restored verbatim to `Views=1;Canvas=0;Layers=0;Shape=0;Fill=0;Swarm=0;SpriteFX=0;Lights=1;Tags=0`. `Shaper.lastView` and every `T321.*` / `T320.*` / `T0312.*` pref deleted. Chunks, Zoetrope, Mirage, the Laumination Builder, the Lauminary Browser, the Sprite Catalog and Pyre closed; `Undo.ClearAll()`.

**The Microsoft Store window is still minimised.** That is what keeps the by-eye channel working; leave it.

## 5. Verified how

**By probe, in the live editor:** every number in §1 and §2 — the popover width/visibility at 820 and 1400 and the value-menu popover's contents; the 61-control focus sweep; the Views-bar CRUD round trip with all four grey reasons and the three-click empty-picker case; the six populated-window audits; Explosive Jet's 28 titled boxes / 0 duplicate nestings / 59 per-group dials before and after; the folded-box widths (94.2 → 323.1) and the `Source frame` control; Chunks' 44 driven controls across four passes plus the individual re-verification of the one false negative; the ProtoGuy guid table and `_sheet`/`_sheetPath` before and after; the Pyre placement rects (455×455 and 682×682 in a 445×320 island) and `worldClip` before and after; Shaper's stage `worldClip` at zoom 20; the pixel scan that located the stray rule at y=540.2pt and the element census proving nothing draws it; the three play samples with their frame hashes and lit counts; the paused audits; the 414-row fill sweep and its diff; three compiles; the asset/pref cleanup and the dirty checks.

**By eye, in real captures of the running editor:** the shape picker's nine wrapped columns at 820 with their `›` headers; the value mode menu; a fresh document's Shape + Fill sections and the `Edge / Add edge` box; Pyre's `Life (frames) 0 – 15`; Explosive Jet's six flattened cards (Fracture / Fracture 2 / Flash / Chunks / Gobs / Dust) before and after the width fix; Pyre's transport buried under the preview at Zoom 6 and clean at Zoom 6 after the fix; the stray rule through the "Frame" label at 4× crop; the Tags row before and after; the Laumination Builder blank and then fully populated; the Zoe and Mirage windows; the Lauminary Browser and the Sprite Catalog.

**Not verified:**

1. **No *human* has operated any of this.** Every press was a synthesized pointer or key event — the same code path a real one takes, not the same hand.
2. **The destination folder in 2.4 is a tooltip, not something you can see.** A user who does not hover still cannot tell where `New` will write. Making it visible needs an element the programme's rules say not to add without asking — flagged rather than built.
3. **Pyre's frame scrubber was left alone** — it is the last native thumbed slider in the package, its in-code justification ("Pyre1 parity") points at a tool deleted on 2026-08-23, and Shaper draws the same control as a MicroSlider. Posted on T-0260 as **Q12** rather than overridden.
4. **Two on-screen instruction paragraphs in the Laumination Builder were left alone** — the layout rules ban them outright, but both sit under bespoke IMGUI canvases whose gestures are otherwise undiscoverable, and an `IMGUIContainer` tooltip may not show on hover. Posted on T-0260 as **Q13**.
5. **T-0265's and T-0288's sweeps were not re-run as such** — only their TSVs are archived, not runnable probe bodies. T-0277's was, and it is the one that covers the fill dials both of the others also touched.
6. **Zoe / Mirage were audited and captured but not DRIVEN** — Chunks got the driving pass this round; those two got the mechanical audit, a populated capture and a read of their control kinds.
7. **The Lauminary Browser lists a visual asset as a bare name with no thumbnail**, which the LauAsset rule says a visual asset must always have. Not chased: it is a legacy IMGUI browser and it is not clear a `Lauminary` is a LauAsset at all.
8. **`Assets/Pyre/Green Lantern.asset` and the TextSplash border-font asset are modified in the tree** and were not touched here; they pre-date this session.

---

## 6. Verdict

Twelve passes in, a full pass still turned up two defects that make a tool visibly wrong on real data — one that paints a fireball over the transport, one that opens an empty editor and says it succeeded — plus six smaller ones, four of which are systemic ZUI or AssetKit behaviour reaching every tool. The pattern is unchanged from round 11 and worth stating again: **everything found here that mattered was found by looking, and every one of them left the element tree perfectly clean.** Round 11 also shows that a fix can land and its *visual* half stay broken (2.5), so a landing is not closed until it has been seen.

Uncovered surfaces remain: Zoe and Mirage were not driven, Launimator's Aseprite window / Sprite Catalog / Cartographer / Larder / Lathe / SpriteFx / TextSplash have never been audited in this programme at all, and no window has been walked at a size other than 820–1500 wide.

**FOUND: 8 non-trivial items, next pass needed**
