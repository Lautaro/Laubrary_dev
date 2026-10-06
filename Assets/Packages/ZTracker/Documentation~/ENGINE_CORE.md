# P3 tracker engine core

This is the opt-in schema-1 engine in `Laubrary.ZTracker.Engine`. It does not replace the existing DLL player or compatibility bridge; that switch belongs to P7. Preparation reads the canonical song/instrument models and their typed retained parameter extension, never the old compatibility views or archives. It makes no authored asset changes. Synth/FM produce diagnosed silence until P4. Sample B, blend/PM curves and macros are diagnosed as deferred; presets and commands beyond the P3 subset remain P5 work.

## Preparation and ownership

`TrackerPreparedSong.Prepare(ZTrackerSong, rate, maxFrames)` requires an explicitly migrated schema-1 asset. The `SongData` overload is useful for an independent golden adapter without creating song assets. Instrument slots still reference schema-1 instrument assets; transient, unsaved instances are sufficient. Preparation copies mono/stereo, Decompress On Load PCM on the main thread, deduplicated by clip identity within each prepared song. Every voice borrows that immutable pool. Settings and per-voice state remain separate when multiple samples share a clip.

Preparation validates model references, capacities, finite data, PCM, loop bounds, visible note-column layout and the routing DAG before publication. Failure disposes partial native allocations. Track outputs and insertion sends both participate in cycle detection. Runtime tables, voices, columns, modulation state, buffers, chain layouts and state arenas are flat unmanaged storage allocated before rendering. Each chain processor has one disposer; copied structs alias its arrays and are not additional owners.

An offline engine or SAP host consumes one fresh prepared owner. `Published` means ownership was claimed, including a configured host before its first callback. Direct disposal of a claimed owner throws. Do not edit or dispose its state arrays, and do not hand the same owner to another renderer. Prepare a fresh owner to make a change. The PCM copy is released together with the owner's quiet render ticket, never per voice.

## Offline API

```csharp
using var engine = new TrackerOffline(TrackerPreparedSong.Prepare(songData, 48000));
engine.SendCommand(TrackerCommand.Play(loop: false));
engine.Render(leftNativeArray, rightNativeArray, frames, blockFrames: 1024);
while (engine.ReadEvent(out TrackerEvent e)) { /* compare or consume events */ }
bool burstActuallyRan = engine.Compiled;
long lostEvents = engine.EventOverflow;
```

Output arrays are planar floats supplied by the caller. Subsequent renders continue every voice, modulation, chain timeline/RNG and clock scalar. The job operates on a persistent native pointer to the same realtime struct used by SAP; it does not lose scalar updates in a copied job field. `Compiled` is an actual BurstDiscard witness, not an attribute inspection. `RenderManaged` exists for controlled baseline experiments. It deliberately increments the managed audio guard and must not be used to claim native proof. `Snapshot` is a borrowed synchronous inspection view for offline checks, not an ownership transfer. Offline swaps are refused; create a new owned engine. `Dispose` is idempotent, and subsequent API calls throw instead of touching a freed pointer.

## SAP host and lifetime

On a GameObject with an AudioSource, add `TrackerSapGenerator`, call `Configure(freshPrepared, playSong: true)`, then use the AudioSource's SAP generator playback API. The host declares stereo and the prepared sample rate. There is one generator for the complete song. Main-thread commands travel through the SAP control-to-realtime pipe. They take effect when SAP delivers its between-block update, without a prefetched audio buffer or inserted output latency. Preparation and pipe submission may allocate on the main thread; rendering does not.

```csharp
var host = gameObject.AddComponent<TrackerSapGenerator>();
host.Configure(TrackerPreparedSong.Prepare(songData, AudioSettings.outputSampleRate));
var source = gameObject.GetComponent<AudioSource>();
source.generator = host;
source.Play();
```

