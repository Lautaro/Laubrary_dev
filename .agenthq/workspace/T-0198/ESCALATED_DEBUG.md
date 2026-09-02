# T-0198 — Escalated Debug: composite generators vs. canvas shape and pixel size

All six phases run. Commits `3d87f078` (fix + probe) and the cleanup commit that deletes the probe, both on `feat/shaper`.

**Headline:** the crash and the pixelSize error are found, fixed and measured. The nulled height stage is **restored but its cause is NOT established** — the undo mechanism proposed in Phase 3 failed to reproduce under test (see Phase 5).

## Phase 1 — Research (what the code actually does)

Every claim below is from source in `D:\UNITY\Laubrary Dev - Shaper`, not from a doc.

- A composite node's bake box is DERIVED from the canvas, not authored: `ShaperDocumentRenderer.RenderPhaseInto` calls `ShaperCompositeDef.FitTree(lay.root, w, h)` once per layer per frame (`ShaperDocumentRenderer.cs:287`, pre-fix), and `FitTo` set `halfExtentX = w·0.5`, `halfExtentY = h·0.5`, `bakeWidth = w`, `bakeHeight = h` (`ShaperCompositeDef.cs:247-258`, pre-fix). So on the owner's document the bake raster is **96×152** — the first non-square bake box the escape hatch has ever been handed.
- `ShaperCompiler.EmitComposite` already clamps `bw`/`bh` to ≥ 1 (`ShaperCompiler.cs:954-955`) and already tolerates a null source. Neither the clamp nor "rounding to zero" was the crash — the raster dimensions were exactly 96 and 152.
- `PyreFormCompositeSource.Render` passed those two numbers straight into `PyreFormCtx(width, height, …)` (`PyreFormCompositeSource.cs:55-57`, pre-fix).
- **`ArcBurstForm.Render` builds a SQUARE plane**: `canvas = min(ctx.W, ctx.H)`, `k = clamp(ceil(SourcePx/canvas),1,4)`, `S = canvas·k`, `n = S·S` (`ArcBurstForm.cs:454-456`). It then calls `PyreSupersample.Downsample(_pr,_pg,_pb,_pa, S, S, k, target, ctx.W, ctx.H, flipY:true)` (`ArcBurstForm.cs:520`). `Downsample` indexes `(y·k+sy)·W2 + x·k+sx` for `y < H` (`PyreSupersample.cs:80-84`), so with `W2 = S = 96k` and `H = 152` it walks past the end of a `96k × 96k` plane → `IndexOutOfRangeException` at `PyreSupersample.cs:84`. `PlasmaBloomForm.cs:469` has the identical shape. `TorchForm.cs:216` does not (it carries real `W2`/`H2`).
- The `k == 1` branch of the same method (`ArcBurstForm.cs:506-518`) does not throw but writes only an `S×S` sub-rect of a `W×H` target — the same defect showing as a silently cropped picture instead of an exception.
- `PyreLayerCompositeSource.Render` **already solved this**, and its comment states the governing fact: "Pyre's canvas is SQUARE (Pyre.cs:1259-1260 — Width and Height are both canvasSize)", so it renders at `canvas = max(width,height)` and centre-windows the result (`PyreLayerCompositeSource.cs:88-92,120-135`). `FireCompositeSource`/`FireballCompositeSource` allocate their sims at the real `width×height` and are unaffected.
- Canvas world extent is `0.5·(w−1)·pixelSize` (`ShaperDocumentRenderer.cs:237-238`), and half-extents are in canvas units. So `FitTo` ignoring `pixelSize` is a unit error, not a rounding one.
- `ShaperLayer.height` is `[SerializeReference]` (`ShaperLightRig.cs:388`). The window's `Change()` wrapper recorded undo with `Undo.RecordObject(document, …)` (`ShaperWindow.cs:1414`, pre-fix), as did five other sites in `ShaperWindow.cs` / `.Sections.cs` and `PyreLayerShaperUI.cs:185`.
- The Height card already handles `layer.height == null` correctly: it shows "This layer has no height stage, so it stays flat." plus an **Add height** button (`ShaperWindow.Sections.cs:1295-1302`), and **Remove height** sets it back to null (`:1334-1335`). No change was needed there.

