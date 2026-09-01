# T-0127 — VERIFICATION

Built and verified in the separate worktree `D:\UNITY\Laubrary Dev - Shaper` (port 7801), pointed at via Coplay's
`set_unity_project_root` and confirmed with `Application.dataPath` before any editor action. Uncommitted.

Every numeric leg below is a permanent addition to `Editor/Shaper/ShaperLightAudit.cs`
(`LT24_SolidsHeightFieldRelief`, called from `RunAll()`) rather than a throwaway script, matching the file's own
LT-1..LT-23 convention — a future task can re-run it with `ShaperLightAudit.RunAll()` or
`ShaperLightAudit.LT24_SolidsHeightFieldRelief()` directly. `LT24_ContactSheet`/`LT24_ExtremeZoom`/
`LT24_ExtremeMetric` are the visual/adversarial checks, same pattern as `LT17_ContactSheet`.

## Part 1 — compiles clean, no regression

`check_compile_errors` clean throughout. Confirmed the new `ShaperSolids.FillTile` overload actually loaded
(not a stale cache) via reflection on the live editor's loaded assembly:

```
OK: ShaperSolidGeometry geo, ShaperSolidOp& op, ShaperSampleGrid& grid, Int32 x0, Int32 y0, Int32 width,
    Int32 height, ShaperSolidEmit& emit, Int32 dstOffset, Int32 dstStride,
    ShaperFillProgram fillProgram=default, ShaperNormalOp normalOp=default
```

`ShaperLightAudit.RunAll()` — **LT-1 through LT-23, every one, PASS**, unchanged from their pre-T-0127
behaviour. Two of these are the structural guarantees this task's change could most plausibly have broken, and
both held:

- **LT-1b (c)** — "ShaperSolids methods touching a compiled-rig type in their IL, SIGNATURE, RETURN or LOCALS: 0
  (expected 0) PASS." Solids still cannot see a light, an ambient or a response block directly; this task added
  a fill program and a normal-dial struct to its signature, neither of which is the compiled-rig type LT-1b
  scans for. The Solids/light-rig architectural boundary SHAPER_THE_DESIGN.md's B8 describes is unchanged.
- **LT-15** — every provider (Constant, and all six Solids forms across 32 rotations each) still writes a UNIT,
  finite, non-zero normal at every sample. This runs `ShaperSolids.FillTile` with NEITHER new parameter
  supplied (the pre-T-0127 call shape), so it also stands as a second confirmation that an unpassed
  `fillProgram`/`normalOp` reproduces the old behaviour exactly.

## Part 2 — LT-24, the new numeric legs, all PASS

```
LT-24 T-0127: a HeightField fill perturbs Solids' own analytic normal
  normal variance across one Box's covered samples: flat fill 0.000E+000 (expect ~0), HeightField fill 1.062E-001 (expect > 0)  PASS
  silhouette untouched on an Orb: coverage differs on 0/4096 samples, edge distance differs on 0/4096 (expect 0, 0)  PASS
  scale=6, Box/Orb/Ring x12 rotations each: unwritten/NaN 0, zero 0, non-unit 0, worst ||N|-1| = 1.306E-007  PASS
  scale=60, Box/Orb/Ring x12 rotations each: unwritten/NaN 0, zero 0, non-unit 0, worst ||N|-1| = 1.306E-007  PASS
  scale=600, Box/Orb/Ring x12 rotations each: unwritten/NaN 0, zero 0, non-unit 0, worst ||N|-1| = 1.306E-007  PASS
  a non-HeightField fill (Solid) is unperturbed and deterministic: 0/6912 components differ across two identical builds (expect 0)  PASS
  RESULT: PASS
```

