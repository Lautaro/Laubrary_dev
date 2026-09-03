# T-0221 — determinism probe results

Run in the Shaper worktree editor (`Application.dataPath` = `D:/UNITY/Laubrary Dev - Shaper/Assets`, verified before trusting anything), against `Editor/Shaper/T0221_GradientProbe.cs` at commit `3d6c0f76`. Final run, all sections:

```
=== T-0221 gradient probe ===
--- A migration exactness ---
PASS  2 keys: 0→2 stops, max channel delta 0, 8-bit mismatches 0/1024
PASS  alpha keys off the colour keys: 0→5 stops, max channel delta 0, 8-bit mismatches 0/1024
PASS  8 keys: 0→8 stops, max channel delta 0, 8-bit mismatches 0/1024
PASS  fixed/stepped: 0→6 stops, max channel delta 0, 8-bit mismatches 0/1024

--- B Pyre ramp presets ---
PASS  stop-for-stop conversion: 31/31 presets (10 of them have more than 8 stops)
PASS  evaluation matches PyreShade: 31/31 (worst delta 0)
PASS  ramp→gradient→ramp round-trip: 31/31

--- C saved-library round-trip ---
PASS  12-stop entry saved and resolved: 12 stops back, space LinearLight
info  Gradient export of the same ramp: 8 keys (the cap now applies only where a UnityEngine.Gradient is demanded)

--- D Shaper demo document render ---
PASS  8 frames, 18 gradients (12 converted by this run): hash 726369D46658FA47 → 726369D46658FA47

--- E Pyre asset render ---
PASS  Green Lantern:      4 frames,  9 gradients, hash 7470403CAEB8116F → 7470403CAEB8116F
PASS  Bars Tentacle Plus: 4 frames,  9 gradients, hash E90AA2BDAF522470 → E90AA2BDAF522470
PASS  Blob Explosion Plus:4 frames, 17 gradients, hash DEC615BE2422E313 → DEC615BE2422E313
```

## What each section actually proves

- **A** takes a gradient in the state a pre-T-0221 asset loads in (legacy `Gradient`, no stops — set through the private fields by reflection), samples it 1024×, converts it, samples again, and compares as `Color32`. Zero differing samples means the stop list reproduces the Gradient it came from, alpha keys at their own positions included.
- **B** runs all 31 shipped Pyre ramp presets through `PyreShaperRampPresets.ToZuiGradient` and back through `ZuiRampGradientBridge`, comparing stop counts, every position and colour, the blend space, and 1024 evaluated samples against `PyreShade`'s own `EvalStops`. **Ten of the 31 have more than 8 stops** — those are precisely the ones the old path subsampled.
- **D/E** are real renders, not ramp maths: the document/asset is `Instantiate`d (the authored asset is never touched), every frame is rendered and hashed, every reachable `ZuiGradient` is then converted, and the same frames are rendered and hashed again. Equal hashes mean the conversion moved no pixel.

## The one thing the probe caught

The first implementation converted a **Fixed (stepped)** gradient by placing one stop per key and interpolating between them — turning every hard edge into a slope. Measured **816 / 1024 samples wrong**. Fixed in `3d6c0f76`: each interval gets a stop pair carrying its own colour, boundaries are repeated positions (how this codebase already writes a hard edge), plus a leading pair for Unity's own `t = 0` quirk (Fixed reads the NEXT key across an interval but returns the FIRST key's colour at exactly zero — measured, not assumed). Now 0 / 1024.

Nothing in the package authors a Fixed gradient today, so this case was reachable only through a hand-authored asset — which is exactly why it was worth a probe rather than an assumption.
