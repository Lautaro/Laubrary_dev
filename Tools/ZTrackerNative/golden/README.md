# Current native reference corpus

This is the current installed Windows x64 C++ engine reference for AgentHQ ZTracker T-0009, architecture phase P0. It contains 122 short cases and 16441272 audio bytes, below the 50 MB audio budget. The generator uses Python's standard library and ctypes, without Unity, an audio device, NumPy, engine changes or imports of the executable regression script. It records actual native output without normalizing, clamping, resampling or shifting events. This is not yet verified against Burst.

Run from `Tools/ZTrackerNative` using 64-bit Python on Windows:

```powershell
python generate_golden.py
python generate_golden.py --output .golden-rerun
python compare_golden.py .golden-rerun
python test_golden.py
python verify.py
```

Both complete generation invocations must run as separate foreground Python processes with the identical installed DLL. The checked-in manifest has no timestamp or machine-specific absolute path. Regeneration overwrites named corpus artifacts, so preserve any edited reference before invoking it. Use `--output` for independent reference replay. `.golden-rerun`, `.golden-tests`, `logs`, the build directory and Python caches are ignored; audio in `golden` is deliberately tracked.

The manifest captures generator/specification fingerprints, all current native source and managed ABI declaration fingerprints, installed DLL SHA256/build identity, ABI version/struct sizes/offsets, sample rate, frame and render-buffer counts, nominal/native clock values, source PCM fingerprints and formulas, ordered native calls (arrays hashed), audio/event/trace hashes, signal peak/mean/stereo RMS and behavioral assertions. The fixed seed is zero for the procedural fixture specification, which uses periodic mathematical functions rather than a PRNG. Native noise cannot accept that seed. Cells and curve arrays can be reconstructed from the case specification; exact input-array bytes are fingerprinted in the ordered calls.

## Data format and timeline

Each `CASE.f32` is headerless little-endian IEEE float32 stereo, interleaved left/right: sample offset `2*frame+channel`. Length is exactly `frames*8` bytes. Each `CASE.events.json` stores `native_events` and separately labelled `harness_controls`. Events preserve every native field: type, uint64 samplePosition, patternIndex, rowIndex, channelIndex, noteValue, instrumentID, intParam, floatParam and stringPayload. Python JSON integers preserve uint64 exactly; consumers must avoid converting samplePosition through a JavaScript Number or floating-point timestamp. JSON object ordering is not event ordering; array order is authoritative.

Native samplePosition is the cumulative number of frames rendered while transport is playing. The first row is processed at stamp zero immediately before its first audio. Row commands run before notes. The first row has no immediate per-tick effects pass; subsequent row tick zero does receive that pass, and nonzero ticks apply effects too. A tick event at the ending boundary of a block can equal the number of frames rendered so far, governing the next sample. Draining happens after Play and after every audio block; poll time and block-end time never replace native timestamps.

The counter is not reset by Play and does not count stopped/idle tail frames. The restart case records the distinction between host-frame actions and native playing-frame events. Direct note injections, releases, natural voice completion and voice stealing do not produce native note events; the harness records explicit injection/query provenance and hostFrame rather than fabricating event stamps. Host query observations can occur at a split imposed by a checkpoint, so the native call stream captures these boundaries as well as nominal buffer size.

The ABI exposes neither event-overflow nor command-queue error counters. Calls here are synchronous native exports, not enqueued managed commands, so the 160-note witness cannot overflow a command queue. Event batches are bounded far below the 4095 usable slots, drained after every block, and rejected if a drain reaches capacity. Exact event-track counts and expected row timelines assert no loss in clock cases. This is not a claim to observe an unavailable overflow counter.

At requested 137.5 BPM / LPB4 / TPL6, the native integer-BPM API cannot represent the requested tempo. These cases use native275/LPB2/requested137.5LPB4 metadata: BPM×LPB and hence row/tick clocks are mathematically identical, with tick length 872.727272... frames at 48 kHz. Their beat grids are explicitly different: native275 has half the beat duration and two rows per beat, while requested137.5 has four. The independently configured beat event interval stays four rows. There is no silent rounding to 137 or 138 and no claim of fractional-BPM ABI support. Native tempo payloads remain native integers. Actual expected row stamps are 0/5237/10473/15710/20946/26182; at120/LPB4 they are 0/6000/12000/18000/24000, identical across buffers64/333/1024.