## Phase 2 — Proof of control (predictions)

Written as `Assets/Packages/Laubrary/Editor/Shaper/T0198_CompositeSizeProbe.cs`, `public static string RunAll()`. **Not yet executed.**

Predictions that should REPRODUCE (pre-fix):
1. `P1` — 96×152, pixelSize 2.48778, ArcBurst composite → exception out of the render.
4. `P4` — after a render at pixelSize 2.48778 the fitted box is ~0.40 of the canvas the grid spans.
5. `P5` — a source that throws takes the whole frame down.
6. `P6` — `Undo.RecordObject` + undo nulls a `[SerializeReference]` height stage.

Predictions that should NOT reproduce (the controls):
2. `P2` — 96×96 at pixelSize 1 renders and always did; the fix must not change it.
3. `P3` — `FitTo` clamps to ≥ 1 bake texel and > 0 extent even at a degenerate canvas.
7. `P7` — a layer with `height == null` renders flat without throwing.
8. `P8` — the owner's own document, on an in-memory DUPLICATE (`Object.Instantiate`, destroyed in a `finally`, never saved), at 96×152 / 2.48778, renders with ink.

Judgment is printed per test as PASS/FAIL against a stated expectation.

## Phase 3/4 — Theory and implementation

Two independent defects plus one data-loss path; all three fixed at the boundary that owns them.

1. **Non-square canvas → `PyreFormCompositeSource` hosts every form on a square canvas of the LARGER edge and centre-windows the node's box out of it**, mirroring `PyreLayerCompositeSource`. A square canvas takes the pre-existing code path unchanged, so nothing already working moves. Chosen over patching `ArcBurstForm`/`PlasmaBloomForm` because Pyre is read-only for this programme AND because the assumption is Pyre-wide, not per-form — fixing it at the bridge covers every current and future form, including the effect-stage call sites (`ShaperEffectStageRunner.cs:51,70`, `ShaperEffectPicture.cs:72`) that go through the same source object.
2. **`FitTo(canvasWidth, canvasHeight, pixelSize)`**, and `FitTree` threads it from `doc.pixelSize`. Bake raster stays 1 texel per sample; both clamped ≥ 1.
3. **`ShaperCompiler.SafeRender`** wraps the generator call on both the single-instance and native-swarm paths: an exception clears the raster, records `ShaperCompositeDef.lastRenderError` (`[NonSerialized]`) and logs once per distinct message. `ShaperCompositeDef.FirstError(root)` is read by `DescribeTransport` (`ShaperWindow.Preview.cs`) ahead of every other transport message.
4. **`Undo.RegisterCompleteObjectUndo` everywhere the Shaper window records undo.** `RecordObject` does not snapshot the managed-reference registry, so an undo restores a document with `[SerializeReference]` fields the snapshot never held; they come back null and the next save drops the entries. That is the demonstrable path by which the demo document's height stage became `rid: -2` with its whole `references:` entry deleted (`git diff Assets/Demos/ShaperDemo/ShaperDemoDoc.asset`). It is not the ONLY possible path — clicking **Remove height** produces a byte-identical result — and the asset alone cannot tell them apart. A CLI save is ruled out for this file: that gotcha nulls *every* managed reference, and here exactly one of them was lost while every node, composite and effect survived.

## Phase 5 — Re-test (RUN; full output in `probe-results.txt`)

All nine predictions PASS on the Shaper worktree editor (`Application.dataPath` verified as `D:/UNITY/Laubrary Dev - Shaper/Assets`, `scriptCompilationFailed=False`).

The crash is reproduced and closed in the same run: the failing call itself — `Downsample(plane, S, S, k, target, 96, 152)`, exactly `ArcBurstForm.cs:520`'s shape — still raises `IndexOutOfRangeException`, while the same document rendered through the bridge does not and puts ink on the canvas (mean alpha 0.1217). The defect is untouched inside Pyre; what changed is that the bridge no longer hands a form a canvas shape it cannot represent. The pixelSize error is closed too: the fitted box is now 1.01× the canvas the grid spans, against ~0.40× before.

