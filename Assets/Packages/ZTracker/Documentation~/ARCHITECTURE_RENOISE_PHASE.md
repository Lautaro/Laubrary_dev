# ZTracker next phase — architecture (T-0008)

Owner brief (AgentHQ ZTracker board, T-0008, 2026-10-05), condensed: adapt ZTracker's authoring model to Renoise's (tracks, columns, track commands, sample commands...); keep the ZTracker synth instrument (it has no Renoise counterpart — authors compose in Renoise with any VST that sounds similar and the parameter changes they write are mapped onto the synth's parameters); build our own Redux-style sampler instrument with Renoise's sample-playback and multi-sample data model (hosting Redux as a VST is **rejected**); move the native C++ audio plugin onto the same Burst + Scriptable Audio Pipeline solution Zounds uses; give ZTracker the same FX chains as Zounds; share engine code with Zounds where it is smart to.

ZTracker lives in this repository (`D:\UNITY\Laubrary Dev`): package `Assets/Packages/ZTracker`, old native source `Tools/ZTrackerNative`. The standalone `D:\UNITY\ZTracker` project is deprecated and read-only reference.

Must-read inputs (in `D:\UNITY\ZTracker\ZTracker\` unless stated): `HEALTH_CHECK_2026-10-05.md`, `BURST_AUDIO_ASSESSMENT_2026-10-05.md`, `RENOISE_IMPORT_ASSESSMENT_2026-10-05.md`, `RENOISE_INTERCHANGE_ASSESSMENT_2026-10-05.md`, and `D:\UNITY\ZTracker\ZTRACKER_LAUBRARY_INTEGRATION_ASSESSMENT.md`. The Redux hosting addendum is superseded by the owner's rejection of VST hosting.

## 1. Decisions

1. **One tracker generator, all in Burst.** A single long-lived SAP generator per playing song (`GeneratorInstance.IRealtime`, `[BurstCompile]`) owns the sample-counted song clock, the voice pool, all instruments and the whole mixer graph. Tracker voices are NOT one Zounds generator each: per-voice generators would copy PCM per note and lose sample-accurate sequencing. The C++ DLL and the managed streamed-clip host are retired at the end of the phase; until then they stay as the parity reference.
2. **Keep the sample-counted clock.** Render to the next tick boundary, apply the tick, continue; fractional tick lengths carry forward. Never schedule notes from the main thread or from frame updates.
3. **Shared audio core, extracted from Zounds.** New runtime assembly `com.Lautaro-Arino.Laubrary.AudioCore` (namespace `Laubrary.Audio`), references Burst/Collections/Mathematics only, no Zounds or ZTracker types. It receives the Zounds effect kernels (`ZoundEffects` → all 16 effect types), `ModulationMath`, envelope/LFO evaluation math, the effect sizing tables (reverb comb lengths, delay ring sizes, state-size functions), the effect/modifier enums (same numeric values — they are serialized as ints), the audio-thread guard counters and a generic SAP lifetime helper (render ticket, quiet-wait, quit drain) generalised from `SapVoiceRegistry`. Zounds and ZTracker both depend on it; neither depends on the other. Zounds must sound identical after extraction (its compiled-vs-uncompiled and equivalence checks are the gate).
4. **FX chains = Zounds chains, hosted as "chain processors".** AudioCore exposes a chain-only processor: a flat native layout + one state arena + modifier evaluation, processing a stereo buffer in place. ZTracker instantiates one per track/group/send/master device chain and one per instrument FX chain — never one per voice (the 1 MB heavy arena per voice that Zounds uses is unaffordable at tracker polyphony). The authoring type is the same serializable `ZoundEffectChain` data shape (moved to or mirrored in AudioCore data), so the Zounds chain editor UI can be reused.
5. **Song model follows Renoise** (section 2). Note columns are independent; effect columns are real; track / column / global command scopes are distinct; groups and send tracks are real buses; master is a track.
6. **Two instrument families.** **Sampler** (new, Renoise/Redux data model, section 3) replaces the old Sample and Kit types. **Synth** keeps the ZTracker synth (subtractive/blend oscillators, unison) and FM as an engine mode of it. Both expose 8 instrument macros (Renoise has 8) evaluated on the audio thread; the synth's macros/parameter map are the landing point for parameter changes composed in Renoise against a stand-in VST.
7. **Migration, not breakage.** Existing `ZTrackerSong` / `ZTrackerInstrument` assets upgrade through a versioned migration that keeps script GUIDs. Old Sample instruments become single-zone Sampler instruments; Kits become Samplers with one zone per mapped note (fixed pitch = zone with key tracking off); Sample-B blend has no Renoise counterpart and is carried as a ZTracker-only extension on the sampler zone (export to Renoise drops it with a warning). Old shared-instrument cells fan out to per-column instrument/volume.
8. **Command queue is the only main→audio channel.** Editor and game edits become ordered commands or prepared-data swaps at a block boundary; the audio thread never reads Unity objects, never allocates, never blocks. Old data is retired only after the render ticket shows the audio thread stopped reading it. No more "stop, edit, restart after 80 ms".

## 2. Song model (Renoise-aligned)

- **Song**: BPM, lines per beat, ticks per line (all three separate, as now), track list, pattern pool, sequence (order list with per-slot track mutes), instrument list, groove off (not supported this phase).
- **Track**: kind = Sequencer | Group | Send | Master. Name, colour, visible note columns (1–12), visible effect columns (0–8), per-column visibility of volume/pan/delay/fx sub-columns, prefader volume/pan/width, postfader volume/pan, mute (trigger mute vs output mute kept distinct), solo, output routing (parent group by default, or explicit), device chain (a Zounds-style FX chain; Send devices route to Send tracks). Groups nest; their device chain processes the sum of children.
- **Pattern**: number of lines (≤ 512, Renoise's limit), one `PatternTrack` per track holding lines; each line has N **note-column cells** {note (0–119, OFF, empty), instrument (or empty), volume (0–0x80 or a column command, or empty), panning (0–0x80 or command, or empty), delay (0–0xFF, 256 per line, or empty), sample-fx (command+value, or empty)} and M **effect-column cells** {command (2 chars), value (00–FF)}. Empty is always distinct from zero. Storage: sparse per-track line arrays (Renoise is sparse); native compile flattens.
- **Command scopes**: column commands (volume/pan/sample-fx sub-columns, and effect-column commands that address a note column) act on that column's voice; track commands (effect columns) act on all voices of the track or the track's mixer; global commands (tempo, LPB, TPL, pattern break, jump, delay line) act on the song — they live in effect columns but are evaluated once per line, not per channel. Command letters and value semantics follow the Renoise effect-command reference (`https://tutorials.renoise.com/wiki/Effect_Commands`); each implemented command gets a written spec (units, target, tick-0 behaviour, memory/zero-repeat, reset) in `Documentation~/COMMANDS.md`, and unsupported commands are preserved in data and flagged, never silently reinterpreted.
- **Automation**: per-track automation lanes on device/mixer/instrument-macro parameters (points with linear/step interpolation), evaluated at control rate on the audio thread. This, plus effect-column device-parameter commands, is how parameter moves composed in Renoise land on the ZTracker synth (via the instrument's parameter map — section 4).
- **Game events**: ZTracker's event tracks and beat ticks stay as ZTracker-only features (an Event track kind or event sub-column); they are not part of the Renoise profile.
- **Capacities**: no fixed `cells[256][32]` blocks. Voices are a pool (default 128, configurable); tracks/columns are bounded by authoring limits (e.g. 64 sequencer tracks × 12 columns) not by native channel count. Native song data is compiled into flat NativeArrays sized to the song.

## 3. Sampler instrument (Redux-style)

Data model follows Renoise's instrument / sampler:

- **Instrument**: name, samples[], keyzone mapping, modulation sets[], FX chains[] (Zounds chains), 8 macros (name, value, mappings to modulation/FX/sample parameters with min/max/curve), global properties (volume, transpose, fine tune, NNA = cut/note-off/continue, scale/quantise not required), phrases **not** in scope.
- **Sample**: PCM (shared, immutable, one copy per sample regardless of voice count), name, volume, panning, transpose, fine tune, base note, loop mode (off/forward/backward/ping-pong), loop start/end, loop release (exit loop on note-off), beat-sync **out of scope**, interpolation (linear/cubic), oneshot, new-note action, autoseek not required, modulation set index, FX chain index, mute group (for hi-hat choke).
- **Keyzone**: per sample note range and velocity range (layers allowed — overlapping zones play simultaneously), base note, key tracking on/off (off = fixed pitch, which is how kits are expressed).
- **Modulation set**: targets volume, panning, pitch, cutoff, resonance, drive (Renoise's set), each with a small device list: AHDSR envelope, multi-point envelope (with sustain and loop), LFO, velocity/keytracking, fader; per-set filter type. Evaluated per voice at control rate using AudioCore modulation math.
- **Sample commands** (Renoise sample-fx sub-column / effect-column commands that address samples): sample offset (0S), reverse (0B), retrigger (0R), note cut/off at tick (0C / 0O style), glide (0G), slide up/down (0U/0D), vibrato (0V), tremolo (0T), arpeggio (0A), volume slide (0I/0O), pan slide (0J), and the delay sub-column — each spec'd in `COMMANDS.md`.

## 4. Synth instrument

Keeps today's synth sound engine (waves A/B, blend modes Mix/Ring/Sync/PM, pulse width, ratio, unison, vibrato, arpeggio, multipoint parameter envelopes) and FM (4 operators; the editor must offer exactly the algorithms the engine implements — today 8 shown vs 6 implemented is a bug). Ported to Burst, with the known defects fixed rather than preserved: wave enum mismatch between editor and engine, detune envelope never evaluated, envelope loop mode mismatch, portamento never active, kit/filter state leaking between voices, `rand()` noise. Gains: per-instrument FX chain (Zounds), modulation sets like the sampler where they apply, and 8 audio-thread macros replacing main-thread `PumpMacros`. **Parameter map**: each synth instrument carries a table "external parameter id → synth parameter (min, max, curve)", so automation / device-parameter commands authored against a stand-in VST in Renoise are re-targeted on import onto synth parameters. Presets remain an authoring convenience but stop costing native instrument slots (resolved to parameter sets at compile time).

## 5. Engine shape (Burst)

- `TrackerRealtime : GeneratorInstance.IRealtime` — owns: compiled song (flat NativeArrays), sample pool (shared immutable PCM, refcounted by render ticket), voice pool (sampler voices and synth voices as tagged structs, no managed refs), per-track mixer state (buffers, prefader/postfader gain/pan ramps, chain-processor state arenas from AudioCore), clock state, command pipe reader, event ring writer.
- Per block: drain commands → loop { render all active voices into their track buffers up to the next tick or block end; process mixer graph bottom-up (tracks → groups → sends → master), each through its chain processor; advance tick; on tick run line/tick commands } → write output.
- Events out: bounded SPSC ring with overflow counter visible to the main thread; event carries sample position.
- Lifetime: render ticket, quiet wait, quit drain — via the AudioCore lifetime helper; one registry for ZTracker generators.
- Audition/preview (editor note entry, instrument editor piano) goes through the same generator, not a separate path.
- Verification hooks: `SapVoiceRenderJob`-style offline render job of the same realtime struct for deterministic golden comparisons; audio-thread-guard counters; compiled-witness check that Burst actually compiled it.

## 6. Phases (each a board task; workers start from this doc)

| # | Task | Depends on | Notes |
|---|---|---|---|
| P0 | Golden reference renders + event traces from the current C++ engine (sample, synth, unison, FM, kit, loops, envelopes, commands, presets, blends, voice stealing) with approved-defect list | — | Python/ctypes against the DLL, no Unity editor needed. Gate 1 of the Burst assessment. |
| P1 | Extract `Laubrary.AudioCore` from Zounds | — | Owns the Unity editor while running. Zounds must render identically (existing compiled/uncompiled + equivalence + allocation checks re-run). Preserve unrelated uncommitted Zounds work. |
| P2 | New Renoise-aligned song model + Sampler/Synth instrument data + versioned migration of existing assets | P1 for FX-chain data type | Data + migration + model tests only; no engine, minimal editor changes to keep compiling. |
| P3 | Burst tracker engine core: generator, clock, command/event pipes, voice pool, sampler voices (keyzones, loops, modulation sets), mixer graph with chain processors, GC-isolation proof in a player | P1, P2 | The heart of the phase. |
| P4 | Synth + FM port to Burst, audio-thread macros, parameter map | P3 | Parity vs P0 goldens with numeric tolerance; listed defects fixed. |
| P5 | Command set: Renoise profile (column/track/global), delay column, sample commands, automation lanes; `COMMANDS.md` | P3 | |
| P6 | Editor: pattern editor for the new model, track/mixer + device chains (reuse Zounds chain editor), sampler editor (zones/loops/modsets), synth editor update | P2–P5 | UI Guide gate + Handover Walk. |
| P7 | Retire C++ DLL + managed host; parity, GC-in-player, lifecycle, quit-while-playing; docs/CHANGELOG | all | Final PM verification. |

Renoise file import/export (XRNS song placeholder import, XRNI sampler interchange, clipboard) is a natural follow-on once P2/P3 exist; the research tasks T-0006/T-0007 define it. Not in this phase unless the owner adds it.

## 7. Rules for every worker

- Work only in `D:\UNITY\Laubrary Dev`; never edit `D:\UNITY\ZTracker\ZTracker`.
- Read `D:\Unity\UNITY_DEV_GUIDE.md`, this repo's `CLAUDE.md`, and this document first. UI work additionally requires `D:\AgentGuide\ui-rules.md` and the Laubrary ZUI layout rules.
- One Unity editor, one owner at a time. Do not run Unity Test Runner.
- The repo has unrelated staged/unstaged Zounds work: never stage it, commit only your own paths (`git commit -- <paths>`), never reset/stash/checkout others' files. Work on branch `x/zounds-sap` unless your task says otherwise; no push.
- Audio thread: no allocation, no locks, no managed objects, no Unity API. Prove Burst compiled (witness check), prove zero managed blocks (guard counter).
- Report verified-by-probe / verified-by-eye / not-verified separately. Never claim GC-safe, parity or ready without the measurement.
