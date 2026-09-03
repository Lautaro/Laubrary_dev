# DotGen — build spec (PM decisions, 2026-09-03)

**Owner's brief (verbatim intent):** the two POC files beside this document (`POC-DotFoundry-Specification.md`, `POC-DotFoundry.html`) are a proof of concept of a tool to implement in Laubrary. "The tool UI and functionality should be pretty much exactly like it is there but made with ZUI in the same way that Pyre is made. Use the same pattern of having a section/toggle bar on top. The POC doesn't save assets but all assets around this tool should be like Pyre assets. The tool should have browsing for assets just like Pyre and the preview window should also have the possibility to have a backsplash just like Pyre." The owner then named the tool **DotGen** and said he will not be reachable: every open question is decided by the PM, never parked.

**Reading order for the POC:** the specification is normative (§§5–12 give exact maths, ranges, defaults, render order). The HTML is the behavioural reference — when the two disagree, the HTML's *observable behaviour* wins for anything visual, the spec wins for maths, and the discrepancy is written into the behaviour checklist (W0.2). The one spec requirement newer than the HTML — double-click a slider restores its default (§14) — is required.

This document translates the POC into Laubrary. Where it is silent, the POC applies verbatim. Where it says something the POC does not, this document wins (it carries Laubrary's own rules: ZUI, Undo, assets, menus).

---

## 1. Identity and placement

| Thing | Decision |
|---|---|
| Tool name | **DotGen** (owner's name). Never "Dot Foundry"/"DotFoundry" in code, comments, UI, docs or CHANGELOG — only when citing the POC files by name. |
| Runtime folder | `Assets/Packages/Laubrary/Runtime/DotGen/` — asmdef `com.Lautaro-Arino.Laubrary.DotGen`, rootNamespace `Laubrary.DotGen`. References: `ZuiRuntime` (for `ZuiFill`), `com.Lautaro-Arino.Laubrary.PreviewKit` (for `IVisualPreview`), `com.Lautaro-Arino.Laubrary.BackSplash` (for `BackSplashSettings`, as `Pyre.previewBackSplash` does). Check each asmdef's real name in the tree before writing the reference. |
| Editor folder | `Assets/Packages/Laubrary/Editor/DotGen/` — asmdef `com.Lautaro-Arino.Laubrary.DotGen.Editor`, `includePlatforms:["Editor"]`, references the runtime asmdef plus **exactly what `Editor/Pyre/PyreEditor.asmdef` actually references** (per the inventory §8: `com.Lautaro-Arino.Laubrary.Zui.Editor` — the UITK factory, NOT the legacy bare `ZUI.Editor` — `com.Lautaro-Arino.Laubrary.ZuiRuntime`, `com.Lautaro-Arino.Laubrary.AssetKit.Editor`, BackSplash runtime + `.Editor`; PreviewKit arrives transitively). Read that file; do not trust this table over it. |
| Asset class | **`DotGen : ScriptableObject`** in namespace `Laubrary.DotGen` (the same class-named-as-tool shape as `Laubrary.Pyre.Pyre`), `[CreateAssetMenu(menuName = "Laubrary/DotGen", fileName = "DotGen")]`. Implements `IVisualPreview` (thumbnail = the rendered frame, static). Carries `previewBackSplash` (a `BackSplashSettings`, `[HideInInspector]`, lazily allocated exactly like `Pyre.previewBackSplash`) and the persisted view state below. |
| Window | `DotGenWindow : ZuiAssetWindow<DotGen>` in `Editor/DotGen/`, split across partials like Pyre (`DotGenWindow.cs` shell + left pane, `.Preview.cs`, `.Cards.cs`, `.Gizmos.cs`). `[MenuItem("Laubrary/DotGen")]` — **the only menu item this tool gets.** `OpenFor(DotGen)` static like `PyreWindow.OpenFor`. `DefaultFolder => "Assets/DotGen"`, `TypeLabel => "DotGen"`, `NewAssetName => "New DotGen"`. |
| Browsing | Comes from `ZuiAssetWindow<T>` (toolbar assign/New/Browse/Duplicate/Rename/Delete + thumbnail grid). `RenderThumbnail` returns a fresh render of the document at a small size (e.g. 128 px). `AnimateThumbnails => false` (a document is a still). Tags section comes from the base. |
| Demo | `Assets/Demos/DotGenDemo/` with **two authored `.asset` documents**: `DotGen Demo.asset` (the POC §20 default demonstration) and `Three Rows of Buildings.asset` (the §21 acceptance scenario, authored *through the window* by the demo task). No demo scene — DotGen has no runtime component in this programme (it produces a still picture; see §9). No builder scripts; the assets are the deliverable. |
| CHANGELOG | One entry under `## [Unreleased]` in `Assets/Packages/Laubrary/CHANGELOG.md` per task that ships user-visible behaviour; the demo task writes the tool's headline paragraph. Never bump `package.json`. |

## 2. Data model (Runtime)

All authored data is plain `[Serializable]` classes inside the `DotGen` ScriptableObject; **Undo works because every edit goes through `Undo.RecordObject(doc, …)`** (window `Dirty` wrapper, §7).

- **Generators are a FLAT list**, not a nested tree: `List<DotGenerator> generators`, each with `string id` and `string parentId` (`""`/null for the root). Sibling order = order of appearance in the list among entries sharing a `parentId`. The tree (children lists) is rebuilt on demand by a tiny non-serialized index (`DotGenTree`), invalidated on any structural edit. Reason: Unity serialization cannot hold a recursive `[Serializable]` class to arbitrary depth (it truncates at depth ~7 with warnings), and the POC allows unlimited nesting. There is exactly one root: the entry with an empty `parentId`; `DotGen.Reset()`/migration guarantees it exists.
- **IDs**: `prefix + "_" + base36(counter)` where `counter` is a serialized `int nextId` on the document (`g_`, `s_`, `m_`, `d_` for generator/selector/mutator/drawer). Selector references (`selectorId` on mutators and the fill drawer) are IDs; `""` means **All dots**.
- **Modules are `[SerializeReference]` polymorphic** classes (Pyre's own form/modifier pattern): abstract `DotPlacement`, `DotSelector`, `DotMutator`, `DotDrawer`, each concrete type tagged `[DotModule("<typeId>", "<Display name>", "<default instance name>")]`. `DotModuleRegistry` enumerates the concrete types by reflection (per category, stable display order = declaration order in the POC: Grid, Box Row, Radial Grid; Margin, Gradient, Random; Nudge, Warp field, Cull; Fill). Every module has `string id`, `string name`, `bool enabled`. **Defaults are field initializers** (the POC's `createDefaults`) — a fresh `Activator.CreateInstance` *is* the registered default, and that is what double-click reset resolves against. Context-sensitive generator defaults (root vs child) live in `DotGenerator.ApplyDefaults(bool isRoot)`; the two Grid defaults that depend on role (8×7 root vs 4×4 child) are set by the generator when it creates its Grid, not by the Grid type.
- **Every control-bearing field carries `[Range(min,max)]` and `[Tooltip]`** matching the POC tables exactly (units in the label text via `ZuiReflect`'s naming — check how Pyre labels "%" fields and copy that). Conditional controls use **`[ZUIShowIf("field", "Value")]`** (exists in `Zui/Scripts/Runtime/ZUIShowIf.cs`; read it — it takes enum value *names*). Enums for every choice (shape, anchor, area basis, height basis, vertical anchor, flow, field type, force, source, cull action, draw target, use, cell shape, fill source, fill style, gizmo mode).
- **Box Row's role-dependent slider max** (height 1..100% vs 1..1000% depending on *Grow outside area*) cannot be a static `[Range]`. Decision: `[Range(1,1000)]` on the field and the UI **rebuilds the card** on the toggle, clamping via a `ZUIShowIf`-style pair: two fields (`minHeightPercent` for grow-off, `minHeightPercentOverflow` for grow-on)? **No** — that duplicates data. Do it as ONE field with the wide `[Range(1,1000)]`; when *Grow outside area* is off, evaluation clamps height to `1 − 2×padding` anyway (POC §8.2 line `if not growOutsideArea: height = min(...)`), so the wide range is harmless. The checklist notes the deviation: the slider shows 1..1000 in both modes.
- **Fill Drawer colours = `ZuiFill`** (Laubrary's own fill, `Zui/Scripts/Runtime/ZuiFill.cs`), not a hand-rolled colour pair. `fill` (single) and `List<ZuiFill> fills` (random from list). Flat ↔ a Solid ZuiFill; Gradient A→B at an angle ↔ ZuiFill's *spatial linear* gradient with its angle. W0.1 verifies that ZuiFill can (a) hold a two-colour linear gradient with an angle and (b) be evaluated per pixel in local `−0.5..0.5` space with the POC's "half-diagonal extent" rule; if (b) has no clean API, DotGen's renderer evaluates the ZuiFill's *data* itself (solid colour / two-stop gradient / angle) through one small `DotGenFillEval` helper — never a second fill type. Opacity stays a separate `[Range(0,100)]` field. Default list entries and default new-entry colours are the POC's hex values.
- **Dot colour** is a `Color` drawn with `Z.Color` (`Zui.cs:657` — exists; the UI guide's "colour gap" note is stale).
- **Inventory corrections (W0.1, T-0224):** `WrapRow` is a private Launimator helper, not a ZUI factory — use `Z.HGroup`/`Z.Row`. `ZuiFill.Evaluate(life, u, v)` takes u/v in **−1..1** (`ZuiFill.cs:167`), so the fill drawer maps its target's local −0.5..0.5 box to −1..1 before sampling; `ZuiFill.Mode.Linear` + `gradient` + `angleDeg` is the A→B-at-angle gradient. `ZuiReflect` never passes `defaultValue` to `Z.MicroSlider` (`ZuiReflect.cs:328-338, 472`) — W1.2 fixes that. No 9-point control exists — W1.2 builds `Z.AnchorGrid`.
- **Document fields**: `int seed` (0..999998), `int frameSize` (render resolution, `[Range(64,2048)]`, default 512 — the POC's fixed 900 becomes a setting; export uses the same value), `Color background` (#0c1017), `Color guideGrid` (#273140), `bool showGuideGrid` (true), `Color frameStroke` (#64748b), `bool showFrameStroke` (true), `DotGizmoMode gizmoMode` (Hovered default), `string selectedGeneratorId`, `BackSplashSettings previewBackSplash`, `float previewZoom` and `Vector2 previewPan` are **NOT** persisted (POC §17) — they live on the window. `Reset()` gives §4.1 root-only defaults; `ApplyDemo()` gives §20.
- **Migration**: `DotGen.OnValidate`/a `Normalize()` called from the window on load: ensure a root exists, every generator has a placement, lists are non-null, every module has an id, a Fill drawer's list has ≥1 entry, `nextId` exceeds every id in use. Never reorder, never overwrite an existing value.

## 3. Evaluation and rendering (Runtime)

- `DotGenEvaluator.Evaluate(DotGen doc) → DotGenResult` — exactly POC §7: per generator (depth-first, hierarchy order) the evaluated areas, base dots, final dots, mutator traces, counts; child spawning with `hash01(seed+37, dotIndex, childId.Length)`; instance cap; the 300-box Box Row cap. `hash01` is POC §6 **bit for bit** (use `uint` arithmetic with `unchecked`; verify against the JS with three known triples in the W1 probe). No `UnityEngine.Random`/`System.Random` anywhere in this assembly.
- `DotGenRenderer.Render(DotGen doc, DotGenResult res, int size, bool withDots) → Texture2D` (and an overload into a caller-owned `Color32[]`): POC §12 order — clear, background, guide grid (10×10), drawers per generator in hierarchy order (each generator's drawers in list order, targets in stable order), frame stroke, dot markers with the 4 px glow (scaled by `size/900` so the look matches the POC at any resolution). Gizmos are **not** part of the renderer — they are editor-only overlays (§6). Anti-aliasing: shapes are rasterised with 4-sample coverage AA so the picture reads like the POC's canvas; dots are AA discs.
- Rasterisation is bounding-box scanline per target (an oriented rectangle/ellipse/diamond); never a per-pixel loop over the whole frame per target. A 512² frame with the §20 demo must evaluate + render in well under 100 ms on this machine (measure it in the W1 probe and write the number down).
- **Export**: `DotGenRenderer.RenderPng(doc, size) → byte[]`; the window's *Export PNG…* opens `EditorUtility.SaveFilePanel` (default `<asset name>.png` beside the asset) and reveals the file — the same shape as `PyreWindow.ExportGif`. If the chosen path is inside `Assets/`, `AssetDatabase.ImportAsset` afterwards. Gizmos never appear in the export by construction (they are not in the renderer).

## 4. Window layout (Editor) — Pyre's shape

Copy `PyreWindow.BuildAsset` structurally:

```
[ ZuiSectionToggleBar("DotGen", Tags, Views, Frame, Hierarchy, Generator, Placement, Selectors, Mutators, Drawers) ]
[ Tags section (base) ]
[ split:  left ScrollView (Z.ColumnFlow(360)) | 6px splitter | right pane ]
   left units, in this order — each ONE Z.Section with a stable key "dotgen.<name>":
     Views      — ZuiViewBar (store asset Assets/DotGen/DotGenViews.asset, prefs key "DotGen.lastView")
     Frame      — seed (Z.Int + "New seed" button), Frame size (MicroSlider 64..2048, rebuild ranges on commit),
                  background / guide grid / frame stroke (colour + toggle each), Gizmos (Z.MiniRadio Hovered|Selected|All|Off),
                  Export PNG… , Reset to demo (confirm dialog, POC §19.2). Readout line: "{dots} dots · {areas} areas".
     Hierarchy  — the generator tree rows (POC §15.5) + a row of buttons: + Child generator, ▲, ▼, Duplicate, Delete.
     Generator  — the Generator Area card (POC §16.1) for the selected generator: name (Z.TextInput — it is a
                  DECLARATION, the one sanctioned text field), enabled, Dot output, dot colour, dot size,
                  shape, width, height, anchor (3×3), rotation; child-only: area basis, every Nth, spawn chance, instance limit.
     Placement  — Method (Z.MiniRadio over the registry) + the reflected card of the chosen placement.
     Selectors  — cards + "+ Selector" (ZuiMenu listing the registry).
     Mutators   — cards + "+ Mutator" (ZuiMenu), each card has ▲ ▼ ×.
     Drawers    — cards + "+ Fill drawer", each card has ▲ ▼ ×.
   right pane:
     preview IMGUIContainer (class "zui-stage", resizable height bar like Pyre) — POC §17 navigation
     a chrome row: −  100%  +  Fit  |  gizmo mode readout  |  legend of enabled dot-output generators (colour dot + name)
     BackSplash panel: BackSplashZui.Build(doc.previewBackSplash, "Preview backdrop", …, owner: doc) exactly as Pyre
```

- The POC's top bar (brand, pills, Export, Reset) is **absorbed**: pills → the Frame section readout; Export/Reset → Frame section buttons. The POC's Inspector callout paragraph is **not** shown on screen (UI rule: instructions live in tooltips) — put its sentence into the Placement/Mutators/Drawers section tooltips.
- The POC's Inspector sticky header (name, enabled, Root/Child, counts) becomes the top row of the Generator section.
- Tree rows: 20 px indent per depth, enable toggle (Z.Toggle "" like Pyre's layer rows), colour chip, name, subtle metadata line "Grid · 42 dots · 1 drawer", child count right-aligned when > 0, selected row highlighted with Pyre's selection tint. Clicking a row selects (persisted `selectedGeneratorId`) and rebuilds the Generator/Placement/Selectors/Mutators/Drawers sections (rebuild the section BODIES, never the flow — Pyre's `RebuildAllForSelection` idiom). Root cannot move/duplicate/delete; ▲▼ disable at sibling bounds.
- **Every enum is radios** (`Z.Segmented` for ≤3 options on one line, `Z.MiniRadio` otherwise) — the POC's `<select>`s are NOT reproduced as dropdowns. Selector reference = `Z.MiniRadio` over `["All dots", …selector names]` (it is a picker over declared names, never a typed string; renamed selectors re-label live).
- **Anchor picker** = a 3×3 grid of `Z.ToggleButton`s in a small box with the anchor name beside it. If W0.1 finds an existing ZUI 9-point control, use it; otherwise build `ZuiAnchorGrid` + `Z.AnchorGrid(...)` in `Zui/Toolkit/` as a shared control (Pyre has anchors too) — not a private helper in the window.
- **Cards** (selector/mutator/drawer): the card-layout rules in `ui-layout-rules.md` §"Card layout": header = fold caret, grip (ZuiReorder), enable toggle, name (rename-in-place TextInput), category label, flexible gap, ×; body = the reflected fields (`ZuiReflect`) with `[ZUIShowIf]` gating, plus the Selector picker row first for mutators/drawers. A disabled module folds its body to a single subtle "Disabled" line. Collapse state is session-only (a `ConditionalWeakTable`/dictionary keyed by module id on the window). **Hovering a card sets `hoveredModuleId`** (PointerEnter/Leave on the card root) and repaints the preview; the hovered/selected card gets the cyan outline class.
- **Sliders**: `Z.MicroSlider` with `defaultValue` supplied (ZuiReflect must pass the type default so double-click restores it — W0.1 checks whether ZuiReflect already does; if not, W1.2 makes it read the default from a fresh instance of the owner type). The double-click reset routes through the same `onBeforeMutate`/`onChanged` as a drag, so it is one Undo step and re-evaluates.
- Stable workspace: the preview never moves when a card appears/disappears (it is in the right pane); the chrome row is fixed height.

## 5. Preview navigation (Editor)

POC §17 verbatim: fit size `max(64, min(viewW−32, viewH−32))`, zoom 25%..800%, ± factor 1.25, wheel `exp(−deltaY×0.0015)` anchored at the cursor, drag pans, double-click fits, `+`/`=`/`−`/`0` keys while the preview has focus, pan clamped by the POC formula. Zoom/pan are window state (not persisted). The frame texture is drawn with `GUI.DrawTexture` point-filtered when zoom ≥ 200% (pixel-art habit) and bilinear below; the BackSplash is drawn behind it exactly like `PyreWindow.DrawBackdrop` (fill → image → frame). The 24 px viewport grid of the POC is replaced by the BackSplash (a user who wants a grid picks one there).

## 6. Gizmos (Editor)

`DotGenGizmos` draws POC §13 over the frame in preview space with `Handles`/`GL` inside the IMGUI island: Generator Area (dashed outline + anchor mark, generator colour), Grid/Radial/Box Row placement guides, selector weight dots (amber, alpha/size from weight), margin band, gradient arrow/wave, nudge/warp vectors, cull red ×s, point-warp centres/radii, line-warp lines, drawer magenta targets — with the POC's caps. Modes Hovered/Selected/All/Off; hover comes from cards (§4) and from tree rows for the Generator Area gizmo. Gizmos consume the evaluator's traces (`DotGenResult`), so they never re-run the pipeline.

## 7. Undo, dirty, re-evaluation

- One `Dirty(Action edit, string label = "Edit DotGen")` on the window: `Undo.RecordObject(doc, label)` → edit → `EditorUtility.SetDirty(doc)` → `MarkDirty()` (invalidate `DotGenResult` + render, repaint preview, refresh readouts/tree metadata). Every data edit goes through it (ZuiReflect's `onBeforeMutate` hook = the record, `onChanged` = the mark). One undo per drag gesture (check how Pyre/ZUI achieve that today and follow it).
- Structural edits (add/remove/reorder/duplicate/select) rebuild the affected section bodies; a slider drag never rebuilds anything (Pyre's Bug-1 lesson: rebuild only on pointer-up when a range depends on the edited value — Frame size).
- Undo/redo → `ZuiWindow.Rebuild` (base) → `OnBeforeRebuild` invalidates the result cache (Pyre's `previewDirty` idiom).
- Evaluation + render happen lazily on the next preview repaint, at most once per frame; the preview draws the cached texture otherwise.

## 8. Decisions on things the POC leaves to the implementer (decided here, not to be re-asked)

1. **New asset = the §20 demo**, not the bare §4.1 root. A new user sees something, immediately. *Reset to demo* restores §20 after a confirm dialog. The §4.1 bare state is reachable by deleting the two children.
2. **Duplicate generator** appends " copy" to the copy and every descendant, regenerates ids, resets selector references in the copy to All dots (POC §18). **Delete** asks nothing (POC) — but it is Undo-able through `Dirty`, which is why no dialog is needed.
3. **Frame size is a document setting** (64..2048, default 512). The POC's 900 is a browser constant; a Unity asset needs the size as data. Dot marker radius and glow scale with `size/900`.
4. **Gizmo caps** are POC values. **Safety caps** (300 boxes, 250 instances) are POC values.
5. **Colour hexes** in the POC are exact defaults (dot colours, fills, background, grid, frame).
6. **Names**: generator/module names are declarations → `Z.TextInput` is correct there; the tree relabels live on commit.
7. **No runtime player, pool or `IChunkAnimation`** in this programme; DotGen outputs a still picture. The renderer lives in Runtime so a future task can add one without moving code.
8. **No keyboard shortcuts beyond the preview's four keys.**
9. **Pixel/pan/zoom are window-instance state**; view fold/gear state goes through the Views bar like Pyre.
10. **Thumbnail** = render at 128 px without dot glow scaling issues (glow at that size is fine).
11. **`showDots` off** hides markers only (dots still drive children/drawers) — POC §12.
12. **Radial Grid default values** initialise on switching to that method (POC §8.3) — `Activator.CreateInstance` does that for free; switching *back* to Grid creates a fresh Grid with the role default (8×7 root / 4×4 child), i.e. a method switch does not remember the previous method's settings (POC behaviour: "Never show dormant controls from another method"; the HTML keeps one config object per type — W0.2 checks whether it *preserves* the other type's values; if it does, DotGen keeps a per-generator `List<DotPlacement> placementBank` so switching back restores them. Implement whichever the HTML does).

## 9. Waves and tasks (AgentHQ node `DotGen-2026-09-03`)

| Wave | Task | Model | Editor | Summary |
|---|---|---|---|---|
| W0.1 | Inventory | sonnet | none | file:line facts for everything §2–§7 lean on (ZuiReflect defaults/ShowIf/Range, MicroSlider defaultValue, ZuiFill gradient+eval, BackSplash draw, ZuiMenu/ZuiReorder/ViewBar/ToggleBar signatures, Pyre `Dirty`/preview/zoom code, PyreGif save-panel, asmdef names, IVisualPreview contract, any 9-point control, colour control). |
| W0.2 | Behaviour checklist | sonnet | none | Every control/range/default/formula from spec + HTML as an acceptance table; HTML-vs-spec discrepancies decided; §8.12 question answered from the HTML. |
| W1.1 | Runtime core | opus | YES | Model, ids, registry, 10 modules, evaluator, renderer, export bytes, defaults/demo, Normalize, IVisualPreview; W1 probe (determinism triple-check of hash01 vs JS, §20 counts, timing). |
| W1.2 | ZUI expansions | opus | none | Only what W0.1 proves missing: ZuiReflect passing type defaults to MicroSlider (double-click reset), `Z.AnchorGrid` if absent, colour control if absent. Shared controls in `Zui/Toolkit/`, documented in `zui.md`. |
| W2.1 | Window shell | opus | YES | ZuiAssetWindow + toggle bar + Frame + Hierarchy + Generator card + preview (nav, BackSplash, chrome, legend) + Undo/Dirty + export + reset + thumbnail. Defines the card contract (`DotGenCard`) W2.2 builds on. |
| W2.2 | Module cards | sonnet | none | Placement method switch + reflected card; selector/mutator/drawer card lists with add menus, reorder, enable, rename, remove, selector pickers, fill list rows, hover → gizmo id. |
| W2.3 | Gizmos | sonnet | none | `DotGenGizmos` per §6 with caps and modes. |
| W3.1 | Demo + docs | sonnet | YES | Author the two demo assets through the window (§21 scenario step by step), CHANGELOG paragraph, `DOTGEN-QUICK-MANUAL.md`. |
| W3.2 | Audit | opus | YES | Coverage sweep: every checklist row traced to a control and to a pixel change; ZuiAudit clean; undo per drag; double-click reset on every slider; determinism; export; re-entry after domain reload; hover gizmos; fixes applied. |
| W4.1 | PM handover walk | Fable (PM) | YES | The Handover Walk, temporal (things move under the pointer), three buckets. |

Editor rights are exclusive: W1.1 → (PM compiles W1.2) → W2.1 → (PM compiles W2.2 + W2.3) → W3.1 → W3.2 → W4.1.
