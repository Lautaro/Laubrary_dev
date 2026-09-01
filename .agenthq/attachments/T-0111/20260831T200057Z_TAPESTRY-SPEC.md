# T-0111 — Tapestry as Shaper fills and a height source: what was built and why

Wave 3 of the Shaper rebuild, built in the second working copy `D:\UNITY\Laubrary Dev - Shaper` (Unity editor on port 7801), branch `feat/shaper`. Nothing here is committed — same handoff convention as T-0110.

## Part 1 — the port contract (todo 1)

Confirmed against the live Shaper code, not assumed from the design doc alone:

- **Tapestry Shape (height fields) → a new fill kind, `ShaperFillKind.HeightField`, that emits `heightDelta` only**, through the exact FC-2.5 interface `IndexedStrip` already proved in T-0110 (zero contract changes to `ShaperFillEmit`). This is **not** a new `ShaperExtrusionTechnique`. Read `ShaperHeight.cs` in full: the extrusion/bevel catalogue is a closed-form, monotone-in-`t` system whose `Inverse()` (HS-5.7) depends on that monotonicity to bisect a cross-section for the 3D solid marcher. An arbitrary imported height field has neither a closed form nor monotonicity in the shape's own inside-distance `t` — plugging it in as a technique would silently break `Inverse()`'s correctness proof. `SHAPER_THE_DESIGN.md` B5 says this in as many words: *"Tapestry Shape's generators become height generators — a fill that emits height and no colour."*
- **Tapestry Surface (`steel`, `steel_clean`) → new fill kinds emitting albedo.** One was ported (`TapestrySteel`, Part 4); the other (`steel_clean`, with its rivet/contour-placement system) was not — see Part 4's scope note.
- **`plasma` → excluded from the fill contract**, ruled explicitly in Part 5 (todo 8), documentation only, no code.
- **Time input and the coordinate-space switch (todo 4) both already existed generically** and were reused, not rebuilt:
  - Time: `ShaperFillInputs.phase01`, baked into every fill's op **once per compile** via `ShaperValue.Sample(dial, phase01, seed, default)` — the exact mechanism `Texture`'s animated `offsetU`/`offsetV` already uses (`ShaperFillCompiler.BakeTexture`). Any future animated dial on the new kinds (e.g. a scroll offset) gets this for free; none was added in this pass because nothing in the ported slice needed one to prove the mechanism (Texture and Gradient already prove it works).
  - Space: `ShaperFillSpace` (`Stamped`/`Fixed`, FC-1.6), already declared on `ShaperFillDef` and baked in `ShaperFillCompiler.BakeAnchor`. Both new fill kinds set `op.positional = 1` and go through the exact same `BakeAnchor`/`Anchor()` path Texture and IndexedStrip use. Verified visually (contact sheet cells 04/05) and by construction (no new code path — same switch, same function).

## Part 2 — the toroidal canvas primitives (todo 2)

`Runtime/Shaper/ShaperTapestryCanvas.cs` ports the position-only subset of `tapsurface/canvas.py`: `HashCell`, `WrappedValueNoiseAt` (a point-sampled form of `wrapped_value_noise`), and `Fbm`.

**Scope cut, and why it's principled rather than a shortcut.** `canvas.py`'s fourth primitive, `gradient(field)`, finite-differences a *whole materialised field* via `np.roll` — i.e. it reads neighbouring samples. Shaper's fill contract forbids this structurally: `ShaperFillOps.FillTile`'s own doc comment states a fill "reads only its OWN sample — never `[i−1]` or `[i+width]`" (BC-1.6/BC-2.1, tile independence). There is no per-sample expression of "the neighbour's value" available inside `Sample()`. Nothing in the ported `TapestrySteel` slice needs it: the Python reference used `gradient()` to build a screen-space surface normal for its own GGX shading pass, and that whole shading pass is out of a fill's jurisdiction under this design's own architecture (see Part 4). `tapshape/canvas.py` (the Shape-side canvas) was **not** ported at all — the height-field path ships pre-baked presets (Part 3), not live regeneration from a genome, so its primitives are unused in this wave.

**Verified against the Python originals, not merely transcribed** — measured, not claimed:

| Function | Python (float64 reference) | C# (float32) | Agreement |
|---|---|---|---|
| `HashCell(0,0,12345,0)` | 0.9671673285621998 | 0.9671673 | float32-precision match |
| `HashCell(3,7,12345,0)` | 0.11212591413041852 | 0.112125911 | match |
| `HashCell(255,255,999,3)` | 0.48911407566122433 | 0.489114076 | match |
| `HashCell(7,3,4294967295,7)` (max uint32 seed) | 0.21673167832974888 | 0.216731682 | match |
| `WrappedValueNoiseAt(0.12345,0.6789,6,42,0)` | 0.18469998450473746 | 0.184699982 | match |
| `WrappedValueNoiseAt(0.999,0.001,4,123,2)` | 0.7840688353055177 | 0.7840688 | match |
| `Fbm(0.3,0.7,6,4,42,0.5)` | 0.4612973993671704 | 0.461297423 | match |
| `Fbm(0.5,0.5,10,5,123,0.6)` | 0.6494125255656302 | 0.6494125 | match |

