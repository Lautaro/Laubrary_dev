# Chunks capability investigation and module-boundary proposal

## Outcome

Chunks should be a composition tool for authored visual events. Its modules should be divided by the meaningful result they create, not by the fields that happen to be technically related. A module either produces a visible result, modifies an explicitly selected produced result, or coordinates several results. Sources, triggering, pooling, preview plumbing and gameplay consequences are not Chunk effect modules.

This document extends the prior T-0119 research. It does not author code, change the current ChunkSpec, or prescribe the final mock editor layout. Its purpose is to give that mock a complete, bounded capability catalogue.

## Evidence investigated

### Current Chunks

The original ChunkSpec owns a complete debris burst: count, directions and speed; gravity, drag and spin; life, size/fade/colour curves; floor bounce; procedural sprites, supplied sprites, sampled pseudo-3D debris, tint and SpriteFx modifiers; animated debris; trails; and cheap hit detection.

Chunks 2.0 then added a second model inside the same asset: Palette Splash, Pyre Spawn, Pyre Movement, Fragment Slicer, Spawn Formation, extra blast groups, Layer Stack and Chunk Timeline. The current seam is deliberately small: IChunkModule exposes Enabled, LayerName and Fire(context). The context supplies origin, parent container, direction, palette, optional layer plan, sorting fallback and a runner for delayed/repeated/moving work.

Important existing constraints to preserve:

- Chunks cannot directly reference Pyre because Pyre already references Chunks. Pyre spawning must remain behind the existing effect-spawner interface/bridge.
- Chunks must never add a component to a pooled Pyre instance. The existing external runner is the correct ownership pattern for movement and delayed work.
- Layering is currently resolved once by the context, with a flat sorting fallback. That is a sound shared service, not a reason to put a layer UI in every module.
- The existing timeline is already one shared multi-lane controller. It should be retained as the sole timing surface, but must no longer be universal.
- Zoe and Pyre can now be picked directly for relevant source/effect roles, avoiding dead wrapper-only pickers.

### Adjacent sources

Pyre provides authored, reusable baked visual effects—including explosion/jet, arc, plasma bloom and flame-like forms—that Chunks should spawn, arrange and combine rather than re-author.

Zoetrope provides Zoe as a meaningful visual source and supplies animation/state/event context. Chunks should be able to sample a chosen Zoe/visual frame, but should not absorb Zoetrope’s character, weapons, locomotion or reaction systems.

Launimator provides authored visual layers, animated sprite/reel content and frame/meta information. It is a valid visual-source provider and animated-content provider; it is not a Chunk module family.

Mirage is the right full-composition preview destination. Its temporary preview-object pattern is also the correct answer for preview helper ownership: preview state must not become a browser asset.

Shaper is the architectural influence: optional local capabilities sit on the thing that owns them, and nesting uses one coherent composition mechanism rather than parallel global lists. Its branch currently contains runtime contracts/audits rather than a complete editor workflow, so it informs ownership and boundaries, not final UI design.

## Capability taxonomy

### 1. Inputs and context — never effect modules

These are supplied by the caller or selected by an effect module; they should not appear as standalone cards:

- Trigger position, direction/aim, scale, seed and caller-provided sorting order.
- Visual source selection: Sprite, Zoe frame, Launimator/Reel frame or a caller-supplied palette.
- Shared palette extraction and optional palette override.
- Preview subject and preview transport.
- Pooling, lifecycle bookkeeping and effect-spawner bridge plumbing.

A mock editor should represent source selection where a capability needs it. It should not have a global “Source” section that makes every Chunk look as if it requires a Zoe.

### 2. Result-producing modules — valid first cards

These can each make a meaningful result at time zero, therefore each is a legitimate first and only module in a Chunk recipe.

