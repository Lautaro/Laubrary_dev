# Chunks 2.0 — Layered Composite FX, Design Doc (draft, awaiting greenlight)

Status: **draft for review** — this is the design document referenced by AgentHQ task T-0031. Nothing described here is built yet. Once reviewed, the user greenlights a task that decomposes this doc into individual AgentHQ tasks under a `ChunksOverhaul-2026-08-18` node.

## Vision

Chunks becomes the tool for building complex, layered destruction/impact effects out of existing Laubrary pieces (PyrePlus blasts — Pyre is deprecated as of 2026-08-18, see the project CLAUDE.md — a Zoe's or any sprite's own art, palette-sampled particles) — composed and sequenced, not just a flat debris burst.

Worked example (the target "shows everything" case, kept as the north star — but every piece below must also work completely alone):

A spaceship Zoe dies. Its idle frame (its only real animation) is sliced into 3 large, recognizable fragments. Each fragment launches outward with rotation, and over its lifetime spawns 5–8 small pyres (randomly chosen from a configured pool for variety), each pyre itself moving in the *opposite* direction to its parent fragment — so it reads as a piece of hull trailing fire as it flies away. Independently, the whole explosion is layered with its own bigger pyres: a light blast shockwave up front, a slow dark smoke cloud in back, and a mid fireball that temporarily covers the fragments as it expands then recedes. Every one of these — the shockwave, each fragment, the smoke, the fireball — occupies its own named, ordered layer, so draw order is authored, not accidental. On top of everything, a shower of 1–3px particles sampled from the exploding sprite's own colours flies outward from the center.

## Design principles

