> **STATUS UPDATE 2026-07-23:** the IMGUI → UI Toolkit switch this design was waiting on HAS happened — the whole editor toolkit (`Laubrary.Zui`, `Z.*`) is now UI Toolkit, and every Laubrary tool window is ported. Per the shelved note below, that means this design is now due for **reconsideration in a UI Toolkit world, not a straight port**. Some of it already exists: `ZuiSection`/`ZuiBox` are collapsible bordered sections that fold, and `Z.Divider` groups within one. Still absent: multi-column field flow, the gear-panel opt-in, cross-column drag, per-project layout persistence, and templates (Zolumn); and the whole Swarm shape-system (PyrePlus). Copied from PreviewLab into the canonical host, where it was missing after the 2026-07-23 merge.

# PyrePlus — modular, opt-in Pyre rework (prototype)

> **Shelved (on hold).** This design and its companion `ZOLUMN_DESIGN.md` are on hold while the project
> investigates switching its editor UI stack from IMGUI to UI Toolkit. Everything below is written
> against ZUI (IMGUI) and assumes `Zolumn` (also IMGUI) as its container primitive — if the UI Toolkit
> switch happens, this design needs to be reconsidered, not just ported, before any implementation
> starts. Do not begin building this without first checking whether the IMGUI vs. UI Toolkit decision
> has been made.

## Purpose

Pyre's `Layer` (`Assets/Packages/Laubrary/Runtime/Pyre/Layer.cs`) is one flat ~40-field bag mixing placement, population, per-particle shape, and modifiers, all always visible regardless of whether they currently do anything. This document specifies a restructured data model and UI, built on the `Zolumn` container (see `ZOLUMN_DESIGN.md`), validated as a parallel prototype — its own asset type, its own renderer, its own window — so the shipping Pyre tool is never put at risk. Folding this back into real `BlastSpec`/`Layer`/`BlastRenderer`, and ideally emitting real `BlastSpec` assets from it, is explicit future work, not part of this build.

## Sections

A `PyrePlusSpec` asset has exactly one implicit layer (no layer-list UI — layers are out of scope for this pass) organized into three `Zolumn` sections:

### Shape (mandatory section, always drawn)

