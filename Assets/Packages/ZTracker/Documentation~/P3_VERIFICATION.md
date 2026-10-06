# P3 independent verification

This document records the engine integration checks and standalone SAP proof in canonical Laubrary Dev on Windows x64, Unity 6000.3.10f1. It is a developer reproduction guide. Native golden coverage and the twelve distinct legacy effect mappings are specified separately in `Tools/ZTrackerNative/P3_GOLDENS.md`; they must not be described as full tracker parity. Physical listening and other platforms are outside these measurements.

## Reproduce the checks

Use the running canonical editor with Unity CLI, explicitly targeting `--project-path "D:/UNITY/Laubrary Dev"`. Do not use the Unity Test Runner. An evaluation file can return the following independently owned check entry points:

```csharp
return new {
    failed = UnityEditor.EditorUtility.scriptCompilationFailed,
    model = Laubrary.ZTracker.Editor.ZTrackerModelCheck.Execute(),
    engine = Laubrary.ZTracker.Engine.TrackerEngineCheck.Execute(),
    integration = Laubrary.ZTracker.Proof.TrackerIntegrationCheck.Execute(),
    chain = AudioCoreChainCheck.Execute(),
    lifetime = AudioCoreLifetimeCheck.Execute()
};
```

The final editor run reports compilation successful, P2 model 40/40, engine 33/33 and all independent integration categories passing. The latter check exact normalized audition gain, active render BurstDiscard marker, specific/stale voice releases including a prepared swap followed by a reused slot, new launches under output mute, Started event, generic provenance isolation, the measured retained native pitch profile, modern/migrated PCM PingPong intro, AHDSR Add/Replace order, and multipoint PingPong intro. Check indices use the authored appended modulation stage; the primary AHDSR now stays at its authored position rather than disappearing from the table.

Twenty warmed active 1024-frame offline renders measured 0 current-thread allocated bytes. The scoped audio guard recorded 36 native renders and 0 managed-block delta. This current-thread allocation measurement does not measure unrelated main-thread activity or every possible song. Render code contains only value structs, flat native arrays/pointers, kernels and bounded loops; it performs no managed allocation, Unity object access or Unity logging. Main-thread preparation, host command submission and proof reporting intentionally use managed data.

AudioCore's 18 reusable cases measured 0 managed/reference, compiled/reference and defined split-path differences, 0 warmed render allocations and actual Burst witnesses. Its arbitrary caller partition characterization remains distinct: the phaser check measures about 0.000236827880144, and the randomized 16-effect/seven-modifier chain measured 0.014371416997164488 in the final editor run. These fixture results characterize existing coefficient/presence sensitivity; they are not a universal numeric bound. The tracker constant-source phaser check measured 333-versus-1024 caller max error 2.98023224e-8, while tracker TPL changes at a fixed caller partition stayed within 1e-7. No P1 Delay/HighPass baseline difference was changed.

## Burst hot-reload safeguard

During this verification, a failed Editor assembly compilation followed by successful C# compilation left the offline job using an old native layout after fields were added to the prepared sample/modulation tables. Compilation success and a true Burst witness alone did not detect it: the same fixture's managed voice used the new base pitch 0.9999999403953552 and finite audio, while the stale native job read shifted fields, reported base pitch 1 and produced NaNs. That candidate output was rejected. The compiler's existing invalidation/recompilation methods were invoked through this temporary editor evaluation, without closing the owner's editor or editing its package:

```csharp
var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
typeof(Unity.Burst.BurstCompiler).GetMethod("NotifyPackagesChanged", flags).Invoke(null, null);
typeof(Unity.Burst.BurstCompiler).GetMethod("DomainReload", flags).Invoke(null, null);
typeof(Unity.Burst.BurstCompiler).GetMethod("TriggerRecompilation", flags).Invoke(null, null);
return "Burst assembly cache invalidated and jobs recompiled";
```

These internal methods were inspected in the installed Burst package and are a development recovery procedure, not a shipped engine dependency. After invalidation, the fresh native job passed the complete checks and exported all 34 real candidate renders with native witnesses true, zero event overflow and zero fixture failures. A fresh standalone AOT build provides a separate compilation path. Future agents changing an unmanaged table layout must require current numerical checks as well as a compilation/witness result before accepting any golden export.

## Standalone proof protocol

The separate `Assets/ZTrackerP3Proof` verification assembly references both ZTracker and Zounds; the engine assembly has no Zounds dependency. Invoke `Laubrary.ZTracker.Proof.TrackerPlayerProofBuild.BuildNow(outputExe)` through a temporary evaluation file. It creates/saves/closes only its own empty additive scene and builds that scene as a Windows x64 Development player outside Assets. Existing owner scenes and their unsaved state are not saved or rebuilt. CLI's 30-second command timeout can occur while Unity continues the build; require a newly dated build-result file before running the player. The final build succeeded in 44.7132031 seconds; the one build error is the pre-existing Pipeline processor lookup of the missing QuizU demo Boot scene, not a player compilation failure.

Run the executable with `--tracker-p3-proof <absolute-result-json> --tracker-p3-gc-objects 3000000 -logFile <absolute-player-log>`. Wait for its process to exit. The opt-in runtime proof installs no overlay, menus or managed audio callback. It creates a looping tracker voice and a looping compiled Zounds voice in the fresh scene, sets runInBackground, warms up for two seconds, then records a three-second baseline, six seconds of repeated forced full garbage collections and three seconds of recovery. The GC object count is bounded 10000..5000000, default 1500000; the stronger proof uses 3000000 retained 256-byte arrays plus references/object headers, roughly 850 MB, to force pauses longer than one DSP block. Each forced collection cycle does two blocking full collections with pending-finalizer drain. The expected frame count comes from wall-clock elapsed seconds times the output rate; it does not rely on a potentially stalled dspTime.

