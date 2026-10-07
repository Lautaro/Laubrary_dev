# Optional Laubrary ZTracker

ZTracker is an optional sibling package requiring Laubrary 0.9.0 and Unity 6000.3. Install the core package first, then this package through Unity Package Manager's local package option. Core Laubrary has no tracker dependency. Preview and gameplay use the same clipless Burst/SAP renderer; no C++ plugin or managed audio callback is shipped. Installed but idle creates no audio graph, and playback is always explicit. Windows x64 is the verified player target; other targets require their own verification.

## Author a song

Open **Laubrary > ZTracker**. **New song** saves an empty song under the host project's Assets/ZTracker folder. **New instrument** creates a saved synth and adds it to the song. Click a pattern cell and enter notes through the keyboard map or the cell editor. **Play**, **From cursor** and instrument audition explicitly start audio. Pattern, Mixer, Automation, Instrument and Song panes share the retained authoring model. **Save** flushes only the selected song and its dirty linked instruments. Data edits support Undo/Redo, and reorderable lists use drag grips.

Sampler zones use **Active** to enable playback. **Second sample** enables the paired-source controls; while disabled it occupies one toggle rather than an empty group. Mixer devices wrap into columns at wider sizes and retain their controls in a single column at narrower sizes. Single-parameter devices keep the parameter in their header.

Instrument controls use musical units and compact named sections. The amplifier and other fixed envelopes have a drawn ADSR (AHDSR where Hold exists): drag a named handle to change only that stage, or edit its millisecond/percentage value. Short envelope times have extra space on the graph. Volume and pan read as percentages, pitch offsets as cents, and cutoff as Hz on a logarithmic track. These are display conversions; opening an existing sound does not clamp its saved values.

Right-click a supported parameter to switch between **Static** and **Envelope**. Legacy empty envelopes display the static value without rewriting the instrument. An authored envelope occupies one compact thumbnail row; click it to reveal the curve and its duration, loop and sustain controls. The curve starts again for every new note and runs independently for overlapping voices. Duration sets its time in seconds; held loops leave on note-off. Ordinary parameter and point edits follow the existing live-update rules; changes to voice/modulation structure require Stop/Play.

Sampler controls distinguish **All samples**, **This sample** and **This sample playback**. **New samples** sets the new-note default for future sample additions. Loop, region and slice positions display milliseconds while retaining source frames in storage. An unassigned mute group reads **None**. Effects use one searchable **Add effect…** picker, and modifier headers stay on a single row at the minimum window width.

Arpeggio belongs to the pattern. Enter **0A** in a track effect identifier and **37** in its hexadecimal argument to cycle the played note, +3 and +7 semitones at successive tracker ticks. This is the compact **A37** notation used in the command documentation. It works for sampler, synth and FM notes, and ends when the row no longer contains the command. No instrument arpeggiates by default. Old instrument arpeggio settings remain archived for recovery and no longer affect playback.

Compatible note, tuning, macro, automation and device-parameter edits publish prepared snapshots at an audio-block boundary, preserving transport and unrelated game overrides. Structural routing/voice-layout changes and primary or paired PCM replacements display a pending Stop/Play state; the current song keeps playing until an explicit restart. Stop, closing an owned preview, editor assembly reload and leaving playback release owned graphs after the render ticket is quiet. An audio-output configuration change stops safely and requires explicit Play; it does not silently recreate playback.

## Use in a game

Add `Laubrary.ZTracker.ZTrackerPlayer`, assign `song`, explicitly call `Play()` and inspect the boolean result and `LastError`. Subscribe to `EventReceived` for bounded engine events delivered on the main thread. Enabling the component, polling status and Update do not start playback. Another tracker ownership request reports an error without replacing someone else's playback.

The lower-level facade is `ZTrackerPlayback.TryPlay(song, out playback, out error, order, row)` or `TryAudition(instrument, midiNote, out playback, out error, preset)`. `TryGetPosition` reads the renderer's published position. All authoring, snapshot preparation, event delivery and control submission happen on the main thread; render sees only persistent native state and compiled function pointers. Do not upload Unity assets or execute managed work from an audio callback.

