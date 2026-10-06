# P5 implementation verification — 2026-10-06

This records implementation evidence and the sequential independent review for T-0014 in the canonical `D:/UNITY/Laubrary Dev` checkout on `x/zounds-sap`. The deprecated source checkout was read-only. The implemented command behavior follows the proposed ZTracker policies in COMMANDS.md; no Renoise playback equivalence is claimed. The existing DLL authoring player remains active until P7. The original implementation artifacts below remain recorded; the post-review evidence at the end supersedes them for the reviewed source.

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

## Independent source and adversarial review

The review compared the command compiler, native scheduler, automation/source maps, prepared presets and grouped fixture assertions with the normative policies, rather than treating the thirty passing labels as the evidence. Four defects were reproduced and corrected:

- A delayed OFF coincident with release-duration automation captured the old `.1s` slope instead of the newly written `1.1s` slope. Before correction the coefficient was `.0002083333`; after correction it is `.00001893939`. Both an effect tick and an off-tick point reproduce the difference at caller sizes64/333/1024. Retained regressions check the frozen coefficient, first release frame and continuing rendered tail, plus earlier/later exact automation deadlines sharing one quantized frame.
- A pending successor suppressed broader track commands on its still-playing predecessor. Two track U10 updates before a half-row launch should produce `+.5` semitones; the previous result was0. Track/Master descriptors now continue while event-local operations remain deferred. Retained cases cover accepted, unmapped, rejected, OFF and instrument-only events, local-over-track precedence at launch, and track reverse without a rejected local offset altering the predecessor.
- Reconstructing an authored automation coordinate by reversing cumulative frame arithmetic could land just below a Step point. At137.5BPM, point6.375 became6.374999999999999, left the macro at0 and rescheduled the same deadline33381.81818181818. A one-step runtime-method probe reproduced this without hanging the editor. The scheduler now retains the original coordinate. Actual compiled renders check macro0→1 at frame33381, simultaneous neighboring lane emission, progress to33382 and no repeated deadline at all three caller sizes.
- Duplicate instrument identities could silently bind a macro source/lane or preset selector to the first matching slot. Such stable-ID bindings now refuse with named diagnostics; independent indexed notes and authored macro banks remain intact. Retained regressions keep both `.2/.8` banks unchanged and preserve the source payload.

Group02 was also strengthened: the prior malformed81 case retained a legacy byte, but did not exercise an actual malformed numeric value. The new numeric129 case asserts `INVALID_COLUMN_NUMERIC`, unchanged held gain1 and exact raw preservation. The reviewed group06/24/26 scenarios augment the existing thirty groups; they do not add new parity claims.

Post-review canonical editor evidence is `D:/UNITY/_builds/ztracker-p5/review-final-suite.txt` and `review-suite-cli.json`: clean matching editor, model40/40, engine33/33, P4 27/27, expanded P5 30/30 and independent integration PASS. Scoped warmed allocations remain0 bytes, with engine36/P5 64 native blocks and0 managed-block delta. The existing harness regenerated `review-goldens/comparison.json` and `p5-policy-result.json`: P3 equivalent22/22, P4 scope51/51,7 retained separate corrections,12 different-DSP characterizations and3/3 independent P5 policy cases. `equivalent_success=true`; aggregate `success=false` remains intentional for the characterized DSP differences.

The post-review standalone proof at `D:/UNITY/_builds/ztracker-p5/review-aot/` supersedes the earlier standalone artifacts for the reviewed runtime. `build-result.json` reports Succeeded in48.5676283s with the same single unrelated missing QuizU Boot scene preprocessing error in `build-errors.txt`. The CLI caller timed out after30s while the editor continued the build; the completed build report contains no bridge-timeout error. `build-freshness.json` records new managed engine/proof and Burst artifacts; the reused incremental bootstrap EXE timestamp is not freshness evidence.

| Reviewed artifact | Modified UTC | SHA256 |
| --- | --- | --- |
| Managed engine DLL | 2026-10-06T04:29:02.631Z | 6de7a1cd04e56f137892e68ce9cdb1267175429c334aff285aa1babcd59d73e2 |
| Managed proof DLL | 2026-10-06T04:29:03.176Z | 4e655df42fba580a5eeacdbfeb0aa66f558ea81239abcc81d5b41ed571e1721e |
| Burst native DLL | 2026-10-06T04:29:13.850Z | 0b29cd03295ead66487bb6c43bcda8b9861707d570a267854e4831fb54ca4988 |

`player-exit.txt` records exit0. `player-proof.json` reports passed=true, errors=0, the P5 native implementation witness, active P4/P5 command/automation coverage and observed macro operations; `player.log` contains no runtime error/exception. The compiled player reran engine33/33, P4 27/27, expanded P5 30/30 and independent integration PASS, with0 warmed P5 render allocations,64 native blocks and0 managed-block delta.

| Reviewed interval | Tracker frames / callbacks | Native guards / managed blocks | Measured tracker rate |
| --- | --- | --- | --- |
| Baseline,3.0051882s | 144384 /141 | 282 /0 | 48044.9111 frames/s |
| Forced full GC,6.0042833s | 288768 /282 | 564 /0 | 48093.6667 frames/s |
| Recovery,3.0000764s | 143360 /140 | 280 /0 | 47785.4497 frames/s |

The reviewed proof retained3,000,000 garbage objects and performed34 collections. Maximum producer/main-thread collection duration was33.7227ms, not audio callback latency. All intervals passed the existing compiled execution/continuity criteria. Eight swaps,10 retired disposals,3 repeated lifecycle cycles, timeout retention/reclamation and pending-swap rejection/disposal passed. Quit passed after75 actual frames and.7480841s in this batch-mode run. This remains allocation/native-execution and observed continuity evidence, without a callback-deadline, underrun, physical-listening or Renoise-equivalence claim.
