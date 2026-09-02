# Chunks editor programme — design decisions (2026-09-02)

This is the build spec every task in the AgentHQ node `ChunksEditor-2026-09-02` implements against. It resolves the questions the mock blueprint (`workspace/T-0123/CHUNKS-EDITOR-BLUEPRINT.md`) left open and turns the T-0119 research (`attachments/T-0119/*.md`) into decisions. Where this file and an older doc disagree, this file wins. Where this file and the engine source disagree, verify in source and report it in your handover.

## 1. What Chunks is

A **chunk recipe** (`ChunkSpec`, a LauAsset) is an ordered **stack of capabilities**. Each capability is one authored unit with its own fields, its own place on the recipe's clock, and its own contribution to the preview. The recipe shows only the surfaces its capabilities bring: a one-capability recipe has no timing surface, no layer surface, no foreign dials anywhere. "Play one Pyre" is not a Chunks job (reference the Pyre directly); Chunks exists to compose or vary: several Pyres in a pattern, Pyres that fly, a sprite fractured and flung, a palette spray off a Zoe, all of it on one clock with cues.

## 2. Data model — DECIDED

- `ChunkSpec` keeps its type name (every consumer, demo and `[CreateAssetMenu]` already points at it) and gains:
  - `[SerializeReference] List<ChunkCapability> capabilities` — the stack, in authored order.
  - `int schemaVersion` (0 = legacy layout, 1 = capability stack). An upgrade routine migrates a legacy asset ONCE: each enabled fixed slot becomes a capability, the legacy debris fields become a `DebrisScatter` capability when the legacy count is > 0 or sprites/sample source are set, `blastGroups` become further `PyreBlast` capabilities, `timeline.tracks` delays are written onto the matching capabilities' `delay`, markers stay on the recipe's `cues`. The legacy fields stay on the class for one release, `[HideInInspector]`, read only by the upgrade. **Migration must run through the editor's normal serialization path (an Editor script + `AssetDatabase.SaveAssets`), never via a CLI `eval` save** — saving a SerializeReference asset from a CLI eval destroyed managed-reference ids in the Shaper programme.
