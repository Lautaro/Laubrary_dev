# PyrePlus — modular, opt-in Pyre rework (parallel prototype)

> **HANDOFF STATUS (2026-07-23).** The IMGUI → UI Toolkit switch this design was once shelved for is **done** — the whole Laubrary editor toolkit is UI Toolkit (`Laubrary.Zui`, the `Z.*` factory) and every tool window is ported. Per the original shelving note, that means this design was **reconsidered for a UITK world**, not ported verbatim: **Zolumn is dropped** (the UITK toolkit already gives collapsible bordered sections that fold — `ZuiSection`/`ZuiBox` — plus a grow-to-fill column layout and a `Z.Divider`), and the window is built directly on `ZuiAssetWindow<T>` + `Z.*` controls. **Slice 1 is BUILT and committed** (Shape section + renderer + window, rendering one live particle). This document is the brief for **Fable Claude** to continue from there. Everything below is current unless a paragraph says "(SLICE 1, DONE)".

## Purpose

Pyre's `Layer` (`Runtime/Pyre/Layer.cs`) is one flat ~60-field bag mixing placement, population, per-particle shape, and modifiers, all always visible regardless of whether they currently do anything (this session added Fire/Fireball/Matte fields to it, making the point sharper). PyrePlus is a restructured data model + UI, validated as a **parallel prototype — its own asset type, its own renderer, its own window** — so the shipping Pyre tool is **never** put at risk. Folding this back into real `Pyre`/`Layer`/`BlastRenderer`, and ideally emitting real `Pyre` assets from it, is explicit future work, not part of this build.

## What exists right now (SLICE 1, DONE — read this before touching anything)