## Coverage

| Case | Frames / block | Coverage | Comparison |
|---|---:|---|---|
| sample_mono | 16000 / 333 | Looping mono, pitched velocity note and release | numeric + exact events |
| sample_stereo_tuned | 16000 / 333 | Stereo source-rate conversion, fine tune and explicit velocity | numeric + exact events |
| sample_loop_0 | 16000 / 333 | Direct normal loop setter mode 0; held then released | numeric + exact events |
| sample_loop_1 | 16000 / 333 | Direct normal loop setter mode 1; held then released | numeric + exact events |
| sample_loop_2 | 16000 / 333 | Direct normal loop setter mode 2; held then released | numeric + exact events |
| sample_loop_3 | 16000 / 333 | Direct normal loop setter mode 3; held then released | numeric + exact events |
| synth_wave_0 | 16000 / 333 | Native wave 0 (sine/square/saw/reverse-saw/triangle) | numeric + exact events |
| synth_wave_1 | 16000 / 333 | Native wave 1 (sine/square/saw/reverse-saw/triangle) | numeric + exact events |
| synth_wave_2 | 16000 / 333 | Native wave 2 (sine/square/saw/reverse-saw/triangle) | numeric + exact events |
| synth_wave_3 | 16000 / 333 | Native wave 3 (sine/square/saw/reverse-saw/triangle) | numeric + exact events |
| synth_wave_4 | 16000 / 333 | Native wave 4 (sine/square/saw/reverse-saw/triangle) | numeric + exact events |
| synth_blend_0 | 16000 / 333 | Oscillator blend mode 0 and blend ADSR | numeric + exact events |
| sample_b_0 | 16000 / 333 | Stereo B source with independent tuning/loop; blend mode 0 | numeric + exact events |
| synth_blend_1 | 16000 / 333 | Oscillator blend mode 1 and blend ADSR | numeric + exact events |
| sample_b_1 | 16000 / 333 | Stereo B source with independent tuning/loop; blend mode 1 | numeric + exact events |
| synth_blend_2 | 16000 / 333 | Oscillator blend mode 2 and blend ADSR | numeric + exact events |
| sample_b_2 | 16000 / 333 | Stereo B source with independent tuning/loop; blend mode 2 | numeric + exact events |
| synth_blend_3 | 16000 / 333 | Oscillator blend mode 3 and blend ADSR | numeric + exact events |
| sample_b_3 | 16000 / 333 | Stereo B source with independent tuning/loop; blend mode 3 | numeric + exact events |
| synth_unison_3 | 16000 / 333 | Detuned stereo unison with 3 oscillators | numeric + exact events |
| synth_unison_8 | 16000 / 333 | Detuned stereo unison with 8 oscillators | numeric + exact events |
| fm_algorithm_0 | 16000 / 333 | Four FM operators, feedback, fixed-Hz operator, algorithm 0 | numeric + exact events |
| fm_algorithm_1 | 16000 / 333 | Four FM operators, feedback, fixed-Hz operator, algorithm 1 | numeric + exact events |
| fm_algorithm_2 | 16000 / 333 | Four FM operators, feedback, fixed-Hz operator, algorithm 2 | numeric + exact events |
| fm_algorithm_3 | 16000 / 333 | Four FM operators, feedback, fixed-Hz operator, algorithm 3 | numeric + exact events |
| fm_algorithm_4 | 16000 / 333 | Four FM operators, feedback, fixed-Hz operator, algorithm 4 | numeric + exact events |
| fm_algorithm_5 | 16000 / 333 | Four FM operators, feedback, fixed-Hz operator, algorithm 5 | numeric + exact events |
| defect_fm_6 | 16000 / 333 | Unsupported FM index falls back to additive algorithm 5 | explicit defect: fm_choices |
| defect_fm_7 | 16000 / 333 | Unsupported FM index falls back to additive algorithm 5 | explicit defect: fm_choices |
| kit_mapped | 16000 / 333 | Mapped drums fixed pitch, overlapping notes, unmapped rejection and release | numeric + exact events |
| envelope_target_0 | 16000 / 333 | Three-point nonlinear envelope target 0 | numeric + exact events |
| envelope_target_1 | 16000 / 333 | Three-point nonlinear envelope target 1 | numeric + exact events |
| envelope_target_2 | 16000 / 333 | Three-point nonlinear envelope target 2 | numeric + exact events |
| envelope_target_3 | 16000 / 333 | Three-point nonlinear envelope target 3 | numeric + exact events |
| envelope_control | 16000 / 333 | Same synth settings without multipoint envelopes | numeric + exact events |
| envelope_loop_0 | 16000 / 333 | Native envelope loop 0 (0 forward, 1 ping-pong, 2 falls forward) | numeric + exact events |
| envelope_loop_1 | 16000 / 333 | Native envelope loop 1 (0 forward, 1 ping-pong, 2 falls forward) | numeric + exact events |
| envelope_loop_2 | 16000 / 333 | Native envelope loop 2 (0 forward, 1 ping-pong, 2 falls forward) | explicit defect: envelope_mapping |
| modulation | 16000 / 333 | Deterministic instrument vibrato and curved arpeggio | numeric + exact events |
| detune_control | 16000 / 333 | Unison control without detune envelope | numeric + exact events |
| defect_detune | 16000 / 333 | Enabled detune envelope has no effect on current unison | explicit defect: detune_envelope |
| portamento_control | 16000 / 333 | Sequential notes with instrument glide False | numeric + exact events |
| portamento_enabled | 16000 / 333 | Sequential notes with instrument glide True | explicit defect: instrument_glide |
| channel_sample_base | 13000 / 333 | Channel sample base, initial and subsequent rows | numeric + exact events |
| channel_sample_mute | 13000 / 333 | Channel sample mute, initial and subsequent rows | numeric + exact events |
| channel_sample_half | 13000 / 333 | Channel sample half, initial and subsequent rows | numeric + exact events |
| channel_sample_left | 13000 / 333 | Channel sample left, initial and subsequent rows | numeric + exact events |
| channel_sample_right | 13000 / 333 | Channel sample right, initial and subsequent rows | numeric + exact events |
| channel_synth_base | 13000 / 333 | Channel synth base, initial and subsequent rows | numeric + exact events |
| channel_synth_mute | 13000 / 333 | Channel synth mute, initial and subsequent rows | numeric + exact events |
| channel_synth_half | 13000 / 333 | Channel synth half, initial and subsequent rows | numeric + exact events |
| channel_synth_left | 13000 / 333 | Channel synth left, initial and subsequent rows | numeric + exact events |
| channel_synth_right | 13000 / 333 | Channel synth right, initial and subsequent rows | numeric + exact events |
| channel_unison_base | 13000 / 333 | Channel unison base, initial and subsequent rows | numeric + exact events |
| channel_unison_mute | 13000 / 333 | Channel unison mute, initial and subsequent rows | numeric + exact events |
| channel_unison_half | 13000 / 333 | Channel unison half, initial and subsequent rows | numeric + exact events |
| channel_unison_left | 13000 / 333 | Channel unison left, initial and subsequent rows | numeric + exact events |
| channel_unison_right | 13000 / 333 | Channel unison right, initial and subsequent rows | numeric + exact events |
| channel_fm_base | 13000 / 333 | Channel fm base, initial and subsequent rows | numeric + exact events |
| channel_fm_mute | 13000 / 333 | Channel fm mute, initial and subsequent rows | numeric + exact events |
| channel_fm_half | 13000 / 333 | Channel fm half, initial and subsequent rows | numeric + exact events |
| channel_fm_left | 13000 / 333 | Channel fm left, initial and subsequent rows | numeric + exact events |
| channel_fm_right | 13000 / 333 | Channel fm right, initial and subsequent rows | numeric + exact events |
| channel_kit_base | 13000 / 333 | Channel kit base, initial and subsequent rows | numeric + exact events |
| channel_kit_mute | 13000 / 333 | Channel kit mute, initial and subsequent rows | numeric + exact events |
| channel_kit_half | 13000 / 333 | Channel kit half, initial and subsequent rows | numeric + exact events |
| channel_kit_left | 13000 / 333 | Channel kit left, initial and subsequent rows | numeric + exact events |
| channel_kit_right | 13000 / 333 | Channel kit right, initial and subsequent rows | numeric + exact events |
| master_filter_0 | 16000 / 333 | Master filter LP/HP/BP mode 0 | numeric + exact events |
| master_filter_1 | 16000 / 333 | Master filter LP/HP/BP mode 1 | numeric + exact events |
| master_filter_2 | 16000 / 333 | Master filter LP/HP/BP mode 2 | numeric + exact events |
| master_dry | 24000 / 333 | Master dry insert and tail; instrument sends zero | numeric + exact events |
| master_delay | 24000 / 333 | Master delay insert and tail; instrument sends zero | numeric + exact events |
| master_reverb | 24000 / 333 | Master reverb insert and tail; instrument sends zero | numeric + exact events |
| master_combined | 24000 / 333 | Master combined insert and tail; instrument sends zero | numeric + exact events |
| defect_send_64 | 8000 / 64 | Tick-split delay/reverb sends, buffer 64 | explicit defect: send_subchunks |
| defect_send_333 | 8000 / 333 | Tick-split delay/reverb sends, buffer 333 | explicit defect: send_subchunks |
| defect_send_1024 | 8000 / 1024 | Tick-split delay/reverb sends, buffer 1024 | explicit defect: send_subchunks |
| defect_send_1333 | 8000 / 1333 | Tick-split delay/reverb sends, buffer 1333 | explicit defect: send_subchunks |
| kit_reuse_control | 16000 / 333 | Kit reuses a completed sample voice with prior filter False | numeric + exact events |
| kit_reuse_filtered | 16000 / 333 | Kit reuses a completed sample voice with prior filter True | explicit defect: kit_filter_leak |
| kit_fresh | 15872 / 333 | Fresh kit reference for the kit-only segment after voice reuse | numeric + exact events |
| send_boundary_dry | 1333 / 1333 | Constant source isolates delay-send alignment at tick1000: dry | numeric + exact events |
| send_boundary_split | 1333 / 1000 | Constant source isolates delay-send alignment at tick1000: split | numeric + exact events |
| send_boundary_tick_split | 1333 / 1333 | Constant source isolates delay-send alignment at tick1000: tick_split | explicit defect: send_subchunks |
| presets_synth | 25000 / 333 | Base, variants 1/2, remembered user index and invalid fallback | numeric + exact events |
| presets_sample | 25000 / 333 | Base, variants 1/2, remembered user index and invalid fallback | numeric + exact events |
| voice_stealing | 12000 / 333 | 160 active looping sample requests; quietest and release-priority stealing | numeric + exact events |
| command_01 | 25000 / 333 | Command byte 1 / hex 01, tick-zero/nonzero, continuation/zero, OFF | numeric + exact events |
| command_01_control | 25000 / 333 | Disabled-command control for byte 1 | numeric + exact events |
| command_02 | 25000 / 333 | Command byte 2 / hex 02, tick-zero/nonzero, continuation/zero, OFF | numeric + exact events |
| command_02_control | 25000 / 333 | Disabled-command control for byte 2 | numeric + exact events |
| command_03 | 25000 / 333 | Command byte 3 / hex 03, tick-zero/nonzero, continuation/zero, OFF | numeric + exact events |
| command_04 | 25000 / 333 | Command byte 4 / hex 04, tick-zero/nonzero, continuation/zero, OFF | numeric + exact events |
| command_04_control | 25000 / 333 | Disabled-command control for byte 4 | numeric + exact events |
| command_07 | 25000 / 333 | Command byte 7 / hex 07, tick-zero/nonzero, continuation/zero, OFF | numeric + exact events |
| command_07_control | 25000 / 333 | Disabled-command control for byte 7 | numeric + exact events |
| command_08 | 25000 / 333 | Command byte 8 / hex 08, tick-zero/nonzero, continuation/zero, OFF | numeric + exact events |
| command_08_control | 25000 / 333 | Disabled-command control for byte 8 | numeric + exact events |
| command_0A | 25000 / 333 | Command byte 10 / hex 0A, tick-zero/nonzero, continuation/zero, OFF | numeric + exact events |
| command_0A_control | 25000 / 333 | Disabled-command control for byte 10 | numeric + exact events |
| command_0C | 25000 / 333 | Command byte 12 / hex 0C, tick-zero/nonzero, continuation/zero, OFF | numeric + exact events |
| command_0C_control | 25000 / 333 | Disabled-command control for byte 12 | numeric + exact events |
| command_10 | 25000 / 333 | Command byte 16 / hex 10, tick-zero/nonzero, continuation/zero, OFF | numeric + exact events |
| command_11 | 25000 / 333 | Command byte 17 / hex 11, tick-zero/nonzero, continuation/zero, OFF | numeric + exact events |
| command_12 | 25000 / 333 | Command byte 18 / hex 12, tick-zero/nonzero, continuation/zero, OFF | numeric + exact events |
| command_0A_up | 25000 / 333 | Gain slide up from .4 initial gain, nonzero ticks and zero reset | numeric + exact events |
| command_08_existing | 25000 / 333 | Pan setter leaves held voice unchanged, then affects next note | numeric + exact events |
| command_0B | 13000 / 333 | Order jump | numeric + exact events |
| command_0D | 13000 / 333 | Pattern break with raw row byte 0x12 (=18) | numeric + exact events |
| command_jump_break | 16000 / 333 | Combined jump and raw-row break on independent channels | numeric + exact events |
| command_0F | 20000 / 333 | Tempo byte 150, TPL3, F00 => TPL1 and exact tempo events | numeric + exact events |
| clock_120_b64 | 27000 / 64 | Exact native event clock and event-track strings at requested 120 | numeric + exact events |
| clock_120_b333 | 27000 / 333 | Exact native event clock and event-track strings at requested 120 | numeric + exact events |
| clock_120_b1024 | 27000 / 1024 | Exact native event clock and event-track strings at requested 120 | numeric + exact events |
| clock_137p5_b64 | 27000 / 64 | Exact native event clock and event-track strings at requested 137p5 | numeric + exact events |
| clock_137p5_b333 | 27000 / 333 | Exact native event clock and event-track strings at requested 137p5 | numeric + exact events |
| clock_137p5_b1024 | 27000 / 1024 | Exact native event clock and event-track strings at requested 137p5 | numeric + exact events |
| transport_restart | 16000 / 333 | Cumulative playing-only counter versus host frames on stop/idle/restart | numeric + exact events |
| noise_5 | 4096 / 333 | Legacy rand noise: statistical-only, nonportable, excluded from numeric parity | statistical only |
| noise_6 | 4096 / 333 | Legacy rand noise: statistical-only, nonportable, excluded from numeric parity | statistical only |
| noise_vibrato_jitter | 4096 / 333 | Legacy random vibrato rate: statistical-only nonportable reference | statistical only |