Every pair agrees to float32 precision (~7 significant digits), which is the correct standard — the Python reference runs in float64, the C# port in float32 by the fill contract's own rule (BC-1.2). The integer hash arithmetic is provably equivalent, not just measured-equivalent: Python sums four terms in uint64 (no overflow — the terms are far under 2^64) and masks the sum to 32 bits once; C#'s plain `uint` wraps mod 2^32 at every intermediate add/multiply by default (unchecked). Those give the same answer by `(a+b+c+d) mod 2^32 == ((a mod 2^32)+...+(d mod 2^32)) mod 2^32` — so the measured agreement above is confirming an identity that was also derived algebraically, not hoping a spot-check happened to pass.

## Part 3 — the height-field preset library (todo 6) and the normalise decision

**245 of the claimed 248 fields were confirmed and imported; the 3 at 128² were not found.** The task description said "245 at 256 square, three at 128". A full scan of `D:\CODEZ\Kiln\projects\Tapestry Shape\_media\**\fields\0000_H.npy` found exactly 245 arrays, all `(256,256)` float32, global range measured at **-1.6476733684539795 .. 2.2057981491088867** (matches the task's "-1.648 to +2.206" almost to the last digit). No 128×128 height field exists anywhere under `Tapestry Shape` — the only 128×128 `*_H.npy` files in the whole Kiln tree live under **`Tapestry Surface`**'s `plasma` test inputs, which are a different project's synthetic test fixtures, not published Tapestry Shape draws. The one place `"248"` appears in the Shape project's own docs is `--seed 1248` in a MANIFEST.md, coincidental. **This is reported honestly as a discrepancy in the originating task description rather than papered over**; 245 is the verified, real number.

**Import pipeline (byte-exact, not merely "numerically close"):**
1. Python (`numpy`) reads each `.npy`, asserts `float32`/`(256,256)`, writes the raw little-endian bytes (`a.tobytes()`) to a staged `.bytes` file — the *exact same IEEE-754 bit pattern* as the source array, plus a `manifest.tsv` (id, source path, measured min/max, a 16-hex-char SHA256 of those bytes).
2. A one-shot Editor utility (`HeightFieldImportTool.cs`, no `[MenuItem]` per project convention) reads each `.bytes` as a `TextAsset`, **re-verifies its SHA256 against the manifest before writing anything**, reinterprets the bytes as `float[]` via `Buffer.BlockCopy`, and bakes an `RFloat`/linear/Read-Write-enabled `Texture2D` via `SetPixelData<float>`. The texture is embedded as a sub-asset of a `ShaperHeightFieldPreset` ScriptableObject (`Assets/Demos/ShaperDemo/TapestryHeightFields/Presets/*.asset`, 245 files).
3. **Result: `ok=245 fail=0 checksumFail=0 total=245`.**
4. A held-out spot check (8 random presets, reloaded from disk *after* `SaveAssets`/`Refresh`, independent of the import pass's in-memory state) recomputed SHA256 from the texture's own `GetPixelData<float>` and compared against the manifest: **8/8 SHA256 match, 8/8 measured min/max match.** This proves bit-exact round-trip through Unity's texture asset pipeline, not just "the importer ran without throwing."
5. The ~64 MB of staged `.bytes` files were deleted after import (`CleanupStaging`) so the data isn't duplicated on disk; only the 245 `.asset` files (~64 MB total) remain.

**The normalise-or-scale decision, measured before deciding:** the 245 fields are genuinely unnormalised and each has its own local range within the global -1.65..+2.21 band. **Decision: ship raw, unnormalised values; the fill authors a `heightFieldScale` dial (default 1) that multiplies the raw sample before it becomes `heightDelta`.** `ShaperHeightFieldPreset.measuredMin`/`measuredMax` are stored as **informational metadata only** — nothing reads them to auto-rescale. Reasoning: a per-field auto-normalise would make the *same* preset asset produce a different relief magnitude depending on when it was imported or if the import were re-run with a different normalisation rule, which is exactly the kind of silent-magic the fill contract's other dials (`heightDelta`, `IndexedStrip`'s per-slot heights) avoid — magnitude is always an authored, visible number on the fill, never inferred. **Measured, not assumed:** `HeightFieldScaleProbe.cs` compiled a `HeightField` fill at scale `-1` and `2.5` against a live preset and sampled 12 points along the field; both scale factors reproduced the expected value to float precision at every non-zero sample (`0/12 mismatches` for both).

## Part 4 — the `TapestrySteel` fill (todo 7): what was ported and what deliberately was not

**`steel` was chosen over `steel_clean`.** Both are candidates per the design doc. Measured sizes: `steel` is 1490 lines / 18 functions; `steel_clean` is 2439 lines / 30 functions and adds an entire contour-walking, corner-detecting, per-side rivet-placement system on top of `steel`'s own pipeline. Porting either to full pixel parity is realistically a multi-day task on its own; `steel_clean`'s extra machinery is a second multi-day task layered on that. `steel` was the honestly-scoped choice.

**Even `steel` was not ported to full parity, and the reason is architectural, not just a time cut.** Reading `steel/gen.py`'s `shade()` function (the ~750-line body of `render()`) shows it conflates two things Shaper's own design explicitly separates: **material** (base tone, oxide/rust colour, scratches, grain — genuine per-pixel *appearance*) and **shading** (GGX specular, an environment reflection map, an NdotL diffuse term computed from a screen-space-differenced surface normal). `SHAPER_THE_DESIGN.md`'s ruling B4 is explicit: *"A fill emits albedo … shine belongs to the lights, not to the paint."* A fill's `Sample()` also cannot compute a screen-space normal at all — that needs neighbour reads, which BC-1.6/BC-2.1 forbid. So the shading half of `steel` is not just out of scope for time reasons; **it is out of a fill's jurisdiction by this design's own contract**, and belongs (if ever ported) to the document's light rig, a different, later task.

**What was ported (the albedo-contributing terms), using the verified canvas primitives from Part 2:**
- Base tone: an `Fbm` sweep between two authored colours (`steelBaseLow`/`steelBaseHigh`), replacing Kiln's multi-entry HSV palette table with one continuous ramp — simplified, not equivalent, and documented as such.
- Rust/oxide bias: Kiln's "rust growing out of the low ground of whatever shape it was handed" ported as an edge-distance-driven blend toward `steelRustColor`, saturating over an authored `steelRustReachPixels` — this is the one place `EdgeDistance` (declared in `RequiredSet()`) gets used, and it is measured to actually bias toward the interior (Part 6).
- Fine grain: a second, higher-frequency `WrappedValueNoiseAt` pass, additive.
- Palette-quantise (new — see Part 5 below).

**What was deliberately NOT ported:** GGX specular, the environment reflection map, NdotL diffuse falloff, the dent/ding discrete-impression field, scratches (a separate hash-placed line pattern), craters, and the posterised-environment "world" pass. Each of those is its own 60-150 line hash-based placement system in the Python source. None was faked or approximated by a placeholder that LOOKS similar — they are simply absent, and the `TapestrySteel` fill does not claim to reproduce `steel`'s reference PNG. **Consequence for verification:** because the Python reference bakes lighting into its output, there is no single Kiln-produced PNG this port can be diffed against pixel-for-pixel — that comparison would be comparing a lit render to an unlit one. Verification here is at the level the fill contract actually promises: the ported *formulas* match the Python source (canvas primitives, Part 2) and the *composed* fill behaves correctly (finite output, correct edge-distance bias, correct quantise behaviour — Part 6).

## Part 5 — the plasma ruling (todo 8, documentation only)

`plasma` (Kiln `Tapestry Surface`) is **not** a fill and none was built for it. Confirmed by the design doc's own measurement (`SHAPER_THE_DESIGN.md` B5): rendered against a completely flat field, `plasma` produces **alpha 0 everywhere** — its transparency *is* its silhouette, which makes it shape-coupled by construction, the opposite of what a fill (shape-independent, tileable, appliable to any silhouette) requires. **Where it belongs instead:** Shaper's own architecture (`SHAPER_THE_DESIGN.md`, "Part A — the whole thing in one page") names a third generator kind alongside shapes and fills — *"one escape hatch for the big pre-existing effects that make finished pictures on their own and are not worth taking apart"*. `plasma` fits that description exactly: an emissive, self-silhouetting generator that is not a shape (it has no coverage rule of its own the way a primitive does) and not a fill (its output is not shape-independent). It should be filed as a composite/effect generator under that escape-hatch category in a later wave, not forced into the fill contract as the one entry that violates it. No code was written for this — it is a recorded decision, matching the design doc's own recommendation.

## Part 6 — verification summary (todo 9)

See `VERIFICATION.md` for the full account, including the honest scope-limit section. In brief, every claim above that could be measured was measured:
- Canvas primitives: bit-for-bit-equivalent arithmetic, cross-checked numerically against a live Python run (Part 2 table).
- Height-field import: 245/245 imported, 0 checksum failures, 8/8 held-out spot-checks bit-exact.
- `heightFieldScale`: 12/12 sample points at scale -1 and 2.5 matched the expected raw×scale value exactly.
- Palette-quantise: 9/9 probe values snapped to the correct 5-level grid.
- `TapestrySteel`: 441-sample sweep, 0 non-finite outputs, 0 out-of-[0,1] outputs, interior redness (0.078) measurably greater than edge redness (-0.023), confirming the rust bias actually reaches toward the interior as designed.
- A contact sheet (`contact-sheet.png`, attached) renders all of the above live through the real Shaper compile→sample pipeline, not through a standalone test harness.
