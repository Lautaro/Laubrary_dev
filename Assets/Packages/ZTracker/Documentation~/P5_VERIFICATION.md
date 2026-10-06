# P5 implementation verification — 2026-10-06

This records implementation evidence for T-0014 in the canonical `D:/UNITY/Laubrary Dev` checkout on `x/zounds-sap`, before the parent's independent review. The deprecated source checkout was read-only. The implemented command behavior follows the proposed ZTracker policies in COMMANDS.md; no Renoise playback equivalence is claimed. The existing DLL authoring player remains active until P7.

## Probe evidence

Editor evaluations explicitly target the canonical project and require the matching `Application.dataPath` plus `EditorUtility.scriptCompilationFailed=false`. The final combined output is `D:/UNITY/_builds/ztracker-p5/final-suite.txt`: model40/40, engine33/33, P4 27/27, P5 30/30 and independent integration PASS. P5's thirty labels are groups containing multiple scenarios, each rerun at caller sizes64/333/1024. They are not thirty individual inputs or a parity corpus. Scalar assertions use absolute1e-6; frame/byte/count assertions are exact. The warmed P5 render measure is0 allocated bytes,64 native blocks and0 managed-block delta; the original engine measure remains0/36/0 and P4's warmed measure remains0 allocated bytes. Actual BurstDiscard witnesses and guard counters are required, not just Burst attributes.

| Group | Observable subcases exercised |
| --- | --- |
| 01 decode | Omitted command amount and raw presence, stale omitted backing values, base36 A1/I001, raw GG, scope mismatch, exact MIDI marker placement, malformed/ambiguous context, absent instrument and omitted payload amount; independent timing survives a wrong-column marker. |
| 02 numerics | Volume80/pan40 and malformed81, with unchanged supported state for malformed input. |
| 03 clock | First13 Kahan/Q stamps, million-row shared primitive progression and virtual callback bucketing at1/64/257/1024, ULP snapping, nonfinite and2^40 guards in live/replay, subframe ticks in a valid row, equal-frame distinct exact launch/tick chronology. |
| 04 pitch | Actual772-row UFF and DFF renders, opposing recovery, finite extreme tuning, increment limits, tiny loops and component-local NaN refusal. |
| 05 glide | Prior slide to tracker pitch62, full G10 rows reaching63/64, independent arpeggio/vibrato and differently tuned zones, GFF with held zone identity, no suitable foreground fallback. |
| 06 ticks | First-row tick0 and delayed half-row launch receiving only the remaining slide ticks, exactframe3000. |
| 07 delay | Track/volume/pan Q precedence, fractional addition and outside-row cancellation. |
| 08 cuts | Early hard cut suppresses a delayed successor without chasing its predecessor; CF1 launch initialization, delayed OFF, OFF/instrument-only captured future cuts and C4 refusal. |
| 09 conflicts | Master/track/local/volume/pan priority and independent valid memory updates; other columns follow track commands; unsupported later timing writer cannot win. |
| 10 banks | V47/V00/V30, blank frozen phase/neutral output and local-zero refusal to borrow the volume bank. |
| 11 reset | Fresh note gain/pan/pitch, local zero reuse across a pattern boundary, pause/resume freeze and Stop selection/memory clearing. |
| 12 ownership | Layered Continue/Note-Off behavior, accepted/rejected delayed successors, detached-tail isolation, immediate local S/B/E binding, invalid/unmapped launches, instrument-changing glide, one-shot OFF and same-track mute-group choke with another track surviving. |
| 13 LFO math | Independent V/T/N formula outputs at k2 and blank neutral0/1/0. |
| 14 gain/arp | A37 offsets, C80/O10 arithmetic and256 full I01 rows reaching1. |
| 15 region | S80 on1000 frames, sliced S02 region, S04 refusal, held reverse movement, paired-B reverse/offset and Synth S refusal. |
| 16 envelope | Independent AHDSR/Multi-point/Fader seek times, individually retained Stepper and finished-clock refusal to reactivate. |
| 17 retrigger | R04 ticks4/8 without0/end, selected restart versus column cursor retention, R00 refusal and late launch. |
| 18 factors | Independent .47/.335/.25/.75/1 factor results. |
| 19 random | Four independent known hash constants, first/second [0,17,34] weighted choices, all-zero refusal, Y0/YF/YFF and no reselection after the chosen bundle's volume cut. |
| 20 mixer | M/L/P endpoints and interior byte curves; Synth numeric gain and sample-only command refusal. |
| 21 stop | X00 immediate/delayed entering-row launches, X01 refusal, unrelated voices and actual inseparable downstream history. |
| 22 unsupported/routes | Hardware/width/phrase/groove payload preservation, unchanged clock/graph, explicit unsupported phrase S context, internal ancestor routing cycle refusal and nested group/send contributors. |
| 23 flow | ZB12/ZD02 frame18000 destination, one launch, original row-end automation frozen during holds, short target clamp and explicit last-slot break wrap. |
| 24 identity | Source slot2→external73→physical cutoff, unmapped external2, unchanged unrelated volume, explicit identity API and unknown source default preservation on fresh Play/Seek. |
| 25 value | Reversed endpoints, exact .25→.65→.7 quantum fixture, explicit knots, equal endpoints and imported Exp Fast refusal/raw preservation. |
| 26 automation | Step/Linear deadlines, tick0 direct setter then lane resumption, unsupported neighbor/quantum/metadata isolation, fractional tempo refusal, safe feedback next to a memory-sizing target, bypass clears old echo while new echo can rebuild. |
| 27 disabled | Last emitted versus latent values, exact enable-at.75 emission and independent direct macro lane. |
| 28 seek | Emitted.6/latent.9 disabled replay, off-tick point chronology, voice/history clearing and warning, subsequent enable.9 and bounded unreachable flow. |
| 29 legacy | Exact raw reversal/presence, per-byte named reasons, decimal16/17/18 versus literalhex16/17/18, F00 candidate retained without execution, F17..31 refusal, exact approved F1..16/F32..255 subset, forged modern relabel/decision refusal, documented pan candidate arithmetic and explicit future-note preset binding without canonical slot cloning. |
| 30 deviations | Per-channel .2/.8 shared macro ownership flags, retained raw bytes and separate corrected tremolo amplitude gate against the native no-op behavior. |

