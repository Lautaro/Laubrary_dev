# T-0329 — Lazor ported off the legacy IMGUI base

**Outcome:** Lazor is now a normal modern Laubrary tool. It was the last of 19 windows still on the retired IMGUI base; it is now the 21st `ZuiAssetWindow` subclass, and the legacy base has zero subclasses left.

Owner's instruction (2026-09-09): *"Keep lazor. It should be ported and finished/update. Not a priority."* — done as stated, no retirement.

## What changed

| File | Change |
|---|---|
| `Assets/Packages/Laubrary/Editor/Lazor/LazorWindow.cs` | Base swapped to `ZuiAssetWindow<LazorShape>`; per-frame `DrawAsset` replaced by retained-mode `BuildAsset`; new pointer-capture splitter, canvas pane and `IMGUIContainer` host |
| `Assets/Packages/Laubrary/Editor/Lazor/LazorWindow.Layers.cs` | Whole left panel rebuilt from per-frame IMGUI into real ZUI controls |
| `Assets/Packages/Laubrary/Editor/Lazor/LazorWindow.Canvas.cs` | Minimal surgery only — the bespoke painting is untouched |
| `Assets/Packages/Laubrary/Runtime/Lazor/LazorRasterizer.cs` | Empty shapes now render a deliberate "empty" marker instead of a flat slab |
| `Assets/Packages/Laubrary/Editor/Lazor/LazorEditor.asmdef` | Added the `com.Lautaro-Arino.Laubrary.Zui.Editor` reference the new base needs |

## The headline result

`ZuiAudit` walks a window's UI Toolkit tree. Lazor previously drew **zero** UI Toolkit elements, so the audit was structurally incapable of seeing it — it could never pass or fail, it simply wasn't there.

- Before: 0 UI Toolkit elements.
- After: **26** elements in the empty/browser state, **152** with a shape loaded.
- `ZuiAudit` result: **0 findings.**

Every control is a standard ZUI control — no ZUI gaps had to be worked around. The three gaps this project's notes historically warned about (enum dropdown, text input, colour field) are all closed: `Z.EnumDropdown`, `Z.TextInput`, `Z.SliderInt`, `Z.MiniRadio` and `Z.Color` all exist and are used here.

## Defects found and fixed during the walk

These were found by *operating* the window, not by probing it — the element-tree probe reported everything present and correct while two of them were live.

1. **The `✕` delete button was clipped off the panel's right edge.** The layer row's fixed-width glyph buttons overflowed the 288px panel, so deleting a layer was unreachable without horizontal scrolling. Fixed by making the name field's container the row's single elastic element.
2. **The Pen help text was truncated mid-word** ("right-click/E"). Now wraps.
3. **Redundant heading** — a section titled "Layer" containing a heading "Layer — Hull". The layer name is now the section title.
4. **The blank library thumbnail** (the defect named in the task). Root cause confirmed live: `Assets/Lazor/New Lazor Shape 1.asset` has an enabled layer but **zero strokes**, so `LazorRasterizer.Render` drew nothing and returned a flat background — indistinguishable from a failed render. It now draws a faint frame and diagonal, so an empty shape reads as intentionally empty.
5. **A stretching `BaseField`** — my own fix for (1) tripped ZuiAudit's `stretch` rule, which forbids a BaseField growing to fill its row. Re-done with a plain container doing the stretching. This is why the audit mattering is not academic.

## Two premises in the task description were wrong

Both were checked against source before acting on them, and neither is a Lazor defect:

- **"every ZuiAssetWindow shows a greyed Save with a reason"** — no `ZuiAssetWindow` subclass has a Save button, and neither did the legacy base. Assets persist the normal Unity way. Adding one would be new work on the shared base affecting all 21 tools. Not done.
- **"a `Refresh` button stranded ~1000px away at the far right"** — that is `ZuiAssetWindow.BuildBrowser`'s own layout (`Z.Flexible()` before the button), identical in both bases and shared by every tool. Porting reproduces it exactly. Changing it is a base-class decision, not a Lazor one. Not done.

## Verified

- Compiles clean; `EditorUtility.scriptCompilationFailed = false`; zero console errors across the whole session.
- Base type confirmed by reflection as `ZuiAssetWindow<LazorShape>`; all legacy members (`DrawAsset`, `DrawLeftPanel`, `DrawCanvasHud`, `OnZUIEnable`, `DrawVerticalSplitter`) confirmed gone.
- **Undo works end-to-end, driven through the real control callbacks:** `+ Add` 1→2 layers, undo → 1. Thickness slider 0.01298→0.1234, undo → 0.01298. 18 `RecordShape` call sites cover every mutation.
- Canvas renders correctly inside the `IMGUIContainer` — strokes, grid, symmetry guides, forward marker and vertex handles all present (see `shots/lazor-ported.png`).
- No new menu items; no Lazor asset was modified.

## Not verified

- **Live mouse interaction on the canvas** — drawing with the Pen, dragging vertices, middle-drag panning and scroll-zoom were not exercised with a real mouse. The drawing code is unchanged and its rect contract is satisfied, but the `IMGUIContainer` hosting is new, so this is the one thing worth a hand pass.
- **Canvas keyboard shortcuts** (Enter / Escape / Backspace / space-pan) now depend on the `IMGUIContainer` holding focus. It is `focusable` and click-to-focus, matching DotGen's stage, but this was not confirmed by hand.
- The splitter drag was not exercised with a real mouse.

## Left deliberately undone

- **`Assets/Packages/Laubrary/Editor/AssetKit/LaubraryAssetWindow.cs` (283 lines) is now dead code** — zero subclasses, confirmed by reflection; every remaining mention in the repo is a doc comment. It is safe to delete, but deleting shared AssetKit infrastructure is outside "port Lazor" and is the owner's call.
- **`Laubrary/Lazor/Lazor` is the flat-menu anti-pattern** the project rules name explicitly. It cannot simply be flattened because `Laubrary/Lazor/Import SVG…` shares the submenu. Untouched — menus are not changed unasked.
