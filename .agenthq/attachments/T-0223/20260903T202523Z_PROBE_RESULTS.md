# T-0223 — determinism probe results

Probe: `Assets/Packages/Laubrary/Editor/Shaper/T0223_RampFieldProbe.cs` (committed in `5fba824c`, deleted in the follow-up commit). Run in the Shaper worktree editor (`Application.dataPath` = `D:/UNITY/Laubrary Dev - Shaper/Assets`, checked first). Renders were taken on `Instantiate()` copies; no authored asset was opened for edit, dirtied or saved.

```
=== T-0223 GradientField probe ===
--- A/B/C Pyre ramp presets through the field ---
PASS  display never writes: 31/31 presets (10 of them hold more than 8 stops)
PASS  8-key picture with exact endpoints: 10/10
PASS  edit round-trips (<=8 stops): 21/21 (worst channel delta 0.00002354, Orb - Toxin
      - Unity's own key-storage precision, not a blend change)
PASS  blend space survives an edit: 21/21

--- D Shaper demo document render ---
PASS  8 frames, 18 ramps shown in a field: hash 726369D46658FA47 -> 726369D46658FA47

--- E Pyre asset render ---
PASS  Green Lantern: 4 frames, 14 ramps (0 over 8 stops) - shown 7470403CAEB8116F -> 7470403CAEB8116F
PASS    after a simulated edit of every ramp: 7470403CAEB8116F (all ramps within 8 keys - expected identical)
PASS  Bars Tentacle Plus: 4 frames, 9 ramps (0 over 8 stops) - shown E90AA2BDAF522470 -> E90AA2BDAF522470
PASS    after a simulated edit of every ramp: E90AA2BDAF522470 (all ramps within 8 keys - expected identical)
PASS  Blob Explosion Plus: 4 frames, 17 ramps (0 over 8 stops) - shown DEC615BE2422E313 -> DEC615BE2422E313
PASS    after a simulated edit of every ramp: DEC615BE2422E313 (all ramps within 8 keys - expected identical)
```

## What each line means

**A — display never writes.** For every shipped Pyre ramp preset, the probe calls exactly what the control calls to fill the field (`ZuiRampGradientBridge.ToGradient`), then compares the ramp's stops position-for-position and colour-for-colour, plus 1024 evaluation samples, against a snapshot taken before. Nothing moved, at any stop count. This is the load-bearing claim: an authored ramp does not degrade merely by being looked at through an 8-key field.

**C — the 8-key boundary is honest.** The ten presets with more than 8 stops each show exactly 8 keys, with the first and last carrying the ramp's real end colour, alpha and position (RGB and alpha compared separately, because a `GradientColorKey` carries no meaningful alpha).

**B — an edit round-trips.** For the 21 presets within 8 keys, the probe hands the shown Gradient straight back the way the field does on edit, and re-samples. Worst channel difference across 21 presets x 1024 samples is 2.35e-5, i.e. Unity's own gradient-key storage precision, not a blend or ordering change. Stop counts and blend spaces are unchanged.

**D/E — render hash.** The Shaper demo document (8 frames, 18 ramps) and three authored Pyre assets render byte-identically after every ramp they hold has been through the display path. For the Pyre assets the probe goes further and simulates an edit of *every* ramp; because none of the three exceeds 8 stops, the pixels are still identical. A >8-stop ramp would legitimately differ there — that is the trade this task chose, and the probe would have said so.

## Not covered by the probe

- No human dragged a stop in Unity's popup; the popup itself is Unity's, unmodified.
- Undo/redo across an edit through the field was not exercised (the control routes through the same `OnBeforeMutate`/`OnChanged` wrappers it always did, unchanged).
- Play mode, runtime/player behaviour and the palette-cycle shader path were not run.