The million-row check invokes the same binary64 clock progression/quantizer used by live rendering, reconstructs deadlines in virtual caller partitions and compares all million stamps. It skips empty callback spans rather than rendering roughly5.24 billion PCM frames at size1. This is shared-clock/virtual-scheduler evidence, not a million-row DSP or physical callback run. Actual rendered command fixtures use64/333/1024. Probability expected constants were supplied independently from a Python little-endian uint64 oracle, not computed by the implementation under test.

## Existing golden harness

The existing P3 exporter and Python adapter were extended rather than replaced. `prepare --p4`, canonical-editor export and `compare` produced `D:/UNITY/_builds/ztracker-p5/goldens/comparison.json`, `p5-policy-result.json` and `D:/UNITY/_builds/ztracker-p5/golden-comparison.txt`. The retained original122-case baseline/source corpus is unchanged; the scoped exporter renders90 retained fixtures plus3 independent P5 policy cases. Scoped sets overlap and must not be added together as disjoint passes.

| Scope | Result |
| --- | --- |
| P3 equivalent gate | 22/22 accepted, with its independently checked kit-filter correction retained. |
| P4 scope | 51/51 accepted; unchanged ordinary atol1e-5/rtol1e-4 and exact event timing. |
| Approved corrections | 7 separate correction checks across the retained scope; no tolerance relaxation. |
| Different DSP | 12 explicit characterizations, not equivalent audio. They make aggregate comparison `success=false` while `equivalent_success=true`. |
| P5 policy PCM/events | 3/3 independent analytic cases at64/333/1024; absolute1e-6, exact note frame3000, delayed silence and subsequent scalar automation/gain levels. These are not native or Renoise parity cases. |

Seeded noise/jitter retain their existing statistical-only exclusions and P4 repeatability checks. No NaN acceptance, frame alignment, baseline regeneration or fallback-to-managed render was used.

## Standalone proof

