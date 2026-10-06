# Optional Laubrary ZTracker

ZTracker is an optional sibling package requiring Laubrary 0.9.0 and Unity 6000.3. Install the core package first, then this package through Unity Package Manager's local package option. Core Laubrary has no tracker dependency. Preview and gameplay use the same clipless Burst/SAP renderer; no C++ plugin or managed audio callback is shipped. Installed but idle creates no audio graph, and playback is always explicit. Windows x64 is the verified player target; other targets require their own verification.

## Author a song

Open **Laubrary > ZTracker**. **New song** saves an empty song under the host project's Assets/ZTracker folder. **New instrument** creates a saved synth and adds it to the song. Click a pattern cell and enter notes through the keyboard map or the cell editor. **Play**, **From cursor** and instrument audition explicitly start audio. Pattern, Mixer, Automation, Instrument and Song panes share the retained authoring model. **Save** flushes only the selected song and its dirty linked instruments. Data edits support Undo/Redo, and reorderable lists use drag grips.

Sampler zones use **Active** to enable playback. **Second sample** enables the paired-source controls; while disabled it occupies one toggle rather than an empty group. Mixer devices wrap into columns at wider sizes and retain their controls in a single column at narrower sizes. Single-parameter devices keep the parameter in their header.

Compatible note, tuning, macro, automation and device-parameter edits publish prepared snapshots at an audio-block boundary, preserving transport and unrelated game overrides. Structural routing/voice-layout changes and primary or paired PCM replacements display a pending Stop/Play state; the current song keeps playing until an explicit restart. Stop, closing an owned preview, editor assembly reload and leaving playback release owned graphs after the render ticket is quiet. An audio-output configuration change stops safely and requires explicit Play; it does not silently recreate playback.

## Use in a game

Add `Laubrary.ZTracker.ZTrackerPlayer`, assign `song`, explicitly call `Play()` and inspect the boolean result and `LastError`. Subscribe to `EventReceived` for bounded engine events delivered on the main thread. Enabling the component, polling status and Update do not start playback. Another tracker ownership request reports an error without replacing someone else's playback.

The lower-level facade is `ZTrackerPlayback.TryPlay(song, out playback, out error, order, row)` or `TryAudition(instrument, midiNote, out playback, out error, preset)`. `TryGetPosition` reads the renderer's published position. All authoring, snapshot preparation, event delivery and control submission happen on the main thread; render sees only persistent native state and compiled function pointers. Do not upload Unity assets or execute managed work from an audio callback.

Import the optional **ZTracker Demo** sample, open its scene and enter Play mode. **Space** explicitly starts/stops its saved synth song. The sample's keyboard dependency remains outside production tracker assemblies.

## Existing assets and compatibility

Song/instrument class identities, data assembly name `ZTracker`, script GUIDs and serialized legacy fields remain intact. Copy authored assets with their .meta files and referenced audio into the consuming project's Assets area. Do not install the old standalone tracker assemblies alongside this package. Explicit playback prepares detached migrations; it does not rewrite the authored legacy asset. Unsupported legacy commands and unrepresentable structures remain preserved and are diagnosed rather than guessed. Legacy direct native upload/interop entry points have been retired; callers must migrate to the public player or prepared engine API.

The replacement is not numerically identical to every historical C++ sound. All 122 frozen reference cases are accounted for: 76 numeric passes, seven verified corrections, 12 DSP characterization cases, 23 modern model-policy cases, three statistical noise exclusions and one unrepresentable dual-loop exclusion. Full numerical parity is false. Event comparisons and deterministic numeric cases retain the original tolerances. See [P7 verification](Documentation~/P7_VERIFICATION.md) for exact scope, reproduction and limits.

## Verification and removal

Normal players contain the data, engine and playback assemblies. Package-owned verification assemblies require `ZTRACKER_PROOF`; editor checks are separate. Forced-GC, public command and lifecycle proofs use real compiled rendering, not a substituted silent callback. This does not certify physical device switching, listening quality, speaker latency or other player platforms.

Remove explicit tracker components/references from game code and scenes before removing this package. Authored songs remain in Assets and require reinstalling the add-on to edit or play. No native DLL unload or project-wide audio settings rewrite is required. The old engine is retained only as a non-imported verification oracle under the development host's `Tools/ZTrackerNative/retired`; it is not a distributable playback backend.
