# T-0111 — VERIFICATION

## Part 1 — what was built

Two new Shaper fill kinds (`ShaperFillContract.cs`: `ShaperFillKind.HeightField = 5`, `ShaperFillKind.TapestrySteel = 6`), fully wired through `ShaperFillDef` (dials), `ShaperFillCompiler` (`BakeHeightField`, `BakeTapestrySteel`), and `ShaperFillOps` (`Sample()` cases, plus a new `Quantise()` static helper — the palette-quantise stage, FC-6.9). A new runtime file `ShaperTapestryCanvas.cs` ports the position-only toroidal noise primitives from Kiln's `tapsurface/canvas.py`. A new `ShaperHeightFieldPreset` ScriptableObject wraps an imported height field. A one-shot Editor import utility converted 245 Kiln-published height fields into `.asset` presets under `Assets/Demos/ShaperDemo/TapestryHeightFields/Presets/`.

All files touched (all in `D:\UNITY\Laubrary Dev - Shaper`, none in the primary `D:\UNITY\Laubrary Dev` editor):

- `Assets/Packages/Laubrary/Runtime/Shaper/ShaperTapestryCanvas.cs` — new. Canvas primitives (todo 2).
- `Assets/Packages/Laubrary/Runtime/Shaper/ShaperHeightFieldPreset.cs` — new. Preset ScriptableObject.
- `Assets/Packages/Laubrary/Runtime/Shaper/ShaperFillContract.cs` — `HeightField`/`TapestrySteel` enum values + doc comments.
- `Assets/Packages/Laubrary/Runtime/Shaper/ShaperFillDef.cs` — new dials for both kinds + `RequiredSet()` cases.
- `Assets/Packages/Laubrary/Runtime/Shaper/ShaperFillCompiler.cs` — new `ShaperFillOp` fields + `BakeHeightField`/`BakeTapestrySteel`.
- `Assets/Packages/Laubrary/Runtime/Shaper/ShaperFillOps.cs` — new `Sample()` cases + `Quantise()` + the edge-sheet gate extended to `TapestrySteel`.
- `Assets/Packages/Laubrary/Runtime/Shaper/ShaperFillResolver.cs` — `FillKindName()` extended (cosmetic/diagnostic only).
- `Assets/Packages/Laubrary/Editor/Shaper/ShaperFillAudit.cs` — `T0111_ContactSheet` added, reusing the existing `Disc`/`Solid`/`Build`/`Paint`/`CompositeOverBackdrop`/`CompositeHeightAsGrey`/`DrawLabel` test-rig helpers already in that file.
- `Assets/Demos/ShaperDemo/TapestryHeightFields/Editor/HeightFieldImportTool.cs` — new, one-shot import utility (no `[MenuItem]`).
- `Assets/Demos/ShaperDemo/TapestryHeightFields/Editor/HeightFieldScaleProbe.cs`, `SteelQuantiseProbe.cs`, `CanvasVerifyProbe.cs` — new, verification probes (Part 2).
- `Assets/Demos/ShaperDemo/TapestryHeightFields/Editor/ShaperTapestryImport.Editor.asmdef` — new, small Editor-only asmdef scoped to just this folder (references the Shaper + ZuiRuntime runtime asmdefs) so the import/probe scripts compile without touching the shared `Laubrary.Demos.asmdef`.
- `Assets/Demos/ShaperDemo/TapestryHeightFields/Presets/*.asset` — 245 new preset assets (imported data).
- `Assets/Demos/ShaperDemo/TapestryHeightFields/manifest.tsv`, `manifest.json` — the import manifest (source ids, measured min/max, checksums).

## Part 2 — what was measured, and the actual numbers (not "verified" without a number next to it)

**Canvas primitives (todo 2).** 8 cross-language sample points (`HashCell` ×4, `WrappedValueNoiseAt` ×2, `Fbm` ×2), each computed independently in a live Python run (numpy, float64) and in the actual compiled C# via Unity CLI (`ShaperTapestryCanvas`, float32). All 8 agree to float32 precision. Full table in `TAPESTRY-SPEC.md` Part 2. The integer-hash equivalence is also derived algebraically (modular-arithmetic identity), so this is belt-and-braces, not the only evidence.

**Height field import (todo 6).** `HeightFieldImportTool.Import()`: `ok=245 fail=0 checksumFail=0 total=245`. `HeightFieldImportTool.VerifySample()` (8 presets chosen by a seeded RNG, reloaded from disk after `SaveAssets`/`Refresh`): `pass=8 fail=0`, every one SHA256-matching its manifest entry AND matching the manifest's measured min/max. Global range across all 245: measured **-1.6476733684539795 .. 2.2057981491088867** (float64 measured in Python at import time), which is the same figure the task description quoted, confirming this is the same dataset.

**`heightFieldScale` (todo 6's normalise decision, todo 1's "no auto-rescale" ruling).** `HeightFieldScaleProbe.Run()`: 12 sample points across a live-compiled `HeightField` fill at scale factors 1, -1 and 2.5. `scale=-1 mismatches: 0/12`, `scale=2.5 mismatches: 0/12` — every non-zero raw sample reproduced exactly `raw × scale`.

