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