- `ChunkCapability` (abstract, `[Serializable]`): `string id` (a GUID string minted on creation — the stable key for timing, targets, view state), `bool enabled = true`, `string displayName` (optional override; empty = the kind's name), `float delay` (**absolute seconds from the recipe start**; capabilities overlap freely), and virtuals: `string KindName`, `bool OccupiesTime` (false for coordinators), `float DurationSeconds(ChunkSpec)` (how long its output is on screen, for the clock), `void Fire(in ChunkModuleContext)`, `string LayerName` where relevant. `IChunkModule` is folded into this base; keep the `ChunkModuleContext` / `ChunkModuleRunner` mechanism.
- Timing is **absolute, multi-lane, one clock**: `delay` lives on the capability; `ChunkTimeline.tracks` is retired; `ChunkTimeline` becomes `ChunkCues` (markers: time, kind Code/Zound, name) — one list on the spec. The clock's length = max over ALL timed capabilities (enabled or not) of `delay + DurationSeconds`, plus the latest cue.
- Modifier capabilities carry `string targetId` — the id of the producer they act on, or empty = every compatible producer (the current behaviour, so migrated assets don't change).
- `ChunkModules.Run` iterates the stack in order: fire each enabled producer at its `delay` (0 = immediately, same as today), modifiers resolve their targets, coordinators contribute context (Layer Plan supplies the `LayerSpec`; Cues fire their markers). `ChunkEmitter.SpawnBurst` becomes thin: make the container, run the stack, size the container lifetime from the clock.
- Determinism: capabilities hash from their own `seed`; no `UnityEngine.Random`/`System.Random` in code paths the preview also runs. The runtime may keep variety where it already does, but the **preview must be deterministic** per seed.

## 3. Capability catalogue — DECIDED

Kinds the tool ships (class names in code, user-facing names in the UI):

| Kind | Role | Basis today | Card body (packed rows, see §5) |
|---|---|---|---|
| **Debris Scatter** (`DebrisScatter`) | producer | legacy ChunkSpec emission/physics/life/floor/sampled/animated fields | Visual: Segmented `Squares / Sprites / Sampled / Animated` (that choice shows only its own fields: sprite list, sample source + px + tumble + tint + modifiers, or the animation picker). Count · Speed · Direction/Spread (a `Z.Pad`-style direction + spread slider) · Upward bias. Gravity · Drag · Spin · Face velocity. Life · Size · Size/Alpha/Colour over life. Floor box (header toggle): Y · Bounce · Friction · Rest. |
| **Fragment Fracture** (`FragmentFracture`) | producer | FragmentSlicerModule | Source picker (visual or sprite) · Pieces · Min area · Seed. Speed · Direction/Spread (inherit burst toggle) · Gravity · Drag · Spin. Life · Alpha over life. Layer slot. |
| **Palette Splash** (`PaletteSplash`) | producer | ParticleSplashModule | Sprite/source · From footprint · Count · Size px. Speed · Direction/Spread (inherit toggle) · Gravity · Drag. Life · Alpha over life · Seed. Layer slot. |
| **Pyre Blast** (`PyreBlast`) | producer | PyreSpawnModule (+ its formation, + SpawnFormationModule, + blastGroups) | Pyre picker (LauAsset picker over `IChunkEffectSpawner`; alternates list = the pool) · Pattern: Segmented `Single / Line / Ring` (formation fields appear only for Line/Ring: Count · Length/Radius · Angle/Arc · Jitter · Stagger · Stagger jitter · Order · Seed). Offset (`Z.Pad`) · Rotation mode · Scale range. Layer slot. The standalone `SpawnFormationModule` is migration input only — it becomes a `PyreBlast` with Pattern ≠ Single. |
| **Trajectory** (`Trajectory`) | modifier | PyreMotionModule | Target picker (sibling producers that spawn Pyres; "All" default) · Speed · Direction/Spread (inherit toggle) · Upward bias. Gravity · Drag · Face velocity. Life: `Until target ends` toggle or seconds. Seed. |
| **Trail** (`Trail`) | modifier | ChunkSpec trailSource/trailInterval | Target picker (Debris / Fracture producers) · Trail source picker (`IChunkTrailSource`) · Interval. |
| **Hits** (`Hits`) | modifier | ChunkSpec useHitDetection/hitDamage/hitRadiusScale | Target picker · Damage · Radius scale. |
| **Layer Plan** (`LayerPlan`) | coordinator (no time) | LayerSpec | The ordered named layer list (name · opacity/blend if `LayerSpec` has them · reorder · remove · Add layer). Producers' Layer slot pickers read from this card; with no Layer Plan the slot picker is absent and everything draws in stack order. |
| **Cues** (`Cues`) | coordinator (occupies time only via its markers) | ChunkTimeline markers + Zound hook | Marker list: time · kind Segmented `Code / Zound` · name (Code = declared text field; Zound = the existing Zound picker hook). Markers also draw on the Timing ruler. |

The Follow Emitter (`ChunkFollowEmitter`, a MonoBehaviour) stays a component with its own inspector; it is not a capability. Its inspector is ported/vetted to the UI guide in this programme.

## 4. Window — DECIDED (Pyre's shape)

`ChunkWindow : ZuiAssetWindow<ChunkSpec>` (AssetKit browser, New/Duplicate/Rename/Delete, Tags — all inherited). Menu stays exactly `Laubrary/Chunks`. Layout copies `PyreWindow.BuildAsset` (`Editor/Pyre/PyreWindow.cs:307-420`):

- **Top**: `ZuiSectionToggleBar("Chunks", Tags, Views, Recipe, Preview, Timing)`; Tags re-parented under the bar exactly as Pyre does.
- **Left pane** (fixed width, persisted, min 460pt per the blueprint's measured widest row; dragged by a 6px splitter like Pyre's; **double-click the splitter resets** it — the blueprint's REQUIRED layout reset): Views section (`ZuiViewBar`, store at `Assets/Chunks/ChunkViews.asset`), then the **Recipe** section: one `Z.BoxKeyed` card per capability keyed by `capability.id`, in stack order; a single `Add capability…` button under the stack that opens a `ZuiMenu` listing the nine kinds (modifiers greyed with a tooltip until a compatible producer exists). Card header = caret · icon · NAME · gap · `On` toggle · ▲▼ reorder · ×. Nothing else in a header. `On` off = card body inert (SetEnabled false) and lane drawn dim; the fold hides.
- **Right pane** (fills): the **Preview** stage (custom-painted, `zui-stage` class) with a resize bar, then a transport row: Play/Pause · Replay · Loop toggle · scrub slider over the recipe clock · a `t = 0.00 / 1.20 s` readout · `Preview in Mirage` button (the existing Mirage handoff). Then the BackSplash backdrop panel (`BuildBackdropPanel`, same as Pyre). Then the **Timing** section: present only when the recipe has two or more time-occupying capabilities or any cue; when present it holds the new ZUI multi-lane control (one lane per timed capability, enabled or not, dim when off; cue markers on the ruler; the shared playhead IS the transport's time). It sits BELOW the preview, never above.
- **Stable workspace**: per-card rebuild (`RebuildCard(id)`), not whole-window, for dial edits that show/hide fields; scroll offset and playhead survive any rebuild; toggling a capability never resizes the preview (the Timing section keeps its lane count; it appears/disappears only on add/remove, and the preview's height is fixed by the resize bar).
- **Undo**: every data edit through the window's `Dial(label, apply)` / `DialAndRebuildCard(id, label, apply)`; add/remove/reorder capability, add/remove layer, add/remove cue are all `Undo.RecordObject` edits. `ZuiWindow` rebuilds on undo/redo.
- **Lone-capability rule** (blueprint §5): a recipe with one timed capability shows no Delay dial on the card and no Timing section. Delay appears on cards only when Timing exists.

## 5. Card layout rules (from the UI guide + blueprint §3)

Packed rows, 3–4 controls per row, labels via `ZuiLabelAlign`; bounded scalars `Z.MicroSlider`; min/max pairs `Z.MicroMinMax`; enums `Z.Segmented` (≤3) or `Z.MiniRadio`; bools `Z.Toggle`; pickers `LauAssetElement.Build` (never a typed name); X/Y `Z.Pad`; curves `Z.Curve`; gradients `Z.Gradient`; every control a tooltip that reads as the effect. Explicit widths (`Num 70`, `Wide 150`, picker 200) — no stretched control. A box titled for one field is forbidden. Nothing on screen explains how it works — that is tooltip text.

## 6. Preview — DECIDED: a schematic that MOVES

The Chunks preview is a fast, deterministic **schematic** of the recipe over its clock, not a Mirage: origin cross, direction arrow + spread cone per producer, formation points numbered in stagger order, Pyre blasts as discs (radius from the Pyre's own preview size, thumbnail if cheap), debris/fragments/splash as dots on their real arcs (gravity, drag, upward bias, floor bounce for debris), trajectories as arcs from the target's points, layer colour by Layer-Plan slot, and cue ticks. **Time is real**: pressing Play advances the playhead at wall-clock speed, every guide shows its state at that time (a capability before its `delay` is a faint outline; after its duration it is gone), the clock loops when Loop is on, Replay restarts, scrubbing the transport or the Timing playhead moves the picture, and dialling any card mid-play changes the next frame. Pyre's transport (`PyreWindow.Preview.cs`) is the behavioural reference for the transport; the Pyre behaviour checklist (`workspace/T-0189/PYRE-BEHAVIOUR-CHECKLIST.md`) is the model for how it is verified.

## 7. Out of scope for this programme (filed, not built)

- Flipping the Pyre→Chunks assembly reference (`attachments/T-0119/…DEPENDENCY_CORRECTION.md`). The window uses the `IChunkEffectSpawner` picker, which already shows Pyre assets.
- A Unity ParticleSystem wrapper capability ("Generic Particle Burst").
- Retiring the mock at `Assets/ChunksMock/` (untracked; leave it).

## 8. Resolved questions (raised by the behaviour checklist, T-0207, 2026-09-02)

1. **"Inherit burst direction"** means the direction the CALLER passes (`SpawnBurst`'s override) or, when none is passed, the recipe's own `ChunkSpec.directionDeg`, which stays a recipe-level field (the composition's default aim, not a Debris Scatter dial). The preview shows it as one `Burst direction` MicroSlider in the transport/chrome row, because it is the one composition-level input every producer can inherit.
2. **Spin is visible in the preview**: debris and fragment dots are small oriented squares, so their spin reads as rotation.
3. **Hits has a schematic tell**: a faint circle of radius `hitRadiusScale × size` around each dot of its target producer.
4. **Timing lane order follows the live stack order** (reordering a card reorders its lane).
5. **A cue crossing** highlights its tick on the Timing ruler while `|t − cue| < 0.1 s`; no stage drawing.
6. **An unassigned Layer slot** draws in stack order behind every slotted output — the same flat sorting fallback `ChunkModuleContext` uses today. The slot picker offers `(stack order)` as its first entry.
7. **Layer references are by name** (`LayerSpec.layers` is a `List<string>`); renaming a layer in the Layer Plan card rewrites every capability's slot that referenced the old name, inside the same Undo step. Removing a layer resets referencing slots to `(stack order)`.
8. **Hits greys in the Add-capability menu** until a Debris Scatter or Fragment Fracture exists; Trajectory until a Pyre Blast exists; Trail until a Debris Scatter or Fragment Fracture exists.
9. **A Pyre Blast's pool pick is deterministic in the preview**: hash(seed, instance index) selects the alternate; the runtime may keep its random pick.
10. `LayerSpec` has no opacity/blend fields today — the Layer Plan card shows name + reorder + remove only; do not add fields the runtime cannot honour.