Sample fixtures exercise mono/stereo, rate and fine-tune conversion, velocity, normal off/forward/ping-pong loops and held sustain regions followed by release. The sustain setter is called directly, with different normal and sustain bounds; on release the old engine switches back to ordinary forward bounds during its release envelope. B playback has its own tuning/rate and forward loop. Mix blend zero means A and one means B; Ring/Sync/PM blend zero means processed output and one means clean A. FM covers exactly six implemented routings, distinct ratios, feedback, four envelopes and one fixed-Hz operator. Stored FM waveform is not used by the renderer, which explicitly computes sine. Kit cases include unmapped rejection, fixed source pitch, overlap, direct release and natural completion.

The voice-limit witness sustains looping samples. It renders between requests so all initial128 pool slots are genuinely active, asserts exactly128, then forces quietest-active stealing and release-priority stealing before reaching160 total requests. Returned IDs and active-count observations are captured. The old engine ramps normalization per native subchunk, so audio across different buffers is not universally bit-identical even without sends. Comparisons use matching case buffer variants; timing invariance is separately asserted.

Command labels use the native bytes rather than future Renoise labels: 01/02 slide,03 retained-voice glide,04 vibrato,07 tremolo,08 next-note pan,0A channel gain slide,0B order jump,0C gain,0D raw-byte pattern break and0F tempo/TPL. “16/17/18” in the task are decimal16/17/18, hex10/11/12 (G macro set/H macro slide/I preset). Hex16/17/18 are unimplemented. G/H have no native audio mapping: their observable state changes are asserted with exact-boundary queries while a sustained voice exists. Presets prebuild three native slots and assert selection/memory/fallback. Glide03 is actively tested despite the separate instrument-glide defect. OFF/release is present in command songs. Pattern break uses0x12 as row18, not BCD12, and combined jump/break uses independent channels. F00 means TPL1, not stop.

