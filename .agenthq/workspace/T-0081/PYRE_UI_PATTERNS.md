# Pyre window — transferable UI patterns (T-0081 scout report)

Read-only extraction from `Assets/Packages/Laubrary/Editor/Pyre/*.cs`. Every pattern below is a thing Pyre's window actually does, with the code that does it, and a one-line note on how the Chunks window would apply it. All paths absolute; all line numbers as of the working tree on `feat/lathe`, 2026-08-25.

The single most important structural fact: **Pyre has TWO variable-length card lists and they use the SAME shape** — the per-layer modifier stack and the spec-wide Global Modifiers stack literally share `BuildModifierBlock` verbatim, differing only by which `List<>` and which rebuild action they are handed. A third list (Layers) is a compact one-line-per-item variant of the same idea. That reuse is why the window feels consistent, and it is the thing Chunks' "blast groups" should copy wholesale rather than reinvent.

---

## 1. The repeating card = `Z.Box(null, null)` + a `zui-row` header + a reflected body + `ZuiFoldCard.Wire`

`D:\UNITY\Laubrary Dev\Assets\Packages\Laubrary\Editor\Pyre\PyreWindow.Modifiers.cs:266-322` — the whole card is ~50 lines and has no per-type code at all.

```csharp
VisualElement BuildModifierBlock(VisualElement listHost, List<PyreModifier> list, int index, Action rebuild)
{
    var m = list[index];
    var box = Z.Box(null, null);                 // UNTITLED box — the header row carries identity
    var header = new VisualElement();
    header.AddToClassList("zui-row");            // one non-wrapping row
    ... grip, enable toggle, name, Z.Flexible(), remove ...
    box.Add(header);
    VisualElement body = null;
    if (m.enabled) { body = new VisualElement(); ZuiReflect.BuildFields(body, m, ModifierDrawerOptions(m, rebuild)); box.Add(body); }
    ZuiFoldCard.Wire(m, header, body, enableToggle, removeBtn);
    return box;
}
```

Three things worth naming separately because they are each a deliberate choice:

