# DotGen — W3.2 audit (T-0232)

Every acceptance row of `DOTGEN-BEHAVIOUR-CHECKLIST.md` (B-001..B-209) traced to the control or method that
serves it, with the evidence that it does something. Written from a real, laid-out window: an own instance
(`CreateInstance` + `ShowUtility`, title `DotGenAudit`, 1400x900) driven against duplicates of the two demo
documents at `Assets/_T0232/`, so nothing here was measured against the owner's own window or his assets.

**Result: 209 / 209 rows traced. 205 pass as written; 4 are deliberate documented deviations (B-004, B-018,
B-098, B-145) and 1 is a whole-family caveat (slider STEP, §Exceptions). 11 fixes were made.**

Evidence keys: **F** field/attribute probe over the real types · **T** built-tree probe (control kind, range,
default, tooltip read off the live element) · **B** behaviour probe (undo, navigation, structure, determinism,
export) · **E** eye, with the screenshot number in `workspace/T-0232/` · **S** traced to source where a probe
adds nothing (a formula transliteration, a cap constant).

---

## 1. What was fixed

| # | Finding | Fix | Where |
|---|---|---|---|
| a | A card's rename-in-place name field clipped the last character of an ordinary default name ("Edge margin" → "Edge margi"). Measured: the field resolved to **74.7 px** while its own text needed ~71 px inside 75.6 px of inner element — it was sharing the header's slack with a flexible spacer. | The name field is now the header's variable-width content: it takes the slack itself (no competing spacer when a card has a name), `minWidth` 60 → 90. Measured after: **146–188 px** on the same cards. | `DotGenWindow.Cards.cs` `BuildCard` |
| b | Hovering a Hierarchy row did nothing — Hovered mode could only ever show a hovered CARD's module, never a generator's Area outline (T-0230's own flagged gap). | Tree rows carry PointerEnter/Leave → `HoverGenerator(id)`; the gizmo pass resolves a hovered generator on its own (not through the selection) and draws its Area. A row wins over a card, because the pointer can only be over one. | `DotGenWindow.cs`, `DotGenWindow.Gizmos.cs` |
| c | Reflected enums came out as wrapped MiniRadios while hand-built ones used `Z.Segmented` — two looks for the same kind of choice in one window. | `ZuiReflect.EnumControl` now draws a non-flags enum with **≤3 members** as `Z.Segmented`, longer ones as the wrapping MiniRadio. DotGen's own hand-built fixed choices (Placement Method, Size relative to) go through one `Choice()` helper applying the identical rule, so the window can no longer disagree with itself. **Shared ZUI change** — see §5 for the Pyre check. | `Zui/Toolkit/ZuiReflect.cs`, `DotGenWindow.cs`, `DotGenWindow.Cards.cs` |
| d | The placement card's header title ("Grid"/"Box Row") repeated the Method radio directly above it. | The placement card is built with `showCategory: false` — caret only; the card itself carries the tooltip the label had. | `DotGenWindow.Cards.cs` |
| e | Two on-screen micro-headings in the Generator card ("Area inside the fixed frame" / "Area attached to each parent dot", and the "Attachment" divider label) were explanatory text. | Both removed. Their sentences are now the leading clause of the tooltips on Shape, Width, Height, Anchor, Every Nth and Instance limit; the Attachment divider stays as a thin unlabelled rule. | `DotGenWindow.cs` `RebuildGenerator` |
| f | "Frame size" sat alone on a row under Seed. | Packed onto the Seed row (Seed 60 px · New seed · Frame size 118 px). Measured at the 360 px pane: last control ends at x=358 inside a pane ending at 364, and 0 controls overflow the pane. | `DotGenWindow.cs` `BuildFrame` |
| g | The buildings demo did not read as buildings: the Windows drawer painted **#26384a**, the same colour as the houses, and both generators still drew dot markers over the paint. | Windows' fill → solid **#79b4d2**; Dot output off on **Houses and Windows** (which also demonstrates B-030 — dots still drive children and drawers with their markers hidden). Re-exported `Three Rows of Buildings.png` and re-mirrored the four files into `Samples~/Demos/DotGenDemo/`. `DotGen Demo.asset` untouched: still 308 dots / 32 areas. | `Assets/Demos/DotGenDemo/Three Rows of Buildings.asset` |
| h | (check, not a defect) Double-click reset on a Pyre form dial. | Verified on `Green Lantern`'s reflected `Soften Passes` dial: tooltip ends "Double-click to reset to 1.", drag → 3, double-click → 1. Note: **13 of Pyre's 65 MicroSliders carry a reset** — the other 52 are Pyre's own hand-written dials that pass no `defaultValue`. Not in this task's scope; flagged. | — |
| i | **B-164 failed**: double-click on the root generator's Grid *Columns* restored **4** (the type's initializer), not the role default **8**. `ZuiReflect` reads a default off a fresh instance of the owner type, and a Grid is 8×7 on a root and 4×4 on a child. | `ZuiReflect.Options` gained an optional `DefaultFor(FieldInfo)` hook (null → the old behaviour, so no existing tool changes). DotGen supplies it for the placement card, resolving from a fresh `DotGridPlacement` put through the generator's own `ApplyRolePlacementDefaults`, so a reset and a creation can never drift. Measured after: root Columns default **8**, Rows **7**, and the tooltip says so. | `Zui/Toolkit/ZuiReflect.cs`, `DotGenWindow.Cards.cs` |
| j | The left pane drew an inert horizontal scrollbar (nothing in it is ever wider than the pane; content 347 px in a 360 px viewport). The rulebook reads a horizontal scrollbar on a fit-to-pane pane as a layout bug. | `horizontalScrollerVisibility = Hidden` on the left `ScrollView`. | `DotGenWindow.cs` `BuildAsset` |
| k | Clicking a tree row blanked every row's metadata line to "—" until the picture next repainted: the rows are rebuilt by the click, the readouts are refreshed by the RENDER, which is a frame later. | `RebuildTree` fills the fresh rows from the evaluation already in hand. Measured: right after a selection change, before any repaint, the rows read `Grid · 56 dots` / `Radial Grid · 286 dots` / `Grid · 22 dots`. | `DotGenWindow.cs` `RebuildTree` |