`SwapPrepared(next, playSong)` queues an already validated, independently allocated same-rate owner. The realtime update finishes the previous owner's ticket before replacing state. The new state starts a fresh row clock at the preserved absolute playing counter. The event ring has a separate lifetime across swaps. Each retired owner must have a terminal ticket and a stable even observation before reclamation. Event consumers must keep the appropriate payload table when retaining events from an earlier prepared owner; a payload index is local to that owner.

`StopAndConfirm(timeout)` first refuses commands/publication and stops the carrying AudioSource, then observes stable even tickets for a full settling interval, flushes pending control work, and destroys the graph instance. A timeout retains the complete ownership bundle. Destruction never frees arrays based on an odd or moving ticket. A registry pump keeps observing retained terminal bundles even when their original host has gone. Quit refuses new rendering, silences hosts, and waits at least two actual frame updates and 0.3 seconds before teardown and the final quit. Helper checks cover stable-even/odd/movement/timeout and drain logic; live callback, swap, destruction and standalone shutdown still require the independent SAP verification described below.

## Clock, commands and events

The clock follows COMMANDS.md P-CLOCK: binary64 Kahan cumulative row durations and its eight-ULP near-integer Q snap, otherwise floor. Row entry is applied before its first sample. Tick deadlines are computed from the row origin; delay `d` uses `Q(E + (d/256)*D)`, independently from audio partitions. Distinct fractional deadlines sharing one frame preserve their unquantized order. The next row owns the row-end boundary. There is no tick rounding of delayed notes.

P3 supports note/OFF, literal instrument selection, volume 0..128, pan 0..128 and delay 0..255. Instrument/volume/pan/foreground/delayed-cell state is per column. A note/OFF/instrument-only event carries its setters to the delayed deadline. Without a note or instrument-only selection, numeric volume/pan apply at row entry and ignore delay. Numeric zero remains distinct from empty. Unsupported local/column/effect commands, automation and parameter-set selectors are diagnosed and ignored without guessed effects.

Global commands are evaluated once per row in canonical track/column order. ZT supports decimal32..255 and zero hard stop; decimal20..31 gives `BPM_RANGE_DISPUTE`; other invalid values are preserved and ignored. ZL supports1..255 and zero hard stop. ZK supports1..16; zero is invalid. ZB selects the next sequence slot at the raw byte row after completing the current row; a shorter destination clamps to its last row with a bounded diagnostic. Fresh Play cuts voices, clears FX histories, restores authored timing/mixer/mutes and resets column selection, while preserving the cumulative playing sample counter. Stop cuts voices and histories immediately. Idle render/audition advances voice and FX time but freezes the playing counter. Seek cuts voices/history and silently replays only the implemented scalar command/column subset along actual sequence/ZB flow, bounded to one million rows. Unreachable targets stop with a diagnostic; no guessed target state is started. Pause/resume, automation reconstruction and the wider P5 command-memory engine are not implemented here.

At48000Hz/137.5BPM/LPB4/TPL6, v1 row frames are `0,5236,10472,15709,20945,26181`; the native P0 floor-countdown engine's equivalent trace is `0,5237,10473,15710,20946,26182`. This one-frame fractional difference is intentional contract characterization, not native event parity. Integral120/4 sampler fixtures remain directly comparable. Initial BPM32..999, LPB1..256 and TPL1..16 are accepted subject to at least one output frame per tick; sub-frame tick settings are refused with `INVALID_CLOCK`.

`TrackerEvent` contains kind, cumulative playing `samplePosition`, sequence/pattern/row/tick, track/column/note/instrument and payload. Unused identifiers are -1. Kinds are Row, Tick, NoteOn, NoteOff, Beat, Authored, VoiceStolen, PreparedSwap, Stopped and Diagnostic. Authored payload is an index into `eventPayloads`; no string allocation happens on audio. Diagnostic payload codes are SeekUnreachable=1, PitchLimit=2, InvalidPitch=3 and BreakClamped=4. Events use a bounded raw-memory SPSC ring: one audio producer, one main-thread consumer, release/acquire cursor publication, drop newest on overflow, cumulative overflow count. Host capacity is65536; offline capacity is configurable2..1048576. Never poll the realtime NativeArrays concurrently; main-thread event/ticket observation uses independent native pointers.