Import the optional **ZTracker Demo** sample, open its scene and enter Play mode. **Space** explicitly starts/stops its saved synth song. The sample's keyboard dependency remains outside production tracker assemblies.

### Song position for gameplay

`player.Clock` (a `ZTrackerSongClock`, null while nothing plays) tells game code where the song is as the player hears it. `Clock.TryGetHeard(out var p)` gives the order, row, pattern id, BPM, LPB (lines per beat), TPL (ticks per line), `LineInBeat` (0 is the first row of a beat), the tick, `RowFraction`, and the time the row has been current and has left, in samples, milliseconds and ticks. `TryGetHeardAt(time)` answers for a past moment on the `Time.realtimeSinceStartupAsDouble` timeline, so a button press can be judged at the instant it happened. `TryGetRendered` ignores the output delay. The output delay is Unity's DSP buffer estimate plus `ExtraLatencySeconds`; speakers, TVs and wireless headphones add delay Unity cannot see, so offer players a setting for it. `Clock.Song` is the detached song data that is playing (tracks, patterns, order list, instruments), and `TryGetLine(order, row, track, out line)` reads one row's cells without allocating. Reading the clock consumes nothing and does not affect `EventReceived`.

### Rhythm accents, hit judging and the timeline

`ZTrackerAccentSource` names what a player can hit: `Beats` (the first row of every beat) or `Notes(track)` (every note start on one track). `ZTrackerAccents.Judge(clock, time, source, windowRows, scratch)` judges a moment, such as an input event's time, against the nearest accent, including the neighbouring patterns. It returns whether it hit, the offset in rows and milliseconds (negative is early), and which order slot and row the accent was. `TryGetAccentNote` reads the note at that accent and `NextEventRow` finds where it ends. `ZTrackerTimeline.Draw(rect, clock, source, windowRows)` draws the pattern the player hears (up to 64 rows; longer patterns page by 64) from OnGUI with ZUI's runtime toolkit: row ticks with beats numbered, each accent's window shaded, accents marked, the heard position as a cursor, and an optional last-press mark.

`ZTrackerPlayback.SetTrackGated(track, true)` silences a track's own pattern notes while `PlayNote(track, instrument, note, velocity)` still sounds on it, and `ReleaseNote(track)` releases that note. Together they let a game keep a melody silent and play its notes only when the player hits them. Trigger-muting a track no longer blocks auditions or game-played notes on it.

## Existing assets and compatibility

Song/instrument class identities, data assembly name `ZTracker`, script GUIDs and serialized legacy fields remain intact. Copy authored assets with their .meta files and referenced audio into the consuming project's Assets area. Do not install the old standalone tracker assemblies alongside this package. Explicit playback prepares detached migrations; it does not rewrite the authored legacy asset. Unsupported legacy commands and unrepresentable structures remain preserved and are diagnosed rather than guessed. Legacy direct native upload/interop entry points have been retired; callers must migrate to the public player or prepared engine API.

The replacement is not numerically identical to every historical C++ sound. All 122 frozen reference cases are accounted for: 76 numeric passes, seven verified corrections, 12 DSP characterization cases, 23 modern model-policy cases, three statistical noise exclusions and one unrepresentable dual-loop exclusion. Full numerical parity is false. Event comparisons and deterministic numeric cases retain the original tolerances. See [P7 verification](Documentation~/P7_VERIFICATION.md) for exact scope, reproduction and limits.

## Verification and removal

Normal players contain the data, engine and playback assemblies. Package-owned verification assemblies require `ZTRACKER_PROOF`; editor checks are separate. Forced-GC, public command and lifecycle proofs use real compiled rendering, not a substituted silent callback. This does not certify physical device switching, listening quality, speaker latency or other player platforms.

Remove explicit tracker components/references from game code and scenes before removing this package. Authored songs remain in Assets and require reinstalling the add-on to edit or play. No native DLL unload or project-wide audio settings rewrite is required. The old engine is retained only as a non-imported verification oracle under the development host's `Tools/ZTrackerNative/retired`; it is not a distributable playback backend.