### Additional native limitation discovered during implementation

Command07's tick handler sets a volume modifier, but source inspection finds no volume-modifier evaluation in any voice renderer. Its generated audio is exactly equal to the disabled-command control, and that equality is asserted. There is no exported getter for this internal modifier. The corpus exercises the command and preserves the no-effect counterexample; the requirement that07 produce observably active audio remains impossible under the task's ban on engine/source/ABI changes. This issue was not in the owner's approved-defect list and is not silently granted an exemption. A future corrected-tremolo gate must prove audible amplitude modulation against a disabled control, with its numeric deviation explicitly approved. Other audio-affecting commands have disabled controls or direct signal/state/event assertions; G/H are verified through exported state.

## APPROVED-DEFECTS

These are approved corrections for the future engine, never instructions to reproduce legacy bugs. Ordinary cases use native enum meanings and corrected channel behavior. Defect-tagged cases preserve evidence; exemptions are per case and per defect ID, never a global tolerance increase.

| Issue / ID | Evidence and reference handling | Future gate |
|---|---|---|
| Channel gain/pan already fixed | Current port transfers settings to each new sample, synth, unison, FM and kit voice. Generator asserts exact mute, isolated hard pan, half-gain ratio and later-row behavior for all five; original verify.py independently covers sample/synth/unison. Older standalone health-check findings predate this fix. No exemption. | Preserve the fixed behavior. |
| Detune envelope / `detune_envelope` | Current render reads envelope targets0–3 but never4. Paired unison renders with target4 enabled/disabled have identical PCM; generator asserts equality. | Corrected envelope must produce the intended detune change; callback proof required if numeric comparison is exempted. |
| Instrument glide / `instrument_glide` | New voice channel identity is initialized to-1, preventing previous-channel pitch lookup. Paired enabled/disabled sequential-note renders are equal; command03 separately changes retained-voice pitch. | Correct instrument glide without removing working command03. |
| Wave enum mismatch / `wave_mapping` | Source-only authoring evidence: editor Sine/Triangle/Saw/Square/Noise versus native Sine/Square/Saw/ReverseSaw/Triangle/White/Pink. Native waves0–4 use native semantics, and5/6 are statistical references. No authoring reinterpretation in this harness. | Explicit saved-data migration; native wave tests themselves retain semantic parity. |
| Eight FM choices, six implemented / `fm_choices` | Current renderer handles0–5;6/7 fall through to5. Generated paired fallback witnesses assert exact equality to5. | Restrict/migrate unsupported authoring choices; do not invent old algorithms6/7. |
| Envelope loop mapping / `envelope_mapping` | Authoring sends1 for Loop,2 for Ping-pong; native0 is forward and1 ping-pong, while2 falls through forward. Generated direct setter cases assert0==2 and0!=1. | Correct authoring migration. Direct native0/1 remain positive parity cases. |
| Send buffers cleared per subchunk / `send_subchunks` | Each voice subchunk clears/writes sends at local index0; outer processing consumes whole-block sends. Corpus preserves delay/reverb sends at64/333/1024/1333. Additional constant-source dry/send controls isolate the fault: a50-percent delay send survives separate1000+333 calls, but inside one1333-frame call the contribution is lost after tick1000. Both relationships are asserted without oscillator/polyphony ramp confounders. | Correct send alignment/accumulation; matching-buffer legacy samples require an explicit per-case exemption plus corrected-behavior proof. Never demand universal block-invariant old PCM hashes. |
| Kit filter state leak / `kit_filter_leak` | Kit initialization omits filter/send reset. Corpus completes a64-frame filtered/unfiltered sample, asserts inactive and reuses exact slot0 for kit. Assertions exclude the128-frame predecessor: fresh kit equals clean reused-kit PCM, while filtered reuse differs within the kit-only segment. | Reset reused voice state; prove fresh and reused kit output agree. |
| rand noise / `random_noise` | Noise5/6 use the same rand call; random vibrato also consumes it. Context creation cannot reset process-global static MSVC CRT state; no seed export exists. Investigator verified external msvcrt.srand does not control this DLL. Corpus uses fixed ordered calls and separate complete generation processes for repeat hashes, but noise/jitter cases are explicitly statistical-only/nonportable and excluded from numeric parity. No external srand is called. | Use a seeded per-voice generator; run a separately calibrated noise/spectral test. Current comparison only applies wide finite/RMS/peak sanity, not portable noise or pink-spectrum parity. |

