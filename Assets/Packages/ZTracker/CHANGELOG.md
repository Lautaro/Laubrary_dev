# ZTracker add-on changes

## [Unreleased]

- Independent P2 review fixed canonical instrument projection, inherited sampler preset sections, kit null-duplicate/effective-default migration, repeated-gesture rollback, live refresh slot ownership and Unity JSON null normalization. Added null archive save/reload and adversarial regression checks, exact sample-bank accounting and explicit refusal of structural scalar refreshes and native truncation; original assets and native DSP remain unchanged.

- Added explicit version-1 song and Sampler/Synth authoring data: sparse independent columns, buses/routing, AudioCore chains, automation, sequence mutes, sampler zones/loops/modulation, eight macros and compiled preset parameter sets. Migration retains script identities, original legacy payloads and unsupported commands without rewriting live assets.
- Added conservative main-thread legacy projections and Undo-safe edit reconciliation for the current editor, audition and runtime upload. Unrepresentable features are retained and rejected explicitly; saved-demo scratch migrations and the flat ZTracker Model Check verify serialization and compatibility. Native ABI/DSP behavior remains unchanged; new realtime features are deferred to later phases.

- Replaced the preview-only tracker surface with retained Laubrary ZUI song/order/pattern authoring, keyboard and clipboard editing, track mixing and multi-column editing, Sample/Synth/FM/Kit instruments, presets, envelopes, effects, arpeggios and macros. New songs start empty; asset edits support Undo and scoped saves.
- Held-note blend and oscillator scalar edits now use the playback owner's gate and cached definitions without restarting the voice or uploading samples/curves; structural edits deliberately restart preview. Legacy macro mappings still use main-thread polling, not sample-accurate native modulation.
- Introduced an optional sibling package for tracker song data, Windows x64 playback, and a compact Laubrary preview window using canonical ZUI. Creating a song provides saved synth content; playing remains explicit.
- Sequenced notes now respect channel gain and pan, with channel gain retained across volume commands and slides without replacing instrument gain.
- Added a native ABI check, platform capability result, preallocated rendering buffers, owned stop/reload/device-change cleanup, and silent idle components. This managed host does not preserve Zounds SAP garbage-collection immunity during simultaneous playback.
- Edit-mode preview and gameplay now share a stereo streaming callback, and an optional saved synth-song demo provides explicit Space-to-play interaction. Song/instrument identities and existing serialized assets are retained.