Allowed frame-count observation jitter is the larger of three DSP blocks or 3% of expected frames. Actual tracker completed-render counters exclude swap/disposal operations; the Zounds ticket is sampled only during intervals with no swaps/disposal. Both voices must render at a rate close to the wall-clock expectation, report native witnesses, have nonzero completed block counts and produce no managed audio guard blocks. Tracker tick events must remain exactly one 120/4/6 tick apart and monotonic. This verifies processed audio frames and event continuity; it does not record the device's physical output or certify every possible driver/hardware stall.

After accounting, two queued replacements verify a never-rendered owner does not taint the native witness, eight spaced replacements verify quiet retirement, zero-timeout stop verifies retention, successful stop verifies frozen callbacks and disposal, and three fresh start/audition/stop/destroy cycles verify repeated lifecycle use. An adversarial timeout stops publication and flushes control before temporarily marking a stable external ticket in flight; destroying the host must retain its owner, and restoring the quiet ticket must permit reclamation. The proof also records native handle existence after actual source stop and an accepted unused replacement if the graph remains present. This distinguishes real graph behavior from the deliberately forced timeout condition.

Quit begins with both voices active. The registry must refuse new rendering, silence them and delay final exit for at least two actual Update frames and 0.3 seconds. The result is saved on Application.quitting, and the process must have exited before the measurement is accepted. No proof relies solely on the process remaining open or on shared lifetime helper checks.

## Final standalone measurements

The final player process exited with code 0 and result passed=true, errors=0. Output rate was 48000 Hz, DSP blocks 1024 frames with four buffers. Both voices independently reported these completed frames and blocks:

| Interval | Wall-clock seconds | Expected frames per voice | Actual frames per voice | Actual blocks per voice | Shared native guard blocks | Managed guard blocks |
| --- | --- | --- | --- | --- | --- | --- |
| Baseline | 3.0000787 | 144003.7776 | 143360 | 140 | 280 | 0 |
| Forced full GC | 6.0000483 | 288002.3184 | 288768 | 282 | 564 | 0 |
| Recovery | 3.0000077 | 144000.3696 | 143360 | 140 | 280 | 0 |

All differences from wall-clock expected frames were less than one DSP block, within the stated jitter tolerance. Both voices kept rendering at approximately 48 kHz during baseline, collection and recovery. Actual tracker render-path and Zounds compiled witnesses stayed true. Thirty-four forced double full-collection cycles ran on 3000000 retained objects; the longest cycle in this final run took 34.3771 ms, exceeding the 21.3333 ms DSP block duration. This final binary includes both the unconditional control-disposal flush and monotonic swap-generation fixes.

The final runtime repeated the complete 33/33 engine checks and independent integration checks, including the prepared-swap stale-release regression, before accounting. Tick/event continuity passed. Eight spaced swaps and two queued swaps reclaimed ten retired owners; the queued owner witness remained compiled. Three fresh host cycles passed. Zero-timeout stop and the deliberately odd external ticket both retained ownership; reclamation occurred after quiet. Successful stop froze callbacks before disposal. Native graph existence was true before source stop and false afterward on this Unity version, so the optional accepted-after-stop unused replacement branch was not exercised. The retained native-handle destruction path was audited in code, while this run's actual stopped graph had already been detached by Unity. It does not claim to force a real callback/control race.

Final quit drained for 8713 actual Update frames and 0.7395414 seconds, exceeding both minimum gates. The very high update count is the headless drain pump's observed frame rate; only the actual-frame minimum and elapsed-time gate are correctness requirements. Evidence files from the completed run are outside Assets at `D:/UNITY/ZTracker/.p3-player/proof-complete.json`, `D:/UNITY/ZTracker/.p3-player/player-complete.log` and `D:/UNITY/ZTracker/.p3-player/build-result.json`. Generated build products are not source deliverables. Addressables settings remained byte-identical; only this build's generated group/linker churn was restored or removed.

## Review corrections and limits

The independent review corrected output-mute launch suppression, PCM/multipoint PingPong intro reflection, primary-envelope operation ordering and generic provenance accidentally enabling native DSP. Live stress found a separate swap-rate bug: the initial owner's state was cleared after retirement, so later swaps incorrectly compared their rate against that cleared state. The host now retains its immutable configured rate. Timeout cleanup retains and later destroys the native graph handle, with the ticket kept alive through disposal and a second quiet observation. Control work is flushed even if Unity has already removed the handle on source stop. The voice generation remains monotonic across prepared swaps, preventing an old specific-release command from hitting a new voice that reused its slot. Completed-render counters and zero-frame owner witnesses were corrected independently of lifetime ticket operation counts.

Explicit retained native sample pitch uses the measured MSVC fast-float calculation, supported by the original P0 held-pitch values and a standalone compilation of the original arithmetic. The native compiler folds sample frequency arithmetic to a float reciprocal product just below one, while the kit constant-folds base/base to one. Modern pitch remains mathematical double-power. This retained profile is tied to the reference's Windows compiler arithmetic; no other compiler ABI/platform parity is implied.

The sampler core supports only P3's committed subset. Synth/FM, Sample B/macros and wider command/preset behavior remain diagnosed P4/P5 work. Native's separate held/normal release loop cannot be represented by the v1 single-loop/release-exit contract and is explicitly outside the equivalent subset. The normative fractional Kahan/Q clock intentionally differs from native countdown by a frame in the documented 137.5 BPM example. Twelve legacy master/send effect fixtures are numerical characterizations of distinct algorithms/mix domains, not accepted parity exemptions. Arbitrary FX caller partition invariance and physical listening remain unclaimed.
