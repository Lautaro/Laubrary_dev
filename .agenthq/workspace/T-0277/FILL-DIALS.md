# T-0277 — the dead fill dials: what each one is, and what was done

Every verdict below was measured, on documents built the way the window builds them, written to disk,
re-imported with `ForceUpdate` and re-loaded before a single measurement. `dead state` is the state
T-0265's sweep measured the dial in; `live state` is the state the engine source says it needs.

| dial | verdict | dead state (px) | live state (px) | what was done | engine evidence |
|---|---|---|---|---|---|
| `fit` (Fit) | **live, conditional** | square ellipse 34x34: **0**; Solid fill: **0** | wide ellipse 34x13: **968** | greyed for every kind that never reads the anchor (Solid, Ramp, Over phase, Gradient/from-the-edge-inwards); tooltip now says it only differs on a shape that is not square | `ShaperFillCompiler.cs:450-459` (Fit sets the anchor divisors and nothing else), `:317-324` (`op.positional` — which kinds read the anchor) |
| `quantiseLevels` (Posterise) | **wired but broken — FIXED** | Gradient **0**, Texture **0** | Steel **1032** | the dial was sampled inside `BakeTapestrySteel` alone and applied inside the Steel branch alone, so on eight of nine kinds it was never even compiled. Sampling moved into the shared block, application moved after the per-kind switch. Now: Gradient **1032**, Texture **214**, Steel **1032** (unchanged) | `ShaperFillCompiler.cs:307-317` (new), `ShaperFillOps.cs:154-171` (new) |
| `textureTilesX` / `textureTilesY` | **live, conditional** | Mapping = Fit once: **0** / **0** | Mapping = Repeat: **819** / **398** | greyed unless Mapping is Repeat, with the reason | `ShaperFillOps.cs` Texture case — `tilesX/tilesY` appear only in the `Tiled` branch; `Fitted` maps the box to 0..1 once |
| `textureAnimated` | **live, conditional** | on, grid 1x1: **0** | with Columns 4: the sheet steps | tooltip now says nothing changes until Columns or Rows is raised — a one-cell grid is the whole image | `ShaperFillCompiler.cs:649-668` — `animCellScale = 1/columns`, identity at 1x1 |
| `textureFrameColumns` / `textureFrameRows` | **live** (T-0265 measured them with the Animated toggle off) | Animated off: **0** | Animated on: **873** / **446** | nothing; already revealed only when Animated is on | `ShaperFillCompiler.cs:651-652` |
| `textureFrameCount` | **live, conditional** | grid 1x1: **0** | Columns 4, Frames 4→2: **784** | greyed while the grid is one cell, with the reason (the value is clamped to columns x rows) | `ShaperFillCompiler.cs:653-654` |
| `stripOffset` (Offset) | **live — the sweep was wrong** | offset 0→16: **0** | offset 0→0.5: **1048** | nothing to fix. The compiler wraps it (`offset - floor(offset)`), and every candidate T-0265's sweep tried (16, 1, −16, 64) is a whole number, so all four wrapped to exactly 0. Tooltip now says one whole step lands back where it started | `ShaperFillCompiler.cs:693-694` |
| `stripPlainColor` (Plain colour) | **live, conditional** | Reach 1 (default): **0** | Reach 0.2: **596** | greyed while Reach saturates, with the reason | `ShaperFillOps.cs` IndexedStrip — `edgeCoverage = clamp01(2 + edge·invReach)` is identically 1 across the whole interior at Reach 1 (T-0110's own derivation, in the code comment) |
| `stripSlots[i].height` (slot Height) | **live, conditional** | one layer: **0** | two layers: **242** | greyed while the document has fewer than two layers that paint, with the reason | `ShaperFillOps.cs` IndexedStrip → `emit.heightDelta` → `ShaperFillResolver.cs:1332` → `ShaperDocumentRenderer.cs:367` `CompositeDepth` |
| `heightDelta` (Height change) | **live, conditional** | one layer: **0** | two layers: **484** | greyed on the same test; the tooltip's claim that it is "the input the relief shading reads" was false and is gone | `ShaperNormals.cs:118-131` (LR-3.3/LR-3.4 forbid every normal provider from reading the fill height buffer), `ShaperDocumentRenderer.cs:367` is its only consumer |
| `heightFieldScale` (Scale) | **live, conditional** | primitive, one layer: **0**; primitive, two layers: **0-3** | **Solid (Pyramid), one layer: 671** | greyed unless the node is a Solid or a second layer paints. T-0265 read it as flat zero because it handed the fill an RGBA texture; the engine reads a height field with `GetPixelData<float>` and needs an **RFloat** one | `ShaperSolids.cs:833-875` (T-0127 central-differences a HeightField fill to perturb a solid's own normal — the one fill height that reaches shading), `ShaperFillCompiler.cs:775` |
| `gradientTint`, `rampTint`, `overPhaseTint`, `proceduralTint` (the four Tints) | **unreachable — control REMOVED** | ramp authored: **0** each | ramp forced null in code: **1032** each | the control is gone from Gradient, Ramp, Colour over phase and Procedural/Noise. Each colour is read by exactly one line: the degenerate path taken when that kind's gradient is **null** — and the gradient is never null (`ShaperFillDef` constructs one, and the ramp control cannot clear it), so no authoring action could make the dial act. The FIELDS stay, serialized and still read on that path. `proceduralTint` stays on Grid and Dots, where the same field is the **Ink** and is live (**188**) | `ShaperFillCompiler.cs:470/487`, `:535/558`, `:845/848`, `BakeProcedural`; defaults at `ShaperFillDef.cs:107,141,221,249` |

## Not fixed — a structural finding outside this card, and it is bigger than the dials

**On a SAVED document every child node acquires a phantom fill, and the parent's whole Fill card goes dead.**
`ShaperNode.fill` (`ShaperNode.cs:276`) is a plain `[Serializable]` field on a `[SerializeReference]` node, so
Unity materialises one on every member that has none — the same class of bug as the phantom border T-0265 fixed.
Measured on a two-member Bag: in memory the bag's own fill colour moves **1754** pixels; the same document
written to disk and re-loaded, **0** — each member is now painted by its own phantom white Solid.

It cannot be fixed the way the border was. The border had an `enabled` flag defaulting to false, so a phantom was
harmless; a fill has no such flag, nothing distinguishes a phantom from an authored plain-white fill, and turning
`fill` into a `[SerializeReference]` field would change its storage and **lose every authored fill in every
existing document**. It needs an owner decision and a migration, so it is reported rather than built.

## The fill rows of T-0265's sweep, re-run

Same method: each document built the way the window builds it, written to disk, re-imported with
`ForceUpdate` and re-loaded before a single measurement; each control perturbed in the state its own
card shows it in and the changed pixels counted at frames 0, 8 and 15. Restricted to the three node
kinds whose card shows a Fill at all - a Primitive (Star), a Solid (Pyramid) and a Bag with two real
members. Fill is ABSENT on a Composite (T-0265), so a composite has no fill row to re-run.

`before` is T-0265's own number for the same document and control; `-` means the control is not a
`ShaperFillDef` field (the Edge card's own width/alignment/joins) and was out of this task's scope.

| document | card | control | state | before | after |
|---|---|---|---|---:|---:|
| Bag (2 members) | Edge (border) | `alignment` | - | 306 | - |
| Bag (2 members) | Edge (border) | `composite` | Edge/fill/Gradient | 438 | 582 |
| Bag (2 members) | Edge (border) | `dotSize` | Edge/fill/Procedural/Dots | 13 | 20 |
| Bag (2 members) | Edge (border) | `dotStagger` | Edge/fill/Procedural/Dots | 13 | 20 |
| Bag (2 members) | Edge (border) | `enabled` | - | 86 | - |
| Bag (2 members) | Edge (border) | `fit` | Edge/fill/Gradient | 0 | 0 |
| Bag (2 members) | Edge (border) | `gradient` | Edge/fill/Gradient | 438 | 580 |
| Bag (2 members) | Edge (border) | `gradientAngleDegrees` | Edge/fill/Gradient | 426 | 559 |
| Bag (2 members) | Edge (border) | `gradientCentreX` | Edge/fill/Gradient/Radial | 420 | 566 |
| Bag (2 members) | Edge (border) | `gradientCentreY` | Edge/fill/Gradient/Radial | 420 | 566 |
| Bag (2 members) | Edge (border) | `gradientDepthPixels` | Edge/fill/Gradient/ByEdgeDistance | 364 | 440 |
| Bag (2 members) | Edge (border) | `gradientMode` | Edge/fill/Gradient | 424 | 554 |
| Bag (2 members) | Edge (border) | `gradientSize` | Edge/fill/Gradient/Radial | 438 | 582 |
| Bag (2 members) | Edge (border) | `gradientTint` | Edge/fill/Gradient | 0 | 0 |
| Bag (2 members) | Edge (border) | `gridHorizontal` | Edge/fill/Procedural/Grid | 8 | 18 |
| Bag (2 members) | Edge (border) | `gridLineWidth` | Edge/fill/Procedural/Grid | 10 | 50 |
| Bag (2 members) | Edge (border) | `gridVertical` | Edge/fill/Procedural/Grid | 14 | 28 |
| Bag (2 members) | Edge (border) | `heightDelta` | Edge/fill/Gradient | 0 | 0 |
| Bag (2 members) | Edge (border) | `heightFieldScale` | Edge/fill/HeightField | 0 | 0 |
| Bag (2 members) | Edge (border) | `heightFieldTint` | Edge/fill/HeightField | 438 | 582 |
| Bag (2 members) | Edge (border) | `joinsCoverage` | - | 0 | - |
| Bag (2 members) | Edge (border) | `kind` | Edge/fill/Gradient | 438 | 582 |
| Bag (2 members) | Edge (border) | `noiseKind` | Edge/fill/Procedural | 434 | 578 |
| Bag (2 members) | Edge (border) | `overPhaseGradient` | Edge/fill/OverPhase | 438 | 582 |
| Bag (2 members) | Edge (border) | `overPhaseTint` | Edge/fill/OverPhase | 0 | 0 |
| Bag (2 members) | Edge (border) | `proceduralAngleDegrees` | Edge/fill/Procedural | 431 | 571 |
| Bag (2 members) | Edge (border) | `proceduralGradient` | Edge/fill/Procedural | 437 | 581 |
| Bag (2 members) | Edge (border) | `proceduralKind` | Edge/fill/Procedural | 438 | 579 |
| Bag (2 members) | Edge (border) | `proceduralOffsetU` | Edge/fill/Procedural | 432 | 564 |
| Bag (2 members) | Edge (border) | `proceduralOffsetV` | Edge/fill/Procedural | 429 | 564 |
| Bag (2 members) | Edge (border) | `proceduralScale` | Edge/fill/Procedural | 434 | 574 |
| Bag (2 members) | Edge (border) | `proceduralTint` | Edge/fill/Procedural | 0 | 0 |
| Bag (2 members) | Edge (border) | `quantiseLevels` | Edge/fill/Gradient | 0 | 558 |
| Bag (2 members) | Edge (border) | `rampGradient` | Edge/fill/RampByQuantity | 154 | 262 |
| Bag (2 members) | Edge (border) | `rampInputHigh` | Edge/fill/RampByQuantity | 154 | 262 |
| Bag (2 members) | Edge (border) | `rampInputLow` | Edge/fill/RampByQuantity | 154 | 262 |
| Bag (2 members) | Edge (border) | `rampQuantity` | Edge/fill/RampByQuantity | 438 | 582 |
| Bag (2 members) | Edge (border) | `rampTint` | Edge/fill/RampByQuantity | 0 | 0 |
| Bag (2 members) | Edge (border) | `solidColor` | Edge/fill/Solid | 438 | 582 |
| Bag (2 members) | Edge (border) | `space` | Edge/fill/Gradient | 434 | 564 |
| Bag (2 members) | Edge (border) | `steelBaseHigh` | Edge/fill/TapestrySteel | 438 | 582 |
| Bag (2 members) | Edge (border) | `steelBaseLow` | Edge/fill/TapestrySteel | 438 | 581 |
| Bag (2 members) | Edge (border) | `steelCells` | Edge/fill/TapestrySteel | 420 | 564 |
| Bag (2 members) | Edge (border) | `steelGrain` | Edge/fill/TapestrySteel | 369 | 495 |
| Bag (2 members) | Edge (border) | `steelOctaves` | Edge/fill/TapestrySteel | 400 | 538 |
| Bag (2 members) | Edge (border) | `steelRustAmount` | Edge/fill/TapestrySteel | 118 | 140 |
| Bag (2 members) | Edge (border) | `steelRustColor` | Edge/fill/TapestrySteel | 306 | 379 |
| Bag (2 members) | Edge (border) | `steelRustReachPixels` | Edge/fill/TapestrySteel | 364 | 440 |
| Bag (2 members) | Edge (border) | `steelSeed` | Edge/fill/TapestrySteel | 414 | 547 |
| Bag (2 members) | Edge (border) | `stripOffset` | Edge/fill/IndexedStrip | 0 | 0 |
| Bag (2 members) | Edge (border) | `stripOrientationDegrees` | Edge/fill/IndexedStrip | 41 | 56 |
| Bag (2 members) | Edge (border) | `stripParameterisation` | Edge/fill/IndexedStrip | 219 | 291 |
| Bag (2 members) | Edge (border) | `stripPlainColor` | Edge/fill/IndexedStrip | 0 | 0 |
| Bag (2 members) | Edge (border) | `stripReach` | Edge/fill/IndexedStrip | 364 | 440 |
| Bag (2 members) | Edge (border) | `stripRepeats` | Edge/fill/IndexedStrip | 214 | 292 |
| Bag (2 members) | Edge (border) | `stripSlots[0].color` | Edge/fill/IndexedStrip | 219 | 291 |
| Bag (2 members) | Edge (border) | `stripSlots[0].height` | Edge/fill/IndexedStrip | 0 | 0 |
| Bag (2 members) | Edge (border) | `stripSlots[1].color` | Edge/fill/IndexedStrip | 219 | 291 |
| Bag (2 members) | Edge (border) | `stripSlots[1].height` | Edge/fill/IndexedStrip | 0 | 0 |
| Bag (2 members) | Edge (border) | `textureAngleDegrees` | Edge/fill/Texture | 181 | 283 |
| Bag (2 members) | Edge (border) | `textureAnimated` | Edge/fill/Texture | 0 | 0 |
| Bag (2 members) | Edge (border) | `textureFrameColumns` | Edge/fill/Texture | 0 | 0 |
| Bag (2 members) | Edge (border) | `textureFrameCount` | Edge/fill/Texture | 0 | 0 |
| Bag (2 members) | Edge (border) | `textureFrameRows` | Edge/fill/Texture | 0 | 0 |
| Bag (2 members) | Edge (border) | `textureMapping` | Edge/fill/Texture | 160 | 236 |
| Bag (2 members) | Edge (border) | `textureOffsetU` | Edge/fill/Texture | 214 | 284 |
| Bag (2 members) | Edge (border) | `textureOffsetV` | Edge/fill/Texture | 201 | 291 |
| Bag (2 members) | Edge (border) | `textureTilesX` | Edge/fill/Texture | 0 | 0 |
| Bag (2 members) | Edge (border) | `textureTilesY` | Edge/fill/Texture | 0 | 0 |
| Bag (2 members) | Edge (border) | `textureTint` | Edge/fill/Texture | 438 | 582 |
| Bag (2 members) | Edge (border) | `veil` | Edge/fill/Gradient | 438 | 582 |
| Bag (2 members) | Edge (border) | `width` | - | 86 | - |
| Bag (2 members) | Fill | `composite` | Fill/Gradient | 0 | 0 |
| Bag (2 members) | Fill | `dotSize` | Fill/Procedural/Dots | 0 | 0 |
| Bag (2 members) | Fill | `dotStagger` | Fill/Procedural/Dots | 0 | 0 |
| Bag (2 members) | Fill | `fit` | Fill/Gradient | 0 | 0 |
| Bag (2 members) | Fill | `gradient` | Fill/Gradient | 0 | 0 |
| Bag (2 members) | Fill | `gradientAngleDegrees` | Fill/Gradient | 0 | 0 |
| Bag (2 members) | Fill | `gradientCentreX` | Fill/Gradient/Radial | 0 | 0 |
| Bag (2 members) | Fill | `gradientCentreY` | Fill/Gradient/Radial | 0 | 0 |
| Bag (2 members) | Fill | `gradientDepthPixels` | Fill/Gradient/ByEdgeDistance | 0 | 0 |
| Bag (2 members) | Fill | `gradientMode` | Fill/Gradient | 0 | 0 |
| Bag (2 members) | Fill | `gradientSize` | Fill/Gradient/Radial | 0 | 0 |
| Bag (2 members) | Fill | `gradientTint` | Fill/Gradient | 0 | 0 |
| Bag (2 members) | Fill | `gridHorizontal` | Fill/Procedural/Grid | 0 | 0 |
| Bag (2 members) | Fill | `gridLineWidth` | Fill/Procedural/Grid | 0 | 0 |
| Bag (2 members) | Fill | `gridVertical` | Fill/Procedural/Grid | 0 | 0 |
| Bag (2 members) | Fill | `heightDelta` | Fill/Gradient | 0 | 0 |
| Bag (2 members) | Fill | `heightFieldScale` | Fill/HeightField | 0 | 0 |
| Bag (2 members) | Fill | `heightFieldTint` | Fill/HeightField | 0 | 0 |
| Bag (2 members) | Fill | `kind` | Fill/Gradient | 0 | 0 |
| Bag (2 members) | Fill | `noiseKind` | Fill/Procedural | 0 | 0 |
| Bag (2 members) | Fill | `overPhaseGradient` | Fill/OverPhase | 0 | 0 |
| Bag (2 members) | Fill | `overPhaseTint` | Fill/OverPhase | 0 | 0 |
| Bag (2 members) | Fill | `proceduralAngleDegrees` | Fill/Procedural | 0 | 0 |
| Bag (2 members) | Fill | `proceduralGradient` | Fill/Procedural | 0 | 0 |
| Bag (2 members) | Fill | `proceduralKind` | Fill/Procedural | 0 | 0 |
| Bag (2 members) | Fill | `proceduralOffsetU` | Fill/Procedural | 0 | 0 |
| Bag (2 members) | Fill | `proceduralOffsetV` | Fill/Procedural | 0 | 0 |
| Bag (2 members) | Fill | `proceduralScale` | Fill/Procedural | 0 | 0 |
| Bag (2 members) | Fill | `proceduralTint` | Fill/Procedural | 0 | 0 |
| Bag (2 members) | Fill | `quantiseLevels` | Fill/Gradient | 0 | 0 |
| Bag (2 members) | Fill | `rampGradient` | Fill/RampByQuantity | 0 | 0 |
| Bag (2 members) | Fill | `rampInputHigh` | Fill/RampByQuantity | 0 | 0 |
| Bag (2 members) | Fill | `rampInputLow` | Fill/RampByQuantity | 0 | 0 |
| Bag (2 members) | Fill | `rampQuantity` | Fill/RampByQuantity | 0 | 0 |
| Bag (2 members) | Fill | `rampTint` | Fill/RampByQuantity | 0 | 0 |
| Bag (2 members) | Fill | `solidColor` | Fill/Solid | 0 | 0 |
| Bag (2 members) | Fill | `space` | Fill/Gradient | 0 | 0 |
| Bag (2 members) | Fill | `steelBaseHigh` | Fill/TapestrySteel | 0 | 0 |
| Bag (2 members) | Fill | `steelBaseLow` | Fill/TapestrySteel | 0 | 0 |
| Bag (2 members) | Fill | `steelCells` | Fill/TapestrySteel | 0 | 0 |
| Bag (2 members) | Fill | `steelGrain` | Fill/TapestrySteel | 0 | 0 |
| Bag (2 members) | Fill | `steelOctaves` | Fill/TapestrySteel | 0 | 0 |
| Bag (2 members) | Fill | `steelRustAmount` | Fill/TapestrySteel | 0 | 0 |
| Bag (2 members) | Fill | `steelRustColor` | Fill/TapestrySteel | 0 | 0 |
| Bag (2 members) | Fill | `steelRustReachPixels` | Fill/TapestrySteel | 0 | 0 |
| Bag (2 members) | Fill | `steelSeed` | Fill/TapestrySteel | 0 | 0 |
| Bag (2 members) | Fill | `stripOffset` | Fill/IndexedStrip | 0 | 0 |
| Bag (2 members) | Fill | `stripOrientationDegrees` | Fill/IndexedStrip | 0 | 0 |
| Bag (2 members) | Fill | `stripParameterisation` | Fill/IndexedStrip | 0 | 0 |
| Bag (2 members) | Fill | `stripPlainColor` | Fill/IndexedStrip | 0 | 0 |
| Bag (2 members) | Fill | `stripReach` | Fill/IndexedStrip | 0 | 0 |
| Bag (2 members) | Fill | `stripRepeats` | Fill/IndexedStrip | 0 | 0 |
| Bag (2 members) | Fill | `stripSlots[0].color` | Fill/IndexedStrip | 0 | 0 |
| Bag (2 members) | Fill | `stripSlots[0].height` | Fill/IndexedStrip | 0 | 0 |
| Bag (2 members) | Fill | `stripSlots[1].color` | Fill/IndexedStrip | 0 | 0 |
| Bag (2 members) | Fill | `stripSlots[1].height` | Fill/IndexedStrip | 0 | 0 |
| Bag (2 members) | Fill | `textureAngleDegrees` | Fill/Texture | 0 | 0 |
| Bag (2 members) | Fill | `textureAnimated` | Fill/Texture | 0 | 0 |
| Bag (2 members) | Fill | `textureFrameColumns` | Fill/Texture | 0 | 0 |
| Bag (2 members) | Fill | `textureFrameCount` | Fill/Texture | 0 | 0 |
| Bag (2 members) | Fill | `textureFrameRows` | Fill/Texture | 0 | 0 |
| Bag (2 members) | Fill | `textureMapping` | Fill/Texture | 0 | 0 |
| Bag (2 members) | Fill | `textureOffsetU` | Fill/Texture | 0 | 0 |
| Bag (2 members) | Fill | `textureOffsetV` | Fill/Texture | 0 | 0 |
| Bag (2 members) | Fill | `textureTilesX` | Fill/Texture | 0 | 0 |
| Bag (2 members) | Fill | `textureTilesY` | Fill/Texture | 0 | 0 |
| Bag (2 members) | Fill | `textureTint` | Fill/Texture | 0 | 0 |
| Bag (2 members) | Fill | `veil` | Fill/Gradient | 0 | 0 |
| Pyramid | Edge (border) | `alignment` | - | 0 | - |
| Pyramid | Edge (border) | `composite` | Edge/fill/Gradient | 0 | 0 |
| Pyramid | Edge (border) | `dotSize` | Edge/fill/Procedural/Dots | 0 | 0 |
| Pyramid | Edge (border) | `dotStagger` | Edge/fill/Procedural/Dots | 0 | 0 |
| Pyramid | Edge (border) | `enabled` | - | 0 | - |
| Pyramid | Edge (border) | `fit` | Edge/fill/Gradient | 0 | 0 |
| Pyramid | Edge (border) | `gradient` | Edge/fill/Gradient | 0 | 0 |
| Pyramid | Edge (border) | `gradientAngleDegrees` | Edge/fill/Gradient | 0 | 0 |
| Pyramid | Edge (border) | `gradientCentreX` | Edge/fill/Gradient/Radial | 0 | 0 |
| Pyramid | Edge (border) | `gradientCentreY` | Edge/fill/Gradient/Radial | 0 | 0 |
| Pyramid | Edge (border) | `gradientDepthPixels` | Edge/fill/Gradient/ByEdgeDistance | 0 | 0 |
| Pyramid | Edge (border) | `gradientMode` | Edge/fill/Gradient | 0 | 0 |
| Pyramid | Edge (border) | `gradientSize` | Edge/fill/Gradient/Radial | 0 | 0 |
| Pyramid | Edge (border) | `gradientTint` | Edge/fill/Gradient | 0 | 0 |
| Pyramid | Edge (border) | `gridHorizontal` | Edge/fill/Procedural/Grid | 0 | 0 |
| Pyramid | Edge (border) | `gridLineWidth` | Edge/fill/Procedural/Grid | 0 | 0 |
| Pyramid | Edge (border) | `gridVertical` | Edge/fill/Procedural/Grid | 0 | 0 |
| Pyramid | Edge (border) | `heightDelta` | Edge/fill/Gradient | 0 | 0 |
| Pyramid | Edge (border) | `heightFieldScale` | Edge/fill/HeightField | 0 | 0 |
| Pyramid | Edge (border) | `heightFieldTint` | Edge/fill/HeightField | 0 | 0 |
| Pyramid | Edge (border) | `joinsCoverage` | - | 0 | - |
| Pyramid | Edge (border) | `kind` | Edge/fill/Gradient | 0 | 0 |
| Pyramid | Edge (border) | `noiseKind` | Edge/fill/Procedural | 0 | 0 |
| Pyramid | Edge (border) | `overPhaseGradient` | Edge/fill/OverPhase | 0 | 0 |
| Pyramid | Edge (border) | `overPhaseTint` | Edge/fill/OverPhase | 0 | 0 |
| Pyramid | Edge (border) | `proceduralAngleDegrees` | Edge/fill/Procedural | 0 | 0 |
| Pyramid | Edge (border) | `proceduralGradient` | Edge/fill/Procedural | 0 | 0 |
| Pyramid | Edge (border) | `proceduralKind` | Edge/fill/Procedural | 0 | 0 |
| Pyramid | Edge (border) | `proceduralOffsetU` | Edge/fill/Procedural | 0 | 0 |
| Pyramid | Edge (border) | `proceduralOffsetV` | Edge/fill/Procedural | 0 | 0 |
| Pyramid | Edge (border) | `proceduralScale` | Edge/fill/Procedural | 0 | 0 |
| Pyramid | Edge (border) | `proceduralTint` | Edge/fill/Procedural | 0 | 0 |
| Pyramid | Edge (border) | `quantiseLevels` | Edge/fill/Gradient | 0 | 0 |
| Pyramid | Edge (border) | `rampGradient` | Edge/fill/RampByQuantity | 0 | 0 |
| Pyramid | Edge (border) | `rampInputHigh` | Edge/fill/RampByQuantity | 0 | 0 |
| Pyramid | Edge (border) | `rampInputLow` | Edge/fill/RampByQuantity | 0 | 0 |
| Pyramid | Edge (border) | `rampQuantity` | Edge/fill/RampByQuantity | 0 | 0 |
| Pyramid | Edge (border) | `rampTint` | Edge/fill/RampByQuantity | 0 | 0 |
| Pyramid | Edge (border) | `solidColor` | Edge/fill/Solid | 0 | 0 |
| Pyramid | Edge (border) | `space` | Edge/fill/Gradient | 0 | 0 |
| Pyramid | Edge (border) | `steelBaseHigh` | Edge/fill/TapestrySteel | 0 | 0 |
| Pyramid | Edge (border) | `steelBaseLow` | Edge/fill/TapestrySteel | 0 | 0 |
| Pyramid | Edge (border) | `steelCells` | Edge/fill/TapestrySteel | 0 | 0 |
| Pyramid | Edge (border) | `steelGrain` | Edge/fill/TapestrySteel | 0 | 0 |
| Pyramid | Edge (border) | `steelOctaves` | Edge/fill/TapestrySteel | 0 | 0 |
| Pyramid | Edge (border) | `steelRustAmount` | Edge/fill/TapestrySteel | 0 | 0 |
| Pyramid | Edge (border) | `steelRustColor` | Edge/fill/TapestrySteel | 0 | 0 |
| Pyramid | Edge (border) | `steelRustReachPixels` | Edge/fill/TapestrySteel | 0 | 0 |
| Pyramid | Edge (border) | `steelSeed` | Edge/fill/TapestrySteel | 0 | 0 |
| Pyramid | Edge (border) | `stripOffset` | Edge/fill/IndexedStrip | 0 | 0 |
| Pyramid | Edge (border) | `stripOrientationDegrees` | Edge/fill/IndexedStrip | 0 | 0 |
| Pyramid | Edge (border) | `stripParameterisation` | Edge/fill/IndexedStrip | 0 | 0 |
| Pyramid | Edge (border) | `stripPlainColor` | Edge/fill/IndexedStrip | 0 | 0 |
| Pyramid | Edge (border) | `stripReach` | Edge/fill/IndexedStrip | 0 | 0 |
| Pyramid | Edge (border) | `stripRepeats` | Edge/fill/IndexedStrip | 0 | 0 |
| Pyramid | Edge (border) | `stripSlots[0].color` | Edge/fill/IndexedStrip | 0 | 0 |
| Pyramid | Edge (border) | `stripSlots[0].height` | Edge/fill/IndexedStrip | 0 | 0 |
| Pyramid | Edge (border) | `stripSlots[1].color` | Edge/fill/IndexedStrip | 0 | 0 |
| Pyramid | Edge (border) | `stripSlots[1].height` | Edge/fill/IndexedStrip | 0 | 0 |
| Pyramid | Edge (border) | `textureAngleDegrees` | Edge/fill/Texture | 0 | 0 |
| Pyramid | Edge (border) | `textureAnimated` | Edge/fill/Texture | 0 | 0 |
| Pyramid | Edge (border) | `textureFrameColumns` | Edge/fill/Texture | 0 | 0 |
| Pyramid | Edge (border) | `textureFrameCount` | Edge/fill/Texture | 0 | 0 |
| Pyramid | Edge (border) | `textureFrameRows` | Edge/fill/Texture | 0 | 0 |
| Pyramid | Edge (border) | `textureMapping` | Edge/fill/Texture | 0 | 0 |
| Pyramid | Edge (border) | `textureOffsetU` | Edge/fill/Texture | 0 | 0 |
| Pyramid | Edge (border) | `textureOffsetV` | Edge/fill/Texture | 0 | 0 |
| Pyramid | Edge (border) | `textureTilesX` | Edge/fill/Texture | 0 | 0 |
| Pyramid | Edge (border) | `textureTilesY` | Edge/fill/Texture | 0 | 0 |
| Pyramid | Edge (border) | `textureTint` | Edge/fill/Texture | 0 | 0 |
| Pyramid | Edge (border) | `veil` | Edge/fill/Gradient | 0 | 0 |
| Pyramid | Edge (border) | `width` | - | 0 | - |
| Pyramid | Fill | `composite` | Fill/Gradient | 1584 | 900 |
| Pyramid | Fill | `dotSize` | Fill/Procedural/Dots | 394 | 124 |
| Pyramid | Fill | `dotStagger` | Fill/Procedural/Dots | 464 | 156 |
| Pyramid | Fill | `fit` | Fill/Gradient | 0 | 0 |
| Pyramid | Fill | `gradient` | Fill/Gradient | 0 | 624 |
| Pyramid | Fill | `gradientAngleDegrees` | Fill/Gradient | 2 | 606 |
| Pyramid | Fill | `gradientCentreX` | Fill/Gradient/Radial | 0 | 618 |
| Pyramid | Fill | `gradientCentreY` | Fill/Gradient/Radial | 0 | 618 |
| Pyramid | Fill | `gradientDepthPixels` | Fill/Gradient/ByEdgeDistance | 0 | 452 |
| Pyramid | Fill | `gradientMode` | Fill/Gradient | 0 | 619 |
| Pyramid | Fill | `gradientSize` | Fill/Gradient/Radial | 624 | 624 |
| Pyramid | Fill | `gradientTint` | Fill/Gradient | 0 | 0 |
| Pyramid | Fill | `gridHorizontal` | Fill/Procedural/Grid | 174 | 160 |
| Pyramid | Fill | `gridLineWidth` | Fill/Procedural/Grid | 450 | 340 |
| Pyramid | Fill | `gridVertical` | Fill/Procedural/Grid | 252 | 144 |
| Pyramid | Fill | `heightDelta` | Fill/Gradient | 0 | 0 |
| Pyramid | Fill | `heightFieldScale` | Fill/HeightField | 87 | 604 |
| Pyramid | Fill | `heightFieldTint` | Fill/HeightField | 624 | 624 |
| Pyramid | Fill | `kind` | Fill/Gradient | 624 | 624 |
| Pyramid | Fill | `noiseKind` | Fill/Procedural | 38 | 622 |
| Pyramid | Fill | `overPhaseGradient` | Fill/OverPhase | 624 | 624 |
| Pyramid | Fill | `overPhaseTint` | Fill/OverPhase | 0 | 0 |
| Pyramid | Fill | `proceduralAngleDegrees` | Fill/Procedural | 0 | 619 |
| Pyramid | Fill | `proceduralGradient` | Fill/Procedural | 0 | 624 |
| Pyramid | Fill | `proceduralKind` | Fill/Procedural | 1334 | 900 |
| Pyramid | Fill | `proceduralOffsetU` | Fill/Procedural | 0 | 623 |
| Pyramid | Fill | `proceduralOffsetV` | Fill/Procedural | 0 | 623 |
| Pyramid | Fill | `proceduralScale` | Fill/Procedural | 0 | 622 |
| Pyramid | Fill | `proceduralTint` | Fill/Procedural | 0 | 0 |
| Pyramid | Fill | `quantiseLevels` | Fill/Gradient | 0 | 624 |
| Pyramid | Fill | `rampGradient` | Fill/RampByQuantity | 0 | 0 |
| Pyramid | Fill | `rampInputHigh` | Fill/RampByQuantity | 624 | 624 |
| Pyramid | Fill | `rampInputLow` | Fill/RampByQuantity | 0 | 0 |
| Pyramid | Fill | `rampQuantity` | Fill/RampByQuantity | 624 | 624 |
| Pyramid | Fill | `rampTint` | Fill/RampByQuantity | 0 | 0 |
| Pyramid | Fill | `solidColor` | Fill/Solid | 624 | 624 |
| Pyramid | Fill | `space` | Fill/Gradient | 0 | 624 |
| Pyramid | Fill | `steelBaseHigh` | Fill/TapestrySteel | 624 | 624 |
| Pyramid | Fill | `steelBaseLow` | Fill/TapestrySteel | 624 | 624 |
| Pyramid | Fill | `steelCells` | Fill/TapestrySteel | 617 | 617 |
| Pyramid | Fill | `steelGrain` | Fill/TapestrySteel | 577 | 577 |
| Pyramid | Fill | `steelOctaves` | Fill/TapestrySteel | 603 | 603 |
| Pyramid | Fill | `steelRustAmount` | Fill/TapestrySteel | 565 | 565 |
| Pyramid | Fill | `steelRustColor` | Fill/TapestrySteel | 624 | 624 |
| Pyramid | Fill | `steelRustReachPixels` | Fill/TapestrySteel | 624 | 624 |
| Pyramid | Fill | `steelSeed` | Fill/TapestrySteel | 617 | 617 |
| Pyramid | Fill | `stripOffset` | Fill/IndexedStrip | 0 | 0 |
| Pyramid | Fill | `stripOrientationDegrees` | Fill/IndexedStrip | 66 | 66 |
| Pyramid | Fill | `stripParameterisation` | Fill/IndexedStrip | 312 | 312 |
| Pyramid | Fill | `stripPlainColor` | Fill/IndexedStrip | 0 | 0 |
| Pyramid | Fill | `stripReach` | Fill/IndexedStrip | 624 | 624 |
| Pyramid | Fill | `stripRepeats` | Fill/IndexedStrip | 316 | 316 |
| Pyramid | Fill | `stripSlots[0].color` | Fill/IndexedStrip | 228 | 228 |
| Pyramid | Fill | `stripSlots[0].height` | Fill/IndexedStrip | 0 | 0 |
| Pyramid | Fill | `stripSlots[1].color` | Fill/IndexedStrip | 396 | 396 |
| Pyramid | Fill | `stripSlots[1].height` | Fill/IndexedStrip | 0 | 0 |
| Pyramid | Fill | `textureAngleDegrees` | Fill/Texture | 207 | 207 |
| Pyramid | Fill | `textureAnimated` | Fill/Texture | 0 | 0 |
| Pyramid | Fill | `textureFrameColumns` | Fill/Texture | 0 | 0 |
| Pyramid | Fill | `textureFrameCount` | Fill/Texture | 0 | 0 |
| Pyramid | Fill | `textureFrameRows` | Fill/Texture | 0 | 0 |
| Pyramid | Fill | `textureMapping` | Fill/Texture | 318 | 318 |
| Pyramid | Fill | `textureOffsetU` | Fill/Texture | 312 | 312 |
| Pyramid | Fill | `textureOffsetV` | Fill/Texture | 268 | 268 |
| Pyramid | Fill | `textureTilesX` | Fill/Texture | 0 | 0 |
| Pyramid | Fill | `textureTilesY` | Fill/Texture | 0 | 0 |
| Pyramid | Fill | `textureTint` | Fill/Texture | 624 | 624 |
| Pyramid | Fill | `veil` | Fill/Gradient | 1584 | 900 |
| Star | Edge (border) | `alignment` | - | 622 | - |
| Star | Edge (border) | `composite` | Edge/fill/Gradient | 1080 | 608 |
| Star | Edge (border) | `dotSize` | Edge/fill/Procedural/Dots | 72 | 40 |
| Star | Edge (border) | `dotStagger` | Edge/fill/Procedural/Dots | 14 | 18 |
| Star | Edge (border) | `enabled` | - | 268 | - |
| Star | Edge (border) | `fit` | Edge/fill/Gradient | 0 | 0 |
| Star | Edge (border) | `gradient` | Edge/fill/Gradient | 0 | 608 |
| Star | Edge (border) | `gradientAngleDegrees` | Edge/fill/Gradient | 1 | 582 |
| Star | Edge (border) | `gradientCentreX` | Edge/fill/Gradient/Radial | 0 | 606 |
| Star | Edge (border) | `gradientCentreY` | Edge/fill/Gradient/Radial | 0 | 606 |
| Star | Edge (border) | `gradientDepthPixels` | Edge/fill/Gradient/ByEdgeDistance | 8 | 456 |
| Star | Edge (border) | `gradientMode` | Edge/fill/Gradient | 7 | 599 |
| Star | Edge (border) | `gradientSize` | Edge/fill/Gradient/Radial | 1076 | 608 |
| Star | Edge (border) | `gradientTint` | Edge/fill/Gradient | 0 | 0 |
| Star | Edge (border) | `gridHorizontal` | Edge/fill/Procedural/Grid | 26 | 12 |
| Star | Edge (border) | `gridLineWidth` | Edge/fill/Procedural/Grid | 66 | 42 |
| Star | Edge (border) | `gridVertical` | Edge/fill/Procedural/Grid | 30 | 20 |
| Star | Edge (border) | `heightDelta` | Edge/fill/Gradient | 0 | 0 |
| Star | Edge (border) | `heightFieldScale` | Edge/fill/HeightField | 0 | 0 |
| Star | Edge (border) | `heightFieldTint` | Edge/fill/HeightField | 1076 | 608 |
| Star | Edge (border) | `joinsCoverage` | - | 0 | - |
| Star | Edge (border) | `kind` | Edge/fill/Gradient | 1076 | 608 |
| Star | Edge (border) | `noiseKind` | Edge/fill/Procedural | 7 | 605 |
| Star | Edge (border) | `overPhaseGradient` | Edge/fill/OverPhase | 1070 | 608 |
| Star | Edge (border) | `overPhaseTint` | Edge/fill/OverPhase | 0 | 0 |
| Star | Edge (border) | `proceduralAngleDegrees` | Edge/fill/Procedural | 0 | 600 |
| Star | Edge (border) | `proceduralGradient` | Edge/fill/Procedural | 0 | 608 |
| Star | Edge (border) | `proceduralKind` | Edge/fill/Procedural | 1076 | 608 |
| Star | Edge (border) | `proceduralOffsetU` | Edge/fill/Procedural | 0 | 598 |
| Star | Edge (border) | `proceduralOffsetV` | Edge/fill/Procedural | 0 | 597 |
| Star | Edge (border) | `proceduralScale` | Edge/fill/Procedural | 0 | 603 |
| Star | Edge (border) | `proceduralTint` | Edge/fill/Procedural | 0 | 0 |
| Star | Edge (border) | `quantiseLevels` | Edge/fill/Gradient | 0 | 582 |
| Star | Edge (border) | `rampGradient` | Edge/fill/RampByQuantity | 2 | 264 |
| Star | Edge (border) | `rampInputHigh` | Edge/fill/RampByQuantity | 2 | 264 |
| Star | Edge (border) | `rampInputLow` | Edge/fill/RampByQuantity | 2 | 264 |
| Star | Edge (border) | `rampQuantity` | Edge/fill/RampByQuantity | 1076 | 608 |
| Star | Edge (border) | `rampTint` | Edge/fill/RampByQuantity | 0 | 0 |
| Star | Edge (border) | `solidColor` | Edge/fill/Solid | 1076 | 608 |
| Star | Edge (border) | `space` | Edge/fill/Gradient | 1 | 593 |
| Star | Edge (border) | `steelBaseHigh` | Edge/fill/TapestrySteel | 1073 | 608 |
| Star | Edge (border) | `steelBaseLow` | Edge/fill/TapestrySteel | 1069 | 606 |
| Star | Edge (border) | `steelCells` | Edge/fill/TapestrySteel | 1043 | 585 |
| Star | Edge (border) | `steelGrain` | Edge/fill/TapestrySteel | 928 | 523 |
| Star | Edge (border) | `steelOctaves` | Edge/fill/TapestrySteel | 997 | 565 |
| Star | Edge (border) | `steelRustAmount` | Edge/fill/TapestrySteel | 244 | 149 |
| Star | Edge (border) | `steelRustColor` | Edge/fill/TapestrySteel | 645 | 392 |
| Star | Edge (border) | `steelRustReachPixels` | Edge/fill/TapestrySteel | 822 | 456 |
| Star | Edge (border) | `steelSeed` | Edge/fill/TapestrySteel | 1021 | 574 |
| Star | Edge (border) | `stripOffset` | Edge/fill/IndexedStrip | 0 | 0 |
| Star | Edge (border) | `stripOrientationDegrees` | Edge/fill/IndexedStrip | 106 | 50 |
| Star | Edge (border) | `stripParameterisation` | Edge/fill/IndexedStrip | 535 | 304 |
| Star | Edge (border) | `stripPlainColor` | Edge/fill/IndexedStrip | 0 | 0 |
| Star | Edge (border) | `stripReach` | Edge/fill/IndexedStrip | 822 | 456 |
| Star | Edge (border) | `stripRepeats` | Edge/fill/IndexedStrip | 537 | 307 |
| Star | Edge (border) | `stripSlots[0].color` | Edge/fill/IndexedStrip | 596 | 336 |
| Star | Edge (border) | `stripSlots[0].height` | Edge/fill/IndexedStrip | 0 | 0 |
| Star | Edge (border) | `stripSlots[1].color` | Edge/fill/IndexedStrip | 480 | 272 |
| Star | Edge (border) | `stripSlots[1].height` | Edge/fill/IndexedStrip | 0 | 0 |
| Star | Edge (border) | `textureAngleDegrees` | Edge/fill/Texture | 337 | 205 |
| Star | Edge (border) | `textureAnimated` | Edge/fill/Texture | 0 | 0 |
| Star | Edge (border) | `textureFrameColumns` | Edge/fill/Texture | 0 | 0 |
| Star | Edge (border) | `textureFrameCount` | Edge/fill/Texture | 0 | 0 |
| Star | Edge (border) | `textureFrameRows` | Edge/fill/Texture | 0 | 0 |
| Star | Edge (border) | `textureMapping` | Edge/fill/Texture | 617 | 355 |
| Star | Edge (border) | `textureOffsetU` | Edge/fill/Texture | 538 | 304 |
| Star | Edge (border) | `textureOffsetV` | Edge/fill/Texture | 694 | 356 |
| Star | Edge (border) | `textureTilesX` | Edge/fill/Texture | 0 | 0 |
| Star | Edge (border) | `textureTilesY` | Edge/fill/Texture | 0 | 0 |
| Star | Edge (border) | `textureTint` | Edge/fill/Texture | 1076 | 608 |
| Star | Edge (border) | `veil` | Edge/fill/Gradient | 1076 | 608 |
| Star | Edge (border) | `width` | - | 268 | - |
| Star | Fill | `composite` | Fill/Gradient | 2752 | 864 |
| Star | Fill | `dotSize` | Fill/Procedural/Dots | 542 | 198 |
| Star | Fill | `dotStagger` | Fill/Procedural/Dots | 396 | 148 |
| Star | Fill | `fit` | Fill/Gradient | 0 | 0 |
| Star | Fill | `gradient` | Fill/Gradient | 0 | 864 |
| Star | Fill | `gradientAngleDegrees` | Fill/Gradient | 1 | 843 |
| Star | Fill | `gradientCentreX` | Fill/Gradient/Radial | 0 | 862 |
| Star | Fill | `gradientCentreY` | Fill/Gradient/Radial | 0 | 862 |
| Star | Fill | `gradientDepthPixels` | Fill/Gradient/ByEdgeDistance | 6 | 738 |
| Star | Fill | `gradientMode` | Fill/Gradient | 8 | 859 |
| Star | Fill | `gradientSize` | Fill/Gradient/Radial | 2752 | 864 |
| Star | Fill | `gradientTint` | Fill/Gradient | 0 | 0 |
| Star | Fill | `gridHorizontal` | Fill/Procedural/Grid | 370 | 96 |
| Star | Fill | `gridLineWidth` | Fill/Procedural/Grid | 754 | 240 |
| Star | Fill | `gridVertical` | Fill/Procedural/Grid | 346 | 122 |
| Star | Fill | `heightDelta` | Fill/Gradient | 0 | 0 |
| Star | Fill | `heightFieldScale` | Fill/HeightField | 0 | 0 |
| Star | Fill | `heightFieldTint` | Fill/HeightField | 2752 | 864 |
| Star | Fill | `kind` | Fill/Gradient | 2752 | 864 |
| Star | Fill | `noiseKind` | Fill/Procedural | 130 | 863 |
| Star | Fill | `overPhaseGradient` | Fill/OverPhase | 2752 | 864 |
| Star | Fill | `overPhaseTint` | Fill/OverPhase | 0 | 0 |
| Star | Fill | `proceduralAngleDegrees` | Fill/Procedural | 0 | 856 |
| Star | Fill | `proceduralGradient` | Fill/Procedural | 0 | 864 |
| Star | Fill | `proceduralKind` | Fill/Procedural | 2752 | 864 |
| Star | Fill | `proceduralOffsetU` | Fill/Procedural | 0 | 859 |
| Star | Fill | `proceduralOffsetV` | Fill/Procedural | 0 | 856 |
| Star | Fill | `proceduralScale` | Fill/Procedural | 0 | 858 |
| Star | Fill | `proceduralTint` | Fill/Procedural | 0 | 0 |
| Star | Fill | `quantiseLevels` | Fill/Gradient | 0 | 864 |
| Star | Fill | `rampGradient` | Fill/RampByQuantity | 2 | 176 |
| Star | Fill | `rampInputHigh` | Fill/RampByQuantity | 2 | 176 |
| Star | Fill | `rampInputLow` | Fill/RampByQuantity | 2 | 176 |
| Star | Fill | `rampQuantity` | Fill/RampByQuantity | 2752 | 864 |
| Star | Fill | `rampTint` | Fill/RampByQuantity | 0 | 0 |
| Star | Fill | `solidColor` | Fill/Solid | 2752 | 864 |
| Star | Fill | `space` | Fill/Gradient | 1 | 864 |
| Star | Fill | `steelBaseHigh` | Fill/TapestrySteel | 2752 | 864 |
| Star | Fill | `steelBaseLow` | Fill/TapestrySteel | 2752 | 864 |
| Star | Fill | `steelCells` | Fill/TapestrySteel | 2733 | 856 |
| Star | Fill | `steelGrain` | Fill/TapestrySteel | 2608 | 792 |
| Star | Fill | `steelOctaves` | Fill/TapestrySteel | 2668 | 841 |
| Star | Fill | `steelRustAmount` | Fill/TapestrySteel | 2203 | 531 |
| Star | Fill | `steelRustColor` | Fill/TapestrySteel | 2527 | 714 |
| Star | Fill | `steelRustReachPixels` | Fill/TapestrySteel | 2584 | 752 |
| Star | Fill | `steelSeed` | Fill/TapestrySteel | 2700 | 845 |
| Star | Fill | `stripOffset` | Fill/IndexedStrip | 0 | 0 |
| Star | Fill | `stripOrientationDegrees` | Fill/IndexedStrip | 246 | 70 |
| Star | Fill | `stripParameterisation` | Fill/IndexedStrip | 1376 | 432 |
| Star | Fill | `stripPlainColor` | Fill/IndexedStrip | 0 | 0 |
| Star | Fill | `stripReach` | Fill/IndexedStrip | 2584 | 752 |
| Star | Fill | `stripRepeats` | Fill/IndexedStrip | 1385 | 437 |
| Star | Fill | `stripSlots[0].color` | Fill/IndexedStrip | 1500 | 478 |
| Star | Fill | `stripSlots[0].height` | Fill/IndexedStrip | 0 | 0 |
| Star | Fill | `stripSlots[1].color` | Fill/IndexedStrip | 1252 | 386 |
| Star | Fill | `stripSlots[1].height` | Fill/IndexedStrip | 0 | 0 |
| Star | Fill | `textureAngleDegrees` | Fill/Texture | 697 | 250 |
| Star | Fill | `textureAnimated` | Fill/Texture | 0 | 0 |
| Star | Fill | `textureFrameColumns` | Fill/Texture | 0 | 0 |
| Star | Fill | `textureFrameCount` | Fill/Texture | 0 | 0 |
| Star | Fill | `textureFrameRows` | Fill/Texture | 0 | 0 |
| Star | Fill | `textureMapping` | Fill/Texture | 1477 | 444 |
| Star | Fill | `textureOffsetU` | Fill/Texture | 1376 | 432 |
| Star | Fill | `textureOffsetV` | Fill/Texture | 1592 | 498 |
| Star | Fill | `textureTilesX` | Fill/Texture | 0 | 0 |
| Star | Fill | `textureTilesY` | Fill/Texture | 0 | 0 |
| Star | Fill | `textureTint` | Fill/Texture | 2752 | 864 |
| Star | Fill | `veil` | Fill/Gradient | 2752 | 864 |
