# Drum kit verification — 2026-10-12

## Scope

Drum kits assign imported audio or a referenced synth to inclusive key ranges. Sample ranges optionally track pitch; synth ranges always use the configured pitch and enter release after a configurable gate. Playback preparation expands sources into private instrument slots without changing authored song slots. External samples are copied into `Assets/ZTracker/DrumSamples/`, with the original file URI stored in both the mapping and the imported clip's metadata. Existing project clips are copied too, so their original import settings remain untouched.

## Verified by probe

- Unity 6000.3.10f1 compiled the final source with no errors.
- `TrackerDrumCheck.Execute()` passed 7/7 groups: tag aliases; range/source validation; overlapping one-shot sample hits and provenance; optional sample transposition; fixed-pitch synth ranges and automatic release; synth refresh identity; FM sources.
- Existing `TrackerEngineCheck` passed 33/33 and `TrackerP4Check` passed 27/27. Burst rendering retained zero managed allocations.
- The editor-authored mixed kit survived save and script reload with three mappings, imported sample paths, matching mapping/importer source URIs and the drum-track flag intact.
- The attached pattern keyboard handler entered kick, snare and synth percussion notes. Their icons were subsequently inspected in the grid. The sample-preview AudioSource reported `isPlaying=true` in edit mode.
- `git diff --check` passed.

## Verified by eye

The first empty-song walk created a kit, browsed the external fixture folder and imported an assigned kick. It exposed a swallowed browser drag and cramped pad labels; those were corrected. After closing and reopening Unity, the second empty-song walk created a fresh kit, dragged a kick onto C3, selected D3–E3 and dropped a snare across that range, resized it to F#3 and undid that resize. A newly created synth was then dragged onto A3. The mixer switched the track to Drums, and the pattern displayed the three source icons.

The UI Guide compliance pass covered the browser, key map, sound details, mixer selector and pattern icons at 1100×720 and 760×440 logical window sizes. The map stays visible while the details scroll; pads wrap; bounded values have compact controls; assignments expose source and category; rename/tag edits refresh their dependent labels without replacing the focused input. One range name escaped its rectangle at minimum width; ellipsis clipping was added and visually rechecked.

## Not verified

Physical keyboard-to-speaker listening, MIDI hardware, an OS Explorer drag, all supported external decoder formats, very large drum-folder performance and standalone player/AOT builds were not exercised. The native automation target did not reliably deliver keys to Unity's floating window, so keyboard entry was checked through the attached handler rather than claimed as a successful physical-input test. MIDI support is deliberately deferred. Structural source changes during song playback retain the existing Stop/Play refresh policy.

## Reproduction and development note

Run the proof entry points directly in the correct running editor through Unity CLI `command eval_file`, with `ZTRACKER_PROOF` enabled as for the existing checks; do not open the Test Runner. After adding the native drum-gate field, this editor initially reused stale Burst code: managed rendering released the voice but Burst read adjacent envelope data. Restarting the owned editor resolved the discrepancy; the final seven checks passed again after the subsequent editor-only edits. No algorithm workaround or cache-deletion operation was added.

Verification fixtures, imported proof clips and scratch scripts are disposable and are removed after validation. No demo builders, new menu items, native helpers or test servers are shipped.