| Module | Outcome | Minimum useful authoring data | Current basis |
|---|---|---|---|
| Debris Scatter | Throws many small bits | visual mode/source, amount, direction/spread, motion preset | Original ChunkSpec |
| Fragment Fracture | Breaks a source into a few recognisable pieces | source, piece count/cut style, initial motion | Fragment Slicer |
| Palette Splash | Sprays small palette-sampled pixels | source, amount, spread/direction, speed, gravity | Particle Splash |
| Pyre Burst | Plays one selected Pyre at a point | Pyre picker, scale/tint/layer | Pyre Spawn |
| Pyre Formation | Plays a selected Pyre repeatedly according to a pattern | Pyre picker, count, pattern, stagger, optional layer | Spawn Formation + Pyre Spawn |
| Generic Particle Burst | Spawns generic non-pixel particles through a constrained Unity ParticleSystem wrapper | particle preset/template, amount, motion, palette/tint mode, layer | New planned module |
| Animated Debris Scatter | Throws authored animated visual bits | animation/reel/Pyre picker, amount and motion | Existing animated content |

The first mock should start with four obvious choices: Pyre Formation, Fragment Fracture, Palette Splash and Generic Particle Burst. Debris Scatter and Animated Debris Scatter must remain represented in the catalogue because they are existing useful behaviour, but they should be presented as result types—not as emission/physics/life sections.

### 3. Modifier modules — require an explicit target

These are not useful on their own. They attach to a selected result-producing module or output collection. The editor must show a target picker/filter and should not offer a modifier before any compatible target exists.

| Modifier | Target | Owns | Does not own |
|---|---|---|---|
| Trajectory | Pyre Formation / Pyre Burst / Fragment Fracture / Debris Scatter output | direction, speed, gravity, drag, face velocity, arc | which effect is spawned |
| Transform Evolution | compatible spawned output | scale, alpha, tint or rotation over life | initial spawn layout |
| Repeated Emission / Trail | moving output or tracked emitter | interval, child effect/capability, stop rule | the parent’s movement |
| Palette/Tint Treatment | compatible colourable result | palette selection, tint/blend policy | the source image itself |
| Collision/Hits | explicit gameplay-facing output only | hit filter/radius/damage callback contract | visual composition semantics |

The current Pyre Movement section is evidence for this split: it has nothing meaningful to do without a Pyre-producing sibling. In the new model it should be “Trajectory,” attached to a selected producer, rather than a global section that must explain why it is disabled.

Hit detection is deliberately not part of the visual-first initial mock. It crosses into gameplay and should be retained as a later, explicit optional consequence module or emitter-side integration, never silently coupled to debris.

### 4. Coordinators — only appear when their prerequisites exist

| Coordinator | When it exists | Rules |
|---|---|---|
| Schedule / Timeline | two independently schedulable modules, or the user asks to offset/repeat/event-trigger one | one shared track view; keyed by stable module id; hidden otherwise |
| Layer Plan | two outputs need a deliberate front/back relationship | one compact composition-level order view; no per-module duplicated stack |
| Cue / Event Markers | author needs a Zound/code/event alongside a composition | markers belong to shared schedule, not a second timeline |
| Group / Variant | author needs repeated alternatives or random selection | belongs inside the relevant result producer, not as a top-level asset |

Layering and scheduling are relationships among modules, so they belong to the recipe composition layer. They are not “capabilities a burst inherently has.”

## Planned generic-particle wrapper module

Add Generic Particle Burst to the accepted future capability catalogue.

It should be a wrapper contract module, not an escape hatch to Unity’s full ParticleSystem inspector. The module gives Chunks a generic complement for smoke, glow, dust, sparks or soft stylised material where pixel-sampled custom sprites are not the desired effect.

### Contract

The recipe card exposes a small visual contract:

- Particle template/preset picker, optionally backed by a ParticleSystem prefab.
- Amount/burst count, scale and lifetime multiplier.
- Direction, spread and speed range.
- Optional gravity and simulation-space choice constrained to the composition.
- Colour mode: authored template colour, one tint, two-colour gradient, or palette sampling.
- Sorting/layer role and deterministic seed where the backend supports it.