Source fingerprints in the manifest tie these findings to the installed reference inputs. Native source itself, the installed binary and ABI declarations are unchanged. Wave/authoring mapping evidence is source inspection, not a live editor walk. The kit and send defects can contaminate pooled output, so they are isolated from clean positive cases with fresh native contexts.

## Comparison library and CLI

```powershell
python compare_golden.py PATH_TO_CANDIDATE --atol 1e-5 --rtol 1e-4 --event-frame-tolerance 0 --report logs/comparison.json
```

A candidate must supply manifest schema1, the same case IDs, frame counts, sample rate/stereo layout, clock and buffer specifications, and the same raw audio/JSON event format. Extra/missing cases/artifacts, malformed event fields, noninteger uint64 positions, nonfinite audio/events and shape/rate mismatches fail before parity comparison. Native provenance hashes may differ for a candidate engine; output hashes in a manifest are reference/reproducibility records rather than bit-exact parity requirements.

Each sample passes only when `abs(candidate-reference) <= atol + rtol*abs(reference)`. Reports include peak absolute error, RMS error, bad-sample count and first offending frame/channel. Events compare all payload fields and array ordering exactly, with zero sample-position tolerance by default. A caller may explicitly request an integer frame tolerance, which remains visible in the report. No automatic alignment, truncation, normalization or global relaxation occurs. Default tolerances are a calibration starting point for C++ /fp:fast versus Burst strict float; they are not empirically measured cross-engine tolerances.