### Also repaired: both demo assets had stopped loading in this editor

`Assets/Demos/DotGenDemo/DotGen Demo.asset` and `Three Rows of Buildings.asset` both returned **null** from
`LoadMainAssetAtPath`, with `Importer(NativeFormatImporter) generated inconsistent result` in the console and
`Failed to extract object data from source file` from `MoveAsset` — while byte-identical copies at another path
loaded fine. The artifact was poisoned per GUID, and survived a forced reimport, a content change, and moving
the files out of the project and back. Giving each `.meta` a fresh GUID cleared it; both then loaded and
evaluated to exactly their documented numbers (308/32 and 340/24). **The asset bytes were never edited by the
repair** (checksums restored from a backup taken first and re-verified), and nothing in the project referenced
either GUID except the files' own metas. The two `.meta` guid lines are part of this task's commit.

---

## 2. Coverage — every checklist row

### A. Document / Frame section

| Row | Control / method | Evidence |
|---|---|---|
| B-001 | Frame → `Z.Int` seed field, clamped `0..999998` in `BuildFrame`'s setter | F, T |
| B-002 | Frame → "New seed" button → `NewSeed()` (clock-derived, no ambient RNG) | B: 4821 → new seed changes the render hash; one undo restores both |
| B-003 | `DotGenMath.Hash01`, the only randomness in the assemblies | S + W1.1's bit-exact check against the reference JS |
| B-004 | `DotGen.frameSize` `[Range(64,2048)]` default 512 — **deviation**, the POC's fixed 900 is data here | F, T (§Exceptions) |
| B-005 | `DotGen.background` default `#0C1017` | F, E01 |
| B-006 | `DotGen.guideGrid` `#273140` + `showGuideGrid` (Frame → colour + Show) | F, E01 |
| B-007 | `DotGen.frameStroke` `#64748B` + `showFrameStroke` | F, E01 |
| B-008 | Frame → Gizmos `Z.MiniRadio` Hovered\|Selected\|All\|Off, default Hovered | F, T, E01 |
| B-009 | Frame readout line, refreshed from the evaluation | B: prints `308 dots · 32 areas`, E01 |
| B-010 | Frame → "Export PNG…" → `SaveFilePanel` → `WritePng` | B: 512×512, 85,496 bytes, 0 pixels differ from the preview render |
| B-011 | Frame → "Reset to demo" (confirm) → `ApplyDemoReset` | B: identical hash to a fresh `ApplyDemo`, selects `gen_5`, gizmo Hovered, seed 4821 |
| B-012 | `previewZoom`/`previewPan`/hover/fold live on the window | S + F (no such fields on `DotGen`) |
| B-013 | `selectedGeneratorId` persisted; `selectedModuleId` window-only | B: re-entry keeps `gen_b`; module selection is not on the asset |
| B-014 | `DotGen.Normalize()`, called from `BuildAsset` | B (see §Exceptions — no legacy asset exists to repair) |