The implementation contract receives the normal Chunk context (origin, direction, palette, scale, layer/sorting, seed) and returns/owns only what it spawned. The wrapper translates this into ParticleSystem configuration and lifecycle. It must support palette complement without pretending Unity’s standard gradient UI is an arbitrary palette system: for true palette selection, emit particles through the wrapper using per-particle colours or a purpose-built palette-aware material path. The final backend design should be proved by a small spike before it is promised.

### Ownership and asset policy

The Generic Particle Burst is one inline Chunk module. A selected reusable ParticleSystem template/preset may be an ordinary authored prefab/asset in its own appropriate library, but it is not a child Chunk asset and never appears as a new Chunk in the Chunk browser. The Chunk module stores its own wrapper-level overrides inline.

This keeps the user promise: a recipe which does not use generic particles gains no generic-particle UI or asset debris.

## Boundary rules

Use these rules when deciding a future capability:

1. If it creates a meaningful visible result at time zero, it can be a result-producing module.
2. If it changes another result and is meaningless alone, it is a modifier with an explicit compatible target.
3. If it relates two or more modules in time/order, it is a composition-level coordinator.
4. If it chooses where/when to fire from game code, it belongs to the caller/emitter—not the recipe editor.
5. If it is a reusable visual asset, it belongs in Pyre, Zoetrope, Launimator or a particle-template library; Chunks references it.
6. If it exists only to make the runtime work, it is internal infrastructure, not a user-facing module or browser asset.
7. A new module must have a one-screen smallest useful case before it earns a place in the catalogue.

## Candidate capabilities: now, later, or outside Chunks

### Include in prototype scope

- Pyre Burst and Pyre Formation.
- Fragment Fracture.
- Palette Splash.
- Generic Particle Burst.
- Debris Scatter as a legacy-compatible producer.
- Trajectory as a modifier.
- Shared Layer Plan and shared Schedule.

### Represent but defer detailed UI

- Animated Debris Scatter.
- Transform Evolution.
- Repeated Emission / Trail.
- Cue/event markers.
- Random/alternative pools within producers.
- Gameplay hit/collision consequence.

### Keep outside the Chunk recipe

- Playing one Pyre with no composition/variation: direct effect reference.
- Pyre authoring, Zoe authoring, Launimator authoring and ParticleSystem template authoring: their own tools.
- Camera shake, post-processing and other global/camera effects: separate future integration, not silently bundled with an impact recipe.
- Character state logic, weapons, input, damage resolution and AI: Zoetrope/game systems.
- Pools, runner internals, source resolution and preview staging: infrastructure.

## Data-model implications for the later prototype-to-production step

A new recipe needs only an inline ordered module list plus optional composition services. Each module needs a stable serialized id, enabled state, display name and typed configuration. Producer modules should publish a typed output handle/category; modifiers target a compatible producer by stable id. The schedule stores module ids, never names or list positions. A layer plan stores module/output assignments only when enabled.

The current IChunkModule contract is a good runtime starting seam but will need a richer discovery/compatibility description for the editor prototype: display name, category, produces/consumes and schedulability. Do not widen runtime context merely to make the editor convenient; keep editor metadata separate if necessary.

## Recommended next task after this investigation

Build the non-production mock editor against mock recipe/module data, with no dependency on the current ChunkSpec layout. It should prove the following cold workflows before production refactoring starts:

1. New recipe → Pyre Formation → select one Pyre → make it fire five times in a line with stagger.
2. Add Trajectory and target that formation.
3. New recipe → Palette Splash → choose Zoe source → configure amount, spread, speed and gravity; no timeline shown.
4. Add Generic Particle Burst as a complementary smoke/dust layer using a mock template and palette mode.
5. Combine Fragment Fracture, several Pyre producers and a layer plan.
6. Add a time offset and confirm exactly one schedule/timeline appears.