**Palette-quantise (todo 3).** `SteelQuantiseProbe.Run()`, first section: 9 probe values through `ShaperFillOps.Quantise(5, ...)`. `quantise mismatches: 0/9` — every output landed exactly on the 5-level grid `{0, 0.25, 0.5, 0.75, 1}`, and R/G/B stayed equal for a grey input (no per-channel drift).

**`TapestrySteel` (todo 7).** `SteelQuantiseProbe.Run()`, second section: a 441-sample sweep (21×21 grid) through a live-compiled `TapestrySteel` op. `nonFinite=0 outOfRange=0` — no NaN/Infinity, every albedo channel stayed in `[0,1]`. Rust bias: average redness (R−B) measured **0.0779** near the shape's interior versus **-0.0232** near its edge — the rust tint measurably reaches toward the interior as the Kiln reference describes ("rust growing out of the low ground"), not just present-somewhere.

**Contact sheet.** `T0111_ContactSheet()` rendered 8 cells through the real compile→sample pipeline (not a standalone harness) to `contact-sheet.png` (attached). `RESULT: PASS` — no fill degraded to its Solid fallback (which would indicate a load/read failure). Cell-by-cell claims are in `TAPESTRY-SPEC.md` Part 6; note the numeric probes above are the authoritative check for the scale-dial and rust-bias claims specifically, because a 110px thumbnail is a weak instrument for judging a photographic-negative relationship by eye — the numbers were checked directly against the sample buffer instead of trusted from the picture.

**Whole-project compile.** Checked after every source edit via the Unity CLI (`unity command recompile` + `recompile_status`, port 7801) — clean (`"failed":false,"errors":[]`) at every checkpoint, most recently after all of the above.

## Part 3 — honest, deliberate scope limits (not fixed here, not hidden)

1. **`steel_clean` was not ported at all**, and `steel` was ported to its albedo-contributing terms only, not to pixel parity with Kiln's shaded PNG output. See `TAPESTRY-SPEC.md` Part 4 for the full accounting of what was and was not ported, and why the shading half is architecturally out of a fill's jurisdiction (not just a time cut).
2. **The 3 claimed 128² height fields were not found** anywhere in the Kiln `Tapestry Shape` project; 245 at 256² is the verified real count. See `TAPESTRY-SPEC.md` Part 3.
3. **No authoring UI.** Neither new fill kind has a ZUI window to create/edit it — same limitation T-0110 shipped with for `IndexedStrip`, and for the same reason (out of this wave's scope; the fill contract and its compile/sample path is what was asked for).
4. **`HeightField`'s `heightDelta` does not yet visibly sculpt or light anything**, for the same reason T-0110's `IndexedStrip` height didn't: nothing outside the audits/probes reads `ShaperFillBuffers.height` yet (no consumer wired to shading, normals, or a cross-layer depth test). This fill populates the exact same, already-proven interface T-0110 populated — it does not regress or advance that particular limitation, it inherits it.
5. **`tapshape/canvas.py` (the Shape-side canvas primitives) was not ported.** The height-field path ships pre-baked presets, not live regeneration from an authored genome, so those primitives have no consumer in this wave. If a future task wants live-authored (not just imported) Tapestry Shape fields, that port is still open.
6. **The rust bias, grain, and base-tone terms in `TapestrySteel` are simplified relative to Kiln's `steel`**, not full ports of its multi-entry HSV palette / scratch / crater / dent systems — named explicitly in `TAPESTRY-SPEC.md` Part 4 rather than implied to be complete.
7. **No live human walkthrough of the Shaper editor UI was done**, because Shaper has no shipped editor window yet for authoring a document interactively (confirmed: `Editor/Shaper/` holds only audit/import scripts, no `EditorWindow`). Everything here was verified through the compiled fill-contract API directly (compile → sample, exactly as the shipped `ShaperFillAudit.cs` harness already does for every other fill kind) — this is the same verification depth T-0110 shipped at, not a lesser standard applied here.

## Part 4 — what this hands over as, and what is NOT yet true

**Done and verified:** the port-contract decision (todo 1), the canvas primitives (todo 2, cross-checked against Python), the palette-quantise stage (todo 3, new capability, measured), confirmation that the time-input and coordinate-space mechanisms already existed and were reused rather than rebuilt (todo 4), a height-field fill kind wired through the exact interface T-0110 proved (todo 5), a 245-preset import library with bit-exact reconstruction proven by checksum (todo 6, with the 128² discrepancy reported rather than hidden), one Surface generator's albedo-contributing terms natively ported and measured (todo 7, explicitly not full parity), the plasma ruling recorded against the design doc's own recommendation (todo 8), and this document plus the contact sheet as the adversarial pass (todo 9).

**NOT yet true, and not claimed:** no authoring UI for either fill kind; no lighting/normal consumer for height-field-sourced relief (same limitation T-0110 shipped with); no full-parity port of either Surface generator's shading; the 128² fields the task description named do not exist in the source project as far as this pass could find. Whoever picks this up next should treat Part 3 as the punch list, not as background colour.
