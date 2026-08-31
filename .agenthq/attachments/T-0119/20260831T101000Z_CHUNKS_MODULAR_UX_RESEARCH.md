# Chunks — modular asset model and low-cognitive-load UX research

## Decision in one sentence

Chunks should stop being a universal debris-burst asset with optional feature sections. It should become a small composition recipe whose only job is to combine selected effect capabilities; each capability owns its data and its authoring surface, and timing becomes visible only when the recipe actually needs coordination.

## Evidence from the current implementation

The current ChunkSpec is a single serialized object that still owns the original debris system's emission, physics, life/look, floor, sampled-sprite, tint/modifier, hit, trail and animation fields, while also owning the newer particle splash, Pyre spawn, Pyre movement, fragment slicer, formation, blast group, layer and timeline modules. The editor consequently builds the legacy debris sections and the newer module sections in one window. The earlier design’s “only show enabled modules” rule has reduced the height of individual off sections, but it has not removed this mixed mental model: a user still has to understand the full asset's possible capabilities to decide which section to enable.

The browser currently finds seven persisted ChunkSpecs: six are named demo/example assets (ArenaDebris, Floating Disc Blowup, SampledDebris, Sparks, WallDebris, Debris) and one is an explicit probe (Assets/_T0075_Probe/ProbeChunk). This is not runtime-generated sub-asset leakage, but it is still a product failure: the browser makes test/probe and demonstration recipes indistinguishable from a real authoring library.

The current timeline is technically sound as one shared multi-lane track: it has one lane per enabled module and a permanent event lane. It should remain the only timeline controller if timing is needed. The problem is its status as a universal “structural backbone”; an immediate, one-shot palette spray has no useful timeline interaction to show.

The current ParticleSplashModule is already strong evidence for the proposed boundary. It has an independent useful outcome: sample a sprite's opaque footprint and colours and emit small pixel particles. It can run with no fragment, pyre or timeline. This is exactly the kind of capability that should be selected and authored as a module, rather than treated as a section in a giant Chunk form.

The Shaper branch is useful architectural inspiration, not yet a finished editor-UX reference. Its ShaperNode owns optional, local capabilities (for example one fill and at most one border) and nests the same node mechanism rather than adding parallel, global lists. Its document/layer structure preserves ownership and evaluation contracts. Shaper currently has runtime contracts and audits, but no full Shaper authoring window was found in the branch, so Chunks must not copy an imagined Shaper UI.

## Proposed product boundary

Do not use Chunks for “play one Pyre.” That should be a direct Pyre/Zoetrope effect reference: one picker, an optional trigger, and no Chunk asset.

Use a Chunk recipe only when the author is composing or varying an effect:

- Spawn one selected Pyre several times using a formation/stagger rule.
- Spawn a Pyre formation, then give the spawned instances a trajectory.
- Play several Pyres/fragment/splash capabilities in a timed or layered composition.
- Sample a Zoe/sprite to make a particle splash or fracture it.
- Coordinate named game events with the same composition.

The smallest useful Chunk is therefore one selected capability with the fields that make that capability useful. A “Pyre formation” recipe should read as one list/card item: selected Pyre asset, count, pattern, stagger and perhaps a layer slot. It must not expose floor bounce, sampled debris curves, a layer stack, events or a timeline unless that recipe adds those capabilities.

## Target asset model

Create a new ChunkRecipe (or retain ChunkSpec only if migration compatibility makes renaming expensive) with only shared identity and composition data:

- LauAsset identity, thumbnail/preview contract and tags.
- An ordered inline [SerializeReference] List<ChunkModule>; each module has a stable serialized id, enabled state and display name.
- A single optional ChunkSchedule/timeline, keyed by stable module ids rather than visible names or list indices.
- Shared composition context only where it truly is shared: source/anchor supplied by the caller, sort-layer translation, and preview metadata. Do not put per-effect dials here.

Each module owns its complete parameters inline. A module is not a new .asset, not a LauAsset, and not a browser item. Its lists (Pyre alternatives, formation settings, modifier stacks) are serialized inside that module. This is the guard against the “3–10 debris assets per Chunk” future: user-authored reusable recipes are browser assets; implementation details and component settings are not.

Recommended initial module vocabulary:

| User-facing capability | Module boundary | Typical compact UI |
|---|---|---|
| Repeated selected Pyre | Pyre formation | Pyre picker; count; pattern; stagger; optional seed/layer |
| Move spawned Pyres | Pyre trajectory | Target module picker; direction/arc; speed; gravity/drag |
| Fracture Zoe/sprite | Fragment fracture | Source picker; pieces; cut style; motion preset |
| Palette particle shower | Palette splash | Source picker; amount; spread; speed; gravity; colour sampling |
| Coordinate separate capabilities | Schedule | One shared track only when timings/events differ |
| Ordering only when needed | Layer plan | Compact ordered slot list, added by modules that need it |

“Pyre movement” should not be a second giant global panel. It should attach to a selected Pyre-forming module (or be a module with an explicit target picker). The model must make invalid combinations impossible or visibly unavailable.

## Target editor workflow

The asset window should begin with a compact composition list, not twenty section headers.