1. **Standalone-first.** Every module below must be usable completely on its own — "just spawn one pyre" or "just add a particle splash" must not require touching layering, timelines, or Mirage.
2. **Composable via one shared primitive.** Layering (draw-order arrangement of named slots) is a cross-cutting concern already duplicated ad hoc in `ZoetropePyre/SpawnPyreFx.cs`, `ZoetropePyre/PyreChunksFx.cs`, and `Chunks/ChunkEmitter.cs` (each hand-rolls a single flat `sortingOrder` int, no stacking). It becomes a new zero-dependency module, `Laubrary.Layering` (mirrors the existing `Laubrary.PreviewKit` pattern — a tiny shared type other tools reference), so Pyre/PyrePlus/Zoetrope can adopt it too, not just Chunks.
3. **One preview solution.** Mirage, not a bespoke Chunks preview stage — reached via a "Preview in Mirage" shortcut button that builds a throwaway `MirageView` (never saved as an asset) and opens it, exactly the pattern `ZoetropeWindows.cs` already uses for a Zoe's rig (`PreviewInMirage(zoe)` → `MirageWindow.OpenFor(view)`).
4. **UI scales with complexity.** A one-fragment, one-pyre Chunk process gets a small, simple UI. A fully layered process gets a UI for every module it uses. Nothing is hidden behind unnecessary nesting, and nothing is shown that isn't in use.
5. **ZUI only, PyrePlus as the UI exemplar.** No raw IMGUI/EditorGUILayout. Context menus use the Envelope-style popover card. Any missing ZUI control gets flagged as a candidate for a new reusable ZUI control, not hand-rolled locally (mirrors the project's standing ZUI-first rule).

## Standalone modules

Each is independently usable; the "layers" module is what lets several of them cohabit one composed process.

1. **Particle Splash** — samples colours off a source sprite (à la Larder's `WareDebris` palette sampling) and sprays a configurable number of 1–3px particles of random shape outward from the sprite's own footprint, with its own spread/speed/life settings. Usable alone as "add a splash of the character's own colours" with no fragment, no pyre, no timeline involved.
2. **Pyre Spawner** — spawns one PyrePlus blast (Pyre is deprecated; target PyrePlus only for new work), or picks randomly from a configured pool of blasts, at a point. This already exists in embryonic form (`ChunkSpec.animationSource` / `IChunkAnimation`, `ZoetropePyre/SpawnPyreFx.cs` — that bridge itself still targets old Pyre and may need a PyrePlus-equivalent as part of this work) — this module formalizes it as a standalone, reusable piece rather than something only reachable through a full Chunks burst.
3. **Pyre Movement** — gives a spawned pyre (or each pyre in a pool) real physical motion — velocity, gravity, drag, arc — independent of the "spawn and let it play in place" default. `ChunkSpec` physics fields already do this for debris chunks; this module is the same physics applied to a pyre/blast instance instead of a debris sprite.
4. **Fragment Slicer** — splits a source sprite into a small number (2–6, not dozens) of large, recognizable pieces, each with its own physical movement. Distinct from the existing `SampledChunkSprites` (which is tuned for many small, non-recognizable debris bits) — this is a new slicing mode aimed at "the pieces still read as parts of the original thing."
5. **Layer Stack** (`Laubrary.Layering.LayerSpec`) — an ordered list of named slots (e.g. `Blast`, `Fragment1`, `Smoke`, `Fragment2`, `Fireball`, `Fragment3`), each resolving to a concrete Unity `sortingOrder` against one shared `baseOrder`/`step`, all living in one caller-chosen `sortingLayerName` (the "local layers → world layers" translation the whole composed effect needs). Arranging modules 1–4 (plus any fragments) into a stack is what turns several standalone pieces into one layered process.
6. **Timeline** — the authoring/playback backbone for a composed Chunk process: when each module fires relative to burst-start, plus two event types fired along the way — a **Code Event** (free-text hook, fires an arbitrary named callback the game code can subscribe to) and a **Zound Event** (built-in, references a Zound via a picker — never a typed string, per the project's "never type a reference string" rule — so wiring audio to any point in the process is a drag-a-marker operation, not code).
7. **Mirage Preview** — a "Preview in Mirage" button on the Chunks window that builds a throwaway `MirageView` holding the current recipe and opens Mirage, looping playback and exposing the Timeline (module 6) for scrubbing. Mirage previews are built for continuous/looping assets; a Chunks burst is a one-shot — this needs a re-trigger/replay affordance (a HUD button) rather than assuming infinite loop, which needs confirming as cheap in Mirage before committing to this shape.
8. **Spawn Formation** (added 2026-08-24, AgentHQ T-0079) — given a shape spec (line, circle/ring) and a count, computes N world offsets from an origin and calls the Pyre Spawner (module 2) once per offset with a per-instance time delay ("life stagger" — spawn point N fires `staggerSeconds * N` after burst-start, not simultaneously). Each spawn point independently draws either one fixed blast or randomly from a configured pool (reusing module 2's own pool-random feature) — this module's own job is purely placement + stagger, not picking. Neither Chunks nor Pyre has any shape/formation utility today (confirmed via grep — Pyre's "Shape"/"ShapeForm" hits are the blast's visual FORM enum, unrelated to spawn positioning). Depends on module 2.
9. **Follow Emitter** (added 2026-08-24, AgentHQ T-0080) — a component, sibling to the base burst emitter, that tracks an external Transform every frame (repositioning its emission point to it), sprays Particle Splash (module 1) particles in the reverse of that transform's current frame-to-frame travel direction, and fires a Pyre Spawner (module 2) burst on a repeating interval at the tracked position — for a debris source that chases a moving character and periodically flares up behind it. Every Chunks burst today is strictly one-shot (fire once, done); nothing attaches to and continuously tracks an external Transform or re-fires on a timer. Depends on modules 1 and 2.

## UI architecture

- **Timeline is the structural backbone** — a bottom-docked track view, in the spirit of PyrePlus's own envelope timeline, showing when each attached module fires and where its events sit.
- **Each module gets its own collapsible ZUI section**, shown only when that module is actually part of the recipe — a simple "one pyre, no layers" Chunk shows one small panel; a fully layered process shows one panel per module in use, stacked under the timeline.
- **Layer reordering** uses drag-reorder (the `ZuiThumbGrid` pattern already shared between PyrePlus's CherryFraming and the Laumination Builder) inside the Layer Stack panel.
- **All context menus** (add a layer, add a pool entry, add an event) use the Envelope-style popover card — that anchor-position math is already solved there; reuse it, don't reinvent it.
- **No raw IMGUI.** Every control is a ZUI wrapper. A missing control (text-input field, object/asset picker, color field, enum popup — the known current ZUI gaps — plus, newly, a **Unity Sorting Layer picker** for the Layer Stack's `sortingLayerName`) gets raised as a ZUI expansion candidate rather than a one-off raw fallback.

## New shared primitives this design needs

- `Laubrary.Layering` (new zero-dep runtime module) — the `LayerSpec` ordered-named-layer type (see prior design discussion this session for the concrete shape).
- Fragment Slicer (big-piece cut) — new, alongside the existing small-debris `SampledChunkSprites`.
- Reversed-relative-motion for a pyre/trail spawn relative to its parent fragment — a small addition to the existing `IChunkTrailSource` interval-spawn path. Module 9 (Follow Emitter) needs this same idea generalized to work relative to an externally followed Transform's frame-to-frame travel direction, not only a fragment's — build it as one shared primitive both callers use, not two copies.
- Palette-sampled particle shower — ported/adapted from Larder's `WareDebris` colour sampling.
- Timeline + event authoring (Code Event = free text; Zound Event = LauAsset-picked reference).
- Possibly a Sorting Layer ZUI picker control (see UI architecture above).
- Shape/formation offset generator (line, circle/ring → N world offsets from an origin) for module 8 — does not exist anywhere in Chunks or Pyre today, confirmed by grep.

## Open questions / risks

- Mirage previews loop by default; a one-shot burst needs a replay affordance — unconfirmed how cheap that is in Mirage today.
- Whether other tools (PyrePlus, Zoetrope) should adopt `Laubrary.Layering` immediately or only once Chunks proves it out — leaning toward "Chunks proves it first," but flagging since `SpawnPyreFx`/`PyreChunksFx` have the identical flat-`sortingOrder` problem today. (Pyre itself is deprecated as of 2026-08-18, not a target for this — and note `SpawnPyreFx`/`PyreChunksFx` still bridge to old Pyre, not PyrePlus, which this design's own Pyre Spawner module will need to resolve.)
- Dev-host housekeeping (which project becomes the isolated Chunks dev clone) is being resolved separately — see the AgentHQ task note; it does not block this design doc.

## Not in scope for this doc

- The Laumination Builder's open zones-vs-frame-index question (separate, unrelated feature — not to be conflated with Chunks work).