**Leg 1 — the headline claim, measured, not eyeballed.** Same Box, `yaw = tilt = roll = 0` so exactly one facet
(the +Z face) fills the frame (no second facet's own, genuinely different constant normal to contaminate the
baseline), same directional light, same MEAN albedo (the `HeightField` fill's flat tint and the `Solid` fill's
colour are both `(0.5, 0.5, 0.5)`, matching B5's "no colour, not no albedo"). Only the fill kind differs. A flat
fill gives that one facet exactly one constant normal — variance `0.000E+000` to float precision, confirming
the test fixture itself is sound. A HeightField fill gives real per-sample variance, `1.062E-001` — the
perturbation is genuinely happening, not just declared.

**Leg 2 — the silhouette boundary holds, measured on an Orb.** Same Orb, flat fill vs HeightField fill: `0/4096`
coverage samples differ, `0/4096` edge-distance samples differ. The perturbation changes only the third
published quantity (SurfaceDirection); Coverage and EdgeDistance are bit-identical regardless of which fill is
attached. This is the direct, measured answer to the task's non-negotiable boundary — a fill still cannot touch
Solids' silhouette, before or after this change.

**Leg 3 — rotation and extreme height, together.** Box/Orb/Ring, 12 rotations each (yaw/tilt/roll all swept),
at `heightFieldScale` 6 (the intended authoring range), 60 (10×) and 600 (100×): zero unwritten/NaN samples,
zero exactly-zero vectors, zero samples exceeding `1e-3` unit-length error, at every scale. The worst observed
`||N|-1|` is bit-identical (`1.306E-007`) across all three scales — plausible and not a test artefact: with
`heightFieldScale = 600` the largest possible per-tap height jump across a texel boundary is a few thousand
canvas units, nowhere close to float overflow, so `Mathf.Sqrt`/normalise stay in a well-conditioned range at
every scale tested and the residual error is dominated by ordinary float rounding rather than by the
perturbation's own magnitude. This directly answers the task's rotation question: the analytic base normal is
recomputed fresh every tile from the solid's real 3D geometry (unchanged by this task), and the perturbation
composes onto whatever that base currently is, so rotating the Solid does not desynchronise the two.

**Leg 4 — a fill kind this task did not scope in stays inert, exactly.** Two identical builds of a Gem with a
`Solid` (non-HeightField) fill: `0/6912` normal components differ. `slopeGain != 0` alone is not enough to
perturb anything — the `fop.kind == HeightField` gate matters, and a Solid fill's constant, non-positional
height (`op.height`, a single authored float with zero spatial gradient by construction) would have contributed
nothing even without the kind gate. Both are true and this leg confirms it rather than assuming it.

## Part 3 — the visual check: does it actually read as raking-light relief

`LT24_ContactSheet` (`D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0127\lt24-contact-sheet.png`, attached), one
directional light, flat-vs-HeightField pairs at three different yaws, plus Orb, plus the `lines_GEN1_001`
preset, plus two adversarial-scale cells. Personally looked at, not just rendered:

- **Cells 01/03/05 (flat) vs 02/04/06 (plates_GEN10_001, scale 6), at yaw 15°/95°/210°** — the flat cells are a
  single uniformly-lit grey facet, as expected (a facet has one constant normal). The plates cells show real
  raised/inset plate-and-pad shapes with visible highlight-on-ridge / shadow-in-groove relief that survives all
  three rotations without breaking up, flattening out, or losing the pattern — this is the task's own success
  criterion, met and looked at directly, not inferred from the numeric legs alone.
- **07 (flat Orb) vs 08 (Orb + plates)** — confirms the perturbation composes correctly on a curved,
  non-facet-constant base normal too, not just a flat facet: the plates pattern is visible and follows the
  sphere's own existing shading gradient (brighter toward the light, darker away from it) rather than
  overriding it.
- **09 (flat) vs 10 (lines_GEN1_001, scale 6)** — the second T-0111/T-0124-named preset, thinner circuit-trace
  detail, also reads as real relief rather than a flat decal.
- **11 (scale 60) vs 12 (scale 600)** — see Part 4; this is the adversarial pair, described honestly below
  rather than folded into the "it works" summary above.

## Part 4 — adversarial self-check: extreme height values

Required by the task, run against my own fix rather than skipped. `LT24_ExtremeZoom`
(`D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0127\lt24-extreme-zoom.png`, attached) renders the same Box at
`heightFieldScale` 6 / 60 / 600 side by side at 220×220, and `LT24_ExtremeMetric` quantifies the difference
between them rather than trusting a screenshot impression:

```
T-0127 extreme-scale metric (Box, plates_GEN10_001)
  scale 6 vs 60:  mean |dRGB| 0, max |dRGB| 163, local-order flips 4
  scale 6 vs 600: mean |dRGB| 0, max |dRGB| 236, local-order flips 7
```

(`|dRGB|` is summed per-channel byte delta, max possible 765; a "local-order flip" is a crude discontinuity
proxy — adjacent-pixel brightness ordering that reverses between the two renders.)

**Honest reading, not oversold.** The perturbation stays NUMERICALLY safe at 100× the intended scale — Part 2's
leg 3 already showed zero NaN/zero/non-unit vectors even at `scale = 600`. Visually, the change is real and
grows with scale (`max |dRGB|` 163 → 236, local-order flips 4 → 7) but is **not** a catastrophic breakdown in
this fixture: `mean |dRGB|` stays at the rounding floor, meaning the effect is localised to specific pixels
rather than smeared across the whole facet, and the zoomed render at 220×220 shows a modestly rougher,
higher-contrast relief at 100×, not visible tearing or an inverted-looking bump. **The root cause, named rather
than left implicit:** `ShaperFillOps.Sample`'s `HeightField` case uses NEAREST-texel indexing
(`bulk[offset + fy*width + fx]`, no bilinear interpolation) — this is HeightField's own existing sampling
convention, unrelated to and unchanged by this task, but it means the central-difference gradient this fix
takes is a true step function at every texel boundary rather than a smooth one. `heightFieldScale` multiplies
that step directly, so cranking it far past the intended authoring range amplifies whatever hard edges the
underlying nearest-sampled texture already has, rather than introducing a new kind of artefact. **This is a
real, named limitation of composing a smooth-normal perturbation onto a nearest-sampled height source, not
something this task's own code introduces independently** — the same nearest-sampling convention would show
the identical blockiness in HeightField's ALBEDO at extreme zoom, this fix just makes it visible in the SHADING
too. Not hardened against (no smoothing/bilinear pass was added, matching the task's instruction to build the
narrowest correct fix rather than solve adjacent problems); named honestly rather than papered over.

## Part 5 — cost

Measured directly (`System.Diagnostics.Stopwatch`, 200 reps, JIT-warm, a 96×96 tile, Box at yaw 22°/tilt 14°):

| Path | ms / `FillTile` call |
|---|---|
| No `fillProgram` passed (pre-T-0127 call shape) | 3.4358 |
| `fillProgram` = a real HeightField fill | 5.1547 |

**+1.72 ms, +50%, on `ShaperSolids.FillTile` itself, and ONLY when a HeightField fill is actually attached to
that Solids node** — the gate means every other Solids node in a document (the overwhelming majority, since
this is opt-in per node) pays exactly the pre-T-0127 cost, confirmed structurally by LT-24 leg 4's determinism
check and by LT-2 (zero-allocation) still passing unmodified. In context: this is Solids' OWN geometry pass,
not the whole `PaintTile` pipeline (which also runs the fill's albedo pass, the exclusivity partition,
compositing and the light law) — the four extra `ShaperFillOps.Sample` calls per covered pixel (each one
`Anchor()`'s 2×2 affine transform plus one array index) are real but bounded work, matching B9's own standard:
"the budget is generous as long as the work is not repeated needlessly," and here it is opt-in, not needless.

## Part 6 — honest scope limits

1. **IndexedStrip's height is not perturbed**, even though it varies spatially (Part 1 of SPEC.md names the
   cost reason: an accurate `edge` at four neighbour taps needs a full silhouette re-walk, unlike HeightField's
   cheap texel lookup). A general fix for every height-emitting fill kind was explicitly out of this task's
   scope and remains unbuilt.
2. **Nearest-texel sampling's step-discontinuity is inherited, not fixed** (Part 4). A bilinear HeightField
   sampling mode would likely smooth this out at extreme scale, but changing HeightField's own sampling
   convention (used by its albedo too) is a different, larger task than wiring the existing convention into the
   normal.
3. **No authored control over how strongly a HeightField fill shades a Solid, beyond the layer's shared
   `slopeGain`.** Reusing the per-layer dial (Part 2 of SPEC.md) was the deliberate, narrowest choice —
   consistent with the extrusion Profile case — but it means there is no per-Solids-node "relief strength" dial
   independent of the layer's own light-response tuning. Not requested by the task; named as a natural future
   knob if one turns out to be wanted.
4. **Not tested against every one of the 245 imported Tapestry presets** — `plates_GEN10_001` and
   `lines_GEN1_001` (the two T-0124 already found read as "etched/plate detail") were used, per the task's own
   suggestion. The other 243 were not individually verified to look good under this perturbation; nothing in
   the fix is preset-specific, but this is a real, stated coverage limit rather than an implied "all 245 work."

## Part 7 — files

**Modified:** `Assets/Packages/Laubrary/Runtime/Shaper/ShaperSolids.cs`,
`Assets/Packages/Laubrary/Runtime/Shaper/ShaperFillResolver.cs`,
`Assets/Packages/Laubrary/Runtime/Shaper/ShaperNormals.cs`,
`Assets/Packages/Laubrary/Editor/Shaper/ShaperLightAudit.cs`.
All in `D:\UNITY\Laubrary Dev - Shaper`, uncommitted.

**Attachments (this task's workspace):**
`D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0127\lt24-contact-sheet.png`,
`D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0127\lt24-extreme-zoom.png`.

Design/reasoning: `D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0127\SPEC.md`.