## Sampler and mixer behavior

Key/velocity zones have inclusive boundaries; overlapping zones launch simultaneous components. Key tracking off preserves fixed pitch. Each component has an independent linear AHDSR including hold, frozen-level linear release, cursor/direction, modulation state and filter history. OFF releases only the current column's cohort and ignores one-shot components. Each old sample's Cut/NoteOff/Continue NNA applies only when a mapped successor launches, keeping mixed-layer NNA and detached tails independent. Sampler-global NNA is retained authoring data; per-sample NNA is authoritative for playback. Nonzero mute groups choke matching components and tails on the same instrument and Sequencer track, not other tracks. The fixed pool steals an idle slot first, then the quietest released envelope, then the quietest active amplitude; every reused field is reset. If layering exceeds the pool, components can be stolen within the new cohort; no unbounded overflow voices are created.

Forward, Backward and PingPong loops use exclusive frame ends and bounded remainder/reflection for large increments. Backward starts at end-1. Release-exits-loop resumes forward playback from the current cursor through the clip. Linear and Catmull-Rom cubic interpolation are implemented. New samplers wrap/reflect interpolation neighbors within their active loop; migrated native samplers retain clip-clamped neighbors for equivalence. Pitch is bounded before exponentiation to2^-20..2^20 source frames per output frame; clipping/invalid input emits bounded diagnostics.

Ordered AHDSR, multipoint sustain/loop, four-shape LFO, velocity, key tracking and fader devices target volume/pan/pitch/cutoff/resonance/drive with Add/Multiply/Replace. Time fields and curve-point positions are seconds; rate is cycles/second, phase is cycles, pitch output is semitones, cutoff is Hz, other targets use their numeric normalized/linear domains. Unit labels accept seconds/normalized/semitones/Hz and do not convert authored numbers. LFO state and multipoint cursors are per component. The first enabled volume Multiply AHDSR uses the incremental amplitude envelope, preserving native envelope arithmetic; additional envelopes remain ordered device stages. Filter types0/1/2/3 are Off/LowPass/HighPass/BandPass, with per-component coefficients/history and cutoff/resonance modulation. Drive uses tanh on the voice stage.

Modern mono uses cosine/sine equal-power pan and stereo uses center-unity balance; authored sample/instrument/note/modulation pan sums then clamps. Migrated instruments retain native square-root pan and native authored/note pan combination, selected from typed v1 provenance, never from a compatibility view. Exclusively migrated sampler songs receive the native voice-slice1/sqrt(active components) normalization ramp and master soft knee above0.9, with `LEGACY_MIX_NORMALIZATION_AND_SOFT_KNEE`. Newly authored or mixed songs add components linearly and have no hidden automatic normalization/knee. Track/group/send/master center pan is unity at every stage.

Instrument FX has an independent chain history per `(Sequencer track, instrument, FX chain)` partition; two tracks using one instrument never merge before their track chains/faders. Track outputs and sends process in DAG order: instrument FX, track prefader width/volume/pan, device segments, insertion taps, postfader and output routing. Send taps can precede, split or follow a chain and select pre/postfader gain/pan. Live postfader gain/pan ramp over64samples; every postfader tap uses the exact same per-frame ramp. Trigger mute blocks only new notes. Output mute/solo/sequence mute suppress every outgoing tap while voices and effect histories continue. Sequence changes within a caller block use per-frame mute masks.

All16 AudioCore effects and5 modifiers use the shared P1 kernels, with a portable preparation compiler and the existing numeric parameter schema. Chains run on the caller block, independent of tracker tick cuts; AudioCore owns its64sample control grid. Segments at insertion taps duplicate modifier state with the same seed/timeline. Arbitrary caller partitions can retain P1 coefficient/presence sensitivity; no universal bit-identical effect partition claim is made. A constant-PCM phaser check measured333vs1024 caller blocks at max error2.98023224e-8, while changing tracker TPL1vs6 at fixed1024caller blocks stayed within1e-7. Continuous bus chain context uses sourcePeak1 and a30second normalized envelope domain with elapsed sample time; it does not pretend a summed bus is an individual PCM source. Source-stage bindings are refused because a bus has no source pitch/speed stage. Live ZPOC/control mapping is outside P3.