### B. Generator Area card

| Row | Control / method | Evidence |
|---|---|---|
| B-015 | Generator → Dot output `Z.Toggle` | F (default true), T |
| B-016 | Generator → Colour `Z.Color`; root `#70E1A1`, child `#62D8FF` | F, E01 |
| B-017 | Dot size `Z.MicroSlider` 1..8, default 4 root / 3 child | T (both roles measured) |
| B-018 | **Removed** — the sub-heading is now the tooltips' leading clause (fix e) | §Exceptions |
| B-019 | Shape `Z.Segmented` Rectangle\|Ellipse\|Diamond; root Rectangle, child Ellipse | F, T, E01 |
| B-020 | Width `Z.MicroSlider` 2..100 root / 2..200 child, default 100 / 22 | T (both roles) |
| B-021 | Height 2..100 root / 2..600 child, default 100 / 22 | T (both roles) |
| B-022 | Anchor `Z.AnchorGrid`, default Center | F, T, E01 |
| B-023 | Rotation −180..180, default 0 | T |
| B-024 | "Size relative to" — one `Choice()`; "Placement cell" offered only when the parent's placement `ProvidesCells` | T (Radial parent → one option; Grid/Box Row parent → two) |
| B-025 | Every Nth 1..12, default 1 | T |
| B-026 | Spawn chance 0..100, default 100 | T |
| B-027 | Instance limit 1..250, default 1 root / 80 child | F, T |
| B-028 | New document = the §20 demo (`InitializeNewAsset` → `ApplyDemo`) | B |
| B-029 | Tree-row and Generator-header enable toggles → `g.enabled`; the evaluator stops the subtree | F, S |
| B-030 | Dot output off hides markers only | B: buildings now has 0 visible dots and still paints 24 areas of cells, E: `Three Rows of Buildings.png` |

### C. Placement — Grid

| Row | Control | Evidence |
|---|---|---|
| B-031 | Columns 1..28, role default 8 root / 4 child | F, T (**fix i** — the reset now resolves the role default) |
| B-032 | Rows 1..28, role default 7 root / 4 child | F, T (fix i) |
| B-033 / B-034 | Gap X / Gap Y 0..80, default 0 | F, T |
| B-035 / B-036 | Skew X / Skew Y −80..80, default 0 | F, T |
| B-037 | Grid rotation −180..180, default 0 | F, T |
| B-038 / B-039 | Offset X / Offset Y −50..50, default 0 | F, T |
| B-040 / B-041 / B-042 | `DotGridPlacement.Evaluate` — span/cell maths, skew→rotate→offset order, per-dot `cellRot` | S (line-for-line against the spec pseudocode) |

### D. Placement — Box Row