The particle's own look. Mandatory fields: form (Disc only — Pyre's other five `LayerShape`s are out of scope for this prototype), `colorOverLife` (`Gradient`), `alpha` (`ZUIValue`, over the particle's own life), `size` (`ZUIValue`). Opt-in fields, via the section's gear panel:
- **Position** — the particle's own path after its own birth, on its own life clock — reuses `ZUIValue2DControl`'s existing "animate over time" Curve mode verbatim (`Zui/Scripts/Editor/ZUIValue2DControl.cs` — click to add points, point order is time, no separate authored time field needed).
- **2D rotation** (spin, one `ZUIValue` of degrees) — 2D only; this is one particle's own pixels rotating in place, not a 3D tilt (3D tilt belongs to the swarm-wide shape transform, below).

### Swarm (single top-level on/off toggle; off = exactly one instance, section fully hidden)

When enabled:
- Mandatory: `count` (min 2).
- `Spawn mode`: **Area** | **Path** (`MiniRadio`, matching the compact-enum-picker idiom already used for `ScatterMode` today).
- `Shape kind` (available to both modes): **Circle | Triangle | Square | Pentagon | Hexagon**, reusing `PolygonRadiusFactor`/`BandSides` (`Runtime/Pyre/BlastRenderer.cs:498-504`), currently Rosing-only, generalized here. **Custom** (a hand-drawn polyline via the 2D pad in animate-over-time mode) is a **Path-only** addition to that list.
- **Area**: a uniformly-random point within the chosen shape's boundary (extends the existing random-in-disc math at `BlastRenderer.cs:525-531` to the other boundary shapes). No progress/spawn-point concept — particles simply scatter inside the (possibly live-transforming, see below) shape.
- **Path**: placement is driven by one **spawn point** travelling along the chosen shape/polyline, positioned by a **`progress`** field (`ZUIValue` — Static/MinMax/Curve, the same authoring control used for every other animatable Pyre field) running 0→1 across the shape: 0 = the path's start, 1 = its end (once around, for a closed shape). Because `progress` is an ordinary authored envelope, it can ease, hold, rewind (dip back down), or run at variable speed exactly like any other curve — there is no separate per-particle stagger control. Each particle samples `progress` (and therefore its position along the shape) **at its own spawn frame** and keeps that position for its whole life — a spawn-time snapshot, not a shared live value — so particles born while `progress` is moving slowly cluster together and particles born while it's moving fast land further apart. Particle spawn *timing* itself stays a plain, even distribution across the swarm's life window.
- Shared **shape transform**, applied to the whole Area/Path shape and evaluated live at the swarm's current-frame progress (not spawn-locked): **Position** (`ZUIValue2DControl`), **Scale** (`ZUIValue`, with an optional **discrete-step snap** — evaluate the envelope normally, then round to the nearest step, so successive placements land on a fixed set of radii instead of a continuum), **Rotation 2D** (`ZUIValue` degrees), **Rotation 3D** (pitch + yaw, two `ZUIValue`s, reusing the pseudo-3D `RotatePitchYaw` math and the `roseZ`/`zSizeInfluence` depth-scaling trick already in `BlastRenderer.cs:509-523`, so a tilted shape visibly shows depth in the preview — nearer parts of the path render bigger/brighter).
- Each particle's actual placement (its Area random point, or its Path progress-sampled point) is computed against a **snapshot of the shape transform taken at that particle's own spawn frame**, not the live transform — otherwise a live-growing/orbiting shape would retroactively slide every already-placed particle instead of leaving a trail behind it. Pyre's own renderer already makes exactly this distinction deliberately: `ringExpand` is evaluated live so an existing ring visibly grows (`BlastRenderer.cs:470-480`), while `spawnRadius`/`ringStartAngle` are evaluated at each shape's own spawn-time progress specifically so already-placed shapes don't retroactively slide (`BlastRenderer.cs:452-490` — see the comments there for the reasoning). Swarm's shape-transform-vs-per-particle-snapshot split is that same principle applied one level up.

### Modifiers (a Zolumn section whose gear panel is the existing "add modifier" menu)

Reuses `PyreModifier` **directly** — its contract has no `BlastSpec`/`Layer` coupling in any method signature (`Runtime/Pyre/PyreModifiers.cs`: `Prepare(Func<ZUIValue,int,float>)` at line 129, `GeometryModifier.InverseWarp(Vector2, float, in GeoCtx)` at line 169, `PixelModifier.ApplyPixel(ref Color, ref float, in PixelInfo)` at line 560, `PostModifier.Apply(Color32[], int, int)` at line 1224) — so `PyrePlus`'s runtime asmdef references Pyre's own runtime asmdef and stores a plain `List<PyreModifier> modifiers` with **zero reimplementation of any modifier**. Only the drawing chrome is new: the same enable/reorder/"+Add" loop Pyre's own `DrawModifiers` already implements (`Editor/Pyre/PyreWindow.cs:1422`), re-skinned into a `Zolumn` body whose gear icon opens the existing `GenericMenu` "+Add modifier" flow instead of a separate button below the list.

### Layers

Explicitly out of scope for this prototype — important for the eventual integration back into real Pyre, not for validating this rework.

## Renderer

`PyrePlusRenderer` (new, `Runtime/PyrePlus/`) mirrors `BlastRenderer`'s determinism contract exactly: pure, static, every random value derived from a seeded `System.Random` keyed by `(seed, particleIndex, fieldId)` — never `UnityEngine.Random` (see `BlastRenderer.cs`'s own top-of-file doc comment, lines 6-9, for why: preview, bake, and runtime must all produce byte-identical output from the same inputs). Public surface mirrors `BlastRenderer.cs:247` (`RenderFrame(spec, frameIndex) -> Color32[]`) and `:1808` (`RenderFrameTexture` wrapper).

## Preview & authoring UI