**P6 and P9 did NOT reproduce the undo defect.** `Undo.RecordObject` kept the height stage alive through both a simple undo and the realistic two-step sequence (create the stage in one group, undo a later edit), and a save/reload round trip on a duplicate grew the null-reference count under neither API. So the mechanism I proposed in Phase 3 is **not demonstrated**, and the cause of the demo document's nulled height stage remains **unknown**. The `RegisterCompleteObjectUndo` change stays as hardening — it is Unity's managed-reference-safe API and costs a snapshot per edit — but it must not be reported as the fix for something that was never reproduced.

## Restore (done, through the window)

`ShaperWindow.OpenFor(doc)`, then the stage re-added inside the window's own `Change()` wrapper, then `AssetDatabase.SaveAssets()`. Only layer[0] "Star" was restored: HEAD shows "Star" carrying rid `2235495212426461256` and "ArcBurst" already at `rid: -2`, so the second layer never had a height stage and giving it one would be inventing authored data. Values restored field for field from HEAD (`technique 3`, depth 10, angle 45, steps 4, curve 1, taper 1, `bevel 2`, bevelAmount 0.3, bevelSteps 4 — verified in the saved YAML). The owner's edits are intact: canvas 96×152, `pixelSize 2.48778`, `layerSpacing 0.97749`, the `zOffset 0.67545` and the `starBaseWidth` curve. `rid: -2` count is 3, matching HEAD.

`demo-doc-96x152-pixelsize2.49.png` is all sixteen frames rendered from the restored document: 46,681 inked pixels, the ArcBurst arcs sweeping the full canvas and the Star layer shaded by its restored height stage. Outputs were NOT rebaked — the bake is a separate deliverable and the sheet on disk was baked at the old square canvas, so it is now stale by the owner's own edits rather than by this task.

## Phase 6 — Smell assessment

**Keep the fix.** But it does paint over a real structural weakness, and the weakness is worth naming.

`IShaperCompositeSource` promises "render into a width×height buffer" and nine of its implementations cannot honour that for a rectangle. Nothing in the contract said the canvas had to be square, and nothing checked. T-0191 changed the bake box from an authored 128×128 default to whatever the canvas happens to be — a good change — and in doing so turned a latent assumption into a crash the first time the owner made a document taller than it was wide. The fix normalises at the bridge, which is the right place given Pyre is read-only here, but it means Shaper now silently crops a form's square picture to a rectangular box. That is a framing decision the author cannot see or control, and the honest long-term answer is either a form-side contract that states "square only" and is enforced, or forms that genuinely draw to a rectangle.

The second smell is the one the probe found by failing to reproduce: **authored data disappeared from the owner's document and nobody can say how.** Two plausible paths (an undo, a "Remove height" click) are byte-identical in the saved asset, and the asset carries no history that could separate them. A tool whose data can vanish without a trace is a tool that will lose data again. Cheap mitigations exist and none are built — the window could refuse to drop a height stage without a confirm the way `AssetKit` already gates Delete, or the document could keep a small authored-stage census that a load can compare against.

## Open / not done yet

- Phases 2 and 5 (run the probe before and after) — awaiting editor rights.
- Restoring the demo document (keep canvas 96×152, pixelSize 2.48778, layerSpacing 0.97749, zOffset 0.67545 and the `starBaseWidth` curve; restore the `ShaperHeightDef` from HEAD: `technique 3`, depth 10, angle 45, steps 4, curve 1, taper 1, bevel 2, bevelAmount 0.3, bevelSteps 4) — **through the window**, never a CLI save.
- The `.meta` for the probe file does not exist yet; Unity mints it on the first import after the grant.
- Not covered by the exception guard: the effect-stage runner's own `source.Render` calls. The square-canvas fix removes the known failure there, but a throwing effect source still escapes. Named, not fixed — it is a different surface and another wave's file.