The final guarded build produced `D:/UNITY/_builds/ztracker-p5/P5Proof.exe`; `build-result.json` reports Succeeded in45.0456899s with1 logged preprocessing error. `build-errors.txt` preserves the exact error: missing demo scene `Assets/Unity Technologies/QuizU - A UI Toolkit demo/Scenes/Boot.unity`. No unrelated demo/project setting was changed. An earlier64.46s build also logged an editor bridge timeout after60s; that earlier two-error build is historical evidence, not the final report.

The final foreground process exited0 (`final-player-exit.txt`). `final-player-proof.json` reports passed=true, errors=0, witness `P5-command-automation-native-core-v1`, active P4/P5 coverage and observed macro operations. The proof retains actual SAP sampler/synth/FM/B callbacks and adds active pitch/LFO/retrigger commands plus addressed macro automation. The compiled player reran engine33/33, P4 27/27, P5 30/30 and independent integration PASS; its warmed P5 render measure is0 allocated bytes,64 native blocks and0 managed-block delta. `final-player.log` retains the player log.

| Interval | Tracker frames / callbacks | Native guards / managed blocks | Measured tracker rate |
| --- | --- | --- | --- |
| Baseline,3.0000628s | 143360 /140 | 281 /0 | 47785.6664 frames/s |
| Forced full GC,6.0000036s | 287744 /281 | 562 /0 | 47957.3046 frames/s |
| Recovery,3.0003852s | 144384 /141 | 282 /0 | 48121.8212 frames/s |

The forced interval retained3,000,000 garbage objects and performed34 collections. All intervals required compiled execution and passed the proof's continuity criteria. The maximum forced collection duration measured on the producer/main thread was31.5014ms; native render progress continued during the interval. This is not a callback duration measurement. For context, the nominal1024-frame/48000Hz block period is21.3333ms. These allocation/native-execution and observed continuity results do not establish a callback deadline, underrun measurement or no-dropout guarantee. The proof completed8 swaps, disposed10 retired owners, passed3 repeated lifecycle cycles and timeout retention/reclamation, rejected and disposed a pending swap correctly, and passed quit after8267 actual frames and.7210361s. Physical listening/device behavior was not inspected.

`build-freshness.json` records the final artifacts independently of Unity's unchanged incremental bootstrap EXE timestamp:

| Final artifact | Modified UTC | SHA256 |
| --- | --- | --- |
| Managed engine DLL | 2026-10-06T04:10:16.7485669Z | 3D88499AC3FA6C600130142D28868893B6FEB7422C077D98454D450537FBFBAA |
| Managed proof DLL | 2026-10-06T04:10:16.7985661Z | 06EB688204AC6430D09FEC35070A1162EC56388DC39BC38BDE9D3BDA33B7D5D6 |
| Burst native DLL | 2026-10-06T04:10:27.6071374Z | A4CB090540BC40BE5261CB7654FF1DFDA13BA24C5AB64F0F11E42E6B2122A8DA |

## Limits and review boundary

Supported source device mappings require original ordinal/identity metadata and declared equivalents. Direct Sample/Synth/Modulation lanes with no such declaration, imported curves/scaling, unknown physical units, memory-sizing targets and discrete DSP parameters lacking an allowed-value table are preserved and diagnosed individually. Unsupported phrases/MIDI/hardware/width/groove-on sources are retained rather than guessed. Legacy conditional conversions remain preserve-and-flag unless a versioned choice and its proofs are provided; no last-channel macro reassignment, automatic instrument clone or invented ZJ was introduced. Explicit preset IDs bind future-note prepared data only.

This executor does not activate conditional legacy proof packages. A modern profile or migrationDecision string cannot override retained unsafe legacy provenance. Only exact approved P2 F1..16→ZK and F32..255→ZT mappings execute with a timing-difference diagnostic; F00 remains a model candidate but is refused here, as are F17..31 and forged identifiers/amounts. This is an explicit implementation limit, not a claim that decision text alone proves equivalence.

Verified-by-probe: the editor suites, compiled offline renders, scoped comparisons, native allocation guards and fresh player evidence above. Verified-by-eye: none; this phase changed core/data/checks/docs only. Not verified: physical listening/device output, Renoise playback equivalence, editor UI/Handover Walk, XRNS/XRNI import/export, unsupported contexts and legacy backend retirement. Independent review and board handover belong to the parent.