Approval means the user can complete all six without seeing irrelevant sections or entering identifiers manually. It is a UX blueprint, not production functionality.


## Dependency correction — Pyre may become a direct Chunks dependency

The earlier documents treated “Pyre references Chunks, therefore Chunks cannot reference Pyre” as a permanent architectural constraint. That is not a desired architecture. It is an accidental current dependency and is superseded by the direction that the Pyre → Chunks reference will be removed. Future Chunks planning may therefore assume a direct Chunks → Pyre reference.

### What the current dependency actually is

Pyre’s asmdef directly references the Chunks assembly. The meaningful coupling is adapter code on the Pyre side:

- PyreChunksDirect makes Pyre implement IChunkEffectSpawner and IChunkAnimation so Chunks can cast an untyped source object and spawn/play it.
- PyreSpawnSource and PyreChunkAnimation are wrapper ScriptableObjects that implement those Chunks interfaces.
- PyreRenderer contains support for the animation adapter.
- Some Chunks comments, fields and picker paths exist only because direct Pyre types were unavailable.

This was introduced to make real Pyre assets selectable in Chunks despite the assembly cycle. It solved a real picker dead end, but the indirection is not a product goal.

### Revised target

After Pyre no longer references Chunks:

- Chunks references Pyre directly.
- Pyre Burst and Pyre Formation store a concrete Pyre reference in their inline per-use configuration, rather than an Object cast to IChunkEffectSpawner.
- Per-use speed, loop, scale, tint and other overrides live inline beside that Pyre reference. They replace wrapper assets as the normal override mechanism.
- Chunks invokes a small explicit Pyre playback/spawn API and continues to use the Pyre pool correctly; it must still never attach components to pooled Pyre instances.
- Pyre-specific frame access for animated debris/fracture can use an explicit Pyre API. The generic IChunkAnimation interface may still remain for non-Pyre providers such as Zoe/Launimator, where it prevents new cross-tool dependencies.
- PyreSpawnSource/PyreChunkAnimation become legacy migration inputs or can be retired once every consumer is migrated. They are not part of the future user workflow.
- IChunkEffectSpawner becomes optional future extension infrastructure, if retained at all, rather than the default Pyre path.

### Effect on the module calculation

The UX conclusion is unchanged: Pyre Burst and Pyre Formation remain result-producing modules; Trajectory remains a target-bound modifier; Layer Plan and Schedule remain shared opt-in coordinators. The change improves their implementation and mock fidelity:

- A Pyre card can use the real Pyre LauAsset picker directly, with no wrapper type, empty wrapper browser or raw-inspector escape hatch.
- A formation card can list one concrete Pyre with its own inline variation controls.
- The module catalogue no longer needs generic “effect spawner” terminology in user-facing copy.
- Fragment Fracture can offer Pyre as one supported visual source through a direct type-specific path while preserving other visual-source providers.
- The Generic Particle Burst wrapper-contract proposal is unaffected. It remains optional and independent of whether any Pyre module is present.

### Dependency-removal migration, before any Chunks refactor

1. Remove Pyre’s Chunks asmdef reference.
2. Move/replace the Pyre-side Chunks adapters with direct Chunks-side Pyre integration.
3. Expose only the small Pyre runtime APIs Chunks genuinely needs: spawn/play, release/lifecycle signal, preview/frame access and relevant authored defaults.
4. Migrate wrapper-backed references to direct Pyre references plus inline override data; retain a one-way compatibility converter until existing assets are verified.
5. Compile Pyre with no Chunks reference, then compile Chunks with Pyre; verify pool reuse and per-use override behaviour.
6. Only then treat the concrete Pyre reference shape as settled input to the mock editor and later data-model work.

This improves—not weakens—the proposed modular design because it removes an implementation-driven abstraction from the primary authoring path while retaining generic extension seams where they are actually useful.