1. A new recipe opens with an intentional empty state: “Add capability” presents direct choices such as Pyre formation, Palette splash and Fragment fracture. It is not a blank form.
2. Adding a capability creates one compact row/card with enable, name, a terse outcome summary and remove. The card’s own useful controls appear immediately; it can fold after the user has tuned it.
3. Selecting a card reveals only its authoring UI. Repeated items use the established Pyre-style compact list + selected detail pattern, rather than a full panel per item.
4. “Add timing…” appears only after two independently schedulable capabilities exist, or after the user asks to offset/repeat/event-drive one. Before that, every capability fires at time zero and there is no timeline noise.
5. Once scheduling exists, show exactly one bottom-docked timeline. Every timing-capable module contributes a lane; the timeline stores offsets and events only. No module may invent a second local timeline.
6. Preview should answer the actual composition question. It should be reachable from the recipe and show the full effect, not separate module previews that lose their relationship.

The “show only used sections” idea is correct but should be the consequence of the data model, not a view filter over all possible sections. A capability that has not been added literally has no module/card or serialized state to show.

## Browser and ownership policy

- Browser default: user-authored recipe assets only.
- Demo assets: move them under a clearly labelled Samples/Demos scope and make browser scope/filter policy explicit; they should not be the default working library.
- Probe/test assets: move out of Assets, create them transiently, or place them in a test-only path that the browser explicitly excludes. Assets/_T0075_Probe/ProbeChunk must not be discoverable as a normal recipe.
- Runtime-generated sprites/textures and preview helpers: never persist them as assets. The existing Mirage preview pattern—ScriptableObject.CreateInstance without AssetDatabase.CreateAsset—is the right ownership model for temporary preview state.
- Do not turn module instances, variations, generated fragments or timing tracks into LauAssets just to get a picker. Pickers are an interface, not an asset-design reason.

## ParticleSystem assessment

Do not make Unity's ParticleSystem the default implementation for Chunks palette splash. The current splash has specific pixel-art requirements: it samples a sprite's opaque footprint and palette, uses cached small pixel sprites, participates in per-particle ordering/layering, and is driven by the composition runner without attaching components to pooled Pyre instances. A raw ParticleSystem exposes a huge unrelated parameter surface and makes the resulting look, sorting and colour sampling harder to reason about.

A Unity ParticleSystem wrapper could later be a separate, deliberately constrained module for generic smoke, glow or soft particles. It should expose a small authored contract (source/preset, emission, motion, colour/layer) rather than embedding Unity's full inspector. It must not replace the pixel-splash module or become a back door to another enormous editor.

## Migration plan

### Phase 0 — stop feature accretion and inventory

Freeze new Chunk sections. Inventory every current ChunkSpec field, identify live consumers, classify it as shared recipe context, a legacy debris capability, a new module capability, or dead/test-only. Back up/move the probe and demo browser clutter without deleting user-authored work blindly.

### Phase 1 — agree the capability catalogue

Write concrete one-screen acceptance examples for: Pyre formation; Pyre formation plus trajectory; palette splash; fracture plus layered Pyres; a timeline-driven composition. For each, name the minimum fields and the module that owns them. Reject any proposed field that cannot name its owner.

### Phase 2 — establish the new seam

Implement the inline module list, stable module ids, module execution contract and one schedule object. Keep the existing runtime bridge constraint: Chunks must not reference Pyre directly; Pyre interaction stays behind the existing spawn/effect-spawner contract. Add compatibility adapters that can execute old ChunkSpecs unchanged.

### Phase 3 — rebuild the editor around composition

Build the capability picker, composition list, selected-card detail panel, conditional layer plan and conditional one-timeline view. Run the UI Guide compliance pass: explicit control widths, compact related fields, pickers for references, no raw text identifiers, stable workspace during edits, Undo per mutation and no redundant one-field boxes.

### Phase 4 — migrate deliberately

Provide an in-place upgrader or “convert legacy spec” flow. Map one legacy debris asset to a Legacy Debris module rather than forcing every old field into the new root. Preserve old assets until the converted recipe is verified in the demo. Do not silently rewrite user assets.

### Phase 5 — browser hygiene and samples

Relocate/exclude probes, scope samples, and give sample recipes descriptive names and a lightweight “Sample” affordance/filter. Verify the default Chunk browser contains no accidental test debris.

### Phase 6 — acceptance and handover

Perform the handover walk twice from an empty project/library:
- Create and preview one repeated Pyre recipe without a timeline.
- Create a palette splash without Pyre/fragments.
- Add a second capability, then add timing and verify the one shared timeline.
- Create fracture plus layered Pyres and verify a meaningful visual result.
Report separately what was verified by probe, by eye and not verified.

## Explicit design decisions requested

1. Adopt “Chunk = composition recipe,” not “Chunk = universal debris burst.”
2. Keep one shared timeline but make it opt-in/contextual.
3. Keep module state inline; browser assets are recipes only.
4. Treat direct single-Pyre playback as outside Chunks.
5. Keep custom pixel splash; consider a constrained Unity ParticleSystem module later, not a wrapper as the core design.
6. Use Shaper’s ownership discipline and compositional tree mindset, while not claiming its currently runtime-focused branch proves a finished editor workflow.


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

