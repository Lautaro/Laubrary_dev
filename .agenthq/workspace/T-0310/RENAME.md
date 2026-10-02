# T-0310 — Views bar Rename

File touched (Shaper worktree only, code-only, not committed): `Assets/Packages/Laubrary/Zui/Toolkit/ZuiViewBar.cs`.
`ZuiViewBar` is shared toolkit code, so the change lands in every host at once (Shaper's `ShaperWindow`
and `PyreWindow` both use it).

## CHANGELOG

CHANGELOG: ZuiViewBar's saved-views bar gets a Rename button, so renaming a view is one click instead of
Save-as-under-new-name + Delete-view-on-the-old.

## What changed

Bar layout was: `Views | picker | Apply | Update | Delete view | [name field] | Save as`.
New layout: `Views | picker | Apply | Update | Delete view | [name field] | Rename | Save as`.

- **Reuses the existing name field** — no new control, no new section, no menu item (per
  `ShaperHarmony/RULES.md` rule 6). Placed the Rename button right after the field and before Save as,
  since both consume the same typed text: type a name, then either Rename the picked view to it, or
  Save as a brand-new view under it.
- **`RenamePreset(oldName, newName)`** — new private method in `ZuiViewBar`, the third leg of the preset
  CRUD alongside the existing `SaveInto`/`DeletePreset`. It renames the `ZuiViewPreset.name` in place
  (never touches `entries`), so the picker stays on the SAME entry, just relabelled — this is the actual
  reason it's one operation and not Save-as+Delete: the old two-step round-tripped through
  serialize/deserialize of the entries and briefly had two presets in the store.
  - `Undo.RecordObject(store, "Rename Zui View")` before the mutation, `EditorUtility.SetDirty` +
    `AssetDatabase.SaveAssetIfDirty(store)` after — same pattern as `SaveInto`/`DeletePreset`, so a
    rename is undoable exactly as far as they are (which is: undoes the asset edit; EditorPrefs writes
    below are not undo-tracked, same as the existing `_prefsKey` writes elsewhere in this file — that's
    a pre-existing property of this bar, not something this change introduces or needs to fix).
  - If the renamed view was the `<Tool>.lastView` EditorPrefs pointer, the pointer is rewritten to the
    new name in the same call, so a domain reload still restores the same view under its new name.
- **`RefreshRenameState()`** — greys the Rename button with a reason in its tooltip (the same
  `SetEnabled(false)` + `tooltip = reason` pattern `ZuiReflect.cs:419` already uses) rather than letting
  it silently no-op, for every case where pressing it right now would do nothing or something surprising:
  - nothing picked in the dropdown → "No view is selected to rename."
  - nothing typed in the name field → "Type the new name above first."
  - typed text already equals the picked view's name → "That is already this view's name."
  - typed text collides with a DIFFERENT existing view → `A view named "X" already exists.`
  - otherwise enabled, tooltip reads `Rename the selected view "A" to "B".`
  - Wired to refresh on: the name field's `onChanged`, the picker's value-changed callback, the end of
    `RefreshPicker` (so it's current after Save as / Update / Delete / Rename all run it), and
    `RestoreLast()` (which sets the picker without raising its change event, so it needs an explicit
    call to catch up).
- `RenamePreset` itself repeats the same guard (belt-and-braces) so it can't act even if called some
  other way with the button not actually reflecting current state.

## Undo note the card asked me to call out explicitly

Per the CLAUDE.md UI Guide's Undo rule, the `ZuiViewStore` ScriptableObject edit (the preset's `name`
field) IS wrapped in `Undo.RecordObject`, matching `Update`/`Delete view` exactly — there was nothing new
to decide there. The `<Tool>.lastView` EditorPrefs pointer rewrite is NOT undo-tracked, but that's true of
every other EditorPrefs write already in this file (`SaveInto`, `ApplyPreset`, `DeletePreset`) — EditorPrefs
isn't serialized project data, so Unity's Undo system has nothing to hook there regardless.

## How the PM should verify by eye

1. Point Coplay/unity-mcp at the Shaper editor (per this project's CLAUDE.md targeting rule), open the
   Shaper window, and get at least two saved views on the Views bar (Save as "A", change something,
   Save as "B" — or use whatever views T-0307's probes already left in place).
2. Pick "A" in the dropdown. Type "A2" into the name field. The **Rename** button (sitting between the
   name field and Save as) should be enabled with tooltip `Rename the selected view "A" to "A2".`. Click
   it: the dropdown should still show one fewer... no — should still list the SAME NUMBER of views, now
   showing "A2" selected in place of "A", and the window's arrangement should be unchanged (rename never
   re-applies a view, only relabels it).
3. Try the greyed cases: clear the name field (Rename greys, tooltip "Type the new name above first.");
   type the CURRENT view's own name back in (greys, "That is already this view's name."); type the name
   of the OTHER saved view "B" (greys, `A view named "B" already exists.`).
4. If "A2" was the `Shaper.lastView`/`Pyre.lastView` pointer (whichever it was before rename), close and
   reopen the window (or force a domain reload) and confirm it still restores onto "A2" — the pointer
   rename round-trips.
5. Check the bar still fits without a horizontal scrollbar at the Shaper window's narrow width (~820px);
   the row already wraps via the existing `Wrap.Wrap`, and Rename is a short single-word button so it
   should not push anything new into overflow, but this needs an eye check at real window width since I
   did not drive the editor.

## Verification buckets

- **Verified by probe:** none — this task was code-only, no Unity CLI/Coplay/compile per the card and
  `ShaperHarmony/RULES.md` rule 4.
- **Verified by eye:** none — same reason; I did not open the editor.
- **Self-reviewed, not compiled:** read `ZuiViewBar.cs` in full before and after the edit, checked
  `Z.Button`/`Z.TextInput` signatures against `Zui.cs` for exact parameter match, checked
  `ZuiReflect.cs:419` for the grey-with-reason pattern being copied, traced every new call site
  (`RefreshRenameState` from the field's onChanged, the picker's callback, `RefreshPicker`, and
  `RestoreLast`) to confirm `_renameBtn`/`_newName` are non-null wherever it's called (the only path
  before `_renameBtn` exists is during `Build()` itself, guarded by the `if (_renameBtn == null) return;`
  at the top of `RefreshRenameState`). No `csc`/Roslyn binary was available in this worktree to run an
  offline compile; the PM's normal Unity compile check is the first real compile this file gets.
