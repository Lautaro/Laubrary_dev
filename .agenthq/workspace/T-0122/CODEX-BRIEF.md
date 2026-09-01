# Implementation brief — Chunks mock editor, stage 2 (AgentHQ T-0122)

You are the implementation agent. Write C# only. Do the whole thing in one pass, then stop.

## Absolute constraints

- You may create/modify files **only** under `D:\UNITY\Laubrary Dev\Assets\ChunksMock\`. Nothing else. Do not touch `Assets/Packages/**`, `Assets/ZUI/**`, `CHANGELOG.md`, `.agenthq/**`, or any git state. Do not run git commands.
- Do not add any `[MenuItem]` other than the single existing one: `Laubrary/Chunks Mock (Prototype)`.
- All data is **in-memory mock data**. Never reference production Chunks types (`ChunkSpec`, `Laubrary.Chunks.*`). Never read or write an asset, never `AssetDatabase`, never `EditorPrefs` beyond what ZUI does internally.
- Keep the whole thing in **one file** unless it exceeds ~800 lines, in which case split into a second file in the same folder.

## Files

1. **NEW** `D:\UNITY\Laubrary Dev\Assets\ChunksMock\Editor\ChunksMock.Editor.asmdef` with exactly:

```json
{
    "name": "ChunksMock.Editor",
    "rootNamespace": "ChunksMock.Editor",
    "references": [
        "com.Lautaro-Arino.Laubrary.Zui.Editor",
        "com.Lautaro-Arino.Laubrary.ZuiRuntime",
        "ZUI.Editor"
    ],
    "includePlatforms": [ "Editor" ],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": false,
    "precompiledReferences": [],
    "autoReferenced": false,
    "defineConstraints": [],
    "versionDefines": [],
    "noEngineReferences": false
}
```

2. **REWRITE** `D:\UNITY\Laubrary Dev\Assets\ChunksMock\Editor\ChunksMockWindow.cs`. Its namespace must change from `Laubrary.Chunks.Mock.Editor` to **`ChunksMock.Editor`** — it must NOT be nested under the production `Laubrary.Chunks` namespace. Keep `using Laubrary.Zui;`.

Read the current file first; it already contains a working `MockFormationStage` custom painter you should keep and extend.

## What to build

The mock currently supports exactly one capability (a Pyre Formation) and has no way to name a recipe. Stage 2 turns it into a real **stack of capabilities** plus **one conditional shared timeline**.

### 1. Recipe identity (currently missing entirely)

At the top of the left pane, a single row — **no section box around it** (never title a box that holds one field):

- `Z.Field("Recipe", "<tooltip>", Z.TextInput(recipe.name, ..., v => recipe.name = v, 180f))` — typing the name here is correct, this is where the name is DECLARED.
- Beside it in the same `Z.HGroup`, `Z.Button("Recipes…", "<tooltip>", …)` opening a `Z.Menu(anchor)` with:
  - a `Section("Mock recipes")` listing three fixed mock recipe names — "Untitled Chunk", "Crate Smash", "Barrel Pop" — each an `Item(name, tooltip, () => LoadMockRecipe(name), checkedNow: recipe.name == name)`. Loading one replaces the whole in-memory recipe with a small believable preset (see §6).
  - a `Separator()` then `Item("New empty recipe", …, NewRecipe)`.
  - The trailing "…" is required because the button opens further UI rather than acting.

### 2. Capability stack (currently only one capability can ever exist)

A `Z.Section("Capabilities", "<tooltip>", "chunks.mock.capabilities", icon: "stack")` containing, in order:

- one **card per capability in `recipe.capabilities`** (a `List<MockCapability>`), in list order;
- then, **always present** (this is what makes it a stack), `Z.Button("Add capability…", "<tooltip>", …)` opening a `Z.Menu` whose items are the four capability kinds (§3). Adding appends a new instance with sensible defaults and rebuilds.

**Card shape — follow this exactly.** Use `Z.BoxKeyed(displayName, tooltip, "chunks.mock.cap." + capability.Id, icon: <kind icon>)`, where `Id` is a per-instance unique string so two cards of the same kind fold independently. The header carries the identity plus the one universal control and the remove ×, via `box.AddHeaderContent(...)` (it right-aligns them on the title row and swallows its own clicks so they never fold the box):

- `Z.ToggleButton("On", "<tooltip>", cap.Enabled, v => { cap.Enabled = v; Rebuild(); })` — the universal control every card has.
- `Z.Button("×", "Remove this capability from the mock recipe.", …).W(24f)`.

Do **not** add a separate "Remove" text row in the body, and do **not** put a subtitle/description line in the body — explanation goes in tooltips only.

**The toggle must actually enable/disable, not just fold.** When `cap.Enabled` is false:
- the card body is greyed and non-interactive — call `SetEnabled(false)` on the box's content container;
- the capability contributes **nothing** to the shared timeline and **nothing** to the spatial guide.

That is the whole point: the fold caret in the box header is the show/hide, the "On" toggle is the enable.

### 3. The four capability kinds

Model them as an abstract base plus four subclasses, all private nested classes:

```
abstract class MockCapability
{
    public readonly string Id = System.Guid.NewGuid().ToString("N").Substring(0, 8);
    public bool Enabled = true;
    public abstract string DisplayName { get; }
    public abstract string Icon { get; }        // a ZUI icon name
    public abstract bool Timed { get; }         // does it occupy time on the shared timeline?
    public float Delay = 0f;                    // seconds before it starts   (timed kinds only)
    public float Duration = 0.5f;               // seconds it lasts           (timed kinds only)
    public abstract Color BandColor { get; }    // its colour on the shared timeline
}
```

**a) Pyre Formation** — icon `"shapes"`, Timed. Keep every existing field and control (mock pyre name picked from a `Z.Menu`, Count, Stagger, Pattern, and Direction°/Radius depending on pattern). Two changes only: the two-option Line/Ring set becomes **`Z.Segmented`** (a short 2-option set is Segmented, MiniRadio is for sets that wrap), and the pyre picker button keeps its trailing "…".

**b) Layer Plan** — icon `"stack-simple"`, **NOT** timed. Owns an ordered `List<MockLayer>` where `MockLayer { string name; float opacity; int blend; }`. In the card:
- one row per layer: `Z.TextInput(name, …, 110f)` · `Z.MicroSlider("Opacity", opacity, 0f, 1f, …, 120f, decimals: 2)` · `Z.Segmented(blend, new[]{"Normal","Add","Multiply"}, …)` · `Z.Flexible()` · `Z.Button("▲"…).W(22f)` · `Z.Button("▼"…).W(22f)` · `Z.Button("×"…).W(22f)`. ▲/▼ reorder within the list and are disabled (`SetEnabled(false)`) at the ends.
- then `Z.Button("Add layer", …)` — this one acts immediately, so **no ellipsis**.
- With zero layers, show nothing but that button. Do not print an explanatory empty-state paragraph.

**c) Palette Splash** — icon `"palette"`, Timed. Fields: a mock palette picked from a `Z.Menu` (offer "Ember", "Ash", "Verdigris", "Bone"), `Z.MicroSlider("Swatches", 1..8, decimals 0)`, `Z.MicroSlider("Spread", 0..1, decimals 2)`. Pack the two sliders into one `Z.HGroup`.

**d) Generic Particle Burst** — icon `"sparkle"`, Timed. Deliberately *generic*, i.e. not a Pyre: fields `Z.Segmented(shape, new[]{"Cone","Disc","Sphere"}, …)`, and `Z.MicroSlider`s for `Count` (1..200, decimals 0), `Speed` (0..20, decimals 1), `Lifetime` (0.05..3, decimals 2), `Spread°` (0..180, decimals 0). Pack them two per `Z.HGroup` row — do not stack four full-width rows.

Every timed kind also shows its `Delay` and `Duration` MicroSliders packed into one `Z.HGroup` row inside its own card (`Delay` 0..2s, `Duration` 0.05..3s, both decimals 2). A change to either must refresh the shared timeline.

### 4. The ONE conditional shared timeline

This is the headline requirement: **it must be absent from a simple Pyre-only recipe.**

- Condition: it exists **only when the recipe currently has two or more ENABLED, Timed capabilities.** One timed capability, or several disabled ones, means the whole section is not built at all.
- Placement: in the **right pane, BELOW the spatial guide** — so its appearing/disappearing shrinks the stage from the bottom and never shoves the workspace down from above.
- Build it as `Z.Section("Timing", "<tooltip>", "chunks.mock.timing", icon: "timer")` containing a single `Z.Timeline(0f, "<tooltip>", s => …)` whose bands you set with `SetSegments(...)`.
- Bands, in capability order, for the enabled timed capabilities only: for each capability emit **two** consecutive `ZuiTimelineSegment`s — a gap band of `cap.Delay` seconds (name `""`, a dim grey, tooltip naming what it delays) and then the capability's own band of `cap.Duration` seconds (name = its DisplayName, colour = `cap.BandColor`, tooltip naming it). A zero-length band is explicitly legal and paints nothing, so you never need to conditionally omit the gap.
- Rebuild the segments whenever anything that feeds them changes.

### 5. Spatial guide

Keep the existing painter and extend it so it reflects the *enabled* capabilities:

- Pyre Formation → exactly as today (line/ring of spawn dots, order arrows).
- Generic Particle Burst → a fan of short rays from the origin: `Count` capped at ~40 drawn rays, opening angle = `Spread°`, ray length scaled by `Speed`, oriented per shape (Cone = a fan pointing right, Disc = full 360° in-plane, Sphere = full 360° with two shorter inner rings to read as volume).
- Palette Splash → a small ring of filled squares around the origin, `Swatches` of them, radius scaled by `Spread`.
- Layer Plan → draws nothing (it is not spatial). Do not draw a placeholder.
- With **no** capabilities at all, draw exactly what it draws today (grid + origin cross).

Change the guide section's icon from `"move"` (which does not resolve) to `"arrows-out-cardinal"`.

### 6. Mock presets, for §1

- `"Untitled Chunk"` → empty capability list.
- `"Crate Smash"` → a Layer Plan (layers "Splinters", "Dust"), a Pyre Formation (Ring, count 8), and a Generic Particle Burst. Two enabled timed capabilities, so the shared timeline appears.
- `"Barrel Pop"` → a single Pyre Formation only. One timed capability, so the shared timeline must NOT appear. This preset exists specifically to demonstrate absence.

## ZUI rules you must obey (this project's canonical UI rulebook)

- **Never** use `EditorGUILayout`, `GUILayout`, `EditorGUI.*`, or a raw UI Toolkit `Slider`/`Toggle`/`Foldout`/`EnumField`/`PopupField`. Use the `Z.*` factories only. The one sanctioned raw island is the custom `Painter2D` preview stage that already exists.
- **Enums are never dropdowns.** A short 2–3 option set → `Z.Segmented`. Never `Z.EnumDropdown` or `Z.Dropdown`.
- **A bounded scalar is a `Z.MicroSlider`**, never a bare number field, and never a `Z.Slider` plus a separate value field.
- **Every single control and every label gets a tooltip.** A tooltip must read correctly for the control's CURRENT state.
- **No explanatory text on screen.** No help paragraphs, no subtitles, no "this mock intentionally…" commentary anywhere in the UI. All of that belongs in tooltips. There must be no build-process commentary ("vertical slice", "prototype", "mock only") visible in the window body — the menu item's own "(Prototype)" is the only place that lives.
- **Never title a box that holds exactly one field.**
- **Pack short related fields into one `Z.HGroup`/`Z.Row`.** Vertical space is the scarce resource. Do not stack short controls in full-width rows.
- **Every control has an explicit width** (MicroSlider ~120–150, text field ~110–180, `×` button 22–24). Nothing left to stretch.
- **A label = an action.** A control that opens further UI is named for the destination and ends with "…"; a control that acts immediately never has an ellipsis.
- Contextual UI must not move the user's workspace — that is why the conditional timeline goes below the stage.

## Correctness requirements

- The window must survive a **domain reload**: `recipe` is a non-serialised field, so guard every rebuild path against it being null and re-create it if so.
- `Rebuild()` (from `ZuiWindow`) is how you refresh the whole window after a structural change. For a value-only change that only affects the drawing, call `MarkDirtyRepaint()` on the stage and update the timeline segments instead of a full rebuild, so a slider drag does not rebuild the window under the pointer.
- Keep the existing `Z.Split("chunks.mock.prototype.split", 340f, left, right)` shape and the left `ScrollView`.
- It must compile against Unity 6 / C# 9. No `record`, no top-level statements, no `init` accessors.

## When you are done

Print a short list of the files you created or changed and nothing else. Do not run Unity, do not run git.