For macro G/H, preset I and preset songs, the comparator also requires the same harness observation host-frame boundaries and checks channel macro values, preset selection and remembered user instrument. A candidate with identical audio but wrong macro/preset state fails. These observations are state verification, not native event stamps or polling-time approximations.

Other harness observations remain reference evidence and are not automatically compared. In particular, matching voice-stealing audio/events does not prove a candidate's active-voice limit or stealing policy. The future runner must independently assert128 simultaneously active voices and the intended quietest/released-voice selection using its own handles; identical internal pool IDs are not a parity requirement. The same principle applies to lifecycle or other internal behavior that has no native event or distinct audio signature.

Library: `compare(reference, candidate, atol=..., rtol=..., event_frame_tolerance=..., exemptions={case: defect}, correction_checks={case: callback})`. A callback receives candidate case metadata, interleaved audio, native events and full trace; it must check the intended corrected behavior. An explicitly approved numeric exemption still requires exact events and valid shape/finite audio. Without a correction callback it reports `EXPECTED_DIVERGENCE_REQUIRES_CORRECTION` and fails (CLI exit2). `--exempt CASE:DEFECT` is useful to isolate the old numeric mismatch but cannot certify a correction by itself. Unknown/unapproved exemptions fail. Statistical-only noise exclusions are always named in the report and gated for finite/nonzero/bounded signal. A native self-match proves comparator operation, never Burst parity.

The test script checks a complete second generation byte-for-byte, comparator self-match, small perturbation pass, large perturbation fail with correct frame/channel, shifted/removed event failure, explicit frame tolerance, nonfinite audio/event rejection, truncated shape, missing/extra cases, wrong rate, exact integers beyond2^53, and per-case exemption validation/correction requirements.

Verified-by-probe: generation assertions, exact clock/event matrices, repeated complete generation, numeric comparison failure paths and original native regression checks (rerun these commands after editing tooling). Verified-by-eye: none; this offline corpus does not use an editor or audio device. Not-verified: Burst parity, calibrated cross-engine tolerance, physical listening, Unity event delivery, GC isolation, non-Windows/native compiler portability or future authoring migration.
