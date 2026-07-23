> **STATUS UPDATE 2026-07-23:** the IMGUI → UI Toolkit switch this design was waiting on HAS happened — the whole editor toolkit (`Laubrary.Zui`, `Z.*`) is now UI Toolkit, and every Laubrary tool window is ported. Per the shelved note below, that means this design is now due for **reconsideration in a UI Toolkit world, not a straight port**. Some of it already exists: `ZuiSection`/`ZuiBox` are collapsible bordered sections that fold, and `Z.Divider` groups within one. Still absent: multi-column field flow, the gear-panel opt-in, cross-column drag, per-project layout persistence, and templates (Zolumn); and the whole Swarm shape-system (PyrePlus). Copied from PreviewLab into the canonical host, where it was missing after the 2026-07-23 merge.

# Zolumn — a new ZUI layout/container primitive

> **Shelved (on hold).** This design and its companion `PYREPLUS_DESIGN.md` are on hold while the
> project investigates switching its editor UI stack from IMGUI to UI Toolkit. Everything below is
> written against ZUI, which is an IMGUI toolkit — if that switch happens, this design (and possibly ZUI
> itself) needs to be reconsidered before any implementation starts. Do not begin building this without
> first checking whether the IMGUI vs. UI Toolkit decision has been made.

## Purpose

A reusable ZUI control: a titled, collapsible, bordered section that lays its child fields out in 1-5 user-configurable columns, with a gear-icon settings panel to opt fields in/out, drag-and-drop to reposition fields (including moving a field to a different column), and **project-scoped persistence** of that layout. It also carries a **template system**: any asset type can be saved as a reusable "quickstart" template and new assets can be created from one (a deep copy — creating from or editing around a template never mutates it). Lives in ZUI (`Assets/Packages/Laubrary/Zui/`) as a general-purpose primitive usable by any Laubrary editor tool, not just Pyre.

## Precedents already in the codebase (reuse, don't rebuild)