- The card box is **`Z.Box(null, null)` — untitled and un-keyed**. Per the rulebook, an untitled per-item card is non-captured view state and must NOT be a `Z.BoxKeyed`; only STABLE boxes (Pyre's Canvas/Solid/Matte/Simulation) get keys. A key on a per-item card would collide across items or orphan on reorder.
- The body is **built into its own child container**, not straight into the box, precisely so `ZuiFoldCard` has one element to show/hide.
- **`ZuiFoldCard.Wire(foldKey, header, body, ...nonFolding)`** (`Assets\Packages\Laubrary\Zui\Toolkit\ZuiFoldCard.cs:45-57`) keys fold state by the **item INSTANCE**, so it survives window rebuilds, undo, reorder and layer re-selection; it prepends the ▾/▸ caret itself; it takes the toggle and the × as `nonFolding` so clicking them never folds the card; and it is null-`body`-safe (a disabled card is header-only, so no caret is added and the header is not made clickable).

**Chunks:** a blast group is exactly this card — `Z.Box(null,null)` + `zui-row` header + reflected/hand-built body + `ZuiFoldCard.Wire(group, header, body, enableToggle, removeBtn)`. Do not give a group card a title or a `BoxKeyed` key.

## 2. The header contents, in this exact order — and no number in the title

`PyreWindow.Modifiers.cs:274-303`: **grip `≡` (16px) → enable `Z.Toggle("")` → `Z.Text(m.DisplayName, ZuiText.Body, …)` → `Z.Flexible()` → `Z.Button("X").W(22f)`**. The fold caret is prepended by `ZuiFoldCard` in front of the grip, so the rendered order is `▾ ≡ ☑ Name … ×`. This matches the card-layout rule in the UI guide verbatim.

The name is **auto-derived from the item's own type**, never typed and never numbered:

```csharp
header.Add(Z.Text(m.DisplayName, ZuiText.Body, m.DisplayName + " modifier."));
```

`DisplayName` comes from the modifier subclass itself; the add-catalog uses the same value (`PyreWindow.Modifiers.cs:408-410`, falling back to `ObjectNames.NicifyVariableName(t.Name)`). There is **no "Modifier 3"** anywhere. That reads better because in a stack of eight cards, "Bloom / Outline / Kaleidoscope" tells you what the stack DOES at a glance, whereas "Modifier 1..8" tells you only how many there are — and an ordinal in the title also goes stale on every reorder, so it would have to be re-rendered (and re-read) constantly for no information gain. Order is already communicated by vertical position plus the grip's tooltip: *"Drag to reorder — a modifier's position IS its apply order."*

Where a **user-typed** name genuinely is the declaration (a layer's own name), Pyre uses an inline `Z.TextInput` right on the row, not a separate rename dialog — `PyreWindow.cs:836-848`, including `name.AddToClassList("zui-audit-allow-stretch")`, the rulebook's sanctioned name-field stretch exception, and a `PointerDownEvent` that selects the row when you click into its name.

**Chunks:** if a blast group has a meaningful type/kind, show that as the header name; if a group is a user-named bag of settings, put an inline `Z.TextInput` there with `zui-audit-allow-stretch`. Either way: no "Group 1", no separate rename affordance.

## 3. Add = a plain `+ Add <thing>` button **below** the list, opening a `Z.Menu` catalog of TYPES

`PyreWindow.Modifiers.cs:80-83` (and identically at `:150-153` for the global stack):

```csharp
Button addModBtn = null;
addModBtn = Z.Button("+ Add modifier", "Add a geometry, pixel or post modifier to the stack.",
    () => ShowAddModifierMenu(addModBtn, list, RebuildModifiers));
modifiersBody.Add(WrapRow(addModBtn));
```

The affordance is a **normal, permanently-visible ZUI button sitting at the bottom of the section body, directly under the last card**, labelled with a leading `+` and the noun. It is never a bare `+` glyph, never hidden behind a hover, never a context menu on empty space. The user knows to click it because it is the only button in the section and it says what it does. Note the self-reference dance (`Button addModBtn = null; addModBtn = Z.Button(... () => ...(addModBtn ...))`) — the button is its own popover anchor.

The catalog itself is a **flat `Z.Menu` with `Section` headings**, not slash-nested submenus (`PyreWindow.Modifiers.cs:362-379`):

```csharp
var menu = Z.Menu(anchor);
string lastGroup = null;
foreach (var e in AddableModifiers())
{
    if (e.group != lastGroup) { menu.Section(e.group); lastGroup = e.group; }
    menu.Item(label, blocked ? reason : $"Add the {label} {group.ToLowerInvariant()} modifier to the stack.", () =>
    { Dirty(() => list.Add((PyreModifier)Activator.CreateInstance(type))); rebuild(); }, enabled: !blocked);
}
menu.Show();
```

Two refinements worth stealing: the catalog is **discovered by reflection and cached per domain** (`AddableModifiers()`, `:389-421` — any parameterless-constructible subclass in any loaded assembly appears with zero hand-maintained list), and an entry that exists but **cannot be authored here is shown DISABLED with the reason as its tooltip** rather than silently omitted (`DisabledInPicker`, `:354-360`). "Not authorable here, and here's why" beats an unexplained absence.

Where an item is a **single slot rather than a list**, the same affordance appears in the slot's empty state: `+ Add simulation` (`PyreWindow.Modifiers.cs:174-179`), which becomes a header + body once filled.

**Chunks:** put `+ Add blast group` (a real `Z.Button`) at the bottom of the groups section; if groups have kinds, open `Z.Menu(anchor)` with `.Section` headings; if they don't, just add one and rebuild.

## 4. Remove = an `X` button on the header, `.W(22f)`, after a `Z.Flexible()`

`PyreWindow.Modifiers.cs:297-302`:

```csharp
var removeBtn = Z.Button("X", "Remove this modifier (undoable).", () =>
{
    int at = list.IndexOf(m);
    if (at >= 0) { Dirty(() => list.RemoveAt(at)); rebuild(); }
}).W(22f);
```

Note it re-resolves the index by `list.IndexOf(m)` at click time rather than trusting the captured `index` — the card may have been reordered since it was built. The `Z.Flexible()` before it pins it to the right edge; the guide's warning applies (the header row must not wrap, or the flexible gap throws the × onto its own line — Pyre's header is a plain `zui-row`, and only the ADD row uses `WrapRow`).

The layer list's delete additionally **refuses to empty the list** and says so via a window notification instead of a dialog (`PyreWindow.cs:900-906`): `if (spec.layers.Count <= 1) { ShowNotification(new GUIContent("A Pyre Plus asset needs at least one layer.")); return; }`.

**Chunks:** `X`/`✕` on the header at `.W(22f)`, index re-resolved at click, `Dirty(...)` + rebuild; if a Chunks asset needs ≥1 group, guard the last delete with `ShowNotification`, not a modal.

## 5. Reorder = a `≡` grip driven by `ZuiReorder.MakeGrip`, and the drag unit is the WRAP, not the row

`PyreWindow.Modifiers.cs:274-287`:

```csharp
var grip = Z.Text("≡", ZuiText.Body, "Drag to reorder — a modifier's position IS its apply order.");
grip.style.unityFontStyleAndWeight = FontStyle.Bold;
grip.style.width = 16f;
ZuiReorder.MakeGrip(grip, box, listHost, (from, to) =>
{
    Dirty(() => { var mm = list[from]; list.RemoveAt(from); list.Insert(to, mm); });
    rebuild();
});
```

`ZuiReorder.MakeGrip(grip, row, container, onMoved)` (`Zui\Toolkit\ZuiReorder.cs:13-16`) draws the insertion line and reports `(from, to)` with remove-then-insert semantics already adjusted; the caller mutates its own list and rebuilds. There is **no** up/down arrow pair and **no** reorder entry in a context menu — the grip is the only reorder route, which is why its tooltip has to explain what order MEANS.

Two structural rules Pyre observes and comments on explicitly:

- **The list gets a dedicated `listHost`** so the reorder insertion line and index math never see the `+ Add` button below (`PyreWindow.Modifiers.cs:70-78`): "A dedicated host for the rows so ZuiReorder's insertion line + index math only ever see modifier blocks, never the '+ Add' button below."
- **The dragged element must be the outermost per-item element.** In the layer list the row PLUS its folded Matte box live in a `wrap`, and `MakeGrip` is given `wrap` (`PyreWindow.cs:809-818`): "The WRAP (not the inner row) is the drag/reorder unit, or reordering would leave the folded matte box behind."

**Chunks:** one `listHost` holding only group cards, `+ Add` outside it; `MakeGrip(grip, cardBox, listHost, …)` where `cardBox` is whatever contains the group's header AND everything folded under it.

## 6. Three mutation helpers, and everything routes through one of them

`PyreWindow.cs:2896-2916` and `:305`:

```csharp
void Dirty(System.Action apply)             // Undo.RecordObject → apply → SetDirty → MarkDirty (invalidates the frame cache)
void DirtyRepaintOnly(System.Action apply)  // same, but does NOT set previewDirty — for edits that change LAYOUT/PLAYBACK, not pixels
void MarkDirty()                            // previewDirty = true; preview.MarkDirtyRepaint(); RefreshTransportReadout();
```

Plus the field-level wrappers that carry the same contract into rich controls: `Val` / `ValIndexed` / `Val2D` / `FillRow` / `SlotFill` (`PyreWindow.cs:2574-2622`), each passing `onChanged: () => MarkDirty()` and `onBeforeMutate: () => Undo.RecordObject(spec, "Edit Pyre Plus")` — the ZUI undo contract (record once per gesture, before the first mutation). And for reflected bodies, `ModifierDrawerOptions` (`PyreWindow.Modifiers.cs:327-342`) hands `ZuiReflect` `OnBeforeChange` / `OnChanged` / `OnStructureChanged` so a reflected card obeys the identical rules with no per-field wiring.

The `DirtyRepaintOnly` split is a genuinely good idea worth naming: an edit that does not change the rendered frames (tile size, zoom, loop delay, every CherryFraming sequence edit) must not invalidate an expensive cache.

**Chunks:** define `Dirty` / `DirtyRepaintOnly` / `MarkDirty` on `ChunkWindow` (it already has `Dial`/`DialF`/`SlicerDial` variants — consolidate rather than adding a fourth) and make every group control route through one; give the reflected/nested bodies a `ZuiReflect.Options` factory exactly like `ModifierDrawerOptions`.

## 7. Targeted rebuild: a stable SECTION that owns a swappable BODY

Every list section in Pyre is built once and refilled many times. `PyreWindow.Modifiers.cs:29-49 / 62-95`:

```csharp
VisualElement modifiersBody;   // cleared/refilled on every add / remove / reorder / enable
ZuiSection modifiersSection;   // persists across body refills

void BuildModifiers(VisualElement root, Pyre s)
{ var sec = Z.Section("Modifiers", "…", icon: "sliders-horizontal"); modifiersSection = sec;
  sec.SetHeaderSuffix(EnabledModifierSuffix); modifiersBody = new VisualElement(); sec.Add(modifiersBody);
  root.Add(sec); RebuildModifiers(); }

void RebuildModifiers() { … modifiersBody.Clear(); … }
```

`RebuildAllForSelection()` (`PyreWindow.cs:949-956`) is the coarse version — `RebuildLayerList(); RebuildShape(); RebuildSwarm(); RebuildModifiers(); MarkDirty();` — used only when the SELECTED item changes. A full `Rebuild()` of the window is reserved for range changes and is deliberately coalesced onto the next frame so it cannot break an in-progress drag (`ScheduleRangeRebuild`, `PyreWindow.cs:2923-2927`).

**Chunks:** hold `groupsSection` + `groupsBody` as fields; `RebuildGroups()` clears and refills `groupsBody` only. Never `Rebuild()` the window on an add/remove/reorder.

## 8. Section header carries state the folded body hides — suffix, checkbox, menu

`ZuiSection` (`Zui\Toolkit\ZuiSection.cs`) has three header affordances Pyre uses to buy back vertical space:

- **`SetHeaderSuffix(Func<string>)`** + `RefreshHeaderSuffix()` (`:174-187`) — Pyre shows the count of ENABLED items while collapsed, so "Modifiers (2)" means two active effects are hidden (`PyreWindow.Modifiers.cs:44, 53-60, 94`). The Simulation box does the same with a `" (on)"` marker (`:170`). A folded section must never hide the fact that it is doing something.
- **`SetHeaderToggle(value, tooltip, onChanged)`** (`:191-195`) — the Swarm section's enable lives ON the header, not as a first body row (`PyreWindow.cs:2156-2166`); CherryFraming's does the same (`PyreWindow.CherryFraming.cs:85-87`). Clicking the checkbox does not fold the section.
- **`SetHeaderMenu(icon, tooltip, open)`** (`:127-131`) — the Shape section's FORM PICKER is a caret-button/right-click menu on the header instead of three rows of in-body radios (`PyreWindow.cs:1132-1135`), with the comment "reclaiming that vertical space". The menu itself is a multi-COLUMN `Z.Menu(anchor).Width(490f).Custom(...)` of icon+label items with a `✓` on the current one (`ShowShapeMenu` `:1193-1209`, `FormColumn` `:1211-1246`).

**Chunks:** enabled-group count as a header suffix on the groups section; any per-section master enable goes on the header; a "which kind" chooser that would otherwise be a block of radios goes in a header menu.

## 9. "Off ⇒ don't build the body" — conditional construction, not disabled controls

Three live instances:

- A disabled card builds **no body at all** (`PyreWindow.Modifiers.cs:308-314`): `if (m.enabled) { body = new VisualElement(); ZuiReflect.BuildFields(...); box.Add(body); }` — a disabled modifier is header-only.
- The Swarm body returns early: `if (!s.swarmEnabled) return;` (`PyreWindow.cs:2206`).
- CherryFraming skips its entire two-grid UI when off, and nulls its host refs so nothing stale is reachable (`PyreWindow.CherryFraming.cs:90-96`): "While cherry framing is off, the whole grid UI is pointless — don't even build it."

Similarly, a sub-box only exists when relevant: the per-layer Matte box is added under the row **only** `if (layer.matteEnabled)` (`PyreWindow.cs:911`), and reflected fields that only matter in a mode are skipped via `Skip` (`PyreWindow.Forms.cs:127` hides `[PyreSwarmOnly]` dials while the swarm is off).

**Chunks:** a disabled blast group shows its header only; any group sub-box (spawn shape, motion, sound) is built only when its own gate is on.

## 10. Space economy: `Z.ColumnFlow` + a fixed dial pane + `WrapRow` + `Z.HGroup`, no computed row budgets

`PyreWindow.cs:307-408` (`BuildAsset`) is the shape of the whole window and is worth reading end to end:

- A left `ScrollView` of fixed, user-draggable width and a right pane with `flexGrow = 1` — hand-rolled here rather than `Z.Split` because the divider also drives the column count.
- **`var flow = Z.ColumnFlow(360f);`** (`:333`) — the entire dial stack is ONE width-driven flow: one 360px column that becomes 2–4 contiguous columns as the pane widens. "Each `Build*` below adds EXACTLY ONE top-level unit… The flow never reaches inside a unit, so the per-section rebuild helpers keep working wherever their section lands — they mutate section BODIES, never the flow. Never call `flow.Clear()`."
- The section order is the reading order down column 1, then 2 (`:354-359`): Canvas → Layers → Global Modifiers → Shape → Swarm → Modifiers.
- `BuildVerticalSplitter()` (`:413-430`) and `BuildPreviewResizeBar()` (`:435-451`) are 6px pointer-captured drag bars, both with tooltips (the second's comment notes the tooltip is the ONLY affordance since UITK won't take a cursor style here).
- Row packing is done by **flex-wrap**, not arithmetic: `static VisualElement WrapRow(params VisualElement[] kids) { var r = Z.Row(kids); r.style.flexWrap = Wrap.Wrap; return r; }` (`:2569-2572`), plus `Z.HGroup(...)` for short pairs (e.g. `PyreWindow.cs:1779-1785`, Speed + Scale on one row).
- Every scalar with a real range is a `Z.MicroSlider(label, value, min, max, tooltip, onChanged, width, showValue: true)` at an explicit width (150–170px), never a bare number.
- Reflected card bodies use **`ZuiReflect.FlowFields`** (`PyreWindow.Forms.cs:116`) so short dials share a line and only a curve takes a full row, with `ControlWidth = 150f` **and** `vopt.controlWidth = 150f` set together so mixed dials flow as one even grid (`:133-139`, with the reasoning: "a grown value claims a whole line and leaves the next dial's row half empty").

**Chunks:** wrap the dial column in `Z.ColumnFlow(360f)` and add each section as exactly one unit; use `WrapRow`/`Z.HGroup` for short fields; give every reflected body `FlowFields` with a shared `ControlWidth`.

## 11. `Z.Section` vs `Z.Box` vs `Z.BoxKeyed` — the actual decision rule Pyre follows

Observed consistently across the file:

- **`Z.Section(title, tooltip, stateKey, icon)`** = a TOP-LEVEL unit in the dial flow. Pyre has exactly eight and they are all listed in one place (`BuildSectionToggleBar`, `PyreWindow.cs:488-499`): Tags, Views, Canvas, Layers, Global Mod, Shape, Swarm, Modifiers. Sections get an icon (`"stack"`, `"shapes"`, `"sliders-horizontal"`, `"globe"`, `"circles-three-plus"`) precisely so the headings read apart. The Layers comment (`:775-778`) states the rule: a green-header Section "rather than a framed BoxKeyed, so the top-level sections read consistently".
- **`Z.BoxKeyed(title, tooltip, "stable.key")`** = a STABLE sub-box inside a section, one that the saved-views bar must capture: `pyreplus.canvas`, `pyreplus.solid`, `pyreplus.solid.lines`, `pyreplus.form`, `pyreplus.sim`, `pyreplus.text`, `pyreplus.cherry.source`… The Simulation box carries the explicit warning (`PyreWindow.Modifiers.cs:162-165`): "BoxKeyed (not Z.Box): … an unkeyed box falls back to keying its view-state by title+tooltip, which orphans saved views the moment the title/tooltip is reworded."
- **`Z.Box(null, null)`** = an untitled, non-captured per-ITEM card (pattern 1).

Titles are short nouns; every explanation lives in the tooltip, including multi-sentence ones (see the Matte box's three-sentence tooltip, `PyreWindow.cs:972-976`). The one place Pyre puts prose on screen is a `Label` explaining that the Swarm does not drive Fire/Fireball (`PyreWindow.CherryFraming.cs` has none; `PyreWindow.cs:2179-2204`) — and the neighbouring comment concedes the general rule: "an on-screen instruction label here was a UI-Guide violation — 'tooltip, not title'" (`:2169-2170`). Treat those two notes as the exception that proves the rule, not as licence.

**Chunks:** its top-level areas (Slicer, Formation, Layers, Timeline, Pyre Spawn, Modifiers, Splash) are `Z.Section`s with icons and belong in a `ZuiSectionToggleBar`; their stable sub-boxes are `Z.BoxKeyed("…", "…", "chunks.<name>")`; group cards are `Z.Box(null,null)`.

## 12. Asset references: `LauAssetElement` chip for a LauAsset, `Z.Object<T>` inside a `Z.Field` for a raw Unity asset — never a text field

Pyre's window has exactly **three** asset references and all three use the same construction: a `Z.Field(label, tooltip, Z.Object<T>(current, tooltip, onPick, width))`.

- Sprite — `PyreWindow.cs:1646-1653` (width 160f), packed in a `WrapRow` beside a Tint toggle.
- Prefab — `PyreWindow.cs:1773-1777` (width 190f).
- TMP font — `PyreWindow.cs:2070-2076` (width 200f).

```csharp
box.Add(Z.Field("Font",
    "The TMP SDF font asset. Its atlas must be Read/Write-enabled … Leave empty to auto-pick …",
    Z.Object<TMP_FontAsset>(s.textFont, "SDF font — needs a readable atlas; leave empty to auto-pick.",
        v => Dirty(() => s.textFont = v), 200f)));
```

`Z.Object<T>` is Unity's own object field — a **type icon + the asset's name + a picker circle**, with click-to-pick, click-to-ping and drag-and-drop inherited wholesale. It is the rulebook's sanctioned raw island for a plain Unity asset. It is emphatically **not** a text field, and nothing in Pyre asks the user to type a reference.

The asset the window ITSELF edits is chosen through the `ZuiAssetWindow<Pyre>` base (`Assets\Packages\Laubrary\Editor\AssetKit\ZuiAssetWindow.cs`): a toolbar with an assign field + New / Duplicate / Rename / Delete / Browse (`:155-180`) and a thumbnail-grid browser (`:265+`), with Pyre supplying `TypeLabel` / `NewAssetName` / `DefaultFolder` (`PyreWindow.cs:36-38`) and animated thumbnails (`RenderThumbnail` `:40`, `UpdateAnimatedThumbnail` `:52`).

For a reference to a **LauAsset**, the canonical control is `LauAssetElement.Build(current, onPick, constraint, thumbCache, suggestedName, folder, tooltip)` (`Assets\Packages\Laubrary\Editor\AssetKit\LauAssetElement.cs:24-44`). On screen it is a **`ZuiChip`**: the asset's **thumbnail** plus its **name**, in an empty/placeholder state when null — and no inline button row at all.

- **Left-click** → `LauAssetBrowser.Show(...)` anchored to the chip's own `worldBound` (the filtered thumbnail browser).
- **Right-click** → `New` (one item, or a `New/<Type>` submenu when several concrete types are registered) / `Edit` (opens that asset's own editor) / `Clear` — each greyed out via `AddDisabledItem` when unavailable rather than hidden (`:46-75`).
- **Drag-and-drop** from the Project window, constrained by `chip.Accepts` (`:41-42`).

`New` and `Edit` only work if the type registered itself with `LauAssetEditors`. Pyre does this in nine lines — `Assets\Packages\Laubrary\Editor\Pyre\PyreSpawnSourceEditorLink.cs:21-26` — and the file's own doc comment names Chunks as the consumer: *"so a Pyre Spawn Source picked in a Chunks Pyre Spawn slot gets a working 'New' and 'Edit' on its chip instead of two dead buttons. Same shape as ChunkSpecEditorLink."*

**Chunks (this is the one the user complained about):** Chunks already uses `LauAssetElement.Build` in two places (`ChunkWindow.PyreSpawn.cs:75` and `:346`, `ChunkWindow.Slicer.cs:102`) and `Z.Object<T>` in others (`ChunkWindow.Slicer.cs:114`, `ChunkWindow.Preview.cs:76`, `ChunkFollowEmitterEditor.cs:68,73`). Neither of those renders as a text field, so **audit for a genuinely raw `Z.TextInput` standing in for a reference, or an unstyled/unattached root** — the `Z.Attach(root)` failure makes ZUI controls render as bare unstyled boxes, which is exactly what "reads as a raw text field" looks like. Note `ChunkWindow.Layers.cs:207` and `ChunkWindow.PyreSpawn.cs:139-142` are legitimately-typed NAMES (declarations), not references. Rule to apply: LauAsset → `LauAssetElement.Build` chip; plain Unity asset → `Z.Field(label, tip, Z.Object<T>(…, width))`; a reference to a named thing → a picker of the owner's declared options, never a text field.

## 13. The empty state is designed away: seed one element in the DATA and refuse to delete the last

A fresh `Pyre` asset is **never** an empty screen. The data model itself ships one layer:

`Assets\Packages\Laubrary\Runtime\Pyre\Pyre.cs:1207`
```csharp
public List<PyreLayer> layers = new List<PyreLayer> { new PyreLayer { matteEnabled = false } };
```

…and the layer delete refuses to go below one (`PyreWindow.cs:900-906`). So on a brand-new asset the user sees: the toolbar (assign / New / Duplicate / Browse), the section toggle bar, Tags, Views, Canvas, a **Layers list already containing "Layer 1"** with `+ Add layer` / `Duplicate` beneath it, and the Shape / Swarm / Modifiers sections already pointed at that layer. The first thing they can click is any dial, or the Shape header's caret to choose a form. Every list-shaped section that IS legitimately empty shows its `+ Add …` button as the only thing in the body (Modifiers, Global Modifiers, the Simulation slot).

With **no asset selected at all**, the base class shows the thumbnail browser automatically (`ZuiAssetWindow.cs:14-16, 145`: "the browser shown automatically whenever no asset is selected", `if (asset == null || browsing) root.Add(BuildBrowser());`), and the toolbar's `New` is right there — so first run is a grid plus a New button, never a blank pane.

Selection is also **sticky and clamped, never reset** (`PyreWindow.cs:349-352`): "Defaulting to the last layer on every rebuild silently jumped the overlay/sections to another layer after any undo or structural edit."

**Chunks:** make sure a fresh ChunkSpec seeds one blast group in the field initializer, guard the last delete, and make sure the groups section's empty body is the `+ Add blast group` button and nothing else.

## 14. Selection inside a list: a `●`/`○` button + a tinted row, and one `RebuildAllForSelection()`

`PyreWindow.cs:801-834, 941-956`:

```csharp
if (sel) row.style.backgroundColor = new Color(0.35f, 0.55f, 0.95f, 0.18f);
row.Add(Z.Button(sel ? "●" : "○", "Select this layer to edit it below.", () => SelectLayer(li)).W(24f));
…
void RebuildAllForSelection() { RebuildLayerList(); RebuildShape(); RebuildSwarm(); RebuildModifiers(); MarkDirty(); }
```

Every other per-row state indicator follows the same "glyph-swap button at `.W(24f)`" convention (the matte toggle `▦`/`□`, `:869-881`) or a small `Z.Text` badge with an explanatory tooltip (`off`, `→2`, `◧`, `▲1`, `:850-861`). Per-row actions that are NOT the primary one get short text buttons at explicit widths (`Dup` `.W(40f)`, `:887-898`).

**Chunks:** if a Chunks list drives detail sections below it (layers do), copy the `●`/`○` + tinted row + single `RebuildAllForSelection()` pattern rather than inventing a second selection idiom.

## 15. Directly reusable helpers — do not reimplement any of these in Chunks

| Type / member | File | What it gives you |
|---|---|---|
| `ZuiFoldCard.Wire(key, header, body, params nonFolding)` | `Assets\Packages\Laubrary\Zui\Toolkit\ZuiFoldCard.cs:45-57` | Per-instance fold state + ▾/▸ caret + click-guard on toggle/× + null-body safety |
| `ZuiReorder.MakeGrip(grip, row, container, onMoved)` | `Assets\Packages\Laubrary\Zui\Toolkit\ZuiReorder.cs:13-16` | Drag-reorder with insertion line, `(from,to)` callback |
| `ZuiSection.SetHeaderSuffix` / `RefreshHeaderSuffix` / `SetHeaderToggle` / `SetHeaderMenu` / `HeaderFoldDisabled` | `Assets\Packages\Laubrary\Zui\Toolkit\ZuiSection.cs:127-195` | Collapsed-state count, header checkbox, header picker menu |
| `Z.ColumnFlow(360f)` (`ZuiColumnFlow`) | `Assets\Packages\Laubrary\Zui\Toolkit\ZuiColumnFlow.cs:39-76` | Width-driven 1→4 column dial stack; add one unit per section |
| `ZuiSectionToggleBar(prefsKey, params (label, section)[])` | `Assets\Packages\Laubrary\Zui\Toolkit\ZuiSectionToggleBar.cs:32-51` | Sections-vs-toggle-bar mode, solo on right-click, persisted per tool |
| `ZuiViewBar` + `ZuiViewStore` | used at `PyreWindow.cs:453-511` | Saved named fold/gear presets over every `ZuiBox` under a root; needs `BoxKeyed` keys |
| `ZuiReflect.BuildFields` / `FlowFields` + `ZuiReflect.Options` | `Zui\Toolkit\ZuiReflect.cs`; usage `PyreWindow.Modifiers.cs:312, 327-342` and `PyreWindow.Forms.cs:116-140` | A whole card body from an object's fields, with Undo/dirty/structure hooks and per-field `ConfigureValue` |
| `LauAssetElement.Build(...)` | `Assets\Packages\Laubrary\Editor\AssetKit\LauAssetElement.cs:24-44` | The chip: thumbnail+name, left-click browser, right-click New/Edit/Clear, drag-drop |
| `LauAssetEditors.RegisterCreate/RegisterOpen` | worked example `Assets\Packages\Laubrary\Editor\Pyre\PyreSpawnSourceEditorLink.cs:21-26` | Makes New/Edit live on every chip pointing at your type |
| `ZuiAssetWindow<T>` | `Assets\Packages\Laubrary\Editor\AssetKit\ZuiAssetWindow.cs` | Toolbar CRUD, thumbnail browser, auto-browser-when-empty, Tags section |
| `Z.Popover(anchorElement, build, options)` | usage `PyreWindow.CherryFraming.cs:351-393` | Per-item settings card anchored to the clicked element (never a computed position) |
| `Z.Menu(anchor).Section/.Item/.Width/.Custom` | usage `PyreWindow.Modifiers.cs:362-379`, `PyreWindow.cs:1193-1246` | Flat, ZUI-styled type catalog with headings, checks, disabled-with-reason entries |
| `WrapRow(params VisualElement[])` | `PyreWindow.cs:2569-2572` | Four-line local helper — copy it verbatim into `ChunkWindow` |

## 16. Two gestural gotchas Pyre paid for, documented in its own headers

Both are in `PyreWindow.CherryFraming.cs:10-20` and apply to any grid/list with per-item drag and per-item popovers:

- **Do NOT `CapturePointer` for drag-reorder.** Capturing routes every later event (including `PointerUp`) back to the ORIGIN element regardless of where the mouse released, which destroys drop-target resolution. Without capture, normal picking delivers `PointerUp` to whatever card is under the cursor, and that card's index IS the drop target. Corollary: a selection change during a press must **restyle existing elements in place**, never rebuild the grid — rebuilding mid-gesture destroys the element `PointerUp` needs to land on.
- **Anchor a popover to the clicked `VisualElement`**, i.e. `Z.Popover(card, …)`, never a hand-computed screen position: `PointerDownEvent.position` is already panel-space, so adding a parent's `worldBound` double-counts and the popover lands off-screen. Defer any grid rebuild to `Options.onClosed` so the anchor stays valid while the popover is open.

**Chunks:** if blast-group cards ever get drag-and-drop or a right-click settings popover, these two rules apply unchanged.