- **Menu:** `Laubrary/Pyre Plus` opens `PyrePlusWindow`.
- **`Runtime/PyrePlus/`** — asmdef `com.Lautaro-Arino.Laubrary.PyrePlus` (references `ZuiRuntime` for `ZUIValue` + `Pyre` for `PyreModifier`):
  - `PyrePlusSpec.cs` — the asset. `[CreateAssetMenu("Laubrary/Pyre Plus")]`. Canvas/timing fields (`canvasSize`, `frameCount`, `seed`, `background`, `pixelsPerUnit`); the **Shape** section (`colorOverLife` Gradient, `alpha` ZUIValue, `size` ZUIValue, `edgeSoftness` float); a `swarmEnabled` stub; a `[SerializeReference] List<PyreModifier> modifiers`; cosmetic preview state.
  - `PyrePlusRenderer.cs` — `RenderFrame(spec, frameIndex) → Color32[]` and `RenderFrameTexture(...)`. Pure, static, deterministic. Renders **one centred particle** through the Shape fields (soft disc, alpha·colour(life)·edge, Over-composited). Has the `Eval(ZUIValue, life, seed, particleIndex, fieldId)` helper (mirrors `BlastRenderer.Eval` — Static reads the value, Curve reads the envelope at the particle's life, MinMax draws once from a seeded `System.Random`) and a `Hash(...)`. **This is the pattern to extend** for Swarm.
- **`Editor/PyrePlus/`** — asmdef `com.Lautaro-Arino.Laubrary.PyrePlus.Editor` (references `PyrePlus`, `Pyre`, `AssetKit.Editor`, `Zui.Editor`, `ZuiRuntime`):
  - `PyrePlusWindow.cs` — `ZuiAssetWindow<PyrePlusSpec>`. Left pane = a `ScrollView` of `Z.Box`/`Z.Section` sections (Canvas, Shape). Right pane = an `IMGUIContainer` preview with a looping transport (frame-gated re-render via `previewDirty`, so a replay-style renderer isn't re-run every repaint — see the Fire lag note below). `BuildAsset(root, spec)` is the entry point; helpers `Val(...)` (a grow-enabled `ZuiValueControl` row), `WrapRow(...)`, `Dirty(...)`.

Open it, hit New, and you get a pulsing soft particle. That is the whole of slice 1.

## The UITK conventions PyrePlus MUST follow (this is the new world)

Read `references/ui-layout-rules.md` in the `laubrary` skill IN FULL before writing UI — it is mandatory, not optional. Then:

- **Every control is a `Z.*` factory and REQUIRES a tooltip.** No raw `EditorGUILayout`/`GUILayout`, no bare labels. Use `Z.Field`, `Z.Row`, `Z.Box`, `Z.Section` (collapsible), `Z.MicroSlider`, `Z.Toggle`/`Z.ToggleButton`, `Z.MiniRadio`/`Z.Segmented`, `Z.EnumDropdown`, `Z.Color`, `Z.Object`, `Z.Value` (the animatable `ZUIValue` control), `Z.Value2D` (the animatable XY pair), `Z.Pad` (plain Vector2). Genuine canvas painting (the preview) stays an `IMGUIContainer` — that is the one sanctioned IMGUI island.
- **Animatable fields are `ZUIValue`, drawn with `Z.Value`/`ZuiValueControl`.** Its Static mode now draws a **MicroSlider** by default — label AND value inside the filled track, no thumb (the fill is the handle), a horizontal-gradient fill. MinMax and Curve modes still use an external label. Turn on `Options.grow` (or `WithGrow()`) so a control fills the row's spare width up to a cap and packed pairs share/reflow — PyrePlus's `Val(...)` already does this. (This is exactly the "use the empty horizontal space" and "label inside the microslider" feedback that drove the current look; keep it.)
- **Plain (non-`ZUIValue`) sliders should also be `Z.MicroSlider` with the label inside**, not `Z.Slider` (which is the vanilla thumbed Unity slider) and not a MicroSlider with an external `Z.Field` label. The convention across Pyre is: label + value INSIDE the microslider track. Pyre's `PackedSlider` helper is the reference.
- **Sections fold.** `Z.Section(title, tooltip)` is a collapsible header that owns its body; `Z.Box(title, tooltip)` is a framed foldable box. Use `Z.Section` for the three top-level sections (Shape / Swarm / Modifiers) and `Z.Box` for sub-groups. The design's "gear panel to opt fields in/out" is NOT needed as its own mechanism — a section that folds, plus a single `Z.Toggle` gating the opt-in fields' visibility (rebuild on toggle), gives the same result far more simply. Prefer that.
- **Every edit records Undo and marks the preview dirty.** Route mutations through a `Dirty(Action)` helper: `Undo.RecordObject(spec, "...")` → apply → `EditorUtility.SetDirty(spec)` → mark the preview dirty. The value controls take an `onBeforeMutate` (record) + `onChanged` (dirty) pair.
- **Do NOT add any menu item beyond `Laubrary/Pyre Plus`.** (Laubrary house rule — no speculative menu items.)
- **Verification (do this, every slice):** flip fields / build a spec **in memory** via `execute_script` (never write a `.asset` from a probe, and never call `AssetDatabase.SaveAssets()` — two assets were silently gutted this way earlier); run `ZuiAudit.Audit(window, out int skipped)` and require **0 findings with skipped==0** (a folded section audits clean falsely — `ZuiAudit.ExpandAll(window)` first); and BAKE frames to a PNG strip and read them back by eye (a scratchpad `CaptureWindow.ps1` does a DPI-aware PrintWindow by window title). For a generator, correctness is visual — "compiles + 0 audit findings" is necessary, not sufficient.

## Determinism contract (non-negotiable — the renderer's whole point)

`PyrePlusRenderer` mirrors `BlastRenderer`'s contract exactly: **pure, static, every random value derived from a seeded `System.Random` keyed by `(seed, particleIndex, fieldId, …)` — never `UnityEngine.Random`, never `Time`**, so preview, bake and runtime produce **byte-identical** output from the same inputs, and any frame renders on its own (no state carried between frames). `Eval(ZUIValue, life, seed, particleIndex, fieldId)` in the renderer is the single funnel for this — a MinMax field becomes a per-particle random draw, a Curve field reads the envelope at that particle's own life, a Static field is the value. **Verify determinism per slice:** render a frame cold, render it again after a forward pass, compare — must be 0 differing pixels. (`PyreNoise` in `Runtime/Pyre/PyreModifiers.cs` is `internal` and NOT reachable from PyrePlus — write your own hash/value-noise if you need noise; the renderer already has a `Hash`.)

## Remaining work

### SLICE 2 — Swarm (the big one)

A single top-level `Z.Toggle` "Swarm" gates the whole section (off = exactly one particle, the current behaviour; the section's other controls are hidden — rebuild on toggle). When on:

- **Mandatory:** `count` (int, min 2) — an ordinary field.
- **Spawn mode** (`Z.Segmented`/`MiniRadio`): **Area** | **Path**.
- **Shape kind** (both modes): **Circle | Triangle | Square | Pentagon | Hexagon**, plus **Custom** (Path only). This is a regular polygon by side count (Circle = ∞ sides). The original design pointed at `PolygonRadiusFactor`/`BandSides` in `BlastRenderer.cs` as reusable, but **those exact symbols may have drifted or never landed — grep the current `Runtime/Pyre/BlastRenderer.cs` for the Rosing (`ScatterMode.Rosing`) placement math before assuming; if it isn't there, write the polygon boundary math fresh** (a point-in-regular-polygon test and a random-point-in-polygon are both short). Custom = a hand-drawn polyline authored via `Z.Value2D` in animate-over-time (Curve) mode (click to add points, point order is time) and/or click-to-add on the preview canvas.
- **Area:** each particle is a uniformly-random point inside the chosen shape's boundary (generalise the existing random-in-disc to the polygon boundary). No progress/spawn-point concept.
- **Path:** placement is a **spawn point travelling along the shape**, positioned by a **`progress` ZUIValue** (Static/MinMax/Curve, 0→1 around the shape, 0 = start, 1 = end/once-around). Because `progress` is an authored envelope it can ease/hold/rewind; **each particle samples `progress` at its OWN spawn frame and keeps that position for its whole life** (a spawn-time snapshot, not a shared live value) — so particles born while `progress` moves slowly cluster, and fast-moving ones spread. Spawn *timing* is a plain even distribution across the swarm's life window.
- **Shared shape transform**, applied to the whole Area/Path shape: **Position** (`Z.Value2D`), **Scale** (`ZUIValue`, with an optional discrete-step snap — evaluate then round to the nearest step so placements land on fixed radii), **Rotation 2D** (`ZUIValue` degrees), **Rotation 3D** (pitch + yaw, two `ZUIValue`s — a pseudo-3D tilt: rotate the shape's points in 3D, project, and scale/brighten by depth so nearer parts render bigger/brighter). The original design cited `RotatePitchYaw`/`roseZ`/`zSizeInfluence` in `BlastRenderer.cs`; **those names may not exist as written — the pseudo-3D lighting/normal math in `BlastRenderer.cs` (search for the `lx/ly/lz` normal-dot-light block) is the style to mirror, but expect to write the tilt+project fresh.**
- **The crucial snapshot rule:** each particle's placement is computed against a **snapshot of the shape transform at THAT particle's spawn frame**, NOT the live transform — otherwise a growing/orbiting shape retroactively slides every already-placed particle instead of leaving a trail. (Pyre makes exactly this split deliberately: `ringExpand` is live so a ring visibly grows, but `spawnRadius`/`ringStartAngle` are spawn-time-snapshot so placed shapes don't slide — read those comments in `BlastRenderer.cs`'s scatter section for the reasoning.) This is the single most important thing to get right in Swarm.
- **Preview authoring overlay:** while editing Swarm, draw the active shape over the canvas plus a dot at every particle's ACTUAL computed spawn location (not just the outline), and make Custom points / the shape's position handle draggable. Mirror the gizmo-interaction style in Pyre's own `Editor/Pyre/PyreWindow.Preview.cs` (the MetaBlob orb markers, Pin-warp handles, Curl-vortex handles: `Handle*`/`Draw*Markers` methods) — click-to-add / drag / right-click-to-remove, mutation on mouse-up, wrapped in Undo.

### SLICE 3 — Modifiers

Reuses `PyreModifier` **directly** — its contract has NO `Pyre`/`Layer` coupling in any signature, so `PyrePlus.asmdef` already references `Pyre` and the `[SerializeReference] List<PyreModifier> modifiers` field is already on the spec. The four kinds and their apply points (all in `Runtime/Pyre/PyreModifiers.cs`):
- `PyreModifier.Prepare(Func<ZUIValue,int,float> eval)` — call before applying, passing an eval closure that resolves the modifier's own animatable fields (mirror how `BlastRenderer` builds that closure).
- `GeometryModifier.InverseWarp(Vector2 off, float phase, in GeoCtx ctx)` — a coordinate warp applied when sampling each pixel's source position.
- `PixelModifier.ApplyPixel(ref Color col, ref float alpha, in PixelInfo info)` — per-pixel colour/alpha (return false to drop the pixel).
- `PostModifier.Apply(Color32[] buf, int W, int H)` — a whole-buffer pass (Bloom, Outline, **Kaleidoscope** — new this session, mirrors a layer into N arms, a good demo for PyrePlus).
Apply them exactly as `BlastRenderer` does (study its per-layer modifier application: geometry warps folded into the sample position, pixel modifiers per lit pixel, post modifiers over the finished buffer). The UI is the same enable/reorder/"+ Add modifier" loop Pyre already draws — reuse Pyre's editor pattern (`Editor/Pyre/PyreWindow.Modifiers.cs` — the `GenericMenu` "+Add" flow and the per-modifier bodies) rather than reinventing it; the shared drag-reorder grip is `ZuiReorder` in the toolkit.

### Opt-in Shape fields (small, do alongside Swarm or after)

- **Position** — the particle's own path after birth, on its own life clock — `Z.Value2D` in Curve (animate-over-time) mode.
- **2D spin** — one `ZUIValue` of degrees, the particle's own pixels rotating in place (2D; the 3D tilt belongs to the swarm shape transform).
Gate both behind a single "advanced" toggle in the Shape section (rebuild on toggle) rather than a gear panel.

## Non-goals for this prototype

- Layers / layer groups / the layer-library save-recall popup (Pyre-only for now).
- Rosing's nested multi-ring composition (Swarm's closed shapes here are single-ring equivalents).
- Bars / MetaBlob / SparkleField / Sprite / Fire / Fireball / Height-balls particle forms — Shape here is a soft disc only.
- Sprite-sheet / AnimationClip export (`BlastBaker` equivalent) and the attached-to-a-character preview (`IPyrePreviewSubject`).
- Converting/exporting to a real `Pyre` asset — worth doing once this data model stabilises, but out of scope here.
- Zolumn (columns/gear-panel/drag-between-columns/layout-persistence/templates) — **shelved**; see `ZOLUMN_DESIGN.md`. If field density ever demands columns, revisit then, built on the UITK toolkit.

## Coplay bridge (target THIS editor first, every session)

The Coplay MCP discovers every open Unity editor. Before any Coplay action: `list_unity_project_roots` → confirm `D:\Unity\Laubrary Dev` is present → `set_unity_project_root` to it → verify with `execute_script` logging `Application.dataPath` (must resolve under `D:\Unity\Laubrary Dev`). If it points elsewhere, `check_compile_errors` looks clean despite broken code and reflection won't find new types. `set_unity_project_root` is per-session.

## Verification checklist (run before calling a slice done)

1. `check_compile_errors` via the Coplay bridge (after the bridge setup above).
2. Open the window, New asset, confirm the default view is minimal (Shape only; Swarm off; Modifiers empty).
3. Swarm on, count > 1: exercise Area vs Path for each shape kind incl. Custom; author a `progress` envelope that eases/holds/rewinds and confirm placements trace it (a linear ramp = once around, a rewind = a visible retrace).
4. Animate the shape transform live and confirm already-spawned particles do NOT retroactively slide (spawn-time snapshot) while new spawns trail the live transform.
5. Enable Rotation 3D → depth-shaded dots (nearer bigger/brighter).
6. `ZuiAudit` clean with sections expanded; determinism check (cold vs sequential = 0 differing pixels); a baked PNG strip read by eye.
7. `CHANGELOG.md` gets an `## [Unreleased]` entry per slice. Commit per slice (the branch is `feat/zui-uitoolkit`; commit freely).
8. The drag-authoring FEEL (dragging path points, dragging the shape handle) is a human's visual judgement — drive it from script where possible, but flag it for a person to actually use.