- Canvas draw: `GUI.DrawTexture` inside `GUI.BeginClip`, following `PyreWindow.DrawPreview` (`Editor/Pyre/PyreWindow.cs:2410-2427`).
- Canvas↔screen conversion: the `FrameRect(Rect view)` helper plus the inline conversions used throughout `PyreWindow.cs` (e.g. `:2570-2574` and the call sites around `:2584`, `:2627`, `:2674`, `:2707`) — small enough to duplicate rather than reference Pyre's own private method.
- Editable path/shape overlay (Custom points, the shape's position handle): copy the click-to-add / drag / right-click-to-remove precedent from `HandlePinWarp`/`DrawPinMarkers` (`PyreWindow.cs:2623-2666`, `:2671-2697`).
- While editing Swarm, draw the active Area/Path shape directly over the canvas, plus a dot at every particle's actual computed spawn location (not just the shape outline) — same idea as `DrawMetaOrbMarkers`/`HandleMetaBlob` (`PyreWindow.cs:2577-2618`).
- Transport (play/pause/scrub/zoom/fps): copy `Tick()`/`FitZoom` (`PyreWindow.cs:245-266`, `:2459-2513`).
- "New from template" / "Save as template": via `Zolumn`'s template system, instantiated for `PyrePlusSpec`, thumbnail rendered via `PyrePlusRenderer.RenderFrameTexture`.

## Non-goals for this pass

- Layers, layer groups, the layer-library save/recall popup (`PyreLayerLibrary`/`PyreLayerLibraryPopup` stay Pyre-only for now).
- Rosing's nested multi-ring composition (each ring its own count/radius/birth/life) — Swarm's closed shapes here are single-ring equivalents only.
- Bars, MetaBlob, SparkleField, Sprite particle shapes.
- `BlastBaker`-equivalent sprite-sheet/AnimationClip export, and `IPyrePreviewSubject` (attached-to-a-character preview).
- Converting/exporting to a real `BlastSpec` — worth doing once this data model stabilizes.

## Asmdefs

- `Runtime/PyrePlus/PyrePlus.asmdef` — `"name": "com.Lautaro-Arino.Laubrary.PyrePlus"`, rootNamespace `Laubrary.PyrePlus`, references: `["com.Lautaro-Arino.Laubrary.ZuiRuntime", "com.Lautaro-Arino.Laubrary.Pyre"]` (the latter for direct `PyreModifier` reuse).
- `Editor/PyrePlus/PyrePlusEditor.asmdef` — `"name": "com.Lautaro-Arino.Laubrary.PyrePlus.Editor"`, rootNamespace `Laubrary.PyrePlus.Editor`, `includePlatforms: ["Editor"]`, references: `["com.Lautaro-Arino.Laubrary.PyrePlus", "com.Lautaro-Arino.Laubrary.AssetKit.Editor", "ZUI.Editor", "com.Lautaro-Arino.Laubrary.ZuiRuntime"]` — matching `Choreographer`'s asmdef shape (`Editor/Choreographer/ChoreographerEditor.asmdef`).

`PyrePlusWindow : LaubraryAssetWindow<PyrePlusSpec>`, matching `ChoreographerWindow`'s structure (`DrawAsset(PyrePlusSpec asset)`, not a raw `OnZUI()` override).

Undo: every field edit wrapped in `Undo.RecordObject(spec, "...")` inside `BeginChangeCheck`/`EndChangeCheck`, matching Pyre's own window. Every user-facing control gets a tooltip. `CHANGELOG.md` gets an `## [Unreleased]` entry once it compiles.

## Verification

1. `check_compile_errors` via the Coplay bridge (`set_unity_project_root` to this project first, per this project's own CLAUDE.md).
2. Open the PyrePlus window, create a new asset, confirm the default view is minimal (Shape only — Swarm off, Modifiers empty).
3. Turn Swarm on, set count > 1; exercise Area vs. Path for each shape kind, including Custom.
4. Author a `progress` envelope that eases, holds, and rewinds; confirm placements trace the expected pattern (a linear ramp traces the shape once; a rewind visibly retraces it).
5. Animate the Swarm shape's scale/rotation live and confirm already-spawned particles don't retroactively slide (spawn-time snapshot behaving correctly) while new spawns visibly trail the live transform.
6. Enable Rotation 3D and confirm depth-shaded dots in the preview.
7. Save an asset as a template, create a new asset from it, edit the new one, and confirm the template itself is unchanged.
8. The actual drag-authoring *feel* (dragging path points, dragging Zolumn fields between columns) is an interactive, visual judgment call for a human to make in the Editor — confirm it compiles and drives correctly from script where possible, but the UX verdict needs a person actually using it.