- **Outer collapsible bordered box** — `ZUI.ClickableBox` (`Assets/Packages/Laubrary/Zui/Scripts/Editor/ZUI.cs:887`): title row itself is clickable to collapse, `ref bool` state, border/title drawn in both states.
- **Gear icon → expanding settings panel** — `Assets/Packages/Laubrary/Editor/Zounds/TabContents/ZoundBrowsers/BrowserTab.cs`, method `DrawSettingsPanel`: a `GUILayout.Button` using `EditorGUIUtility.IconContent("SettingsIcon")` toggles a bool fed into `ZUI.FoldoutBox`/the underlying `AnimatedFoldout` animation — content pushes following rows down (normal `EditorGUILayout` flow), never an overlay.
- **Style/spacing tokens ("a Zheet")** — `ZUIStyleSheetAsset` (`Zui/Scripts/Runtime/ZUIStyleSheetAsset.cs`) plus `ZUI.UseSheet(...)`, `ZUI.VerticalSpace()`/`HorizontalSpace()`, `ZUI.ControlHeight`, `ZUI.LabelWidthWide`/`LabelWidthNarrow` (`Zui/Scripts/Editor/ZUI.cs:515-571`). Zolumn must read all spacing/sizing from these tokens, never hardcode pixel values, so a tool can later declare its own Zheet and restyle Zolumn with zero new plumbing — this mechanism already fully exists.
- **Drag-to-reorder mechanics** — `Assets/Packages/Laubrary/Editor/Pyre/PyreWindow.cs`, `DrawModifiers` (~line 1422) and `HandleModDrag` (~line 1527): a `≡` grip glyph per row; `MouseDown` on the grip claims the drag by index (`draggingMod`, `draggingModList`); each row's `Rect` is captured during the draw pass; the drop target is resolved by comparing the mouse's Y position against each row's vertical center; mutation (`list.RemoveAt`/`Insert`) is deferred to `MouseUp`, wrapped in `Undo.RecordObject`. This is the mechanical precedent, but it's hand-rolled once, private to `PyreWindow`, and scoped to a single flat list — it should not be re-copied a second time for Zolumn. See "Drag-and-drop belongs in ZUI core" below.
- **Whole-asset preset/template save-recall** — `Editor/Pyre/PyreLayerLibrary.cs`: a project-wide `ScriptableObject`, found via `AssetDatabase.FindAssets("t:PyreLayerLibrary")`, created on demand under `Assets/Pyre/` if missing, storing deep clones (`Layer.Clone()`) so editing the source afterwards can't mutate the saved copy; a `PopupWindowContent` picker (`PyreLayerLibraryPopup`) lists every entry with a live thumbnail (rendered once via the real `BlastRenderer.RenderFrameTexture` on a throwaway spec, cached per popup instance) plus Insert/Delete buttons. Zolumn's template system generalizes this pattern from "one `Layer`" to "any `ScriptableObject` asset type."
- **"New asset" extension point** — `Editor/AssetKit/LaubraryAssetWindow.cs`: `protected virtual void InitializeNewAsset(T item)` and the `"New"` button flow (`LaubraryAssetWindow.cs:150`) — the hook a template picker plugs into (deep-clone the chosen template's fields into the just-created asset).

**Confirmed absent today** (genuinely new work): a width-aware multi-column field flow — `ZUI.HRow` (`ZUIRow.cs`), `ZUI.Flow`/`ZUI.Field` (`ZUIFlow.cs`), and `ZUI.Blocks` (`ZUIBlocks.cs`) are all manually-composed single rows/cells with no auto-wrap logic — a **general-purpose, multi-zone drag-and-drop primitive** (today's only implementation is Pyre's private, single-list `HandleModDrag`), and project-scoped layout persistence.

## Drag-and-drop belongs in ZUI core, not Zolumn

Drag-and-drop reordering is not a Zolumn-specific need — any list-editing ZUI control benefits from it (Pyre's own modifier stack already hand-rolls one). Rather than building Zolumn's cross-column drag handling as bespoke code, extract a general primitive directly into ZUI core (alongside `ZUI.ClickableBox`/`ZUI.FoldoutBox`, in `Zui/Scripts/Editor/`), and have Zolumn be its first consumer:

```csharp
public static class ZUI
{
    // dragId: a stable string identifying this drag gesture across frames.
    // zoneId: which drop-zone a row currently belongs to (a Zolumn column, or Pyre's modifier list, etc.).
    // itemId: a stable string per draggable row (NOT a list index — the whole point is items can move
    //         between zones, so an index into "the list it happened to be in this frame" isn't stable).
    public static ZUIDragScope DragHandle(string dragId, string zoneId, string itemId, Rect rowRect);
}
```

- Generalizes `PyreWindow`'s existing `draggingMod`/`draggingModList`/`HandleModDrag` shape (grip glyph → `MouseDown` claims the drag → row `Rect`s captured during the draw pass → drop target resolved by comparing mouse position against captured rects → mutation deferred to `MouseUp`), but keyed by `(zoneId, itemId)` string pairs instead of `(list, index)`, and drop-target resolution reports **which zone** the mouse is over as well as the row position within it — Zolumn then resolves column-by-mouse-X using the zone report, then row-by-mouse-Y within that zone, exactly as before.
- Consumers (Zolumn's columns, and potentially a future refactor of Pyre's own `DrawModifiers`) own their own data mutation — `ZUI.DragHandle` reports "item X was dropped into zone Y at position Z" and lets the caller decide how to apply that to its own list(s). ZUI has no opinion on what a "zone" or "item" represents.
- Migrating Pyre's `DrawModifiers`/`HandleModDrag` onto this new primitive is *not* required for this pass (Pyre keeps working as-is) — but it's the reason to build the primitive generally rather than let Zolumn duplicate bespoke drag logic a second time.

## Data model

```
ZolumnLayoutSettings : ScriptableObject     // one per project, Editor-only
    List<ZolumnSectionLayout> sections
        string sectionId                     // stable key, e.g. "PyrePlus.Shape"
        int columnCount                      // 1-5
        List<ZolumnColumn> columns
            List<string> fieldIds             // declared order, mutated by drag-and-drop
        List<string> enabledFieldIds          // which opt-in fields are currently switched on
```

Stored at `ProjectSettings/ZolumnLayoutSettings.asset` — a plain `ScriptableObject` asset saved under the project's own `ProjectSettings/` folder (the standard place Unity itself keeps project-scoped editor configuration, e.g. `ProjectSettings/TagManager.asset`), loaded/created the same way `PyreLayerLibrary.Load()` finds-or-creates its asset. This makes the layout genuinely per-project — whether it ends up committed to source control or gitignored is then the project's own normal choice, not something Zolumn needs to special-case.

```
ZolumnTemplateLibrary<T> : ScriptableObject   // one per templated asset type (e.g. PyrePlusSpec)
    List<T> templates                          // deep clones, same idea as PyreLayerLibrary.layers
```

Found/created the same way as `PyreLayerLibrary` (`Assets/<Tool>/<Tool>Templates.asset`, host-project authoring space, never inside the package). "Save as template" deep-clones the current asset's fields into a new library entry. "New from template" deep-clones a chosen template's fields into a freshly-created asset via `LaubraryAssetWindow<T>.InitializeNewAsset` — the template itself is never touched by either direction.

## Control surface

```csharp
using (var z = Zolumn.Begin("PyrePlus.Shape", "Shape", ZolumnOptions.Default))
{
    if (!z.Visible) return;   // collapsed — nothing else to draw
    z.Field("size",     ZolumnFootprint.SingleRow,      () => { /* draw Size control */ });
    z.Field("alpha",    ZolumnFootprint.SingleRow,      () => { /* draw Alpha control */ }, optIn: true);
    z.Field("position", ZolumnFootprint.EnvelopeFourRow, () => { /* draw Position control */ }, optIn: true);
}
```

- `ZolumnFootprint { SingleRow, TwoRowStacked, EnvelopeFourRow }` — each field declares its own row footprint so Zolumn can lay out columns without live IMGUI text measurement (deliberately simpler and more robust than measuring every control's `CalcSize` every frame; no existing ZUI primitive attempts that today).
- `optIn: true` fields only draw once enabled via the gear panel; `optIn: false` (default) fields are mandatory — always drawn, but still participate in column layout and can still be dragged to reposition.
- The gear icon (next to the title row's collapse arrow) opens a settings panel listing every `optIn` field as a checkbox, plus the column-count (1-5) picker — same mechanism as `BrowserTab.DrawSettingsPanel`.
- Every mutation (drag reorder, enable/disable, column count change) writes through to `ZolumnLayoutSettings` immediately and marks it dirty, mirroring `PyreLayerLibrary.Add`/`RemoveAt` (`Undo.RecordObject` + `EditorUtility.SetDirty` + `AssetDatabase.SaveAssets()`).

## Dev plan

1. `ZolumnLayoutSettings` (data-only) + a load/create-at-`ProjectSettings/` helper modeled on `PyreLayerLibrary.Load()`.
2. `Zolumn` static class + `ZolumnScope`: title row (composing `ZUI.ClickableBox`'s collapse behavior), gear icon + settings foldout (composing `ZUI.FoldoutBox`), column-count picker.
3. Column flow layout: given `columnCount` and the section's available width, split evenly, walk each column's `fieldIds` in order, draw each field's action inside a fixed-width `GUILayout.BeginVertical(GUILayout.Width(colWidth))`.
4. **`ZUI.DragHandle`** (new ZUI-core primitive, `Zui/Scripts/Editor/`): generalize `HandleModDrag`'s mouse-event shape (`PyreWindow.cs:1527`) into the `(dragId, zoneId, itemId, rowRect)` API described above — keyed by stable string ids, reporting drop-zone + position rather than mutating a list itself.
5. Zolumn consumes `ZUI.DragHandle` for its column drag-and-drop: each column is a zone, each field id is an item; on drop, Zolumn moves the field id between its own `ZolumnColumn.fieldIds` lists and writes through to `ZolumnLayoutSettings`.
6. `ZolumnTemplateLibrary<T>` + a generic picker popup modeled directly on `PyreLayerLibraryPopup` (thumbnail rendering is caller-supplied, since only the caller knows how to render its own asset type).
7. Extend `LaubraryAssetWindow<T>`'s `"New"` button with a `▾`: "New (blank)" vs. "New from template…" (opens the template picker).

## Verification

- Confirm `ZUI.DragHandle` works standalone with a single zone (equivalent to today's `HandleModDrag` case) before wiring Zolumn's multi-zone (cross-column) usage on top of it.
- Wire Zolumn into one simple section (or a throwaway scratch window) with 4-5 fields, some `optIn`; confirm column count 1-5 reflow, gear-panel toggling, dragging a field into a different column, and that closing/reopening Unity preserves the layout.
- Confirm `ZolumnLayoutSettings.asset` appears under `ProjectSettings/` as a normal diffable asset.
- Confirm "save as template" / "new from template" round-trip without ever mutating the template (edit the new asset, reload the template, confirm it's unchanged).