Typed retained filter scalars compile to each voice, with a diagnostic and native normalized cutoff times Nyquist. Retained vibrato compiles cents to semitones plus fade and deterministic cohort-derived rate variation; this seeded variation is not a native rand parity claim. Arpeggio compiles a looping step curve. Retained sends resolve to the first explicitly authored Send track containing the matching Delay/Reverb device. Missing destinations refuse preparation rather than creating hidden buses. Sample-B-related retained curves remain diagnosed P4 work. Retained effects never mutate the instrument asset.

## Limits and diagnostics

| Area | Bound |
| --- | --- |
| Output rate / preparation block | 8000..192000Hz /64..65536frames |
| Voices / tracks / Sequencer tracks | 4096 /128 /64 |
| Instrument slots / patterns / sequence slots | 4096 /4096 /65536 |
| Samples or zones per instrument | 4096 |
| Modulation sets / devices per component / points per curve | 256 /64 /4096 |
| Instrument FX partitions / total chains | 8192 /16384 |
| Nodes / modifiers / bindings per chain | 64 /64 /512 |
| PCM / mixer stereo buffers / aggregate chain state | 256MiB /128MiB /128MiB |
| Individual chain state / steps per modifier | 16MiB /4096 |

Preparation diagnostics include empty instruments, unsupported legacy note, ignored P5 commands/column selectors/automation/presets, P4 synth/sample-B/macros, implicit-zero clock commands, invalid/disputed clocks, compiled retained filter/vibrato/arpeggio/sends and migrated mix policy. Diagnostics are managed preparation data. Runtime diagnostics use the bounded event ring and numeric payloads. Malformed refs/layout/curves, unsupported units/source bindings, cycles, missing explicit retained send destinations, invalid loop/PCM and capacity failures throw before ownership transfer.

## Reusable checks and current proof scope

Run `Laubrary.ZTracker.Engine.TrackerEngineCheck.Execute()` through Unity CLI `eval_file` in the canonical editor, without the Unity Test Runner. A temporary evaluation file can return `new { failed = UnityEditor.EditorUtility.scriptCompilationFailed, witness = TrackerPreparedSong.ImplementationWitness, check = TrackerEngineCheck.Execute() }`. Target `--project-path "D:/UNITY/Laubrary Dev"` explicitly, and refresh assets after editing. Fixtures create transient clips/instruments and destroy them; no asset files, UI, legacy audio device or scene state are written.

Implementation checks currently pass33/33 with editor compilation successful and the new type loaded. They cover independent six-setting Kahan/Q traces across64/333/1024partitions, precise delayed notes, literal zero, column/layer ownership, sample NNA/choke/stealing/filter reset, loop/release/cubic, all device kinds/operations/targets, modulation sustain/loops, all16effects/5modifiers, instrument routing attribution, insertion taps/faders/mutes, global clocks/flow/restart/seek, overflow, malformed preparation and lifetime helpers. Warmed scoped `GC.GetAllocatedBytesForCurrentThread` measured0bytes over20active1024frame renders; the scoped guard measured36native blocks and0managed-block delta, and the offline BurstDiscard witness was true.

Not yet certified by these implementation checks: native P0 golden comparison and effect mapping/corrections; fresh standalone player GC isolation alongside a Zounds voice; real SAP callback/swap/retirement/destruction and quit stress; physical listening or UI workflow. Those require the independent verification phase. Helper and offline evidence must not be represented as live SAP proof. The unchanged native P0 corpus and its explicit scoped case manifests remain the reference, including its approved corrections and representability limits.
