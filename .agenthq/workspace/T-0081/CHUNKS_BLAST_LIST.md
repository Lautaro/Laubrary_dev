# T-0081 — Chunks blast list as uniform cards

**File changed:** `D:\UNITY\Laubrary Dev\Assets\Packages\Laubrary\Editor\Chunks\ChunkWindow.PyreSpawn.cs` (rewritten). **No other file was touched** — `ChunkWindow.cs` did not need editing, and nothing under `Zui\`, `Runtime\Chunks\`, `ChunkWindow.Layers.cs`, `ChunkWindow.Timeline.cs` or `ChunkModules.cs` was modified.

**Nothing has been compiled or run.** There is no Unity editor involvement in this pass at all — no Coplay, no `unity.exe`, no probe. Every claim below is a code-reading claim. Signatures were checked against the ZUI/Unity sources named in each line, but a compile is still the PM's step.

## Requirement → code trace

### A. One "Blasts" section holding a uniform list of cards
- `BuildPyreSpawn(VisualElement root, ChunkSpec c)` — builds exactly ONE `Z.Section("Blasts", …, "chunks.blasts")`. The per-blast `Z.Section`s are gone; so is the "Pyre Spawn" title and every "Blast N" ordinal in this file.
- `c.pyreSpawn` is card 0, added via `firstHost.Add(BuildBlastCard(null, c, c.pyreSpawn, -1))`; every `c.blastGroups[i]` follows via `listHost.Add(BuildBlastCard(listHost, c, c.blastGroups[i], i))`. **One builder, `BuildBlastCard`, for both**, and one body builder, `BuildBlastBody`, called from it — so no card can express less than another.
- The two genuine asymmetries are per-card capability branches *inside* that one builder, not a second code path: the grip (`if (!first && listHost != null) … else` a 16px spacer) and the `×` (`if (first) … Clear … else … Remove …`). Everything else is identical.
- Section title is "Blasts" — the user's word (explosions), not the module name.

### B. Pyre card shape
- `BuildBlastCard`: `Z.Box(null, null)` (untitled, unkeyed — a per-item card is not captured view state) → a `zui-row` header with `flexWrap = Wrap.NoWrap` set explicitly → a separate `body` `VisualElement` → `ZuiFoldCard.Wire(m, header, body, enableToggle, removeBtn, name)`, keyed on the **module instance**, so fold survives rebuild/undo/reorder.
- Header order: caret (prepended by `ZuiFoldCard`) → `≡` grip (or its 16px spacer) → `Z.Toggle("")` enable → the name → `Z.Flexible()` → `×` at `.W(22f)`.
- **Name is an inline `Z.TextInput` on the header**, with `zui-audit-allow-stretch`, `flexGrow:1`, `minWidth:60`, `maxWidth:220`. `BuildBlastGroupIdentityRow` and its `▲ ▼` buttons are **deleted**; there is no "Name" field in any body.
- Placeholder when nothing is typed: `name.textEdition.placeholder = BlastDerivedName(m)` + `hidePlaceholderOnFocus = true`. `BlastDerivedName` mirrors `PyreSpawnModule.DisplayName` minus the authored label (asset name → first pool entry + "…" → "Blast"), so a card is never nameless and never numbered.

### C. Reorder by grip
- `ZuiReorder.MakeGrip(grip, box, listHost, (from, to) => MoveBlastGroup(c, from, to))`. The drag unit passed is `box` — the whole card, header **and** folded body — not the header row.
- `listHost` holds **only** group cards: `firstHost` (the spec's own blast) and the `+ Add blast…` row are both outside it, so ZuiReorder's insertion line and index math never see a non-row.
- No `CapturePointer` is hand-rolled here; the only capture is `ZuiReorder`'s own, inside the toolkit.
- `MoveBlastGroup` → `MutateBlastGroups` is **unchanged**, including the delay-carrying block: snapshot every group's delay keyed **by the group OBJECT**, mutate, drop every `"Blast "` lane, re-key from the new positions. Not one line of that method was edited beyond the undo label string ("Reorder blast groups" → "Reorder blasts", which is display text on the undo stack, not a persisted key).

### D. Add and remove, discoverable
- `BuildAddBlastButton(ChunkSpec c)` → a single visible `Z.Button("+ Add blast…").W(110f)` added via `sec.Add(Z.Row(BuildAddBlastButton(c)))` — **inside** the section, directly under the list, never loose in the window body.
- The pool's button is renamed to **`+ Add to pool…`** `.W(124f)`. Both tooltips now state plainly which of the two things they do and explicitly disclaim the other ("It does not add another blast to the burst; that is '+ Add blast…' below." / "(Not the same as the Pool inside a card, which is one blast picked at random from several.)"). The Pool box's own tooltip says the same.
- Remove: group cards remove themselves with the index **re-resolved at click time** (`c.blastGroups.IndexOf(m)`), routed through `MutateBlastGroups` so the delay carrying still runs.

**Empty-state decision, and why.** Neither of the two offered options is literally available, because `c.pyreSpawn` is a serialized FIELD on `ChunkSpec`, not a list entry — the list cannot empty out past it, and a "guard the last one" refusal would make card 0's `×` a button that never does anything. I chose a third form of the same idea: **card 0's `×` CLEARS** — `ClearFirstBlast(c)` replaces `c.pyreSpawn` with a fresh (disabled, empty) `PyreSpawnModule` in one undoable step. That keeps the guarantee the guard exists for (there is always at least one blast slot) while keeping the button honest — it always performs something, and its tooltip says exactly what ("the first blast is built into the spec, so this empties it and switches it off rather than removing the card"). The resulting empty state is not a dead end: the section shows one header-only card whose toggle arms it, plus `+ Add blast…` which creates a *fully populated* card (it opens the browser and the picked asset is set in the same gesture). I rejected the more elegant "promote `blastGroups[0]` into `pyreSpawn` on removing card 0" because promotion moves a blast between two DIFFERENT timeline-lane keyspaces (`"Pyre Spawn"` vs `"Blast N"`), and getting that wrong silently swaps two delays — exactly the regression the brief told me not to risk.

`ClearFirstBlast` also drops the `ChunkModules.PyreSpawn` timeline lane, because that lane is keyed to the SLOT rather than to the blast: left behind, it would silently apply the old delay to whatever is picked next, and with the module off the Timeline section does not even render the lane for the user to see it. This is a data edit inside `Dial`'s undo contract, not a model change.

### E. Collapsed state is informative
- `sec.SetHeaderSuffix(() => BlastCountSuffix(Spec))`; `BlastCountSuffix` returns `" (N)"` where N counts **armed** blasts (`pyreSpawn.enabled` + enabled groups), and `""` when N is 0 — a folded section with nothing armed really is hiding nothing. So a folded section reads "Blasts (3)".

### F. Off ⇒ don't build the body
- `BuildBlastCard`: `VisualElement body = null; if (m.enabled) { body = new VisualElement(); BuildBlastBody(…); box.Add(body); }`. `ZuiFoldCard.Wire` is null-body-safe, so a disabled card is header-only with no caret.

## PM's four extra defects

1. **D-01 (unticked blast permanently undeletable) — fixed, and I checked the disabled path specifically.** The header (grip / enable toggle / name / `×`) is built *before* and *outside* the `if (m.enabled)` guard, and the guard now only wraps `body`. Reading `BuildBlastCard` with `m.enabled == false`: the grip is still added, `enableToggle` is still added, `name` is still added, `removeBtn` is still added and `header` is still `box.Add(header)`-ed; only `BuildBlastBody` is skipped. So a disabled group card still shows a working `×` that calls `MutateBlastGroups(… RemoveAt …)`, and a disabled first card still shows a working `×` that calls `ClearFirstBlast`. **Verified by code-reading only — nobody has clicked it.**

2. **D-03 (`+2` ordinals elsewhere) — I touched none of them.** `ChunkModules.BlastGroupTrack` is called from exactly two places in my file and both are inside `MutateBlastGroups`, unchanged: the snapshot loop (`string key = ChunkModules.BlastGroupTrack(c.blastGroups[i], i);`) and the re-key loop (`moduleName = ChunkModules.BlastGroupTrack(c.blastGroups[i], i)`). I also left the `t.moduleName.StartsWith("Blast ")` sweep exactly as it was, because it must keep matching the string that method produces. Nothing I added feeds a card title, a placeholder or a name back into that key — the card's display name comes from `m.label` / `BlastDerivedName(m)` and is used only for the header text and for the *desired* name of a newly declared layer slot. The call sites in `ChunkWindow.Layers.cs:451`, `ChunkWindow.Timeline.cs:186` and `Runtime/Chunks/Modules/ChunkModules.cs:73` were **not** edited and are yours.

3. **D-05 (shared Pool fold key) — fixed.** `BuildPyreSpawnPool` now takes `int index` and keys its box `BlastStateKey(index) + ".pool"`; `BuildBlastSeveral` likewise uses `BlastStateKey(index) + ".several"`. `static string BlastStateKey(int index) => index < 0 ? "chunks.blast.first" : "chunks.blast." + index;`. Note these are position-keyed, so a *reorder* carries a card's fold state to whatever now sits at that position. That is pure view state (Chunks has no `ZuiViewBar`, so nothing else consumes these keys), and index keying is what the old per-blast section keys already did.

4. **D-07 (no depth affordance on a fresh spec) — handled inside my file, by reusing the Layer Stack section's own machinery rather than duplicating it.** `BuildPyreSpawnLayerSlot` **never returns null now**:
   - *No slots declared* → a `Z.Field("Layer slot", …, Z.Button("+ Add depth slot…").W(140f))` whose tooltip says the recipe declares no slots and that every blast is therefore drawing at the emitter's flat order.
   - *Slots declared* → the `Z.MiniRadio` picker as before, plus a permanently-present `Z.Button("New slot…").W(90f)` after a `Z.Flexible()` (never added/removed, so clicking through slots does not shift it out from under the pointer).
   - Both buttons open `ShowNewSlotForBlastMenu`, a two-item `Z.Menu` ("Behind everything" / "In front of everything") that calls the **existing** `AddSlotForModule(c, desired, v => m.layerName = v, front)` from `ChunkWindow.Layers.cs` — one `Dial` that declares the slot and assigns this blast to it — then `Rebuild()` so this card's picker appears. It is a partial class, so this is a call, not an edit; `ChunkWindow.Layers.cs` is untouched.
   - I chose "a button that creates a slot and assigns it" over "a disabled picker with an explanatory tooltip" because the disabled version still leaves the user having to find the Layer Stack section — the rulebook's "a step that requires being TOLD by you is a missing feature". The label ends in "…" because pressing it opens a two-item menu rather than acting immediately (Label = action rule, case 2).
   - The picker's tooltip now also reports the resolved sortingOrder and, **only when it is actually true**, that N modules share this slot — via the existing `LsSlotUsers(c, slot)`, the same helper the Layers panel uses.
   - What I did **not** do: change the `layerName = "Blast"` default for a newly added blast. That default lives in `PyreSpawnModule`'s field initializer (runtime data model, off limits), and overriding it at the add site would be a silent behaviour change that makes a new blast resolve to *no* slot on specs that do have a "Blast" slot. The in-card affordance is the fix I can make honestly from this file.

## Capability-preservation checklist

Every control that existed in the old `BuildBlastSection` and its helpers, and where it is now:

| Old control | Now |
|---|---|
| Section enable (header toggle) | Per-card enable `Z.Toggle("")` on the card header — **and now reachable while disabled** (was not) |
| Group name text field ("Name" in the body) | Inline `Z.TextInput` on the card header, with a derived placeholder. Now available on the FIRST card too (a capability gain; `label` already existed on the module) |
| `▲ ▼` move buttons | Replaced by the `≡` grip + `ZuiReorder.MakeGrip`, same `MoveBlastGroup` semantics |
| `×` remove | On the card header at `.W(22f)`, index re-resolved at click |
| Blast picker (`LauAssetElement.Build`, `IChunkEffectSpawner`) | `BuildBlastBody`, first row, unchanged incl. the pool-overridden tooltip variant |
| Pool box: entry chips, per-entry `×`, add button | `BuildPyreSpawnPool`, unchanged except the per-blast fold key, the renamed/`.W()`-sized add button and the clarified tooltips |
| "Several" toggle | `BuildBlastSeveral`, unchanged |
| Formation dials: Shape, Count, Length, Angle°, Radius, Arc°, Start angle°, Scatter, Stagger s, Jitter, Order, Shape seed | `BuildBlastSeveral`, all present, unchanged controls (`Z.Segmented`, `Z.MicroSlider`, `Num2F`, `Z.MiniRadio`, `Int2F`) |
| Rotation mode radio + Fixed angle + Random angle range | `BuildPyreSpawnRotation`, unchanged |
| Scale (`Z.MinMax`) | `BuildBlastBody`, unchanged |
| Seed (`Int2`) | `BuildBlastBody`, unchanged, still packed on the Scale row |
| Offset / Centre offset (`Z.Vector2Field`) | `BuildBlastBody`, unchanged incl. its `onBeforeMutate` one-undo-per-gesture contract |
| Layer slot picker | `BuildPyreSpawnLayerSlot`, unchanged picker + a new way in when no slots exist |
| "Superseded by Spawn Formation" warning | `BuildBlastBody`, still first-blast-only (correct: `ChunkModules.Run` substitutes the formation for `pyreSpawn` only), now carrying a tooltip it previously lacked |
| Timeline delay carrying across add/remove/reorder | `MutateBlastGroups`, byte-for-byte the same logic |

Nothing was dropped.

## Deliberately not done

- **No partial rebuild (`_blastsBody` / `RebuildBlasts()`).** Pyre pattern §7 says never full-`Rebuild()` on add/remove/reorder, but in Chunks a structural blast change also changes the Timeline's lanes and the Layer Stack's Modules rows, both of which live in other sections. A section-local rebuild would leave those stale, which is a worse bug than a rebuild. Structural changes keep routing through `MutateBlastGroups` → `DialAndRebuild` exactly as before.
- **No `Z.ColumnFlow`, no section icon, no `ZuiSectionToggleBar`.** Those are window-shape changes across every Chunks section, not this task.
- **Did not change `PyreSpawnModule`, `ChunkSpec`, `ChunkModules` or any lane key.**
- **Did not touch the ordinal labelling in `Layers.cs` / `Timeline.cs` / `ChunkModules.cs`** — PM's separate pass.

## Risks the PM should check by eye

1. **`name.textEdition.placeholder` / `hidePlaceholderOnFocus`.** Confirmed present as settable properties in this exact editor (`ITextEdition.placeholder` and `set_placeholder` / `set_hidePlaceholderOnFocus` both found in `6000.3.10f1/Editor/Data/Managed/UnityEngine/UnityEngine.UIElementsModule.dll` and its XML docs), and `textEdition` is confirmed on `TextInputBaseField<T>` — but this is the **only new-to-this-codebase API in the change**, so if anything fails to compile it is almost certainly these two lines. Nothing else depends on them.
2. **The card's fold zone is now narrow.** The caret is `pickingMode = Ignore`, the grip swallows its own pointer-down, and the toggle / name / `×` are all passed to `ZuiFoldCard` as `nonFolding` (which stops their pointer-down). That leaves the caret area and the `Z.Flexible()` gap as the only places a click folds the card. I capped the name at `maxWidth 220f` precisely to guarantee that gap exists; please confirm by eye that a card actually folds when clicked in the gap, at a narrow pane width too.
3. **Passing the name field as `nonFolding` registers a `StopPropagation` on its `PointerDownEvent`.** By my reading this fires on bubble-up, after the inner text element has already taken focus, so click-to-edit and drag-to-select should be unaffected — but that is reasoning, not observation. Worth one click and one drag inside a name field.
4. **`isDelayed = true` on the name field.** Deliberate: without it the callback fires per keystroke and `Dial` re-slices the whole preview (`RefreshChunkPreview` does a `GetPixels32` + slice) on every character. Consequence: the typed name commits on Enter or focus-loss, not live, and the Timeline lane's gutter label therefore only picks it up on the next rebuild. If you would rather it be live, the change is one line — but do not swap it for a per-keystroke `DialAndRebuild`, which would destroy the focused field mid-typing.
5. **`ShowNewSlotForBlastMenu` calls `Rebuild()` after `AddSlotForModule`.** `AddSlotForModule` sets `_lsFocusSlot` and then `RebuildLayerSlots` consumes it to focus the new slot's name field; my extra `Rebuild()` runs `BuildLayerStack` → `RebuildLayerSlots` again, so focus *should* still land — but that is a two-step interaction across two files and is worth watching. Also: if your D-03 pass changes `AddSlotForModule`'s signature, this call site needs updating with it.
6. **Fold/`BoxKeyed` state keys changed** (`chunks.pyrespawn*` / `chunks.blastgroup.N*` → `chunks.blasts`, `chunks.blast.first*`, `chunks.blast.N*`). Existing saved fold state for those boxes is orphaned once; they come back at their default state. Unavoidable, and harmless — Chunks has no `ZuiViewBar`.
7. **`c.blastGroups.RemoveAll(g => g == null)` runs at the top of `BuildPyreSpawn`, without Undo.** It is a null-repair mirroring `ChunkSpec.OnValidate`, done *before* building rather than mid-build (a structural mutation from inside a builder would re-enter `Rebuild()` while the outer `BuildAsset` is still on the stack). A null entry can only come from a broken deserialize and already breaks the position-keyed lanes, but flagging it since it is a silent write during a draw.
8. **`ZuiAudit`** has not been run — it needs a laid-out window. The name field carries `zui-audit-allow-stretch`; every other control has an explicit width (`.W(22f)`, `.W(90f)`, `.W(110f)`, `.W(124f)`, `.W(140f)`, `Wide`, `Num`).
9. **Not verified by eye, at all:** the whole thing. No card has been folded, dragged, added, removed or cleared by a human or a probe.