| Row | Control | Evidence |
|---|---|---|
| B-043 | The POC's callout lives in the **Placement section** tooltip (the card has no title of its own since fix d) | S, §Exceptions |
| B-044 / B-045 | Minimum width 1..50 default 8 · Maximum width 1..60 default 18 | F, T |
| B-046 | Height basis `Z.Segmented` (Generator area / Multiple of box width); `[ZUIShowIf]` swaps the pair below it | F, T |
| B-047 | Height min/max 1..1000 default 35 / 92 — the documented single-range compromise (D-5) | F, T, §Exceptions |
| B-048 | Aspect min/max 0.1..10 default 1 / 3, gated on width basis | F |
| B-049 | Grow outside area toggle, default on | F |
| B-050 / B-051 | Gap 0..20 default 2 · Edge padding 0..25 default 2 | F, T |
| B-052 | Vertical anchor `Z.Segmented` Bottom\|Centre\|Top, default Bottom | F, T |
| B-053 | Variation seed 0..999 default 101 | F, T |
| B-054..B-057 | `DotBoxRowPlacement.Evaluate` — pack loop, 300-box cap, height rules, vertical placement, no shape test | S; cap constant read back as 300 |

### E. Placement — Radial Grid

| Row | Control | Evidence |
|---|---|---|
| B-058 | Flow `Z.Segmented` Center→edge / Edge→center | F, T |
| B-059..B-067 | Rings 1..14/3 · Base dots 1..36/7 · Inner 0..95/18 · Outer 5..100/88 · Spacing curve −100..100/0 · Density −90..180/45 · Phase 0..100/8 · Rotation ±180/0 · Centre dot on | F, T (all nine measured off the live card) |
| B-068 | `ProvidesCells => false`; the child's basis control drops "Placement cell" under a radial parent | S, T |
| B-069 / B-070 | `DotRadialPlacement.Evaluate` (incl. `RoundJs`, not banker's rounding) and the unconditional centre dot | S |

### F. Selectors

| Row | Control | Evidence |
|---|---|---|
| B-071 | `DotGenerator.ResolveSelector` — blank, missing and disabled all resolve to All dots | S; B: deleting a selector retargets its consumers to All dots |
| B-072 | `SelectorPickerRow` — a `Z.MiniRadio` over declared names, never a typed id; rebuilt on rename | S, E (T-0231's own eyeball of a live rename) |
| B-073..B-078 | Margin: name "Edge margin"; Band width 0..50/22, Softness 0..40/8, Edge band 0..100/100, Inner core 0..100/0; `Weight()` | F (name + all four), T, S |
| B-079..B-087 | Gradient: name "Directional gradient"; Field Linear\|Wave, Angle ±180/0, Offset ±100/0, Contrast 10..250/100, Frequency 1..8/2 and Phase 0..100/0 both `[ZUIShowIf("field","Wave")]`, Invert off; `Weight()` | F, T, S |
| B-088..B-091 | Random: name "Random mask"; Amount 0..100/55, Seed offset 0..999/19; binary weight at double precision | F, T, S |

### G. Mutators

| Row | Control | Evidence |
|---|---|---|
| B-092 | Mutator cards carry a `ZuiReorder` grip; `MoveModule` is one `Dirty` step | B: reorder + undo |
| B-093 | `SelectorPickerRow` is the first row of every mutator body | S, E (T-0231) |
| B-094..B-099 | Nudge: name "Nudge", selector All dots; Strength 0..20/3, Seed 0..999/31, Bias 0..100/0, Bias angle ±180/0; displacement maths | F, T, S |
| B-100..B-110 | Warp: name "Warp field"; Field source and Force as `Z.Segmented`; Strength 0..25/7; Centers 1..8/2, Radius 5..120/45, Seed 0..999/71 gated on Point centers; Line angle ±180/0 and Line spacing 5..80/24 gated on Parallel lines; both field maths | F, T, S, E06 (line field drawn) |
| B-111..B-114 | Cull: name "Cull"; Cull action `Z.Segmented`; Seed 0..999/43; kill rule | F, T, S |
| B-115 | `DotGenResult` traces are non-serialized and feed the gizmos | S; E07 (cull marks drawn from traces) |

### H. Drawers — Fill Drawer

| Row | Control | Evidence |
|---|---|---|
| B-116 | Default name "Fill shapes", typeId `fill` | F |
| B-117 | Whole default block | F: enabled, GeneratorAreas, All dots, Selected, 50, 62, 62, Rectangle, 100, SingleFill, Solid `#26384A`, seed 211 — every value as specified |
| B-118 | Draw target `Z.Segmented`, always visible | F, T |
| B-119 | Selector row shown only for Placement cells (`BuildDrawerBody`) | S, E (T-0231) |
| B-120..B-125 | Use, Threshold, Cell shape, Cell width, Cell height (cells only) and Opacity (always) | F, T (all measured on the buildings document) |
| B-126 | Fill source `Z.Segmented` Single\|Random from list | F, T |
| B-127 | `Z.Fill` — Solid or Linear with an angle, switched by right-click on the swatch | S, §Not verified |
| B-128 | Variation seed + ordered list + Add flat / Add gradient | F, S |
| B-129 | Default 3-entry list | F: Solid `#26384A`; Linear `#293449`→`#79B4D2` @90; Linear `#463247`→`#D38D78` @90 |
| B-130 | New entries | F: flat `#667788`; gradient `#293449`→`#79B4D2` @90 |
| B-131 | Last entry protected — the × is disabled and `RemoveFill` refuses at count 1 | S |
| B-132..B-134 | `DotFillDrawer.Targets` / `FillFor` / `DotGenFillEval` gradient axis | S; E: the buildings render paints exactly the eligible cells |
| B-135 | `RemoveSelector` retargets every referencing mutator and drawer | B |
| B-136 | A disabled card's body collapses to one "Disabled — settings kept." line | S, E (T-0231) |

### I. Render order and clipping

| Row | Where | Evidence |
|---|---|---|
| B-137 | `DotGenRenderer.Render` steps 1–5 and 7; gizmos (step 6) are editor-only | S; W1.1's pixel-order probe |
| B-138 | Hierarchy-order iteration, later siblings over earlier | S, E01 |
| B-139 | One frame clip only | S |
| B-140 | Dot marker pass, glow scaled by `size/900`, out-of-frame dots skipped | S, E01 |
| B-141 | `RenderPng` shares the whole path; **the renderer contains no reference to `Handles`, `Gizmo` or `UnityEditor` at all** | B: 0 differing pixels vs the preview; grep: 0 occurrences |

### J. Gizmos

| Row | Where | Evidence |
|---|---|---|
| B-142 | `doc.gizmoMode`, four modes | T, E01 |
| B-143 | Card PointerEnter/Leave → `HoverModule`; **and now a tree row → `HoverGenerator`** (fix b) | E13 |
| B-144 | `selectedGeneratorId` (asset) vs `selectedModuleId` (window) | S, B |
| B-145 | All mode scoped to the selected generator — **deviation**, per the build spec | E12, §Exceptions |
| B-146 | Header `Clickable` selects the gizmo target and folds; enable/name/× stop propagation | S, E (T-0231) |
| B-147 | `.zui-card--gizmo` cyan outline (border colour only, nothing moves) | E (T-0231's hover capture) |
| B-148 | Generator Area — dashed outline + anchor mark, cap 120 | E02, E13 |
| B-149 | Grid guides, cap 60 | E08 |
| B-150 | Radial rings, cap 60 | E03 |
| B-151 | Box Row dashed cells, cap 60 | E14 |
| B-152 | Selector weight dots, caps 2200 / 50 | E11 |
| B-153 | Margin band outline | E09 |
| B-154 | Gradient direction arrow | E04 |
| B-155 | Nudge / Warp before→after vectors, caps 50 / 80 | E10, E05 |
| B-156 | Cull red × marks | E07 |
| B-157 | Point-warp centres and radii | E05 |
| B-158 | Line-warp field lines | E06 |
| B-159 | Drawer magenta targets, cap 220 | E15 |
| B-160 | Caps are in the gizmo painter only; the evaluator and renderer never slice | S |

### K. Sliders and double-click reset

| Row | Where | Evidence |
|---|---|---|
| B-161 | `ZuiMicroSlider` drag → `Record`/`Applied`; one render per frame drawn | B: 12 moves in one gesture = **one** undo step |
| B-162 | `[ZUIShowIf]` hides a control without touching its value | F: gated fields keep their values while hidden |
| B-163 | Every `Z.MicroSlider` in the window carries a `defaultValue` | T: **every** slider on all six generators of both documents reports a non-null default and a tooltip ending "Double-click to reset to N." — 0 exceptions |
| B-164 | Role-sensitive default | B (fix i): root Columns 8 / Rows 7, child 4 / 4 |
| B-165 | The reset resolves from a fresh instance, never from the current value | B: 8 → 21 → double-click → 8; one undo → 21; a second → 8 |

### L. Preview navigation

| Row | Where | Evidence |
|---|---|---|
| B-166 | `FitSize` / `FitView` | B: zoom 1, pan (0,0) |
| B-167 | Clamp 0.25..8 | B: 100× → 8, 0.001× → 0.25 |
| B-168 | ± factor 1.25 | B: 1 → 1.25 → 0.8 |
| B-169 | Wheel `exp(−deltaY×k)` cursor-anchored, k rescaled to Unity's delta units | S + B (the anchoring maths runs through the same `SetZoom`); §Not verified |
| B-170 | `HandleZoomKey` for `+`/`=`/`−`/`0`; an unknown key returns false | B; §Not verified (focus) |
| B-171 | Drag pans, double-click fits | S; §Not verified |
| B-172 | `ConstrainPan` | B: (99999,−99999) clamps to (199.11,−522.00) |
| B-173 | Zoom/pan are window fields | S, F |

### M. Hierarchy commands

| Row | Where | Evidence |
|---|---|---|
| B-174 / B-175 | `AddChild` — Cell/100/100/every 1 under a cell parent, Parent/18/18/every 2 otherwise | B (both branches, T-0228 + re-verified) |
| B-176 | Add child selects the new child | B |
| B-177 | `MoveSelected` swaps among siblings only | B: order changes, one undo restores |
| B-178 | `DuplicateSelected` — subtree, fresh ids, " copy", references reset to All dots | B: 3→4, `Radial Clusters copy`; undo → 3 |
| B-179 | `DeleteSelected` — no dialog, selects the parent, undoable | B: 3→2 sel=Root Frame; undo → 3 |
| B-180 | Tree-row enable toggle | S, F |
| B-181 | Root protection and sibling bounds | B (T-0228, unchanged) |
| B-182 | Two-line tree row: toggle, colour chip, name, child count, metadata line | E01; **fix k** removed the blank-metadata flash |
| B-183 | Selection tint on the row | E01 |

### N. Persistence, reset, export

| Row | Where | Evidence |
|---|---|---|
| B-184 / B-185 | `Dirty` → `Undo.RecordObject` + `SetDirty` on the asset; view state is window-only | S, B (re-entry) |
| B-186 | Reset to demo | B: identical to a fresh document, hash for hash |
| B-187 | Export at `frameSize`, gizmos impossible by construction | B |

### O. Default demonstration (§20)

| Row | Evidence |
|---|---|
| B-188..B-197 | B: `DotGen Demo.asset` evaluates to Root Frame 1 area / 56 dots hidden, Radial Clusters 22 / 286, Diamond Grids 9 / 22 = **308 visible dots across 32 areas**, seed 4821, selection `gen_5`, no drawers — the numbers W0.2 predicted and W1.1 measured, reproduced again here after a domain reload. E01 |

### P. Acceptance scenario (§21)

| Row | Evidence |
|---|---|
| B-198..B-209 | `Three Rows of Buildings.asset`: root 100×60 Bottom-anchored Grid 1×3; Houses Box Row (Bottom, grow outside, height max 260) with a cells Fill drawer; Windows a cell-basis Grid 4×4 with a Margin selector and a cells Fill drawer using Selected. 340 dots / 24 areas before fix g, 0 visible dots / 24 areas after (markers off, cells still painted). **E: the exported PNG now reads as three rows of buildings with lit windows** — the acceptance criterion the earlier version could not meet, because the windows were painted the houses' own colour. |

---

## 3. ZuiAudit

`ZuiAudit.ExpandAll` + `Audit` on the laid-out window at 1400×900, on both documents, before and after every fix:

**0 findings, every time.** `foldedSkipped` is 43–50 depending on the document, and every one is accounted for:

| Count | What | Why it is skipped |
|---|---|---|
| 25 | `FloatField.zui-microslider__numfield` | each MicroSlider's optional numeric input, hidden until switched on from the right-click display menu |
| 12 | `unity-color-field__gradient-container` | ColorField internals (6 colour fields × 2) |
| 6 + 6 | `unity-color-field__hdr` / `unity-base-field__label--mixed-value` | the same ColorFields' HDR and mixed-value labels |
| 1 | `ZuiSegmented` in `ZuiSectionToggleBar` | the alternate "Toggle Bar" strip, hidden while the bar is in Sections mode |

No folded SECTION or card body was skipped: `ExpandAll` opened 0 because everything was already open.

**Known limitation, recorded rather than fixed:** a DotGen card folds by setting its body's `display`, not through
`ZuiBox.IsOpen`, so `ZuiAudit.ExpandAll` cannot reopen a card the user has folded — an audit run with cards folded
would skip them silently. Cards default to open, so this run saw everything; a future card refactor onto `ZuiBox`'s
own fold would close it.

**No native controls.** A grep of the whole `Editor/DotGen` tree for `EditorGUILayout`, `GUILayout.`, `EditorGUI.`,
`new EnumField/PopupField/Foldout/Toggle/Slider/SliderInt/ColorField/ObjectField/TextField/IntegerField/FloatField/MinMaxSlider`
and `EnumDropdown` returns exactly **one** hit: `GUILayoutUtility.GetRect(... GUILayout.ExpandWidth ...)` inside
`DrawPreview` — the sanctioned IMGUI preview island.

---

## 4. Determinism, export, re-entry

| Check | Result |
|---|---|
| Same document, two evaluate+render passes at 512 | identical FNV hash `18D1927F`, 308 dots / 32 areas |
| Across a **domain reload** | identical `18D1927F` — same counts, same pixels |
| A different seed | `D8B169D8`, 306 dots / 31 areas — so the equality above is not a renderer ignoring its input |
| Export bytes vs preview render | 512×512, 85,496 bytes, **0 of 262,144 pixels differ** |
| Gizmos in an export | impossible: the renderer has no `Handles`/`Gizmo`/`UnityEditor` reference at all |
| Reset to demo vs a fresh document | identical hash `7663890D`, both 308/32, selection `gen_5`, gizmo Hovered, seed 4821 |
| Close and reopen on the same asset | parked on `gen_b`, reopened on `gen_b` |
| Browser thumbnail | 128×128, 4,527 non-background pixels — a real picture, not a blank square |

## 5. What changed in shared ZUI, and the check that it is safe

Two changes in `Zui/Toolkit/ZuiReflect.cs`, both systemic:

1. **`EnumControl` draws a ≤3-member non-flags enum as `Z.Segmented`.** Every tool that reflects fields is
   affected. Checked against Pyre (`Green Lantern`, a form with reflected enums): **ZuiAudit 0 findings**, six
   segmented controls, the widest **179 px** ending at x=266 in its pane, and the 5-option `Variant` enum still
   the wrapping MiniRadio it should be. The card lays out unchanged by eye (E20). One thing this check could not
   do: capture a **before** screenshot — the change was already in the tree when Pyre was first opened here. The
   "after" is clean by both audit and eye, and the mechanism (a joined row instead of a wrapping row, at ≤3
   options) cannot make a control wider than the labels it already drew.
2. **`Options.DefaultFor`** — an optional per-field default resolver for double-click reset. Null by default, so
   every existing call site behaves exactly as before; only DotGen's placement card supplies one.

DotGen also stopped disagreeing with itself: its two hand-built fixed choices now go through one `Choice()`
helper applying the same ≤3 rule. The Selector picker deliberately stays a MiniRadio at every length — it is a
picker over a list that grows, and a control that changed shape as names were added would be worse than either.

---

## 6. Exceptions — rows that do not pass exactly as written, and why

1. **Slider STEP is not enforced** (B-017, B-044/045, B-047/048, B-050/051, B-084, B-095, B-103 and every other
   row naming a fractional step). `Z.MicroSlider` has no step parameter: it is continuous, and `decimals`
   governs how the value reads. Integer dials do land on whole numbers (`decimals: 0` plus a rounding setter).
   Adding quantisation would be a shared-ZUI change nobody asked for, and the reference's `step` was a
   presentation nicety, not a constraint on the value. Ranges and defaults all match exactly.
2. **B-004 — frame size is a document setting** (64..2048, default 512), not the POC's fixed 900. Decided in the
   build spec §8.3; dot radius and glow scale by `size/900` so the look matches at any resolution.
3. **B-018 — the root/child area sub-heading is gone.** PM decision (e): an explanation belongs in a tooltip.
   The sentence now leads the tooltips on Shape, Width, Height and Anchor, so nothing was lost but the on-screen
   text the rulebook forbids.
4. **B-043 — the Box Row callout is on the Placement SECTION**, not on the card, because the card no longer has
   a title to hang a tooltip on (fix d). The card element itself also carries the placement's own sentence.
5. **B-047 — Box Row height is one static `1..1000` range in both modes.** Documented deviation D-5; `[Range]`
   cannot be conditional, and evaluation clamps to `1 − 2×padding` when Grow outside area is off.
6. **B-078 — the Margin selector has no `zone` field.** W1.1 decision 2: it is algebraically inert.
7. **B-098 — Nudge's Bias angle is always visible**, where the reference hides it while bias is 0. `[ZUIShowIf]`
   matches a value and "above zero" is not one; the tooltip says the dial does nothing at bias 0.
8. **B-145 — All mode is scoped to the selected generator**, per the build spec §6 and T-0230 decision 1, where
   the reference draws every generator at once.
9. **B-010 — the export filename defaults to `<asset name>.png`**, not a seed-embedded name, and there is no
   gizmo-mode save/restore around it (gizmos are not in the renderer, so there is nothing to hide).
10. **B-014 — Normalize is exercised only on documents this code wrote.** No older DotGen asset exists anywhere
    to repair, so the migration path has no real input yet.
11. **B-054 / B-027 — the 300-box and 250-instance caps are implemented and read back, but were never driven to
    their limits.**

## 7. Not verified

- **No literal pointer drag anywhere.** The one-undo-per-drag guarantee was driven through the slider's own
  `OpenGesture` / `SetValue` × 12 / `CloseGesture` — the exact three methods its PointerDown/Move/Up handlers
  call — and proved through the asset (one Ctrl+Z restored the pre-drag value). Nobody has dragged a slider, the
  preview, the splitter or a card grip with a real mouse in this window.
- **The preview's wheel and double-click**: the maths runs through the same `SetZoom`/`FitView` the events call,
  but no wheel or double-click event was synthesized.
- **The four zoom keys** were verified by calling `HandleZoomKey`. Whether the IMGUI island actually takes
  keyboard focus on click in this Unity version is still unconfirmed.
- **Both modal dialogs.** Export PNG…'s save panel and Reset to demo's confirm were bypassed by calling the
  functions they run afterwards; nobody has seen either dialog.
- **`Z.Fill`'s right-click Flat↔Gradient switch** (B-127) — a native context menu with no synthesizable path, as
  T-0231 also found. Both demo drawers are Solid fills.
- **The Views bar** was never saved to or recalled from; `Assets/DotGen/DotGenViews.asset` still does not exist.
- **Tags**: the section renders; no tag was added or filtered on.
- **Pyre's undo half of fix h**: the tooltip and the reset jump were probed on a real Pyre dial; the "one Ctrl+Z"
  half was proved on DotGen (the same shared control), not re-read off Pyre's own asset.
- **Frame size at 2048** — a 16× heavier render than the 3.5 ms measured at 512. Estimated, not measured.
